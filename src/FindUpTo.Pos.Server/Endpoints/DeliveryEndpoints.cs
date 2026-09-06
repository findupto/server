using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FindUpTo.Pos.Server.Data;
using FindUpTo.Pos.Server.Hubs;
using FindUpTo.Pos.Server.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace FindUpTo.Pos.Server.Endpoints;

public static class DeliveryEndpoints
{
    private static readonly string[] ManagementRoles = ["Owner", "Manager", "Admin", "Counter"];

    public static void MapDeliveryEndpoints(this WebApplication app)
    {
        app.MapGet("/api/delivery/riders", async (CoreDbContext db) =>
        {
            var riders = await db.Riders.AsNoTracking().Join(db.Users.AsNoTracking(), r => r.UserId, u => u.Id, (r, u) => new { rider = r, username = u.Username }).ToListAsync();
            var ids = riders.Select(x => x.rider.Id).ToList();
            var locations = await db.RiderLocations.AsNoTracking().Where(x => ids.Contains(x.RiderId)).GroupBy(x => x.RiderId).Select(g => g.OrderByDescending(x => x.RecordedAtUtc).First()).ToListAsync();
            return Results.Ok(riders.Select(x => new { x.rider.Id, x.username, x.rider.VehicleType, x.rider.VehicleNumber, x.rider.Online, x.rider.Available, x.rider.LastSeenAtUtc, location = locations.SingleOrDefault(l => l.RiderId == x.rider.Id) }));
        }).RequireAuthorization(p => p.RequireRole(ManagementRoles));

        app.MapPost("/api/delivery/riders", async (RiderCreateRequest input, ClaimsPrincipal user, CoreDbContext db) =>
        {
            if (await db.Riders.AnyAsync(x => x.UserId == input.UserId)) return Results.Conflict("This user is already a rider.");
            var staff = await db.Users.FindAsync(input.UserId);
            if (staff is null || !staff.Active) return Results.NotFound("Staff user not found.");
            var rider = new RiderProfile { UserId = input.UserId, VehicleType = input.VehicleType.Trim(), VehicleNumber = input.VehicleNumber.Trim() };
            db.Riders.Add(rider); db.AuditLogs.Add(new AuditLog { Username = user.Identity?.Name ?? "unknown", Action = "CreateRider", EntityType = "Rider", Details = $"UserId={input.UserId}" }); await db.SaveChangesAsync();
            return Results.Ok(new { rider.Id, rider.UserId, rider.VehicleType, rider.VehicleNumber });
        }).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin"));

        app.MapPost("/api/delivery/riders/me/availability", async (RiderAvailabilityRequest input, ClaimsPrincipal user, CoreDbContext db, IHubContext<PosHub> hub) =>
        {
            var rider = await GetRiderAsync(db, user); if (rider is null) return Results.Forbid();
            rider.Online = input.Online; rider.Available = input.Available; rider.LastSeenAtUtc = DateTime.UtcNow; await db.SaveChangesAsync();
            await hub.Clients.All.SendAsync("rider.updated", new { riderId = rider.Id, online = rider.Online, available = rider.Available, lastSeenAtUtc = rider.LastSeenAtUtc });
            return Results.Ok(new { rider.Id, rider.Online, rider.Available });
        }).RequireAuthorization(p => p.RequireRole("Rider"));

        app.MapPost("/api/delivery/riders/me/location", async (RiderLocationRequest input, ClaimsPrincipal user, CoreDbContext db, IHubContext<PosHub> hub) =>
        {
            if (input.Latitude is < -90 or > 90 || input.Longitude is < -180 or > 180) return Results.BadRequest("Invalid GPS coordinates.");
            var rider = await GetRiderAsync(db, user); if (rider is null) return Results.Forbid();
            rider.Online = true; rider.LastSeenAtUtc = DateTime.UtcNow;
            var location = new RiderLocation { RiderId = rider.Id, Latitude = input.Latitude, Longitude = input.Longitude, AccuracyMeters = input.AccuracyMeters, SpeedMetersPerSecond = input.SpeedMetersPerSecond, HeadingDegrees = input.HeadingDegrees };
            db.RiderLocations.Add(location); await db.SaveChangesAsync();
            var deliveries = await db.DeliveryTrackings.AsNoTracking().Where(x => x.RiderId == rider.Id && x.Status != "Delivered" && x.Status != "Cancelled").Select(x => x.TrackingCode).ToListAsync();
            foreach (var code in deliveries) await hub.Clients.Group($"tracking:{code}").SendAsync("location.updated", new { trackingCode = code, riderId = rider.Id, latitude = location.Latitude, longitude = location.Longitude, accuracyMeters = location.AccuracyMeters, speedMetersPerSecond = location.SpeedMetersPerSecond, headingDegrees = location.HeadingDegrees, recordedAtUtc = location.RecordedAtUtc });
            await hub.Clients.All.SendAsync("rider.location", new { riderId = rider.Id, location.Latitude, location.Longitude, location.AccuracyMeters, location.SpeedMetersPerSecond, location.HeadingDegrees, location.RecordedAtUtc });
            return Results.Ok(location);
        }).RequireAuthorization(p => p.RequireRole("Rider"));

        app.MapPost("/api/delivery/orders/{orderId:int}/assign-rider", async (int orderId, AssignRiderRequest input, ClaimsPrincipal user, CoreDbContext db, IHubContext<PosHub> hub) =>
        {
            var tracking = await db.DeliveryTrackings.SingleOrDefaultAsync(x => x.OrderId == orderId); if (tracking is null) return Results.NotFound("Delivery tracking is not enabled for this order.");
            var rider = await db.Riders.FindAsync(input.RiderId); if (rider is null || !rider.Available) return Results.BadRequest("Rider is unavailable.");
            tracking.RiderId = rider.Id; tracking.Status = "Assigned"; tracking.UpdatedAtUtc = DateTime.UtcNow; await db.SaveChangesAsync();
            await hub.Clients.All.SendAsync("delivery.updated", new { orderId, trackingCode = tracking.TrackingCode, riderId = rider.Id, status = tracking.Status, updatedAtUtc = tracking.UpdatedAtUtc });
            return Results.Ok(await BuildTrackingAsync(db, tracking));
        }).RequireAuthorization(p => p.RequireRole(ManagementRoles));

        app.MapPost("/api/delivery/orders/{orderId:int}/tracking", async (int orderId, DeliveryTrackingRequest input, ClaimsPrincipal user, CoreDbContext db, IHubContext<PosHub> hub) =>
        {
            var order = await db.Orders.FindAsync(orderId); if (order is null) return Results.NotFound();
            var tracking = await db.DeliveryTrackings.SingleOrDefaultAsync(x => x.OrderId == orderId);
            if (tracking is null) { tracking = new DeliveryTracking { OrderId = orderId, TrackingCode = CreateTrackingCode(), Status = input.Status.Trim() }; db.DeliveryTrackings.Add(tracking); }
            tracking.Status = string.IsNullOrWhiteSpace(input.Status) ? tracking.Status : input.Status.Trim(); tracking.DeliveryAddress = input.DeliveryAddress.Trim(); tracking.DestinationLatitude = input.DestinationLatitude; tracking.DestinationLongitude = input.DestinationLongitude; tracking.UpdatedAtUtc = DateTime.UtcNow; await db.SaveChangesAsync();
            await hub.Clients.All.SendAsync("delivery.updated", new { orderId, trackingCode = tracking.TrackingCode, status = tracking.Status, updatedAtUtc = tracking.UpdatedAtUtc });
            return Results.Ok(await BuildTrackingAsync(db, tracking));
        }).RequireAuthorization(p => p.RequireRole(ManagementRoles));

        app.MapGet("/api/delivery/track/{trackingCode}", async (string trackingCode, CoreDbContext db) =>
        {
            var tracking = await db.DeliveryTrackings.AsNoTracking().SingleOrDefaultAsync(x => x.TrackingCode == trackingCode.Trim());
            return tracking is null ? Results.NotFound() : Results.Ok(await BuildTrackingAsync(db, tracking));
        }).AllowAnonymous();
    }

    private static async Task<RiderProfile?> GetRiderAsync(CoreDbContext db, ClaimsPrincipal user)
    {
        if (!int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub"), out var userId)) return null;
        return await db.Riders.SingleOrDefaultAsync(x => x.UserId == userId);
    }

    private static async Task<object> BuildTrackingAsync(CoreDbContext db, DeliveryTracking tracking)
    {
        var rider = tracking.RiderId.HasValue ? await db.Riders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == tracking.RiderId.Value) : null;
        var location = tracking.RiderId.HasValue ? await db.RiderLocations.AsNoTracking().Where(x => x.RiderId == tracking.RiderId.Value).OrderByDescending(x => x.RecordedAtUtc).FirstOrDefaultAsync() : null;
        var username = rider is null ? null : await db.Users.AsNoTracking().Where(x => x.Id == rider.UserId).Select(x => x.Username).SingleOrDefaultAsync();
        return new { tracking.OrderId, tracking.TrackingCode, tracking.Status, tracking.DeliveryAddress, tracking.DestinationLatitude, tracking.DestinationLongitude, tracking.UpdatedAtUtc, rider = rider is null ? null : new { rider.Id, username, rider.VehicleType, rider.VehicleNumber, rider.Online, rider.LastSeenAtUtc }, location };
    }

    private static string CreateTrackingCode() => $"FU{Convert.ToHexString(RandomNumberGenerator.GetBytes(6))}";
}
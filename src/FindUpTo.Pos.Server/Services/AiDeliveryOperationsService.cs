using FindUpTo.Pos.Server.Data;
using FindUpTo.Pos.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace FindUpTo.Pos.Server.Services;

public sealed class AiDeliveryOperationsService(CoreDbContext db)
{
    public async Task<object> RecommendRiderAsync(int orderId, CancellationToken cancellationToken)
    {
        var tracking = await db.DeliveryTrackings.AsNoTracking().SingleOrDefaultAsync(x => x.OrderId == orderId, cancellationToken)
            ?? throw new InvalidOperationException("Delivery tracking is not enabled for this order.");
        if (!tracking.DestinationLatitude.HasValue || !tracking.DestinationLongitude.HasValue)
            throw new InvalidOperationException("Delivery destination GPS coordinates are required for automatic rider selection.");

        var riders = await db.Riders.AsNoTracking().Where(x => x.Online && x.Available).ToListAsync(cancellationToken);
        if (riders.Count == 0) throw new InvalidOperationException("No online available riders were found.");

        var riderIds = riders.Select(x => x.Id).ToList();
        var latest = await db.RiderLocations.AsNoTracking()
            .Where(x => riderIds.Contains(x.RiderId))
            .ToListAsync(cancellationToken);

        var candidates = riders.Select(r =>
        {
            var location = latest.Where(x => x.RiderId == r.Id).OrderByDescending(x => x.RecordedAtUtc).FirstOrDefault();
            var distance = location is null ? double.MaxValue : HaversineMeters(location.Latitude, location.Longitude, tracking.DestinationLatitude.Value, tracking.DestinationLongitude.Value);
            var freshnessSeconds = location is null ? double.MaxValue : Math.Max(0, (DateTime.UtcNow - location.RecordedAtUtc).TotalSeconds);
            var freshnessPenalty = freshnessSeconds > 300 ? 100000d : freshnessSeconds * 20d;
            return new { rider = r, location, score = distance + freshnessPenalty, distanceMeters = distance, freshnessSeconds };
        })
        .Where(x => x.location is not null)
        .OrderBy(x => x.score)
        .Take(5)
        .Select(x => new
        {
            riderId = x.rider.Id,
            vehicleType = x.rider.VehicleType,
            vehicleNumber = x.rider.VehicleNumber,
            latitude = x.location!.Latitude,
            longitude = x.location.Longitude,
            distanceMeters = Math.Round(x.distanceMeters, 1),
            locationAgeSeconds = Math.Round(x.freshnessSeconds, 1),
            score = Math.Round(x.score, 1)
        }).ToList();

        if (candidates.Count == 0) throw new InvalidOperationException("No rider has a usable recent GPS location.");
        return new { orderId, trackingCode = tracking.TrackingCode, destinationLatitude = tracking.DestinationLatitude, destinationLongitude = tracking.DestinationLongitude, recommended = candidates[0], alternatives = candidates.Skip(1) };
    }

    public async Task<object> AssignRecommendedRiderAsync(int orderId, string username, CancellationToken cancellationToken)
    {
        var recommendation = await RecommendRiderAsync(orderId, cancellationToken);
        var riderId = recommendation.GetType().GetProperty("recommended")?.GetValue(recommendation)?.GetType().GetProperty("riderId")?.GetValue(recommendation) as int?;
        if (!riderId.HasValue) throw new InvalidOperationException("Unable to determine the recommended rider.");
        var tracking = await db.DeliveryTrackings.SingleOrDefaultAsync(x => x.OrderId == orderId, cancellationToken) ?? throw new InvalidOperationException("Delivery tracking is not enabled for this order.");
        var rider = await db.Riders.SingleOrDefaultAsync(x => x.Id == riderId.Value && x.Online && x.Available, cancellationToken) ?? throw new InvalidOperationException("Recommended rider is no longer available.");
        tracking.RiderId = rider.Id;
        tracking.Status = "Assigned";
        tracking.UpdatedAtUtc = DateTime.UtcNow;
        rider.Available = false;
        db.AuditLogs.Add(new AuditLog { Username = username, Action = "AIAssignedRider", EntityType = "Delivery", EntityId = orderId.ToString(), Details = $"RiderId={rider.Id};TrackingCode={tracking.TrackingCode}" });
        await db.SaveChangesAsync(cancellationToken);
        return new { success = true, orderId, trackingCode = tracking.TrackingCode, riderId = rider.Id, status = tracking.Status, assignedAtUtc = tracking.UpdatedAtUtc };
    }

    private static double HaversineMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double radius = 6371000;
        var dLat = Degrees(lat2 - lat1);
        var dLon = Degrees(lon2 - lon1);
        var a = Math.Pow(Math.Sin(dLat / 2), 2) + Math.Cos(Degrees(lat1)) * Math.Cos(Degrees(lat2)) * Math.Pow(Math.Sin(dLon / 2), 2);
        return radius * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double Degrees(double value) => value * Math.PI / 180d;
}

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FindUpTo.Pos.Server.Data;
using FindUpTo.Pos.Server.Hubs;
using FindUpTo.Pos.Server.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace FindUpTo.Pos.Server.Endpoints;

public static class DeviceEndpoints
{
    public static void MapDeviceEndpoints(this WebApplication app)
    {
        app.MapPost("/api/relay/devices", async (CreateRelayDeviceRequest input, ClaimsPrincipal user, CoreDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(input.Name)) return Results.BadRequest("Device name is required.");
            var name = input.Name.Trim(); if (name.Length > 120) return Results.BadRequest("Device name is too long.");
            if (await db.RelayDevices.AnyAsync(x => x.Name == name)) return Results.Conflict("A device with that name already exists.");
            var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+','-').Replace('/','_');
            var device = new RelayDevice { Name = name, SecretHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))) };
            db.RelayDevices.Add(device); await db.SaveChangesAsync(); await AuditEndpoints.WriteAsync(db, user, "Created", "RelayDevice", device.Id.ToString(), device.Name);
            return Results.Ok(new { deviceId = device.Id, device.Name, secret, warning = "Store this secret securely. It is shown only once." });
        }).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin"));

        app.MapGet("/api/relay/devices", async (CoreDbContext db) => Results.Ok(await db.RelayDevices.AsNoTracking().OrderBy(x => x.Name).Select(x => new { x.Id, x.Name, x.Active, x.CreatedAtUtc, x.LastSeenAtUtc }).ToListAsync())).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin"));
        app.MapPatch("/api/relay/devices/{id:int}", async (int id, bool active, ClaimsPrincipal user, CoreDbContext db) => { var device = await db.RelayDevices.FindAsync(id); if (device is null) return Results.NotFound(); device.Active = active; await db.SaveChangesAsync(); await AuditEndpoints.WriteAsync(db, active ? "Activated" : "Deactivated", "RelayDevice", id.ToString(), device.Name); return Results.Ok(new { device.Id, device.Name, device.Active }); }).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin"));
        app.MapPost("/api/relay/auth", async (RelayDeviceLoginRequest input, CoreDbContext db, IConfiguration configuration) => { var device = await db.RelayDevices.SingleOrDefaultAsync(x => x.Id == input.DeviceId && x.Active); if (device is null) return Results.Unauthorized(); var supplied = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input.Secret ?? ""))); if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(device.SecretHash))) return Results.Unauthorized(); device.LastSeenAtUtc = DateTime.UtcNow; await db.SaveChangesAsync(); var key = configuration["Jwt:Key"] ?? Environment.GetEnvironmentVariable("POS_JWT_KEY"); if (string.IsNullOrWhiteSpace(key) || key.Length < 32) return Results.Problem("JWT signing key is not configured.", statusCode: 500); var claims = new[] { new Claim(JwtRegisteredClaimNames.Sub, $"device:{device.Id}"), new Claim(ClaimTypes.Name, device.Name), new Claim(ClaimTypes.Role, "Device"), new Claim("device_id", device.Id.ToString()) }; var expires = DateTime.UtcNow.AddHours(2); var token = new JwtSecurityToken(claims: claims, expires: expires, signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256)); return Results.Ok(new { token = new JwtSecurityTokenHandler().WriteToken(token), expiresAtUtc = expires, deviceId = device.Id, device.Name }); }).AllowAnonymous();
        app.MapPost("/api/relay/send", async (RelayMessageRequest input, ClaimsPrincipal user, CoreDbContext db, IHubContext<PosHub> hub) => { if (string.IsNullOrWhiteSpace(input.Type) || input.Type.Length > 64 || string.IsNullOrWhiteSpace(input.Payload) || input.Payload.Length > 32768) return Results.BadRequest("Invalid relay message."); var target = await db.RelayDevices.AsNoTracking().SingleOrDefaultAsync(x => x.Id == input.TargetDeviceId && x.Active); if (target is null) return Results.NotFound("Target device not found or inactive."); var sender = user.FindFirstValue("device_id") ?? user.Identity?.Name ?? "unknown"; await hub.Clients.Group($"device:{target.Id}").SendAsync("relay.message", new { targetDeviceId = target.Id, from = sender, type = input.Type, payload = input.Payload, sentAtUtc = DateTime.UtcNow }); return Results.Ok(new { delivered = true, targetDeviceId = target.Id }); }).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin", "Counter", "Device"));
        app.MapNotificationEndpoints();
    }
}

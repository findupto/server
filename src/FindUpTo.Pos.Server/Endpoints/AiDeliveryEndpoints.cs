using System.Security.Claims;
using FindUpTo.Pos.Server.Hubs;
using FindUpTo.Pos.Server.Services;
using Microsoft.AspNetCore.SignalR;

namespace FindUpTo.Pos.Server.Endpoints;

public static class AiDeliveryEndpoints
{
    public static void MapAiDeliveryEndpoints(this WebApplication app)
    {
        app.MapGet("/api/ai/delivery/orders/{orderId:int}/recommend-rider", async (int orderId, AiDeliveryOperationsService service, CancellationToken cancellationToken) =>
        {
            try { return Results.Ok(await service.RecommendRiderAsync(orderId, cancellationToken)); }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { message = ex.Message }); }
        }).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin", "Counter"));

        app.MapPost("/api/ai/delivery/orders/{orderId:int}/assign-rider", async (int orderId, ClaimsPrincipal user, AiDeliveryOperationsService service, IHubContext<PosHub> hub, CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await service.AssignRecommendedRiderAsync(orderId, user.Identity?.Name ?? "unknown", cancellationToken);
                await hub.Clients.All.SendAsync("delivery.updated", result, cancellationToken);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { message = ex.Message }); }
        }).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin"));
    }
}

using FindUpTo.Pos.Server.Services;

namespace FindUpTo.Pos.Server.Endpoints;

public static class PremiumEndpoints
{
    public static void MapPremiumEndpoints(this WebApplication app)
    {
        var roles = new[] { "Owner", "Manager", "Admin" };
        app.MapGet("/api/premium/executive", async (PremiumIntelligenceService intelligence, int? days, CancellationToken ct) =>
            Results.Ok(await intelligence.GetExecutiveDashboardAsync(Math.Clamp(days ?? 30, 7, 365), ct)))
            .RequireAuthorization(p => p.RequireRole(roles));

        app.MapGet("/api/premium/menu-engineering", async (PremiumIntelligenceService intelligence, int? days, CancellationToken ct) =>
            Results.Ok(await intelligence.GetMenuEngineeringAsync(Math.Clamp(days ?? 30, 7, 365), ct)))
            .RequireAuthorization(p => p.RequireRole(roles));

        app.MapGet("/api/premium/customers/segments", async (PremiumIntelligenceService intelligence, int? days, CancellationToken ct) =>
            Results.Ok(await intelligence.GetCustomerSegmentsAsync(Math.Clamp(days ?? 90, 30, 730), ct)))
            .RequireAuthorization(p => p.RequireRole(roles));

        app.MapGet("/api/premium/sales-forecast", async (PremiumIntelligenceService intelligence, int? historyDays, int? horizonDays, CancellationToken ct) =>
            Results.Ok(await intelligence.GetSalesForecastAsync(Math.Clamp(historyDays ?? 30, 14, 365), Math.Clamp(horizonDays ?? 14, 1, 90), ct)))
            .RequireAuthorization(p => p.RequireRole(roles));

        app.MapGet("/api/premium/data-quality", async (PremiumIntelligenceService intelligence, CancellationToken ct) =>
            Results.Ok(await intelligence.GetDataQualityAsync(ct)))
            .RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin"));

        app.MapGet("/api/premium/smart-alerts", async (PremiumIntelligenceService intelligence, int? days, CancellationToken ct) =>
            Results.Ok(await intelligence.GetSmartAlertsAsync(Math.Clamp(days ?? 30, 7, 365), ct)))
            .RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin", "Counter", "Kitchen"));
    }
}

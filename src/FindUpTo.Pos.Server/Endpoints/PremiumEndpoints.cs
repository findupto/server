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

        app.MapGet("/api/premium/replenishment", async (AdvancedPremiumIntelligenceService intelligence, int? historyDays, int? targetDays, CancellationToken ct) =>
            Results.Ok(await intelligence.GetReplenishmentPlanAsync(Math.Clamp(historyDays ?? 30, 7, 365), Math.Clamp(targetDays ?? 14, 3, 60), ct)))
            .RequireAuthorization(p => p.RequireRole(roles));

        app.MapGet("/api/premium/profitability", async (AdvancedPremiumIntelligenceService intelligence, int? days, CancellationToken ct) =>
            Results.Ok(await intelligence.GetProfitabilityAsync(Math.Clamp(days ?? 30, 7, 365), ct)))
            .RequireAuthorization(p => p.RequireRole(roles));

        app.MapGet("/api/premium/retention-opportunities", async (AdvancedPremiumIntelligenceService intelligence, int? days, CancellationToken ct) =>
            Results.Ok(await intelligence.GetRetentionOpportunitiesAsync(Math.Clamp(days ?? 180, 30, 730), ct)))
            .RequireAuthorization(p => p.RequireRole(roles));

        app.MapGet("/api/premium/ai-brief", async (AdvancedPremiumIntelligenceService intelligence, int? days, CancellationToken ct) =>
            Results.Ok(await intelligence.GenerateExecutiveAiBriefAsync(Math.Clamp(days ?? 30, 7, 365), ct)))
            .RequireAuthorization(p => p.RequireRole(roles));
    }
}

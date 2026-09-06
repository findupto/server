using System.Security.Claims;
using FindUpTo.Pos.Server.Data;
using FindUpTo.Pos.Server.Services;
using Microsoft.EntityFrameworkCore;

namespace FindUpTo.Pos.Server.Endpoints;

public static class AiEndpoints
{
    private static readonly string[] AllowedRoles = ["Owner", "Manager", "Admin", "Counter", "Kitchen"];

    public static void MapAiEndpoints(this WebApplication app)
    {
        app.MapPost("/api/ai/operate", async (AiOperateRequest input, ClaimsPrincipal user, BusinessAiService ai, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(input.Message)) return Results.BadRequest(new { message = "message is required." });
            if (input.Message.Length > 4000) return Results.BadRequest(new { message = "message is limited to 4000 characters." });
            try { return Results.Ok(await ai.RunAsync(user, input.Message, cancellationToken)); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
            catch (ArgumentException ex) { return Results.BadRequest(new { message = ex.Message }); }
            catch (InvalidOperationException ex) { return Results.Problem(ex.Message, statusCode: 503); }
        }).RequireAuthorization(p => p.RequireRole(AllowedRoles));

        app.MapGet("/api/ai/status", async (AiProviderService providers, CancellationToken cancellationToken) =>
        {
            var provider = await providers.ResolveAsync(cancellationToken);
            return Results.Ok(new { enabled = provider.Provider != "none", provider = provider.Provider, baseUrl = provider.BaseUrl, model = provider.Model, requiresApiKey = provider.RequiresApiKey, local = provider.Local, status = provider.Status,
                capabilities = new[] { "sales", "payments", "inventory", "inventory-forecast", "reorder-recommendations", "purchasing-intelligence", "purchasing-automation", "supplier-intelligence", "receiving", "product-updates", "reports", "receipt-printing", "rider-tracking", "delivery-tracking", "routing-eta", "customer-crm", "customer-retention", "customer-churn", "finance-cashflow", "kitchen-kds", "pricing-optimization", "promotion-intelligence", "anomaly-detection", "fraud-alerts", "operational-alerts", "workforce-intelligence", "staff-performance", "staff-anomalies" } });
        }).RequireAuthorization(p => p.RequireRole(AllowedRoles));

        app.MapGet("/api/ai/customers/retention", async (CoreDbContext db, int? days, CancellationToken cancellationToken) =>
        {
            var lookback = Math.Clamp(days ?? 90, 30, 730); var now = DateTime.UtcNow; var fromDate = now.AddDays(-lookback);
            var customers = await db.Customers.AsNoTracking().ToListAsync(cancellationToken);
            var orders = await db.Orders.AsNoTracking().Where(x => x.CustomerId.HasValue && x.CreatedAtUtc >= fromDate && x.Status != "Cancelled").ToListAsync(cancellationToken);
            var result = customers.Select(c =>
            {
                var customerOrders = orders.Where(x => x.CustomerId == c.Id).OrderByDescending(x => x.CreatedAtUtc).ToList();
                var last = customerOrders.FirstOrDefault()?.CreatedAtUtc;
                var recency = last.HasValue ? Math.Max(0, (int)(now - last.Value).TotalDays) : lookback;
                var frequency = customerOrders.Count; var value = customerOrders.Sum(x => x.Total);
                var score = frequency == 0 ? 100 : Math.Clamp((decimal)recency * .7m - frequency * 2m - value / 100m, 0m, 100m);
                var risk = frequency == 0 ? "Inactive" : score >= 70 ? "Critical" : score >= 45 ? "High" : score >= 25 ? "Medium" : "Low";
                return new { customerId = c.Id, customerName = c.Name, phone = c.Phone, orders = frequency, spend = Math.Round(value, 2), lastOrderAtUtc = last, daysSinceLastOrder = recency, churnRiskScore = Math.Round(score, 1), churnRisk = risk,
                    recommendedAction = risk switch { "Critical" => "WinBack", "High" => "PersonalOffer", "Medium" => "Engage", _ => "Retain" } };
            }).OrderByDescending(x => x.churnRiskScore).ThenByDescending(x => x.spend).ToList();
            return Results.Ok(new { generatedAtUtc = now, lookbackDays = lookback, customers = result });
        }).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin"));

        app.MapGet("/api/ai/customers/summary", async (CoreDbContext db, CancellationToken cancellationToken) =>
        {
            var now = DateTime.UtcNow; var fromDate = now.AddDays(-90);
            var orders = await db.Orders.AsNoTracking().Where(x => x.CustomerId.HasValue && x.CreatedAtUtc >= fromDate && x.Status != "Cancelled").ToListAsync(cancellationToken);
            var active = orders.Select(x => x.CustomerId!.Value).Distinct().Count(); var repeat = orders.GroupBy(x => x.CustomerId!.Value).Count(g => g.Count() >= 2);
            var atRisk = orders.GroupBy(x => x.CustomerId!.Value).Count(g => (now - g.Max(x => x.CreatedAtUtc)).TotalDays >= 45);
            return Results.Ok(new { generatedAtUtc = now, lookbackDays = 90, activeCustomers = active, repeatCustomers = repeat, repeatRate = active == 0 ? 0m : Math.Round(repeat / (decimal)active, 3), customersAtRisk = atRisk });
        }).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin", "Counter"));

        app.MapGet("/api/ai/pricing/recommendations", async (PricingAiService pricing, int? days, CancellationToken cancellationToken) => Results.Ok(await pricing.GetRecommendationsAsync(Math.Clamp(days ?? 30, 7, 365), cancellationToken))).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin"));
        app.MapGet("/api/ai/pricing/promotions", async (PricingAiService pricing, int? days, CancellationToken cancellationToken) => Results.Ok(await pricing.GetPromotionInsightsAsync(Math.Clamp(days ?? 30, 7, 365), cancellationToken))).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin"));
        app.MapGet("/api/ai/anomalies", async (AnomalyAiService anomaly, int? days, CancellationToken cancellationToken) => Results.Ok(await anomaly.AnalyzeAsync(Math.Clamp(days ?? 30, 7, 365), cancellationToken))).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin"));
        app.MapGet("/api/ai/workforce", async (WorkforceAiService workforce, int? days, CancellationToken cancellationToken) => Results.Ok(await workforce.AnalyzeAsync(Math.Clamp(days ?? 30, 7, 365), cancellationToken))).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin"));
        app.MapGet("/api/ai/inventory/forecast", async (CoreDbContext db, int? days, CancellationToken cancellationToken) => Results.Ok(await GetInventoryForecastAsync(db, Math.Clamp(days ?? 30, 7, 365), cancellationToken))).RequireAuthorization(p => p.RequireRole(AllowedRoles));
        app.MapGet("/api/ai/inventory/reorders", async (CoreDbContext db, CancellationToken cancellationToken) => Results.Ok(await GetReordersAsync(db, cancellationToken))).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin"));
        app.MapGet("/api/ai/purchasing/suppliers", async (CoreDbContext db, CancellationToken cancellationToken) => Results.Ok(await GetSupplierInsightsAsync(db, cancellationToken))).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin"));
        app.MapGet("/api/ai/purchasing/recommendations", async (CoreDbContext db, CancellationToken cancellationToken) => Results.Ok(await GetPurchaseRecommendationsAsync(db, cancellationToken))).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin"));
        app.MapGet("/api/ai/kitchen/queue", async (int? limit, CoreDbContext db, CancellationToken cancellationToken) => Results.Ok(await new KitchenAiService(db).GetQueueAsync(Math.Clamp(limit ?? 100, 1, 500), cancellationToken))).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin", "Kitchen"));
        app.MapGet("/api/ai/kitchen/recommend-next", async (CoreDbContext db, CancellationToken cancellationToken) => Results.Ok(await new KitchenAiService(db).RecommendNextAsync(cancellationToken))).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin", "Kitchen"));
        app.MapGet("/api/ai/providers/discover", async (AiProviderService providers, CancellationToken cancellationToken) => Results.Ok(await providers.DiscoverAsync(cancellationToken))).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin"));
        app.MapPut("/api/ai/configuration", async (AiConfigurationRequest input, ClaimsPrincipal user, AiProviderService providers, CancellationToken cancellationToken) =>
        {
            if (user.FindFirstValue(ClaimTypes.Role) != "Owner") return Results.Forbid();
            var provider = input.Provider.Trim().ToLowerInvariant(); var allowed = new[] { "auto", "openai", "ollama", "lmstudio", "llamacpp" };
            if (!allowed.Contains(provider)) return Results.BadRequest("Unsupported AI provider.");
            if (provider == "openai" && string.IsNullOrWhiteSpace(input.ApiKey)) return Results.BadRequest("API key is required for a purchased OpenAI model.");
            if (provider is "ollama" or "lmstudio" or "llamacpp" && string.IsNullOrWhiteSpace(input.Model)) return Results.BadRequest("A local model name is required.");
            await providers.ConfigureAsync(provider, input.Model ?? "", input.ApiKey, input.BaseUrl, cancellationToken);
            return Results.Ok(new { success = true, message = "AI configuration saved. API keys are never returned by this endpoint." });
        }).RequireAuthorization(p => p.RequireRole("Owner"));
    }

    private static async Task<object> GetInventoryForecastAsync(CoreDbContext db, int horizon, CancellationToken ct)
    {
        var now = DateTime.UtcNow; var fromDate = now.AddDays(-Math.Max(30, horizon));
        var products = await db.Products.AsNoTracking().Where(x => x.Available).ToListAsync(ct);
        var inventory = await db.ProductInventories.AsNoTracking().ToDictionaryAsync(x => x.ProductId, ct);
        var sales = await (from item in db.OrderItems.AsNoTracking()
                           join order in db.Orders.AsNoTracking() on item.PosOrderId equals order.Id
                           where order.CreatedAtUtc >= fromDate && order.Status != "Cancelled"
                           group item by item.ProductId into g
                           select new { productId = g.Key, units = g.Sum(x => x.Quantity) }).ToDictionaryAsync(x => x.productId, x => x.units, ct);
        var items = products.Select(p =>
        {
            inventory.TryGetValue(p.Id, out var s); var units = sales.GetValueOrDefault(p.Id); var daily = units / (decimal)Math.Max(30, horizon); var forecast = Math.Ceiling(daily * horizon);
            var onHand = s?.QuantityOnHand ?? 0m; var reorder = s?.ReorderLevel ?? 0m;
            return new { productId = p.Id, productName = p.Name, quantityOnHand = onHand, reorderLevel = reorder, unitsSold = units, dailySalesRate = Math.Round(daily, 3), forecastUnits = forecast,
                daysRemaining = daily > 0 ? Math.Round(onHand / daily, 1) : (decimal?)null, recommendedOrderQuantity = Math.Max(0m, Math.Ceiling(Math.Max(reorder, forecast) - onHand)) };
        }).OrderByDescending(x => x.recommendedOrderQuantity).ToList();
        return new { generatedAtUtc = now, horizonDays = horizon, items };
    }

    private static async Task<object> GetReordersAsync(CoreDbContext db, CancellationToken ct) => new
    {
        generatedAtUtc = DateTime.UtcNow,
        recommendations = await db.ProductInventories.AsNoTracking().Where(x => x.TrackInventory && x.QuantityOnHand <= x.ReorderLevel)
            .Select(x => new { productId = x.ProductId, quantityOnHand = x.QuantityOnHand, reorderLevel = x.ReorderLevel, averageCost = x.AverageCost,
                recommendedOrderQuantity = Math.Max(1m, x.ReorderLevel * 2m - x.QuantityOnHand), estimatedCost = Math.Round(Math.Max(1m, x.ReorderLevel * 2m - x.QuantityOnHand) * x.AverageCost, 2) })
            .OrderByDescending(x => x.estimatedCost).ToListAsync(ct)
    };

    private static async Task<object> GetSupplierInsightsAsync(CoreDbContext db, CancellationToken ct)
    {
        var suppliers = await db.Suppliers.AsNoTracking().Where(x => x.Active).ToListAsync(ct);
        var orders = await db.PurchaseOrders.AsNoTracking().Where(x => x.Status != "Cancelled").Include(x => x.Items).ToListAsync(ct);
        return new { generatedAtUtc = DateTime.UtcNow, suppliers = suppliers.Select(s =>
        {
            var items = orders.Where(o => o.SupplierId == s.Id).SelectMany(o => o.Items).ToList(); var received = items.Sum(i => i.QuantityReceived); var ordered = items.Sum(i => i.QuantityOrdered);
            return new { supplierId = s.Id, supplierName = s.Name, orderCount = orders.Count(o => o.SupplierId == s.Id), fillRate = ordered > 0 ? Math.Round(received / ordered, 3) : 0m,
                averageReceivedUnitCost = received > 0 ? Math.Round(items.Sum(i => i.QuantityReceived * i.UnitCost) / received, 2) : 0m };
        }).ToList() };
    }

    private static async Task<object> GetPurchaseRecommendationsAsync(CoreDbContext db, CancellationToken ct)
    {
        var inventory = await db.ProductInventories.AsNoTracking().Where(x => x.TrackInventory && x.QuantityOnHand <= x.ReorderLevel).ToListAsync(ct);
        var products = await db.Products.AsNoTracking().ToDictionaryAsync(x => x.Id, ct);
        return new { generatedAtUtc = DateTime.UtcNow, recommendations = inventory.Select(x =>
        {
            var productName = products.TryGetValue(x.ProductId, out var p) ? p.Name : $"Product #{x.ProductId}";
            var quantity = Math.Max(1m, x.ReorderLevel * 2m - x.QuantityOnHand);
            return new { productId = x.ProductId, productName, quantityOnHand = x.QuantityOnHand, reorderLevel = x.ReorderLevel,
                recommendedOrderQuantity = quantity, estimatedCost = Math.Round(quantity * x.AverageCost, 2) };
        }).OrderByDescending(x => x.estimatedCost).ToList() };
    }

    public sealed record AiOperateRequest(string Message);
    public sealed record AiConfigurationRequest(string Provider, string? Model = null, string? ApiKey = null, string? BaseUrl = null);
}

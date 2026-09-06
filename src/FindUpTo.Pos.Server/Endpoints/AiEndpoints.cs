using System.Security.Claims;
using FindUpTo.Pos.Server.Data;
using FindUpTo.Pos.Server.Services;
using Microsoft.EntityFrameworkCore;

namespace FindUpTo.Pos.Server.Endpoints;

public static class AiEndpoints
{
    private static readonly string[] AllowedRoles = ["Owner", "Manager", "Admin", "Counter"];

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
            return Results.Ok(new { enabled = provider.Provider != "none", provider = provider.Provider, baseUrl = provider.BaseUrl, model = provider.Model, requiresApiKey = provider.RequiresApiKey, local = provider.Local, status = provider.Status, capabilities = new[] { "sales", "payments", "inventory", "inventory-forecast", "reorder-recommendations", "purchasing", "receiving", "product-updates", "reports", "receipt-printing", "rider-tracking", "delivery-tracking", "routing-eta", "customer-crm", "finance-cashflow" } });
        }).RequireAuthorization(p => p.RequireRole(AllowedRoles));

        app.MapGet("/api/ai/inventory/forecast", async (CoreDbContext db, int? days, CancellationToken cancellationToken) =>
        {
            var horizon = Math.Clamp(days ?? 30, 7, 365);
            var now = DateTime.UtcNow;
            var from = now.AddDays(-Math.Max(30, horizon));
            var products = await db.Products.AsNoTracking().Where(x => x.Available).ToListAsync(cancellationToken);
            var inventory = await db.ProductInventories.AsNoTracking().ToDictionaryAsync(x => x.ProductId, cancellationToken);
            var sales = await db.OrderItems.AsNoTracking().Where(x => x.PosOrder.CreatedAtUtc >= from && x.PosOrder.Status != "Cancelled").GroupBy(x => x.ProductId).Select(g => new { productId = g.Key, units = g.Sum(x => x.Quantity), orders = g.Select(x => x.PosOrderId).Distinct().Count() }).ToListAsync(cancellationToken);
            var byProduct = sales.ToDictionary(x => x.productId);
            var result = products.Select(p =>
            {
                inventory.TryGetValue(p.Id, out var stock);
                byProduct.TryGetValue(p.Id, out var sold);
                var lookbackDays = Math.Max(30, (now - from).TotalDays);
                var dailyRate = sold is null ? 0m : Math.Round(sold.units / (decimal)lookbackDays, 3);
                var forecastUnits = Math.Ceiling(dailyRate * horizon);
                var onHand = stock?.QuantityOnHand ?? 0m;
                var reorderLevel = stock?.ReorderLevel ?? 0m;
                var target = Math.Max(reorderLevel, forecastUnits);
                var recommended = Math.Max(0m, Math.Ceiling(target - onHand));
                var daysRemaining = dailyRate > 0 ? Math.Round(onHand / dailyRate, 1) : (double?)null;
                return new { productId = p.Id, productName = p.Name, quantityOnHand = onHand, reorderLevel, unitsSold = sold?.units ?? 0, dailySalesRate = dailyRate, forecastUnits, daysRemaining, recommendedOrderQuantity = recommended, priority = onHand <= 0 && dailyRate > 0 ? "Critical" : onHand <= reorderLevel && dailyRate > 0 ? "High" : recommended > 0 ? "Medium" : "Normal" };
            }).OrderByDescending(x => x.priority == "Critical").ThenByDescending(x => x.priority == "High").ThenByDescending(x => x.recommendedOrderQuantity).ToList();
            return Results.Ok(new { horizonDays = horizon, lookbackFromUtc = from, generatedAtUtc = now, items = result });
        }).RequireAuthorization(p => p.RequireRole(AllowedRoles));

        app.MapGet("/api/ai/inventory/reorders", async (CoreDbContext db, CancellationToken cancellationToken) =>
        {
            var now = DateTime.UtcNow;
            var from = now.AddDays(-30);
            var inventory = await db.ProductInventories.AsNoTracking().ToListAsync(cancellationToken);
            var sales = await db.OrderItems.AsNoTracking().Where(x => x.PosOrder.CreatedAtUtc >= from && x.PosOrder.Status != "Cancelled").GroupBy(x => x.ProductId).Select(g => new { productId = g.Key, units = g.Sum(x => x.Quantity) }).ToListAsync(cancellationToken);
            var sold = sales.ToDictionary(x => x.productId, x => x.units);
            var products = await db.Products.AsNoTracking().Where(x => x.Available).ToDictionaryAsync(x => x.Id, cancellationToken);
            var recommendations = inventory.Where(x => x.TrackInventory && x.QuantityOnHand <= x.ReorderLevel).Select(x =>
            {
                products.TryGetValue(x.ProductId, out var product);
                var daily = sold.TryGetValue(x.ProductId, out var units) ? units / 30m : 0m;
                var target = Math.Max(x.ReorderLevel * 2m, daily * 14m);
                return new { productId = x.ProductId, productName = product?.Name ?? $"Product #{x.ProductId}", quantityOnHand = x.QuantityOnHand, reorderLevel = x.ReorderLevel, averageCost = x.AverageCost, unitsSoldLast30Days = sold.GetValueOrDefault(x.ProductId), dailySalesRate = Math.Round(daily, 3), recommendedOrderQuantity = Math.Max(1m, Math.Ceiling(target - x.QuantityOnHand)), estimatedCost = Math.Round(Math.Max(1m, Math.Ceiling(target - x.QuantityOnHand)) * x.AverageCost, 2) };
            }).OrderByDescending(x => x.estimatedCost).ToList();
            return Results.Ok(new { generatedAtUtc = now, recommendations });
        }).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin"));

        app.MapGet("/api/ai/providers/discover", async (AiProviderService providers, CancellationToken cancellationToken) => Results.Ok(await providers.DiscoverAsync(cancellationToken))).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin"));

        app.MapPut("/api/ai/configuration", async (AiConfigurationRequest input, ClaimsPrincipal user, AiProviderService providers, CancellationToken cancellationToken) =>
        {
            if (user.FindFirstValue(ClaimTypes.Role) != "Owner") return Results.Forbid();
            var provider = input.Provider.Trim().ToLowerInvariant();
            var allowed = new[] { "auto", "openai", "ollama", "lmstudio", "llamacpp" };
            if (!allowed.Contains(provider)) return Results.BadRequest("Unsupported AI provider.");
            if (provider == "openai" && string.IsNullOrWhiteSpace(input.ApiKey)) return Results.BadRequest("API key is required for a purchased OpenAI model.");
            if (provider is "ollama" or "lmstudio" or "llamacpp" && string.IsNullOrWhiteSpace(input.Model)) return Results.BadRequest("A local model name is required.");
            await providers.ConfigureAsync(provider, input.Model ?? "", input.ApiKey, input.BaseUrl, cancellationToken);
            return Results.Ok(new { success = true, message = "AI configuration saved. API keys are never returned by this endpoint." });
        }).RequireAuthorization(p => p.RequireRole("Owner"));
    }

    public sealed record AiOperateRequest(string Message);
    public sealed record AiConfigurationRequest(string Provider, string? Model = null, string? ApiKey = null, string? BaseUrl = null);
}
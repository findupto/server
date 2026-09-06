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
            return Results.Ok(new { enabled = provider.Provider != "none", provider = provider.Provider, baseUrl = provider.BaseUrl, model = provider.Model, requiresApiKey = provider.RequiresApiKey, local = provider.Local, status = provider.Status, capabilities = new[] { "sales", "payments", "inventory", "inventory-forecast", "reorder-recommendations", "purchasing-intelligence", "purchasing-automation", "supplier-intelligence", "receiving", "product-updates", "reports", "receipt-printing", "rider-tracking", "delivery-tracking", "routing-eta", "customer-crm", "finance-cashflow", "kitchen-kds" } });
        }).RequireAuthorization(p => p.RequireRole(AllowedRoles));

        app.MapGet("/api/ai/inventory/forecast", async (CoreDbContext db, int? days, CancellationToken cancellationToken) =>
        {
            var horizon = Math.Clamp(days ?? 30, 7, 365);
            var now = DateTime.UtcNow;
            var from = now.AddDays(-Math.Max(30, horizon));
            var products = await db.Products.AsNoTracking().Where(x => x.Available).ToListAsync(cancellationToken);
            var inventory = await db.ProductInventories.AsNoTracking().ToDictionaryAsync(x => x.ProductId, cancellationToken);
            var sales = await db.OrderItems.AsNoTracking().Where(x => x.PosOrder.CreatedAtUtc >= from && x.PosOrder.Status != "Cancelled").GroupBy(x => x.ProductId).Select(g => new { productId = g.Key, units = g.Sum(x => x.Quantity) }).ToListAsync(cancellationToken);
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
            var inventory = await db.ProductInventories.AsNoTracking().ToListAsync(cancellationToken);
            var sales = await db.OrderItems.AsNoTracking().Where(x => x.PosOrder.CreatedAtUtc >= DateTime.UtcNow.AddDays(-30) && x.PosOrder.Status != "Cancelled").GroupBy(x => x.ProductId).Select(g => new { productId = g.Key, units = g.Sum(x => x.Quantity) }).ToListAsync(cancellationToken);
            var sold = sales.ToDictionary(x => x.productId, x => x.units);
            var products = await db.Products.AsNoTracking().Where(x => x.Available).ToDictionaryAsync(x => x.Id, cancellationToken);
            var recommendations = inventory.Where(x => x.TrackInventory && x.QuantityOnHand <= x.ReorderLevel).Select(x =>
            {
                products.TryGetValue(x.ProductId, out var product);
                var daily = sold.TryGetValue(x.ProductId, out var units) ? units / 30m : 0m;
                var target = Math.Max(x.ReorderLevel * 2m, daily * 14m);
                var quantity = Math.Max(1m, Math.Ceiling(target - x.QuantityOnHand));
                return new { productId = x.ProductId, productName = product?.Name ?? $"Product #{x.ProductId}", quantityOnHand = x.QuantityOnHand, reorderLevel = x.ReorderLevel, averageCost = x.AverageCost, unitsSoldLast30Days = sold.GetValueOrDefault(x.ProductId), dailySalesRate = Math.Round(daily, 3), recommendedOrderQuantity = quantity, estimatedCost = Math.Round(quantity * x.AverageCost, 2) };
            }).OrderByDescending(x => x.estimatedCost).ToList();
            return Results.Ok(new { generatedAtUtc = DateTime.UtcNow, recommendations });
        }).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin"));

        app.MapGet("/api/ai/purchasing/suppliers", async (CoreDbContext db, CancellationToken cancellationToken) =>
        {
            var suppliers = await db.Suppliers.AsNoTracking().Where(x => x.Active).ToListAsync(cancellationToken);
            var orders = await db.PurchaseOrders.AsNoTracking().Where(x => x.Status != "Cancelled").Include(x => x.Items).ToListAsync(cancellationToken);
            var result = suppliers.Select(s =>
            {
                var supplierOrders = orders.Where(x => x.SupplierId == s.Id).ToList();
                var items = supplierOrders.SelectMany(x => x.Items).ToList();
                var received = items.Sum(x => x.QuantityReceived);
                var ordered = items.Sum(x => x.QuantityOrdered);
                var spend = items.Sum(x => x.QuantityReceived * x.UnitCost);
                return new { supplierId = s.Id, supplierName = s.Name, orderCount = supplierOrders.Count, completedOrders = supplierOrders.Count(x => x.Status == "Received"), unitsReceived = received, fillRate = ordered > 0 ? Math.Round(received / ordered, 3) : 0m, averageReceivedUnitCost = received > 0 ? Math.Round(spend / received, 2) : 0m, active = s.Active };
            }).OrderByDescending(x => x.completedOrders).ThenByDescending(x => x.fillRate).ToList();
            return Results.Ok(new { generatedAtUtc = DateTime.UtcNow, suppliers = result });
        }).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin"));

        app.MapGet("/api/ai/purchasing/recommendations", async (CoreDbContext db, CancellationToken cancellationToken) =>
        {
            var inventory = await db.ProductInventories.AsNoTracking().Where(x => x.TrackInventory && x.QuantityOnHand <= x.ReorderLevel).ToListAsync(cancellationToken);
            var products = await db.Products.AsNoTracking().Where(x => x.Available).ToDictionaryAsync(x => x.Id, cancellationToken);
            var suppliers = await db.Suppliers.AsNoTracking().Where(x => x.Active).ToDictionaryAsync(x => x.Id, cancellationToken);
            var history = await db.PurchaseOrders.AsNoTracking().Where(x => x.Status != "Cancelled").Include(x => x.Items).ToListAsync(cancellationToken);
            var recentSales = await db.OrderItems.AsNoTracking().Where(x => x.PosOrder.CreatedAtUtc >= DateTime.UtcNow.AddDays(-30) && x.PosOrder.Status != "Cancelled").GroupBy(x => x.ProductId).Select(g => new { productId = g.Key, units = g.Sum(x => x.Quantity) }).ToDictionaryAsync(x => x.productId, cancellationToken);
            var recommendations = inventory.Select(stock =>
            {
                products.TryGetValue(stock.ProductId, out var product);
                var candidates = history.SelectMany(po => po.Items.Where(i => i.ProductId == stock.ProductId && i.QuantityReceived > 0).Select(i => new { po.SupplierId, i.UnitCost, i.QuantityReceived })).ToList();
                var best = candidates.GroupBy(x => x.SupplierId).Select(g => new { supplierId = g.Key, received = g.Sum(x => x.QuantityReceived), avgCost = g.Sum(x => x.QuantityReceived * x.UnitCost) / g.Sum(x => x.QuantityReceived) }).OrderBy(x => x.avgCost).ThenByDescending(x => x.received).FirstOrDefault();
                var daily = recentSales.GetValueOrDefault(stock.ProductId) / 30m;
                var quantity = Math.Max(1m, Math.Ceiling(Math.Max(stock.ReorderLevel * 2m, daily * 14m) - stock.QuantityOnHand));
                var unitCost = best?.avgCost ?? stock.AverageCost;
                return new { productId = stock.ProductId, productName = product?.Name ?? $"Product #{stock.ProductId}", recommendedOrderQuantity = quantity, supplierId = best?.supplierId, supplierName = best is not null && suppliers.TryGetValue(best.supplierId, out var supplier) ? supplier.Name : null, estimatedUnitCost = Math.Round(unitCost, 2), estimatedCost = Math.Round(quantity * unitCost, 2), confidence = best is null ? "Low" : best.received >= stock.ReorderLevel ? "High" : "Medium" };
            }).OrderByDescending(x => x.estimatedCost).ToList();
            return Results.Ok(new { generatedAtUtc = DateTime.UtcNow, recommendations });
        }).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin"));

        app.MapPost("/api/ai/purchasing/create-drafts", async (ClaimsPrincipal user, CoreDbContext db, CancellationToken cancellationToken) =>
        {
            var inventory = await db.ProductInventories.Where(x => x.TrackInventory && x.QuantityOnHand <= x.ReorderLevel).ToListAsync(cancellationToken);
            var products = await db.Products.AsNoTracking().Where(x => x.Available).ToDictionaryAsync(x => x.Id, cancellationToken);
            var suppliers = await db.Suppliers.Where(x => x.Active).ToDictionaryAsync(x => x.Id, cancellationToken);
            var history = await db.PurchaseOrders.AsNoTracking().Where(x => x.Status != "Cancelled").Include(x => x.Items).ToListAsync(cancellationToken);
            var recentSales = await db.OrderItems.AsNoTracking().Where(x => x.PosOrder.CreatedAtUtc >= DateTime.UtcNow.AddDays(-30) && x.PosOrder.Status != "Cancelled").GroupBy(x => x.ProductId).Select(g => new { productId = g.Key, units = g.Sum(x => x.Quantity) }).ToDictionaryAsync(x => x.productId, cancellationToken);
            var groups = new Dictionary<int, List<(int ProductId, string ProductName, decimal Quantity, decimal UnitCost)>>();
            foreach (var stock in inventory)
            {
                if (!products.TryGetValue(stock.ProductId, out var product)) continue;
                var candidates = history.SelectMany(po => po.Items.Where(i => i.ProductId == stock.ProductId && i.QuantityReceived > 0).Select(i => new { po.SupplierId, i.UnitCost, i.QuantityReceived })).ToList();
                var best = candidates.GroupBy(x => x.SupplierId).Select(g => new { supplierId = g.Key, avgCost = g.Sum(x => x.QuantityReceived * x.UnitCost) / g.Sum(x => x.QuantityReceived) }).OrderBy(x => x.avgCost).FirstOrDefault();
                if (best is null || !suppliers.ContainsKey(best.supplierId)) continue;
                var quantity = Math.Max(1m, Math.Ceiling(Math.Max(stock.ReorderLevel * 2m, (recentSales.GetValueOrDefault(stock.ProductId) / 30m) * 14m) - stock.QuantityOnHand));
                if (!groups.TryGetValue(best.supplierId, out var list)) groups[best.supplierId] = list = [];
                list.Add((stock.ProductId, product.Name, quantity, Math.Round(best.avgCost, 2)));
            }
            var created = new List<object>();
            foreach (var group in groups)
            {
                var order = new FindUpTo.Pos.Server.Models.PurchaseOrder { SupplierId = group.Key, Status = "Draft", CreatedByUsername = user.Identity?.Name ?? "unknown", Notes = "AI reorder recommendation — review before ordering." };
                order.Items.AddRange(group.Value.Select(x => new FindUpTo.Pos.Server.Models.PurchaseOrderItem { ProductId = x.ProductId, ProductName = x.ProductName, QuantityOrdered = x.Quantity, UnitCost = x.UnitCost }));
                db.PurchaseOrders.Add(order);
                created.Add(new { supplierId = group.Key, supplierName = suppliers[group.Key].Name, itemCount = group.Value.Count, estimatedTotal = Math.Round(group.Value.Sum(x => x.Quantity * x.UnitCost), 2) });
            }
            if (created.Count > 0) await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { createdCount = created.Count, message = created.Count == 0 ? "No supplier-backed reorder drafts were needed." : "Draft purchase orders created for review; nothing was sent to suppliers.", purchaseOrders = created });
        }).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin"));

        app.MapGet("/api/ai/kitchen/queue", async (int? limit, CoreDbContext db, CancellationToken cancellationToken) => Results.Ok(await new KitchenAiService(db).GetQueueAsync(limit ?? 100, cancellationToken))).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin", "Kitchen"));
        app.MapGet("/api/ai/kitchen/recommend-next", async (CoreDbContext db, CancellationToken cancellationToken) => Results.Ok(await new KitchenAiService(db).RecommendNextAsync(cancellationToken))).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin", "Kitchen"));

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
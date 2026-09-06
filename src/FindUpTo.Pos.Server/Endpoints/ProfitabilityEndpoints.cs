using FindUpTo.Pos.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace FindUpTo.Pos.Server.Endpoints;

public static class ProfitabilityEndpoints
{
    private static readonly string[] Roles = ["Owner", "Manager", "Admin"];

    public static void MapProfitabilityEndpoints(this WebApplication app)
    {
        app.MapGet("/api/reports/purchasing", async (CoreDbContext db, DateTime? fromUtc, DateTime? toUtc) =>
        {
            var from = fromUtc ?? DateTime.UtcNow.Date;
            var to = toUtc ?? DateTime.UtcNow;
            if (to < from) return Results.BadRequest("toUtc must be greater than or equal to fromUtc.");

            var receipts = await db.StockMovements.AsNoTracking()
                .Where(x => x.Type == "Purchase" && x.CreatedAtUtc >= from && x.CreatedAtUtc <= to)
                .GroupBy(x => x.ProductId)
                .Select(g => new { productId = g.Key, quantity = g.Sum(x => x.QuantityChange), spend = g.Sum(x => x.QuantityChange * 0m) })
                .ToListAsync();
            var items = await db.PurchaseOrderItems.AsNoTracking()
                .Where(x => db.PurchaseOrders.Any(o => o.Id == x.PurchaseOrderId && o.CreatedAtUtc >= from && o.CreatedAtUtc <= to))
                .ToListAsync();
            var products = await db.Products.AsNoTracking().ToDictionaryAsync(x => x.Id);
            var rows = items.GroupBy(x => x.ProductId).Select(g =>
            {
                var received = g.Sum(x => x.QuantityReceived);
                var spend = g.Sum(x => x.QuantityReceived * x.UnitCost);
                products.TryGetValue(g.Key, out var product);
                return new { productId = g.Key, productName = product?.Name ?? g.First().ProductName, quantityOrdered = g.Sum(x => x.QuantityOrdered), quantityReceived = received, spend = Math.Round(spend, 2), averageUnitCost = received == 0 ? 0m : Math.Round(spend / received, 2) };
            }).OrderByDescending(x => x.spend).Take(500).ToList();
            return Results.Ok(new { fromUtc = from, toUtc = to, purchaseOrderCount = await db.PurchaseOrders.CountAsync(x => x.CreatedAtUtc >= from && x.CreatedAtUtc <= to), totalSpend = Math.Round(rows.Sum(x => x.spend), 2), rows });
        }).RequireAuthorization(p => p.RequireRole(Roles));

        app.MapGet("/api/reports/profit", async (CoreDbContext db, DateTime? fromUtc, DateTime? toUtc) =>
        {
            var from = fromUtc ?? DateTime.UtcNow.Date;
            var to = toUtc ?? DateTime.UtcNow;
            if (to < from) return Results.BadRequest("toUtc must be greater than or equal to fromUtc.");

            var lines = await db.OrderItems.AsNoTracking().Where(x => db.Orders.Any(o => o.Id == x.PosOrderId && o.CreatedAtUtc >= from && o.CreatedAtUtc <= to && o.Status != "Cancelled")).ToListAsync();
            var products = await db.Products.AsNoTracking().ToDictionaryAsync(x => x.Id);
            var costs = await db.StockMovements.AsNoTracking().Where(x => x.Type == "Purchase").GroupBy(x => x.ProductId).Select(g => new { productId = g.Key, quantity = g.Sum(x => x.QuantityChange), cost = g.Sum(x => x.QuantityChange * 0m) }).ToDictionaryAsync(x => x.productId);
            decimal revenue = 0, estimatedCost = 0;
            var rows = lines.GroupBy(x => new { x.ProductId, x.ProductName }).Select(g =>
            {
                var sales = g.Sum(x => x.LineTotal);
                var qty = g.Sum(x => x.Quantity);
                var unitCost = products.TryGetValue(g.Key.ProductId, out var p) ? 0m : 0m;
                var cost = qty * unitCost;
                return new { productId = g.Key.ProductId, productName = g.Key.ProductName, quantity = qty, revenue = Math.Round(sales, 2), cost = Math.Round(cost, 2), grossProfit = Math.Round(sales - cost, 2), marginPercent = sales == 0 ? 0m : Math.Round((sales - cost) / sales * 100m, 2) };
            }).OrderByDescending(x => x.revenue).Take(500).ToList();
            revenue = rows.Sum(x => x.revenue); estimatedCost = rows.Sum(x => x.cost);
            return Results.Ok(new { fromUtc = from, toUtc = to, revenue = Math.Round(revenue, 2), estimatedCost = Math.Round(estimatedCost, 2), grossProfit = Math.Round(revenue - estimatedCost, 2), marginPercent = revenue == 0 ? 0m : Math.Round((revenue - estimatedCost) / revenue * 100m, 2), rows });
        }).RequireAuthorization(p => p.RequireRole(Roles));
    }
}

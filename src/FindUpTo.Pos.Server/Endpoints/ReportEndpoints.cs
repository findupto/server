using FindUpTo.Pos.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace FindUpTo.Pos.Server.Endpoints;

public static class ReportEndpoints
{
    public static void MapReportEndpoints(this WebApplication app)
    {
        app.MapGet("/api/reports/sales", async (CoreDbContext db, DateTime? fromUtc, DateTime? toUtc) =>
        {
            var from = fromUtc ?? DateTime.UtcNow.Date;
            var to = toUtc ?? DateTime.UtcNow;
            if (to < from) return Results.BadRequest("toUtc must be greater than or equal to fromUtc.");

            var orders = await db.Orders.AsNoTracking()
                .Where(x => x.CreatedAtUtc >= from && x.CreatedAtUtc <= to && x.Status != "Cancelled")
                .ToListAsync();
            var payments = await db.Payments.AsNoTracking()
                .Where(x => x.CreatedAtUtc >= from && x.CreatedAtUtc <= to && x.Status == "Paid")
                .ToListAsync();

            return Results.Ok(new
            {
                fromUtc = from,
                toUtc = to,
                orderCount = orders.Count,
                subtotal = orders.Sum(x => x.Subtotal),
                tax = orders.Sum(x => x.Tax),
                grossSales = orders.Sum(x => x.Total),
                paidAmount = payments.Sum(x => x.AmountPaid),
                paymentsByMethod = payments.GroupBy(x => x.Method).Select(g => new { method = g.Key, amount = g.Sum(x => x.AmountPaid), count = g.Count() }).OrderByDescending(x => x.amount)
            });
        }).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin"));

        app.MapGet("/api/reports/products", async (CoreDbContext db, DateTime? fromUtc, DateTime? toUtc) =>
        {
            var from = fromUtc ?? DateTime.UtcNow.Date;
            var to = toUtc ?? DateTime.UtcNow;
            if (to < from) return Results.BadRequest("toUtc must be greater than or equal to fromUtc.");

            var rows = await db.OrderItems.AsNoTracking()
                .Where(x => x.PosOrderId > 0 && db.Orders.Any(o => o.Id == x.PosOrderId && o.CreatedAtUtc >= from && o.CreatedAtUtc <= to && o.Status != "Cancelled"))
                .GroupBy(x => new { x.ProductId, x.ProductName })
                .Select(g => new { productId = g.Key.ProductId, productName = g.Key.ProductName, quantity = g.Sum(x => x.Quantity), sales = g.Sum(x => x.LineTotal) })
                .OrderByDescending(x => x.sales)
                .Take(100)
                .ToListAsync();
            return Results.Ok(rows);
        }).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin"));
    }
}

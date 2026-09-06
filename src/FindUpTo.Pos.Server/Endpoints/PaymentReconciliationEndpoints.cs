using FindUpTo.Pos.Server.Data;
using FindUpTo.Pos.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace FindUpTo.Pos.Server.Endpoints;

public static class PaymentReconciliationEndpoints
{
    private static readonly string[] Roles = ["Owner", "Manager", "Admin"];
    private sealed record PaymentReconciliationRow(int OrderId, string OrderStatus, string OrderType, int? CustomerId, DateTime CreatedAtUtc, decimal Total, decimal CollectedAmount, decimal OutstandingAmount, decimal OverpaidAmount, string PaymentStatus, int PaymentCount, DateTime? LastPaymentAtUtc, object PaymentMethods);

    public static void MapPaymentReconciliationEndpoints(this WebApplication app)
    {
        app.MapGet("/api/payments/reconciliation", async (CoreDbContext db, DateTime? fromUtc, DateTime? toUtc, string? status) =>
        {
            var (from, to) = NormalizeRange(fromUtc, toUtc);
            var orders = await db.Orders.AsNoTracking().Where(x => x.CreatedAtUtc >= from && x.CreatedAtUtc < to).OrderByDescending(x => x.CreatedAtUtc).Take(2000).ToListAsync();
            var orderIds = orders.Select(x => x.Id).ToList();
            var payments = await db.Payments.AsNoTracking().Where(x => orderIds.Contains(x.PosOrderId) && x.Status == "Paid").ToListAsync();
            var rows = orders.Select(order => BuildRow(order, payments.Where(x => x.PosOrderId == order.Id).ToList())).Where(x => string.IsNullOrWhiteSpace(status) || x.PaymentStatus.Equals(status.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
            return Results.Ok(new { fromUtc = from, toUtc = to, count = rows.Count, items = rows });
        }).RequireAuthorization(p => p.RequireRole(Roles));

        app.MapGet("/api/payments/reconciliation/summary", async (CoreDbContext db, DateTime? fromUtc, DateTime? toUtc) =>
        {
            var (from, to) = NormalizeRange(fromUtc, toUtc);
            var orders = await db.Orders.AsNoTracking().Where(x => x.CreatedAtUtc >= from && x.CreatedAtUtc < to).ToListAsync();
            var ids = orders.Select(x => x.Id).ToList();
            var payments = await db.Payments.AsNoTracking().Where(x => ids.Contains(x.PosOrderId) && x.Status == "Paid").ToListAsync();
            var rows = orders.Select(order => BuildRow(order, payments.Where(x => x.PosOrderId == order.Id).ToList())).ToList();
            var byMethod = payments.GroupBy(x => x.Method).Select(g => new { method = g.Key, amount = Math.Round(g.Sum(NetCollected), 2), paymentCount = g.Count() }).OrderByDescending(x => x.amount).ToList();
            return Results.Ok(new { fromUtc = from, toUtc = to, orderCount = rows.Count, paidOrderCount = rows.Count(x => x.PaymentStatus == "Paid"), partiallyPaidOrderCount = rows.Count(x => x.PaymentStatus == "PartiallyPaid"), unpaidOrderCount = rows.Count(x => x.PaymentStatus == "Unpaid"), overpaidOrderCount = rows.Count(x => x.PaymentStatus == "Overpaid"), billedAmount = Math.Round(rows.Sum(x => x.Total), 2), collectedAmount = Math.Round(rows.Sum(x => x.CollectedAmount), 2), outstandingAmount = Math.Round(rows.Sum(x => x.OutstandingAmount), 2), overpaidAmount = Math.Round(rows.Sum(x => x.OverpaidAmount), 2), paymentsByMethod = byMethod });
        }).RequireAuthorization(p => p.RequireRole(Roles));

        app.MapGet("/api/payments/reconciliation/discrepancies", async (CoreDbContext db, DateTime? fromUtc, DateTime? toUtc) =>
        {
            var (from, to) = NormalizeRange(fromUtc, toUtc);
            var orders = await db.Orders.AsNoTracking().Where(x => x.CreatedAtUtc >= from && x.CreatedAtUtc < to).ToListAsync();
            var ids = orders.Select(x => x.Id).ToList();
            var payments = await db.Payments.AsNoTracking().Where(x => ids.Contains(x.PosOrderId) && x.Status == "Paid").ToListAsync();
            var discrepancies = orders.Select(order => BuildRow(order, payments.Where(x => x.PosOrderId == order.Id).ToList())).Where(x => x.PaymentStatus == "Overpaid" || (x.PaymentStatus == "Paid" && !x.OrderStatus.Equals("Completed", StringComparison.OrdinalIgnoreCase))).ToList();
            return Results.Ok(new { fromUtc = from, toUtc = to, count = discrepancies.Count, items = discrepancies });
        }).RequireAuthorization(p => p.RequireRole(Roles));
    }

    private static (DateTime From, DateTime To) NormalizeRange(DateTime? fromUtc, DateTime? toUtc)
    {
        var to = (toUtc ?? DateTime.UtcNow).ToUniversalTime();
        var from = (fromUtc ?? to.AddDays(-30)).ToUniversalTime();
        return from < to ? (from, to) : (to.AddDays(-30), to);
    }

    private static PaymentReconciliationRow BuildRow(PosOrder order, List<Payment> payments)
    {
        var collected = Math.Round(payments.Sum(NetCollected), 2);
        var total = Math.Round(order.Total, 2);
        var outstanding = Math.Round(Math.Max(total - collected, 0m), 2);
        var overpaid = Math.Round(Math.Max(collected - total, 0m), 2);
        var status = overpaid > 0 ? "Overpaid" : outstanding == 0 ? "Paid" : collected > 0 ? "PartiallyPaid" : "Unpaid";
        var methods = payments.GroupBy(x => x.Method).Select(g => new { method = g.Key, amount = Math.Round(g.Sum(NetCollected), 2) }).ToList();
        return new PaymentReconciliationRow(order.Id, order.Status, order.OrderType, order.CustomerId, order.CreatedAtUtc, total, collected, outstanding, overpaid, status, payments.Count, payments.Count == 0 ? null : payments.Max(x => x.CreatedAtUtc), methods);
    }

    private static decimal NetCollected(Payment payment) => Math.Round(payment.AmountPaid - payment.ChangeAmount, 2);
}

using FindUpTo.Pos.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace FindUpTo.Pos.Server.Services;

public sealed class AnomalyAiService(CoreDbContext db)
{
    public async Task<object> AnalyzeAsync(int days, CancellationToken cancellationToken)
    {
        days = Math.Clamp(days, 7, 365);
        var now = DateTime.UtcNow;
        var from = now.AddDays(-days);
        var orders = await db.Orders.AsNoTracking().Include(x => x.Items)
            .Where(x => x.CreatedAtUtc >= from).ToListAsync(cancellationToken);
        var payments = await db.Payments.AsNoTracking()
            .Where(x => x.CreatedAtUtc >= from).ToListAsync(cancellationToken);

        var daily = orders.GroupBy(x => x.CreatedAtUtc.Date).Select(g => new { day = g.Key, orders = g.Count(), revenue = g.Where(x => x.Status != "Cancelled").Sum(x => x.Total) }).ToList();
        var avgOrders = daily.Count == 0 ? 0m : daily.Average(x => x.orders);
        var avgRevenue = daily.Count == 0 ? 0m : daily.Average(x => x.revenue);
        var alerts = new List<object>();

        foreach (var day in daily.OrderByDescending(x => x.day))
        {
            if (avgOrders > 0 && day.orders >= avgOrders * 2.5m)
                alerts.Add(new { type = "OrderSpike", severity = "Medium", date = day.day, message = $"Order volume was {day.orders} versus a daily average of {Math.Round(avgOrders, 1)}." });
            if (avgRevenue > 0 && day.revenue <= avgRevenue * 0.35m)
                alerts.Add(new { type = "RevenueDrop", severity = "High", date = day.day, message = $"Revenue was {Math.Round(day.revenue, 2)} versus a daily average of {Math.Round(avgRevenue, 2)}." });
        }

        var refundedOrVoided = payments.Count(x => x.Status.Equals("Refunded", StringComparison.OrdinalIgnoreCase) || x.Status.Equals("Voided", StringComparison.OrdinalIgnoreCase));
        if (refundedOrVoided > 0)
            alerts.Add(new { type = "PaymentReversals", severity = refundedOrVoided >= 5 ? "High" : "Medium", date = now.Date, message = $"{refundedOrVoided} payment reversals detected in the selected period." });

        var cancelled = orders.Count(x => x.Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase));
        if (orders.Count > 0 && cancelled >= Math.Ceiling(orders.Count * 0.15m))
            alerts.Add(new { type = "CancellationRate", severity = "Medium", date = now.Date, message = $"Cancellation rate is {Math.Round(cancelled * 100m / orders.Count, 1)}%." });

        return new { generatedAtUtc = now, lookbackDays = days, baseline = new { averageDailyOrders = Math.Round(avgOrders, 2), averageDailyRevenue = Math.Round(avgRevenue, 2) }, alertCount = alerts.Count, alerts = alerts.OrderByDescending(x => x.GetType().GetProperty("severity")?.GetValue(x)?.ToString() == "High").ToList() };
    }
}

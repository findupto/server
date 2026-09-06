using FindUpTo.Pos.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace FindUpTo.Pos.Server.Services;

public sealed class BusinessHealthAiService(CoreDbContext db)
{
    public async Task<object> GetSummaryAsync(int days, CancellationToken cancellationToken)
    {
        days = Math.Clamp(days, 1, 365);
        var now = DateTime.UtcNow;
        var from = now.AddDays(-days);
        var orders = await db.Orders.AsNoTracking().Where(x => x.CreatedAtUtc >= from).ToListAsync(cancellationToken);
        var payments = await db.Payments.AsNoTracking().Where(x => x.CreatedAtUtc >= from).ToListAsync(cancellationToken);
        var inventory = await db.ProductInventories.AsNoTracking().Where(x => x.TrackInventory).ToListAsync(cancellationToken);
        var activeCustomers = await db.Orders.AsNoTracking().Where(x => x.CustomerId.HasValue && x.CreatedAtUtc >= from && x.Status != "Cancelled").Select(x => x.CustomerId!.Value).Distinct().CountAsync(cancellationToken);
        var completed = orders.Where(x => x.Status is "Completed" or "Served").ToList();
        var cancelled = orders.Count(x => x.Status == "Cancelled");
        var revenue = orders.Where(x => x.Status != "Cancelled").Sum(x => x.Total);
        var paid = payments.Where(x => x.Status == "Paid").Sum(x => x.AmountPaid);
        var lowStock = inventory.Count(x => x.QuantityOnHand <= x.ReorderLevel);
        var cancellationRate = orders.Count == 0 ? 0m : cancelled * 100m / orders.Count;
        var score = 100m;
        if (cancellationRate >= 15) score -= 25; else if (cancellationRate >= 8) score -= 12;
        if (lowStock > 0) score -= Math.Min(25, lowStock * 3);
        if (completed.Count == 0 && orders.Count > 0) score -= 20;
        if (revenue <= 0 && orders.Count > 0) score -= 15;
        score = Math.Clamp(score, 0, 100);
        var health = score >= 80 ? "Healthy" : score >= 60 ? "Watch" : score >= 40 ? "AtRisk" : "Critical";
        var priorities = new List<string>();
        if (lowStock > 0) priorities.Add("Review low-stock inventory.");
        if (cancellationRate >= 8) priorities.Add("Investigate elevated order cancellations.");
        if (revenue <= 0 && orders.Count > 0) priorities.Add("Investigate orders without recognized revenue.");
        if (priorities.Count == 0) priorities.Add("No major operational risk detected.");
        return new { generatedAtUtc = now, lookbackDays = days, healthScore = Math.Round(score, 1), health, kpis = new { orders = orders.Count, completedOrders = completed.Count, cancelledOrders = cancelled, cancellationRatePercent = Math.Round(cancellationRate, 1), revenue = Math.Round(revenue, 2), paidAmount = Math.Round(paid, 2), lowStockItems = lowStock, activeCustomers }, priorities };
    }
}

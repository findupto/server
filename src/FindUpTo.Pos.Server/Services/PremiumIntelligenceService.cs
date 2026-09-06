using FindUpTo.Pos.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace FindUpTo.Pos.Server.Services;

public sealed class PremiumIntelligenceService(CoreDbContext db)
{
    public async Task<object> GetExecutiveDashboardAsync(int days, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var from = now.Date.AddDays(-days + 1);
        var orders = await db.Orders.AsNoTracking().Where(x => x.CreatedAtUtc >= from && x.Status != "Cancelled").Include(x => x.Items).ToListAsync(ct);
        var payments = await db.Payments.AsNoTracking().Where(x => x.CreatedAtUtc >= from && x.Status == "Paid").ToListAsync(ct);
        var expenses = await db.Expenses.AsNoTracking().Where(x => x.ExpenseDateUtc >= from).ToListAsync(ct);
        var revenue = orders.Sum(x => x.Total);
        var grossProfit = orders.SelectMany(x => x.Items).Sum(x => x.LineTotal - x.UnitCost * x.Quantity - x.DiscountAmount);
        var expenseTotal = expenses.Sum(x => x.Amount);
        var daily = orders.GroupBy(x => x.CreatedAtUtc.Date).Select(g => new { date = g.Key, revenue = g.Sum(x => x.Total), orders = g.Count() }).OrderBy(x => x.date).ToList();
        var previousFrom = from.AddDays(-days);
        var previous = await db.Orders.AsNoTracking().Where(x => x.CreatedAtUtc >= previousFrom && x.CreatedAtUtc < from && x.Status != "Cancelled").Select(x => x.Total).ToListAsync(ct);
        var previousRevenue = previous.Sum();
        return new
        {
            generatedAtUtc = now, periodDays = days, revenue, orders = orders.Count, averageTicket = orders.Count == 0 ? 0m : Math.Round(revenue / orders.Count, 2),
            grossProfit = Math.Round(grossProfit, 2), grossMargin = revenue == 0 ? 0m : Math.Round(grossProfit / revenue, 4),
            expenses = Math.Round(expenseTotal, 2), contributionAfterExpenses = Math.Round(grossProfit - expenseTotal, 2),
            revenueGrowth = previousRevenue == 0 ? (revenue > 0 ? 1m : 0m) : Math.Round((revenue - previousRevenue) / previousRevenue, 4),
            payments = payments.GroupBy(x => x.Method).Select(g => new { method = g.Key, amount = Math.Round(g.Sum(x => x.AmountPaid), 2), transactions = g.Count() }).OrderByDescending(x => x.amount).ToList(),
            daily
        };
    }

    public async Task<object> GetMenuEngineeringAsync(int days, CancellationToken ct)
    {
        var from = DateTime.UtcNow.Date.AddDays(-days + 1);
        var items = await (from item in db.OrderItems.AsNoTracking()
                           join order in db.Orders.AsNoTracking() on item.PosOrderId equals order.Id
                           where order.CreatedAtUtc >= from && order.Status != "Cancelled"
                           group item by new { item.ProductId, item.ProductName } into g
                           select new
                           {
                               productId = g.Key.ProductId, productName = g.Key.ProductName,
                               units = g.Sum(x => x.Quantity), revenue = g.Sum(x => x.LineTotal - x.DiscountAmount),
                               grossProfit = g.Sum(x => x.LineTotal - x.DiscountAmount - x.UnitCost * x.Quantity)
                           }).ToListAsync(ct);
        if (items.Count == 0) return new { generatedAtUtc = DateTime.UtcNow, periodDays = days, items };
        var medianUnits = Median(items.Select(x => (decimal)x.units));
        var medianMargin = Median(items.Select(x => x.revenue == 0 ? 0m : x.grossProfit / x.revenue));
        var result = items.Select(x =>
        {
            var margin = x.revenue == 0 ? 0m : x.grossProfit / x.revenue;
            var popular = x.units >= medianUnits; var profitable = margin >= medianMargin;
            var quadrant = (popular, profitable) switch { (true, true) => "Star", (true, false) => "Plowhorse", (false, true) => "Puzzle", _ => "Dog" };
            return new { x.productId, x.productName, x.units, revenue = Math.Round(x.revenue, 2), grossProfit = Math.Round(x.grossProfit, 2), margin = Math.Round(margin, 4), quadrant };
        }).OrderByDescending(x => x.revenue).ToList();
        return new { generatedAtUtc = DateTime.UtcNow, periodDays = days, medianUnits, medianMargin = Math.Round(medianMargin, 4), items = result };
    }

    public async Task<object> GetCustomerSegmentsAsync(int days, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var from = now.AddDays(-days);
        var customers = await db.Customers.AsNoTracking().ToListAsync(ct);
        var orders = await db.Orders.AsNoTracking().Where(x => x.CustomerId.HasValue && x.CreatedAtUtc >= from && x.Status != "Cancelled").ToListAsync(ct);
        var rows = customers.Select(c =>
        {
            var o = orders.Where(x => x.CustomerId == c.Id).ToList();
            var last = o.Count == 0 ? (DateTime?)null : o.Max(x => x.CreatedAtUtc);
            var recency = last.HasValue ? (now - last.Value).TotalDays : days;
            var frequency = o.Count; var monetary = o.Sum(x => x.Total);
            var segment = frequency == 0 ? "New/No Purchase" : recency <= 14 && frequency >= 5 ? "VIP" : recency <= 30 && frequency >= 2 ? "Loyal" : recency <= 45 ? "Active" : recency <= 90 ? "At Risk" : "Churned";
            return new { customerId = c.Id, customerName = c.Name, orders = frequency, spend = Math.Round(monetary, 2), daysSinceLastOrder = Math.Round(recency, 1), segment };
        }).OrderBy(x => x.segment).ThenByDescending(x => x.spend).ToList();
        return new { generatedAtUtc = now, periodDays = days, segments = rows.GroupBy(x => x.segment).Select(g => new { segment = g.Key, customers = g.Count(), spend = Math.Round(g.Sum(x => x.spend), 2), averageSpend = g.Count() == 0 ? 0m : Math.Round(g.Average(x => x.spend), 2) }).ToList(), customers = rows };
    }

    public async Task<object> GetSalesForecastAsync(int historyDays, int horizonDays, CancellationToken ct)
    {
        var end = DateTime.UtcNow.Date.AddDays(1); var start = end.AddDays(-historyDays);
        var orders = await db.Orders.AsNoTracking().Where(x => x.CreatedAtUtc >= start && x.CreatedAtUtc < end && x.Status != "Cancelled").Select(x => new { x.CreatedAtUtc, x.Total }).ToListAsync(ct);
        var series = Enumerable.Range(0, historyDays).Select(i => new { day = start.AddDays(i), value = orders.Where(x => x.CreatedAtUtc.Date == start.AddDays(i)).Sum(x => x.Total) }).ToList();
        var values = series.Select(x => x.value).ToList();
        var recent = values.TakeLast(Math.Min(7, values.Count)).DefaultIfEmpty(0m).Average();
        var older = values.Take(Math.Max(1, values.Count - Math.Min(7, values.Count))).DefaultIfEmpty(0m).Average();
        var trend = older == 0 ? 0m : Math.Clamp((recent - older) / older, -0.5m, 0.5m);
        var forecast = Enumerable.Range(1, horizonDays).Select(i => new { date = end.Date.AddDays(i - 1), predictedRevenue = Math.Round(Math.Max(0m, recent * (1m + trend * i / Math.Max(1, horizonDays))), 2) }).ToList();
        return new { generatedAtUtc = DateTime.UtcNow, historyDays, horizonDays, baselineDailyRevenue = Math.Round(recent, 2), trendPercent = Math.Round(trend * 100m, 2), historical = series, forecast };
    }

    public async Task<object> GetDataQualityAsync(CancellationToken ct)
    {
        var products = await db.Products.AsNoTracking().ToListAsync(ct);
        var customers = await db.Customers.AsNoTracking().ToListAsync(ct);
        var inventory = await db.ProductInventories.AsNoTracking().ToListAsync(ct);
        var categories = await db.Categories.AsNoTracking().ToListAsync(ct);
        var categoryIds = categories.Select(x => x.Id).ToHashSet();
        var inventoryByProduct = inventory.ToDictionary(x => x.ProductId);
        var missingCategory = products.Count(x => !categoryIds.Contains(x.CategoryId));
        var missingCost = products.Count(x => !inventoryByProduct.TryGetValue(x.Id, out var i) || i.AverageCost <= 0);
        var missingBarcode = products.Count(x => string.IsNullOrWhiteSpace(x.Barcode));
        var missingDescription = products.Count(x => string.IsNullOrWhiteSpace(x.Description));
        var missingImage = products.Count(x => string.IsNullOrWhiteSpace(x.ImageUrl));
        var missingPhone = customers.Count(x => string.IsNullOrWhiteSpace(x.Phone));
        var score = products.Count + customers.Count == 0 ? 100m : Math.Round(100m - (missingCategory + missingCost + missingBarcode + missingDescription + missingImage + missingPhone) / (decimal)Math.Max(1, products.Count * 5 + customers.Count) * 100m, 1);
        return new { generatedAtUtc = DateTime.UtcNow, qualityScore = Math.Clamp(score, 0m, 100m), products = products.Count, customers = customers.Count, issues = new { missingCategory, missingCost, missingBarcode, missingDescription, missingImage, customersMissingPhone = missingPhone } };
    }

    public async Task<object> GetSmartAlertsAsync(int days, CancellationToken ct)
    {
        var now = DateTime.UtcNow; var from = now.AddDays(-days);
        var alerts = new List<object>();
        var lowStock = await db.ProductInventories.AsNoTracking().Where(x => x.TrackInventory && x.QuantityOnHand <= x.ReorderLevel).CountAsync(ct);
        if (lowStock > 0) alerts.Add(new { severity = lowStock >= 10 ? "critical" : "warning", type = "low-stock", count = lowStock, message = $"{lowStock} tracked products are at or below reorder level." });
        var orders = await db.Orders.AsNoTracking().Where(x => x.CreatedAtUtc >= from && x.Status != "Cancelled").ToListAsync(ct);
        var revenue = orders.Sum(x => x.Total); var average = orders.Count == 0 ? 0m : revenue / orders.Count;
        var recentOrders = orders.Where(x => x.CreatedAtUtc >= now.AddDays(-3)).ToList(); var recentAverage = recentOrders.Count == 0 ? average : recentOrders.Sum(x => x.Total) / recentOrders.Count;
        if (average > 0 && recentAverage < average * .7m) alerts.Add(new { severity = "warning", type = "ticket-drop", count = recentOrders.Count, message = "Average ticket in the last 3 days is materially below the selected-period average." });
        var expenses = await db.Expenses.AsNoTracking().Where(x => x.ExpenseDateUtc >= from).ToListAsync(ct);
        var dailyExpense = expenses.Count == 0 ? 0m : expenses.Sum(x => x.Amount) / days;
        if (revenue > 0 && dailyExpense * days > revenue * .5m) alerts.Add(new { severity = "critical", type = "expense-pressure", count = expenses.Count, message = "Recorded expenses consume more than half of POS revenue for the selected period." });
        return new { generatedAtUtc = now, periodDays = days, alerts };
    }

    private static decimal Median(IEnumerable<decimal> source)
    {
        var values = source.OrderBy(x => x).ToArray();
        if (values.Length == 0) return 0m;
        var middle = values.Length / 2;
        return values.Length % 2 == 0 ? (values[middle - 1] + values[middle]) / 2m : values[middle];
    }
}

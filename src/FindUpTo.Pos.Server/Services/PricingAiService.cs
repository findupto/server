using FindUpTo.Pos.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace FindUpTo.Pos.Server.Services;

public sealed class PricingAiService(CoreDbContext db)
{
    public async Task<object> GetRecommendationsAsync(int days, CancellationToken cancellationToken)
    {
        days = Math.Clamp(days, 7, 365);
        var now = DateTime.UtcNow;
        var from = now.AddDays(-days);
        var products = await db.Products.AsNoTracking().Where(x => x.Available).ToListAsync(cancellationToken);
        var orders = await db.Orders.AsNoTracking().Include(x => x.Items).Where(x => x.CreatedAtUtc >= from && x.Status != "Cancelled").ToListAsync(cancellationToken);
        var result = products.Select(product =>
        {
            var lines = orders.SelectMany(x => x.Items).Where(x => x.ProductId == product.Id).ToList();
            var units = lines.Sum(x => x.Quantity);
            var revenue = lines.Sum(x => x.LineTotal);
            var averagePrice = units > 0 ? revenue / units : product.Price;
            var dailyUnits = units / (decimal)days;
            var recentDays = Math.Min(30, days);
            var recentUnits = orders.Where(x => x.CreatedAtUtc >= now.AddDays(-recentDays)).SelectMany(x => x.Items).Where(x => x.ProductId == product.Id).Sum(x => x.Quantity);
            var recentDaily = recentUnits / (decimal)recentDays;
            var trend = dailyUnits <= 0 ? 0m : Math.Round((recentDaily - dailyUnits) / dailyUnits * 100m, 1);
            var suggestedPrice = product.Price;
            var action = "Hold";
            if (units == 0) action = "Review";
            else if (trend >= 20m) { action = "TestIncrease"; suggestedPrice = Math.Round(product.Price * 1.05m, 2); }
            else if (trend <= -20m) { action = "TestDecrease"; suggestedPrice = Math.Round(product.Price * 0.95m, 2); }
            return new { productId = product.Id, productName = product.Name, currentPrice = product.Price, suggestedPrice, action, unitsSold = units, revenue = Math.Round(revenue, 2), averageRealizedPrice = Math.Round(averagePrice, 2), dailyUnits = Math.Round(dailyUnits, 3), recentDailyUnits = Math.Round(recentDaily, 3), demandTrendPercent = trend };
        }).OrderByDescending(x => x.action != "Hold").ThenByDescending(x => Math.Abs(x.demandTrendPercent)).ToList();
        return new { generatedAtUtc = now, lookbackDays = days, recommendations = result };
    }

    public async Task<object> GetPromotionInsightsAsync(int days, CancellationToken cancellationToken)
    {
        days = Math.Clamp(days, 7, 365);
        var now = DateTime.UtcNow;
        var from = now.AddDays(-days);
        var promotions = await db.Promotions.AsNoTracking().ToListAsync(cancellationToken);
        var products = await db.Products.AsNoTracking().ToDictionaryAsync(x => x.Id, cancellationToken);
        var orders = await db.Orders.AsNoTracking().Include(x => x.Items).Where(x => x.CreatedAtUtc >= from && x.Status != "Cancelled").ToListAsync(cancellationToken);
        var result = promotions.Select(p =>
        {
            var affected = orders.SelectMany(x => x.Items).Where(i => (!p.ProductId.HasValue || i.ProductId == p.ProductId) && (!p.CategoryId.HasValue || (products.TryGetValue(i.ProductId, out var product) && product.CategoryId == p.CategoryId))).ToList();
            var units = affected.Sum(x => x.Quantity);
            var discount = affected.Sum(x => x.DiscountAmount);
            return new { promotionId = p.Id, promotionName = p.Name, active = p.Active, discountType = p.DiscountType, value = p.Value, unitsAffected = units, discountGranted = Math.Round(discount, 2), status = units == 0 ? "NoMeasuredUsage" : discount > 0 ? "GeneratingDiscount" : "Review" };
        }).ToList();
        return new { generatedAtUtc = now, lookbackDays = days, promotions = result };
    }
}

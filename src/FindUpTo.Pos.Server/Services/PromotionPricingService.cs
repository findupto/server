using FindUpTo.Pos.Server.Data;
using FindUpTo.Pos.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace FindUpTo.Pos.Server.Services;

public sealed class PromotionPricingService(CoreDbContext db)
{
    public async Task ApplyAsync(PosOrder order, IReadOnlyDictionary<int, Product> products)
    {
        var now = DateTime.UtcNow;
        var promotions = await db.Promotions.AsNoTracking()
            .Where(x => x.Active && (!x.StartsAtUtc.HasValue || x.StartsAtUtc <= now) && (!x.EndsAtUtc.HasValue || x.EndsAtUtc >= now))
            .ToListAsync();

        var categoryIds = products.Values.Select(x => x.CategoryId).Distinct().ToArray();
        var categoryPromotions = promotions.Where(x => x.CategoryId.HasValue && categoryIds.Contains(x.CategoryId.Value))
            .GroupBy(x => x.CategoryId!.Value).ToDictionary(g => g.Key, g => g.ToList());
        var productPromotions = promotions.Where(x => x.ProductId.HasValue).GroupBy(x => x.ProductId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());
        var globalPromotions = promotions.Where(x => !x.ProductId.HasValue && !x.CategoryId.HasValue).ToList();

        foreach (var item in order.Items)
        {
            var gross = Math.Round(item.UnitPrice * item.Quantity, 2);
            var candidates = new List<Promotion>();
            if (productPromotions.TryGetValue(item.ProductId, out var pp)) candidates.AddRange(pp);
            var categoryId = products[item.ProductId].CategoryId;
            if (categoryPromotions.TryGetValue(categoryId, out var cp)) candidates.AddRange(cp);
            candidates.AddRange(globalPromotions);

            var bestDiscount = candidates.Select(x => CalculateDiscount(x, gross)).DefaultIfEmpty(0m).Max();
            item.DiscountAmount = Math.Min(gross, Math.Round(bestDiscount, 2));
            item.LineTotal = Math.Round(gross - item.DiscountAmount, 2);
        }

        order.Subtotal = Math.Round(order.Items.Sum(x => x.UnitPrice * x.Quantity), 2);
        order.Discount = Math.Round(order.Items.Sum(x => x.DiscountAmount), 2);
        var taxable = Math.Max(0m, order.Subtotal - order.Discount);
        var taxRate = await db.BusinessSettings.Select(x => x.TaxPercent).SingleAsync();
        order.Tax = Math.Round(taxable * taxRate / 100m, 2);
        order.Total = Math.Round(taxable + order.Tax, 2);
    }

    private static decimal CalculateDiscount(Promotion promotion, decimal gross)
    {
        if (gross <= 0) return 0;
        if (promotion.DiscountType.Equals("Percent", StringComparison.OrdinalIgnoreCase))
            return gross * promotion.Value / 100m;
        return promotion.Value;
    }
}
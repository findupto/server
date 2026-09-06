using FindUpTo.Pos.Server.Data;
using FindUpTo.Pos.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace FindUpTo.Pos.Server.Services;

public sealed record InventorySaleResult(bool Success, int? ProductId = null, string? ProductName = null, decimal Required = 0, decimal Available = 0)
{
    public static InventorySaleResult Ok() => new(true);
}

public sealed class InventoryService(CoreDbContext db)
{
    public async Task<InventorySaleResult> DeductForSaleAsync(PosOrder order)
    {
        var quantities = order.Items.GroupBy(x => x.ProductId).ToDictionary(g => g.Key, g => g.Sum(x => (decimal)x.Quantity));
        if (quantities.Count == 0) return InventorySaleResult.Ok();

        var trackedIds = await db.ProductInventories.Where(x => quantities.Keys.Contains(x.ProductId) && x.TrackInventory).Select(x => x.ProductId).ToListAsync();
        foreach (var productId in trackedIds)
        {
            var quantity = quantities[productId];
            var changed = await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ProductInventories SET QuantityOnHand = QuantityOnHand - {quantity}, UpdatedAtUtc = {DateTime.UtcNow} WHERE ProductId = {productId} AND TrackInventory = 1 AND QuantityOnHand >= {quantity}");
            if (changed != 1)
            {
                var current = await db.ProductInventories.AsNoTracking().SingleOrDefaultAsync(x => x.ProductId == productId);
                var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(x => x.Id == productId);
                return new InventorySaleResult(false, productId, product?.Name ?? productId.ToString(), quantity, current?.QuantityOnHand ?? 0m);
            }

            var currentBalance = await db.ProductInventories.AsNoTracking().Where(x => x.ProductId == productId).Select(x => x.QuantityOnHand).SingleAsync();
            db.StockMovements.Add(new StockMovement { ProductId = productId, QuantityChange = -quantity, BalanceAfter = currentBalance, Type = "Sale", Reason = $"Order {order.Id}", Username = order.CreatedByUsername, CreatedAtUtc = DateTime.UtcNow });
        }

        await db.SaveChangesAsync();
        return InventorySaleResult.Ok();
    }
}

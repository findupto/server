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

        var tracked = await db.ProductInventories.Where(x => quantities.Keys.Contains(x.ProductId) && x.TrackInventory).ToDictionaryAsync(x => x.ProductId);
        foreach (var (productId, quantity) in quantities)
        {
            if (!tracked.TryGetValue(productId, out var inventory)) continue;

            decimal balance;
            if (db.Database.IsRelational())
            {
                var changed = await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ProductInventories SET QuantityOnHand = QuantityOnHand - {quantity}, UpdatedAtUtc = {DateTime.UtcNow} WHERE ProductId = {productId} AND TrackInventory = 1 AND QuantityOnHand >= {quantity}");
                if (changed != 1)
                {
                    var current = await db.ProductInventories.AsNoTracking().SingleOrDefaultAsync(x => x.ProductId == productId);
                    var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(x => x.Id == productId);
                    return new InventorySaleResult(false, productId, product?.Name ?? productId.ToString(), quantity, current?.QuantityOnHand ?? 0m);
                }
                balance = await db.ProductInventories.AsNoTracking().Where(x => x.ProductId == productId).Select(x => x.QuantityOnHand).SingleAsync();
            }
            else
            {
                if (inventory.QuantityOnHand < quantity)
                {
                    var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(x => x.Id == productId);
                    return new InventorySaleResult(false, productId, product?.Name ?? productId.ToString(), quantity, inventory.QuantityOnHand);
                }
                balance = inventory.QuantityOnHand - quantity;
                inventory.QuantityOnHand = balance;
                inventory.UpdatedAtUtc = DateTime.UtcNow;
            }

            db.StockMovements.Add(new StockMovement { ProductId = productId, QuantityChange = -quantity, BalanceAfter = balance, Type = "Sale", Reason = $"Order {order.Id}", Username = order.CreatedByUsername, CreatedAtUtc = DateTime.UtcNow });
        }

        await db.SaveChangesAsync();
        return InventorySaleResult.Ok();
    }

    public async Task<IReadOnlyList<int>> RestoreForCancellationAsync(PosOrder order, string username)
    {
        if (order.Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase)) return [];

        var reason = $"Order {order.Id}";
        var saleMovements = await db.StockMovements
            .Where(x => x.Type == "Sale" && x.Reason == reason)
            .ToListAsync();
        if (saleMovements.Count == 0) return [];

        var alreadyRestored = await db.StockMovements
            .Where(x => x.Type == "SaleReturn" && x.Reason == reason)
            .Select(x => x.ProductId)
            .ToHashSetAsync();

        var changedProducts = new List<int>();
        foreach (var group in saleMovements.GroupBy(x => x.ProductId))
        {
            if (alreadyRestored.Contains(group.Key)) continue;
            var quantity = -group.Sum(x => x.QuantityChange);
            if (quantity <= 0) continue;
            var inventory = await db.ProductInventories.SingleOrDefaultAsync(x => x.ProductId == group.Key);
            if (inventory is null) continue;
            inventory.QuantityOnHand += quantity;
            inventory.UpdatedAtUtc = DateTime.UtcNow;
            db.StockMovements.Add(new StockMovement
            {
                ProductId = group.Key,
                QuantityChange = quantity,
                BalanceAfter = inventory.QuantityOnHand,
                Type = "SaleReturn",
                Reason = reason,
                Username = username,
                CreatedAtUtc = DateTime.UtcNow
            });
            changedProducts.Add(group.Key);
        }

        if (changedProducts.Count > 0) await db.SaveChangesAsync();
        return changedProducts;
    }
}

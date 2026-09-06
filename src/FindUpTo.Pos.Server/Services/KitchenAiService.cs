using FindUpTo.Pos.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace FindUpTo.Pos.Server.Services;

public sealed class KitchenAiService(CoreDbContext db)
{
    public async Task<object> GetQueueAsync(int limit, CancellationToken cancellationToken)
    {
        limit = Math.Clamp(limit, 1, 200);
        var now = DateTime.UtcNow;
        var orders = await db.Orders.AsNoTracking()
            .Include(x => x.Items)
            .Where(x => x.Status == "New" || x.Status == "Accepted" || x.Status == "Preparing" || x.Status == "Ready")
            .OrderBy(x => x.CreatedAtUtc)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return orders.Select(o => new
        {
            orderId = o.Id,
            o.OrderType,
            o.Status,
            o.Notes,
            createdAtUtc = o.CreatedAtUtc,
            ageMinutes = Math.Max(0, (int)(now - o.CreatedAtUtc).TotalMinutes),
            priority = CalculatePriority(o.Status, o.OrderType, o.CreatedAtUtc, o.Items.Sum(i => i.Quantity)),
            itemCount = o.Items.Sum(i => i.Quantity),
            items = o.Items.Select(i => new { i.ProductName, i.Quantity, i.Notes }).ToList()
        });
    }

    public async Task<object> RecommendNextAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var orders = await db.Orders.AsNoTracking()
            .Include(x => x.Items)
            .Where(x => x.Status == "New" || x.Status == "Accepted" || x.Status == "Preparing")
            .ToListAsync(cancellationToken);

        var ranked = orders
            .Select(o => new
            {
                order = o,
                priority = CalculatePriority(o.Status, o.OrderType, o.CreatedAtUtc, o.Items.Sum(i => i.Quantity))
            })
            .OrderByDescending(x => x.priority)
            .ThenBy(x => x.order.CreatedAtUtc)
            .FirstOrDefault();

        if (ranked is null)
            return new { available = false, message = "Kitchen queue is empty." };

        var o = ranked.order;
        return new
        {
            available = true,
            orderId = o.Id,
            o.Status,
            o.OrderType,
            priority = ranked.priority,
            ageMinutes = Math.Max(0, (int)(now - o.CreatedAtUtc).TotalMinutes),
            reason = BuildReason(o.Status, o.OrderType, o.CreatedAtUtc, o.Items.Sum(i => i.Quantity)),
            items = o.Items.Select(i => new { i.ProductName, i.Quantity, i.Notes }).ToList()
        };
    }

    private static int CalculatePriority(string status, string orderType, DateTime createdAtUtc, int itemCount)
    {
        var age = Math.Clamp((int)(DateTime.UtcNow - createdAtUtc).TotalMinutes, 0, 240);
        var score = age + Math.Min(itemCount, 20);
        if (status == "New") score += 35;
        else if (status == "Accepted") score += 20;
        else if (status == "Preparing") score += 10;
        if (orderType == "Delivery") score += 15;
        else if (orderType == "Dine In") score += 10;
        return score;
    }

    private static string BuildReason(string status, string orderType, DateTime createdAtUtc, int itemCount)
    {
        var age = Math.Max(0, (int)(DateTime.UtcNow - createdAtUtc).TotalMinutes);
        return $"{status} order, {age} minutes old, {itemCount} item(s), {orderType} priority.";
    }
}

using FindUpTo.Pos.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace FindUpTo.Pos.Server.Services;

public sealed class WorkforceAiService(CoreDbContext db)
{
    public async Task<object> AnalyzeAsync(int days, CancellationToken cancellationToken)
    {
        days = Math.Clamp(days, 7, 365);
        var now = DateTime.UtcNow;
        var from = now.AddDays(-days);
        var orders = await db.Orders.AsNoTracking().Where(x => x.CreatedAtUtc >= from).ToListAsync(cancellationToken);
        var users = await db.Users.AsNoTracking().ToListAsync(cancellationToken);
        var rows = users.Select(u =>
        {
            var name = u.Username;
            var mine = orders.Where(x => x.CreatedByUsername == name).ToList();
            var completed = mine.Count(x => x.Status == "Completed" || x.Status == "Served");
            var cancelled = mine.Count(x => x.Status == "Cancelled");
            var revenue = mine.Where(x => x.Status != "Cancelled").Sum(x => x.Total);
            var cancellationRate = mine.Count == 0 ? 0m : cancelled * 100m / mine.Count;
            var avgValue = completed == 0 ? 0m : revenue / completed;
            var alert = cancellationRate >= 20m ? "HighCancellation" : completed == 0 && mine.Count > 0 ? "NoCompletedOrders" : "Normal";
            return new { username = name, role = u.Role, ordersHandled = mine.Count, completedOrders = completed, cancelledOrders = cancelled, cancellationRatePercent = Math.Round(cancellationRate, 1), revenueHandled = Math.Round(revenue, 2), averageCompletedOrderValue = Math.Round(avgValue, 2), alert };
        }).OrderByDescending(x => x.ordersHandled).ToList();
        return new { generatedAtUtc = now, lookbackDays = days, staff = rows, alerts = rows.Where(x => x.alert != "Normal").ToList() };
    }
}

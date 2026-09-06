using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using FindUpTo.Pos.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace FindUpTo.Pos.Server.Services;

public sealed class AdvancedPremiumIntelligenceService(CoreDbContext db, AiProviderService providers, IHttpClientFactory httpClientFactory)
{
    public async Task<object> GetReplenishmentPlanAsync(int historyDays, int targetDays, CancellationToken ct)
    {
        historyDays = Math.Clamp(historyDays, 7, 365);
        targetDays = Math.Clamp(targetDays, 3, 60);
        var fromDate = DateTime.UtcNow.Date.AddDays(-historyDays);
        var sales = await (from item in db.OrderItems.AsNoTracking()
                           join order in db.Orders.AsNoTracking() on item.PosOrderId equals order.Id
                           where order.CreatedAtUtc >= fromDate && order.Status != "Cancelled"
                           group item by new { item.ProductId, item.ProductName } into g
                           select new { g.Key.ProductId, g.Key.ProductName, units = g.Sum(x => x.Quantity) }).ToListAsync(ct);
        var stock = await db.ProductInventories.AsNoTracking().ToDictionaryAsync(x => x.ProductId, ct);
        var products = await db.Products.AsNoTracking().Where(x => x.Available).ToDictionaryAsync(x => x.Id, ct);
        var plan = sales.Select(x =>
        {
            var averageDaily = x.units / (decimal)historyDays;
            var inventory = stock.GetValueOrDefault(x.ProductId);
            var available = inventory?.QuantityOnHand ?? 0m;
            var reorderLevel = inventory?.ReorderLevel ?? 0m;
            var target = Math.Ceiling(averageDaily * targetDays);
            var recommended = Math.Max(0m, target - available);
            var urgency = available <= 0m ? "critical" : available <= reorderLevel ? "high" : recommended > 0m ? "planned" : "healthy";
            return new { x.ProductId, x.ProductName, price = products.GetValueOrDefault(x.ProductId)?.Price ?? 0m, unitsSold = x.units, averageDailyUnits = Math.Round(averageDaily, 3), currentStock = available, reorderLevel, targetStock = target, recommendedOrderQuantity = recommended, daysOfCover = averageDaily <= 0m ? (decimal?)null : Math.Round(available / averageDaily, 1), urgency };
        }).OrderByDescending(x => x.urgency == "critical").ThenByDescending(x => x.recommendedOrderQuantity).ToList();
        return new { generatedAtUtc = DateTime.UtcNow, historyDays, targetDays, items = plan, recommendedItems = plan.Count(x => x.recommendedOrderQuantity > 0m) };
    }

    public async Task<object> GetProfitabilityAsync(int days, CancellationToken ct)
    {
        days = Math.Clamp(days, 7, 365);
        var fromDate = DateTime.UtcNow.Date.AddDays(-days + 1);
        var rows = await (from item in db.OrderItems.AsNoTracking()
                          join order in db.Orders.AsNoTracking() on item.PosOrderId equals order.Id
                          where order.CreatedAtUtc >= fromDate && order.Status != "Cancelled"
                          group item by item.ProductName into g
                          select new { product = g.Key, units = g.Sum(x => x.Quantity), revenue = g.Sum(x => x.LineTotal - x.DiscountAmount), cost = g.Sum(x => x.UnitCost * x.Quantity) }).ToListAsync(ct);
        var totalRevenue = rows.Sum(x => x.revenue);
        var totalCost = rows.Sum(x => x.cost);
        var ranked = rows.Select(x => new { x.product, x.units, revenue = Math.Round(x.revenue, 2), cost = Math.Round(x.cost, 2), grossProfit = Math.Round(x.revenue - x.cost, 2), margin = x.revenue == 0m ? 0m : Math.Round((x.revenue - x.cost) / x.revenue, 4) }).OrderByDescending(x => x.grossProfit).ToList();
        return new { generatedAtUtc = DateTime.UtcNow, periodDays = days, revenue = Math.Round(totalRevenue, 2), cost = Math.Round(totalCost, 2), grossProfit = Math.Round(totalRevenue - totalCost, 2), grossMargin = totalRevenue == 0m ? 0m : Math.Round((totalRevenue - totalCost) / totalRevenue, 4), products = ranked };
    }

    public async Task<object> GetRetentionOpportunitiesAsync(int days, CancellationToken ct)
    {
        days = Math.Clamp(days, 30, 730);
        var now = DateTime.UtcNow;
        var fromDate = now.AddDays(-days);
        var customers = await db.Customers.AsNoTracking().ToListAsync(ct);
        var orders = await db.Orders.AsNoTracking().Where(x => x.CustomerId.HasValue && x.CreatedAtUtc >= fromDate && x.Status != "Cancelled").ToListAsync(ct);
        var opportunities = customers.Select(c =>
        {
            var customerOrders = orders.Where(x => x.CustomerId == c.Id).OrderByDescending(x => x.CreatedAtUtc).ToList();
            var last = customerOrders.FirstOrDefault()?.CreatedAtUtc;
            var spend = customerOrders.Sum(x => x.Total);
            var average = customerOrders.Count == 0 ? 0m : spend / customerOrders.Count;
            var daysSince = last.HasValue ? (now - last.Value).TotalDays : days;
            var priority = customerOrders.Count >= 3 && daysSince > 30 ? "high" : customerOrders.Count >= 1 && daysSince > 45 ? "medium" : "low";
            var action = priority == "high" ? "win-back" : priority == "medium" ? "re-engage" : "nurture";
            return new { customerId = c.Id, customerName = c.Name, phone = c.Phone, orders = customerOrders.Count, spend = Math.Round(spend, 2), averageOrder = Math.Round(average, 2), daysSinceLastOrder = Math.Round(daysSince, 1), priority, action };
        }).Where(x => x.priority != "low").OrderByDescending(x => x.priority == "high").ThenByDescending(x => x.spend).Take(500).ToList();
        return new { generatedAtUtc = now, periodDays = days, opportunities, highPriority = opportunities.Count(x => x.priority == "high"), mediumPriority = opportunities.Count(x => x.priority == "medium") };
    }

    public async Task<object> GenerateExecutiveAiBriefAsync(int days, CancellationToken ct)
    {
        days = Math.Clamp(days, 7, 365);
        var premium = new PremiumIntelligenceService(db);
        var executive = await premium.GetExecutiveDashboardAsync(days, ct);
        var alerts = await premium.GetSmartAlertsAsync(days, ct);
        var provider = await providers.ResolveAsync(ct);
        if (provider.Provider == "none")
            return new { success = false, provider = "none", message = "No AI provider configured. Premium deterministic analytics remain available.", executive, alerts };
        var key = await providers.GetApiKeyAsync(ct);
        if (provider.RequiresApiKey && string.IsNullOrWhiteSpace(key))
            return new { success = false, provider = provider.Provider, message = "The selected AI provider requires an API key.", executive, alerts };
        var prompt = $"Create a concise executive briefing for a POS business from the following live analytics. Give: 1) three most important findings, 2) three concrete actions ranked by expected business impact, 3) risks, 4) opportunities. Never invent facts or numbers; use only supplied data. Analytics: {JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(new { executive, alerts }))}";
        var client = httpClientFactory.CreateClient("business-ai");
        var body = new JsonObject { ["model"] = provider.Model, ["input"] = prompt, ["store"] = false, ["max_output_tokens"] = 900 };
        using var request = AiProviderService.CreateRequest(provider, "/v1/responses");
        if (provider.RequiresApiKey) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request, ct);
        var content = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) return new { success = false, provider = provider.Provider, error = $"AI provider returned {(int)response.StatusCode}.", executive, alerts };
        var json = JsonNode.Parse(content)?.AsObject();
        var text = json?["output"]?.AsArray().SelectMany(x => x?["content"]?.AsArray() ?? []).Select(x => x?["text"]?.GetValue<string>()).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? json?["output_text"]?.GetValue<string>();
        return new { success = true, provider = provider.Provider, model = provider.Model, briefing = text ?? "AI returned no briefing text.", executive, alerts };
    }
}

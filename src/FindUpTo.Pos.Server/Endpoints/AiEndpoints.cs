using System.Security.Claims;
using FindUpTo.Pos.Server.Data;
using FindUpTo.Pos.Server.Services;
using Microsoft.EntityFrameworkCore;

namespace FindUpTo.Pos.Server.Endpoints;

public static class AiEndpoints
{
    private static readonly string[] AllowedRoles = ["Owner", "Manager", "Admin", "Counter", "Kitchen"];

    public static void MapAiEndpoints(this WebApplication app)
    {
        app.MapPost("/api/ai/operate", async (AiOperateRequest input, ClaimsPrincipal user, BusinessAiService ai, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(input.Message)) return Results.BadRequest(new { message = "message is required." });
            if (input.Message.Length > 4000) return Results.BadRequest(new { message = "message is limited to 4000 characters." });
            try { return Results.Ok(await ai.RunAsync(user, input.Message, cancellationToken)); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
            catch (ArgumentException ex) { return Results.BadRequest(new { message = ex.Message }); }
            catch (InvalidOperationException ex) { return Results.Problem(ex.Message, statusCode: 503); }
        }).RequireAuthorization(p => p.RequireRole(AllowedRoles));

        app.MapGet("/api/ai/status", async (AiProviderService providers, CancellationToken cancellationToken) =>
        {
            var provider = await providers.ResolveAsync(cancellationToken);
            return Results.Ok(new { enabled = provider.Provider != "none", provider = provider.Provider, baseUrl = provider.BaseUrl, model = provider.Model, requiresApiKey = provider.RequiresApiKey, local = provider.Local, status = provider.Status, capabilities = new[] { "sales", "payments", "inventory", "purchasing", "receiving", "product-updates", "reports", "receipt-printing", "rider-tracking", "delivery-tracking", "routing-eta", "customer-crm", "finance-cashflow", "kitchen-kds" } });
        }).RequireAuthorization(p => p.RequireRole(AllowedRoles));

        app.MapGet("/api/ai/finance/summary", async (CoreDbContext db, int? days, CancellationToken cancellationToken) =>
        {
            var period = Math.Clamp(days ?? 30, 1, 3650);
            var from = DateTime.UtcNow.AddDays(-period);
            var orders = await db.Orders.AsNoTracking().Where(x => x.CreatedAtUtc >= from && x.Status != "Cancelled").ToListAsync(cancellationToken);
            var payments = await db.Payments.AsNoTracking().Where(x => x.CreatedAtUtc >= from && x.Status == "Paid").ToListAsync(cancellationToken);
            var expenses = await db.Expenses.AsNoTracking().Where(x => x.ExpenseDateUtc >= from).ToListAsync(cancellationToken);
            var revenue = Math.Round(payments.Sum(x => x.AmountPaid - x.ChangeAmount), 2);
            var expensesTotal = Math.Round(expenses.Sum(x => x.Amount), 2);
            var netCash = Math.Round(revenue - expensesTotal, 2);
            var orderValue = Math.Round(orders.Sum(x => x.Total), 2);
            var paymentByMethod = payments.GroupBy(x => x.Method).Select(g => new { method = g.Key, amount = Math.Round(g.Sum(x => x.AmountPaid - x.ChangeAmount), 2), count = g.Count() }).OrderByDescending(x => x.amount).ToList();
            var expenseByCategory = expenses.GroupBy(x => x.Category).Select(g => new { category = g.Key, amount = Math.Round(g.Sum(x => x.Amount), 2), count = g.Count() }).OrderByDescending(x => x.amount).ToList();
            var credit = await db.CustomerCreditAccounts.AsNoTracking().Where(x => x.Active).ToListAsync(cancellationToken);
            return Results.Ok(new { periodDays = period, fromUtc = from, toUtc = DateTime.UtcNow, orderCount = orders.Count, orderValue, collectedRevenue = revenue, expenses = expensesTotal, netCash, averageOrderValue = orders.Count == 0 ? 0m : Math.Round(orderValue / orders.Count, 2), outstandingCustomerCredit = Math.Round(credit.Sum(x => x.Balance), 2), paymentByMethod, expenseByCategory, alerts = BuildFinanceAlerts(revenue, expensesTotal, credit.Sum(x => x.Balance), orders.Count) });
        }).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin"));

        app.MapGet("/api/ai/kitchen/queue", async (int? limit, CoreDbContext db, CancellationToken cancellationToken) => Results.Ok(await new KitchenAiService(db).GetQueueAsync(limit ?? 100, cancellationToken))).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin", "Kitchen"));
        app.MapGet("/api/ai/kitchen/recommend-next", async (CoreDbContext db, CancellationToken cancellationToken) => Results.Ok(await new KitchenAiService(db).RecommendNextAsync(cancellationToken))).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin", "Kitchen"));

        app.MapGet("/api/ai/providers/discover", async (AiProviderService providers, CancellationToken cancellationToken) => Results.Ok(await providers.DiscoverAsync(cancellationToken))).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin"));

        app.MapPut("/api/ai/configuration", async (AiConfigurationRequest input, ClaimsPrincipal user, AiProviderService providers, CancellationToken cancellationToken) =>
        {
            if (user.FindFirstValue(ClaimTypes.Role) != "Owner") return Results.Forbid();
            var provider = input.Provider.Trim().ToLowerInvariant();
            var allowed = new[] { "auto", "openai", "ollama", "lmstudio", "llamacpp" };
            if (!allowed.Contains(provider)) return Results.BadRequest("Unsupported AI provider.");
            if (provider == "openai" && string.IsNullOrWhiteSpace(input.ApiKey)) return Results.BadRequest("API key is required for a purchased OpenAI model.");
            if (provider is "ollama" or "lmstudio" or "llamacpp" && string.IsNullOrWhiteSpace(input.Model)) return Results.BadRequest("A local model name is required.");
            await providers.ConfigureAsync(provider, input.Model ?? "", input.ApiKey, input.BaseUrl, cancellationToken);
            return Results.Ok(new { success = true, message = "AI configuration saved. API keys are never returned by this endpoint." });
        }).RequireAuthorization(p => p.RequireRole("Owner"));
    }

    private static object[] BuildFinanceAlerts(decimal revenue, decimal expenses, decimal credit, int orders)
    {
        var alerts = new List<object>();
        if (orders == 0) alerts.Add(new { level = "warning", code = "NO_SALES", message = "No non-cancelled orders were recorded in the selected period." });
        if (expenses > revenue && revenue > 0) alerts.Add(new { level = "critical", code = "NEGATIVE_CASHFLOW", message = "Recorded expenses exceed collected revenue in the selected period." });
        if (credit > 0) alerts.Add(new { level = "info", code = "OUTSTANDING_CREDIT", message = $"Customer credit outstanding: {credit:0.00}." });
        return alerts.ToArray();
    }

    public sealed record AiOperateRequest(string Message);
    public sealed record AiConfigurationRequest(string Provider, string? Model = null, string? ApiKey = null, string? BaseUrl = null);
}

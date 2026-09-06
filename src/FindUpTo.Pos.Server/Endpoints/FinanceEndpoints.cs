using System.Security.Claims;
using FindUpTo.Pos.Server.Data;
using FindUpTo.Pos.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace FindUpTo.Pos.Server.Endpoints;

public static class FinanceEndpoints
{
    private static readonly string[] Roles = ["Owner", "Manager", "Admin"];
    private static readonly string[] PaymentMethods = ["Cash", "Card", "Online", "Bank"];

    public static void MapFinanceEndpoints(this WebApplication app)
    {
        app.MapGet("/api/expenses", async (CoreDbContext db, DateTime? fromUtc, DateTime? toUtc, string? category) =>
        {
            var from = fromUtc ?? DateTime.UtcNow.Date;
            var to = toUtc ?? DateTime.UtcNow;
            if (to < from) return Results.BadRequest("toUtc must be greater than or equal to fromUtc.");
            var q = db.Expenses.AsNoTracking().Where(x => x.ExpenseDateUtc >= from && x.ExpenseDateUtc <= to);
            if (!string.IsNullOrWhiteSpace(category)) q = q.Where(x => x.Category == category.Trim());
            return Results.Ok(await q.OrderByDescending(x => x.ExpenseDateUtc).Take(1000).ToListAsync());
        }).RequireAuthorization(p => p.RequireRole(Roles));

        app.MapPost("/api/expenses", async (ExpenseRequest input, ClaimsPrincipal user, CoreDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(input.Category) || input.Category.Trim().Length > 64) return Results.BadRequest("Category is required and must be 64 characters or fewer.");
            if (string.IsNullOrWhiteSpace(input.Description) || input.Description.Trim().Length > 500) return Results.BadRequest("Description is required and must be 500 characters or fewer.");
            if (input.Amount <= 0 || input.Amount > 1000000000m) return Results.BadRequest("Amount must be greater than zero.");
            var method = PaymentMethods.FirstOrDefault(x => x.Equals(input.PaymentMethod?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (method is null) return Results.BadRequest("Payment method must be Cash, Card, Online, or Bank.");
            var expense = new Expense { Category = input.Category.Trim(), Description = input.Description.Trim(), Amount = Math.Round(input.Amount, 2), ExpenseDateUtc = input.ExpenseDateUtc ?? DateTime.UtcNow, PaymentMethod = method, Reference = input.Reference?.Trim() ?? "", CreatedByUsername = user.Identity?.Name ?? "unknown" };
            db.Expenses.Add(expense);
            await db.SaveChangesAsync();
            await AuditEndpoints.WriteAsync(db, user, "Created", "Expense", expense.Id.ToString(), $"Amount={expense.Amount:0.00};Category={expense.Category}");
            return Results.Created($"/api/expenses/{expense.Id}", expense);
        }).RequireAuthorization(p => p.RequireRole(Roles));

        app.MapDelete("/api/expenses/{id:int}", async (int id, ClaimsPrincipal user, CoreDbContext db) =>
        {
            var expense = await db.Expenses.FindAsync(id);
            if (expense is null) return Results.NotFound();
            db.Expenses.Remove(expense);
            await db.SaveChangesAsync();
            await AuditEndpoints.WriteAsync(db, user, "Deleted", "Expense", id.ToString(), $"Amount={expense.Amount:0.00};Category={expense.Category}");
            return Results.NoContent();
        }).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin"));
    }
}

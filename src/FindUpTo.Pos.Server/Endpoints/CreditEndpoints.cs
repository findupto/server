using System.Security.Claims;
using FindUpTo.Pos.Server.Data;
using FindUpTo.Pos.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace FindUpTo.Pos.Server.Endpoints;

public static class CreditEndpoints
{
    private static readonly string[] Roles = ["Owner", "Manager", "Admin"];

    public static void MapCreditEndpoints(this WebApplication app)
    {
        app.MapGet("/api/credit/accounts", async (CoreDbContext db) =>
        {
            var rows = await db.CustomerCreditAccounts.AsNoTracking().Join(db.Customers.AsNoTracking(), a => a.CustomerId, c => c.Id,
                (a, c) => new { accountId = a.Id, customerId = c.Id, customerName = c.Name, phone = c.Phone, creditLimit = a.CreditLimit, balance = a.Balance, availableCredit = a.CreditLimit - a.Balance, active = a.Active, updatedAtUtc = a.UpdatedAtUtc })
                .OrderByDescending(x => x.balance).ToListAsync();
            return Results.Ok(rows);
        }).RequireAuthorization(p => p.RequireRole(Roles));

        app.MapGet("/api/customers/{customerId:int}/credit", async (int customerId, CoreDbContext db) =>
        {
            var customer = await db.Customers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == customerId);
            if (customer is null) return Results.NotFound();
            var account = await db.CustomerCreditAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.CustomerId == customerId);
            return Results.Ok(new { customerId, customerName = customer.Name, phone = customer.Phone, account });
        }).RequireAuthorization(p => p.RequireRole(Roles));

        app.MapPut("/api/customers/{customerId:int}/credit", async (int customerId, CreditAccountRequest input, ClaimsPrincipal user, CoreDbContext db) =>
        {
            if (input.CreditLimit < 0 || input.CreditLimit > 1000000000m) return Results.BadRequest("Credit limit must be between zero and one billion.");
            if (!await db.Customers.AnyAsync(x => x.Id == customerId)) return Results.NotFound();
            var account = await db.CustomerCreditAccounts.SingleOrDefaultAsync(x => x.CustomerId == customerId);
            if (account is null) { account = new CustomerCreditAccount { CustomerId = customerId }; db.CustomerCreditAccounts.Add(account); }
            if (input.CreditLimit < account.Balance) return Results.BadRequest("Credit limit cannot be below the outstanding balance.");
            account.CreditLimit = Math.Round(input.CreditLimit, 2); account.Active = input.Active; account.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
            await AuditEndpoints.WriteAsync(db, user, "Updated", "CustomerCreditAccount", account.Id.ToString(), $"CustomerId={customerId};Limit={account.CreditLimit:0.00};Active={account.Active}");
            return Results.Ok(new { account.Id, account.CustomerId, account.CreditLimit, account.Balance, availableCredit = account.CreditLimit - account.Balance, account.Active });
        }).RequireAuthorization(p => p.RequireRole(Roles));

        app.MapPost("/api/orders/{orderId:int}/credit", async (int orderId, ClaimsPrincipal user, CoreDbContext db) =>
        {
            var order = await db.Orders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == orderId);
            if (order is null) return Results.NotFound();
            if (!order.CustomerId.HasValue) return Results.BadRequest("Credit sales require a customer.");
            if (order.Status == "Cancelled") return Results.BadRequest("Cancelled orders cannot be charged to credit.");
            if (order.Total <= 0) return Results.BadRequest("Order total must be greater than zero.");
            var account = await db.CustomerCreditAccounts.SingleOrDefaultAsync(x => x.CustomerId == order.CustomerId.Value);
            if (account is null || !account.Active) return Results.BadRequest("Customer credit account is not active.");
            var alreadyCharged = await db.CreditTransactions.AnyAsync(x => x.PosOrderId == orderId && x.Type == "Charge");
            if (alreadyCharged) return Results.Conflict("Order has already been charged to credit.");
            if (account.Balance + order.Total > account.CreditLimit) return Results.BadRequest(new { message = "Credit limit exceeded.", balance = account.Balance, creditLimit = account.CreditLimit, requested = order.Total });
            account.Balance += order.Total; account.UpdatedAtUtc = DateTime.UtcNow;
            db.CreditTransactions.Add(new CreditTransaction { CustomerCreditAccountId = account.Id, PosOrderId = orderId, Amount = order.Total, Type = "Charge", CreatedByUsername = user.Identity?.Name ?? "unknown", Reference = $"Order-{orderId}" });
            await db.SaveChangesAsync();
            await AuditEndpoints.WriteAsync(db, user, "CreditCharge", "CustomerCreditAccount", account.Id.ToString(), $"OrderId={orderId};Amount={order.Total:0.00}");
            return Results.Ok(new { customerId = order.CustomerId, orderId, charged = order.Total, balance = account.Balance, availableCredit = account.CreditLimit - account.Balance });
        }).RequireAuthorization(p => p.RequireRole(Roles));

        app.MapPost("/api/customers/{customerId:int}/credit/payment", async (int customerId, CreditPaymentRequest input, ClaimsPrincipal user, CoreDbContext db) =>
        {
            if (input.Amount <= 0 || input.Amount > 1000000000m) return Results.BadRequest("Payment amount must be greater than zero.");
            var account = await db.CustomerCreditAccounts.SingleOrDefaultAsync(x => x.CustomerId == customerId);
            if (account is null) return Results.NotFound();
            if (input.Amount > account.Balance) return Results.BadRequest("Payment cannot exceed the outstanding balance.");
            account.Balance -= Math.Round(input.Amount, 2); account.UpdatedAtUtc = DateTime.UtcNow;
            db.CreditTransactions.Add(new CreditTransaction { CustomerCreditAccountId = account.Id, Amount = Math.Round(input.Amount, 2), Type = "Payment", Reference = input.Reference?.Trim() ?? "", Notes = input.Notes?.Trim() ?? "", CreatedByUsername = user.Identity?.Name ?? "unknown" });
            await db.SaveChangesAsync();
            await AuditEndpoints.WriteAsync(db, user, "CreditPayment", "CustomerCreditAccount", account.Id.ToString(), $"Amount={input.Amount:0.00}");
            return Results.Ok(new { customerId, paid = input.Amount, balance = account.Balance, availableCredit = account.CreditLimit - account.Balance });
        }).RequireAuthorization(p => p.RequireRole(Roles));

        app.MapGet("/api/customers/{customerId:int}/credit/transactions", async (int customerId, CoreDbContext db) =>
        {
            var account = await db.CustomerCreditAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.CustomerId == customerId);
            if (account is null) return Results.NotFound();
            var rows = await db.CreditTransactions.AsNoTracking().Where(x => x.CustomerCreditAccountId == account.Id).OrderByDescending(x => x.CreatedAtUtc).Take(500).ToListAsync();
            return Results.Ok(rows);
        }).RequireAuthorization(p => p.RequireRole(Roles));

        app.MapGet("/api/reports/credit-aging", async (CoreDbContext db) =>
        {
            var accounts = await db.CustomerCreditAccounts.AsNoTracking().Join(db.Customers.AsNoTracking(), a => a.CustomerId, c => c.Id, (a, c) => new { a.Id, a.CustomerId, customerName = c.Name, phone = c.Phone, a.Balance }).Where(x => x.Balance > 0).ToListAsync();
            var charges = await db.CreditTransactions.AsNoTracking().Where(x => x.Type == "Charge").ToListAsync();
            var now = DateTime.UtcNow;
            var rows = accounts.Select(a =>
            {
                var tx = charges.Where(x => x.CustomerCreditAccountId == a.Id).OrderBy(x => x.CreatedAtUtc).ToList();
                var current = tx.Where(x => (now - x.CreatedAtUtc).TotalDays < 30).Sum(x => x.Amount);
                var d30 = tx.Where(x => (now - x.CreatedAtUtc).TotalDays >= 30 && (now - x.CreatedAtUtc).TotalDays < 60).Sum(x => x.Amount);
                var d60 = tx.Where(x => (now - x.CreatedAtUtc).TotalDays >= 60 && (now - x.CreatedAtUtc).TotalDays < 90).Sum(x => x.Amount);
                var d90 = tx.Where(x => (now - x.CreatedAtUtc).TotalDays >= 90).Sum(x => x.Amount);
                return new { customerId = a.CustomerId, customerName = a.customerName, phone = a.phone, balance = a.Balance, current = Math.Round(current, 2), days30 = Math.Round(d30, 2), days60 = Math.Round(d60, 2), days90Plus = Math.Round(d90, 2) };
            }).ToList();
            return Results.Ok(new { asOfUtc = now, totalOutstanding = Math.Round(rows.Sum(x => x.balance), 2), rows });
        }).RequireAuthorization(p => p.RequireRole(Roles));
    }
}

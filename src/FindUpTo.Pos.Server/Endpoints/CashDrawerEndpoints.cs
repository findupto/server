using System.Security.Claims;
using FindUpTo.Pos.Server.Data;
using FindUpTo.Pos.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace FindUpTo.Pos.Server.Endpoints;

public static class CashDrawerEndpoints
{
    public static void MapCashDrawerEndpoints(this WebApplication app)
    {
        var staff = new[] { "Owner", "Manager", "Admin", "Counter" };

        app.MapGet("/api/cash-drawer/current", async (CoreDbContext db) =>
        {
            var session = await db.CashDrawerSessions.AsNoTracking().Include(x => x.Movements)
                .SingleOrDefaultAsync(x => x.Status == "Open");
            if (session is null) return Results.NotFound();
            var cashSales = await db.Payments.AsNoTracking().Where(x => x.Status == "Paid" && x.Method == "Cash" && x.CreatedAtUtc >= session.OpenedAtUtc).SumAsync(x => (decimal?)x.AmountPaid) ?? 0m;
            var cashChange = await db.Payments.AsNoTracking().Where(x => x.Status == "Paid" && x.Method == "Cash" && x.CreatedAtUtc >= session.OpenedAtUtc).SumAsync(x => (decimal?)x.ChangeAmount) ?? 0m;
            var movementNet = session.Movements.Sum(x => x.Amount);
            return Results.Ok(new { session, cashSales, cashChange, movementNet, expectedCash = session.OpeningFloat + cashSales - cashChange + movementNet });
        }).RequireAuthorization();

        app.MapPost("/api/cash-drawer/open", async (OpenCashDrawerRequest input, ClaimsPrincipal user, CoreDbContext db) =>
        {
            if (input.OpeningFloat < 0) return Results.BadRequest("Opening float cannot be negative.");
            if (await db.CashDrawerSessions.AnyAsync(x => x.Status == "Open")) return Results.Conflict("A cash drawer is already open.");
            var session = new CashDrawerSession { OpeningFloat = input.OpeningFloat, OpenedByUsername = user.Identity?.Name ?? "unknown", Notes = input.Notes.Trim() };
            db.CashDrawerSessions.Add(session); await db.SaveChangesAsync();
            await AuditEndpoints.WriteAsync(db, user, "Opened", "CashDrawerSession", session.Id.ToString(), $"OpeningFloat={session.OpeningFloat:0.00}");
            return Results.Created($"/api/cash-drawer/sessions/{session.Id}", session);
        }).RequireAuthorization(p => p.RequireRole(staff));

        app.MapPost("/api/cash-drawer/movement", async (CashDrawerMovementRequest input, ClaimsPrincipal user, CoreDbContext db) =>
        {
            var session = await db.CashDrawerSessions.SingleOrDefaultAsync(x => x.Status == "Open");
            if (session is null) return Results.Conflict("No cash drawer is open.");
            if (input.Amount == 0) return Results.BadRequest("Movement amount cannot be zero.");
            var type = input.Type.Trim();
            if (!type.Equals("CashIn", StringComparison.OrdinalIgnoreCase) && !type.Equals("CashOut", StringComparison.OrdinalIgnoreCase)) return Results.BadRequest("Type must be CashIn or CashOut.");
            var amount = type.Equals("CashOut", StringComparison.OrdinalIgnoreCase) ? -Math.Abs(input.Amount) : Math.Abs(input.Amount);
            var movement = new CashDrawerMovement { CashDrawerSessionId = session.Id, Amount = amount, Type = type, Reason = input.Reason.Trim(), Username = user.Identity?.Name ?? "unknown" };
            db.CashDrawerMovements.Add(movement); await db.SaveChangesAsync();
            await AuditEndpoints.WriteAsync(db, user, type, "CashDrawerMovement", movement.Id.ToString(), $"Amount={amount:0.00};Reason={movement.Reason}");
            return Results.Ok(movement);
        }).RequireAuthorization(p => p.RequireRole(staff));

        app.MapPost("/api/cash-drawer/close", async (CloseCashDrawerRequest input, ClaimsPrincipal user, CoreDbContext db) =>
        {
            if (input.ClosingAmount < 0) return Results.BadRequest("Closing amount cannot be negative.");
            var session = await db.CashDrawerSessions.SingleOrDefaultAsync(x => x.Status == "Open");
            if (session is null) return Results.NotFound();
            var cashSales = await db.Payments.AsNoTracking().Where(x => x.Status == "Paid" && x.Method == "Cash" && x.CreatedAtUtc >= session.OpenedAtUtc).SumAsync(x => (decimal?)x.AmountPaid) ?? 0m;
            var cashChange = await db.Payments.AsNoTracking().Where(x => x.Status == "Paid" && x.Method == "Cash" && x.CreatedAtUtc >= session.OpenedAtUtc).SumAsync(x => (decimal?)x.ChangeAmount) ?? 0m;
            var movementNet = await db.CashDrawerMovements.Where(x => x.CashDrawerSessionId == session.Id).SumAsync(x => (decimal?)x.Amount) ?? 0m;
            var expected = session.OpeningFloat + cashSales - cashChange + movementNet;
            session.ClosingAmount = input.ClosingAmount; session.Status = "Closed"; session.ClosedByUsername = user.Identity?.Name ?? "unknown"; session.ClosedAtUtc = DateTime.UtcNow; session.Notes = string.IsNullOrWhiteSpace(input.Notes) ? session.Notes : input.Notes.Trim();
            await db.SaveChangesAsync();
            await AuditEndpoints.WriteAsync(db, user, "Closed", "CashDrawerSession", session.Id.ToString(), $"Expected={expected:0.00};Actual={input.ClosingAmount:0.00};Variance={input.ClosingAmount - expected:0.00}");
            return Results.Ok(new { session, expectedCash = expected, actualCash = input.ClosingAmount, variance = input.ClosingAmount - expected });
        }).RequireAuthorization(p => p.RequireRole(staff));

        app.MapGet("/api/cash-drawer/sessions", async (CoreDbContext db, int take = 50) =>
        {
            take = Math.Clamp(take, 1, 200);
            return Results.Ok(await db.CashDrawerSessions.AsNoTracking().OrderByDescending(x => x.OpenedAtUtc).Take(take).ToListAsync());
        }).RequireAuthorization(p => p.RequireRole(staff));
    }
}

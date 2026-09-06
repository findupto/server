using FindUpTo.Pos.Server.Data;
using FindUpTo.Pos.Server.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace FindUpTo.Pos.Server.Endpoints;

public static class PromotionEndpoints
{
    public static void MapPromotionEndpoints(this WebApplication app)
    {
        app.MapGet("/api/promotions", async (CoreDbContext db) =>
        {
            var now = DateTime.UtcNow;
            var promotions = await db.Promotions.AsNoTracking()
                .Where(x => x.Active && (!x.StartsAtUtc.HasValue || x.StartsAtUtc <= now) && (!x.EndsAtUtc.HasValue || x.EndsAtUtc >= now))
                .OrderBy(x => x.Name)
                .ToListAsync();
            return Results.Ok(promotions);
        }).AllowAnonymous();

        app.MapGet("/api/promotions/all", async (CoreDbContext db) =>
            Results.Ok(await db.Promotions.AsNoTracking().OrderByDescending(x => x.UpdatedAtUtc).Take(500).ToListAsync()))
            .RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin", "Counter"));

        app.MapPost("/api/promotions", async (PromotionRequest input, CoreDbContext db, IHubContext<PosHub> hub) =>
        {
            if (string.IsNullOrWhiteSpace(input.Name) || input.Value < 0) return Results.BadRequest("Promotion name and non-negative value are required.");
            if (!input.DiscountType.Equals("Percent", StringComparison.OrdinalIgnoreCase) && !input.DiscountType.Equals("Fixed", StringComparison.OrdinalIgnoreCase)) return Results.BadRequest("Discount type must be Percent or Fixed.");
            if (input.DiscountType.Equals("Percent", StringComparison.OrdinalIgnoreCase) && input.Value > 100) return Results.BadRequest("Percent discount cannot exceed 100.");
            if (input.StartsAtUtc.HasValue && input.EndsAtUtc.HasValue && input.EndsAtUtc < input.StartsAtUtc) return Results.BadRequest("Promotion end time must be after start time.");
            if (input.ProductId.HasValue && !await db.Products.AnyAsync(x => x.Id == input.ProductId.Value)) return Results.BadRequest("Product not found.");
            if (input.CategoryId.HasValue && !await db.Categories.AnyAsync(x => x.Id == input.CategoryId.Value)) return Results.BadRequest("Category not found.");

            var promotion = new Promotion
            {
                Name = input.Name.Trim(), Description = input.Description?.Trim() ?? "", DiscountType = input.DiscountType.Trim(), Value = input.Value,
                Active = input.Active, StartsAtUtc = input.StartsAtUtc, EndsAtUtc = input.EndsAtUtc, ProductId = input.ProductId, CategoryId = input.CategoryId,
                UpdatedAtUtc = DateTime.UtcNow
            };
            db.Promotions.Add(promotion);
            await db.SaveChangesAsync();
            await hub.Clients.All.SendAsync("promotion.updated", new { promotionId = promotion.Id });
            return Results.Created($"/api/promotions/{promotion.Id}", promotion);
        }).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin", "Counter"));

        app.MapPut("/api/promotions/{id:int}", async (int id, PromotionRequest input, CoreDbContext db, IHubContext<PosHub> hub) =>
        {
            var promotion = await db.Promotions.FindAsync(id);
            if (promotion is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(input.Name) || input.Value < 0) return Results.BadRequest("Promotion name and non-negative value are required.");
            if (!input.DiscountType.Equals("Percent", StringComparison.OrdinalIgnoreCase) && !input.DiscountType.Equals("Fixed", StringComparison.OrdinalIgnoreCase)) return Results.BadRequest("Discount type must be Percent or Fixed.");
            if (input.DiscountType.Equals("Percent", StringComparison.OrdinalIgnoreCase) && input.Value > 100) return Results.BadRequest("Percent discount cannot exceed 100.");
            if (input.StartsAtUtc.HasValue && input.EndsAtUtc.HasValue && input.EndsAtUtc < input.StartsAtUtc) return Results.BadRequest("Promotion end time must be after start time.");

            promotion.Name = input.Name.Trim(); promotion.Description = input.Description?.Trim() ?? ""; promotion.DiscountType = input.DiscountType.Trim(); promotion.Value = input.Value;
            promotion.Active = input.Active; promotion.StartsAtUtc = input.StartsAtUtc; promotion.EndsAtUtc = input.EndsAtUtc; promotion.ProductId = input.ProductId; promotion.CategoryId = input.CategoryId; promotion.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
            await hub.Clients.All.SendAsync("promotion.updated", new { promotionId = promotion.Id });
            return Results.Ok(promotion);
        }).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin", "Counter"));

        app.MapDelete("/api/promotions/{id:int}", async (int id, CoreDbContext db, IHubContext<PosHub> hub) =>
        {
            var promotion = await db.Promotions.FindAsync(id);
            if (promotion is null) return Results.NotFound();
            db.Promotions.Remove(promotion);
            await db.SaveChangesAsync();
            await hub.Clients.All.SendAsync("promotion.updated", new { promotionId = id, deleted = true });
            return Results.NoContent();
        }).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin", "Counter"));
    }
}
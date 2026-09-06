using System.Security.Claims;
using FindUpTo.Pos.Server.Data;
using FindUpTo.Pos.Server.Hubs;
using FindUpTo.Pos.Server.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace FindUpTo.Pos.Server.Endpoints;

public static class CatalogEndpoints
{
    private static readonly string[] CatalogRoles = ["Owner", "Manager", "Admin", "Counter"];

    public static void MapCatalogEndpoints(this WebApplication app)
    {
        app.MapPost("/api/categories", async (CategoryRequest input, ClaimsPrincipal user, CoreDbContext db, IHubContext<PosHub> hub) =>
        {
            var name = input.Name?.Trim() ?? "";
            if (name.Length is < 1 or > 120) return Results.BadRequest("Category name must be between 1 and 120 characters.");
            if (await db.Categories.AnyAsync(x => x.Name.ToLower() == name.ToLower())) return Results.Conflict("A category with that name already exists.");

            var category = new Category { Name = name, SortOrder = input.SortOrder };
            db.Categories.Add(category);
            await db.SaveChangesAsync();
            await AuditEndpoints.WriteAsync(db, user, "Created", "Category", category.Id.ToString(), category.Name);
            await hub.Clients.All.SendAsync("catalog.updated", new { type = "category", id = category.Id });
            return Results.Created($"/api/categories/{category.Id}", category);
        }).RequireAuthorization(p => p.RequireRole(CatalogRoles));

        app.MapPut("/api/categories/{id:int}", async (int id, CategoryRequest input, ClaimsPrincipal user, CoreDbContext db, IHubContext<PosHub> hub) =>
        {
            var name = input.Name?.Trim() ?? "";
            if (name.Length is < 1 or > 120) return Results.BadRequest("Category name must be between 1 and 120 characters.");
            var category = await db.Categories.FindAsync(id);
            if (category is null) return Results.NotFound();
            if (await db.Categories.AnyAsync(x => x.Id != id && x.Name.ToLower() == name.ToLower())) return Results.Conflict("A category with that name already exists.");

            category.Name = name;
            category.SortOrder = input.SortOrder;
            await db.SaveChangesAsync();
            await AuditEndpoints.WriteAsync(db, user, "Updated", "Category", id.ToString(), category.Name);
            await hub.Clients.All.SendAsync("catalog.updated", new { type = "category", id });
            return Results.Ok(category);
        }).RequireAuthorization(p => p.RequireRole(CatalogRoles));

        app.MapDelete("/api/categories/{id:int}", async (int id, ClaimsPrincipal user, CoreDbContext db, IHubContext<PosHub> hub) =>
        {
            var category = await db.Categories.FindAsync(id);
            if (category is null) return Results.NotFound();
            category.Active = false;
            await db.Products.Where(x => x.CategoryId == id).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Available, false).SetProperty(x => x.UpdatedAtUtc, DateTime.UtcNow));
            await db.SaveChangesAsync();
            await AuditEndpoints.WriteAsync(db, user, "Deactivated", "Category", id.ToString(), category.Name);
            await hub.Clients.All.SendAsync("catalog.updated", new { type = "category", id });
            return Results.NoContent();
        }).RequireAuthorization(p => p.RequireRole(CatalogRoles));

        app.MapPost("/api/products", async (ProductRequest input, ClaimsPrincipal user, CoreDbContext db, IHubContext<PosHub> hub) =>
        {
            var validation = await ValidateProductAsync(input, db, null);
            if (validation is not null) return validation;

            var product = new Product
            {
                CategoryId = input.CategoryId,
                Name = input.Name.Trim(),
                Description = input.Description?.Trim() ?? "",
                Price = Math.Round(input.Price, 2),
                ImageUrl = input.ImageUrl?.Trim() ?? "",
                Barcode = input.Barcode?.Trim() ?? "",
                Available = input.Available,
                UpdatedAtUtc = DateTime.UtcNow
            };
            db.Products.Add(product);
            await db.SaveChangesAsync();
            await AuditEndpoints.WriteAsync(db, user, "Created", "Product", product.Id.ToString(), product.Name);
            await hub.Clients.All.SendAsync("catalog.updated", new { type = "product", id = product.Id });
            return Results.Created($"/api/products/{product.Id}", product);
        }).RequireAuthorization(p => p.RequireRole(CatalogRoles));

        app.MapPut("/api/products/{id:int}", async (int id, ProductRequest input, ClaimsPrincipal user, CoreDbContext db, IHubContext<PosHub> hub) =>
        {
            var product = await db.Products.FindAsync(id);
            if (product is null) return Results.NotFound();
            var validation = await ValidateProductAsync(input, db, id);
            if (validation is not null) return validation;

            product.CategoryId = input.CategoryId;
            product.Name = input.Name.Trim();
            product.Description = input.Description?.Trim() ?? "";
            product.Price = Math.Round(input.Price, 2);
            product.ImageUrl = input.ImageUrl?.Trim() ?? "";
            product.Barcode = input.Barcode?.Trim() ?? "";
            product.Available = input.Available;
            product.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
            await AuditEndpoints.WriteAsync(db, user, "Updated", "Product", id.ToString(), product.Name);
            await hub.Clients.All.SendAsync("catalog.updated", new { type = "product", id });
            return Results.Ok(product);
        }).RequireAuthorization(p => p.RequireRole(CatalogRoles));

        app.MapDelete("/api/products/{id:int}", async (int id, ClaimsPrincipal user, CoreDbContext db, IHubContext<PosHub> hub) =>
        {
            var product = await db.Products.FindAsync(id);
            if (product is null) return Results.NotFound();
            product.Available = false;
            product.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
            await AuditEndpoints.WriteAsync(db, user, "Deactivated", "Product", id.ToString(), product.Name);
            await hub.Clients.All.SendAsync("catalog.updated", new { type = "product", id });
            return Results.NoContent();
        }).RequireAuthorization(p => p.RequireRole(CatalogRoles));
    }

    private static async Task<IResult?> ValidateProductAsync(ProductRequest input, CoreDbContext db, int? existingId)
    {
        var name = input.Name?.Trim() ?? "";
        var description = input.Description?.Trim() ?? "";
        var imageUrl = input.ImageUrl?.Trim() ?? "";
        var barcode = input.Barcode?.Trim() ?? "";
        if (input.CategoryId <= 0 || !await db.Categories.AnyAsync(x => x.Id == input.CategoryId && x.Active)) return Results.BadRequest("Category not found or inactive.");
        if (name.Length is < 1 or > 160) return Results.BadRequest("Product name must be between 1 and 160 characters.");
        if (description.Length > 2000) return Results.BadRequest("Product description cannot exceed 2000 characters.");
        if (imageUrl.Length > 1000) return Results.BadRequest("Product image URL cannot exceed 1000 characters.");
        if (input.Price < 0 || input.Price > 100000000m) return Results.BadRequest("Product price is invalid.");
        if (barcode.Length > 64) return Results.BadRequest("Barcode cannot exceed 64 characters.");
        if (barcode.Length > 0 && await db.Products.AnyAsync(x => x.Id != existingId && x.Barcode == barcode)) return Results.Conflict("That barcode is already assigned to another product.");
        return null;
    }
}

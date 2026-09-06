using System.Security.Claims;
using FindUpTo.Pos.Server.Data;
using FindUpTo.Pos.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace FindUpTo.Pos.Server.Endpoints;

public static class BarcodeEndpoints
{
    public static void MapBarcodeEndpoints(this WebApplication app)
    {
        app.MapGet("/api/products/barcode/{barcode}", async (string barcode, CoreDbContext db) =>
        {
            var value = barcode.Trim();
            if (string.IsNullOrWhiteSpace(value)) return Results.BadRequest("Barcode is required.");
            var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(x => x.Barcode == value);
            return product is null ? Results.NotFound() : Results.Ok(product);
        }).RequireAuthorization();

        app.MapPut("/api/products/{id:int}/barcode", async (int id, BarcodeRequest input, ClaimsPrincipal user, CoreDbContext db) =>
        {
            var value = input.Barcode?.Trim() ?? "";
            if (value.Length > 64) return Results.BadRequest("Barcode cannot exceed 64 characters.");
            var product = await db.Products.FindAsync(id);
            if (product is null) return Results.NotFound();
            if (value.Length > 0 && await db.Products.AnyAsync(x => x.Id != id && x.Barcode == value)) return Results.Conflict("That barcode is already assigned to another product.");
            product.Barcode = value; product.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
            await AuditEndpoints.WriteAsync(db, user, "BarcodeChanged", "Product", id.ToString(), value);
            return Results.Ok(product);
        }).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin"));
    }
}

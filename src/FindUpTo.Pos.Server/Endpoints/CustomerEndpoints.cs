using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FindUpTo.Pos.Server.Data;
using FindUpTo.Pos.Server.Hubs;
using FindUpTo.Pos.Server.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace FindUpTo.Pos.Server.Endpoints;

public static class CustomerEndpoints
{
    public static void MapCustomerEndpoints(this WebApplication app)
    {
        app.MapPost("/api/customer/session", async (CustomerSessionRequest input, CoreDbContext db, IConfiguration config) =>
        {
            if (string.IsNullOrWhiteSpace(input.Name) && string.IsNullOrWhiteSpace(input.Phone)) return Results.BadRequest("Customer name or phone is required.");
            Customer? customer = null;
            if (!string.IsNullOrWhiteSpace(input.Phone)) customer = await db.Customers.SingleOrDefaultAsync(x => x.Phone == input.Phone.Trim());
            if (customer is null)
            {
                customer = new Customer { Name = input.Name?.Trim() ?? "", Phone = input.Phone?.Trim() ?? "", Address = input.Address?.Trim() ?? "", Notes = input.Notes?.Trim() ?? "" };
                db.Customers.Add(customer);
                await db.SaveChangesAsync();
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(input.Name)) customer.Name = input.Name.Trim();
                if (!string.IsNullOrWhiteSpace(input.Address)) customer.Address = input.Address.Trim();
                await db.SaveChangesAsync();
            }

            var keyText = config["Jwt:Key"] ?? Environment.GetEnvironmentVariable("POS_JWT_KEY");
            if (string.IsNullOrWhiteSpace(keyText) || keyText.Length < 32) keyText = "CHANGE_THIS_DEVELOPMENT_KEY_TO_A_LONG_RANDOM_SECRET_32CHARS";
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(keyText));
            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, $"customer:{customer.Id}"),
                new Claim(ClaimTypes.Name, $"customer:{customer.Id}"),
                new Claim(ClaimTypes.Role, "Customer"),
                new Claim("customer_id", customer.Id.ToString())
            };
            var token = new JwtSecurityToken(claims: claims, expires: DateTime.UtcNow.AddDays(30), signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
            return Results.Ok(new CustomerSessionResponse(new JwtSecurityTokenHandler().WriteToken(token), customer.Id, customer.Name, customer.Phone, customer.Address));
        }).AllowAnonymous();

        app.MapGet("/api/customer/products", async (CoreDbContext db, int? categoryId) =>
        {
            var q = db.Products.AsNoTracking().Where(x => x.Available);
            if (categoryId.HasValue) q = q.Where(x => x.CategoryId == categoryId.Value);
            return Results.Ok(await q.OrderBy(x => x.Name).ToListAsync());
        }).RequireAuthorization(p => p.RequireRole("Customer"));

        app.MapGet("/api/customer/promotions", async (CoreDbContext db) =>
        {
            var now = DateTime.UtcNow;
            return Results.Ok(await db.Promotions.AsNoTracking().Where(x => x.Active && (!x.StartsAtUtc.HasValue || x.StartsAtUtc <= now) && (!x.EndsAtUtc.HasValue || x.EndsAtUtc >= now)).OrderBy(x => x.Name).ToListAsync());
        }).RequireAuthorization(p => p.RequireRole("Customer"));

        app.MapPost("/api/customer/orders", async (CreateCustomerOrderRequest input, ClaimsPrincipal user, CoreDbContext db, IHubContext<PosHub> hub) =>
        {
            if (!int.TryParse(user.FindFirstValue("customer_id"), out var customerId)) return Results.Unauthorized();
            if (input.Items is null || input.Items.Count == 0) return Results.BadRequest("Cart must contain at least one item.");
            var ids = input.Items.Select(x => x.ProductId).Distinct().ToList();
            var products = await db.Products.Where(x => ids.Contains(x.Id) && x.Available).ToDictionaryAsync(x => x.Id);
            if (products.Count != ids.Count) return Results.BadRequest("One or more products are unavailable.");
            var customer = await db.Customers.FindAsync(customerId);
            if (customer is null) return Results.Unauthorized();
            if (!string.IsNullOrWhiteSpace(input.Address)) customer.Address = input.Address.Trim();
            var orderType = string.Equals(input.OrderType, "Delivery", StringComparison.OrdinalIgnoreCase) ? "Delivery" : "Pickup";
            var order = new PosOrder { CustomerId = customerId, CreatedByUsername = user.Identity?.Name ?? $"customer:{customerId}", OrderType = orderType, Notes = input.Notes?.Trim() ?? "" };
            foreach (var line in input.Items)
            {
                if (line.Quantity <= 0) return Results.BadRequest("Quantity must be greater than zero.");
                var product = products[line.ProductId];
                order.Items.Add(new OrderItem { ProductId = product.Id, ProductName = product.Name, UnitPrice = product.Price, Quantity = line.Quantity, Notes = line.Notes?.Trim() ?? "", LineTotal = product.Price * line.Quantity });
            }
            order.Subtotal = order.Items.Sum(x => x.LineTotal);
            var taxRate = await db.BusinessSettings.Select(x => x.TaxPercent).SingleAsync();
            order.Tax = Math.Round(order.Subtotal * taxRate / 100m, 2);
            order.Total = order.Subtotal + order.Tax;
            db.Orders.Add(order);
            await db.SaveChangesAsync();
            await hub.Clients.All.SendAsync("order.updated", new { orderId = order.Id, customerId, status = order.Status, total = order.Total, updatedAtUtc = order.UpdatedAtUtc });
            return Results.Created($"/api/customer/orders/{order.Id}", new CustomerOrderResponse(order.Id, orderType, order.Status, order.Subtotal, order.Tax, order.Total, order.CreatedAtUtc, order.Items));
        }).RequireAuthorization(p => p.RequireRole("Customer"));

        app.MapGet("/api/customer/orders", async (ClaimsPrincipal user, CoreDbContext db) =>
        {
            if (!int.TryParse(user.FindFirstValue("customer_id"), out var customerId)) return Results.Unauthorized();
            var orders = await db.Orders.AsNoTracking().Include(x => x.Items).Where(x => x.CustomerId == customerId).OrderByDescending(x => x.CreatedAtUtc).Take(100).ToListAsync();
            return Results.Ok(orders.Select(x => new CustomerOrderResponse(x.Id, x.OrderType, x.Status, x.Subtotal, x.Tax, x.Total, x.CreatedAtUtc, x.Items)));
        }).RequireAuthorization(p => p.RequireRole("Customer"));

        app.MapGet("/api/customer/orders/{id:int}", async (int id, ClaimsPrincipal user, CoreDbContext db) =>
        {
            if (!int.TryParse(user.FindFirstValue("customer_id"), out var customerId)) return Results.Unauthorized();
            var order = await db.Orders.AsNoTracking().Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == id && x.CustomerId == customerId);
            return order is null ? Results.NotFound() : Results.Ok(new CustomerOrderResponse(order.Id, order.OrderType, order.Status, order.Subtotal, order.Tax, order.Total, order.CreatedAtUtc, order.Items));
        }).RequireAuthorization(p => p.RequireRole("Customer"));
    }
}

public sealed record CustomerSessionRequest(string? Name, string? Phone, string? Address = null, string? Notes = null);
public sealed record CustomerSessionResponse(string Token, int CustomerId, string Name, string Phone, string Address);
public sealed record CustomerOrderItemRequest(int ProductId, int Quantity, string? Notes = null);
public sealed record CreateCustomerOrderRequest(List<CustomerOrderItemRequest> Items, string OrderType = "Pickup", string? Address = null, string? Notes = null);
public sealed record CustomerOrderResponse(int Id, string OrderType, string Status, decimal Subtotal, decimal Tax, decimal Total, DateTime CreatedAtUtc, IReadOnlyCollection<OrderItem> Items);

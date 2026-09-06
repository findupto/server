using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FindUpTo.Pos.Server.Data;
using FindUpTo.Pos.Server.Models;
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
    }
}

public sealed record CustomerSessionRequest(string? Name, string? Phone, string? Address = null, string? Notes = null);
public sealed record CustomerSessionResponse(string Token, int CustomerId, string Name, string Phone, string Address);

using System.Security.Claims;
using FindUpTo.Pos.Server.Data;
using FindUpTo.Pos.Server.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FindUpTo.Pos.Server.Endpoints;

public static class UserEndpoints
{
    private static readonly string[] Roles = ["Owner", "Manager", "Admin", "Counter", "Waiter", "Kitchen", "Rider"];

    public static void MapUserEndpoints(this WebApplication app)
    {
        app.MapGet("/api/users", async (CoreDbContext db) =>
            Results.Ok(await db.Users.AsNoTracking().OrderBy(x => x.Role).ThenBy(x => x.Username)
                .Select(x => new { x.Id, x.Username, x.Role, x.Active, x.CreatedAtUtc }).Take(500).ToListAsync()))
            .RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin"));

        app.MapPost("/api/users", async (CreateUserRequest input, ClaimsPrincipal actor, CoreDbContext db, IPasswordHasher<AppUser> hasher) =>
        {
            if (string.IsNullOrWhiteSpace(input.Username) || string.IsNullOrWhiteSpace(input.Password)) return Results.BadRequest("Username and password are required.");
            if (!Roles.Contains(input.Role, StringComparer.OrdinalIgnoreCase)) return Results.BadRequest("Invalid role.");
            var username = input.Username.Trim();
            if (await db.Users.AnyAsync(x => x.Username == username)) return Results.Conflict("Username already exists.");
            if (input.Role.Equals("Owner", StringComparison.OrdinalIgnoreCase) && actor.FindFirstValue(ClaimTypes.Role) != "Owner") return Results.Forbid();
            var user = new AppUser { Username = username, Role = Roles.First(x => x.Equals(input.Role, StringComparison.OrdinalIgnoreCase)), Active = true };
            user.PasswordHash = hasher.HashPassword(user, input.Password);
            db.Users.Add(user); await db.SaveChangesAsync();
            return Results.Created($"/api/users/{user.Id}", new { user.Id, user.Username, user.Role, user.Active, user.CreatedAtUtc });
        }).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin"));

        app.MapPatch("/api/users/{id:int}", async (int id, UpdateUserRequest input, ClaimsPrincipal actor, CoreDbContext db, IPasswordHasher<AppUser> hasher) =>
        {
            var user = await db.Users.FindAsync(id); if (user is null) return Results.NotFound();
            var actorRole = actor.FindFirstValue(ClaimTypes.Role) ?? "";
            if (user.Role == "Owner" && actorRole != "Owner") return Results.Forbid();
            if (input.Role is not null)
            {
                if (!Roles.Contains(input.Role, StringComparer.OrdinalIgnoreCase)) return Results.BadRequest("Invalid role.");
                if (input.Role.Equals("Owner", StringComparison.OrdinalIgnoreCase) && actorRole != "Owner") return Results.Forbid();
                user.Role = Roles.First(x => x.Equals(input.Role, StringComparison.OrdinalIgnoreCase));
            }
            if (input.Active.HasValue) user.Active = input.Active.Value;
            if (!string.IsNullOrWhiteSpace(input.Password)) user.PasswordHash = hasher.HashPassword(user, input.Password);
            await db.SaveChangesAsync();
            return Results.Ok(new { user.Id, user.Username, user.Role, user.Active, user.CreatedAtUtc });
        }).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin"));
    }
}

public sealed record CreateUserRequest(string Username, string Password, string Role);
public sealed record UpdateUserRequest(string? Password = null, string? Role = null, bool? Active = null);

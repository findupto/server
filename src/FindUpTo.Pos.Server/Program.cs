using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FindUpTo.Pos.Server.Data;
using FindUpTo.Pos.Server.Hubs;
using FindUpTo.Pos.Server.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

var connection = builder.Configuration.GetConnectionString("Pos") ?? "Data Source=pos.db";
builder.Services.AddDbContext<PosDbContext>(options => options.UseSqlite(connection));
builder.Services.AddSingleton<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var jwtKey = builder.Configuration["Jwt:Key"] ?? Environment.GetEnvironmentVariable("POS_JWT_KEY");
if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Length < 32)
    jwtKey = "CHANGE_THIS_DEVELOPMENT_KEY_TO_A_LONG_RANDOM_SECRET_32CHARS";

var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) && context.HttpContext.Request.Path.StartsWithSegments("/hubs/pos"))
                    context.Token = accessToken;
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PosDbContext>();
    await db.Database.EnsureCreatedAsync();
    await SeedAsync(db, scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>());
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "findupto-pos-server" }));

app.MapPost("/api/auth/login", async (LoginRequest request, PosDbContext db, IPasswordHasher<AppUser> hasher) =>
{
    var user = await db.Users.SingleOrDefaultAsync(x => x.Username == request.Username && x.Active);
    if (user is null || hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password) == PasswordVerificationResult.Failed)
        return Results.Unauthorized();

    var claims = new[]
    {
        new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
        new Claim(ClaimTypes.Name, user.Username),
        new Claim(ClaimTypes.Role, user.Role)
    };
    var token = new JwtSecurityToken(claims: claims, expires: DateTime.UtcNow.AddHours(12), signingCredentials: new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256));
    return Results.Ok(new LoginResponse(new JwtSecurityTokenHandler().WriteToken(token), user.Id, user.Username, user.Role));
}).AllowAnonymous();

app.MapGet("/api/settings", async (PosDbContext db) =>
    Results.Ok(await db.BusinessSettings.AsNoTracking().SingleAsync()));

app.MapPut("/api/settings", async (BusinessSetting input, PosDbContext db) =>
{
    var current = await db.BusinessSettings.SingleAsync();
    current.BusinessName = input.BusinessName.Trim();
    current.Phone = input.Phone.Trim();
    current.Address = input.Address.Trim();
    current.TaxPercent = input.TaxPercent;
    current.CurrencyCode = input.CurrencyCode.Trim().ToUpperInvariant();
    current.CurrencySymbol = input.CurrencySymbol.Trim();
    current.UpdatedAtUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.Ok(current);
}).RequireAuthorization(policy => policy.RequireRole("Owner", "Manager"));

app.MapGet("/api/me", (ClaimsPrincipal user) => Results.Ok(new
{
    username = user.Identity?.Name,
    role = user.FindFirstValue(ClaimTypes.Role)
})).RequireAuthorization();

app.MapHub<PosHub>("/hubs/pos");

app.Run();

static async Task SeedAsync(PosDbContext db, IPasswordHasher<AppUser> hasher)
{
    if (!await db.BusinessSettings.AnyAsync())
    {
        db.BusinessSettings.Add(new BusinessSetting());
        await db.SaveChangesAsync();
    }

    if (await db.Users.AnyAsync()) return;

    var seedUsers = new[]
    {
        (Username: "Malik", Role: "Owner", Env: "INITIAL_OWNER_PASSWORD"),
        (Username: "MK", Role: "Admin", Env: "INITIAL_ADMIN_PASSWORD"),
        (Username: "WR", Role: "Waiter", Env: "INITIAL_WAITER_PASSWORD"),
        (Username: "CP", Role: "Counter", Env: "INITIAL_COUNTER_PASSWORD")
    };

    foreach (var item in seedUsers)
    {
        var password = Environment.GetEnvironmentVariable(item.Env);
        if (string.IsNullOrWhiteSpace(password))
            password = $"CHANGE_ME_{item.Username}";

        var user = new AppUser { Username = item.Username, Role = item.Role };
        user.PasswordHash = hasher.HashPassword(user, password);
        db.Users.Add(user);
    }

    await db.SaveChangesAsync();
}

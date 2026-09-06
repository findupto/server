using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using FindUpTo.Pos.Server.Data;
using FindUpTo.Pos.Server.Endpoints;
using FindUpTo.Pos.Server.Hubs;
using FindUpTo.Pos.Server.Models;
using FindUpTo.Pos.Server.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
builder.Host.ConfigurePosWindowsService();
var connection = builder.Configuration.GetConnectionString("Pos") ?? "Data Source=pos.db";
builder.Services.AddDbContext<CoreDbContext>(options => options.UseSqlite(connection));
builder.Services.AddSingleton<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
builder.Services.AddScoped<PromotionPricingService>();
builder.Services.AddScoped<InventoryService>();
builder.Services.AddHostedService<AutomaticBackupHostedService>();
builder.Services.AddHostedService<SyncSchemaHostedService>();
builder.Services.AddHostedService<PushNotificationHostedService>();
builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
    options.AddPolicy("customer-session", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
});

var jwtKey = builder.Configuration["Jwt:Key"] ?? Environment.GetEnvironmentVariable("POS_JWT_KEY");
if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Length < 32) throw new InvalidOperationException("A JWT signing key of at least 32 characters is required. Configure POS_JWT_KEY or Jwt:Key.");
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters { ValidateIssuerSigningKey = true, IssuerSigningKey = signingKey, ValidateIssuer = false, ValidateAudience = false, ValidateLifetime = true, NameClaimType = ClaimTypes.Name, RoleClaimType = ClaimTypes.Role };
    options.Events = new JwtBearerEvents { OnMessageReceived = context => { var token = context.Request.Query["access_token"]; if (!string.IsNullOrEmpty(token) && context.HttpContext.Request.Path.StartsWithSegments("/hubs/pos")) context.Token = token; return Task.CompletedTask; } };
});
builder.Services.AddAuthorization();

var app = builder.Build();
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
    await PrepareDatabaseAsync(db);
    await SeedAsync(db, scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>());
}

if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "findupto-pos-server" }));
app.MapPost("/api/auth/login", async (LoginRequest request, CoreDbContext db, IPasswordHasher<AppUser> hasher) =>
{
    if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrEmpty(request.Password)) return Results.Unauthorized();
    var user = await db.Users.SingleOrDefaultAsync(x => x.Username == request.Username.Trim() && x.Active);
    if (user is null || hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password) == PasswordVerificationResult.Failed) return Results.Unauthorized();
    var claims = new[] { new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()), new Claim(ClaimTypes.Name, user.Username), new Claim(ClaimTypes.Role, user.Role) };
    var token = new JwtSecurityToken(claims: claims, expires: DateTime.UtcNow.AddHours(12), signingCredentials: new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256));
    return Results.Ok(new LoginResponse(new JwtSecurityTokenHandler().WriteToken(token), user.Id, user.Username, user.Role));
}).AllowAnonymous().RequireRateLimiting("login");

app.MapGet("/api/me", (ClaimsPrincipal user) => Results.Ok(new { username = user.Identity?.Name, role = user.FindFirstValue(ClaimTypes.Role) })).RequireAuthorization();
app.MapGet("/api/settings", async (CoreDbContext db) => Results.Ok(await db.BusinessSettings.AsNoTracking().SingleAsync())).RequireAuthorization();
app.MapGet("/api/categories", async (CoreDbContext db) => Results.Ok(await db.Categories.AsNoTracking().Where(x => x.Active).OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ToListAsync())).AllowAnonymous();
app.MapGet("/api/products", async (CoreDbContext db, int? categoryId) => { var q = db.Products.AsNoTracking().Where(x => x.Available); if (categoryId.HasValue) q = q.Where(x => x.CategoryId == categoryId.Value); return Results.Ok(await q.OrderBy(x => x.Name).ToListAsync()); }).AllowAnonymous();

app.MapCatalogEndpoints();
app.MapInventoryEndpoints();
app.MapUserEndpoints();
app.MapWorkflowEndpoints();
app.MapPromotionEndpoints();
app.MapMessagingEndpoints();
app.MapCustomerEndpoints();
app.MapReportEndpoints();
app.MapAuditEndpoints();
app.MapCashDrawerEndpoints();
app.MapBackupEndpoints();
app.MapSyncEndpoints();
app.MapPrinterEndpoints();
app.MapTableEndpoints();
app.MapReceiptEndpoints();
app.MapDeviceEndpoints();
app.MapNotificationEndpoints();
app.MapBarcodeEndpoints();
app.MapHub<PosHub>("/hubs/pos");
app.Run();

static async Task PrepareDatabaseAsync(CoreDbContext db)
{
    var connection = db.Database.GetDbConnection();
    await connection.OpenAsync();
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Users';";
    var hasUsersTable = Convert.ToInt64(await command.ExecuteScalarAsync()) > 0;
    await connection.CloseAsync();
    if (!hasUsersTable)
    {
        var allowCreate = string.Equals(Environment.GetEnvironmentVariable("POS_ALLOW_SCHEMA_CREATE"), "true", StringComparison.OrdinalIgnoreCase);
        if (!allowCreate) throw new InvalidOperationException("The POS database has no schema. Create/apply EF Core migrations first, or explicitly set POS_ALLOW_SCHEMA_CREATE=true for a brand-new database.");
        await db.Database.EnsureCreatedAsync();
    }
    else
    {
        await EnsureInventorySchemaAsync(db);
    }
}

static async Task EnsureInventorySchemaAsync(CoreDbContext db)
{
    await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS ProductInventories (Id INTEGER NOT NULL CONSTRAINT PK_ProductInventories PRIMARY KEY AUTOINCREMENT, ProductId INTEGER NOT NULL, QuantityOnHand TEXT NOT NULL, ReorderLevel TEXT NOT NULL, TrackInventory INTEGER NOT NULL DEFAULT 0, UpdatedAtUtc TEXT NOT NULL);");
    await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_ProductInventories_ProductId ON ProductInventories(ProductId);");
    await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS StockMovements (Id INTEGER NOT NULL CONSTRAINT PK_StockMovements PRIMARY KEY AUTOINCREMENT, ProductId INTEGER NOT NULL, QuantityChange TEXT NOT NULL, BalanceAfter TEXT NOT NULL, Type TEXT NOT NULL, Reason TEXT NOT NULL, Username TEXT NOT NULL, CreatedAtUtc TEXT NOT NULL);");
    await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_StockMovements_ProductId_CreatedAtUtc ON StockMovements(ProductId, CreatedAtUtc);");
}

static async Task SeedAsync(CoreDbContext db, IPasswordHasher<AppUser> hasher)
{
    if (!await db.BusinessSettings.AnyAsync())
    {
        db.BusinessSettings.Add(new BusinessSetting { BusinessName = Environment.GetEnvironmentVariable("POS_BUSINESS_NAME")?.Trim() is { Length: > 0 } businessName ? businessName : "FindUpTo POS", Phone = Environment.GetEnvironmentVariable("POS_BUSINESS_PHONE")?.Trim() ?? "", Address = Environment.GetEnvironmentVariable("POS_BUSINESS_ADDRESS")?.Trim() ?? "", TaxPercent = decimal.TryParse(Environment.GetEnvironmentVariable("POS_TAX_PERCENT"), out var tax) ? Math.Clamp(tax, 0m, 100m) : 0m, CurrencyCode = Environment.GetEnvironmentVariable("POS_CURRENCY_CODE")?.Trim().ToUpperInvariant() is { Length: 3 } currencyCode ? currencyCode : "PKR", CurrencySymbol = Environment.GetEnvironmentVariable("POS_CURRENCY_SYMBOL")?.Trim() is { Length: > 0 } currencySymbol ? currencySymbol : "Rs." });
        await db.SaveChangesAsync();
    }
    var seedUsers = new[] { (Username: "Malik", Role: "Owner", Env: "INITIAL_OWNER_PASSWORD"), (Username: "MK", Role: "Admin", Env: "INITIAL_ADMIN_PASSWORD"), (Username: "WR", Role: "Waiter", Env: "INITIAL_WAITER_PASSWORD"), (Username: "CP", Role: "Counter", Env: "INITIAL_COUNTER_PASSWORD") };
    foreach (var item in seedUsers)
    {
        if (await db.Users.AnyAsync(x => x.Username == item.Username)) continue;
        var password = Environment.GetEnvironmentVariable(item.Env);
        if (string.IsNullOrWhiteSpace(password)) throw new InvalidOperationException($"Missing required initial password environment variable: {item.Env}");
        var user = new AppUser { Username = item.Username, Role = item.Role };
        user.PasswordHash = hasher.HashPassword(user, password);
        db.Users.Add(user);
    }
    await db.SaveChangesAsync();
}

public partial class Program { }

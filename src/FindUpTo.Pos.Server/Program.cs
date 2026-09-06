using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FindUpTo.Pos.Server.Data;
using FindUpTo.Pos.Server.Endpoints;
using FindUpTo.Pos.Server.Hubs;
using FindUpTo.Pos.Server.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
var connection = builder.Configuration.GetConnectionString("Pos") ?? "Data Source=pos.db";
builder.Services.AddDbContext<CoreDbContext>(options => options.UseSqlite(connection));
builder.Services.AddSingleton<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var jwtKey = builder.Configuration["Jwt:Key"] ?? Environment.GetEnvironmentVariable("POS_JWT_KEY");
if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Length < 32) jwtKey = "CHANGE_THIS_DEVELOPMENT_KEY_TO_A_LONG_RANDOM_SECRET_32CHARS";
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true, IssuerSigningKey = signingKey, ValidateIssuer = false, ValidateAudience = false,
        ValidateLifetime = true, NameClaimType = ClaimTypes.Name, RoleClaimType = ClaimTypes.Role
    };
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var token = context.Request.Query["access_token"];
            if (!string.IsNullOrEmpty(token) && context.HttpContext.Request.Path.StartsWithSegments("/hubs/pos")) context.Token = token;
            return Task.CompletedTask;
        }
    };
});
builder.Services.AddAuthorization();
var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
    await db.Database.EnsureCreatedAsync();
    await SeedAsync(db, scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>());
}
if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "findupto-pos-server" }));

app.MapPost("/api/auth/login", async (LoginRequest request, CoreDbContext db, IPasswordHasher<AppUser> hasher) =>
{
    var user = await db.Users.SingleOrDefaultAsync(x => x.Username == request.Username && x.Active);
    if (user is null || hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password) == PasswordVerificationResult.Failed) return Results.Unauthorized();
    var claims = new[] { new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()), new Claim(ClaimTypes.Name, user.Username), new Claim(ClaimTypes.Role, user.Role) };
    var token = new JwtSecurityToken(claims: claims, expires: DateTime.UtcNow.AddHours(12), signingCredentials: new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256));
    return Results.Ok(new LoginResponse(new JwtSecurityTokenHandler().WriteToken(token), user.Id, user.Username, user.Role));
}).AllowAnonymous();

app.MapGet("/api/me", (ClaimsPrincipal user) => Results.Ok(new { username = user.Identity?.Name, role = user.FindFirstValue(ClaimTypes.Role) })).RequireAuthorization();
app.MapGet("/api/settings", async (CoreDbContext db) => Results.Ok(await db.BusinessSettings.AsNoTracking().SingleAsync())).RequireAuthorization();
app.MapPut("/api/settings", async (BusinessSetting input, CoreDbContext db) =>
{
    var current = await db.BusinessSettings.SingleAsync();
    current.BusinessName = input.BusinessName.Trim(); current.Phone = input.Phone.Trim(); current.Address = input.Address.Trim(); current.TaxPercent = input.TaxPercent;
    current.CurrencyCode = input.CurrencyCode.Trim().ToUpperInvariant(); current.CurrencySymbol = input.CurrencySymbol.Trim(); current.UpdatedAtUtc = DateTime.UtcNow;
    await db.SaveChangesAsync(); return Results.Ok(current);
}).RequireAuthorization(p => p.RequireRole("Owner", "Manager"));

app.MapGet("/api/categories", async (CoreDbContext db) => Results.Ok(await db.Categories.AsNoTracking().Where(x => x.Active).OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ToListAsync())).AllowAnonymous();
app.MapPost("/api/categories", async (CategoryRequest input, CoreDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(input.Name)) return Results.BadRequest("Category name is required.");
    var item = new Category { Name = input.Name.Trim(), SortOrder = input.SortOrder }; db.Categories.Add(item); await db.SaveChangesAsync();
    return Results.Created($"/api/categories/{item.Id}", item);
}).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin"));

app.MapGet("/api/products", async (CoreDbContext db, int? categoryId) =>
{
    var q = db.Products.AsNoTracking().Where(x => x.Available); if (categoryId.HasValue) q = q.Where(x => x.CategoryId == categoryId.Value);
    return Results.Ok(await q.OrderBy(x => x.Name).ToListAsync());
}).AllowAnonymous();
app.MapPost("/api/products", async (ProductRequest input, CoreDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(input.Name) || input.Price < 0) return Results.BadRequest("Valid product name and price are required.");
    if (!await db.Categories.AnyAsync(x => x.Id == input.CategoryId)) return Results.BadRequest("Category not found.");
    var item = new Product { CategoryId = input.CategoryId, Name = input.Name.Trim(), Description = input.Description?.Trim() ?? "", Price = input.Price, ImageUrl = input.ImageUrl?.Trim() ?? "", Available = input.Available };
    db.Products.Add(item); await db.SaveChangesAsync(); return Results.Created($"/api/products/{item.Id}", item);
}).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin"));
app.MapPut("/api/products/{id:int}", async (int id, ProductRequest input, CoreDbContext db) =>
{
    var item = await db.Products.FindAsync(id); if (item is null) return Results.NotFound();
    item.CategoryId = input.CategoryId; item.Name = input.Name.Trim(); item.Description = input.Description?.Trim() ?? ""; item.Price = input.Price; item.ImageUrl = input.ImageUrl?.Trim() ?? ""; item.Available = input.Available; item.UpdatedAtUtc = DateTime.UtcNow;
    await db.SaveChangesAsync(); return Results.Ok(item);
}).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin"));

app.MapGet("/api/customers", async (CoreDbContext db, string? search) =>
{
    var q = db.Customers.AsNoTracking(); if (!string.IsNullOrWhiteSpace(search)) q = q.Where(x => x.Name.Contains(search) || x.Phone.Contains(search));
    return Results.Ok(await q.OrderByDescending(x => x.CreatedAtUtc).Take(100).ToListAsync());
}).RequireAuthorization();
app.MapPost("/api/customers", async (CustomerRequest input, CoreDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(input.Name) && string.IsNullOrWhiteSpace(input.Phone)) return Results.BadRequest("Customer name or phone is required.");
    var item = new Customer { Name = input.Name.Trim(), Phone = input.Phone.Trim(), Address = input.Address.Trim(), Notes = input.Notes.Trim() }; db.Customers.Add(item); await db.SaveChangesAsync();
    return Results.Created($"/api/customers/{item.Id}", item);
}).RequireAuthorization();

app.MapPost("/api/orders", async (CreateOrderRequest input, ClaimsPrincipal user, CoreDbContext db) =>
{
    if (input.Items is null || input.Items.Count == 0) return Results.BadRequest("Order must contain at least one item.");
    var ids = input.Items.Select(x => x.ProductId).Distinct().ToList();
    var products = await db.Products.Where(x => ids.Contains(x.Id) && x.Available).ToDictionaryAsync(x => x.Id);
    if (products.Count != ids.Count) return Results.BadRequest("One or more products are unavailable.");
    if (input.CustomerId.HasValue && !await db.Customers.AnyAsync(x => x.Id == input.CustomerId.Value)) return Results.BadRequest("Customer not found.");
    var order = new PosOrder { CustomerId = input.CustomerId, CreatedByUsername = user.Identity?.Name ?? "unknown", OrderType = string.IsNullOrWhiteSpace(input.OrderType) ? "Counter" : input.OrderType.Trim(), Notes = input.Notes.Trim() };
    foreach (var line in input.Items)
    {
        if (line.Quantity <= 0) return Results.BadRequest("Quantity must be greater than zero.");
        var product = products[line.ProductId];
        order.Items.Add(new OrderItem { ProductId = product.Id, ProductName = product.Name, UnitPrice = product.Price, Quantity = line.Quantity, Notes = line.Notes.Trim(), LineTotal = product.Price * line.Quantity });
    }
    order.Subtotal = order.Items.Sum(x => x.LineTotal); var taxRate = await db.BusinessSettings.Select(x => x.TaxPercent).SingleAsync();
    order.Tax = Math.Round(order.Subtotal * taxRate / 100m, 2); order.Total = order.Subtotal + order.Tax;
    db.Orders.Add(order); await db.SaveChangesAsync(); await NotifyOrder(app, order);
    return Results.Created($"/api/orders/{order.Id}", await db.Orders.AsNoTracking().Include(x => x.Items).SingleAsync(x => x.Id == order.Id));
}).RequireAuthorization();

app.MapGet("/api/orders", async (CoreDbContext db, string? status) =>
{
    var q = db.Orders.AsNoTracking().Include(x => x.Items).AsQueryable(); if (!string.IsNullOrWhiteSpace(status)) q = q.Where(x => x.Status == status);
    return Results.Ok(await q.OrderByDescending(x => x.CreatedAtUtc).Take(200).ToListAsync());
}).RequireAuthorization();
app.MapGet("/api/orders/{id:int}", async (int id, CoreDbContext db) =>
{
    var order = await db.Orders.AsNoTracking().Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == id); return order is null ? Results.NotFound() : Results.Ok(order);
}).RequireAuthorization();
app.MapPatch("/api/orders/{id:int}/status", async (int id, UpdateOrderStatusRequest input, CoreDbContext db) =>
{
    var allowed = new[] { "New", "Accepted", "Preparing", "Ready", "OutForDelivery", "Served", "Completed", "Cancelled" };
    if (!allowed.Contains(input.Status, StringComparer.OrdinalIgnoreCase)) return Results.BadRequest("Invalid order status.");
    var order = await db.Orders.FindAsync(id); if (order is null) return Results.NotFound();
    order.Status = allowed.First(x => string.Equals(x, input.Status, StringComparison.OrdinalIgnoreCase)); order.UpdatedAtUtc = DateTime.UtcNow;
    await db.SaveChangesAsync(); await NotifyOrder(app, order); return Results.Ok(order);
}).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin", "Counter"));

app.MapUserEndpoints();
app.MapWorkflowEndpoints();
app.MapPromotionEndpoints();
app.MapMessagingEndpoints();
app.MapHub<PosHub>("/hubs/pos");
app.Run();

static async Task NotifyOrder(WebApplication app, PosOrder order)
{
    var hub = app.Services.GetRequiredService<IHubContext<PosHub>>();
    await hub.Clients.All.SendAsync("order.updated", new { orderId = order.Id, status = order.Status, total = order.Total, updatedAtUtc = order.UpdatedAtUtc });
}

static async Task SeedAsync(CoreDbContext db, IPasswordHasher<AppUser> hasher)
{
    if (!await db.BusinessSettings.AnyAsync()) { db.BusinessSettings.Add(new BusinessSetting()); await db.SaveChangesAsync(); }
    if (await db.Users.AnyAsync()) return;
    var seedUsers = new[] { (Username: "Malik", Role: "Owner", Env: "INITIAL_OWNER_PASSWORD"), (Username: "MK", Role: "Admin", Env: "INITIAL_ADMIN_PASSWORD"), (Username: "WR", Role: "Waiter", Env: "INITIAL_WAITER_PASSWORD"), (Username: "CP", Role: "Counter", Env: "INITIAL_COUNTER_PASSWORD") };
    foreach (var item in seedUsers)
    {
        var password = Environment.GetEnvironmentVariable(item.Env); if (string.IsNullOrWhiteSpace(password)) password = $"CHANGE_ME_{item.Username}";
        var user = new AppUser { Username = item.Username, Role = item.Role }; user.PasswordHash = hasher.HashPassword(user, password); db.Users.Add(user);
    }
    await db.SaveChangesAsync();
}
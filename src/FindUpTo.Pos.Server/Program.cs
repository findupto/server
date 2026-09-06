using System.Text;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
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
var connection = builder.Configuration.GetConnectionString("Pos") ?? "Data Source=pos.db";
builder.Services.AddDbContext<CoreDbContext>(options => options.UseSqlite(connection));
builder.Services.AddSingleton<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
builder.Services.AddHostedService<AutomaticBackupHostedService>();
builder.Services.AddHostedService<SyncSchemaHostedService>();
builder.Services.AddHostedService<PushNotificationHostedService>();
builder.Services.AddSignalR(); builder.Services.AddEndpointsApiExplorer(); builder.Services.AddSwaggerGen();
var jwtKey = builder.Configuration["Jwt:Key"] ?? Environment.GetEnvironmentVariable("POS_JWT_KEY");
if (string.IsNullOrWhiteSpace(jwtKey) && builder.Environment.IsDevelopment()) jwtKey = "CHANGE_THIS_DEVELOPMENT_KEY_TO_A_LONG_RANDOM_SECRET_32CHARS";
if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Length < 32) throw new InvalidOperationException("A JWT signing key of at least 32 characters is required. Configure Jwt:Key or POS_JWT_KEY.");
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options => { options.TokenValidationParameters = new TokenValidationParameters { ValidateIssuerSigningKey = true, IssuerSigningKey = signingKey, ValidateIssuer = false, ValidateAudience = false, ValidateLifetime = true, NameClaimType = ClaimTypes.Name, RoleClaimType = ClaimTypes.Role }; options.Events = new JwtBearerEvents { OnMessageReceived = context => { var token = context.Request.Query["access_token"]; if (!string.IsNullOrEmpty(token) && context.HttpContext.Request.Path.StartsWithSegments("/hubs/pos")) context.Token = token; return Task.CompletedTask; } }; }); builder.Services.AddAuthorization();
var app = builder.Build(); using (var scope = app.Services.CreateScope()) { var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>(); await db.Database.EnsureCreatedAsync(); await SeedAsync(db, scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>(), app.Environment); }
if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); } app.UseAuthentication(); app.UseAuthorization();
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "findupto-pos-server" }));
app.MapPost("/api/auth/login", async (LoginRequest request, CoreDbContext db, IPasswordHasher<AppUser> hasher) => { var user = await db.Users.SingleOrDefaultAsync(x => x.Username == request.Username && x.Active); if (user is null || hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password) == PasswordVerificationResult.Failed) return Results.Unauthorized(); var claims = new[] { new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()), new Claim(ClaimTypes.Name, user.Username), new Claim(ClaimTypes.Role, user.Role) }; var token = new JwtSecurityToken(claims: claims, expires: DateTime.UtcNow.AddHours(12), signingCredentials: new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256)); return Results.Ok(new LoginResponse(new JwtSecurityTokenHandler().WriteToken(token), user.Id, user.Username, user.Role)); }).AllowAnonymous();
app.MapGet("/api/me", (ClaimsPrincipal user) => Results.Ok(new { username = user.Identity?.Name, role = user.FindFirstValue(ClaimTypes.Role) })).RequireAuthorization(); app.MapGet("/api/settings", async (CoreDbContext db) => Results.Ok(await db.BusinessSettings.AsNoTracking().SingleAsync())).RequireAuthorization();
app.MapGet("/api/categories", async (CoreDbContext db) => Results.Ok(await db.Categories.AsNoTracking().Where(x => x.Active).OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ToListAsync())).AllowAnonymous();
app.MapGet("/api/products", async (CoreDbContext db, int? categoryId) => { var q = db.Products.AsNoTracking().Where(x => x.Available); if (categoryId.HasValue) q = q.Where(x => x.CategoryId == categoryId.Value); return Results.Ok(await q.OrderBy(x => x.Name).ToListAsync()); }).AllowAnonymous();
app.MapUserEndpoints(); app.MapWorkflowEndpoints(); app.MapPromotionEndpoints(); app.MapMessagingEndpoints(); app.MapCustomerEndpoints(); app.MapReportEndpoints(); app.MapAuditEndpoints(); app.MapCashDrawerEndpoints(); app.MapBackupEndpoints(); app.MapSyncEndpoints(); app.MapPrinterEndpoints(); app.MapTableEndpoints(); app.MapReceiptEndpoints(); app.MapDeviceEndpoints(); app.MapNotificationEndpoints(); app.MapHub<PosHub>("/hubs/pos"); app.Run();
public partial class Program { }
static async Task SeedAsync(CoreDbContext db, IPasswordHasher<AppUser> hasher, IWebHostEnvironment environment) { if (!await db.BusinessSettings.AnyAsync()) { db.BusinessSettings.Add(new BusinessSetting()); await db.SaveChangesAsync(); } if (await db.Users.AnyAsync()) return; var seedUsers = new[] { (Username: "Malik", Role: "Owner", Env: "INITIAL_OWNER_PASSWORD"), (Username: "MK", Role: "Admin", Env: "INITIAL_ADMIN_PASSWORD"), (Username: "WR", Role: "Waiter", Env: "INITIAL_WAITER_PASSWORD"), (Username: "CP", Role: "Counter", Env: "INITIAL_COUNTER_PASSWORD") }; foreach (var item in seedUsers) { var password = Environment.GetEnvironmentVariable(item.Env); if (string.IsNullOrWhiteSpace(password)) { if (!environment.IsDevelopment()) throw new InvalidOperationException($"Missing required initial password environment variable: {item.Env}"); password = $"CHANGE_ME_{item.Username}"; } var user = new AppUser { Username = item.Username, Role = item.Role }; user.PasswordHash = hasher.HashPassword(user, password); db.Users.Add(user); } await db.SaveChangesAsync(); }

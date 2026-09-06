using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FindUpTo.Pos.Server.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FindUpTo.Pos.Server.Tests;

public sealed class PosApiFactory : WebApplicationFactory<Program>
{
    private readonly string databaseName = $"pos-tests-{Guid.NewGuid():N}";

    public PosApiFactory()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        Environment.SetEnvironmentVariable("POS_JWT_KEY", "integration-test-jwt-key-must-be-at-least-32-chars");
        Environment.SetEnvironmentVariable("INITIAL_OWNER_PASSWORD", "Owner-Test-Password-123!");
        Environment.SetEnvironmentVariable("INITIAL_ADMIN_PASSWORD", "Admin-Test-Password-123!");
        Environment.SetEnvironmentVariable("INITIAL_WAITER_PASSWORD", "Waiter-Test-Password-123!");
        Environment.SetEnvironmentVariable("INITIAL_COUNTER_PASSWORD", "Counter-Test-Password-123!");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var descriptors = services.Where(d => d.ServiceType == typeof(DbContextOptions<CoreDbContext>) || d.ServiceType == typeof(CoreDbContext)).ToList();
            foreach (var descriptor in descriptors) services.Remove(descriptor);
            services.AddDbContext<CoreDbContext>(options => options.UseInMemoryDatabase(databaseName));
        });
    }
}

public class PosApiTests : IClassFixture<PosApiFactory>
{
    private readonly HttpClient client;

    public PosApiTests(PosApiFactory factory) => client = factory.CreateClient();

    [Fact]
    public async Task Health_ReturnsOk()
    {
        var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsJwtAndRole()
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username = "Malik", password = "Owner-Test-Password-123!" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<LoginResult>();
        Assert.False(string.IsNullOrWhiteSpace(result?.token));
        Assert.Equal("Owner", result?.role);
    }

    [Fact]
    public async Task Login_WithInvalidCredentials_ReturnsUnauthorized()
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username = "Malik", password = "wrong-password" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithoutToken_ReturnsUnauthorized()
    {
        var response = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Categories_ArePublic()
    {
        var response = await client.GetAsync("/api/categories");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task WaiterCannotCreateCategory()
    {
        await AuthenticateAsync("WR", "Waiter-Test-Password-123!");
        var response = await client.PostAsJsonAsync("/api/categories", new { name = "Forbidden", sortOrder = 1 });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminCanCreateCategory()
    {
        await AuthenticateAsync("MK", "Admin-Test-Password-123!");
        var response = await client.PostAsJsonAsync("/api/categories", new { name = "Drinks", sortOrder = 1 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task CreateProductThenOrderCalculatesTotal()
    {
        await AuthenticateAsync("MK", "Admin-Test-Password-123!");
        var categoryResponse = await client.PostAsJsonAsync("/api/categories", new { name = $"Food-{Guid.NewGuid():N}", sortOrder = 1 });
        var category = await categoryResponse.Content.ReadFromJsonAsync<IdResult>();
        Assert.NotNull(category);

        var productResponse = await client.PostAsJsonAsync("/api/products", new
        {
            categoryId = category!.id,
            name = "Test Burger",
            description = "",
            price = 100m,
            imageUrl = "",
            available = true
        });
        var product = await productResponse.Content.ReadFromJsonAsync<IdResult>();
        Assert.Equal(HttpStatusCode.Created, productResponse.StatusCode);
        Assert.NotNull(product);

        var orderResponse = await client.PostAsJsonAsync("/api/orders", new
        {
            items = new[] { new { productId = product!.id, quantity = 2, notes = "" } },
            orderType = "Counter",
            notes = ""
        });
        Assert.Equal(HttpStatusCode.Created, orderResponse.StatusCode);
        var order = await orderResponse.Content.ReadFromJsonAsync<OrderResult>();
        Assert.NotNull(order);
        Assert.Equal(200m, order!.subtotal);
        Assert.Equal(200m, order.total);
    }

    [Fact]
    public async Task CannotOrderUnavailableProduct()
    {
        await AuthenticateAsync("MK", "Admin-Test-Password-123!");
        var categoryResponse = await client.PostAsJsonAsync("/api/categories", new { name = $"Unavailable-{Guid.NewGuid():N}", sortOrder = 1 });
        var category = await categoryResponse.Content.ReadFromJsonAsync<IdResult>();
        var productResponse = await client.PostAsJsonAsync("/api/products", new
        {
            categoryId = category!.id,
            name = "Unavailable Product",
            description = "",
            price = 50m,
            imageUrl = "",
            available = false
        });
        var product = await productResponse.Content.ReadFromJsonAsync<IdResult>();

        var orderResponse = await client.PostAsJsonAsync("/api/orders", new
        {
            items = new[] { new { productId = product!.id, quantity = 1, notes = "" } },
            orderType = "Counter",
            notes = ""
        });
        Assert.Equal(HttpStatusCode.BadRequest, orderResponse.StatusCode);
    }

    [Fact]
    public async Task PaymentCannotBeRecordedTwice()
    {
        await AuthenticateAsync("MK", "Admin-Test-Password-123!");
        var categoryResponse = await client.PostAsJsonAsync("/api/categories", new { name = $"Payment-{Guid.NewGuid():N}", sortOrder = 1 });
        var category = await categoryResponse.Content.ReadFromJsonAsync<IdResult>();
        var productResponse = await client.PostAsJsonAsync("/api/products", new
        {
            categoryId = category!.id,
            name = "Payment Product",
            description = "",
            price = 100m,
            imageUrl = "",
            available = true
        });
        var product = await productResponse.Content.ReadFromJsonAsync<IdResult>();
        var orderResponse = await client.PostAsJsonAsync("/api/orders", new
        {
            items = new[] { new { productId = product!.id, quantity = 1, notes = "" } },
            orderType = "Counter",
            notes = ""
        });
        var order = await orderResponse.Content.ReadFromJsonAsync<IdResult>();

        var firstPayment = await client.PostAsJsonAsync($"/api/orders/{order!.id}/payment", new { amountTendered = 100m, method = "Cash", reference = "" });
        Assert.Equal(HttpStatusCode.OK, firstPayment.StatusCode);
        var secondPayment = await client.PostAsJsonAsync($"/api/orders/{order.id}/payment", new { amountTendered = 100m, method = "Cash", reference = "" });
        Assert.Equal(HttpStatusCode.Conflict, secondPayment.StatusCode);
    }

    private async Task AuthenticateAsync(string username, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        response.EnsureSuccessStatusCode();
        var login = await response.Content.ReadFromJsonAsync<LoginResult>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.token);
    }

    private sealed record LoginResult(string token, int userId, string username, string role);
    private sealed record IdResult(int id);
    private sealed record OrderResult(decimal subtotal, decimal tax, decimal total);
}

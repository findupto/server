using System.Net;
using System.Net.Http.Json;
using FindUpTo.Pos.Server.Services;

namespace FindUpTo.Pos.Server.Tests;

public sealed class DeliveryRoutingServiceTests
{
    [Fact]
    public async Task ReturnsNullWhenRoutingProviderIsNotConfigured()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddHttpClient("delivery-routing");
        await using var provider = services.BuildServiceProvider();
        var service = ActivatorUtilities.CreateInstance<DeliveryRoutingService>(provider);
        var result = await service.GetRouteAsync(31.45, 73.13, 31.46, 73.14);
        Assert.Null(result);
    }

    [Fact]
    public async Task ParsesOsrmRouteAndCachesSecondRequest()
    {
        var handler = new CountingHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["OSRM:BaseUrl"] = "https://routing.test" }).Build());
        services.AddHttpClient("delivery-routing").ConfigurePrimaryHttpMessageHandler(() => handler);
        await using var provider = services.BuildServiceProvider();
        var service = ActivatorUtilities.CreateInstance<DeliveryRoutingService>(provider);
        var first = await service.GetRouteAsync(31.45, 73.13, 31.46, 73.14);
        var second = await service.GetRouteAsync(31.45, 73.13, 31.46, 73.14);
        Assert.NotNull(first);
        Assert.Equal(1200, first!.DistanceMeters);
        Assert.Equal(300, first.DurationSeconds);
        Assert.Equal(2, first.Geometry.Count);
        Assert.Same(first, second);
        Assert.Equal(1, handler.Requests);
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { routes = new[] { new { distance = 1200, duration = 300, geometry = new { coordinates = new[] { new[] { 73.13, 31.45 }, new[] { 73.14, 31.46 } } } } } })
            });
        }
    }
}

using System.Text.Json;

namespace FindUpTo.Pos.Server.Services;

public sealed record DeliveryRouteResult(double DistanceMeters, double DurationSeconds, IReadOnlyList<(double Latitude, double Longitude)> Geometry);

public sealed class DeliveryRoutingService(IHttpClientFactory httpClientFactory, IConfiguration configuration)
{
    public async Task<DeliveryRouteResult?> GetRouteAsync(double fromLatitude, double fromLongitude, double toLatitude, double toLongitude, CancellationToken cancellationToken = default)
    {
        var baseUrl = configuration["OSRM:BaseUrl"] ?? Environment.GetEnvironmentVariable("POS_OSRM_URL") ?? "https://router.project-osrm.org";
        baseUrl = baseUrl.TrimEnd('/');
        var url = $"{baseUrl}/route/v1/driving/{fromLongitude.ToString(System.Globalization.CultureInfo.InvariantCulture)},{fromLatitude.ToString(System.Globalization.CultureInfo.InvariantCulture)};{toLongitude.ToString(System.Globalization.CultureInfo.InvariantCulture)},{toLatitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}?overview=full&geometries=geojson&steps=false";
        try
        {
            using var client = httpClientFactory.CreateClient("delivery-routing");
            using var response = await client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            if (!root.TryGetProperty("routes", out var routes) || routes.GetArrayLength() == 0) return null;
            var route = routes[0];
            var distance = route.GetProperty("distance").GetDouble();
            var duration = route.GetProperty("duration").GetDouble();
            var points = new List<(double Latitude, double Longitude)>();
            if (route.TryGetProperty("geometry", out var geometry) && geometry.TryGetProperty("coordinates", out var coordinates))
                foreach (var point in coordinates.EnumerateArray()) if (point.GetArrayLength() >= 2) points.Add((point[1].GetDouble(), point[0].GetDouble()));
            return new DeliveryRouteResult(distance, duration, points);
        }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
        catch (JsonException) { return null; }
    }
}
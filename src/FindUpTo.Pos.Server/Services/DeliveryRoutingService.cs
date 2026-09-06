using System.Globalization;
using System.Text.Json;

namespace FindUpTo.Pos.Server.Services;

public sealed record DeliveryRouteResult(double DistanceMeters, double DurationSeconds, IReadOnlyList<(double Latitude, double Longitude)> Geometry);

public sealed class DeliveryRoutingService(IHttpClientFactory httpClientFactory, IConfiguration configuration)
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(20);
    private readonly Dictionary<string, (DateTime ExpiresUtc, DeliveryRouteResult Result)> cache = new();
    private readonly object cacheLock = new();

    public async Task<DeliveryRouteResult?> GetRouteAsync(double fromLatitude, double fromLongitude, double toLatitude, double toLongitude, CancellationToken cancellationToken = default)
    {
        if (!Valid(fromLatitude, fromLongitude) || !Valid(toLatitude, toLongitude)) return null;
        var key = string.Join(':', fromLatitude.ToString("F5", CultureInfo.InvariantCulture), fromLongitude.ToString("F5", CultureInfo.InvariantCulture), toLatitude.ToString("F5", CultureInfo.InvariantCulture), toLongitude.ToString("F5", CultureInfo.InvariantCulture));
        lock (cacheLock) if (cache.TryGetValue(key, out var hit) && hit.ExpiresUtc > DateTime.UtcNow) return hit.Result;

        var baseUrl = configuration["OSRM:BaseUrl"] ?? Environment.GetEnvironmentVariable("POS_OSRM_URL");
        if (string.IsNullOrWhiteSpace(baseUrl)) return null;
        baseUrl = baseUrl.Trim().TrimEnd('/');
        var url = $"{baseUrl}/route/v1/driving/{fromLongitude.ToString(CultureInfo.InvariantCulture)},{fromLatitude.ToString(CultureInfo.InvariantCulture)};{toLongitude.ToString(CultureInfo.InvariantCulture)},{toLatitude.ToString(CultureInfo.InvariantCulture)}?overview=full&geometries=geojson&steps=false";
        try
        {
            using var client = httpClientFactory.CreateClient("delivery-routing");
            using var response = await client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var routes = document.RootElement.GetProperty("routes");
            if (routes.GetArrayLength() == 0) return null;
            var route = routes[0];
            var points = new List<(double Latitude, double Longitude)>();
            if (route.TryGetProperty("geometry", out var geometry) && geometry.TryGetProperty("coordinates", out var coordinates))
                foreach (var point in coordinates.EnumerateArray()) if (point.GetArrayLength() >= 2) points.Add((point[1].GetDouble(), point[0].GetDouble()));
            var result = new DeliveryRouteResult(route.GetProperty("distance").GetDouble(), route.GetProperty("duration").GetDouble(), points);
            lock (cacheLock) { cache[key] = (DateTime.UtcNow.Add(CacheLifetime), result); if (cache.Count > 256) foreach (var old in cache.Where(x => x.Value.ExpiresUtc <= DateTime.UtcNow).Select(x => x.Key).ToList()) cache.Remove(old); }
            return result;
        }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
        catch (JsonException) { return null; }
        catch (KeyNotFoundException) { return null; }
    }

    private static bool Valid(double lat, double lon) => lat is >= -90 and <= 90 && lon is >= -180 and <= 180;
}

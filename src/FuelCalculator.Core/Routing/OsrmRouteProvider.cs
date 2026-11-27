using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using FuelCalculator.Core.Domain;

namespace FuelCalculator.Core.Routing;

/// <summary>A stop could not be found, or no road connects the stops. The message is safe to show.</summary>
public sealed class RouteNotFoundException(string message) : Exception(message);

public sealed record GeocodedPlace(GeoPoint Point, string Name);

public interface IGeocoder
{
    Task<GeocodedPlace?> GeocodeAsync(string query, CancellationToken cancellationToken = default);
}

public static class RouteStopParser
{
    /// <summary>
    /// Reads "lat,lon" or "label | lat,lon" (the form the frontend sends when a stop has GPS coordinates).
    /// </summary>
    public static bool TryParseCoordinates(string? text, out GeoPoint point)
    {
        point = new GeoPoint(0, 0);

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var candidate = text.Contains('|') ? text[(text.LastIndexOf('|') + 1)..] : text;
        var parts = candidate.Split(',', StringSplitOptions.TrimEntries);

        if (parts.Length != 2
            || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
            || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon)
            || lat is < -90 or > 90
            || lon is < -180 or > 180)
        {
            return false;
        }

        point = new GeoPoint(lat, lon);
        return true;
    }

    /// <summary>The label part of "label | lat,lon", or null when there is none.</summary>
    public static string? Label(string text) =>
        text.Contains('|') ? text[..text.IndexOf('|')].Trim() is { Length: > 0 } label ? label : null : null;
}

public sealed class NominatimOptions
{
    public string Endpoint { get; init; } = "https://nominatim.openstreetmap.org/search";

    /// <summary>Nominatim's public usage policy allows at most one request per second.</summary>
    public TimeSpan MinInterval { get; init; } = TimeSpan.FromMilliseconds(1100);

    public string UserAgent { get; init; } = "FuelCalculator/0.1 (route fuel planning)";
}

/// <summary>
/// Place search with OpenStreetMap's Nominatim. Results are cached for the life of the instance, and
/// requests are spaced to respect the public server's one-per-second limit. Data © OpenStreetMap contributors.
/// </summary>
public sealed class NominatimGeocoder(HttpClient httpClient, NominatimOptions? options = null) : IGeocoder
{
    private readonly NominatimOptions _options = options ?? new NominatimOptions();
    private readonly ConcurrentDictionary<string, GeocodedPlace?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _lastCall = DateTimeOffset.MinValue;

    public async Task<GeocodedPlace?> GeocodeAsync(string query, CancellationToken cancellationToken = default)
    {
        var key = query.Trim();

        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        await _gate.WaitAsync(cancellationToken);

        try
        {
            var wait = _lastCall + _options.MinInterval - DateTimeOffset.UtcNow;

            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, cancellationToken);
            }

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{_options.Endpoint}?format=jsonv2&limit=1&q={Uri.EscapeDataString(key)}");
            request.Headers.UserAgent.ParseAdd(_options.UserAgent);

            using var response = await httpClient.SendAsync(request, cancellationToken);
            _lastCall = DateTimeOffset.UtcNow;
            response.EnsureSuccessStatusCode();

            var place = Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            _cache[key] = place;
            return place;
        }
        finally
        {
            _gate.Release();
        }
    }

    public static GeocodedPlace? Parse(string json)
    {
        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() == 0)
        {
            return null;
        }

        var first = document.RootElement[0];
        var lat = double.Parse(first.GetProperty("lat").GetString()!, CultureInfo.InvariantCulture);
        var lon = double.Parse(first.GetProperty("lon").GetString()!, CultureInfo.InvariantCulture);
        var name = first.TryGetProperty("display_name", out var display) ? display.GetString() : null;
        return new GeocodedPlace(new GeoPoint(lat, lon), name ?? $"{lat:0.####},{lon:0.####}");
    }
}

public sealed class OsrmOptions
{
    /// <summary>The public demo server is for light use only; host your own OSRM for production.</summary>
    public string Endpoint { get; init; } = "https://router.project-osrm.org";

    public string UserAgent { get; init; } = "FuelCalculator/0.1 (route fuel planning)";
}

/// <summary>
/// Driving routes from OSRM (OpenStreetMap road data), with stops found by <see cref="IGeocoder"/>
/// unless they are given as GPS coordinates. Needs no API key.
/// </summary>
public sealed class OsrmRouteProvider(HttpClient httpClient, IGeocoder geocoder, OsrmOptions? options = null) : IRouteProvider
{
    private readonly OsrmOptions _options = options ?? new OsrmOptions();

    public async Task<RouteResult> GetRouteAsync(RouteRequest request, CancellationToken cancellationToken = default)
    {
        var stops = new List<GeocodedPlace>();

        foreach (var stop in new[] { request.Origin }.Concat(request.Waypoints).Append(request.Destination))
        {
            stops.Add(await ResolveAsync(stop, cancellationToken));
        }

        var coordinates = new StringBuilder();

        foreach (var stop in stops)
        {
            if (coordinates.Length > 0)
            {
                coordinates.Append(';');
            }

            coordinates.Append(Format(stop.Point.Longitude)).Append(',').Append(Format(stop.Point.Latitude));
        }

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"{_options.Endpoint}/route/v1/driving/{coordinates}?overview=full&geometries=polyline&steps=false");
        httpRequest.Headers.UserAgent.ParseAdd(_options.UserAgent);

        using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = document.RootElement;
        var code = root.TryGetProperty("code", out var codeElement) ? codeElement.GetString() : null;

        if (code != "Ok" || !root.TryGetProperty("routes", out var routes) || routes.GetArrayLength() == 0)
        {
            var detail = root.TryGetProperty("message", out var message) ? message.GetString() : code;
            throw new RouteNotFoundException($"No road route was found between these stops ({detail ?? $"HTTP {(int)response.StatusCode}"}).");
        }

        var route = routes[0];
        var encoded = route.GetProperty("geometry").GetString() ?? string.Empty;
        var legs = new List<RouteLeg>();
        var index = 0;

        foreach (var leg in route.GetProperty("legs").EnumerateArray())
        {
            legs.Add(new RouteLeg(
                stops[index].Name,
                stops[index + 1].Name,
                leg.GetProperty("distance").GetDouble() / 1000d,
                TimeSpan.FromSeconds(leg.GetProperty("duration").GetDouble()),
                null,
                []));
            index++;
        }

        return new RouteResult(
            route.GetProperty("distance").GetDouble() / 1000d,
            TimeSpan.FromSeconds(route.GetProperty("duration").GetDouble()),
            encoded,
            PolylineCodec.Decode(encoded),
            legs);
    }

    private async Task<GeocodedPlace> ResolveAsync(string stop, CancellationToken cancellationToken)
    {
        if (RouteStopParser.TryParseCoordinates(stop, out var point))
        {
            return new GeocodedPlace(point, RouteStopParser.Label(stop) ?? $"{point.Latitude:0.####},{point.Longitude:0.####}");
        }

        return await geocoder.GeocodeAsync(stop, cancellationToken)
            ?? throw new RouteNotFoundException($"Could not find '{stop}'. Check the spelling, add the country, or enter GPS coordinates.");
    }

    private static string Format(double value) => Math.Round(value, 6).ToString("0.######", CultureInfo.InvariantCulture);
}

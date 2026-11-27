using System.Globalization;
using System.Text;
using System.Text.Json;
using FuelCalculator.Core.Domain;
using FuelCalculator.Core.Routing;
using FuelCalculator.Core.Segmentation;

namespace FuelCalculator.Core.Planning;

/// <summary>A fuel station from map data.</summary>
public sealed record FuelStation(long Id, double Latitude, double Longitude, string? Name, string? Brand);

public interface IFuelStationProvider
{
    /// <summary>Fuel stations within <paramref name="corridorKm"/> of the route line.</summary>
    Task<IReadOnlyList<FuelStation>> GetStationsAsync(
        IReadOnlyList<GeoPoint> routePoints,
        double corridorKm,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Measures where a point lies relative to a route: how far along it and how far off it.
/// Uses a flat projection per route segment, which is accurate to metres at corridor scale.
/// </summary>
public sealed class RouteProjection
{
    private const double KmPerDegreeLatitude = 110.574d;
    private const double KmPerDegreeLongitudeAtEquator = 111.320d;

    private readonly IReadOnlyList<GeoPoint> _points;
    private readonly double[] _cumulativeKm;

    public RouteProjection(IReadOnlyList<GeoPoint> points)
    {
        if (points.Count < 2)
        {
            throw new ArgumentException("A route needs at least two points.", nameof(points));
        }

        _points = points;
        _cumulativeKm = new double[points.Count];

        for (var i = 1; i < points.Count; i++)
        {
            _cumulativeKm[i] = _cumulativeKm[i - 1] + GeoMath.HaversineKm(points[i - 1], points[i]);
        }
    }

    public double TotalKm => _cumulativeKm[^1];

    public IReadOnlyList<GeoPoint> Points => _points;

    public (double AlongKm, double OffsetKm) Project(GeoPoint point)
    {
        var bestOffset = double.MaxValue;
        var bestAlong = 0d;

        for (var i = 1; i < _points.Count; i++)
        {
            var a = _points[i - 1];
            var b = _points[i];
            var kmPerLon = KmPerDegreeLongitudeAtEquator * Math.Cos(a.Latitude * Math.PI / 180d);
            var bx = (b.Longitude - a.Longitude) * kmPerLon;
            var by = (b.Latitude - a.Latitude) * KmPerDegreeLatitude;
            var px = (point.Longitude - a.Longitude) * kmPerLon;
            var py = (point.Latitude - a.Latitude) * KmPerDegreeLatitude;
            var lengthSquared = bx * bx + by * by;
            var t = lengthSquared == 0 ? 0 : Math.Clamp((px * bx + py * by) / lengthSquared, 0d, 1d);
            var dx = px - t * bx;
            var dy = py - t * by;
            var offset = Math.Sqrt(dx * dx + dy * dy);

            if (offset < bestOffset)
            {
                bestOffset = offset;
                bestAlong = _cumulativeKm[i - 1] + t * (_cumulativeKm[i] - _cumulativeKm[i - 1]);
            }
        }

        return (bestAlong, bestOffset);
    }

    /// <summary>Route points thinned to at least <paramref name="minSpacingKm"/> apart, keeping both ends.</summary>
    public IReadOnlyList<GeoPoint> Thin(double minSpacingKm)
    {
        var kept = new List<GeoPoint> { _points[0] };
        var lastKm = 0d;

        for (var i = 1; i < _points.Count - 1; i++)
        {
            if (_cumulativeKm[i] - lastKm >= minSpacingKm)
            {
                kept.Add(_points[i]);
                lastKm = _cumulativeKm[i];
            }
        }

        kept.Add(_points[^1]);
        return kept;
    }
}

public static class FuelStationSnapper
{
    /// <summary>
    /// Turns map stations into refuel candidates: keeps those within <paramref name="corridorKm"/> of the
    /// route, places each at its distance along the route, and tags it with the country it stands in.
    /// Stations within 200 m of each other along the route count once.
    /// </summary>
    public static async Task<IReadOnlyList<RefuelCandidate>> SnapAsync(
        RouteProjection route,
        IEnumerable<FuelStation> stations,
        double corridorKm,
        ICountryResolver countryResolver,
        CancellationToken cancellationToken = default)
    {
        var candidates = new List<RefuelCandidate>();

        foreach (var station in stations)
        {
            var point = new GeoPoint(station.Latitude, station.Longitude);
            var (alongKm, offsetKm) = route.Project(point);

            if (offsetKm > corridorKm || alongKm >= route.TotalKm)
            {
                continue;
            }

            var country = await countryResolver.ResolveCountryCodeAsync(point, cancellationToken);

            if (country is null)
            {
                continue;
            }

            candidates.Add(new RefuelCandidate(Math.Round(alongKm, 3), country, station.Name ?? station.Brand));
        }

        var ordered = candidates.OrderBy(candidate => candidate.DistanceKm).ToList();
        var distinct = new List<RefuelCandidate>();

        foreach (var candidate in ordered)
        {
            if (distinct.Count > 0 && candidate.DistanceKm - distinct[^1].DistanceKm < 0.2d)
            {
                if (distinct[^1].Name is null && candidate.Name is not null)
                {
                    distinct[^1] = candidate;
                }

                continue;
            }

            distinct.Add(candidate);
        }

        return distinct;
    }
}

public sealed class OverpassOptions
{
    /// <summary>Overpass servers to try in order; the next one is used when a server is busy or down.</summary>
    public IReadOnlyList<string> Endpoints { get; init; } =
    [
        "https://overpass-api.de/api/interpreter",
        "https://overpass.private.coffee/api/interpreter"
    ];

    /// <summary>Route length per Overpass request; long routes are split to keep each request light.</summary>
    public double ChunkKm { get; init; } = 500d;

    public string UserAgent { get; init; } = "FuelCalculator/0.1 (route fuel planning)";
}

/// <summary>
/// Fuel stations (<c>amenity=fuel</c>) from OpenStreetMap through the Overpass API.
/// Data © OpenStreetMap contributors, ODbL. Mind the public instance's usage policy.
/// </summary>
public sealed class OverpassFuelStationProvider(HttpClient httpClient, OverpassOptions? options = null) : IFuelStationProvider
{
    private readonly OverpassOptions _options = options ?? new OverpassOptions();

    public async Task<IReadOnlyList<FuelStation>> GetStationsAsync(
        IReadOnlyList<GeoPoint> routePoints,
        double corridorKm,
        CancellationToken cancellationToken = default)
    {
        var route = new RouteProjection(routePoints);
        var thinned = route.Thin(0.5d);
        var stations = new Dictionary<long, FuelStation>();

        foreach (var chunk in Chunk(thinned, _options.ChunkKm))
        {
            foreach (var station in Parse(await QueryAsync(BuildQuery(chunk, corridorKm), cancellationToken)))
            {
                stations.TryAdd(station.Id, station);
            }
        }

        return stations.Values.ToArray();
    }

    private async Task<string> QueryAsync(string query, CancellationToken cancellationToken)
    {
        HttpRequestException? lastError = null;

        foreach (var endpoint in _options.Endpoints)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new FormUrlEncodedContent([new KeyValuePair<string, string>("data", query)])
            };
            request.Headers.UserAgent.ParseAdd(_options.UserAgent);

            try
            {
                using var response = await httpClient.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync(cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                // Busy (429), gateway errors and timeouts on the public servers are common; try the next one.
                lastError = ex;
            }
        }

        throw lastError ?? new HttpRequestException("No Overpass endpoint is configured.");
    }

    /// <summary>Route length covered by one bounding box in the query.</summary>
    public const double BoxKm = 50d;

    /// <summary>
    /// Overpass QL for fuel stations near the route: one bounding box per <see cref="BoxKm"/> of route,
    /// widened by the corridor. Box lookups use Overpass's spatial index, so they stay fast on long routes;
    /// stations inside a box but outside the corridor are dropped later by <see cref="FuelStationSnapper"/>.
    /// </summary>
    public static string BuildQuery(IReadOnlyList<GeoPoint> points, double corridorKm)
    {
        var query = new StringBuilder("[out:json][timeout:90];(");

        foreach (var piece in BoxPieces(points))
        {
            var south = piece.Min(point => point.Latitude);
            var north = piece.Max(point => point.Latitude);
            var west = piece.Min(point => point.Longitude);
            var east = piece.Max(point => point.Longitude);
            var marginLat = corridorKm / 110.574d;
            var widestLat = Math.Max(Math.Abs(south), Math.Abs(north));
            var marginLon = corridorKm / (111.320d * Math.Max(0.01d, Math.Cos(widestLat * Math.PI / 180d)));

            query.Append("nwr[amenity=fuel](")
                .Append(Format(south - marginLat)).Append(',')
                .Append(Format(west - marginLon)).Append(',')
                .Append(Format(north + marginLat)).Append(',')
                .Append(Format(east + marginLon)).Append(");");
        }

        return query.Append(");out center tags;").ToString();
    }

    public static IReadOnlyList<FuelStation> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var stations = new List<FuelStation>();

        foreach (var element in document.RootElement.GetProperty("elements").EnumerateArray())
        {
            double lat, lon;

            if (element.TryGetProperty("lat", out var latValue) && element.TryGetProperty("lon", out var lonValue))
            {
                lat = latValue.GetDouble();
                lon = lonValue.GetDouble();
            }
            else if (element.TryGetProperty("center", out var center))
            {
                lat = center.GetProperty("lat").GetDouble();
                lon = center.GetProperty("lon").GetDouble();
            }
            else
            {
                continue;
            }

            string? name = null, brand = null;

            if (element.TryGetProperty("tags", out var tags))
            {
                name = tags.TryGetProperty("name", out var n) ? n.GetString() : null;
                brand = tags.TryGetProperty("brand", out var b) ? b.GetString() : null;
            }

            var id = element.GetProperty("id").GetInt64();
            var typeOffset = element.TryGetProperty("type", out var type) && type.GetString() == "way" ? 1L << 40 : 0L;
            stations.Add(new FuelStation(id + typeOffset, lat, lon, name ?? brand, brand));
        }

        return stations;
    }

    /// <summary>Splits the route into pieces of at most <see cref="BoxKm"/>, adding points every 10 km or less.</summary>
    private static IEnumerable<IReadOnlyList<GeoPoint>> BoxPieces(IReadOnlyList<GeoPoint> points)
    {
        var dense = new List<GeoPoint> { points[0] };

        for (var i = 1; i < points.Count; i++)
        {
            var steps = Math.Max(1, (int)Math.Ceiling(GeoMath.HaversineKm(points[i - 1], points[i]) / 10d));

            for (var step = 1; step <= steps; step++)
            {
                dense.Add(GeoMath.Interpolate(points[i - 1], points[i], step / (double)steps));
            }
        }

        var piece = new List<GeoPoint> { dense[0] };
        var length = 0d;

        for (var i = 1; i < dense.Count; i++)
        {
            var step = GeoMath.HaversineKm(dense[i - 1], dense[i]);

            if (length + step > BoxKm && piece.Count > 1)
            {
                yield return piece;
                piece = [dense[i - 1]];
                length = 0d;
            }

            piece.Add(dense[i]);
            length += step;
        }

        yield return piece;
    }

    private static IEnumerable<IReadOnlyList<GeoPoint>> Chunk(IReadOnlyList<GeoPoint> points, double chunkKm)
    {
        var current = new List<GeoPoint> { points[0] };
        var length = 0d;

        for (var i = 1; i < points.Count; i++)
        {
            current.Add(points[i]);
            length += GeoMath.HaversineKm(points[i - 1], points[i]);

            if (length >= chunkKm && i < points.Count - 1)
            {
                yield return current;
                current = [points[i]];
                length = 0d;
            }
        }

        yield return current;
    }

    private static string Format(double value) => Math.Round(value, 5).ToString("0.#####", CultureInfo.InvariantCulture);
}

using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using FuelCalculator.Core.Domain;
using FuelCalculator.Core.Routing;
using FuelCalculator.Core.Segmentation;

namespace FuelCalculator.Core.Planning;

/// <summary>A fuel station from map data.</summary>
public sealed record FuelStation(long Id, double Latitude, double Longitude, string? Name, string? Brand)
{
    /// <summary>OpenStreetMap <c>fuel:*</c> tags, without the prefix: "diesel" → true means <c>fuel:diesel=yes</c>.</summary>
    public IReadOnlyDictionary<string, bool>? Fuels { get; init; }

    /// <summary>True for <c>opening_hours=24/7</c>, false for any other hours, null when not mapped.</summary>
    public bool? Open24Hours { get; init; }

    /// <summary>Whether the station sells the grade: true or false when tagged, null when the map doesn't say.</summary>
    public bool? Sells(FuelType fuelType)
    {
        if (Fuels is null)
        {
            return null;
        }

        string[] keys = fuelType switch
        {
            FuelType.DIESEL => ["diesel"],
            FuelType.GASOLINE_91 => ["octane_91", "octane_92"],
            FuelType.GASOLINE_95 => ["octane_95"],
            _ => ["octane_98", "octane_100"]
        };

        var known = keys.Where(Fuels.ContainsKey).Select(key => Fuels[key]).ToArray();
        return known.Length == 0 ? null : known.Any(value => value);
    }
}

public interface IFuelStationProvider
{
    /// <summary>Fuel stations within <paramref name="corridorKm"/> of the route line (and possibly a little beyond).</summary>
    Task<IReadOnlyList<FuelStation>> GetStationsAsync(
        IReadOnlyList<GeoPoint> routePoints,
        double corridorKm,
        CancellationToken cancellationToken = default);
}

/// <summary>What is already known about the stations along a route, without any network call.</summary>
public sealed record StationLookup(IReadOnlyList<FuelStation> Stations, IReadOnlyList<StationTile> Missing, int TotalTiles)
{
    public bool IsComplete => Missing.Count == 0;
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
}

public static class FuelStationSnapper
{
    /// <summary>
    /// Turns map stations into refuel candidates: keeps those within <paramref name="corridorKm"/> of the
    /// route, places each at its distance along the route, and tags it with the country it stands in.
    /// With <paramref name="fuelType"/>, stations tagged as not selling it are skipped (untagged ones stay).
    /// Stations within 200 m of each other along the route count once.
    /// </summary>
    public static async Task<IReadOnlyList<RefuelCandidate>> SnapAsync(
        RouteProjection route,
        IEnumerable<FuelStation> stations,
        double corridorKm,
        ICountryResolver countryResolver,
        FuelType? fuelType = null,
        CancellationToken cancellationToken = default)
    {
        var candidates = new List<RefuelCandidate>();

        foreach (var station in stations)
        {
            if (fuelType is { } wanted && station.Sells(wanted) == false)
            {
                continue;
            }

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

            candidates.Add(new RefuelCandidate(Math.Round(alongKm, 3), country, station.Name ?? station.Brand, Open24Hours: station.Open24Hours));
        }

        var distinct = new List<RefuelCandidate>();

        foreach (var candidate in candidates.OrderBy(candidate => candidate.DistanceKm))
        {
            if (distinct.Count > 0 && candidate.DistanceKm - distinct[^1].DistanceKm < 0.2d)
            {
                if ((distinct[^1].Name is null && candidate.Name is not null) || (distinct[^1].Open24Hours != true && candidate.Open24Hours == true))
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

/// <summary>A fixed 0.1° × 0.1° map tile (about 11 km), the unit stations are fetched and cached in.</summary>
public readonly record struct StationTile(int X, int Y)
{
    private const double Scale = 10d;

    public double South => Y / Scale;

    public double West => X / Scale;

    public double North => (Y + 1) / Scale;

    public double East => (X + 1) / Scale;

    public static StationTile At(double latitude, double longitude) =>
        new((int)Math.Floor(longitude * Scale), (int)Math.Floor(latitude * Scale));

    /// <summary>Every tile within <paramref name="corridorKm"/> of the route line.</summary>
    public static IReadOnlyList<StationTile> ForRoute(IReadOnlyList<GeoPoint> points, double corridorKm)
    {
        var tiles = new HashSet<StationTile>();
        var marginLat = corridorKm / 110.574d;

        for (var i = 0; i < points.Count; i++)
        {
            var from = points[i];
            var to = i + 1 < points.Count ? points[i + 1] : from;
            var steps = Math.Max(1, (int)Math.Ceiling(GeoMath.HaversineKm(from, to) / 1d));

            for (var step = 0; step <= steps; step++)
            {
                var point = GeoMath.Interpolate(from, to, step / (double)steps);
                var marginLon = corridorKm / (111.320d * Math.Max(0.01d, Math.Cos(point.Latitude * Math.PI / 180d)));
                var low = At(point.Latitude - marginLat, point.Longitude - marginLon);
                var high = At(point.Latitude + marginLat, point.Longitude + marginLon);

                for (var x = low.X; x <= high.X; x++)
                {
                    for (var y = low.Y; y <= high.Y; y++)
                    {
                        tiles.Add(new StationTile(x, y));
                    }
                }
            }
        }

        return tiles.OrderBy(tile => tile.X).ThenBy(tile => tile.Y).ToArray();
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

    /// <summary>Most tiles asked for in one request; small batches finish (and get cached) even on a slow server.</summary>
    public int TilesPerRequest { get; init; } = 25;

    /// <summary>Folder for the per-tile station cache; null keeps the cache in memory only.</summary>
    public string? CacheFolder { get; init; }

    /// <summary>How long a cached tile is trusted.</summary>
    public TimeSpan CacheLifetime { get; init; } = TimeSpan.FromDays(30);

    public string UserAgent { get; init; } = "FuelCalculator/0.1 (route fuel planning)";
}

/// <summary>
/// Fuel stations (<c>amenity=fuel</c>) from OpenStreetMap through the Overpass API, fetched and cached per
/// 0.1° tile. Data © OpenStreetMap contributors, ODbL. Mind the public instances' usage policy.
/// </summary>
public sealed class OverpassFuelStationProvider(HttpClient httpClient, OverpassOptions? options = null) : IFuelStationProvider
{
    private static readonly JsonSerializerOptions CacheJson = new(JsonSerializerDefaults.Web);

    private readonly OverpassOptions _options = options ?? new OverpassOptions();
    private readonly ConcurrentDictionary<StationTile, IReadOnlyList<FuelStation>> _memory = new();

    public async Task<IReadOnlyList<FuelStation>> GetStationsAsync(
        IReadOnlyList<GeoPoint> routePoints,
        double corridorKm,
        CancellationToken cancellationToken = default)
    {
        var lookup = Lookup(routePoints, corridorKm);

        if (lookup.IsComplete)
        {
            return lookup.Stations;
        }

        await FetchAsync(lookup.Missing, cancellationToken);
        return Lookup(routePoints, corridorKm).Stations;
    }

    /// <summary>The stations already cached for the route, and the tiles still to fetch. No network call.</summary>
    public StationLookup Lookup(IReadOnlyList<GeoPoint> routePoints, double corridorKm)
    {
        var tiles = StationTile.ForRoute(routePoints, corridorKm);
        var stations = new Dictionary<long, FuelStation>();
        var missing = new List<StationTile>();

        foreach (var tile in tiles)
        {
            if (TryReadCache(tile) is { } cached)
            {
                foreach (var station in cached)
                {
                    stations.TryAdd(station.Id, station);
                }
            }
            else
            {
                missing.Add(tile);
            }
        }

        return new StationLookup(stations.Values.ToArray(), missing, tiles.Count);
    }

    /// <summary>
    /// Fetches the tiles in batches, one request at a time, caching each batch as it arrives, so an
    /// interrupted fetch keeps what it got.
    /// </summary>
    public async Task FetchAsync(IReadOnlyList<StationTile> tiles, CancellationToken cancellationToken = default)
    {
        foreach (var batch in tiles.Where(tile => TryReadCache(tile) is null).Chunk(Math.Max(1, _options.TilesPerRequest)))
        {
            var found = Parse(await QueryAsync(BuildQuery(batch), cancellationToken));
            var byTile = batch.ToDictionary(tile => tile, _ => new List<FuelStation>());

            foreach (var station in found)
            {
                if (byTile.TryGetValue(StationTile.At(station.Latitude, station.Longitude), out var list))
                {
                    list.Add(station);
                }
            }

            foreach (var (tile, list) in byTile)
            {
                WriteCache(tile, list);
            }
        }
    }

    /// <summary>Overpass QL for every fuel station inside the given tiles.</summary>
    public static string BuildQuery(IEnumerable<StationTile> tiles)
    {
        var query = new StringBuilder("[out:json][timeout:90];(");

        foreach (var tile in tiles)
        {
            query.Append("nwr[amenity=fuel](")
                .Append(Format(tile.South)).Append(',')
                .Append(Format(tile.West)).Append(',')
                .Append(Format(tile.North)).Append(',')
                .Append(Format(tile.East)).Append(");");
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
            Dictionary<string, bool>? fuels = null;
            bool? open24 = null;

            if (element.TryGetProperty("tags", out var tags))
            {
                foreach (var tag in tags.EnumerateObject())
                {
                    var value = tag.Value.GetString();

                    switch (tag.Name)
                    {
                        case "name":
                            name = value;
                            break;
                        case "brand":
                            brand = value;
                            break;
                        case "opening_hours":
                            open24 = value?.Trim() == "24/7";
                            break;
                        default:
                            if (tag.Name.StartsWith("fuel:", StringComparison.Ordinal) && value is "yes" or "no")
                            {
                                fuels ??= new Dictionary<string, bool>();
                                fuels[tag.Name["fuel:".Length..]] = value == "yes";
                            }

                            break;
                    }
                }
            }

            var id = element.GetProperty("id").GetInt64();
            var typeOffset = element.TryGetProperty("type", out var type) && type.GetString() == "way" ? 1L << 40 : 0L;
            stations.Add(new FuelStation(id + typeOffset, lat, lon, name ?? brand, brand) { Fuels = fuels, Open24Hours = open24 });
        }

        return stations;
    }

    private IReadOnlyList<FuelStation>? TryReadCache(StationTile tile)
    {
        if (_memory.TryGetValue(tile, out var inMemory))
        {
            return inMemory;
        }

        var path = CachePath(tile);

        if (path is null || !File.Exists(path) || File.GetLastWriteTimeUtc(path) < DateTime.UtcNow - _options.CacheLifetime)
        {
            return null;
        }

        try
        {
            var stations = JsonSerializer.Deserialize<FuelStation[]>(File.ReadAllText(path), CacheJson) ?? [];
            _memory[tile] = stations;
            return stations;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null;
        }
    }

    private void WriteCache(StationTile tile, IReadOnlyList<FuelStation> stations)
    {
        _memory[tile] = stations;
        var path = CachePath(tile);

        if (path is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(stations, CacheJson));
        }
        catch (IOException)
        {
            // The cache is an optimisation; a failed write only means the tile is fetched again.
        }
    }

    private string? CachePath(StationTile tile) =>
        _options.CacheFolder is { } folder ? Path.Combine(folder, $"{tile.X}_{tile.Y}.json") : null;

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

    private static string Format(double value) => Math.Round(value, 5).ToString("0.#####", CultureInfo.InvariantCulture);
}

/// <summary>
/// Fetches missing station tiles in the background, one batch at a time, so estimates never wait on a slow
/// Overpass server: an estimate uses what is cached and the next one picks up what arrived meanwhile.
/// </summary>
public sealed class StationPrefetcher(OverpassFuelStationProvider provider)
{
    private readonly ConcurrentDictionary<StationTile, byte> _queued = new();
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);

    /// <summary>Raised with the error when a background fetch fails; the tiles can be queued again.</summary>
    public event Action<Exception>? Failed;

    /// <summary>Queues tiles that are not already being fetched. Returns at once.</summary>
    public void Queue(IReadOnlyList<StationTile> tiles, CancellationToken stopping)
    {
        var fresh = tiles.Where(tile => _queued.TryAdd(tile, 0)).ToArray();

        if (fresh.Length == 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            await _oneAtATime.WaitAsync(stopping);

            try
            {
                await provider.FetchAsync(fresh, stopping);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !stopping.IsCancellationRequested)
            {
                Failed?.Invoke(ex);
            }
            finally
            {
                foreach (var tile in fresh)
                {
                    _queued.TryRemove(tile, out _);
                }

                _oneAtATime.Release();
            }
        }, stopping);
    }
}

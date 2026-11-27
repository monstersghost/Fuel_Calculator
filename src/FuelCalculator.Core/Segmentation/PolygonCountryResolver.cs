using System.Text.Json;
using FuelCalculator.Core.Domain;

namespace FuelCalculator.Core.Segmentation;

/// <summary>
/// Resolves a point to a country with real border polygons (Natural Earth 1:50m, cut to the region).
/// A point inside no polygon, such as a causeway or a coastal road the 1:50m coastline cuts off,
/// takes the nearest country within <see cref="SnapDistanceKm"/>; beyond that it is unknown.
/// </summary>
public sealed class PolygonCountryResolver : ICountryResolver
{
    private const string ResourceName = "FuelCalculator.Core.world-countries.json";
    private const double KmPerDegreeLatitude = 110.574d;
    private const double KmPerDegreeLongitudeAtEquator = 111.320d;

    private static readonly Lazy<PolygonCountryResolver> Embedded = new(LoadFromResource);

    private readonly IReadOnlyList<CountryShape> _countries;

    public PolygonCountryResolver(IReadOnlyList<CountryShape> countries, double snapDistanceKm = 25d)
    {
        _countries = countries;
        SnapDistanceKm = snapDistanceKm;
    }

    public double SnapDistanceKm { get; }

    /// <summary>The shared instance built from the embedded border file; parsed once.</summary>
    public static PolygonCountryResolver LoadEmbedded() => Embedded.Value;

    public Task<string?> ResolveCountryCodeAsync(GeoPoint point, CancellationToken cancellationToken = default)
    {
        foreach (var country in _countries)
        {
            if (country.Contains(point.Longitude, point.Latitude))
            {
                return Task.FromResult<string?>(country.Code);
            }
        }

        return Task.FromResult(NearestWithinSnap(point));
    }

    private string? NearestWithinSnap(GeoPoint point)
    {
        var snapDegreesLat = SnapDistanceKm / KmPerDegreeLatitude;
        var kmPerDegreeLon = KmPerDegreeLongitudeAtEquator * Math.Cos(point.Latitude * Math.PI / 180d);
        var snapDegreesLon = kmPerDegreeLon > 0 ? SnapDistanceKm / kmPerDegreeLon : 180d;

        string? best = null;
        var bestKm = SnapDistanceKm;

        foreach (var country in _countries)
        {
            foreach (var polygon in country.Polygons)
            {
                if (!polygon.BoundsContain(point.Longitude, point.Latitude, snapDegreesLon, snapDegreesLat))
                {
                    continue;
                }

                var km = polygon.DistanceToOuterRingKm(point.Longitude, point.Latitude, kmPerDegreeLon);

                if (km <= bestKm)
                {
                    bestKm = km;
                    best = country.Code;
                }
            }
        }

        return best;
    }

    private static PolygonCountryResolver LoadFromResource()
    {
        using var stream = typeof(PolygonCountryResolver).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded border data '{ResourceName}' is missing.");
        using var document = JsonDocument.Parse(stream);

        var countries = new List<CountryShape>();

        foreach (var country in document.RootElement.GetProperty("countries").EnumerateArray())
        {
            var polygons = new List<PolygonShape>();

            foreach (var polygon in country.GetProperty("polygons").EnumerateArray())
            {
                var rings = polygon.EnumerateArray()
                    .Select(ring => ring.EnumerateArray().Select(value => value.GetDouble()).ToArray())
                    .ToArray();
                polygons.Add(new PolygonShape(rings));
            }

            countries.Add(new CountryShape(country.GetProperty("code").GetString()!, polygons));
        }

        return new PolygonCountryResolver(countries);
    }
}

public sealed class CountryShape(string code, IReadOnlyList<PolygonShape> polygons)
{
    public string Code { get; } = code;

    public IReadOnlyList<PolygonShape> Polygons { get; } = polygons;

    public bool Contains(double lon, double lat)
    {
        foreach (var polygon in Polygons)
        {
            if (polygon.Contains(lon, lat))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>An outer ring plus optional holes; each ring is a flat [lon, lat, lon, lat, ...] array.</summary>
public sealed class PolygonShape
{
    private readonly double[][] _rings;
    private readonly double _minLon;
    private readonly double _maxLon;
    private readonly double _minLat;
    private readonly double _maxLat;

    public PolygonShape(double[][] rings)
    {
        if (rings.Length == 0 || rings[0].Length < 6)
        {
            throw new ArgumentException("A polygon needs an outer ring of at least three points.", nameof(rings));
        }

        _rings = rings;
        var outer = rings[0];
        _minLon = _maxLon = outer[0];
        _minLat = _maxLat = outer[1];

        for (var i = 2; i < outer.Length; i += 2)
        {
            _minLon = Math.Min(_minLon, outer[i]);
            _maxLon = Math.Max(_maxLon, outer[i]);
            _minLat = Math.Min(_minLat, outer[i + 1]);
            _maxLat = Math.Max(_maxLat, outer[i + 1]);
        }
    }

    public bool BoundsContain(double lon, double lat, double marginLon = 0d, double marginLat = 0d) =>
        lon >= _minLon - marginLon && lon <= _maxLon + marginLon
        && lat >= _minLat - marginLat && lat <= _maxLat + marginLat;

    public bool Contains(double lon, double lat)
    {
        if (!BoundsContain(lon, lat) || !RingContains(_rings[0], lon, lat))
        {
            return false;
        }

        for (var hole = 1; hole < _rings.Length; hole++)
        {
            if (RingContains(_rings[hole], lon, lat))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Distance from the point to the outer ring's nearest edge, on a local flat projection.</summary>
    public double DistanceToOuterRingKm(double lon, double lat, double kmPerDegreeLon)
    {
        var ring = _rings[0];
        var best = double.MaxValue;

        for (int i = 0, j = ring.Length - 2; i < ring.Length; j = i, i += 2)
        {
            var ax = (ring[j] - lon) * kmPerDegreeLon;
            var ay = (ring[j + 1] - lat) * 110.574d;
            var bx = (ring[i] - lon) * kmPerDegreeLon;
            var by = (ring[i + 1] - lat) * 110.574d;
            best = Math.Min(best, DistanceFromOriginToSegment(ax, ay, bx, by));
        }

        return best;
    }

    // Even-odd ray cast towards +longitude.
    private static bool RingContains(double[] ring, double lon, double lat)
    {
        var inside = false;

        for (int i = 0, j = ring.Length - 2; i < ring.Length; j = i, i += 2)
        {
            double xi = ring[i], yi = ring[i + 1], xj = ring[j], yj = ring[j + 1];

            if ((yi > lat) != (yj > lat) && lon < (xj - xi) * (lat - yi) / (yj - yi) + xi)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static double DistanceFromOriginToSegment(double ax, double ay, double bx, double by)
    {
        var dx = bx - ax;
        var dy = by - ay;
        var lengthSquared = dx * dx + dy * dy;
        var t = lengthSquared == 0 ? 0 : Math.Clamp(-(ax * dx + ay * dy) / lengthSquared, 0d, 1d);
        var px = ax + t * dx;
        var py = ay + t * dy;
        return Math.Sqrt(px * px + py * py);
    }
}

using FuelCalculator.Core.Domain;

namespace FuelCalculator.Core.Routing;

public sealed class MockRouteProvider : IRouteProvider
{
    private static readonly GeoPoint KuwaitCity = new(29.3759, 47.9774);
    private static readonly GeoPoint HafrAlBatin = new(28.4342, 45.9636);
    private static readonly GeoPoint Riyadh = new(24.7136, 46.6753);
    private static readonly GeoPoint SalwaBorder = new(24.7446, 50.7456);
    private static readonly GeoPoint Doha = new(25.2854, 51.5310);
    private static readonly GeoPoint Dammam = new(26.4207, 50.0888);
    private static readonly GeoPoint Manama = new(26.2285, 50.5860);
    private static readonly GeoPoint UaeBorder = new(24.1531, 52.6372);
    private static readonly GeoPoint Dubai = new(25.2048, 55.2708);
    private static readonly GeoPoint OmanBorder = new(24.2186, 55.7545);
    private static readonly GeoPoint Muscat = new(23.5880, 58.3829);

    public Task<RouteResult> GetRouteAsync(RouteRequest request, CancellationToken cancellationToken = default)
    {
        var points = BuildRoutePoints(request);
        var encodedPolyline = PolylineCodec.Encode(points);
        var totalDistance = GeoMath.TotalDistanceKm(points);
        var duration = TimeSpan.FromHours(totalDistance / 90d);
        var legs = BuildLegs(points);

        return Task.FromResult(new RouteResult(
            totalDistance,
            duration,
            encodedPolyline,
            points,
            legs));
    }

    private static IReadOnlyList<GeoPoint> BuildRoutePoints(RouteRequest request)
    {
        var origin = DetectPlace(request.Origin);
        var destination = DetectPlace(request.Destination);
        var destinationText = request.Destination.ToUpperInvariant();
        var originText = request.Origin.ToUpperInvariant();

        IReadOnlyList<GeoPoint> corridor = destinationText switch
        {
            var text when text.Contains("QATAR", StringComparison.Ordinal) || text.Contains("DOHA", StringComparison.Ordinal) =>
                [KuwaitCity, HafrAlBatin, Riyadh, SalwaBorder, Doha],
            var text when text.Contains("BAHRAIN", StringComparison.Ordinal) || text.Contains("MANAMA", StringComparison.Ordinal) =>
                [KuwaitCity, HafrAlBatin, Dammam, Manama],
            var text when text.Contains("OMAN", StringComparison.Ordinal) || text.Contains("MUSCAT", StringComparison.Ordinal) =>
                [KuwaitCity, HafrAlBatin, Riyadh, UaeBorder, OmanBorder, Muscat],
            var text when text.Contains("UAE", StringComparison.Ordinal) || text.Contains("DUBAI", StringComparison.Ordinal) || text.Contains("ABU DHABI", StringComparison.Ordinal) =>
                [KuwaitCity, HafrAlBatin, Riyadh, UaeBorder, Dubai],
            _ => [origin ?? KuwaitCity, HafrAlBatin, destination ?? Doha]
        };

        if (originText.Contains("QATAR", StringComparison.Ordinal) || originText.Contains("DOHA", StringComparison.Ordinal)
            || originText.Contains("UAE", StringComparison.Ordinal) || originText.Contains("DUBAI", StringComparison.Ordinal)
            || originText.Contains("BAHRAIN", StringComparison.Ordinal) || originText.Contains("MANAMA", StringComparison.Ordinal)
            || originText.Contains("OMAN", StringComparison.Ordinal) || originText.Contains("MUSCAT", StringComparison.Ordinal))
        {
            corridor = corridor.Reverse().ToArray();
        }

        var points = corridor.ToList();

        foreach (var waypoint in request.Waypoints)
        {
            var waypointPoint = DetectPlace(waypoint);

            if (waypointPoint is not null && !points.Contains(waypointPoint))
            {
                points.Insert(Math.Max(1, points.Count - 1), waypointPoint);
            }
        }

        if (origin is not null)
        {
            points[0] = origin;
        }

        if (destination is not null)
        {
            points[^1] = destination;
        }

        return points;
    }

    private static GeoPoint? DetectPlace(string text)
    {
        var normalized = text.ToUpperInvariant();

        if (normalized.Contains("KUWAIT", StringComparison.Ordinal) || normalized.Contains("KWI", StringComparison.Ordinal))
        {
            return KuwaitCity;
        }

        if (normalized.Contains("RIYADH", StringComparison.Ordinal) || normalized.Contains("SAUDI", StringComparison.Ordinal))
        {
            return Riyadh;
        }

        if (normalized.Contains("QATAR", StringComparison.Ordinal) || normalized.Contains("DOHA", StringComparison.Ordinal))
        {
            return Doha;
        }

        if (normalized.Contains("DUBAI", StringComparison.Ordinal) || normalized.Contains("UAE", StringComparison.Ordinal) || normalized.Contains("ABU DHABI", StringComparison.Ordinal))
        {
            return Dubai;
        }

        if (normalized.Contains("BAHRAIN", StringComparison.Ordinal) || normalized.Contains("MANAMA", StringComparison.Ordinal))
        {
            return Manama;
        }

        if (normalized.Contains("OMAN", StringComparison.Ordinal) || normalized.Contains("MUSCAT", StringComparison.Ordinal))
        {
            return Muscat;
        }

        return null;
    }

    private static IReadOnlyList<RouteLeg> BuildLegs(IReadOnlyList<GeoPoint> points)
    {
        var legs = new List<RouteLeg>();

        for (var i = 1; i < points.Count; i++)
        {
            var distance = GeoMath.HaversineKm(points[i - 1], points[i]);
            var duration = TimeSpan.FromHours(distance / 90d);
            var encoded = PolylineCodec.Encode([points[i - 1], points[i]]);
            var step = new RouteStep(distance, duration, encoded, $"Mock drive leg {i}");

            legs.Add(new RouteLeg(
                $"Mock point {i}",
                $"Mock point {i + 1}",
                distance,
                duration,
                encoded,
                [step]));
        }

        return legs;
    }
}

using FuelCalculator.Core.Domain;

namespace FuelCalculator.Core.Routing;

public sealed class MockRouteProvider : IRouteProvider
{
    private static readonly GeoPoint KuwaitCity = new(29.3759, 47.9774);
    private static readonly GeoPoint HafrAlBatin = new(28.4342, 45.9636);
    private static readonly GeoPoint AlHadithah = new(31.5204, 37.1263);
    private static readonly GeoPoint AlOmariBorder = new(31.3342, 36.6559);
    private static readonly GeoPoint Damascus = new(33.5138, 36.2765);
    private static readonly GeoPoint AlNabk = new(34.0241, 36.7284);
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
        var allStopsText = string.Join(' ', [request.Origin, .. request.Waypoints, request.Destination]).ToUpperInvariant();

        IReadOnlyList<GeoPoint> corridor = allStopsText switch
        {
            var text when IsSyriaRoute(text) =>
                [KuwaitCity, HafrAlBatin, AlHadithah, AlOmariBorder, Damascus, AlNabk],
            var text when text.Contains("QATAR", StringComparison.Ordinal) || text.Contains("DOHA", StringComparison.Ordinal) =>
                [KuwaitCity, HafrAlBatin, Riyadh, SalwaBorder, Doha],
            var text when text.Contains("BAHRAIN", StringComparison.Ordinal) || text.Contains("MANAMA", StringComparison.Ordinal) =>
                [KuwaitCity, HafrAlBatin, Dammam, Manama],
            var text when text.Contains("OMAN", StringComparison.Ordinal) || text.Contains("MUSCAT", StringComparison.Ordinal) =>
                [KuwaitCity, HafrAlBatin, Riyadh, UaeBorder, OmanBorder, Muscat],
            var text when text.Contains("UAE", StringComparison.Ordinal) || text.Contains("DUBAI", StringComparison.Ordinal) || text.Contains("ABU DHABI", StringComparison.Ordinal) =>
                [KuwaitCity, HafrAlBatin, Riyadh, UaeBorder, Dubai],
            _ => destination is not null
                ? [origin ?? KuwaitCity, HafrAlBatin, destination]
                : [origin ?? KuwaitCity, HafrAlBatin]
        };

        if (originText.Contains("QATAR", StringComparison.Ordinal) || originText.Contains("DOHA", StringComparison.Ordinal)
            || originText.Contains("UAE", StringComparison.Ordinal) || originText.Contains("DUBAI", StringComparison.Ordinal)
            || originText.Contains("BAHRAIN", StringComparison.Ordinal) || originText.Contains("MANAMA", StringComparison.Ordinal)
            || originText.Contains("OMAN", StringComparison.Ordinal) || originText.Contains("MUSCAT", StringComparison.Ordinal)
            || IsSyriaRoute(originText))
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

        if (TryParseCoordinates(text, out var coordinatePoint))
        {
            return coordinatePoint;
        }

        if (normalized.Contains("HOME", StringComparison.Ordinal)
            || normalized.Contains("KUWAIT", StringComparison.Ordinal)
            || normalized.Contains("KWI", StringComparison.Ordinal))
        {
            return KuwaitCity;
        }

        if (normalized.Contains("HAFAR", StringComparison.Ordinal)
            || normalized.Contains("HAFR", StringComparison.Ordinal)
            || normalized.Contains("BATIN", StringComparison.Ordinal))
        {
            return HafrAlBatin;
        }

        if (normalized.Contains("HADITHAH", StringComparison.Ordinal)
            || normalized.Contains("HADITHA", StringComparison.Ordinal)
            || normalized.Contains("QURAYYAT", StringComparison.Ordinal))
        {
            return AlHadithah;
        }

        if (normalized.Contains("NABK", StringComparison.Ordinal))
        {
            return AlNabk;
        }

        if (normalized.Contains("DAMASCUS", StringComparison.Ordinal))
        {
            return Damascus;
        }

        if (normalized.Contains("SYRIA", StringComparison.Ordinal))
        {
            return AlNabk;
        }

        if (normalized.Contains("OMARI", StringComparison.Ordinal)
            || normalized.Contains("JORDAN", StringComparison.Ordinal)
            || normalized.Contains("AMMAN", StringComparison.Ordinal))
        {
            return AlOmariBorder;
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

    private static bool IsSyriaRoute(string text) =>
        text.Contains("SYRIA", StringComparison.Ordinal)
        || text.Contains("NABK", StringComparison.Ordinal)
        || text.Contains("DAMASCUS", StringComparison.Ordinal)
        || text.Contains("HADITHAH", StringComparison.Ordinal)
        || text.Contains("HADITHA", StringComparison.Ordinal);

    private static bool TryParseCoordinates(string text, out GeoPoint point)
    {
        point = default!;
        var matches = System.Text.RegularExpressions.Regex.Matches(
            text,
            @"[-+]?\d{1,2}(?:\.\d+)?",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        if (matches.Count < 2)
        {
            return false;
        }

        if (!double.TryParse(matches[0].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var latitude)
            || !double.TryParse(matches[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var longitude)
            || latitude is < -90 or > 90
            || longitude is < -180 or > 180)
        {
            return false;
        }

        point = new GeoPoint(latitude, longitude);
        return true;
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

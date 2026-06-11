using FuelCalculator.Core.Domain;

namespace FuelCalculator.Core.Routing;

public interface IGoogleMapsLinkParser
{
    bool TryParse(string? link, out ParsedRouteInput parsed);
}

public sealed class GoogleMapsLinkParser : IGoogleMapsLinkParser
{
    public bool TryParse(string? link, out ParsedRouteInput parsed)
    {
        parsed = new ParsedRouteInput(null, null, []);

        if (string.IsNullOrWhiteSpace(link) || !Uri.TryCreate(link, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Host.Contains("goo.gl", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Contains("maps.app.goo.gl", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!uri.Host.Contains("google.", StringComparison.OrdinalIgnoreCase)
            && !uri.Host.Contains("maps.google.", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (TryParseQuery(uri, out parsed))
        {
            return true;
        }

        return TryParseDirectionsPath(uri, out parsed);
    }

    private static bool TryParseQuery(Uri uri, out ParsedRouteInput parsed)
    {
        parsed = new ParsedRouteInput(null, null, []);
        var query = ParseQuery(uri.Query);

        if (!query.TryGetValue("origin", out var origin) || !query.TryGetValue("destination", out var destination))
        {
            return false;
        }

        var waypoints = query.TryGetValue("waypoints", out var waypointValue)
            ? waypointValue.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];

        parsed = new ParsedRouteInput(origin, destination, waypoints);
        return true;
    }

    private static bool TryParseDirectionsPath(Uri uri, out ParsedRouteInput parsed)
    {
        parsed = new ParsedRouteInput(null, null, []);
        var pathParts = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => Uri.UnescapeDataString(part.Replace('+', ' ')))
            .ToArray();
        var dirIndex = Array.FindIndex(pathParts, part => part.Equals("dir", StringComparison.OrdinalIgnoreCase));

        if (dirIndex < 0)
        {
            return false;
        }

        var routeParts = pathParts
            .Skip(dirIndex + 1)
            .Where(part =>
                !part.StartsWith('@')
                && !part.StartsWith("data=", StringComparison.OrdinalIgnoreCase)
                && !part.StartsWith("am=", StringComparison.OrdinalIgnoreCase)
                && !part.Contains('!'))
            .ToArray();

        if (routeParts.Length < 2)
        {
            return false;
        }

        parsed = new ParsedRouteInput(routeParts[0], routeParts[^1], routeParts.Skip(1).SkipLast(1).ToArray());
        return true;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);

            if (pair.Length == 2)
            {
                result[Uri.UnescapeDataString(pair[0])] = Uri.UnescapeDataString(pair[1].Replace('+', ' '));
            }
        }

        return result;
    }
}

using System.Net.Http.Json;
using System.Text.Json;
using FuelCalculator.Core.Domain;

namespace FuelCalculator.Core.Routing;

public sealed class GoogleRoutesOptions
{
    public string? ApiKey { get; init; }

    public string Endpoint { get; init; } = "https://routes.googleapis.com/directions/v2:computeRoutes";
}

public sealed class GoogleRoutesProvider(HttpClient httpClient, GoogleRoutesOptions options) : IRouteProvider
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly GoogleRoutesOptions _options = options;

    public async Task<RouteResult> GetRouteAsync(RouteRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("Google Routes API key is not configured.");
        }

        var body = new
        {
            origin = new { address = request.Origin },
            destination = new { address = request.Destination },
            intermediates = request.Waypoints.Select(waypoint => new { address = waypoint }).ToArray(),
            travelMode = "DRIVE",
            routingPreference = "TRAFFIC_UNAWARE",
            computeAlternativeRoutes = false,
            polylineEncoding = "ENCODED_POLYLINE"
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint)
        {
            Content = JsonContent.Create(body)
        };

        httpRequest.Headers.Add("X-Goog-Api-Key", _options.ApiKey);
        httpRequest.Headers.Add(
            "X-Goog-FieldMask",
            "routes.distanceMeters,routes.duration,routes.polyline.encodedPolyline,routes.legs.distanceMeters,routes.legs.duration,routes.legs.polyline.encodedPolyline,routes.legs.steps.distanceMeters,routes.legs.steps.staticDuration,routes.legs.steps.polyline.encodedPolyline,routes.legs.steps.navigationInstruction.instructions");

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var routes = document.RootElement.GetProperty("routes");

        if (routes.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("Google Routes API returned no routes.");
        }

        var route = routes[0];
        var distanceKm = route.TryGetProperty("distanceMeters", out var distanceElement)
            ? distanceElement.GetDouble() / 1000d
            : 0d;
        var duration = route.TryGetProperty("duration", out var durationElement)
            ? ParseGoogleDuration(durationElement.GetString())
            : null;
        var encodedPolyline = route.GetProperty("polyline").GetProperty("encodedPolyline").GetString() ?? string.Empty;
        var points = PolylineCodec.Decode(encodedPolyline);
        var legs = ParseLegs(route);

        return new RouteResult(distanceKm, duration, encodedPolyline, points, legs);
    }

    private static IReadOnlyList<RouteLeg> ParseLegs(JsonElement route)
    {
        if (!route.TryGetProperty("legs", out var legsElement) || legsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var legs = new List<RouteLeg>();
        var index = 1;

        foreach (var legElement in legsElement.EnumerateArray())
        {
            var distanceKm = legElement.TryGetProperty("distanceMeters", out var distanceElement)
                ? distanceElement.GetDouble() / 1000d
                : 0d;
            var duration = legElement.TryGetProperty("duration", out var durationElement)
                ? ParseGoogleDuration(durationElement.GetString())
                : null;
            var encodedPolyline = legElement.TryGetProperty("polyline", out var polylineElement)
                && polylineElement.TryGetProperty("encodedPolyline", out var encodedElement)
                    ? encodedElement.GetString()
                    : null;

            legs.Add(new RouteLeg(
                $"Leg {index}",
                $"Leg {index + 1}",
                distanceKm,
                duration,
                encodedPolyline,
                ParseSteps(legElement)));
            index++;
        }

        return legs;
    }

    private static IReadOnlyList<RouteStep> ParseSteps(JsonElement legElement)
    {
        if (!legElement.TryGetProperty("steps", out var stepsElement) || stepsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var steps = new List<RouteStep>();

        foreach (var stepElement in stepsElement.EnumerateArray())
        {
            var distanceKm = stepElement.TryGetProperty("distanceMeters", out var distanceElement)
                ? distanceElement.GetDouble() / 1000d
                : 0d;
            var duration = stepElement.TryGetProperty("staticDuration", out var durationElement)
                ? ParseGoogleDuration(durationElement.GetString())
                : null;
            var encodedPolyline = stepElement.TryGetProperty("polyline", out var polylineElement)
                && polylineElement.TryGetProperty("encodedPolyline", out var encodedElement)
                    ? encodedElement.GetString()
                    : null;
            var instructions = stepElement.TryGetProperty("navigationInstruction", out var instructionElement)
                && instructionElement.TryGetProperty("instructions", out var textElement)
                    ? textElement.GetString()
                    : null;

            steps.Add(new RouteStep(distanceKm, duration, encodedPolyline, instructions));
        }

        return steps;
    }

    private static TimeSpan? ParseGoogleDuration(string? duration)
    {
        if (string.IsNullOrWhiteSpace(duration) || !duration.EndsWith('s'))
        {
            return null;
        }

        return double.TryParse(duration[..^1], out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : null;
    }
}

namespace FuelCalculator.Core.Routing;

public interface IGoogleMapsLinkResolver
{
    Task<string?> ResolveAsync(string link, CancellationToken cancellationToken = default);
}

public sealed class GoogleMapsLinkResolver(HttpClient httpClient) : IGoogleMapsLinkResolver
{
    private readonly HttpClient _httpClient = httpClient;

    public async Task<string?> ResolveAsync(string link, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(link) || !Uri.TryCreate(link, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (!IsShortGoogleMapsHost(uri))
        {
            return link;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        var finalUri = response.RequestMessage?.RequestUri;

        // Only hand back a URL that ended up on Google Maps; anything else is not a route link.
        return finalUri is not null && IsGoogleHost(finalUri) ? finalUri.ToString() : null;
    }

    private static bool IsGoogleHost(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps
        && (uri.Host.StartsWith("google.", StringComparison.OrdinalIgnoreCase)
            || uri.Host.StartsWith("www.google.", StringComparison.OrdinalIgnoreCase)
            || uri.Host.StartsWith("maps.google.", StringComparison.OrdinalIgnoreCase));

    private static bool IsShortGoogleMapsHost(Uri uri) =>
        uri.Host.Equals("maps.app.goo.gl", StringComparison.OrdinalIgnoreCase)
        || uri.Host.Equals("goo.gl", StringComparison.OrdinalIgnoreCase);
}

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

        return response.RequestMessage?.RequestUri?.ToString();
    }

    private static bool IsShortGoogleMapsHost(Uri uri) =>
        uri.Host.Equals("maps.app.goo.gl", StringComparison.OrdinalIgnoreCase)
        || uri.Host.Equals("goo.gl", StringComparison.OrdinalIgnoreCase);
}

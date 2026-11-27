using System.IO.Compression;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FuelCalculator.Core.Domain;

namespace FuelCalculator.Core.Fuel;

/// <summary>
/// Live pump prices for the 27 EU countries from the European Commission's Weekly Oil Bulletin
/// (prices with taxes; Euro-super 95 and road diesel, published every week). It serves the last
/// bulletin it read, from memory or its cache file, and never waits on the network:
/// <see cref="RefreshAsync"/> is called in the background.
/// </summary>
public sealed partial class EuOilBulletinPriceProvider : IFuelPriceProvider
{
    public static readonly Uri DefaultPage = new("https://energy.ec.europa.eu/data-and-analysis/weekly-oil-bulletin_en");

    private static readonly Dictionary<string, string> CountryCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Austria"] = "AT", ["Belgium"] = "BE", ["Bulgaria"] = "BG", ["Croatia"] = "HR", ["Cyprus"] = "CY",
        ["Czechia"] = "CZ", ["Czech Republic"] = "CZ", ["Denmark"] = "DK", ["Estonia"] = "EE", ["Finland"] = "FI",
        ["France"] = "FR", ["Germany"] = "DE", ["Greece"] = "GR", ["Hungary"] = "HU", ["Ireland"] = "IE",
        ["Italy"] = "IT", ["Latvia"] = "LV", ["Lithuania"] = "LT", ["Luxembourg"] = "LU", ["Malta"] = "MT",
        ["Netherlands"] = "NL", ["Poland"] = "PL", ["Portugal"] = "PT", ["Romania"] = "RO", ["Slovakia"] = "SK",
        ["Slovenia"] = "SI", ["Spain"] = "ES", ["Sweden"] = "SE"
    };

    private readonly HttpClient _httpClient;
    private readonly string? _cacheFile;
    private readonly Uri _page;
    private Bulletin? _current;

    public EuOilBulletinPriceProvider(HttpClient httpClient, string? cacheFile, Uri? page = null)
    {
        _httpClient = httpClient;
        _cacheFile = cacheFile;
        _page = page ?? DefaultPage;
        _current = LoadCache();
    }

    public sealed record Bulletin(DateOnly DataDate, DateTimeOffset FetchedAt, IReadOnlyDictionary<(string Country, FuelType Fuel), decimal> Prices);

    /// <summary>When the last bulletin was read; null if none has been.</summary>
    public DateTimeOffset? FetchedAt => _current?.FetchedAt;

    public bool NeedsRefresh(TimeSpan maxAge) => _current is null || DateTimeOffset.UtcNow - _current.FetchedAt > maxAge;

    public Task<FuelPriceQuote?> GetPriceAsync(string countryCode, FuelType fuelType, CancellationToken cancellationToken = default)
    {
        var bulletin = _current;

        if (bulletin is null || !bulletin.Prices.TryGetValue((countryCode.Trim().ToUpperInvariant(), fuelType), out var price))
        {
            return Task.FromResult<FuelPriceQuote?>(null);
        }

        return Task.FromResult<FuelPriceQuote?>(new FuelPriceQuote(
            countryCode.Trim().ToUpperInvariant(),
            fuelType,
            price,
            "EUR",
            "EU Weekly Oil Bulletin (live)",
            bulletin.DataDate,
            bulletin.FetchedAt,
            ConfidenceLevel.HIGH,
            false));
    }

    /// <summary>Reads the bulletin page, downloads the latest prices-with-taxes workbook and swaps it in.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var html = await _httpClient.GetStringAsync(_page, cancellationToken);
        var link = FindLatestWithTaxesLink(html, _page)
            ?? throw new InvalidOperationException("The bulletin page has no 'prices with taxes' workbook link.");
        var workbook = await _httpClient.GetByteArrayAsync(link, cancellationToken);
        var parsed = ParseWorkbook(workbook);

        if (parsed.Prices.Count == 0)
        {
            throw new InvalidOperationException("The bulletin workbook had no prices.");
        }

        _current = parsed with { FetchedAt = DateTimeOffset.UtcNow };
        SaveCache(_current);
    }

    /// <summary>The absolute URL of the "prices with taxes" workbook on the bulletin page, escapes kept.</summary>
    public static string? FindLatestWithTaxesLink(string html, Uri page)
    {
        foreach (Match match in HrefPattern().Matches(html))
        {
            var href = WebUtility.HtmlDecode(match.Groups[1].Value);
            var readable = WebUtility.UrlDecode(href);

            if (readable.Contains("with Taxes", StringComparison.OrdinalIgnoreCase)
                && !readable.Contains("without", StringComparison.OrdinalIgnoreCase)
                && readable.Contains(".xlsx", StringComparison.OrdinalIgnoreCase)
                && !readable.Contains("History", StringComparison.OrdinalIgnoreCase))
            {
                return new Uri(page, href).AbsoluteUri;
            }
        }

        return null;
    }

    /// <summary>
    /// Reads the workbook: A2 is the data date (an Excel serial), then one row per country with Euro-super 95
    /// in column B and road diesel in column C, in EUR per 1,000 litres.
    /// </summary>
    public static Bulletin ParseWorkbook(byte[] workbook)
    {
        var cells = XlsxCells(workbook);
        var dataDate = cells.TryGetValue("A2", out var serial) && double.TryParse(serial, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var days)
            ? DateOnly.FromDateTime(new DateTime(1899, 12, 30).AddDays(Math.Floor(days)))
            : DateOnly.FromDateTime(DateTime.UtcNow);
        var prices = new Dictionary<(string, FuelType), decimal>();

        foreach (var (reference, value) in cells)
        {
            if (!reference.StartsWith('A') || !CountryCodes.TryGetValue(value.Trim(), out var code))
            {
                continue;
            }

            var row = reference[1..];
            Add(code, FuelType.GASOLINE_95, $"B{row}");
            Add(code, FuelType.DIESEL, $"C{row}");
        }

        return new Bulletin(dataDate, DateTimeOffset.MinValue, prices);

        void Add(string code, FuelType fuel, string cell)
        {
            if (cells.TryGetValue(cell, out var text)
                && decimal.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var per1000)
                && per1000 > 0)
            {
                prices[(code, fuel)] = Math.Round(per1000 / 1000m, 4);
            }
        }
    }

    /// <summary>The first worksheet's cells as reference → text, resolving shared strings.</summary>
    private static Dictionary<string, string> XlsxCells(byte[] workbook)
    {
        XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace pkg = "http://schemas.openxmlformats.org/package/2006/relationships";

        using var zip = new ZipArchive(new MemoryStream(workbook), ZipArchiveMode.Read);
        XDocument Read(string name) => XDocument.Load(zip.GetEntry(name)?.Open() ?? throw new InvalidDataException($"Workbook part {name} is missing."));

        var firstSheet = Read("xl/workbook.xml").Descendants(main + "sheet").First();
        var relId = firstSheet.Attribute(rel + "id")!.Value;
        var target = Read("xl/_rels/workbook.xml.rels").Descendants(pkg + "Relationship").First(r => r.Attribute("Id")!.Value == relId).Attribute("Target")!.Value.TrimStart('/');
        var sheetPath = target.StartsWith("xl/", StringComparison.Ordinal) ? target : "xl/" + target;

        var shared = zip.GetEntry("xl/sharedStrings.xml") is null
            ? []
            : Read("xl/sharedStrings.xml").Descendants(main + "si").Select(si => string.Concat(si.Descendants(main + "t").Select(t => t.Value))).ToArray();

        var cells = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var cell in Read(sheetPath).Descendants(main + "c"))
        {
            var reference = cell.Attribute("r")?.Value;
            var value = cell.Element(main + "v")?.Value;

            if (reference is null)
            {
                continue;
            }

            if (cell.Attribute("t")?.Value == "s" && int.TryParse(value, out var index) && index < shared.Length)
            {
                value = shared[index];
            }
            else if (cell.Attribute("t")?.Value == "inlineStr")
            {
                value = string.Concat(cell.Descendants(main + "t").Select(t => t.Value));
            }

            if (value is not null)
            {
                cells[reference] = value;
            }
        }

        return cells;
    }

    private Bulletin? LoadCache()
    {
        if (_cacheFile is null || !File.Exists(_cacheFile))
        {
            return null;
        }

        try
        {
            var saved = JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(_cacheFile));
            return saved is null
                ? null
                : new Bulletin(saved.DataDate, saved.FetchedAt, saved.Prices.ToDictionary(p => (p.Country, p.Fuel), p => p.Price));
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null;
        }
    }

    private void SaveCache(Bulletin bulletin)
    {
        if (_cacheFile is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_cacheFile)!);
            var file = new CacheFile(bulletin.DataDate, bulletin.FetchedAt, bulletin.Prices.Select(p => new CachePrice(p.Key.Country, p.Key.Fuel, p.Value)).ToArray());
            File.WriteAllText(_cacheFile, JsonSerializer.Serialize(file));
        }
        catch (IOException)
        {
            // Cache only; the next refresh tries again.
        }
    }

    private sealed record CacheFile(DateOnly DataDate, DateTimeOffset FetchedAt, CachePrice[] Prices);

    private sealed record CachePrice(string Country, FuelType Fuel, decimal Price);

    [GeneratedRegex("href=\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex HrefPattern();
}

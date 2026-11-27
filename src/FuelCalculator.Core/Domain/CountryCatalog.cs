namespace FuelCalculator.Core.Domain;

/// <summary>
/// Per-country facts the calculator needs: local currency (every ISO country, from Domain/Data/countries.json)
/// and, where known, the fuel grades sold at the pump. A country with no grade list is assumed to sell all
/// grades, so only the nearest-priced-grade fallback applies to it.
/// </summary>
public static class CountryCatalog
{
    private static readonly FuelType[] AllGrades = [FuelType.GASOLINE_91, FuelType.GASOLINE_95, FuelType.GASOLINE_98, FuelType.DIESEL];
    private static readonly FuelType[] NoPremium = [FuelType.GASOLINE_91, FuelType.GASOLINE_95, FuelType.DIESEL];
    private static readonly FuelType[] NoRegular = [FuelType.GASOLINE_95, FuelType.GASOLINE_98, FuelType.DIESEL];

    // Grades as sold at the pump in 2026, where checked. Octane 90 (JO, SY) and 92 (EG) count as 91.
    private static readonly Dictionary<string, FuelType[]> Grades = new(StringComparer.OrdinalIgnoreCase)
    {
        ["KW"] = AllGrades,
        ["AE"] = AllGrades,
        ["SA"] = AllGrades,
        ["QA"] = NoPremium,
        ["BH"] = AllGrades,
        ["OM"] = AllGrades,
        ["JO"] = NoPremium,
        ["SY"] = NoPremium,
        ["IQ"] = NoPremium,
        ["LB"] = NoRegular,
        ["EG"] = NoPremium
    };

    private static readonly Lazy<IReadOnlyDictionary<string, CountryInfo>> Countries = new(Load);

    // How often governments reset regulated pump prices (checked 2026-11-27).
    private static readonly Dictionary<string, PriceCycle> PriceCycles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AE"] = PriceCycle.Monthly, ["QA"] = PriceCycle.Monthly, ["OM"] = PriceCycle.Monthly, ["BH"] = PriceCycle.Monthly,
        ["SA"] = PriceCycle.Monthly, ["JO"] = PriceCycle.Monthly, ["KW"] = PriceCycle.Quarterly, ["EG"] = PriceCycle.Quarterly
    };

    public static PriceCycle? GetPriceCycle(string countryCode) =>
        PriceCycles.TryGetValue(countryCode.Trim(), out var cycle) ? cycle : null;

    public static string? GetLocalCurrency(string? countryCode) =>
        countryCode is not null && Countries.Value.TryGetValue(countryCode.Trim(), out var info) ? info.Currency : null;

    public static string? GetName(string? countryCode) =>
        countryCode is not null && Countries.Value.TryGetValue(countryCode.Trim(), out var info) ? info.Name : null;

    /// <summary>
    /// The grade to price instead when <paramref name="requested"/> is not sold in the country,
    /// or null when it is sold or the country's grades are not known. Diesel has no substitute.
    /// </summary>
    public static FuelType? GetSubstituteGrade(string countryCode, FuelType requested)
    {
        if (requested == FuelType.DIESEL
            || !Grades.TryGetValue(countryCode.Trim(), out var sold)
            || sold.Contains(requested))
        {
            return null;
        }

        foreach (var grade in NearestPetrolGrades(requested))
        {
            if (sold.Contains(grade))
            {
                return grade;
            }
        }

        return null;
    }

    /// <summary>
    /// Other petrol grades in the order to try them when <paramref name="requested"/> can't be priced:
    /// the nearest octane first, higher before lower. Diesel has none.
    /// </summary>
    public static IReadOnlyList<FuelType> NearestPetrolGrades(FuelType requested) => requested switch
    {
        FuelType.GASOLINE_98 => [FuelType.GASOLINE_95, FuelType.GASOLINE_91],
        FuelType.GASOLINE_95 => [FuelType.GASOLINE_98, FuelType.GASOLINE_91],
        FuelType.GASOLINE_91 => [FuelType.GASOLINE_95, FuelType.GASOLINE_98],
        _ => []
    };

    private static IReadOnlyDictionary<string, CountryInfo> Load()
    {
        using var document = EmbeddedData.Load("countries.json");
        var countries = new Dictionary<string, CountryInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var country in document.RootElement.GetProperty("countries").EnumerateObject())
        {
            var currency = country.Value.TryGetProperty("currency", out var value) ? value.GetString() : null;
            var name = country.Value.TryGetProperty("name", out var nameValue) ? nameValue.GetString() : null;
            countries[country.Name] = new CountryInfo(name ?? country.Name, string.IsNullOrWhiteSpace(currency) ? null : currency);
        }

        return countries;
    }

    private sealed record CountryInfo(string Name, string? Currency);
}

public enum PriceCycle
{
    Monthly,
    Quarterly
}

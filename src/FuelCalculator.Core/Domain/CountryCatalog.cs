namespace FuelCalculator.Core.Domain;

/// <summary>
/// Per-country facts the calculator needs: local currency and the fuel grades sold at the pump.
/// Countries not listed are treated as unknown: no currency default and no grade substitution.
/// </summary>
public static class CountryCatalog
{
    private static readonly FuelType[] AllGrades = [FuelType.GASOLINE_91, FuelType.GASOLINE_95, FuelType.GASOLINE_98, FuelType.DIESEL];
    private static readonly FuelType[] NoPremium = [FuelType.GASOLINE_91, FuelType.GASOLINE_95, FuelType.DIESEL];
    private static readonly FuelType[] NoRegular = [FuelType.GASOLINE_95, FuelType.GASOLINE_98, FuelType.DIESEL];

    private static readonly Dictionary<string, CountryInfo> Countries = new(StringComparer.OrdinalIgnoreCase)
    {
        // Grades as sold at the pump in 2026. Octane 90 (JO, SY) and 92 (EG) count as 91.
        ["KW"] = new("KWD", AllGrades),
        ["AE"] = new("AED", AllGrades),
        ["SA"] = new("SAR", AllGrades),
        ["QA"] = new("QAR", NoPremium),
        ["BH"] = new("BHD", AllGrades),
        ["OM"] = new("OMR", AllGrades),
        ["JO"] = new("JOD", NoPremium),
        ["SY"] = new("SYP", NoPremium),
        ["IQ"] = new("IQD", NoPremium),
        ["LB"] = new("LBP", NoRegular),
        ["EG"] = new("EGP", NoPremium)
    };

    public static string? GetLocalCurrency(string? countryCode) =>
        countryCode is not null && Countries.TryGetValue(countryCode.Trim(), out var info) ? info.Currency : null;

    /// <summary>
    /// The grade to price instead when <paramref name="requested"/> is not sold in the country,
    /// or null when it is sold or the country is unknown. Petrol falls back to the nearest grade
    /// sold, preferring the higher octane; diesel has no substitute.
    /// </summary>
    public static FuelType? GetSubstituteGrade(string countryCode, FuelType requested)
    {
        if (requested == FuelType.DIESEL
            || !Countries.TryGetValue(countryCode.Trim(), out var info)
            || info.Grades.Contains(requested))
        {
            return null;
        }

        foreach (var grade in NearestPetrolGrades(requested))
        {
            if (info.Grades.Contains(grade))
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

    private sealed record CountryInfo(string Currency, FuelType[] Grades);
}

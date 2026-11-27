namespace FuelCalculator.Core.Domain;

/// <summary>
/// Per-country facts the calculator needs: local currency and the fuel grades sold at the pump.
/// Countries not listed are treated as unknown: no currency default and no grade substitution.
/// </summary>
public static class CountryCatalog
{
    private static readonly FuelType[] AllGrades = [FuelType.GASOLINE_91, FuelType.GASOLINE_95, FuelType.GASOLINE_98, FuelType.DIESEL];
    private static readonly FuelType[] NoPremium = [FuelType.GASOLINE_91, FuelType.GASOLINE_95, FuelType.DIESEL];

    private static readonly Dictionary<string, CountryInfo> Countries = new(StringComparer.OrdinalIgnoreCase)
    {
        ["KW"] = new("KWD", AllGrades),
        ["AE"] = new("AED", AllGrades),
        ["SA"] = new("SAR", NoPremium),
        ["QA"] = new("QAR", NoPremium),
        ["BH"] = new("BHD", NoPremium),
        ["OM"] = new("OMR", NoPremium),
        ["JO"] = new("JOD", AllGrades),
        ["SY"] = new("SYP", AllGrades)
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

        FuelType[] preference = requested switch
        {
            FuelType.GASOLINE_98 => [FuelType.GASOLINE_95, FuelType.GASOLINE_91],
            FuelType.GASOLINE_95 => [FuelType.GASOLINE_98, FuelType.GASOLINE_91],
            _ => [FuelType.GASOLINE_95, FuelType.GASOLINE_98]
        };

        foreach (var grade in preference)
        {
            if (info.Grades.Contains(grade))
            {
                return grade;
            }
        }

        return null;
    }

    private sealed record CountryInfo(string Currency, FuelType[] Grades);
}

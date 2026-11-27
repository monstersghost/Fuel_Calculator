using System.Text.Json;

namespace FuelCalculator.Core;

/// <summary>
/// Reads the JSON data files built by tools/data/build_world_data.py and embedded in this assembly.
/// </summary>
internal static class EmbeddedData
{
    public static JsonDocument Load(string fileName)
    {
        var resource = $"FuelCalculator.Core.{fileName}";
        using var stream = typeof(EmbeddedData).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Embedded data '{resource}' is missing.");
        return JsonDocument.Parse(stream);
    }
}

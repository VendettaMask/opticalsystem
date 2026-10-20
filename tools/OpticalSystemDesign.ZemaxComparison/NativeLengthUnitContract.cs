using System.Text.Json;

namespace OpticalSystemDesign.ZemaxComparison;

/// <summary>Use OpticStudio's physical lens scaling tool before focal analysis, preserving source bytes.</summary>
public static class NativeLengthUnitContract
{
    public static bool IsSupported(string unit) => unit is "MM" or "CM" or "IN" or "M";

    public static CanonicalAnalysisRequest Configure(CanonicalAnalysisRequest request, string unit)
    {
        if (!IsSupported(unit)) throw new InvalidDataException("Unverified native lens unit: " + unit);
        return unit == "MM" ? request : request with { NativeLensUnit = unit };
    }

    public static void Validate(string dataPath, CanonicalAnalysisRequest request)
    {
        if (request.NativeLensUnit is null) return;
        var expected = request.NativeLensUnit switch
        {
            "CM" => "Centimeters",
            "IN" => "Inches",
            "M" => "Meters",
            _ => throw new InvalidDataException("Invalid native length conversion request.")
        };
        var directory = Path.GetDirectoryName(dataPath)!;
        using var record = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "unit-normalization.json")));
        using var model = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "model.json")));
        if (record.RootElement.GetProperty("originalUnit").GetString() != expected
            || record.RootElement.GetProperty("convertedUnit").GetString() != "Millimeters"
            || record.RootElement.GetProperty("method").GetString() != "OpticStudio.Tools.OpenScale.ScaleByUnits"
            || model.RootElement.GetProperty("units").GetProperty("LensUnits").GetString() != "Millimeters")
            throw new InvalidDataException("Native physical millimeter conversion readback differs from the request.");
    }
}

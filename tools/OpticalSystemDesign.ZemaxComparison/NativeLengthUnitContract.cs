using System.Text.Json;
using OpticalSystemDesign.Validation;

namespace OpticalSystemDesign.ZemaxComparison;

/// <summary>Use OpticStudio's physical lens scaling tool before focal analysis, preserving source bytes.</summary>
public static class NativeLengthUnitContract
{
    public static bool IsSupported(string unit) => unit is "MM" or "CM" or "IN" or "M" or "METER";

    public static CanonicalAnalysisRequest Configure(CanonicalAnalysisRequest request, string unit)
    {
        if (!IsSupported(unit)) throw new InvalidDataException("Unverified native lens unit: " + unit);
        return unit == "MM" ? request : request with { NativeLensUnit = unit == "METER" ? "M" : unit };
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
        var receiptPath = Path.Combine(directory, "unit-normalization.json");
        var modelPath = Path.Combine(directory, "model.json");
        if (!EvidenceFiles.Exists(receiptPath) || !EvidenceFiles.Exists(modelPath))
            throw new InvalidDataException("Native physical millimeter conversion requires the conversion receipt and model readback.");
        using var record = JsonDocument.Parse(EvidenceFiles.ReadAllText(receiptPath));
        using var model = JsonDocument.Parse(EvidenceFiles.ReadAllText(modelPath));
        var receipt = record.RootElement;
        if (String(receipt, "originalUnit") != expected
            || String(receipt, "convertedUnit") != "Millimeters"
            || String(receipt, "method") != "OpticStudio.Tools.OpenScale.ScaleByUnits"
            || !receipt.TryGetProperty("savedInput", out var saved) || saved.ValueKind != JsonValueKind.False
            || model.RootElement.ValueKind != JsonValueKind.Object
            || !model.RootElement.TryGetProperty("units", out var units) || String(units, "LensUnits") != "Millimeters")
            throw new InvalidDataException("Native physical millimeter conversion readback differs from the request.");

        static string? String(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object
            && value.TryGetProperty(key, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;
    }
}

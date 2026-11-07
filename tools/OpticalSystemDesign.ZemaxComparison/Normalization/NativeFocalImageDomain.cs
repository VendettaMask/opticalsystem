using System.Text.Json;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Validation;

namespace OpticalSystemDesign.ZemaxComparison.Normalization;

internal static class NativeFocalImageDomain
{
    internal static void Validate(string path, CanonicalAnalysisRequest request, string? workbenchPath)
    {
        if (workbenchPath is null || request.ImageSpaceAfocal || request.Surface != -1
            || request.CanonicalAnalysisKey is not ("PSF" or "MTF" or "Field Curvature and Distortion" or "Diffraction Encircled Energy"))
            return;
        if (request.CanonicalAnalysisKey == "Diffraction Encircled Energy"
            && (!bool.TryParse(request.ZemaxSettings.GetValueOrDefault("UseHuygensPSF")?.ToString(), out var huygens) || huygens))
            return;

        var actual = JsonFiles.Read<AnalysisData>(workbenchPath);
        if (actual.Outcome != AnalysisOutcome.NotApplicable
            || actual.Values.GetValueOrDefault(FocalImageDomain.ResultKey) is not JsonElement { ValueKind: JsonValueKind.String } domain)
            return;

        // Confirm the captured prescription independently. A Workbench
        // outcome by itself is insufficient to invalidate numerical data.
        var modelPath = Path.Combine(Path.GetDirectoryName(path)!, "model.json");
        if (!EvidenceFiles.Exists(modelPath)) return;
        using var model = JsonDocument.Parse(EvidenceFiles.ReadAllText(modelPath));
        var root = model.RootElement;
        var verified = domain.GetString() switch
        {
            FocalImageDomain.CollimatedContactPupil => request.MaximumFieldRadius == 0 && IsFocalContactPrescription(root, axialOnly: true),
            FocalImageDomain.CollimatedPlaneTrain => IsFocalContactPrescription(root, axialOnly: false),
            _ => false
        };
        if (!verified) return;

        throw new InvalidDataException(request.CanonicalAnalysisKey switch
        {
            "PSF" => "Native focal FFT PSF has no finite diffraction image in the verified collimated contact-pupil prescription.",
            "MTF" => "Native focal FFT MTF has no image-plane frequency scale in the verified collimated contact-pupil prescription.",
            "Diffraction Encircled Energy" => "Native focal FFT energy has no finite image diffraction scale in the verified collimated contact-pupil prescription.",
            _ => "Native field curvature and distortion have no finite focus or valid image-height reference in the verified collimated contact-pupil prescription."
        });
    }

    private static bool IsFocalContactPrescription(JsonElement root, bool axialOnly)
    {
        if (!root.TryGetProperty("mode", out var mode) || mode.ValueKind != JsonValueKind.String || mode.GetString() != "Sequential"
            || !root.TryGetProperty("aperture", out var aperture) || aperture.ValueKind != JsonValueKind.Object
            || !aperture.TryGetProperty("AFocalImageSpace", out var afocal) || afocal.ValueKind != JsonValueKind.False
            || !root.TryGetProperty("surfaces", out var surfaces) || surfaces.ValueKind != JsonValueKind.Array || surfaces.GetArrayLength() < 3
            || !root.TryGetProperty("fields", out var fields) || fields.ValueKind != JsonValueKind.Array || fields.GetArrayLength() == 0
            || axialOnly && fields.EnumerateArray().Any(field => !field.TryGetProperty("data", out var data) || !Zero(data, "X") || !Zero(data, "Y")))
            return false;

        var rows = surfaces.EnumerateArray().ToArray();
        return Flag(rows[0], "IsObject") && Flag(rows[^1], "IsImage")
            && rows[0].TryGetProperty("Thickness", out var objectDistance) && objectDistance.ValueKind == JsonValueKind.Null
            && rows.Skip(1).SkipLast(1).All(row => Zero(row, "Thickness"))
            && rows.All(row => row.TryGetProperty("Type", out var type) && type.ValueKind == JsonValueKind.String
                && Blank(row, "Material")
                && (axialOnly ? type.GetString() is ("Standard" or "GridSag") && Blank(row, "Coating")
                    : type.GetString() == "Standard" && row.TryGetProperty("Radius", out var radius)
                        && (radius.ValueKind == JsonValueKind.Null || Zero(row, "Radius"))));
    }

    private static bool Zero(JsonElement row, string key) =>
        row.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number && value.GetDouble() == 0;

    private static bool Blank(JsonElement row, string key) =>
        row.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString());

    private static bool Flag(JsonElement row, string key) =>
        row.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.True;
}

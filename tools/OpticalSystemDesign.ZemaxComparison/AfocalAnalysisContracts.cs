using System.Text.Json;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Normalization;

namespace OpticalSystemDesign.ZemaxComparison;

public static class AfocalAnalysisContracts
{
    public static string? BlockReason(string lensUnit, bool afocal, string key)
    {
        if (lensUnit != "MM") return "Live adapter requires MM lens units; no length/angle substitution.";
        return afocal && key is not ("Ray Fan" or "MTF" or "Field Curvature" or "Field Curvature and Distortion")
            ? $"Afocal {key}: settings/result contract has not been verified; no focal interpretation."
            : null;
    }

    public static CanonicalAnalysisRequest Configure(CanonicalAnalysisRequest request, bool afocal, string source)
    {
        if (!afocal) return request;
        var unit = source.Split('\n').Select(line => line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Single(tokens => tokens.Length > 0 && tokens[0] == "UNIT");
        var angle = unit.Length > 6 ? unit[6] switch
        {
            "AM" => "ArcMinute",
            "MR" => "Milliradian",
            "DG" => "Degree",
            "RA" => "Radian",
            _ => throw new InvalidDataException("Unverified source afocal angular unit: " + unit[6])
        } : throw new InvalidDataException("Source afocal angular unit is missing.");
        return request with { ImageSpaceAfocal = true, NativeAfocalAngleUnit = angle };
    }

    public static string FrequencyUnit(CanonicalAnalysisRequest request) => request.NativeAfocalAngleUnit switch
    {
        "ArcMinute" => "CyclesPerArcMinute",
        "Milliradian" => "CyclesPerMilliradian",
        "Degree" => "CyclesPerDegree",
        "Radian" => "CyclesPerRadian",
        _ => throw new InvalidDataException("Missing verified afocal angle unit.")
    };

    public static void MapNativePayload(Dictionary<string, object?> payload, CanonicalAnalysisRequest request)
    {
        if (request.ImageSpaceAfocal && request.CanonicalAnalysisKey == "MTF")
            payload["maximumFrequency"] = request.MaximumFrequency
                * PhysicalNormalization.UnitScale("CyclesPerMilliradian", FrequencyUnit(request));
    }

    public static AnalysisConfiguration ComparisonSettings(AnalysisConfiguration config, CanonicalAnalysisRequest? request)
    {
        if (request?.ImageSpaceAfocal != true) return config;
        if (request.CanonicalAnalysisKey == "Ray Fan")
            return config with
            {
                Quantities = config.AfocalQuantities
                ?? throw new InvalidDataException("Afocal Ray Fan requires explicit AfocalQuantities/IncidentAngle tolerances in degrees; focal length tolerances are not angular tolerances.")
            };
        if (request.CanonicalAnalysisKey is "Field Curvature" or "Field Curvature and Distortion")
            return config with { Quantities = config.Quantities.Where(pair => pair.Key == "Distortion").ToDictionary() };
        return config;
    }

    public static void ValidateNativeModel(string dataPath, CanonicalAnalysisRequest request)
    {
        if (!request.ImageSpaceAfocal) return;
        var path = Path.Combine(Path.GetDirectoryName(dataPath)!, "model.json");
        if (!File.Exists(path)) throw new InvalidDataException("Afocal native unit/model readback is missing.");
        using var model = JsonDocument.Parse(File.ReadAllText(path));
        var root = model.RootElement;
        var expected = request.NativeAfocalAngleUnit switch
        {
            "ArcMinute" => "ArcMinutes",
            "Milliradian" => "Milliradians",
            "Degree" => "Degrees",
            "Radian" => "Radians",
            _ => "Unverified"
        };
        if (!root.GetProperty("aperture").GetProperty("AFocalImageSpace").GetBoolean()
            || root.GetProperty("units").GetProperty("LensUnits").GetString() != "Millimeters"
            || root.GetProperty("units").GetProperty("AfocalModeUnits").GetString() != expected)
            throw new InvalidDataException("Native afocal image-space/angle-unit readback differs from the canonical request.");
        if (request.CanonicalAnalysisKey is "Ray Fan" or "MTF")
        {
            var settingsPath = Path.Combine(Path.GetDirectoryName(dataPath)!, "captured-settings.json");
            if (!File.Exists(settingsPath)) throw new InvalidDataException("Afocal captured selectors are missing.");
            using var settings = JsonDocument.Parse(File.ReadAllText(settingsPath));
            var selectors = settings.RootElement.GetProperty("selectors");
            if (selectors.GetProperty("Field").GetInt32() != request.Field
                || selectors.GetProperty("Wavelength").GetInt32() != request.Wavelength)
                throw new InvalidDataException("Native field/wavelength selector differs from the afocal request.");
            if (request.CanonicalAnalysisKey == "MTF")
            {
                var expectedFrequency = request.MaximumFrequency
                    * PhysicalNormalization.UnitScale("CyclesPerMilliradian", FrequencyUnit(request));
                var actualFrequency = settings.RootElement.GetProperty("properties").GetProperty("MaximumFrequency").GetDouble();
                if (Math.Abs(actualFrequency - expectedFrequency) > 1e-12 * Math.Max(1, Math.Abs(expectedFrequency)))
                    throw new InvalidDataException("Native angular MTF frequency readback differs from the canonical request.");
            }
        }
    }
}

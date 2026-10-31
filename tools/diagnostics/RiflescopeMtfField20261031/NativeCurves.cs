using System.Text.Json;
using OpticalSystemDesign.ZemaxComparison;

internal static class NativeCurves
{
    internal static Series1DResult[] Read(string root, string method, int n, string sourceSha256, double[] frequencies)
    {
        var id = $"{method}-n{n}";
        var indexPath = Path.Combine(root, "native-reference-index.json");
        var index = File.Exists(indexPath) ? JsonFiles.Read<Dictionary<string, string>>(indexPath) : [];
        var folder = Path.Combine(root, index.GetValueOrDefault(id, "native/" + id));
        var execution = JsonFiles.Read<JsonElement>(Path.Combine(Path.GetDirectoryName(folder)!, "execution.json")).EnumerateArray()
            .SingleOrDefault(v => v.GetProperty("id").GetString() == $"{method}-n{n}");
        if (execution.ValueKind == JsonValueKind.Undefined || execution.GetProperty("result").GetProperty("ExitCode").GetInt32() != 0
            || !execution.GetProperty("sourceUnchanged").GetBoolean()) throw new InvalidDataException("Native capture has no completed successful execution");
        var capture = JsonFiles.Read<JsonElement>(Path.Combine(folder, "capture.json"));
        if (capture.GetProperty("analysisType").GetString() != (method == "fourier" ? "FftMtfvsField" : "HuygensMtfvsField")
            || !capture.GetProperty("textSaved").GetBoolean() || capture.GetProperty("screenshotStatus").GetString() != "NotRequested")
            throw new InvalidDataException("Expected completed direct native field curves");
        var settings = JsonFiles.Read<JsonElement>(Path.Combine(folder, "captured-settings.json"));
        var p = settings.GetProperty("properties"); var request = settings.GetProperty("request");
        if (request.GetProperty("configuration").GetInt32() != 1 || request.GetProperty("inputSha256").GetString() != sourceSha256
            || request.GetProperty("captureScreenshots").GetBoolean() || p.GetProperty("SampleSize").GetString() != $"S_{n}x{n}"
            || p.GetProperty("FieldDensity").GetInt32() != 10 || p.GetProperty("ScanType").GetString() != "Plus_Y"
            || !p.GetProperty("RemoveVignetting").GetBoolean() || p.GetProperty("UsePolarization").GetBoolean()
            || settings.GetProperty("selectors").GetProperty("Wavelength").GetInt32() != 0
            || method == "huygens" && p.GetProperty("ImageDelta").GetDouble() != 0)
            throw new InvalidDataException("Native physical settings changed");
        var unit = Math.PI * 1000 / 10800; // mrad per arcminute; input frequencies are reciprocally scaled.
        for (var f = 0; f < 6; f++) if (Math.Abs(p.GetProperty("Freq_" + (f + 1)).GetDouble() - frequencies[f] * unit) > 1e-14)
            throw new InvalidDataException("Native frequency unit conversion differs");
        var model = JsonFiles.Read<JsonElement>(Path.Combine(folder, "model.json"));
        if (!model.GetProperty("aperture").GetProperty("AFocalImageSpace").GetBoolean()
            || model.GetProperty("units").GetProperty("AfocalModeUnits").GetString() != "ArcMinutes") throw new InvalidDataException("Native afocal unit changed");
        var data = JsonFiles.Read<JsonElement>(Path.Combine(folder, "data.json"));
        if (data.GetProperty("messages").GetArrayLength() != 0) throw new InvalidDataException("Native analysis messages require review");
        var series = data.GetProperty("dataSeries"); if (series.GetArrayLength() != 6) throw new InvalidDataException("Six native frequencies required");
        return series.EnumerateArray().SelectMany(s =>
        {
            var x = s.GetProperty("x").EnumerateArray().Select(v => v.GetDouble()).ToArray();
            if (x.Length != 300 || x[0] != 0 || x[^1] != 1) throw new InvalidDataException("Native full field span differs");
            return Enumerable.Range(0, 2).Select(d => new Series1DResult("curve", "Native", x,
                s.GetProperty("y").EnumerateArray().Select(v => v[d].GetDouble()).ToArray(),
                new("NormalizedField", "Dimensionless"), new("Modulation", "Dimensionless")));
        }).ToArray();
    }
}

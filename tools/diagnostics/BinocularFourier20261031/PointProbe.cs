using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.ZemaxComparison;

internal static class PointProbe
{
    internal static Optic Model(string prior)
    {
        using var cases = JsonDocument.Parse(File.ReadAllText(Path.Combine(prior, "cases.json")));
        var row = cases.RootElement.EnumerateArray().Single(r => r.GetProperty("id").GetString() == "m3-fourier");
        return Optic.FromSnapshot(JsonFiles.Read<OpticSnapshot>(row.GetProperty("snapshot").GetString()!));
    }
    internal static void Prepare(string prior, string root)
    {
        var optic = Model(prior); var wave = optic.Wavelengths[1];
        var shift = ContrastMetrics.PupilSeparation(optic, (0, 1), wave, 3.2);
        var points = new List<object>(); var weights = new List<double>();
        const int n = 32, subdivisions = 8;
        for (var row = 0; row < n; row++) for (var column = 0; column < n; column++)
        {
            var count = 0; double x = 0, y = 0;
            for (var sy = 0; sy < subdivisions; sy++) for (var sx = 0; sx < subdivisions; sx++)
            {
                var px = -1 + 2d * (column + (sx + .5) / subdivisions) / n;
                var py = -1 + 2d * (row + (sy + .5) / subdivisions) / n;
                if (!ContrastMetrics.TryPair(px, py, shift, true, true, out _)) continue;
                x += px; y += py; count++;
            }
            if (count == 0) continue;
            points.Add(new { hx = 0d, hy = 1d, px = x / count - shift / 2, py = y / count });
            points.Add(new { hx = 0d, hy = 1d, px = x / count + shift / 2, py = y / count });
            weights.Add(count / (double)(subdivisions * subdivisions));
        }
        var native = Path.Combine(root, "point-native"); Directory.CreateDirectory(native);
        var request = JsonFiles.Read<Dictionary<string, JsonElement>>(Path.Combine(prior, "native/m3-fourier.request.json"));
        JsonElement E(object v) => JsonSerializer.SerializeToElement(v, JsonFiles.Options);
        request["adapter"] = E("point-comparison"); request["pointKind"] = E("phase-pairs");
        request["wavelength"] = E(2); request["originalKeys"] = E(new[] { "Fourier MTF vs Field" });
        request["pointInputs"] = E(points); request["diagnosticPupilOperands"] = E(true);
        JsonFiles.Write(Path.Combine(native, "pairs.request.json"), request);
        JsonFiles.Write(Path.Combine(native, "jobs.json"), new[] { new { id = "pairs", request = "pairs.request.json", inputSha256 = request["inputSha256"].GetString() } });
        JsonFiles.Write(Path.Combine(root, "point-weights.json"), weights);
        Console.WriteLine("Prepared " + points.Count + " shifted-pupil rays");
    }
    internal static void Compare(string prior, string root)
    {
        var optic = Model(prior); var wave = optic.Wavelengths[1];
        var request = JsonFiles.Read<JsonElement>(Path.Combine(root, "point-native/pairs.request.json"));
        var coordinates = request.GetProperty("pointInputs").EnumerateArray().Select(p => (p.GetProperty("px").GetDouble(), p.GetProperty("py").GetDouble())).ToArray();
        var wf = DiffractionEngine.GenerateDefocusedWavefront(optic, (0, 1), wave, coordinates, 0, wave, optic.RayAimingEnabled);
        var native = JsonFiles.Read<JsonElement>(Path.Combine(root, "point-native/pairs/point-reference.json"));
        var rays = native.GetProperty("surfaces").EnumerateArray().Last().GetProperty("rays").EnumerateArray().ToArray();
        var rows = wf.Samples.Select((sample, i) => new { index = i, x = coordinates[i].Item1, y = coordinates[i].Item2,
            sample.OpdWaves, nativeOpdWaves = rays[i].GetProperty("opdWaves").GetDouble(),
            nativeError = rays[i].GetProperty("error").GetInt32(), nativeVignette = rays[i].GetProperty("vignette").GetInt32(), sample.Intensity }).ToArray();
        JsonFiles.Write(Path.Combine(root, "paired-opd-comparison.json"), rows);
        Console.WriteLine("OPD maximum error=" + rows.Max(r => Math.Abs(r.OpdWaves - r.nativeOpdWaves)).ToString("R"));
    }
}

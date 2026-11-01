using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Metrics;
using OpticalSystemDesign.ZemaxComparison.Normalization;

internal static class EnergyReferenceControlDiagnostics
{
    internal static int Run(string repository, string output)
    {
        if (Directory.Exists(output)) throw new IOException("Fresh output required");
        Directory.CreateDirectory(output);
        using var zip = ZipFile.OpenRead(Path.Combine(repository, "validation/rechecks/2026-10-27/afocal-fft-energy/evidence-current.zip"));
        var policy = JsonFiles.Read<JsonElement>(Path.Combine(repository, "validation/rechecks/2026-10-29/final-core-170/afocal-comparison.json"))
            .GetProperty("cases").EnumerateArray().First(c => c.GetProperty("id").GetString()!.StartsWith("0013-"))
            .GetProperty("effectiveTolerances").GetProperty("EnergyFraction").Deserialize<Tolerances>(JsonFiles.Options)!;
        var rows = new List<object>();
        foreach (var cohort in Directory.GetDirectories(Path.Combine(repository, "artifacts/validation"), "centroid-energy-20261030-native-*").Order())
        foreach (var folder in Directory.GetDirectories(cohort).Order())
        {
            if (!File.Exists(Path.Combine(folder, "data.json"))) continue;
            var id = Path.GetFileName(folder);
            var r = JsonFiles.Read<JsonElement>(Path.Combine(cohort, id + ".request.json"));
            var afocal = r.GetProperty("imageSpaceAfocal").GetBoolean();
            var config = r.GetProperty("configuration").GetInt32();
            var model = Path.GetFileName(r.GetProperty("input").GetString()!).StartsWith("Binocular", StringComparison.OrdinalIgnoreCase)
                ? "003-binocular_system-c" + config : "001-afocal-riflescope-c1";
            using var stream = zip.GetEntry("artifacts/validation/afocal-recompare-20261026/baseline-v4/models/" + model + ".json")!.Open();
            var optic = Optic.FromSnapshot(JsonSerializer.Deserialize<OpticSnapshot>(stream, JsonFiles.Options)!);
            optic.ImageSpaceAfocal = afocal;
            var settings = r.GetProperty("workbenchSettings").Deserialize<Dictionary<string, string>>(JsonFiles.Options)!;
            var nativeSettings = r.GetProperty("zemaxSettings");
            var reference = nativeSettings.GetProperty("ReferTo").GetString()!;
            settings["Reference"] = reference == "ChiefRay" ? "chief" : reference.ToLowerInvariant();
            var scale = afocal ? PhysicalNormalization.UnitScale(r.GetProperty("nativeAfocalAngleUnit").GetString()!, "Milliradian") : 1;
            settings["MaximumDistanceMicrometers"] = (nativeSettings.GetProperty("RadiusMaximum").GetDouble() * scale).ToString("R", CultureInfo.InvariantCulture);
            var data = new WorkbenchRuntime(optic).BuildAnalysisData("Encircled Energy", settings);
            var native = JsonFiles.Read<JsonElement>(Path.Combine(folder, "data.json")).GetProperty("dataSeries")[0];
            var selectedField = r.GetProperty("field").GetInt32();
            if (selectedField < 1 || data.PlotSeries.Count != optic.Fields.Count) throw new InvalidDataException("Diagnostic requires an explicit field in the original field table");
            var curve = data.PlotSeries[selectedField - 1];
            var actual = new Series1DResult("curve", "Core", curve.Points.Select(p => p.X).ToArray(), curve.Points.Select(p => p.Y).ToArray(), new("Radius", afocal ? "Milliradian" : "Micrometer"), new("EnergyFraction", "Dimensionless"));
            var expected = actual with { DisplayName = "Native", X = native.GetProperty("x").EnumerateArray().Select(p => p.GetDouble() * scale).ToArray(), Y = native.GetProperty("y").EnumerateArray().Select(p => p[0].GetDouble()).ToArray() };
            var matched = ComparisonMetrics.Align(actual, expected, []);
            var violations = matched.Workbench.Zip(matched.Zemax, (a, b) => Math.Abs(a - b) > policy.Absolute + policy.Relative * Math.Abs(b)).Count(v => v);
            rows.Add(new { cohort = Path.GetFileName(cohort), id, afocal, selectedField, reference, wave = r.GetProperty("wavelength").GetInt32(),
                violations, metric = ComparisonMetrics.Calculate("curve", "Dimensionless", matched, policy) });
            JsonFiles.Write(Path.Combine(output, Path.GetFileName(cohort) + "-" + id + ".json"), data);
        }
        JsonFiles.Write(Path.Combine(output, "summary.json"), rows);
        using var core = File.OpenRead(typeof(Optic).Assembly.Location);
        JsonFiles.Write(Path.Combine(output, "identity.json"), new { coreSha256 = Convert.ToHexStringLower(SHA256.HashData(core)),
            role = "Selected native diagnostic fields; explicit angular-unit conversion, no reference or ordinate fitting", formalOpticalPassCount = 0 });
        return 0;
    }
}

using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Workbench;

internal static class EnergyQuadratureStudy
{
    internal static Task Run(string repository, string study)
    {
        var options = JsonFiles.Read<JsonElement>(Path.Combine(study, "quadrature-request.json"));
        var sampling = options.GetProperty("sampling").GetInt32();
        var snapshot = Path.Combine(repository, "artifacts/validation/model-capabilities-stage5-20261101/models/014-c1.optic.json");
        var optic = Optic.FromSnapshot(JsonFiles.Read<OpticSnapshot>(snapshot));
        var rows = JsonFiles.Read<JsonElement[]>(Path.Combine(repository, "artifacts/validation/model-capabilities-stage5-20261101/rows.json"));
        var node = JsonNode.Parse(rows.Single(r => r.GetProperty("index").GetInt32() == 502)
            .GetProperty("run").GetProperty("request").GetRawText())!;
        node["pupilSampling"] = sampling;
        foreach (var key in new[] { "PupilSampling", "Sampling", "NumRays" }) node["workbenchSettings"]![key] = sampling.ToString();
        var request = node.Deserialize<CanonicalAnalysisRequest>(JsonFiles.Options)!;
        var factory = typeof(DiffractionEngine).GetMethod("ComputeBoundedCumulativeEnergyKnots", BindingFlags.NonPublic | BindingFlags.Static)!;
        var build = typeof(DiffractionEncircledEnergyAnalysis).GetMethod("BuildCurve", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var analysis = new DiffractionEncircledEnergyAnalysis(optic, sampling, 64, 401, 2, 1, "encircled", "centroid", 5);
        var core = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(DiffractionEngine).Assembly.Location)));
        foreach (var decimation in options.GetProperty("decimations").EnumerateArray().Select(v => v.GetInt32()))
        {
            var folder = Path.Combine(study, "decimation-" + decimation);
            if (Directory.Exists(folder)) throw new IOException("Fresh quadrature result required");
            Directory.CreateDirectory(folder);
            var series = new List<AnalysisSeries>(); var metadata = new List<object>();
            foreach (var ideal in new[] { true, false })
            {
                var grid = factory.Invoke(null, new object[] { optic, (0d, 0d), optic.Wavelengths[1], sampling,
                    "centroid", "encircled", 5d, ideal, decimation })!;
                var points = (IReadOnlyList<AnalysisPoint>)build.Invoke(analysis, new[] { grid, (object)5d })!;
                series.Add(new("Radius (µm)", "Energy fraction", points, Name: ideal ? "Diffraction limit" : "Field 1",
                    XQuantity: AnalysisAxisQuantity.Radius, XUnit: AnalysisAxisUnit.Micrometer,
                    YQuantity: AnalysisAxisQuantity.EnergyFraction, YUnit: AnalysisAxisUnit.Dimensionless));
                var type = grid.GetType();
                object? Property(string name) => type.GetProperty(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(grid);
                metadata.Add(new { ideal, fourierSampling = Property("FourierSampling"), imagePitch = Property("ImagePitch"), binWidth = Property("BinWidth") });
            }
            JsonFiles.Write(Path.Combine(folder, "data.json"), new AnalysisData("Diffraction Encircled Energy",
                new Dictionary<string, object> { ["DiagnosticOnly"] = true, ["ImageDecimation"] = decimation,
                    ["PupilSampling"] = sampling, ["Metadata"] = metadata }, series[0], series));
            JsonFiles.Write(Path.Combine(folder, "provenance.json"), new { diagnosticOnly = true,
                executorId = "Diagnostic.SharedCore.BoundedImageQuadrature/v1", codeFingerprint = "diagnostic:" + core,
                snapshotSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(snapshot))), sampling, decimation });
            JsonFiles.Write(Path.Combine(study, "decimation-" + decimation + ".request.json"),
                new WorkbenchJob(snapshot, folder, request));
            Console.WriteLine(folder);
        }
        return Task.CompletedTask;
    }
}

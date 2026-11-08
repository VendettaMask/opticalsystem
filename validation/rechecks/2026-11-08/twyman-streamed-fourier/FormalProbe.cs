using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Serialization;

var options = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true, WriteIndented = true,
    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    Converters = { new JsonStringEnumConverter() }
};
var job = JsonDocument.Parse(File.ReadAllText(args[0])).RootElement;
var path = job.GetProperty("snapshotPath").GetString()!;
var snapshotBytes = File.ReadAllBytes(path);
var snapshot = JsonSerializer.Deserialize<OpticSnapshot>(snapshotBytes, options)!;
var parameters = job.GetProperty("request").GetProperty("workbenchSettings")
    .Deserialize<Dictionary<string,string>>(options)!;
parameters["FourierSamplingMode"] = "Adaptive";
parameters["FourierMaximumSampling"] = "8192";
parameters["FourierRelativeAccuracy"] = "0.001";
var application = new WorkbenchRuntime(Optic.FromSnapshot(snapshot));
var timer = Stopwatch.StartNew();
Console.WriteLine($"Starting formal PSF #2157, core {Hash(typeof(Optic))}");
var result = application.BuildAnalysisData("PSF", parameters);
Console.WriteLine($"{result.Outcome} {result.OutcomeReason}, wall {timer.Elapsed.TotalSeconds:F3}s");
Console.WriteLine(JsonSerializer.Serialize(result.Values.GetValueOrDefault("FourierSamplingValidation"), options));
var image = result.PlotSeries.Count == 1
    ? result.PlotSeries[0].Points.Chunk(128).Select(row => row.Select(p => p.Value).ToArray()).ToArray()
    : Array.Empty<double?[]>();
var axes = result.PlotSeries.Count == 1
    ? result.PlotSeries[0].Points.Take(128).Select(p => p.X).ToArray()
    : Array.Empty<double>();
var receipt = new
{
    sourceSnapshotSha256 = Convert.ToHexString(SHA256.HashData(snapshotBytes)).ToLowerInvariant(),
    coreSha256 = Hash(typeof(Optic)), applicationSha256 = Hash(typeof(WorkbenchRuntime)),
    result.Outcome, result.OutcomeReason, result.Values, axes, image,
    wallSeconds = timer.Elapsed.TotalSeconds
};
File.WriteAllText(args[1], JsonSerializer.Serialize(receipt, options));
return result.Outcome == AnalysisOutcome.Success ? 0 : 1;

static string Hash(Type type) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(type.Assembly.Location))).ToLowerInvariant();

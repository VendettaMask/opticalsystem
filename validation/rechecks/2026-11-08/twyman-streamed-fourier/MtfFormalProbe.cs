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
parameters["FourierMaximumSampling"] = "6144";
parameters["FourierRelativeAccuracy"] = "0.001";
var application = new WorkbenchRuntime(Optic.FromSnapshot(snapshot));
var timer = Stopwatch.StartNew();
Console.WriteLine($"Starting formal MTF #2159, core {Hash(typeof(Optic))}");
var result = application.BuildAnalysisData("MTF", parameters);
Console.WriteLine($"{result.Outcome} {result.OutcomeReason}, wall {timer.Elapsed.TotalSeconds:F3}s");
Console.WriteLine(JsonSerializer.Serialize(result.Values.GetValueOrDefault("FourierSamplingValidation"), options));
var series = result.PlotSeries.Select(s => new { s.Name, s.XQuantity, s.XUnit, s.YQuantity, s.YUnit, x = s.Points.Select(p => p.X).ToArray(), y = s.Points.Select(p => p.Y).ToArray() }).ToArray();
var receipt = new
{
    sourceSnapshotSha256 = Convert.ToHexString(SHA256.HashData(snapshotBytes)).ToLowerInvariant(),
    coreSha256 = Hash(typeof(Optic)), applicationSha256 = Hash(typeof(WorkbenchRuntime)),
    result.Outcome, result.OutcomeReason, result.Values, series, parameters,
    wallSeconds = timer.Elapsed.TotalSeconds
};
File.WriteAllText(args[1], JsonSerializer.Serialize(receipt, options));
return result.Outcome == AnalysisOutcome.Success ? 0 : 1;

static string Hash(Type type) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(type.Assembly.Location))).ToLowerInvariant();


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
using var job = JsonDocument.Parse(File.ReadAllText(args[0]));
var root = job.RootElement;
var snapshotBytes = File.ReadAllBytes(root.GetProperty("snapshotPath").GetString()!);
var snapshot = JsonSerializer.Deserialize<OpticSnapshot>(snapshotBytes, options)!;
var parameters = root.GetProperty("request").GetProperty("workbenchSettings")
    .Deserialize<Dictionary<string, string>>(options)!;
parameters["FourierSamplingMode"] = "Adaptive";
parameters["FourierMaximumSampling"] = args[2];
parameters["FourierRelativeAccuracy"] = "0.001";
var application = new WorkbenchRuntime(Optic.FromSnapshot(snapshot));
var timer = Stopwatch.StartNew();
var result = application.BuildAnalysisData("MTF", parameters);
var series = result.PlotSeries.Select(curve => new
{
    curve.Name, curve.XQuantity, curve.XUnit, curve.YQuantity, curve.YUnit,
    x = curve.Points.Select(point => point.X).ToArray(),
    y = curve.Points.Select(point => point.Y).ToArray()
}).ToArray();
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

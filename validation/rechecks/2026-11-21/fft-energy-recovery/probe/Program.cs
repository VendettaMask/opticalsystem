using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core.FileIO;

var root = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
var source = Path.Combine(root, "src/OpticalSystemDesign.App/Assets/SequentialExamples/Objectives/Double Gauss 28 degree field.zmx");
var optic = await new ZemaxZmxImporter().ImportFileAsync(source);
var json = new JsonSerializerOptions { WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
foreach (var entry in args.Skip(2).DefaultIfEmpty("64"))
{
    var runtime = new WorkbenchRuntime(optic.Clone());
    var mode = int.TryParse(entry, out var n) ? "Fixed" : entry;
    if (n == 0) n = 64;
    var settings = runtime.MergeAnalysisSettings("Diffraction Encircled Energy", new Dictionary<string, string>
    {
        ["PupilSampling"] = n.ToString(), ["FourierSamplingMode"] = mode,
        ["FourierMaximumSampling"] = "2048",
        ["MaximumDistanceMicrometers"] = n > 1024 ? "20" : "0"
    });
    using var cancel = new CancellationTokenSource(TimeSpan.FromMinutes(3));
    var clock = Stopwatch.StartNew(); Console.WriteLine($"Starting {entry}");
    var data = runtime.BuildAnalysisData("Diffraction Encircled Energy", settings, cancel.Token);
    File.WriteAllText(Path.Combine(output, entry + ".json"), JsonSerializer.Serialize(new { settings, seconds = clock.Elapsed.TotalSeconds, data }, json));
    Console.WriteLine($"{entry}: {data.Outcome} {data.OutcomeReason}; {data.PlotSeries.Count} curves in {clock.Elapsed.TotalSeconds:F2}s");
}

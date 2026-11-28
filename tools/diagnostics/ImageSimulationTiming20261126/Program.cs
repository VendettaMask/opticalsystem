using System.Diagnostics;
using System.Text.Json;
using System.Reflection;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.Core.Raytrace;

var root = args[0];
var exampleRoot = Path.Combine(root, "src/OpticalSystemDesign.App/Assets/SequentialExamples");
var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(exampleRoot, "workspaces.json")));
var example = catalog.RootElement.EnumerateObject().Single(p => p.Name.Contains("blue notch", StringComparison.OrdinalIgnoreCase));
using var app = WorkbenchApplication.Create();
var timer = Stopwatch.StartNew();
var observations = new ConcurrentDictionary<string,int>();
long nextObservation = 0;
Action observe = () =>
{
    var now = Environment.TickCount64;
    var next = Interlocked.Read(ref nextObservation);
    if (now < next || Interlocked.CompareExchange(ref nextObservation, now + 25, next) != next) return;
    var frames = new StackTrace().GetFrames().Select(f => f.GetMethod()!).Where(m =>
        m.DeclaringType?.Assembly.GetName().Name == "OpticalSystemDesign.Core"
        && m.DeclaringType != typeof(ComputationCancellation)).ToArray();
    var phase = frames.LastOrDefault(m => m.DeclaringType?.Name == "ImageSimulationEngine")?.Name ?? "other";
    var leaf = string.Join("/",frames.Take(4).Select(m => m.DeclaringType!.Name+"."+m.Name));
    observations.AddOrUpdate(phase+": "+leaf,1,(_,n)=>n+1);
};
using var observing = args.Contains("--profile") ? (IDisposable)typeof(ComputationCancellation).GetMethod("ObserveCheckpoints",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[observe])! : null;
await app.Documents.OpenAsync(Path.Combine(exampleRoot, example.Name));
Console.WriteLine($"Opened example: {timer.Elapsed.TotalSeconds:F3}s");
IEnumerable<JsonElement> Windows(JsonElement element)
{
    if (element.TryGetProperty("windows", out var windows)) foreach (var w in windows.EnumerateArray()) yield return w;
    if (element.TryGetProperty("children", out var children)) foreach (var child in children.EnumerateArray()) foreach (var w in Windows(child)) yield return w;
}
foreach (var window in Windows(example.Value.GetProperty("layout")).Where(w => w.GetProperty("kind").GetString() == "Image Simulation"))
{
    var saved = JsonSerializer.Deserialize<Dictionary<string,string>>(window.GetProperty("settings").GetRawText())!;
    saved["SourceFile"] = Path.Combine(exampleRoot,"Images",saved["SourceFile"]);
    if (args.Contains("--full")) saved["PreviewReduction"] = "1";
    var settings = app.Analyses.MergeSettings("Image Simulation", saved);
    Console.WriteLine(JsonSerializer.Serialize(new { phase="begin",settings }));
    using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(40));
    timer.Restart();
    observations.Clear();
    using var rays=SequentialTraceMeasurement.Begin();
    try
    {
        var run = await app.Analyses.RunAsync(new(Guid.NewGuid(),1,"Image Simulation",settings),cancellation.Token);
        var points=run.View.PlotPanes.SelectMany(p=>p.Series).SelectMany(s=>s.Points).Select(p=>new{p.Red,p.Green,p.Blue}).ToArray();
        var record=new { phase="complete",seconds=timer.Elapsed.TotalSeconds,outcome=run.View.Outcome.ToString(),reason=run.View.OutcomeReason,report=run.View.ReportText,
            samples=observations.OrderByDescending(p=>p.Value).Take(16),pixelsSha256=Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(points))),
            settings,rayCount=rays.RayCount,profiled=args.Contains("--profile"),pointCount=points.Length,
            maximumRgb=new[]{points.Max(p=>p.Red),points.Max(p=>p.Green),points.Max(p=>p.Blue)},
            coreSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(ComputationCancellation).Assembly.Location))) };
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(typeof(Program).Assembly.Location)!,saved["DisplayAs"]=="源位图"?"source-timing.json":"simulation-timing.json"),JsonSerializer.Serialize(record,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(JsonSerializer.Serialize(new{record.phase,record.seconds,record.outcome,record.reason,record.samples,record.pixelsSha256}));
    }
    catch (OperationCanceledException) { Console.WriteLine(JsonSerializer.Serialize(new { phase="cancelled",seconds=timer.Elapsed.TotalSeconds })); }
}

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Services;

var root = args[0];
var lens = args.ElementAtOrDefault(1) ?? "cooke";
var sampling = args.ElementAtOrDefault(2) ?? "64";
var profile = args.Contains("--profile");
var gpu = args.Contains("--gpu");
var fixedSampling = args.Contains("--fixed");
var output = args.FirstOrDefault(a => a.StartsWith("--output="))?[9..];
var binary = new { path = typeof(DiffractionEngine).Assembly.Location,
    sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(DiffractionEngine).Assembly.Location))),
    optimized = !typeof(DiffractionEngine).Assembly.GetCustomAttribute<DebuggableAttribute>()!.IsJITOptimizerDisabled,
    processors = Environment.ProcessorCount, framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription };
if (args.Contains("--fft-only"))
{
    var transform = typeof(DiffractionEngine).GetMethod("FourierTransform2D", BindingFlags.Static|BindingFlags.NonPublic)!;
    var stages = new List<object>();
    using var session = new AnalysisGpuSession(gpu);
    foreach (var side in new[] { 128, 512, 1024 })
    {
        var values = new Complex[side, side];
        for (var row = 0; row < side; row++) for (var column = 0; column < side; column++)
            values[row, column] = new Complex(Math.Sin(.1*row+.2*column), Math.Cos(.3*row-.1*column));
        transform.Invoke(null, [values, false]);
        for (var repeat = 0; repeat < 3; repeat++)
        {
            var timer = Stopwatch.StartNew();
            transform.Invoke(null, [values, false]);
            stages.Add(new { side, repeat, seconds = timer.Elapsed.TotalSeconds, gpuUsed = session.UsedDevice });
        }
    }
    var payload = JsonSerializer.Serialize(new { binary, stages }, new JsonSerializerOptions { WriteIndented = true });
    Console.WriteLine(payload);
    if (output is not null) File.WriteAllText(output, payload);
    return;
}
using var app = WorkbenchApplication.Create(lens);
if (lens is not ("cooke" or "tessar"))
{
    var examples = Path.Combine(root, "src/OpticalSystemDesign.App/Assets/SequentialExamples");
    var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(examples, "workspaces.json")));
    var file = catalog.RootElement.EnumerateObject().Single(p => p.Name.Contains(lens, StringComparison.OrdinalIgnoreCase)).Name;
    await app.Documents.OpenAsync(Path.Combine(examples, file));
}
var overrides = new Dictionary<string,string>
{
    ["Sampling"] = sampling,
    ["FourierSamplingMode"] = fixedSampling ? "固定采样" : "按需加密"
};
foreach (var argument in args.Where(a => a.StartsWith("--setting=")))
{
    var pair = argument[10..].Split('=', 2);
    overrides[pair[0]] = pair[1];
}
var settings = app.Analyses.MergeSettings("MTF", overrides);
var observations = new ConcurrentDictionary<string,int>();
long nextObservation = 0;
Action observe = () =>
{
    var now = Environment.TickCount64;
    var next = Interlocked.Read(ref nextObservation);
    if (now < next || Interlocked.CompareExchange(ref nextObservation, now + 10, next) != next) return;
    var frames = new StackTrace().GetFrames().Select(f => f.GetMethod()!).Where(m =>
        m.DeclaringType?.Assembly.GetName().Name == "OpticalSystemDesign.Core"
        && m.DeclaringType != typeof(ComputationCancellation)).ToArray();
    var phase = frames.Any(m => m.DeclaringType?.Name.Contains("FourierResultValidation") == true) ? "refinement" : "requested";
    var leaf = string.Join("/", frames.Take(5).Select(m => m.DeclaringType!.Name + "." + m.Name));
    observations.AddOrUpdate(phase + ": " + leaf, 1, (_, n) => n + 1);
};
using var observing = profile ? (IDisposable)typeof(ComputationCancellation).GetMethod("ObserveCheckpoints", BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null, [observe])! : null;
var rounds = new List<object>();
var roundCount = int.Parse(args.FirstOrDefault(a => a.StartsWith("--rounds="))?[9..] ?? "2");
for (var repeat = 0; repeat < roundCount; repeat++)
{
    observations.Clear();
    using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(35));
    using var rays = SequentialTraceMeasurement.Begin();
    var allocated = GC.GetTotalAllocatedBytes(true);
    var cpu = Process.GetCurrentProcess().TotalProcessorTime;
    var timer = Stopwatch.StartNew();
    Console.WriteLine(JsonSerializer.Serialize(new { phase = "begin", lens, sampling, gpu, fixedSampling, profile, repeat }));
    try
    {
        var run = await app.Analyses.RunAsync(new(Guid.NewGuid(), 1, "MTF", settings) { UseGpuAcceleration = gpu }, cancellation.Token);
        timer.Stop();
        var cpuSeconds = (Process.GetCurrentProcess().TotalProcessorTime-cpu).TotalSeconds;
        var allocatedBytes = GC.GetTotalAllocatedBytes(true)-allocated;
        if (output is not null && args.Contains("--curves"))
            File.WriteAllText(Path.ChangeExtension(output, $"round{repeat}.curves.json"), JsonSerializer.Serialize(new
            { run.View.Outcome, run.View.OutcomeReason, run.View.Series, run.View.PlotOptions }));
        var record = new { binary, lens, sampling, gpu, gpuUsed = run.Provenance.GpuDeviceUsed, fixedSampling, profile, repeat, seconds = timer.Elapsed.TotalSeconds,
            cpuSeconds, allocatedBytes, rayCount = rays.RayCount,
            outcome = run.View.Outcome.ToString(), reason = run.View.OutcomeReason,
            report = run.View.ReportText,
            samples = observations.OrderByDescending(p => p.Value).Take(20).ToArray(), settings };
        rounds.Add(record);
        Console.WriteLine(JsonSerializer.Serialize(record));
    }
    catch (OperationCanceledException)
    {
        var record = new { binary, lens, sampling, gpu, fixedSampling, profile, repeat, seconds = timer.Elapsed.TotalSeconds,
            outcome = "cancelled", rayCount = rays.RayCount, allocatedBytes = GC.GetTotalAllocatedBytes(true)-allocated,
            samples = observations.OrderByDescending(p => p.Value).Take(20).ToArray(), settings };
        rounds.Add(record);
        Console.WriteLine(JsonSerializer.Serialize(record));
        break;
    }
}
if (output is not null) File.WriteAllText(output, JsonSerializer.Serialize(rounds, new JsonSerializerOptions { WriteIndented = true }));

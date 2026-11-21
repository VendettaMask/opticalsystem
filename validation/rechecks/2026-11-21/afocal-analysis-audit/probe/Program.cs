using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.FileIO;

var source = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
var imported = ZemaxZmxReader.ImportConfigurationSet(File.ReadAllText(source), Path.GetDirectoryName(source)).ActiveOptic;
var snapshot = imported.ToSnapshot();
var selected = args.Length > 2 ? args[2].Split('|').ToHashSet(StringComparer.Ordinal) : null;
var overrides = args.Length > 3
    ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(args[3]))! : [];
var records = new List<object>();
var descriptors = WorkbenchAnalysisCatalog.Descriptors.Where(d => selected is null || selected.Contains(d.CanonicalKey)).ToArray();
foreach (var (descriptor, index) in descriptors.Select((d, i) => (d, i)))
{
    var runtime = new WorkbenchRuntime(Optic.FromSnapshot(snapshot));
    var defaults = runtime.GetAnalysisParameters(descriptor.CanonicalKey);
    var settings = defaults.ToDictionary(p => p.Key, p => p.DefaultValue);
    // Explicit diagnostic sampling, not a replacement for UI or precision defaults.
    foreach (var parameter in defaults)
    {
        var value = parameter.Key switch
        {
            "Sampling" or "PupilSampling" or "GridSize" or "GridResolution" => "16",
            "ImageSampling" or "ImageSize" or "ImageWidth" or "ImageHeight" or "ImagePixels" => "16",
            "NumRays" or "NumPoints" or "PlotPointCount" or "SampleCount" => "16",
            "NumRings" or "RayDensity" => "2",
            "FocusDensity" or "FieldDensity" or "WaveDensity" or "FieldSamples" => "3",
            "NumSteps" or "Steps" or "NumberOfSteps" => "3",
            "PsfSize" => "8",
            "PsfGridSize" or "FieldGrid" => "2",
            "Resolution" or "ResolutionX" or "ResolutionY" => "16",
            "FieldNumber" or "WavelengthNumber" => "1",
            "UsePolarization" => "false",
            "FourierSamplingMode" => "Fixed",
            "SaveOutput" => "false",
            _ => null
        };
        if (value is not null) settings[parameter.Key] = value;
    }
    foreach (var (key, value) in overrides) settings[key] = value;
    var elapsed = Stopwatch.StartNew();
    object record;
    try
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var result = runtime.BuildAnalysisData(descriptor.CanonicalKey, settings, cancellation.Token);
        var plotSeries = result.PlotSeries.Concat(result.PlotPanes?.SelectMany(p => p.Series) ?? []);
        record = new
        {
            analysis = descriptor.CanonicalKey, outcome = result.Outcome.ToString(), result.OutcomeReason,
            seconds = elapsed.Elapsed.TotalSeconds, settings,
            parameters = defaults.Select(p => new { p.Key, p.DisplayName, p.DefaultValue }),
            scalars = result.Values.Where(p => p.Value is string or bool or int or long or double or float)
                .ToDictionary(p => p.Key, p => p.Value is double d && !double.IsFinite(d) ? (object)d.ToString() : p.Value),
            series = plotSeries.Select(s => new
            {
                s.Name, s.XAxisLabel, s.YAxisLabel,
                xQuantity = s.XQuantity.ToString(), xUnit = s.XUnit.ToString(),
                yQuantity = s.YQuantity.ToString(), yUnit = s.YUnit.ToString(),
                count = s.Points.Count,
                nonfinite = s.Points.Count(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y)),
                minimumX = FiniteMinimum(s.Points.Select(p => p.X)), maximumX = FiniteMaximum(s.Points.Select(p => p.X)),
                minimumY = FiniteMinimum(s.Points.Select(p => p.Y)), maximumY = FiniteMaximum(s.Points.Select(p => p.Y))
            }).ToArray()
        };
        Console.WriteLine($"{index + 1}/{descriptors.Length}: {descriptor.CanonicalKey}: {result.Outcome} ({elapsed.Elapsed.TotalSeconds:F2}s)");
    }
    catch (Exception error)
    {
        record = new { analysis = descriptor.CanonicalKey, outcome = "Exception", seconds = elapsed.Elapsed.TotalSeconds,
            errorType = error.GetType().Name, reason = error.Message, settings,
            parameters = defaults.Select(p => new { p.Key, p.DisplayName, p.DefaultValue }) };
        Console.WriteLine($"{index + 1}/{descriptors.Length}: {descriptor.CanonicalKey}: {error.GetType().Name}");
    }
    records.Add(record);
    File.WriteAllText(output, JsonSerializer.Serialize(new { source, afocal = imported.ImageSpaceAfocal,
        requested = descriptors.Length, completed = records.Count, results = records }, new JsonSerializerOptions { WriteIndented = true }));
}
static double? FiniteMinimum(IEnumerable<double> values)
{
    var finite = values.Where(double.IsFinite).ToArray(); return finite.Length == 0 ? null : finite.Min();
}
static double? FiniteMaximum(IEnumerable<double> values)
{
    var finite = values.Where(double.IsFinite).ToArray(); return finite.Length == 0 ? null : finite.Max();
}

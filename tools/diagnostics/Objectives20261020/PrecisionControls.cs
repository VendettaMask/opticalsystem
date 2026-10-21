using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using System.Reflection;
using System.Numerics;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Normalization;

internal static class PrecisionControls
{
    public static void ComplexPupil(string output)
    {
        if (Directory.Exists(output)) throw new IOException("Use a fresh output directory.");
        Directory.CreateDirectory(output);
        const string evidence = "artifacts/validation/objectives-residuals-20261021";
        using var jobs = JsonDocument.Parse(File.ReadAllText(Path.Combine(evidence, "native-complex-controls-request.json")));
        using var items = JsonDocument.Parse(File.ReadAllText("artifacts/validation/objectives-20261020/final-v7-intersection-fallback/items.json"));
        var rows = new List<object>();
        foreach (var group in jobs.RootElement.GetProperty("jobs").EnumerateArray().GroupBy(j =>
            (Lens: j.GetProperty("lens").GetString()!, Order: j.GetProperty("pitchDerivation").GetString()!)))
        {
            var item = items.RootElement.EnumerateArray().Single(i => i.GetProperty("lens").GetString() == group.Key.Lens
                && i.GetProperty("run").GetProperty("key").GetString() == "Diffraction Encircled Energy");
            var run = item.GetProperty("run").Deserialize<AnalysisRun>(JsonFiles.Options)!;
            var optic = Optic.FromSnapshot(JsonFiles.Read<OpticSnapshot>(item.GetProperty("snapshot").GetString()!));
            var arrays = new Dictionary<string, double[,]>();
            var pitch = group.First().GetProperty("analysisRequest").GetProperty("imageDeltaMicrometers").GetDouble();
            foreach (var job in group)
            {
                using var raw = JsonDocument.Parse(File.ReadAllText(Path.Combine(evidence, "native-complex-controls-v1", job.GetProperty("id").GetString()!, "data.json")));
                var grid = raw.RootElement.GetProperty("dataGrids")[0];
                var values = new double[64, 64];
                for (var row = 0; row < 64; row++) for (var column = 0; column < 64; column++)
                    values[row, column] = grid.GetProperty("values")[row][column].GetDouble();
                arrays[job.GetProperty("analysisRequest").GetProperty("zemaxSettings").GetProperty("Type").GetString()!] = values;
            }
            var spectrum = new Complex[64, 64];
            var irradianceError = 0d;
            for (var row = 0; row < 64; row++) for (var column = 0; column < 64; column++)
            {
                var complex = new Complex(arrays["Real"][row, column], arrays["Imaginary"][row, column]);
                spectrum[(row + 33) % 64, (column + 33) % 64] = complex;
                irradianceError = Math.Max(irradianceError, Math.Abs(complex.Magnitude * complex.Magnitude - arrays["Linear"][row, column]));
            }
            typeof(DiffractionEngine).GetMethod("FourierTransform2D", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [spectrum, true]);
            var peak = spectrum.Cast<Complex>().Max(c => c.Magnitude);
            var count = spectrum.Cast<Complex>().Count(c => c.Magnitude > peak * 1e-6);
            var analysis = new DiffractionEncircledEnergyAnalysis(optic, 32, 64, 401, run.Request!.Wavelength, 1, maximumDistanceMicrometers: 5);
            var curve = EnergyGridControls.Curve(analysis, new PsfResult(arrays["Linear"], 32, 64, 1, pitch), pitch, nativeShiftedGrid: true);
            using var nativeEnergy = JsonDocument.Parse(File.ReadAllText(Path.Combine(item.GetProperty("path").GetString()!, "native/data.json")));
            var expected = nativeEnergy.RootElement.GetProperty("dataSeries")[1].GetProperty("y").EnumerateArray().Select(y => y[0].GetDouble()).ToArray();
            var max = curve.Zip(expected, (a, b) => Math.Abs(a.Y - b)).Max();
            rows.Add(new
            {
                group.Key.Lens,
                group.Key.Order,
                pitch,
                count,
                irradianceError,
                cumulativeMaximumDifference = max,
                pupilMagnitude = Enumerable.Range(0, 64).Select(r => Enumerable.Range(0, 64).Select(c => spectrum[r, c].Magnitude).ToArray()).ToArray()
            });
            Console.WriteLine($"{group.Key}: pupil={count}, amplitude/intensity max={irradianceError:G5}, cumulative max={max:G5}, pitch={pitch:R}");
        }
        JsonFiles.Write(Path.Combine(output, "summary.json"), rows);
    }
    public static void EnergyConvergence(string output)
    {
        if (Directory.Exists(output)) throw new IOException("Use a fresh control directory.");
        Directory.CreateDirectory(output);
        const string evidence = "artifacts/validation/objectives-residuals-20261021";
        using var jobs = JsonDocument.Parse(File.ReadAllText(Path.Combine(evidence, "native-energy-controls-request.json")));
        var policy = ComparisonConfiguration.Load("tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json");
        var rows = new List<object>();
        foreach (var job in jobs.RootElement.GetProperty("jobs").EnumerateArray())
        {
            var id = job.GetProperty("id").GetString()!;
            var request = job.GetProperty("analysisRequest").Deserialize<CanonicalAnalysisRequest>(JsonFiles.Options)!;
            var settings = new Dictionary<string, string>(request.WorkbenchSettings)
            {
                ["PupilSampling"] = request.PupilSampling.ToString(),
                ["ImageSampling"] = request.ImageSampling.ToString()
            };
            request = request with { WorkbenchSettings = settings };
            var optic = OpticalFormatCatalog.Import(File.ReadAllText(job.GetProperty("input").GetString()!), ".zmx");
            optic.Aperture.Value *= job.GetProperty("apertureScale").GetDouble();
            var data = new WorkbenchRuntime(optic).BuildAnalysisData(request.CanonicalAnalysisKey, settings);
            var directory = Path.Combine(output, id);
            Directory.CreateDirectory(directory);
            JsonFiles.Write(Path.Combine(directory, "data.json"), data);
            var entry = AnalysisComparisonRegistry.Get(request.CanonicalAnalysisKey);
            var run = new AnalysisRun { Key = request.CanonicalAnalysisKey, Directory = id, Request = request };
            try
            {
                var native = ResultNormalizer.Zemax(Path.Combine(evidence, "native-energy-controls-v3", id, "data.json"), entry, request);
                var actual = ResultNormalizer.Workbench(Path.Combine(directory, "data.json"), entry, request);
                ComparisonRunner.Compare(run, entry, policy.Analyses[request.CanonicalAnalysisKey], actual, native, directory);
            }
            catch (InvalidDataException exception) { run.Conclusion = Conclusion.Incomparable; run.Reason = exception.Message; }
            JsonFiles.Write(Path.Combine(directory, "comparison.json"), run);
            rows.Add(new
            {
                id,
                lens = job.GetProperty("lens").GetString(),
                request.PupilSampling,
                apertureScale = job.GetProperty("apertureScale").GetDouble(),
                run.Conclusion,
                run.Reason,
                metrics = run.Metrics.Select(m => new { m.Id, m.MaxAbsolute, m.Nrmse, m.Conclusion })
            });
            JsonFiles.Write(Path.Combine(output, "summary.json"), rows);
            Console.WriteLine($"{id}: {run.Conclusion}; {string.Join(';', run.Metrics.Select(m => $"max={m.MaxAbsolute:G5} NRMSE={m.Nrmse:G5}"))}");
        }
    }
    public static void PsfPlane(string output)
    {
        if (Directory.Exists(output)) throw new IOException("Use a fresh output directory.");
        Directory.CreateDirectory(output);
        using var items = JsonDocument.Parse(File.ReadAllText("artifacts/validation/objectives-20261020/final-v7-intersection-fallback/items.json"));
        var item = items.RootElement.EnumerateArray().Single(i => i.GetProperty("lens").GetString() == "Objectives/Double Gauss 5 degree field.zmx"
            && i.GetProperty("run").GetProperty("key").GetString() == "FFT PSF Cross Section");
        var oldRun = item.GetProperty("run").Deserialize<AnalysisRun>(JsonFiles.Options)!;
        var optic = Optic.FromSnapshot(JsonFiles.Read<OpticSnapshot>(item.GetProperty("snapshot").GetString()!));
        var source = new PsfAnalysis(optic, 32, 64, 3, 1, zemaxCompatible: true).GenerateData();
        var nativePath = "artifacts/validation/objectives-residuals-20261021/native-psf-controls/double-gauss5-psf32/data.json";
        using var native = JsonDocument.Parse(File.ReadAllText(nativePath));
        var grid = native.RootElement.GetProperty("dataGrids")[0];
        var nx = grid.GetProperty("nx").GetInt32();
        var ny = grid.GetProperty("ny").GetInt32();
        var dx = grid.GetProperty("dx").GetDouble();
        var dy = grid.GetProperty("dy").GetDouble();
        var xmin = grid.GetProperty("minX").GetDouble();
        var ymin = grid.GetProperty("minY").GetDouble();
        var points = Enumerable.Range(0, ny).SelectMany(row => Enumerable.Range(0, nx).Select(column =>
            new AnalysisPoint(xmin + column * dx, ymin + row * dy, Value: grid.GetProperty("values")[row][column].GetDouble()))).ToArray();
        var actualHeatmap = source.PlotSeries.Single(s => s.Kind == AnalysisSeriesKind.Heatmap);
        var squared = points.Zip(actualHeatmap.Points, (a, b) => Math.Pow(a.Value!.Value - b.Value!.Value, 2)).Sum();
        Console.WriteLine($"Raw 32-grid relative L2={Math.Sqrt(squared / points.Sum(p => p.Value!.Value * p.Value.Value)):R}; max={points.Zip(actualHeatmap.Points, (a, b) => Math.Abs(a.Value!.Value - b.Value!.Value)).Max():R}");
        foreach (var control in new[] { "formal", "native-grid-with-formal-display" })
        {
            var selectedSource = control == "formal" ? source : source with { SeriesList = [actualHeatmap with { Points = points }] };
            var presentation = typeof(Optic).Assembly.GetType("OpticalSystemDesign.Core.Analysis.PsfProfilePresentation")!;
            var data = (AnalysisData)presentation.GetMethod("CreateCrossSectionData")!.Invoke(null,
                [oldRun.Key, "Diagnostic", selectedSource, "X-线性", "中心", 0d, true])!;
            var directory = Path.Combine(output, control);
            Directory.CreateDirectory(directory);
            JsonFiles.Write(Path.Combine(directory, "data.json"), data);
            var entry = AnalysisComparisonRegistry.Get(oldRun.Key);
            var expected = ResultNormalizer.Zemax(Path.Combine(item.GetProperty("path").GetString()!, "native/data.json"), entry, oldRun.Request!);
            var actual = ResultNormalizer.Workbench(Path.Combine(directory, "data.json"), entry, oldRun.Request!);
            var run = new AnalysisRun { Key = oldRun.Key, Directory = control, Request = oldRun.Request };
            ComparisonRunner.Compare(run, entry, item.GetProperty("settings").Deserialize<AnalysisConfiguration>(JsonFiles.Options)!, actual, expected, directory);
            JsonFiles.Write(Path.Combine(directory, "comparison.json"), run);
            Console.WriteLine($"{control}: {run.Conclusion}; {string.Join(';', run.Metrics.Select(m => $"max={m.MaxAbsolute:R} nrmse={m.Nrmse:R}"))}");
        }
    }
    public static void Run(string output)
    {
        if (Directory.Exists(output)) throw new IOException("Use a fresh control directory.");
        Directory.CreateDirectory(output);
        using var items = JsonDocument.Parse(File.ReadAllText("artifacts/validation/objectives-20261020/final-v7-intersection-fallback/items.json"));
        var rows = new List<object>();
        foreach (var item in items.RootElement.EnumerateArray())
        {
            var oldRun = item.GetProperty("run").Deserialize<AnalysisRun>(JsonFiles.Options)!;
            if (oldRun.Key is not ("Diffraction Encircled Energy" or "FFT PSF Cross Section" or "PSF")) continue;
            var request = oldRun.Request!;
            var optic = Optic.FromSnapshot(JsonFiles.Read<OpticSnapshot>(item.GetProperty("snapshot").GetString()!));
            var directory = Path.Combine(output, oldRun.Directory);
            Directory.CreateDirectory(directory);
            var data = new WorkbenchRuntime(optic).BuildAnalysisData(oldRun.Key, request.WorkbenchSettings);
            JsonFiles.Write(Path.Combine(directory, "data.json"), data);
            var entry = AnalysisComparisonRegistry.Get(oldRun.Key);
            var run = new AnalysisRun { Key = oldRun.Key, Directory = oldRun.Directory, Request = request };
            try
            {
                var native = ResultNormalizer.Zemax(Path.Combine(item.GetProperty("path").GetString()!, "native/data.json"), entry, request);
                var actual = ResultNormalizer.Workbench(Path.Combine(directory, "data.json"), entry, request);
                ComparisonRunner.Compare(run, entry, item.GetProperty("settings").Deserialize<AnalysisConfiguration>(JsonFiles.Options)!, actual, native, directory);
            }
            catch (Exception exception) when (exception is InvalidDataException or FileNotFoundException)
            { run.Conclusion = Conclusion.Incomparable; run.Reason = exception.Message; }
            JsonFiles.Write(Path.Combine(directory, "comparison.json"), run);
            rows.Add(new
            {
                lens = item.GetProperty("lens").GetString(),
                run.Key,
                run.Conclusion,
                run.Reason,
                metrics = run.Metrics.Select(m => new { m.Id, m.MaxAbsolute, m.Nrmse, m.Conclusion })
            });
            JsonFiles.Write(Path.Combine(output, "summary.json"), new { coreSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(Optic).Assembly.Location))), rows });
            Console.WriteLine($"{item.GetProperty("lens").GetString()} / {run.Key}: {run.Conclusion}; {string.Join(';', run.Metrics.Select(m => $"{m.Id} max={m.MaxAbsolute:G5} NRMSE={m.Nrmse:G5}"))}");
        }
    }
}

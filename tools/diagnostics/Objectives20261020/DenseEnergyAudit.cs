using System.Numerics;
using System.Reflection;
using System.Text.Json.Nodes;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Workbench;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Normalization;

internal static class DenseEnergyAudit
{
    private const BindingFlags Hidden = BindingFlags.Static | BindingFlags.NonPublic;

    internal static void CircularIntervalControls(string output)
    {
        if (Directory.Exists(output)) throw new IOException("Fresh output required");
        Directory.CreateDirectory(output);
        var rows = new List<object>();
        var policy = ComparisonConfiguration.Load("tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json");
        var manifest = JsonFiles.Read<System.Text.Json.JsonElement>("artifacts/validation/objectives-repair-20261023/native-sampling-controls/request-manifest.json");
        foreach (var item in manifest.EnumerateArray())
        {
            var path = item.GetProperty("request").GetString()!;
            var request = JsonFiles.Read<CanonicalAnalysisRequest>(path);
            if (request.CanonicalAnalysisKey != "Diffraction Encircled Energy") continue;
            NumericResult expected;
            try { expected = ResultNormalizer.Zemax(Path.Combine(item.GetProperty("output").GetString()!, "data.json"), AnalysisComparisonRegistry.Get(request.CanonicalAnalysisKey), request); }
            catch (InvalidDataException) { continue; }
            var raw = JsonNode.Parse(File.ReadAllText(path))!;
            var optic = WorkbenchExecutor.Import(raw["input"]!.GetValue<string>()).GetAwaiter().GetResult().Configurations[0];
            var wave = optic.Wavelengths[request.Wavelength - 1];
            var reference = optic.Wavelengths.First(w => w.IsPrimary);
            var cone = ((double Rms, double Tangential, double Sagittal))typeof(DiffractionEngine)
                .GetMethod("CumulativeEnergyCone", Hidden)!.Invoke(null, [optic, (0d, 0d), wave])!;
            var spectrum = ((ValueTuple<OpticalSystemDesign.Core.Domain.Wavelength, PsfResult>[])typeof(DiffractionEngine)
                .GetMethod("ComputeFftCumulativeEnergySpectrum", Hidden)!.Invoke(null, [optic, (0d, 0d), new[] { wave }, 256, false])!);
            var pitch = spectrum[0].Item2.SampleSpacingMicrometers;
            var sphere = typeof(DiffractionEngine).GetMethod("CumulativeEnergyReferenceSphere", Hidden)!.Invoke(null, [optic, (0d, 0d), reference]);
            var actual = new NumericResult(); var count = 0;
            for (var ideal = 0; ideal < 2; ideal++)
            {
                var step = 1 / Math.Sqrt(256);
                var grid = new PupilGridSpecification(256, true, true, step * 127);
                typeof(PupilGridSpecification).GetProperty("ExplicitCoordinateStep", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(grid, step);
                typeof(PupilGridSpecification).GetProperty("UseCachedFftStopMap", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(grid, true);
                object?[] args = [optic, (0d, 0d), wave, 256, 512, false, true, 0d, null, null, true, ideal == 0,
                    optic.RayAimingEnabled, reference, pitch, true, null, sphere, grid];
                var psf = (PsfResult)typeof(DiffractionEngine).GetMethod("ComputeFftPsfCore", Hidden)!.Invoke(null, args)!;
                count = ((Complex[,])args[16]!).Cast<Complex>().Count(c => c.Magnitude > 0);
                var analysis = new DiffractionEncircledEnergyAnalysis(optic, 256, 512, 401, request.Wavelength, 1, maximumDistanceMicrometers: 5);
                var curve = EnergyGridControls.Curve(analysis, psf, pitch);
                actual.Series.Add(new("curve:" + ideal, "circular interval control", curve.Select(p => p.X).ToArray(), curve.Select(p => p.Y).ToArray(), new("Radius", "Micrometer"), new("EnergyFraction", "Dimensionless")));
            }
            var id = Path.GetFileName(item.GetProperty("output").GetString()!);
            var run = new AnalysisRun { Key = request.CanonicalAnalysisKey, Directory = id, Request = request };
            ComparisonRunner.Compare(run, AnalysisComparisonRegistry.Get(run.Key), policy.Analyses[run.Key], actual, expected, output);
            rows.Add(new { id, coneEqual = cone.Rms == cone.Tangential && cone.Rms == cone.Sagittal, count, conclusion = run.Conclusion, metrics = run.Metrics });
            JsonFiles.Write(Path.Combine(output, "summary.json"), rows);
            Console.WriteLine($"{id}: {run.Conclusion}, pupil={count}");
        }
    }

    internal static void Prepare(string output)
    {
        if (Directory.Exists(output)) throw new IOException("Fresh output required");
        Directory.CreateDirectory(output);
        var jobs = new List<object>(); var rows = new List<object>();
        var manifest = JsonFiles.Read<System.Text.Json.JsonElement>("artifacts/validation/objectives-repair-20261023/native-sampling-controls/request-manifest.json");
        foreach (var item in manifest.EnumerateArray().Where(j => j.GetProperty("originalKey").GetString() is { } key
            && (key.StartsWith("Objectives/Double Gauss 28", StringComparison.Ordinal)
                || key.StartsWith("Objectives/Gaussian Quadrature Over Wavelength", StringComparison.Ordinal))))
        {
            var requestPath = item.GetProperty("request").GetString()!;
            var raw = JsonNode.Parse(File.ReadAllText(requestPath))!.AsObject();
            var request = JsonFiles.Read<CanonicalAnalysisRequest>(requestPath);
            var optic = WorkbenchExecutor.Import(raw["input"]!.GetValue<string>()).GetAwaiter().GetResult().Configurations[0];
            var wave = optic.Wavelengths[request.Wavelength - 1];
            var result = ((ValueTuple<OpticalSystemDesign.Core.Domain.Wavelength, PsfResult>[])typeof(DiffractionEngine)
                .GetMethod("ComputeFftCumulativeEnergySpectrum", Hidden)!.Invoke(null, [optic, (0d, 0d), new[] { wave }, 256, false])!)[0].Item2;
            var cone = typeof(DiffractionEngine).GetMethod("CumulativeEnergyCone", Hidden)!.Invoke(null, [optic, (0d, 0d), wave]);
            var id = Path.GetFileName(item.GetProperty("output").GetString()!);
            foreach (var type in new[] { "Linear", "Real", "Imaginary" })
            {
                var directory = Path.GetFullPath(Path.Combine(output, id + "-" + type)); Directory.CreateDirectory(directory);
                var q = raw.DeepClone().AsObject();
                q["canonicalAnalysisKey"] = "PSF"; q["adapter"] = "contract"; q["analysisType"] = "FftPsf";
                q["imageSampling"] = 512; q["gridSize"] = 512; q["imageDeltaMicrometers"] = result.SampleSpacingMicrometers;
                q["zemaxSettings"] = new JsonObject { ["SampleSize"] = "PsfS_256x256", ["OutputSize"] = "PsfS_512x512",
                    ["ImageDelta"] = result.SampleSpacingMicrometers, ["Field"] = request.Field, ["Wavelength"] = request.Wavelength,
                    ["Surface"] = -1, ["Rotation"] = "CW0", ["UsePolarization"] = false, ["Normalize"] = false, ["Type"] = type };
                var path = Path.Combine(directory, "request.json"); File.WriteAllText(path, q.ToJsonString(JsonFiles.Options));
                jobs.Add(new { id = Path.GetFileName(directory), request = path, output = directory });
            }
            JsonFiles.Write(Path.Combine(output, id + "-core-psf.json"), new { pitch = result.SampleSpacingMicrometers,
                values = Enumerable.Range(0, 512).Select(y => Enumerable.Range(0, 512).Select(x => result.Values[y, x]).ToArray()).ToArray() });
            var moments = ((double Rms, double Tangential, double Sagittal))cone!;
            var grid = (PupilGridSpecification)typeof(DiffractionEngine).GetMethod("ExpandedCumulativeEnergyGrid", Hidden)!
                .Invoke(null, [optic, (0d, 0d), wave, 256, result.SampleSpacingMicrometers, moments.Tangential, moments.Sagittal])!;
            var contains = typeof(PupilGridSpecification).GetMethod("ContainsSample", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var support = Enumerable.Range(0, 256).SelectMany(y => Enumerable.Range(0, 256)
                .Where(x => (bool)contains.Invoke(grid, [x, y])!).Select(x => new { x, y })).ToArray();
            JsonFiles.Write(Path.Combine(output, id + "-expanded-core-support.json"), support);
            rows.Add(new { id, rms = moments.Rms, tangential = moments.Tangential, sagittal = moments.Sagittal,
                expandedCoreSupport = support.Length, pitch = result.SampleSpacingMicrometers });
        }
        JsonFiles.Write(Path.Combine(output, "native-jobs.json"), jobs);
        JsonFiles.Write(Path.Combine(output, "stage.json"), rows);
    }

    internal static void Inspect(string root)
    {
        var rows = new List<object>();
        foreach (var stage in JsonFiles.Read<System.Text.Json.JsonElement>(Path.Combine(root, "stage.json")).EnumerateArray())
        {
            var id = stage.GetProperty("id").GetString()!;
            double[,] Read(string type)
            {
                var raw = JsonFiles.Read<System.Text.Json.JsonElement>(Path.Combine(root, id + "-" + type, "data.json"));
                var grid = raw.GetProperty("dataGrids")[0].GetProperty("values");
                var values = new double[512, 512];
                for (var y = 0; y < 512; y++) for (var x = 0; x < 512; x++) values[y, x] = grid[y][x].GetDouble();
                return values;
            }
            var real = Read("Real"); var imaginary = Read("Imaginary"); var linear = Read("Linear");
            var spectrum = new Complex[512, 512];
            var irradianceError = 0d;
            for (var y = 0; y < 512; y++) for (var x = 0; x < 512; x++)
            {
                var value = new Complex(real[y, x], imaginary[y, x]);
                spectrum[(y + 257) % 512, (x + 257) % 512] = value;
                irradianceError = Math.Max(irradianceError, Math.Abs(value.Magnitude * value.Magnitude - linear[y, x]));
            }
            typeof(DiffractionEngine).GetMethod("FourierTransform2D", Hidden)!.Invoke(null, [spectrum, true]);
            var peak = spectrum.Cast<Complex>().Max(v => v.Magnitude);
            var pupil = Enumerable.Range(0, 512).SelectMany(y => Enumerable.Range(0, 512)
                .Where(x => spectrum[y, x].Magnitude > peak * 1e-6).Select(x => new { x, y, magnitude = spectrum[y, x].Magnitude })).ToArray();
            JsonFiles.Write(Path.Combine(root, id + "-native-pupil.json"), pupil);
            var core = JsonFiles.Read<System.Text.Json.JsonElement>(Path.Combine(root, id + "-core-psf.json")).GetProperty("values");
            var maximumError = 0d;
            for (var y = 0; y < 512; y++) for (var x = 0; x < 512; x++)
                maximumError = Math.Max(maximumError, Math.Abs(core[y][x].GetDouble() / 100 - linear[(y + 511) % 512, (x + 511) % 512]));
            rows.Add(new { id, nativePupilCount = pupil.Length, irradianceError, maximumPsfError = maximumError });
        }
        JsonFiles.Write(Path.Combine(root, "inspection.json"), rows);
    }
}

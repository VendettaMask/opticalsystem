using System.Text.Json;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Metrics;

internal static class SpectrumStudy
{
    internal static readonly double[] Frequencies = [.1, .2, .4, .8, 1.6, 3.2];
    internal static Series1DResult[] Read(string root, string id, string sourceHash)
    {
        var folder = Path.Combine(root, "native-spectra");
        var execution = JsonFiles.Read<JsonElement>(Path.Combine(folder, "execution.json")).EnumerateArray().Single(v => v.GetProperty("id").GetString() == id);
        if (execution.GetProperty("result").GetProperty("ExitCode").GetInt32() != 0 || !execution.GetProperty("sourceUnchanged").GetBoolean())
            throw new InvalidDataException("Incomplete native spectral scan");
        var data = JsonFiles.Read<JsonElement>(Path.Combine(folder, id, "spectra.json"));
        var request = data.GetProperty("request");
        if (!data.GetProperty("complete").GetBoolean() || data.GetProperty("savedInput").GetBoolean()
            || request.GetProperty("inputSha256").GetString() != sourceHash || request.GetProperty("captureScreenshots").GetBoolean()
            || request.GetProperty("configuration").GetInt32() != 1 || request.GetProperty("wavelength").GetInt32() != 0)
            throw new InvalidDataException("Native spectral provenance differs");
        var rows = data.GetProperty("rows").EnumerateArray().ToArray();
        if (rows.Length != 10) throw new InvalidDataException("Ten physical field nodes required");
        var x = Enumerable.Range(0, 10).Select(i => i / 9d).ToArray();
        var display = Enumerable.Range(0, 300).Select(i => i / 299d).ToArray();
        return Enumerable.Range(0, 12).Select(k =>
        {
            var values = rows.Select((r, i) =>
            {
                var s = r.GetProperty("settings"); var p = s.GetProperty("properties");
                if (r.GetProperty("x").GetDouble() != 0 || Math.Abs(r.GetProperty("y").GetDouble() - 2.5 * x[i]) > 1e-14
                    || s.GetProperty("field").GetInt32() != 2 || s.GetProperty("wavelength").GetInt32() != 0
                    || p.GetProperty("Configuration").GetInt32() != 2 || p.GetProperty("UsePolarization").GetBoolean()
                    || p.GetProperty("PupilSampleSize").GetString() != $"S_{request.GetProperty("pupilSampling").GetInt32()}x{request.GetProperty("pupilSampling").GetInt32()}"
                    || p.GetProperty("ImageSampleSize").GetString() != $"S_{request.GetProperty("imageSampling").GetInt32()}x{request.GetProperty("imageSampling").GetInt32()}"
                    || p.GetProperty("ImageDelta").GetDouble() != request.GetProperty("imageDeltaMicrometers").GetDouble())
                    throw new InvalidDataException("Native spectral settings differ");
                var raw = r.GetProperty("data");
                if (raw.GetProperty("messages").GetArrayLength() != 0) throw new InvalidDataException("Native messages require review");
                var series = raw.GetProperty("dataSeries")[0]; var index = 8 << (k / 2);
                var nativeFrequency = series.GetProperty("x")[index].GetDouble();
                if (Math.Abs(nativeFrequency / (Math.PI * 1000 / 10800) - Frequencies[k / 2]) > 1e-12)
                    throw new InvalidDataException("Exact frequency knot missing; frequency interpolation forbidden");
                return series.GetProperty("y")[index][k % 2].GetDouble();
            }).ToArray();
            return new Series1DResult("curve", "NativeSpectrumNaturalFieldDisplay", display, Natural(x, values, display),
                new("NormalizedField", "Dimensionless"), new("Modulation", "Dimensionless"));
        }).ToArray();
    }
    // Independent natural cubic display reconstruction, never used by the product.
    private static double[] Natural(double[] x, double[] y, double[] targets)
    {
        var n = x.Length; var diag = new double[n]; var rhs = new double[n]; var upper = new double[n];
        diag[0] = diag[^1] = 1;
        for (var i = 1; i < n - 1; i++)
        {
            var left = x[i] - x[i - 1]; var right = x[i + 1] - x[i];
            var multiplier = left / diag[i - 1];
            diag[i] = 2 * (left + right) - multiplier * upper[i - 1]; upper[i] = right;
            rhs[i] = 6 * ((y[i + 1] - y[i]) / right - (y[i] - y[i - 1]) / left) - multiplier * rhs[i - 1];
        }
        var second = new double[n]; for (var i = n - 2; i >= 0; i--) second[i] = (rhs[i] - upper[i] * second[i + 1]) / diag[i];
        return targets.Select(t =>
        {
            var j = Math.Clamp(Array.BinarySearch(x, t) is var q && q >= 0 ? q : ~q - 1, 0, n - 2);
            var h = x[j + 1] - x[j]; var a = (x[j + 1] - t) / h; var b = (t - x[j]) / h;
            return a * y[j] + b * y[j + 1] + ((a * a * a - a) * second[j] + (b * b * b - b) * second[j + 1]) * h * h / 6;
        }).ToArray();
    }
    internal static void Run(string repository, string root)
    {
        var source = JsonFiles.Read<JsonElement>(Path.Combine(root, "cases.json"))[0].GetProperty("sourceSha256").GetString()!;
        var gate = ComparisonConfiguration.Load(Path.Combine(repository, "tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json"))
            .Analyses["Huygens MTF vs Field"].Quantities["Modulation"];
        var comparisons = new List<object>();
        void Compare(string name, Func<Series1DResult[]> a, Func<Series1DResult[]> b)
        {
            try
            {
                var first = a(); var second = b();
                for (var k = 0; k < 12; k++)
                {
                    var aligned = ComparisonMetrics.Align(first[k], second[k], []);
                    var metric = ComparisonMetrics.Calculate(name, "Dimensionless", aligned, gate);
                    comparisons.Add(new { name, frequency = Frequencies[k / 2], direction = k % 2 == 0 ? "T" : "S", metric,
                        pointwiseViolations = aligned.Workbench.Zip(aligned.Zemax, (x,y) => Math.Abs(x-y) > gate.Absolute + gate.Relative*Math.Abs(y)).Count(v=>v) });
                    Console.WriteLine($"{name}/{k}: {metric.Conclusion}; {metric.Nrmse:R}; {metric.MaxAbsolute:R}");
                }
            }
            catch (Exception error) when (error is IOException or InvalidOperationException or InvalidDataException)
            { comparisons.Add(new { name, error = error.Message }); }
        }
        Series1DResult[] Native(string id) => Read(root, id, source);
        Compare("BootstrapSpectra64VsDirect64", () => Native("bootstrap64"), () => NativeCurves.Read(root, "huygens", 64, source, Frequencies));
        Compare("NativePupil128To256", () => Native("p128-i64-d04"), () => Native("p256-i64-d04"));
        Compare("NativePupil256To512", () => Native("p256-i64-d04"), () => Native("p512-i64-d04"));
        Compare("NativeWindow64To128", () => Native("p512-i64-d04"), () => Native("p512-i128-d04"));
        var windowCohort = Path.Combine(root, "core-window-cohort.json");
        var cohort = JsonFiles.Read<JsonElement>(File.Exists(windowCohort) ? windowCohort : Path.Combine(root, "core-cohort.json")).GetProperty("coreSha256").GetString()!;
        Series1DResult[] Core(int n)
        {
            var folder = Path.Combine(root, "core-" + cohort[..12], "window-p" + n + "-i64-d04");
            if (JsonFiles.Read<JsonElement>(Path.Combine(folder,"identity.json")).GetProperty("coreSha256").GetString() != cohort) throw new InvalidDataException("Core cohort differs");
            var data = JsonFiles.Read<JsonElement>(Path.Combine(folder,"huygens.json"));
            if (data.GetProperty("outcome").GetString() != "Success") throw new InvalidDataException("Core unavailable");
            return data.GetProperty("seriesList").EnumerateArray().Select(s => {
                var points=s.GetProperty("points").EnumerateArray().ToArray();
                return new Series1DResult("curve","Core",points.Select(p=>p.GetProperty("x").GetDouble()).ToArray(),points.Select(p=>p.GetProperty("y").GetDouble()).ToArray(),
                    new("NormalizedField","Dimensionless"),new("Modulation","Dimensionless")); }).ToArray();
        }
        Compare("CorePupil64To128", () => Core(64), () => Core(128));
        Compare("Core128VsNative512", () => Core(128), () => Native("p512-i128-d04"));
        JsonFiles.Write(Path.Combine(root,"spectra-assessment.json"),new { referenceBasis="NativeFrequencyKnotsAndIndependentNaturalFieldDisplay",coreSha256=cohort,comparisons });
    }
}

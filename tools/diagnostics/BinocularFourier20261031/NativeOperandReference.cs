using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.ZemaxComparison;

// Diagnostic reference normalization only. No optical calculation or Core spline is used here.
internal static class NativeOperandReference
{
    private static readonly double[] Frequencies = [.1, .2, .4, .8, 1.6, 3.2];
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    internal static double[] Interpolate(double[] x, double[] y, double[] targets, bool capturedBoundary = true)
    {
        var n = x.Length;
        var matrix = new double[n, n + 1];
        matrix[0, 0] = 1;
        // Captured display boundary: s''(0)=0; s'(1)-s''(1)/3=y(1).
        // This diagnostic rule is accepted only after whole-buffer proof on independent native captures.
        var endWidth = x[n - 1] - x[n - 2];
        matrix[n - 1, n - 2] = endWidth;
        matrix[n - 1, n - 1] = 2 * (endWidth - 1);
        matrix[n - 1, n] = 6 * (y[n - 1] - (y[n - 1] - y[n - 2]) / endWidth);
        if (!capturedBoundary)
        {
            matrix[n - 1, n - 2] = 0; matrix[n - 1, n - 1] = 1; matrix[n - 1, n] = 0;
        }
        for (var i = 1; i < n - 1; i++)
        {
            var left = x[i] - x[i - 1]; var right = x[i + 1] - x[i];
            matrix[i, i - 1] = left; matrix[i, i] = 2 * (left + right); matrix[i, i + 1] = right;
            matrix[i, n] = 6 * ((y[i + 1] - y[i]) / right - (y[i] - y[i - 1]) / left);
        }
        // Independent dense pivoted elimination, rather than the product's spline implementation.
        for (var column = 0; column < n; column++)
        {
            var pivot = Enumerable.Range(column, n - column).MaxBy(row => Math.Abs(matrix[row, column]));
            for (var j = column; j <= n; j++) (matrix[column, j], matrix[pivot, j]) = (matrix[pivot, j], matrix[column, j]);
            var divisor = matrix[column, column];
            if (Math.Abs(divisor) < 1e-15) throw new InvalidDataException("Singular native display interpolation");
            for (var j = column; j <= n; j++) matrix[column, j] /= divisor;
            for (var row = 0; row < n; row++) if (row != column)
            {
                var factor = matrix[row, column];
                for (var j = column; j <= n; j++) matrix[row, j] -= factor * matrix[column, j];
            }
        }
        return targets.Select(t =>
        {
            var upper = 1; while (upper < n - 1 && t > x[upper]) upper++;
            var lower = upper - 1; var width = x[upper] - x[lower];
            var a = (x[upper] - t) / width; var b = (t - x[lower]) / width;
            return a * y[lower] + b * y[upper]
                + ((a * a * a - a) * matrix[lower, n] + (b * b * b - b) * matrix[upper, n]) * width * width / 6;
        }).ToArray();
    }
    private static double[][][] Read(string folder, string requestPath, int config, int sampling)
    {
        var request = JsonFiles.Read<JsonElement>(requestPath);
        var execution = JsonFiles.Read<JsonElement>(Path.Combine(Path.GetDirectoryName(folder)!, "execution.json"))
            .EnumerateArray().Single(r => r.GetProperty("id").GetString() == Path.GetFileName(folder));
        if (execution.GetProperty("result").GetProperty("ExitCode").GetInt32() != 0 || !execution.GetProperty("sourceUnchanged").GetBoolean())
            throw new InvalidDataException("Native node capture did not complete successfully");
        if (request.GetProperty("configuration").GetInt32() != config || request.GetProperty("captureScreenshots").GetBoolean()
            || Hash(request.GetProperty("input").GetString()!) != request.GetProperty("inputSha256").GetString())
            throw new InvalidDataException("Native input identity changed");
        var model = JsonFiles.Read<JsonElement>(Path.Combine(folder, "model.json"));
        if (!model.GetProperty("aperture").GetProperty("AFocalImageSpace").GetBoolean()
            || model.GetProperty("units").GetProperty("AfocalModeUnits").GetString() != "Milliradians")
            throw new InvalidDataException("Native physical mode/unit changed");
        var fields = JsonFiles.Read<JsonElement>(Path.Combine(folder, "fourier-fields.json"));
        if (fields.GetProperty("fieldType").GetString() != "Angle" || fields.GetProperty("originalMaximum").GetDouble() != 1.5)
            throw new InvalidDataException("Native scan extent changed");
        var actual = fields.GetProperty("actual");
        if (actual.GetArrayLength() != 10) throw new InvalidDataException("Ten native calculation fields required");
        for (var i = 0; i < 10; i++)
        {
            var field = actual[i];
            if (field.GetProperty("X").GetDouble() != 0 || field.GetProperty("Y").GetDouble() != 1.5 * i / 9d)
                throw new InvalidDataException("Native calculation field differs");
            foreach (var key in new[] { "VDX", "VDY", "VCX", "VCY", "VAN", "TAN" })
                if (field.GetProperty(key).GetDouble() != 0) throw new InvalidDataException("Native vignetting was not removed");
        }
        var data = JsonFiles.Read<JsonElement>(Path.Combine(folder, "fourier-operands.json"));
        if (data.GetProperty("configuration").GetInt32() != config || data.GetProperty("savedInput").GetBoolean())
            throw new InvalidDataException("Native operand capture identity differs");
        var rows = data.GetProperty("rows").EnumerateArray().ToArray();
        if (rows.Length != 60) throw new InvalidDataException("All sixty native T/S pairs required");
        return Frequencies.Select(f => Enumerable.Range(1, 10).Select(field =>
        {
            var row = rows.Single(r => r.GetProperty("input").GetProperty("frequency").GetDouble() == f
                && r.GetProperty("input").GetProperty("field").GetInt32() == field);
            var input = row.GetProperty("input");
            if (input.GetProperty("sampling").GetInt32() != (int)Math.Log2(sampling / 16d)
                || input.GetProperty("wavelength").GetInt32() != 0 || input.GetProperty("grid").GetInt32() != 0)
                throw new InvalidDataException("Native Fourier operand settings differ");
            var values = row.GetProperty("values");
            var pair = new[] { values.GetProperty("MTFT/0").GetDouble(), values.GetProperty("MTFS/0").GetDouble() };
            if (pair.Any(v => !double.IsFinite(v))) throw new InvalidDataException("Nonfinite native modulation");
            return pair;
        }).ToArray()).ToArray();
    }
    internal static void Run(string repository, string root)
    {
        var prior = Path.Combine(repository, "artifacts/validation/afocal-mtf-field-20261031");
        var old128 = Path.Combine(repository, "artifacts/validation/binocular-fourier-cause-20261031/native/scan128-all");
        var proofs = new List<object>();
        var naturalControls = new List<object>();
        var coordinates = Enumerable.Range(0, 10).Select(i => i / 9d).ToArray();
        double[]? displayCoordinates = null;
        void Prove(int config, int sampling, string folder, string request, string captured, bool highOnly = false)
        {
            var nodes = Read(folder, request, config, sampling);
            var native = JsonFiles.Read<JsonElement>(Path.Combine(captured, "data.json")).GetProperty("dataSeries");
            if (native.GetArrayLength() != (highOnly ? 1 : 6)) throw new InvalidDataException("Unexpected raw native frequency count");
            var settings = JsonFiles.Read<JsonElement>(Path.Combine(captured, "captured-settings.json"));
            if (settings.GetProperty("properties").GetProperty("SampleSize").GetString() != $"S_{sampling}x{sampling}"
                || settings.GetProperty("request").GetProperty("configuration").GetInt32() != config)
                throw new InvalidDataException("Proof capture identity differs");
            for (var frequency = highOnly ? 5 : 0; frequency < 6; frequency++) for (var direction = 0; direction < 2; direction++)
            {
                var series = native[highOnly ? 0 : frequency];
                var x = series.GetProperty("x").EnumerateArray().Select(v => v.GetDouble()).ToArray();
                displayCoordinates ??= x;
                if (!x.SequenceEqual(displayCoordinates)) throw new InvalidDataException("Native display grid differs");
                var reconstructed = Interpolate(coordinates, nodes[frequency].Select(v => v[direction]).ToArray(), x);
                var y = series.GetProperty("y").EnumerateArray().Select(v => v[direction].GetDouble()).ToArray();
                var maximum = y.Zip(reconstructed, (a, b) => Math.Abs(a - b)).Max();
                var natural = Interpolate(coordinates, nodes[frequency].Select(v => v[direction]).ToArray(), x, capturedBoundary: false);
                naturalControls.Add(new { config, sampling, frequency = Frequencies[frequency], direction,
                    maximumAbsoluteError = y.Zip(natural, (a, b) => Math.Abs(a - b)).Max() });
                proofs.Add(new { config, sampling, frequency = Frequencies[frequency], direction, points = y.Length, maximumAbsoluteError = maximum,
                    rawCurve = Path.Combine(captured, "data.json"), rawCurveSha256 = Hash(Path.Combine(captured, "data.json")),
                    rawNodes = Path.Combine(folder, "fourier-operands.json"), rawNodesSha256 = Hash(Path.Combine(folder, "fourier-operands.json")) });
                Console.WriteLine($"Display proof C{config}/N{sampling}/F{Frequencies[frequency]}/{direction}: {maximum:R}");
                if (maximum > 1e-10) throw new InvalidDataException("Native display interpolation was not established");
            }
        }
        Prove(1, 128, Path.Combine(root, "node-control/nodes"), Path.Combine(root, "node-control/nodes.request.json"), old128);
        Prove(1, 32, Path.Combine(root, "operands-c1/c1-n32"), Path.Combine(root, "operands-c1/c1-n32.request.json"), Path.Combine(prior, "native/m3-fourier"));
        Prove(2, 32, Path.Combine(root, "operands-c2/c2-n32"), Path.Combine(root, "operands-c2/c2-n32.request.json"), Path.Combine(prior, "native/m4-fourier"));
        foreach (var (config, sampling) in new[] { (1, 256), (2, 128), (2, 256) })
        {
            var relative = $"operands-c{config}/c{config}-n{sampling}";
            Prove(config, sampling, Path.Combine(root, relative), Path.Combine(root, relative + ".request.json"),
                Path.Combine(repository, $"artifacts/validation/binocular-fourier-cause-20261031/single-frequency-native/c{config}-n{sampling}"), highOnly: true);
        }
        var proofPath = Path.Combine(root, "native-display-proof.json");
        JsonFiles.Write(proofPath, new { method = "IndependentDenseCubicWithCapturedDisplayBoundary", productCoreUsed = false,
            leftBoundary = "s''(0)=0", rightBoundary = "s'(1)-s''(1)/3=y(1)", proofTolerance = 1e-10, proofs });
        JsonFiles.Write(Path.Combine(root, "native-display-natural-control.json"), new { acceptedReference = false,
            reason = "Natural right boundary does not reproduce the captured native display; never substitute this control for missing native data", naturalControls });
        foreach (var config in new[] { 1, 2 }) foreach (var sampling in new[] { 128, 256 })
        {
            var relative = config == 1 && sampling == 128 ? "node-control/nodes" : $"operands-c{config}/c{config}-n{sampling}";
            var folder = Path.Combine(root, relative); var request = folder + ".request.json";
            var nodes = Read(folder, request, config, sampling);
            var target = Path.Combine(root, "derived-curves", $"c{config}-n{sampling}"); Directory.CreateDirectory(target);
            var all = Frequencies.Select((frequency, index) =>
            {
                var t = Interpolate(coordinates, nodes[index].Select(v => v[0]).ToArray(), displayCoordinates!);
                var s = Interpolate(coordinates, nodes[index].Select(v => v[1]).ToArray(), displayCoordinates!);
                return new { index, frequencyCyclesPerMilliradian = frequency, x = displayCoordinates,
                    y = t.Zip(s, (a, b) => new[] { a, b }).ToArray() };
            }).ToArray();
            JsonFiles.Write(Path.Combine(target, "curve.json"), new { provenance = "NativeOperandsWithValidatedDisplayInterpolation", config, sampling,
                rawNodes = relative + "/fourier-operands.json", rawNodesSha256 = Hash(Path.Combine(folder, "fourier-operands.json")),
                splineProofSha256 = Hash(proofPath), calculatedFieldCount = 10, productCoreUsed = false, dataSeries = all });
            foreach (var name in new[] { "model.json", "fourier-fields.json", "fourier-operands.json", "environment.json", "polygon-dependencies.json" })
                File.Copy(Path.Combine(folder, name), Path.Combine(target, name), true);
            File.Copy(request, Path.Combine(target, "request.json"), true);
        }
    }
}

using System.Numerics;
using System.Reflection;
using System.Text.Json;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.ZemaxComparison;

internal static class ComplexPupilDiagnosis
{
    internal static void Run(string native, string output, int pupilSampling = 32)
    {
        var size = 2 * pupilSampling;
        var stretch = Math.Sqrt(Math.Max(1, pupilSampling / 32d));
        var indexScale = (pupilSampling / 2d - 1) / stretch;
        var real = Grid(Path.Combine(native, $"psf-real-{pupilSampling}/data.json"), size);
        var imag = Grid(Path.Combine(native, $"psf-imaginary-{pupilSampling}/data.json"), size);
        var intensity = Grid(Path.Combine(native, $"psf-{pupilSampling}/data.json"), size);
        using var rays = JsonDocument.Parse(File.ReadAllText(Path.Combine(native, $"pupil-{pupilSampling}/pupil-grid.json")));
        var inputs = rays.RootElement.GetProperty("inputs"); var rows = rays.RootElement.GetProperty("rows");
        var spectrum = new Complex[size, size]; var maxIntensityDifference = 0d;
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                // The formal/native display starts at -31 rather than -32.
                // Undo that published periodic index convention, not a fitted shift.
                spectrum[(y + 1) % size, (x + 1) % size] = new(real[y][x], imag[y][x]);
                maxIntensityDifference = Math.Max(maxIntensityDifference,
                    Math.Abs(real[y][x] * real[y][x] + imag[y][x] * imag[y][x] - intensity[y][x]));
            }
        var shift = typeof(DiffractionEngine).GetMethod("FftShift", BindingFlags.NonPublic | BindingFlags.Static)!;
        var fft = typeof(DiffractionEngine).GetMethod("Fft2D", BindingFlags.NonPublic | BindingFlags.Static)!;
        var pupil = (Complex[,])shift.Invoke(null, [spectrum])!;
        for (var y = 0; y < size; y++) for (var x = 0; x < size; x++) pupil[y, x] = Complex.Conjugate(pupil[y, x]);
        fft.Invoke(null, [pupil]);
        // Inverse DFT scaling and the captured pupil count, not a fitted gain.
        var count = rows.GetArrayLength();
        for (var y = 0; y < size; y++) for (var x = 0; x < size; x++) pupil[y, x] = Complex.Conjugate(pupil[y, x]) * count / (size * size);
        // Present the inverse transform with its zero-index Fourier origin at
        // the array center, as for the formal padded pupil representation.
        pupil = (Complex[,])shift.Invoke(null, [pupil])!;
        var comparisons = Enumerable.Range(0, count).Select(i =>
        {
            var input = inputs[i]; var row = rows[i];
            var px = input.GetProperty("px").GetDouble(); var py = input.GetProperty("py").GetDouble();
            var x = (int)Math.Round(pupilSampling + px * indexScale); var y = (int)Math.Round(pupilSampling + py * indexScale);
            var value = pupil[y, x]; var opd = row.GetProperty("opd").GetDouble();
            var difference = Wrap(value.Phase + 2 * Math.PI * opd) / (2 * Math.PI);
            var oppositeSignDifference = Wrap(value.Phase - 2 * Math.PI * opd) / (2 * Math.PI);
            return new { px, py, amplitude = value.Magnitude, value.Real, value.Imaginary,
                nativeBatchOpdWaves = opd, inverseFieldPhaseWaves = value.Phase / (2 * Math.PI),
                phaseDifferenceWaves = difference, oppositeSignDifferenceWaves = oppositeSignDifference };
        }).ToArray();
        var phaseMap = comparisons.ToDictionary(p => ((int)Math.Round(p.px * indexScale), (int)Math.Round(p.py * indexScale)));
        var averagedNodes = 0;
        var averagedPhaseDifferences = phaseMap.Select(pair =>
        {
            var (x, y) = pair.Key;
            var neighbors = new[] { (x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1) };
            var expectedPhase = pair.Value.nativeBatchOpdWaves;
            if ((x + y) % 2 == 0 && (x != 0 || y != 0) && neighbors.All(phaseMap.ContainsKey))
            {
                expectedPhase = neighbors.Average(p => phaseMap[p].nativeBatchOpdWaves);
                averagedNodes++;
            }
            return Wrap(2 * Math.PI * (pair.Value.inverseFieldPhaseWaves - expectedPhase)) / (2 * Math.PI);
        }).ToArray();
        var inferred = new HashSet<(int X, int Y)>();
        for (var y = 0; y < size; y++) for (var x = 0; x < size; x++) if (pupil[y, x].Magnitude > 1e-8) inferred.Add((x, y));
        var expected = Enumerable.Range(0, count).Select(i => ((int)Math.Round(pupilSampling + inputs[i].GetProperty("px").GetDouble() * indexScale),
            (int)Math.Round(pupilSampling + inputs[i].GetProperty("py").GetDouble() * indexScale))).ToHashSet();
        var summary = new { count, maxIntensityDifference, inferredCount = inferred.Count,
            missingMaskPoints = expected.Except(inferred).Count(), extraMaskPoints = inferred.Except(expected).Count(),
            minimumAmplitude = comparisons.Min(p => p.amplitude), maximumAmplitude = comparisons.Max(p => p.amplitude),
            maxPhaseDifferenceWaves = comparisons.Max(p => Math.Abs(p.phaseDifferenceWaves)),
            maxOppositeSignDifferenceWaves = comparisons.Max(p => Math.Abs(p.oppositeSignDifferenceWaves)),
            averagedNodes, maxAveragedPhaseDifferenceWaves = averagedPhaseDifferences.Max(Math.Abs),
            comparisons, semantics = "Numerical inverse transform of independently captured native real/imaginary FFT fields using the formal Core FFT routine. Published periodic grid and centered inverse-transform conventions and native pupil count only; no fitted shift, gain, piston or optical prescription change. The four-neighbor phase control is a diagnostic hypothesis, not a direct native pupil API export or product implementation." };
        JsonFiles.Write(Path.Combine(output, "complex-pupil-summary.json"), summary);
        Console.WriteLine($"Complex pupil: mask missing={summary.missingMaskPoints}, extra={summary.extraMaskPoints}; amplitude={summary.minimumAmplitude:R}..{summary.maximumAmplitude:R}; phase diff={summary.maxPhaseDifferenceWaves:R}");
        Console.WriteLine($"Checkerboard four-neighbor control: averaged={averagedNodes}, phase difference={summary.maxAveragedPhaseDifferenceWaves:R}");
    }
    private static double Wrap(double phase) => Math.Atan2(Math.Sin(phase), Math.Cos(phase));
    private static double[][] Grid(string path, int size)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var g = doc.RootElement.GetProperty("dataGrids")[0];
        if (g.GetProperty("nx").GetInt32() != size || g.GetProperty("ny").GetInt32() != size) throw new InvalidDataException("Unexpected native grid size.");
        return g.GetProperty("values").Deserialize<double[][]>()!;
    }
}

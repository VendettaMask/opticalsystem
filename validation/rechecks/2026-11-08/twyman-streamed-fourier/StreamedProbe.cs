using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Serialization;

var options = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true, WriteIndented = true,
    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    Converters = { new JsonStringEnumConverter() }
};
var snapshot = JsonSerializer.Deserialize<OpticSnapshot>(File.ReadAllText(args[0]), options)!;
var optic = Optic.FromSnapshot(snapshot);
if (optic.RayAimingEnabled || optic.ImageSpaceAfocal || optic.Wavelengths.Count != 1)
    throw new InvalidDataException("The diagnostic assumes scalar monochromatic focal space and the original unaimed grid");
var wave = optic.Wavelengths[0];
const int display = 128;
const double pitch = .25;
var fft = (Action<Complex[]>)typeof(DiffractionEngine).GetMethod("Fft", BindingFlags.NonPublic | BindingFlags.Static)!
    .CreateDelegate(typeof(Action<Complex[]>));
var control = DiffractionEngine.ComputeFftPsf(optic, (0, 0), wave, 256, 512, usePolarization: false,
    cellCenteredPupil: true, zemaxFftSampling: true, imageDelta: pitch, ignoreScalarTransmission: true,
    interpolateScalarPupilAmplitude: true, croppedOutputSize: display);
foreach (var nText in args.Skip(2))
{
    var count = int.Parse(nText);
    var size = checked(2 * count);
    var stretchX = control.PupilGridStretchX * (count - 2d) / count * 256 / 254;
    var stretchY = control.PupilGridStretchY * (count - 2d) / count * 256 / 254;
    var x = Enumerable.Range(0, count).Select(i => stretchX * (i - count / 2d) / (count / 2d - 1)).ToArray();
    var y = Enumerable.Range(0, count).Select(i => stretchY * (i - count / 2d) / (count / 2d - 1)).ToArray();
    var center = (display - 1) / 2;
    var timer = Stopwatch.StartNew();
    var result = new Complex[count, display];
    var traced = 0L;
    var valid = 0L;
    var ideal = 0d;
    var maximumStep = 0d;
    var edges = 0L;
    var traceTime = TimeSpan.Zero;
    var fftTime = TimeSpan.Zero;
    var previous = new RawRow(count);
    var current = new RawRow(count);
    double[]? previousPhase = null;
    var plan = new WindowPlan(size, -center, display, fft);
    var rowsPerBatch = Math.Max(1, 32768 / count);
    for (var first = 0; first < count; first += rowsPerBatch)
    {
        var length = Math.Min(rowsPerBatch, count - first);
        var rows = Enumerable.Range(0, length).Select(_ => new RawRow(count)).ToArray();
        var samples = new List<PupilSample>();
        var locations = new List<(int Row, int Column)>();
        for (var row = 0; row < length; row++)
            for (var column = 0; column < count; column++)
            {
                if (x[column] * x[column] + y[first + row] * y[first + row] > 1) continue;
                samples.Add(new PupilSample(x[column], y[first + row], 1));
                locations.Add((row, column));
            }
        if (samples.Count > 0)
        {
            var stepTimer = Stopwatch.StartNew();
            var wavefront = WavefrontEngine.GenerateChiefRayBatch(optic, (0, 0), wave, samples, false);
            traceTime += stepTimer.Elapsed;
            traced += samples.Count;
            for (var i = 0; i < wavefront.Samples.Count; ++i)
            {
                var ray = wavefront.Samples[i];
                if (ray.Intensity <= 0) continue;
                var (row, column) = locations[i];
                rows[row].Amplitude[column] = Math.Sqrt(ray.LaunchIntensity ?? ray.Intensity);
                rows[row].Phase[column] = ray.OpdWaves;
            }
        }
        var prepared = new List<(int Row, Complex[] Values)>();
        for (var i = 0; i < length; ++i)
        {
            var index = first + i - 1;
            if (index >= 0) Prepare(index, previous, current, rows[i], prepared);
            previous = current;
            current = rows[i];
        }
        if (first + length == count) Prepare(count - 1, previous, current, new RawRow(count), prepared);
        var fftTimer = Stopwatch.StartNew();
        Parallel.ForEach(prepared, new ParallelOptions { MaxDegreeOfParallelism = 4 }, row =>
        {
            var value = plan.Apply(row.Values);
            for (var i = 0; i < display; ++i) result[row.Row, i] = value[i];
        });
        fftTime += fftTimer.Elapsed;
        if (first > 0 && (first % 256 < rowsPerBatch || first + length == count))
            Console.WriteLine($"{count}: rows {first + length}/{count}, rays {traced}, phase {maximumStep:R}, {timer.Elapsed.TotalSeconds:F2}s");
    }
    var image = new double[display][];
    for (var row = 0; row < display; ++row) image[row] = new double[display];
    Parallel.For(0, display, new ParallelOptions { MaxDegreeOfParallelism = 4 }, column =>
    {
        var input = new Complex[count];
        for (var row = 0; row < count; ++row) input[row] = result[row, column];
        var output = plan.Apply(input);
        for (var row = 0; row < display; ++row) image[row][column] = Math.Pow(output[row].Magnitude / ideal, 2);
    });
    var maxControlError = count != 256 ? (double?)null : image.SelectMany((row, r) => row.Select((value, c) =>
        Math.Abs(value - control.Values[r, c] / (100 * control.UnaberratedPeakRatio)))).Max();
    Console.WriteLine($"{count} done: {traced} traced, {valid} transmitted, {edges} adjacent, max step {maximumStep:R}, max intensity {image.Max(row => row.Max()):R}, baseline error {maxControlError:R}; trace {traceTime.TotalSeconds:F2}s, row FFT {fftTime.TotalSeconds:F2}s, wall {timer.Elapsed.TotalSeconds:F2}s");
    File.WriteAllText(Path.Combine(args[1], $"psf-{count}.json"), JsonSerializer.Serialize(new
    {
        count, size, display, pitch, stretchX, stretchY, traced, valid, edges, maximumStep, ideal, maxControlError,
        traceSeconds = traceTime.TotalSeconds, rowFftSeconds = fftTime.TotalSeconds, wallSeconds = timer.Elapsed.TotalSeconds, image
    }, options));

    void Prepare(int row, RawRow above, RawRow raw, RawRow below, List<(int, Complex[])> prepared)
    {
        var filtered = new double[count];
        Array.Fill(filtered, double.NaN);
        var pupil = new Complex[count];
        var hasValues = false;
        for (var column = 0; column < count; ++column)
        {
            if (!(raw.Amplitude[column] > 0)) continue;
            hasValues = true;
            var a = raw.Amplitude[column];
            var p = raw.Phase[column];
            if (((row + column) & 1) == 0 && column > 0 && column + 1 < count &&
                raw.Amplitude[column - 1] > 0 && raw.Amplitude[column + 1] > 0 &&
                above.Amplitude[column] > 0 && below.Amplitude[column] > 0)
            {
                a = (raw.Amplitude[column - 1] + raw.Amplitude[column + 1] + above.Amplitude[column] + below.Amplitude[column]) / 4;
                if (column != count / 2 || row != count / 2)
                    p = (raw.Phase[column - 1] + raw.Phase[column + 1] + above.Phase[column] + below.Phase[column]) / 4;
            }
            filtered[column] = p;
            if (column > 0 && double.IsFinite(filtered[column - 1]))
            {
                maximumStep = Math.Max(maximumStep, Math.Abs(p - filtered[column - 1]));
                edges++;
            }
            if (previousPhase is not null && double.IsFinite(previousPhase[column]))
            {
                maximumStep = Math.Max(maximumStep, Math.Abs(p - previousPhase[column]));
                edges++;
            }
            pupil[column] = Complex.FromPolarCoordinates(a, -2 * Math.PI * p);
            ideal += a;
            valid++;
        }
        if (hasValues) prepared.Add((row, pupil));
        previousPhase = filtered;
    }
}

sealed class RawRow(int count)
{
    public double[] Phase { get; } = new double[count];
    public double[] Amplitude { get; } = new double[count];
}

sealed class WindowPlan
{
    private readonly int length;
    private readonly int radix;
    private readonly int[] bins;
    private readonly Complex[,] roots;
    private readonly Action<Complex[]> fft;

    public WindowPlan(int size, int first, int count, Action<Complex[]> fft)
    {
        length = size & -size;
        radix = size / length;
        this.fft = fft;
        bins = Enumerable.Range(first, count).Select(k => (k + size) % length).ToArray();
        roots = new Complex[count, radix];
        for (var i = 0; i < count; ++i)
            for (var digit = 0; digit < radix; ++digit)
                roots[i, digit] = Complex.FromPolarCoordinates(1, -2 * Math.PI * (first + i) * digit / size);
    }

    public Complex[] Apply(Complex[] values)
    {
        var output = new Complex[bins.Length];
        var buffer = new Complex[length];
        for (var digit = 0; digit < radix; digit++)
        {
            Array.Clear(buffer);
            for (var i = digit; i < values.Length; i += radix) buffer[i / radix] = values[i];
            fft(buffer);
            for (var k = 0; k < bins.Length; ++k) output[k] += buffer[bins[k]] * roots[k, digit];
        }
        return output;
    }
}

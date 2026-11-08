using System.Numerics;
using System.IO.Compression;
using System.Text.Json;
using System.Security.Cryptography;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Serialization;

if (args[0] == "--nodes")
{
    var definitions = new[] { (Panels: 8191, Order: 12), (Panels: 16384, Order: 8) };
    using var writer = File.CreateText(args[1]);
    foreach (var (panels, order) in definitions)
        foreach (var (sample, weight) in RadialNodes(panels, order))
            writer.WriteLine(FormattableString.Invariant($"{sample.X:R},{weight:R}"));
    return;
}

const int imageSize = 32;
const double pitch = .00025;
var snapshotBytes = File.ReadAllBytes(args[0]);
if (!Convert.ToHexString(SHA256.HashData(snapshotBytes)).Equals(
    "91293CB8F215C3C32073B0280EC3D640BCA5926AB3E1C36340DA05EDA9059823", StringComparison.Ordinal))
    throw new InvalidDataException("Axicon snapshot identity differs");
var snapshot = JsonSerializer.Deserialize<OpticSnapshot>(snapshotBytes, new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
    Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
})!;
var optic = Optic.FromSnapshot(snapshot);
var wave = optic.Wavelengths.First();
var frame = DiffractionEngine.CreateHuygensImageFrame(optic, (0, 0), wave, optic.RayAimingEnabled);
var k = 2 * Math.PI / (.001 * wave.Micrometers);
var normal = Cross(frame.TangentX, frame.TangentY);
var q = Enumerable.Range(-imageSize / 2, imageSize).SelectMany(y => Enumerable.Range(-imageSize / 2, imageSize)
    .Select(x => x * x + y * y)).Distinct().Order().ToArray();
var radii = q.Select(value => pitch * Math.Sqrt(value)).ToArray();
var lookup = q.Select((value, index) => (value, index)).ToDictionary(pair => pair.value, pair => pair.index);
var focus = new[] { -.01, -.005, 0, .005, .01 };
if (args[1] == "--native")
{
    using var file = File.OpenRead(args[2]);
    using var compressed = new GZipStream(file, CompressionMode.Decompress);
    using var reader = new BinaryReader(compressed);
    var count = reader.ReadInt32();
    var orders = new[] { 8191 * 12, 16384 * 8 };
    if (count != orders.Sum()) throw new InvalidDataException("Unexpected native node count");
    var native = new List<object>();
    var angular = Enumerable.Range(0, 24).Select(j => Math.Cos(Math.PI * (j + .5) / 24)).ToArray();
    foreach (var nodeCount in orders)
    {
        var samples = new (double Radius, double Weight, double Path, double Frequency, double Focus, double Hit)[nodeCount];
        for (var i = 0; i < nodeCount; ++i)
            samples[i] = (reader.ReadDouble(), reader.ReadDouble(), reader.ReadDouble(), reader.ReadDouble(), reader.ReadDouble(), reader.ReadDouble());
        var reference = samples.Sum(p => p.Weight);
        var fields = new Complex[focus.Length, radii.Length];
        Parallel.For(0, radii.Length, j =>
        {
            var sums = new Complex[focus.Length];
            var corrections = new Complex[focus.Length];
            foreach (var ray in samples)
            {
                var argument = k * ray.Frequency * radii[j];
                var bessel = 0d;
                foreach (var cosine in angular) bessel += Math.Cos(cosine * argument);
                for (var z = 0; z < focus.Length; ++z)
                {
                    var phase = k * (ray.Path - samples[0].Path + focus[z] * ray.Focus);
                    var value = Complex.FromPolarCoordinates(ray.Weight * bessel / angular.Length, phase) - corrections[z];
                    var next = sums[z] + value;
                    corrections[z] = (next - sums[z]) - value;
                    sums[z] = next;
                }
            }
            for (var z = 0; z < focus.Length; ++z) fields[z, j] = sums[z] / reference;
        });
        var results = Enumerable.Range(0, focus.Length).Select(i =>
        {
            var plane = new double[imageSize, imageSize];
            for (var row = 0; row < imageSize; ++row)
                for (var column = 0; column < imageSize; ++column)
                {
                    var x = column - imageSize / 2; var y = row - imageSize / 2;
                    plane[row, column] = Math.Pow(fields[i, lookup[x * x + y * y]].Magnitude, 2);
                }
            var mtf = (Tangential: IndependentModulation(plane, 50, pitch, false),
                Sagittal: IndependentModulation(plane, 50, pitch, true));
            return new
            {
                focus = focus[i],
                line = Enumerable.Range(0, imageSize).Select(j => plane[imageSize / 2, j]).ToArray(),
                center = plane[imageSize / 2, imageSize / 2],
                peak = plane.Cast<double>().Max(),
                mtfT = mtf.Tangential,
                mtfS = mtf.Sagittal
            };
        }).ToArray();
        Console.WriteLine($"Native {nodeCount}: center {results[2].center:R}, peak {results[2].peak:R}, MTF {string.Join(' ', results.Select(v => v.mtfT.ToString("R")))}");
        native.Add(new { nodeCount, reference, minimumHit = samples.Min(r => r.Hit), maximumHit = samples.Max(r => r.Hit), results });
    }
    File.WriteAllText("native-results.json", JsonSerializer.Serialize(native, new JsonSerializerOptions { WriteIndented = true }));
    return;
}
var output = new List<object>();
foreach (var trial in args.Skip(1))
{
    var parts = trial.Split('x');
    var panels = int.Parse(parts[0]); var order = int.Parse(parts[1]);

    var field = new Complex[focus.Length, radii.Length];
    var compensation = new Complex[focus.Length, radii.Length];
    var maximumBesselArgument = 0d; var maxPhaseStep = 0d; var previousPhase = double.NaN; var firstPhase = double.NaN;
    var reference = 0d; var accepted = 0; var timer = System.Diagnostics.Stopwatch.StartNew();
    var nodes = RadialNodes(panels, order);
    foreach (var batch in nodes.Chunk(16384))
    {
        var wf = WavefrontEngine.GenerateChiefRayBatch(optic, (0, 0), wave,
            batch.Select(p => p.Sample).ToArray(), optic.RayAimingEnabled);
        for (var node = 0; node < wf.Samples.Count; node++)
        {
            var sample = wf.Samples[node];
            if (!(sample.Intensity > 0) || sample.ImageDirection is not { Length: > 0 } direction) continue;
            accepted++;
            var amplitude = Math.Sqrt(sample.LaunchIntensity ?? sample.Intensity) * batch[node].Weight;
            reference += amplitude;
            var pupil = new Vector3D(sample.PupilX, sample.PupilY, sample.PupilZ);
            var phase = -2 * Math.PI * sample.OpdWaves + k * wf.ImageRefractiveIndex * (Dot(frame.Center - pupil, direction) - wf.Radius);
            if (double.IsNaN(firstPhase)) firstPhase = phase;
            if (double.IsFinite(previousPhase)) maxPhaseStep = Math.Max(maxPhaseStep, Math.Abs(previousPhase - phase));
            previousPhase = phase;
            var omega = k * wf.ImageRefractiveIndex * Math.Sqrt(Math.Pow(Dot(frame.TangentX, direction), 2) + Math.Pow(Dot(frame.TangentY, direction), 2));
            var delta = k * wf.ImageRefractiveIndex * Dot(normal, direction);
            maximumBesselArgument = Math.Max(maximumBesselArgument, omega * radii[^1]);
            var shifts = focus.Select(z => Complex.FromPolarCoordinates(amplitude, phase + z * delta)).ToArray();
            for (var j = 0; j < radii.Length; ++j)
            {
                var bessel = J0(omega * radii[j]);
                for (var i = 0; i < focus.Length; ++i)
                {
                    var add = shifts[i] * bessel - compensation[i, j];
                    var next = field[i, j] + add;
                    compensation[i, j] = (next - field[i, j]) - add;
                    field[i, j] = next;
                }
            }
        }
    }
    if (accepted != panels * order) throw new InvalidDataException("A Core pupil ray failed or was clipped");
    var results = Enumerable.Range(0, focus.Length).Select(i =>
    {
        var plane = new double[imageSize, imageSize];
        for (var row = 0; row < imageSize; ++row)
            for (var column = 0; column < imageSize; ++column)
            {
                var x = column - imageSize / 2; var y = row - imageSize / 2;
                plane[row, column] = 100 * Math.Pow(field[i, lookup[x * x + y * y]].Magnitude / reference, 2);
            }
        var mtf = MtfMethodEvaluator.SampleHuygensMtf(DiffractionEngine.ComputePsfMtf(new PsfResult(
            plane, panels * order, imageSize, 0, pitch * 1000)), imageSize, 50, true);
        return new
        {
            focus = focus[i],
            line = Enumerable.Range(0, imageSize).Select(j => plane[imageSize / 2, j] / 100).ToArray(),
            center = plane[imageSize / 2, imageSize / 2] / 100,
            peak = plane.Cast<double>().Max() / 100,
            mtfT = mtf.Tangential,
            mtfS = mtf.Sagittal
        };
    }).ToArray();
    Console.WriteLine($"{trial}: accepted {accepted}, r-integral {reference:R}, phase turns {(previousPhase - firstPhase) / (2 * Math.PI):F3}, max node step {maxPhaseStep / (2 * Math.PI):F3}, J0 max arg {maximumBesselArgument:R}, elapsed {timer.Elapsed.TotalSeconds:F2}");
    Console.WriteLine($"center {results[2].center:R}, peak {results[2].peak:R}, MTF {string.Join(' ', results.Select(v => v.mtfT.ToString("R")))}");
    output.Add(new { panels, order, accepted, reference, firstPhase, lastPhase = previousPhase, maxPhaseStep, maximumBesselArgument, results });
}
File.WriteAllText("probe-results.json", JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
static IEnumerable<(PupilSample Sample, double Weight)> RadialNodes(int panels, int order)
{
    var unit = ApertureSampler.GenerateHuygensGaussianQuadrature(order, 4, 0).Where((_, i) => i % 4 == 0)
        .Select(p => (X: p.X * p.X, W: 4 * p.Weight)).ToArray();
    for (var panel = 0; panel < panels; ++panel)
        foreach (var node in unit)
        {
            var r = (panel + node.X) / panels;
            yield return (new PupilSample(r, 0, 1), 2 * r * node.W / panels);
        }
}
static double IndependentModulation(double[,] plane, double frequency, double pitch, bool alongX)
{
    var n = plane.GetLength(0);
    var projection = new double[n];
    for (var i = 0; i < n; ++i)
        for (var j = 0; j < n; ++j)
            projection[i] += alongX ? plane[j, i] : plane[i, j];
    var values = Enumerable.Range(0, n / 2).Select(bin =>
        projection.Select((power, i) => Complex.FromPolarCoordinates(power, -2 * Math.PI * bin * i / n))
            .Aggregate(Complex.Zero, (a, b) => a + b).Magnitude / projection.Sum()).ToArray();
    // Natural spline with unit-spaced samples. OpticStudio defines display
    // frequencies using the physical span between first and last pixel.
    var a = new double[values.Length];
    var b = new double[values.Length];
    var d = new double[values.Length];
    for (var i = 1; i < values.Length - 1; ++i)
    {
        var divisor = 4 - a[i - 1];
        a[i] = 1 / divisor;
        b[i] = (6 * (values[i + 1] - 2 * values[i] + values[i - 1]) - b[i - 1]) / divisor;
    }
    for (var i = values.Length - 2; i > 0; --i)
        d[i] = b[i] - a[i] * d[i + 1];
    var position = frequency * pitch * (n - 1);
    var low = (int)Math.Floor(position);
    var t = position - low;
    var s = 1 - t;
    return Math.Clamp(s * values[low] + t * values[low + 1]
        + ((s * s * s - s) * d[low] + (t * t * t - t) * d[low + 1]) / 6, 0, 1);
}

static double J0(double x)
{
    var sum = 1d; var term = 1d; var f = -x * x / 4;
    for (var k = 1; k < 48; ++k)
    {
        term *= f / (k * k);
        sum += term;
        if (Math.Abs(term) <= 1e-17) break;
    }
    return sum;
}
static double Dot(Vector3D a, Vector3D b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
static Vector3D Cross(Vector3D a, Vector3D b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);




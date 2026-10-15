using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Globalization;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Apodization;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.FileIO;

if (args.Length != 3) throw new ArgumentException("Usage: <repository> <native-directory> <fresh-output>");
var root = Path.GetFullPath(args[0]);
var nativeRoot = Path.GetFullPath(args[1]);
var output = Path.GetFullPath(args[2]);
if (Directory.Exists(output)) throw new IOException("Use a fresh output directory.");
Directory.CreateDirectory(output);
var source = Path.Combine(nativeRoot, "source.ZMX");
string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
if (Hash(source) != "8bcc937c2c2e02ba175f38875fd0def40db547f7eedab509cbfd1fed4353e0e8") throw new InvalidDataException("Source identity differs.");
var original = OpticalFormatCatalog.Import(File.ReadAllText(source), ".zmx");
original.RayAimingEnabled = true;
var records = new List<object>();
var candidates = new List<object>();
foreach (var folder in Directory.EnumerateDirectories(nativeRoot).Where(p => Path.GetFileName(p).EndsWith("-linear")).Order())
{
    var name = Path.GetFileName(folder);
    using var request = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "request.json")));
    var number = request.RootElement.GetProperty("field").GetInt32();
    var pupilCount = request.RootElement.GetProperty("pupilSampling").GetInt32();
    var optic = Optic.FromSnapshot(original.ToSnapshot());
    if (request.RootElement.TryGetProperty("diagnosticApodizationFactor", out var apodization))
        optic.Apodization = new ZemaxApodization(ZemaxApodizationType.Gaussian, apodization.GetDouble());
    using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "Auto/captured-settings.json")));
    var properties = settings.RootElement.GetProperty("properties");
    var pitch = properties.GetProperty("ImageDelta").GetDouble() / 1000;
    var pol = properties.GetProperty("UsePolarization").GetBoolean();
    var waveNumber = request.RootElement.GetProperty("wavelength").GetInt32();
    var parameters = new Dictionary<string,string> {
        ["PupilSampling"] = pupilCount.ToString(CultureInfo.InvariantCulture), ["ImageSampling"] = "32",
        ["ImageDeltaMicrometers"] = (pitch * 1000).ToString("R", CultureInfo.InvariantCulture),
        ["WavelengthNumber"] = waveNumber.ToString(CultureInfo.InvariantCulture), ["FieldNumber"] = number.ToString(CultureInfo.InvariantCulture),
        ["UsePolarization"] = pol.ToString(), ["UseCentroid"] = "false", ["Normalized"] = "false", ["Type"] = "线性" };
    var data = new WorkbenchRuntime(optic).BuildAnalysisData("Huygens PSF", parameters);
    data = JsonSerializer.Deserialize<AnalysisData>(JsonSerializer.Serialize(data))!;
    var actual = data.PlotSeries.Single().Points.Select(p => p.Value!.Value).ToArray();
    File.WriteAllText(Path.Combine(output, name + "-workbench.json"), JsonSerializer.Serialize(data));
    foreach (var method in new[] { "Auto", "Planar", "Spherical" })
    {
        var target = Grid(Path.Combine(folder, method, "data.json"));
        var metric = Metric(actual, target);
        Console.WriteLine($"{name}/{method}: relativeL2={metric.RelativeL2:R}, max={metric.Maximum:R}");
        records.Add(new { name, method, pitch, pol, metric, nativeSha256 = Hash(Path.Combine(folder, method, "data.json")) });
    }
    if (waveNumber == 0) continue;
    var field = (Hx: 0d, Hy: number == 1 ? 0d : -1d);
    var pupilRadius = pupilCount / 2d - 1;
    var pupils = Enumerable.Range(0, pupilCount).SelectMany(y => Enumerable.Range(0, pupilCount)
        .Select(x => ((x - pupilCount / 2d) / pupilRadius, (y - pupilCount / 2d) / pupilRadius)))
        .Where(p => p.Item1 * p.Item1 + p.Item2 * p.Item2 <= Math.Pow(1 - .5 / pupilRadius, 2)).ToArray();
    var wave = optic.Wavelengths[waveNumber - 1];
    if (pol)
    {
        try
        {
            WavefrontEngine.GenerateChiefRaySamples(optic, field, wave, pupils, aimAtStop: true, usePolarization: true);
            candidates.Add(new { name, formalPowerTransport = "Supported", semantics = "A supported power trace alone does not certify scalar PSF weighting." });
        }
        catch (NotSupportedException error)
        {
            candidates.Add(new { name, formalPowerTransport = "Unsupported", error = error.ToString(),
                semantics = "Recorded existing full-transport limitation; the separate Jones hypothesis remains approximate and nonpassing." });
        }
    }
    var wf = WavefrontEngine.GenerateChiefRaySamples(optic, field, wave, pupils, aimAtStop: true);
    var normalizer = WavefrontEngine.GenerateChiefRaySamples(optic, (0, 0), wave, pupils, aimAtStop: true);
    var frame = DiffractionEngine.CreateHuygensImageFrame(optic, field, wave, true);
    var pupilDistance = Math.Abs(optic.Paraxial.EstimateExitPupilLocation());
    var pupilDiameter = Math.Abs(optic.Paraxial.EstimateExitPupilDiameter());
    var halfExtent = .61 * wave.Micrometers / 1000 * pupilDistance * (pupilCount - 2) / (wf.ImageRefractiveIndex * pupilDiameter);
    var k = 2 * Math.PI / (wave.Micrometers / 1000);
    var jones = pol ? JonesPupilEngine.Generate(optic, field, wave, pupilCount, aimAtStop: true, zemaxCentered: true) : null;
    var normalizerJones = pol ? JonesPupilEngine.Generate(optic, (0, 0), wave, pupilCount, aimAtStop: true, zemaxCentered: true) : null;
    double Power(JonesPupilSample j) => j.IsValid ? (j.Jxx.Magnitude*j.Jxx.Magnitude + j.Jxy.Magnitude*j.Jxy.Magnitude
        + j.Jyx.Magnitude*j.Jyx.Magnitude + j.Jyy.Magnitude*j.Jyy.Magnitude)/2 : 0;
    var jonesByPupil = jones?.Samples.ToDictionary(j => (Math.Round(j.Px, 12), Math.Round(j.Py,12)), Power);
    var normalizerByPupil = normalizerJones?.Samples.ToDictionary(j => (Math.Round(j.Px, 12), Math.Round(j.Py,12)), Power);
    var amplitude = wf.Samples.Select(s => s.Intensity > 0 ? Math.Sqrt(pol ? s.Intensity : s.LaunchIntensity ?? s.Intensity)
        * Math.Sqrt(jonesByPupil?.GetValueOrDefault((Math.Round(s.NormalizedPupilX,12),Math.Round(s.NormalizedPupilY,12))) ?? 1) : 0).ToArray();
    var ideal = normalizer.Samples.Sum(s => s.Intensity > 0 ? Math.Sqrt(pol ? s.Intensity : s.LaunchIntensity ?? s.Intensity)
        * Math.Sqrt(normalizerByPupil?.GetValueOrDefault((Math.Round(s.NormalizedPupilX,12),Math.Round(s.NormalizedPupilY,12))) ?? 1) : 0);
    var imageAxis = new Vector3D(frame.TangentX.Y*frame.TangentY.Z-frame.TangentX.Z*frame.TangentY.Y,
        frame.TangentX.Z*frame.TangentY.X-frame.TangentX.X*frame.TangentY.Z,
        frame.TangentX.X*frame.TangentY.Y-frame.TangentX.Y*frame.TangentY.X);
    var chiefAxis = optic.TraceGenericFinalSample(field.Hx, field.Hy, 0, 0, wave.Micrometers, true)!.Direction;
    Console.WriteLine($"geometry {name}: radius={wf.Radius:R}, pupilDistance={pupilDistance:R}, halfExtent={halfExtent:R}");
    foreach (var spherical in new[] { false, true })
    foreach (var origin in new[] { "sphere", "exit-plane", "chief-plane" })
    foreach (var weight in new[] { "unit", "distance", "normal" })
    {
        if (!spherical && origin != "sphere") continue;
        if (!spherical && weight != "unit") continue;
        var values = new Complex[32 * 32];
        for (var y = 0; y < 32; y++)
        for (var x = 0; x < 32; x++)
        {
            var dx = (x - 16) * pitch; var dy = (y - 16) * pitch;
            var point = frame.Center + frame.TangentX * dx + frame.TangentY * dy;
            var sum = Complex.Zero;
            for (var i = 0; i < wf.Samples.Count; i++)
            {
                var s = wf.Samples[i];
                if (amplitude[i] <= 0 || s.ImageDirection is not { } direction) continue;
                var pupil = new Vector3D(s.PupilX, s.PupilY, s.PupilZ);
                var pathShift = 0d;
                if (origin != "sphere")
                {
                    var axis = origin == "chief-plane" ? chiefAxis : imageAxis;
                    var plane = frame.Center - axis * pupilDistance;
                    pathShift = Dot(pupil - plane, axis) / Dot(direction, axis);
                    pupil -= direction * pathShift;
                }
                var delta = point - pupil;
                var distance = delta.Length;
                var path = -s.OpdWaves * wave.Micrometers / 1000
                    + wf.ImageRefractiveIndex * ((spherical ? distance : Dot(delta, direction)) - wf.Radius - pathShift);
                var a = amplitude[i] * (weight == "unit" ? 1 : pupilDistance / distance);
                if (weight == "normal") a *= .5 * (1 + Dot(delta / distance, direction));
                sum += Complex.FromPolarCoordinates(a, -k * path);
            }
            var footprint = Math.Abs(dx) >= halfExtent || Math.Abs(dy) >= halfExtent ? 0
                : Sinc(Math.PI * dx / halfExtent) * Sinc(Math.PI * dy / halfExtent);
            values[y * 32 + x] = sum / ideal * footprint;
        }
        var targetMethod = spherical ? "Spherical" : "Planar";
        var target = Grid(Path.Combine(folder, targetMethod, "data.json"));
        var metric = Metric(values.Select(c => c.Magnitude * c.Magnitude).ToArray(), target);
        double? complexL2 = null;
        if (CompleteGrid(Path.Combine(nativeRoot, name.Replace("-linear", "-real"), targetMethod, "data.json"))
            && CompleteGrid(Path.Combine(nativeRoot, name.Replace("-linear", "-imaginary"), targetMethod, "data.json")))
        {
            var rv = Grid(Path.Combine(nativeRoot, name.Replace("-linear", "-real"), targetMethod, "data.json"));
            var iv = Grid(Path.Combine(nativeRoot, name.Replace("-linear", "-imaginary"), targetMethod, "data.json"));
            var cv = rv.Zip(iv, (r, i) => new Complex(r, i)).ToArray();
            var rotation = Complex.FromPolarCoordinates(1, (cv[528] / values[528]).Phase);
            complexL2 = Math.Sqrt(values.Zip(cv, (a,b) => Complex.Abs(a * rotation - b) * Complex.Abs(a * rotation - b)).Sum()
                / cv.Sum(c => c.Magnitude * c.Magnitude));
        }
        Console.WriteLine($"candidate {name}/{targetMethod}/{origin}/{weight}: relativeL2={metric.RelativeL2:R}, max={metric.Maximum:R}, complex={complexL2:R}");
        candidates.Add(new { name, targetMethod, origin, weight, pupils = pupils.Length, wf.Radius, pupilDistance, halfExtent, metric, complexL2,
            complexSemantics = complexL2 is null ? "Nonfinite wide-window complex grids excluded; intensity remains independently comparable." : "Global phase only removed." });
    }
}
var options = new JsonSerializerOptions { WriteIndented = true };
File.WriteAllText(Path.Combine(output, "comparison.json"), JsonSerializer.Serialize(records, options));
File.WriteAllText(Path.Combine(output, "hypotheses.json"), JsonSerializer.Serialize(candidates, options));
File.WriteAllText(Path.Combine(output, "provenance.json"), JsonSerializer.Serialize(new {
    sourceSha256 = Hash(source), coreSha256 = Hash(typeof(DiffractionEngine).Assembly.Location),
    toolSha256 = Hash(Assembly.GetExecutingAssembly().Location), nativeRoot,
    semantics = "Formal product comparison and offline propagation hypotheses; no fitted native values enter product computations."
}, options));

double[] Grid(string path)
{
    using var json = JsonDocument.Parse(File.ReadAllText(path));
    var grid = json.RootElement.GetProperty("dataGrids")[0];
    var values = grid.GetProperty("values").EnumerateArray().SelectMany(row => row.EnumerateArray()).ToArray();
    if (values.Length != 1024 || values.Any(v => v.ValueKind != JsonValueKind.Number)) throw new InvalidDataException("Incomplete/nonfinite native grid: " + path);
    return values.Select(v => v.GetDouble()).ToArray();
}
bool CompleteGrid(string path)
{
    if (!File.Exists(path)) return false;
    using var json = JsonDocument.Parse(File.ReadAllText(path));
    var values = json.RootElement.GetProperty("dataGrids")[0].GetProperty("values").EnumerateArray().SelectMany(row => row.EnumerateArray()).ToArray();
    return values.Length == 1024 && values.All(v => v.ValueKind == JsonValueKind.Number);
}
ErrorMetric Metric(double[] actual, double[] expected)
    => new(Math.Sqrt(actual.Zip(expected, (a,b) => (a-b)*(a-b)).Sum() / expected.Sum(v => v*v)), actual.Zip(expected, (a,b) => Math.Abs(a-b)).Max());
double Sinc(double x) => Math.Abs(x) < 1e-8 ? 1 - x*x/6 : Math.Sin(x)/x;
double Dot(Vector3D a, Vector3D b) => a.X*b.X + a.Y*b.Y + a.Z*b.Z;
readonly record struct ErrorMetric(double RelativeL2, double Maximum);

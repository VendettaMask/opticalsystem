using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.ZemaxComparison;

internal static class PsfProbe
{
    internal static void Run(string repository, string root)
    {
        var optic = Optic.FromSnapshot(JsonFiles.Read<OpticSnapshot>(Path.Combine(repository,
            "artifacts/validation/afocal-result-audit-20261031/inputs/models/001-afocal-riflescope-c1.json")));
        var w = optic.Wavelengths[1]; const int n = 32; var field = (0d, .7d);
        var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        MethodInfo M(Type type, string name) => type.GetMethod(name, flags)!;
        var nodes = M(typeof(DiffractionEngine), "HuygensPupilSamples").Invoke(null, [n]);
        var wf = (WavefrontResult)M(typeof(WavefrontEngine), "GenerateScalarAfocalHuygensSamples")
            .Invoke(null, [optic, field, w, nodes, true, w])!;
        wf = wf with { Samples = wf.Samples.Select(s => s.Intensity > 0 && s.LaunchIntensity is { } p ? s with { Intensity = p } : s).ToArray() };
        var center = ((double X, double Y))M(typeof(DiffractionEngine), "HuygensGeometricCenterOffset")
            .Invoke(null, [optic, field, new[] { w }, w, n, true, AfocalHuygensIntegration.NativeFiniteCell])!;
        var image = optic.SurfaceGroup.Items[^1].CoordinateSystem;
        var chief = optic.TraceGenericFinalSample(0, .7, 0, 0, w.Micrometers, true)!;
        var direction = image.ToLocalDirection(chief.Direction);
        var chiefY = Math.Atan2(direction.Y, direction.Z) * 1000;
        var pitch = DiffractionEngine.DefaultHuygensImageDeltaMillimeters(optic, field, w, n);
        var kernel = M(typeof(DiffractionEngine), "HuygensFarFieldSummation");
        var rows = new List<object>();
        foreach (var centered in new[] { false, true })
        {
            var folder = Path.Combine(root, "native-huygens-split", centered ? "psf-field2-centroid" : "psf-field2-chief");
            var grid = JsonFiles.Read<JsonElement>(Path.Combine(folder, "data.json")).GetProperty("dataGrids")[0];
            var expected = grid.GetProperty("values").EnumerateArray().SelectMany(r => r.EnumerateArray().Select(v => v.GetDouble())).ToArray();
            var nativeSum = expected.Sum(); expected = expected.Select(v => v / nativeSum).ToArray();
            var text = File.ReadAllText(Path.Combine(folder, "data.txt"));
            var match = Regex.Match(text, @"中心坐标:\s*[^,]+,\s*([-+0-9.Ee]+)");
            var nativeCenterY = double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * 1000;
            var selected = centered ? center : (X: 0d, Y: 0d);
            foreach (var mode in new[] { "Original", "CenteredFootprint", "NativeCenter", "NativeCenterAndFootprint" })
            {
                var offset = mode.StartsWith("Native", StringComparison.Ordinal) ? (X: 0d, Y: nativeCenterY - chiefY) : selected;
                var prepared = wf;
                if (mode.Contains("Footprint", StringComparison.Ordinal))
                {
                    var origin = wf.AfocalReferencePlaneOrigin!.Value;
                    prepared = wf with { Samples = wf.Samples.Select(s =>
                    {
                        var local = image.ToLocalDirection(new Vector3D(s.PupilX, s.PupilY, s.PupilZ) - origin);
                        return s with { OpdWaves = s.OpdWaves + wf.ImageRefractiveIndex * (local.X * offset.X + local.Y * offset.Y) / w.Micrometers };
                    }).ToArray() };
                    offset = (0, 0);
                }
                var values = (double[,])kernel.Invoke(null, [prepared, w, n, pitch, image, n, false, null, 0d, offset, true])!;
                var actual = values.Cast<double>().ToArray(); var total = actual.Sum(); actual = actual.Select(v => v / total).ToArray();
                var errors = actual.Zip(expected, (a, b) => Math.Abs(a - b)).ToArray();
                var nrmse = Math.Sqrt(errors.Sum(e => e * e) / errors.Length) / expected.Max();
                rows.Add(new { centered, mode, nativeCenterY, coreChiefY = chiefY, coreCenterOffset = new { center.X, center.Y },
                    nrmse, maximumNormalizedAbsolute = errors.Max() / expected.Max() });
                Console.WriteLine($"Centered={centered}/{mode}: NRMSE={nrmse:R}; max={errors.Max() / expected.Max():R}; coreY={chiefY + center.Y:R}; nativeY={nativeCenterY:R}");
            }
        }
        JsonFiles.Write(Path.Combine(root, "psf-centering-probe.json"), new { productCandidate = false, pitch, rows });
    }
}

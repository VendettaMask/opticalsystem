using System.Reflection;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Coordinates;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Metrics;

// Candidate coordinate transforms on actual shared-Core wavefronts. No product evaluator is replaced.
internal static class ProjectionProbe
{
    internal static void Run(string repository, string root)
    {
        var optic = Optic.FromSnapshot(JsonFiles.Read<OpticSnapshot>(Path.Combine(repository,
            "artifacts/validation/afocal-result-audit-20261031/inputs/models/001-afocal-riflescope-c1.json")));
        var native = JsonFiles.Read<System.Text.Json.JsonElement>(Path.Combine(repository,
            "artifacts/validation/afocal-mtf-field-controls-20261031/native/rifle-huygens-mono/data.json")).GetProperty("dataSeries");
        var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        MethodInfo M(Type t, string name) => t.GetMethod(name, flags)!;
        var n = 32; var w = optic.Wavelengths[1];
        var pupils = M(typeof(DiffractionEngine), "HuygensPupilSamples").Invoke(null, [n])!;
        var generate = M(typeof(WavefrontEngine), "GenerateScalarAfocalHuygensSamples");
        var sum = M(typeof(DiffractionEngine), "HuygensFarFieldSummation");
        var evaluator = typeof(Optic).Assembly.GetType("OpticalSystemDesign.Core.Analysis.MtfMethodEvaluator")!;
        var sampleMtf = M(evaluator, "SampleHuygensMtf");
        var spline = M(typeof(MtfThroughFocusAnalysis), "CubicSplineInterpolate");
        var pitch = DiffractionEngine.DefaultHuygensImageDeltaMillimeters(optic, (0, 1), w, n);
        double[] frequencies = [.1, .2, .4, .8, 1.6, 3.2];
        var all = new Dictionary<string, double[][]>();
        foreach (var mode in new[] { "ImageXY", "ImageXYCenteredFootprint", "ChiefNormalCenteredFootprint", "DirectionAngleJacobianCenteredFootprint", "DirectionCosineJacobianCenteredFootprint" }) all[mode] = Enumerable.Range(0, 12).Select(_ => new double[10]).ToArray();
        var fields = Enumerable.Range(0, 10).Select(i => i / 9d).ToArray(); var geometry = new List<object>();
        static double Dot(Vector3D a, Vector3D b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        static Vector3D Unit(Vector3D a) => a * (1 / a.Length);
        foreach (var i in Enumerable.Range(0, 10))
        {
            var field = (0d, fields[i]);
            var wavefront = (WavefrontResult)generate.Invoke(null, [optic, field, w, pupils, true, w])!;
            wavefront = wavefront with { Samples = wavefront.Samples.Select(s => s.Intensity > 0 && s.LaunchIntensity is { } p ? s with { Intensity = p } : s).ToArray() };
            var origin = wavefront.AfocalReferencePlaneOrigin!.Value;
            var image = optic.SurfaceGroup.Items[^1].CoordinateSystem;
            var chief = optic.TraceGenericFinalSample(0, fields[i], 0, 0, w.Micrometers, true)!;
            var d = Unit(image.ToLocalDirection(chief.Direction));
            var center = ((double X, double Y))M(typeof(DiffractionEngine), "HuygensGeometricCenterOffset")
                .Invoke(null, [optic, field, new[] { w }, w, n, true, AfocalHuygensIntegration.NativeFiniteCell])!;
            geometry.Add(new { field = fields[i], direction = d, center = new { x = center.X, y = center.Y } });
            foreach (var mode in all.Keys)
            {
                var bx = new Vector3D(1, 0, 0); var by = new Vector3D(0, 1, 0);
                if (mode.StartsWith("ChiefNormal", StringComparison.Ordinal))
                {
                    bx = Unit(bx - d * d.X);
                    by = new Vector3D(d.Y * bx.Z - d.Z * bx.Y, d.Z * bx.X - d.X * bx.Z, d.X * bx.Y - d.Y * bx.X);
                }
                if (mode.StartsWith("DirectionAngleJacobian", StringComparison.Ordinal))
                {
                    var ax = Math.Atan2(d.X, d.Z); var ay = Math.Atan2(d.Y, d.Z);
                    bx = (bx - d * d.X) * (Math.Abs(d.Z) / Math.Pow(Math.Cos(ax), 2));
                    by = (by - d * d.Y) * (Math.Abs(d.Z) / Math.Pow(Math.Cos(ay), 2));
                }
                if (mode.StartsWith("DirectionCosineJacobian", StringComparison.Ordinal))
                {
                    bx = new Vector3D(1, 0, -d.X / d.Z);
                    by = new Vector3D(0, 1, -d.Y / d.Z);
                }
                bx = image.ToGlobalDirection(bx); by = image.ToGlobalDirection(by);
                var projected = wavefront with { Samples = wavefront.Samples.Select(s =>
                {
                    var offset = new Vector3D(s.PupilX, s.PupilY, s.PupilZ) - origin;
                    return s with { PupilX = origin.X + Dot(offset, bx), PupilY = origin.Y + Dot(offset, by), PupilZ = origin.Z,
                        OpdWaves = mode == "ImageXYPositiveOpd" ? -s.OpdWaves : s.OpdWaves
                            + (mode.EndsWith("CenteredFootprint", StringComparison.Ordinal) ? wavefront.ImageRefractiveIndex
                                * (Dot(offset, bx) * center.X + Dot(offset, by) * center.Y) / w.Micrometers : 0) };
                }).ToArray() };
                var selectedCenter = mode.EndsWith("CenteredFootprint", StringComparison.Ordinal) ? (0d, 0d) : center;
                var footprintSampling = mode == "ImageXYNoFootprint" ? 1000000 : n;
                var values = (double[,])sum.Invoke(null, [projected, w, n, pitch, CoordinateSystem.Global, footprintSampling, false, null, 0d, selectedCenter, true])!;
                var psf = new PsfResult(values, n, n, 1, pitch, SampleSpacingUnit: AnalysisAxisUnit.Milliradian);
                var mtf = DiffractionEngine.ComputePsfMtf(psf, true);
                for (var f = 0; f < 6; f++)
                {
                    var value = ((double T, double S))sampleMtf.Invoke(null, [mtf, 2 * n, frequencies[f], true])!;
                    all[mode][2 * f][i] = value.T; all[mode][2 * f + 1][i] = value.S;
                }
            }
        }
        var gate = ComparisonConfiguration.Load(Path.Combine(repository, "tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json"))
            .Analyses["Huygens MTF vs Field"].Quantities["Modulation"];
        var rows = new List<object>();
        foreach (var mode in all.Keys) for (var f = 0; f < 6; f++) for (var dir = 0; dir < 2; dir++)
        {
            var x = native[f].GetProperty("x").EnumerateArray().Select(v => v.GetDouble()).ToArray();
            var y = native[f].GetProperty("y").EnumerateArray().Select(v => v[dir].GetDouble()).ToArray();
            var display = (double[])spline.Invoke(null, [fields, all[mode][2 * f + dir], x])!;
            var reference = new Series1DResult("curve", "Native", x, y, new("NormalizedField", "Dimensionless"), new("Modulation", "Dimensionless"));
            var metric = ComparisonMetrics.Calculate("ProjectionProbe", "Dimensionless", ComparisonMetrics.Align(reference with { Y = display }, reference, []), gate);
            rows.Add(new { mode, frequency = frequencies[f], direction = dir, metric });
            Console.WriteLine($"{mode}/{frequencies[f]}/{dir}: {metric.Conclusion}; {metric.Nrmse:R}");
        }
        JsonFiles.Write(Path.Combine(root, "projection-probe-combined.json"), new { acceptedProductChange = false, pitch, geometry, rows });
    }
}

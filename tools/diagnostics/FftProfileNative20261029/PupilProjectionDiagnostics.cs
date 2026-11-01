using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.ZemaxComparison;

internal static class PupilProjectionDiagnostics
{
    internal static int Run(string repository, string output)
    {
        if (File.Exists(output)) throw new IOException("Output exists");
        using var zip = ZipFile.OpenRead(Path.Combine(repository, "validation/rechecks/2026-10-27/afocal-fft-energy/evidence-current.zip"));
        var rows = new List<object>();
        foreach (var configuration in new[] { 1, 2 })
        {
            using var stream = zip.GetEntry($"artifacts/validation/afocal-recompare-20261026/baseline-v4/models/003-binocular_system-c{configuration}.json")!.Open();
            var optic = Optic.FromSnapshot(JsonSerializer.Deserialize<OpticSnapshot>(stream, JsonFiles.Options)!);
            var support = typeof(Optic).Assembly.GetType("OpticalSystemDesign.Core.Analysis.ImageSpaceAnalysisSupport")!;
            var exit = ((double Position, double Diameter))support.GetMethod("CompoundExitPupil", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [optic])!;
            var nodes = new[] { (0d, 0d), (.001, 0d), (0d, .001), (-.001, 0d), (0d, -.001) };
            var traced = optic.SequentialRayTracer.TraceFinalSamples(optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(0, 0,
                optic.Wavelengths[1].Micrometers, nodes.Select(p => new PupilSample(p.Item1, p.Item2, 1)), optic.RayAimingEnabled));
            if (traced.Any(r => r is not { Vignetted: false, Intensity: > 0 })) throw new InvalidDataException("Pupil pencil blocked");
            var frame = optic.SurfaceGroup.Items[^1].CoordinateSystem;
            var points = traced.Select(r => frame.ToLocalPoint(r!.Position)).ToArray();
            var directions = traced.Select(r => frame.ToLocalDirection(r!.Direction)).ToArray();
            var normal = directions[0] / directions[0].Length;
            var centerDistance = (exit.Position - points[0].Z) / normal.Z;
            var plane = points[0] + normal * centerDistance;
            var local = points.Select((p, i) => p + directions[i] * (Dot(plane - p, normal) / Dot(directions[i], normal))).ToArray();
            var relative = points.Select((p, i) =>
            {
                var delta = p - points[0]; var projectedDirection = Dot(directions[i], normal); var axialDelta = Dot(delta, normal);
                return delta - normal * axialDelta + (directions[i] - normal * projectedDirection) * ((centerDistance - axialDelta) / projectedDirection);
            }).ToArray();
            var wavefront = WavefrontEngine.GenerateChiefRaySamples(optic, (0, 0), optic.Wavelengths[1], nodes,
                aimAtStop: optic.RayAimingEnabled, referenceWavelength: optic.Wavelengths[1]);
            var global = wavefront.Samples.Select(p => new Vector3D(p.PupilX, p.PupilY, p.PupilZ)).ToArray();
            object Measure(Vector3D[] p)
            {
                var x = (p[1] - p[0]).Length; var y = (p[2] - p[0]).Length;
                var sx = (p[1] - p[3]).Length; var sy = (p[2] - p[4]).Length;
                return new { positiveX = x, positiveY = y, positiveAspect = x / y, symmetricAspect = sx / sy };
            }
            rows.Add(new { configuration, exit.Position, exit.Diameter, points, directions,
                globalWavefront = Measure(global), localProjection = Measure(local), relativeProjection = Measure(relative) });
        }
        JsonFiles.Write(output, rows);
        Console.WriteLine(JsonSerializer.Serialize(rows, JsonFiles.Options));
        return 0;
    }
    static double Dot(Vector3D a, Vector3D b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
}

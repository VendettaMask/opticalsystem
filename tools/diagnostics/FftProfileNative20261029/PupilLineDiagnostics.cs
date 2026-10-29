using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.ZemaxComparison;

internal static class PupilLineDiagnostics
{
    internal static int Run(string repository, string output)
    {
        using var zip = ZipFile.OpenRead(Path.Combine(repository, "validation/rechecks/2026-10-27/afocal-fft-energy/evidence-current.zip"));
        var rows = new List<object>();
        foreach (var configuration in new[] { 1, 2 })
        {
            using var stream = zip.GetEntry($"artifacts/validation/afocal-recompare-20261026/baseline-v4/models/003-binocular_system-c{configuration}.json")!.Open();
            var optic = Optic.FromSnapshot(JsonSerializer.Deserialize<OpticSnapshot>(stream, JsonFiles.Options)!);
            var stopIndex = optic.SurfaceGroup.Items.ToList().FindIndex(s => s.IsStop);
            var stop = optic.SurfaceGroup.Items[stopIndex];
            var wave = optic.Wavelengths.Single(w => w.IsPrimary);
            var hit = optic.SequentialRayTracer.TraceGenericSurfaceSample(0, 0, 0, 0, wave.Micrometers, stopIndex, optic.RayAimingEnabled)!;
            var material = hit.InteractionKind is RayInteractionKind.Reflected or RayInteractionKind.TotalInternalReflection ? stop.MaterialBefore : stop.MaterialAfter;
            var trace = optic.SequentialRayTracer.GetType().GetMethod("TracePupilGeometryProbe", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var fit = typeof(ExitPupilGeometryEngine).GetMethod("FitLines", BindingFlags.Static | BindingFlags.NonPublic)!;
            foreach (var h in new[] { 1e-3, 1e-4, 1e-5 })
                foreach (var (name, direction) in new[] { ("positive-x", new Vector3D(1, 0, 0)), ("negative-x", new Vector3D(-1, 0, 0)), ("positive-y", new Vector3D(0, 1, 0)), ("negative-y", new Vector3D(0, -1, 0)) })
                {
                    var lines = new List<(Vector3D P, Vector3D D)>();
                    foreach (var d in new[] { hit.Direction, Unit(hit.Direction + direction * h) })
                    {
                        var ray = (RayTraceSample)trace.Invoke(optic.SequentialRayTracer, [new RealRay(hit.Position, d, wave.Nanometers), stopIndex + 1, material])!;
                        lines.Add((ray.Position, Unit(ray.Direction)));
                    }
                    var result = ((Vector3D Point, double Residual))fit.Invoke(null, [lines])!;
                    rows.Add(new { configuration, h, name, imageLocalZ = optic.SurfaceGroup.Items[^1].CoordinateSystem.ToLocalPoint(result.Point).Z, result.Residual });
                }
        }
        JsonFiles.Write(output, rows);
        Console.WriteLine(JsonSerializer.Serialize(rows, JsonFiles.Options));
        return 0;
    }
    private static Vector3D Unit(Vector3D value) => value / value.Length;
}

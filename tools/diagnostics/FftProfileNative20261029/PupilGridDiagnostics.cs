using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.ZemaxComparison;

internal static class PupilGridDiagnostics
{
    internal static int Run(string repository, string output)
    {
        if (Directory.Exists(output)) throw new IOException("Output already exists");
        Directory.CreateDirectory(output);
        using var zip = ZipFile.OpenRead(Path.Combine(repository, "validation/rechecks/2026-10-27/afocal-fft-energy/evidence-current.zip"));
        var rows = new List<object>();
        foreach (var configuration in new[] { 1, 2 })
        {
            var modelPath = $"artifacts/validation/afocal-recompare-20261026/baseline-v4/models/003-binocular_system-c{configuration}.json";
            using var stream = zip.GetEntry(modelPath)!.Open();
            var optic = Optic.FromSnapshot(JsonSerializer.Deserialize<OpticSnapshot>(stream, JsonFiles.Options)!);
            var directory = Path.Combine(repository, "artifacts/validation/afocal-native-resume-20261029-02");
            var request = JsonFiles.Read<JsonElement>(Path.Combine(directory, $"binocular-c{configuration}-boundary-and-seam33.request.json"));
            var native = JsonFiles.Read<JsonElement>(Path.Combine(directory, $"binocular-c{configuration}-boundary-and-seam33/image-geometry.json")).EnumerateArray().ToArray();
            var inputs = request.GetProperty("pointInputs").EnumerateArray().ToArray();
            var id = configuration == 1 ? "0065" : "0095";
            var operands = JsonFiles.Read<JsonElement>(Path.Combine(directory, $"{id}-003-binocular_system-c{configuration}-primary-f1-w2-encircled-energy-pupil/afocal-pupil-operands.json"))
                .GetProperty("results").EnumerateArray().ToArray();
            var nativeZ = operands.Single(row => row.GetProperty("code").GetString() == "EXPP").GetProperty("value").GetDouble();
            var pupil = new[] { (0d, 0d), (-.001, 0d), (.001, 0d), (0d, -.001), (0d, .001) };
            var nativeRays = pupil.Select(p =>
            {
                var index = Array.FindIndex(inputs, row => row.GetProperty("px").GetDouble() == p.Item1 && row.GetProperty("py").GetDouble() == p.Item2);
                var ray = native[index];
                return (P: new Vector3D(ray.GetProperty("x").GetDouble(), ray.GetProperty("y").GetDouble(), ray.GetProperty("z").GetDouble()),
                    D: new Vector3D(ray.GetProperty("l").GetDouble(), ray.GetProperty("m").GetDouble(), ray.GetProperty("n").GetDouble()));
            }).ToArray();
            var coreRays = optic.SequentialRayTracer.TraceFinalSamples(optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(0, 0,
                optic.Wavelengths[1].Micrometers, pupil.Select(p => new PupilSample(p.Item1, p.Item2, 1)), optic.RayAimingEnabled));
            var frame = optic.SurfaceGroup.Items[^1].CoordinateSystem;
            var core = coreRays.Select(ray => (P: frame.ToLocalPoint(ray!.Position), D: frame.ToLocalDirection(ray.Direction))).ToArray();
            var support = typeof(Optic).Assembly.GetType("OpticalSystemDesign.Core.Analysis.ImageSpaceAnalysisSupport")!;
            var exit = ((double Position, double Diameter))support.GetMethod("CompoundExitPupil", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [optic])!;
            object Measure((Vector3D P, Vector3D D)[] rays, double z)
            {
                var n = rays[0].D / rays[0].D.Length;
                var origin = rays[0].P + n * ((z - rays[0].P.Z) / n.Z);
                var positions = rays.Select(ray => ray.P + ray.D * (Dot(origin - ray.P, n) / Dot(ray.D, n))).ToArray();
                var positiveX = (positions[2] - positions[0]).Length / .001;
                var negativeX = (positions[1] - positions[0]).Length / .001;
                var positiveY = (positions[4] - positions[0]).Length / .001;
                var negativeY = (positions[3] - positions[0]).Length / .001;
                return new { z, positions, positiveX, negativeX, positiveY, negativeY,
                    symmetricX = (positions[2] - positions[1]).Length / .002,
                    symmetricY = (positions[4] - positions[3]).Length / .002,
                    positiveAspect = positiveX / positiveY, negativeAspect = negativeX / negativeY };
            }
            var material = (CatalogGlassMaterial)optic.SurfaceGroup.Items.Select(s => s.Geometry).OfType<PolygonGroupGeometry>().Single().VolumeMaterial;
            rows.Add(new { configuration, material = new { material.Formula, material.Coefficients, index = material.RefractiveIndex(optic.Wavelengths[1].Nanometers), wavelength = optic.Wavelengths[1].Nanometers }, nativeZ, coreExit = new { exit.Position, exit.Diameter },
                nativeAtNative = Measure(nativeRays, nativeZ), coreAtNative = Measure(core, nativeZ), coreAtCore = Measure(core, exit.Position) });
        }
        JsonFiles.Write(Path.Combine(output, "pupil-scales.json"), rows);
        Console.WriteLine(JsonSerializer.Serialize(rows, JsonFiles.Options));
        return 0;
    }
    private static double Dot(Vector3D a, Vector3D b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
}

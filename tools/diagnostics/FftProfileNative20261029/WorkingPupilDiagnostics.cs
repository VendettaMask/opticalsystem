using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.ZemaxComparison;

internal static class WorkingPupilDiagnostics
{
    internal static int Run(string repository, string output)
    {
        if (File.Exists(output)) throw new IOException("Output exists");
        using var zip = ZipFile.OpenRead(Path.Combine(repository, "validation/rechecks/2026-10-27/afocal-fft-energy/evidence-current.zip"));
        var rows = new List<object>();
        foreach (var model in new[] { "001-afocal-riflescope-c1", "002-beam-expander-c1", "003-binocular_system-c1", "003-binocular_system-c2", "004-apochromat_prism-c1" })
        {
            using var stream = zip.GetEntry("artifacts/validation/afocal-recompare-20261026/baseline-v4/models/" + model + ".json")!.Open();
            var optic = Optic.FromSnapshot(JsonSerializer.Deserialize<OpticSnapshot>(stream, JsonFiles.Options)!);
            var wave = optic.Wavelengths.FirstOrDefault(w => w.IsPrimary) ?? optic.Wavelengths[0];
            const double h = .001;
            var nodes = new[] { (0d, 0d), (h, 0d), (0d, h), (-h, 0d), (0d, -h) };
            var final = optic.SequentialRayTracer.TraceFinalSamples(optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(0, 0,
                wave.Micrometers, nodes.Select(p => new PupilSample(p.Item1, p.Item2, 1)), optic.RayAimingEnabled));
            if (final.Any(r => r is not { Vignetted: false, Intensity: > 0 })) throw new InvalidDataException("Pupil pencil blocked");
            var frame = optic.SurfaceGroup.Items[^1].CoordinateSystem;
            var p = final.Select(r => frame.ToLocalPoint(r!.Position)).ToArray();
            var d = final.Select(r => frame.ToLocalDirection(r!.Direction)).ToArray();
            var normal = d[0] / d[0].Length;
            double? paraZ = null, paraD = null;
            try { paraZ = optic.Paraxial.EstimateExitPupilLocation(); paraD = optic.Paraxial.EstimateExitPupilDiameter(); }
            catch (NotSupportedException) { }
            var geometric = ExitPupilGeometryEngine.Compute(optic, (0, 0), wave, nodes.Take(3).ToArray());
            var geometricZ = frame.ToLocalPoint(geometric.Center).Z;
            object Measure(double z)
            {
                var distance = (z - p[0].Z) / normal.Z;
                var projected = p.Select((point, i) =>
                {
                    var delta = point - p[0]; var q = Dot(d[i], normal); var axial = Dot(delta, normal);
                    return delta - normal * axial + (d[i] - normal * q) * ((distance - axial) / q);
                }).ToArray();
                return new { z, positiveX = 2 * projected[1].Length / h, positiveY = 2 * projected[2].Length / h,
                    symmetricX = (projected[1] - projected[3]).Length / h, symmetricY = (projected[2] - projected[4]).Length / h };
            }
            var support = typeof(Optic).Assembly.GetType("OpticalSystemDesign.Core.Analysis.ImageSpaceAnalysisSupport")!;
            rows.Add(new { model, primaryWavelengthMicrometers = wave.Micrometers,
                paraxialApplicable = !optic.SurfaceGroup.Items.Any(s => s.Geometry is OpticalSystemDesign.Core.Geometries.PolygonGroupGeometry),
                paraxialCompatibilityValuesAreDiagnosticOnly = true, paraZ, paraD,
                currentDiffractionDiameter = (double)support.GetMethod("AfocalDiffractionPupilDiameterMillimeters", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [optic])!,
                geometricZ, geometricDiameter = 2 * geometric.DisplayRadiusMillimeters / h,
                atParaxialPlane = paraZ is { } z ? Measure(z) : null, atGeometricPlane = Measure(geometricZ) });
        }
        JsonFiles.Write(output, rows);
        Console.WriteLine(JsonSerializer.Serialize(rows, JsonFiles.Options));
        return 0;
    }
    static double Dot(Vector3D a, Vector3D b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
}

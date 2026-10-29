using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Normalization;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

internal static class OpdReferenceDiagnostics
{
    internal static void WriteIfRequested(string id, Optic optic, CanonicalAnalysisRequest request,
        NumericResult actual, NumericResult expected)
    {
        var directory = Environment.GetEnvironmentVariable("OPTICAL_OPD_DIAGNOSTICS");
        if (string.IsNullOrWhiteSpace(directory) || !id.Contains("riflescope")) return;
        Directory.CreateDirectory(directory);
        var fields = (IReadOnlyList<(double, double)>)typeof(WavefrontEngine).Assembly
            .GetType("OpticalSystemDesign.Core.Analysis.SpotAnalysisEngine")!
            .GetMethod("DefinedFields", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public)!
            .Invoke(null, [optic])!;
        var field = fields[request.Field - 1];
        var wave = optic.Wavelengths[request.Wavelength - 1];
        var primary = optic.Wavelengths.First(w => w.IsPrimary);
        var pupil = Enumerable.Range(0, 41).Select(i => -1 + i / 20.0).ToArray();
        var nodes = pupil.Select(y => (0.0, y)).Concat(pupil.Select(x => (x, 0.0))).ToArray();
        var selected = WavefrontEngine.GenerateChiefRaySamples(optic, field, wave, nodes,
            aimAtStop: optic.RayAimingEnabled, referenceWavelength: wave);
        var primaryReference = WavefrontEngine.GenerateChiefRaySamples(optic, field, wave, nodes,
            aimAtStop: optic.RayAimingEnabled, referenceWavelength: primary);
        var chief = optic.SequentialRayTracer.TraceFinalSamples(optic.SequentialRayTracer.RayGenerator.GenerateGeneric(
            field.Item1, field.Item2, 0, 0, wave.Micrometers, optic.RayAimingEnabled)).Single()!;
        var selectedDistance = optic.Paraxial.EstimateExitPupilLocation(wave.Micrometers);
        var primaryDistance = optic.Paraxial.EstimateExitPupilLocation(primary.Micrometers);
        var direction = chief.Direction;
        var normalZ = optic.SurfaceGroup.Items[^1].CoordinateSystem.ToLocalDirection(direction).Z;
        double[] AtDistance(double distance)
        {
            var currentZ = optic.SurfaceGroup.Items[^1].CoordinateSystem
                .ToLocalPoint(selected.AfocalReferencePlaneOrigin!.Value).Z;
            var shift = (distance - currentZ) / normalZ;
            return selected.Samples.Select(s => s.OpdWaves + selected.ImageRefractiveIndex
                * shift * (1 - 1 / Dot(s.ImageDirection!.Value, direction)) / (wave.Micrometers * 1e-3)).ToArray();
        }
        JsonFiles.Write(Path.Combine(directory, id + ".json"), new { id, request, actual, expected,
            selectedDistance, primaryDistance, direction, selected, primaryReference,
            primaryPupil = AtDistance(primaryDistance), chromaticPupil = AtDistance(selectedDistance) });
    }

    private static double Dot(Vector3D a, Vector3D b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
}

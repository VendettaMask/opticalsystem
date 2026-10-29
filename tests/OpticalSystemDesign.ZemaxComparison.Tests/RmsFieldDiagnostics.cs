using System.Reflection;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Normalization;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

internal static class RmsFieldDiagnostics
{
    internal static void WriteIfRequested(string id, Optic optic, CanonicalAnalysisRequest request,
        AnalysisData data, NumericResult actual, NumericResult expected)
    {
        var directory = Environment.GetEnvironmentVariable("OPTICAL_RMS_FIELD_DIAGNOSTICS");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var fields = new List<object>();
        var angularVariants = new List<object>();
        if (id.Contains("riflescope"))
        {
            var working = Optic.FromSnapshot(optic.ToSnapshot());
            foreach (var field in working.Fields)
            {
                field.VignetteFactorX = field.VignetteFactorY = field.VignetteDecenterX = field.VignetteDecenterY = field.VignetteAngleDegrees = 0;
            }
            var pupil = ApertureSampler.GenerateGaussianQuadrature(6, 12);
            var generator = working.SequentialRayTracer.RayGenerator;
            var stop = working.SurfaceGroup.Items.Single(s => s.IsStop);
            var wave = working.Wavelengths[request.Wavelength - 1];
            foreach (var index in new[] { 0, 9, 12, 13, 14, 15 })
            {
                var hy = index / 15.0;
                var targets = ((double X, double Y)[])typeof(RayGenerator).GetMethod("ParaxialStopTargets", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(generator, [pupil, generator.GetPupilVignetting(0, hy)])!;
                var bundle = generator.GenerateNormalizedPupilSamples(0, hy, wave.Micrometers, pupil, aimAtStop: true);
                // Revival is diagnostic only: show where a failed-aim launch would go.
                var diagnosticBundle = new RealRayBundle(bundle.Rays.Select((ray, i) => ray with { Intensity = pupil[i].Weight }));
                var histories = working.SequentialRayTracer.TraceGaussianPupil(diagnosticBundle);
                var rays = histories.Select((history, i) =>
                {
                    var hit = history.FirstOrDefault(s => s.SurfaceNumber == stop.Number);
                    var local = hit is null ? (OpticalSystemDesign.Core.Backend.Vector3D?)null : stop.CoordinateSystem.ToLocalPoint(hit.Position);
                    return new { index = i, pupil = pupil[i], targetX = targets[i].X, targetY = targets[i].Y,
                        launch = bundle.Rays[i], stop = local,
                        stopResidual = local is { } p ? double.Hypot(p.X - targets[i].X, p.Y - targets[i].Y) : double.PositiveInfinity,
                        history };
                }).ToArray();
                fields.Add(new { index, normalizedFieldY = hy, degrees = hy * request.MaximumFieldRadius,
                    aliveLaunches = bundle.Rays.Count(r => r.Intensity > 0), rays });
                if (index is 0 or >= 13)
                {
                    var phase = Math.PI / 12;
                    var rotated = pupil.Select(p => new PupilSample(
                        p.X * Math.Cos(phase) - p.Y * Math.Sin(phase),
                        p.X * Math.Sin(phase) + p.Y * Math.Cos(phase), p.Weight)).ToArray();
                    var alternate = generator.GenerateNormalizedPupilSamples(0, hy, wave.Micrometers, rotated, aimAtStop: true);
                    var traced = working.SequentialRayTracer.TraceGaussianPupil(alternate);
                    var image = working.SurfaceGroup.Items[^1];
                    var points = traced.Select((history, i) =>
                    {
                        var last = history.Last();
                        var direction = image.CoordinateSystem.ToLocalDirection(last.Direction);
                        return new { X = Math.Atan2(direction.X, direction.Z) * 1000,
                            Y = Math.Atan2(direction.Y, direction.Z) * 1000, Weight = alternate.Rays[i].Intensity };
                    }).ToArray();
                    var weight = points.Sum(p => p.Weight);
                    var cx = points.Sum(p => p.Weight * p.X) / weight;
                    var cy = points.Sum(p => p.Weight * p.Y) / weight;
                    var rms = Math.Sqrt(points.Sum(p => p.Weight * (Math.Pow(p.X - cx, 2) + Math.Pow(p.Y - cy, 2))) / weight);
                    angularVariants.Add(new { index, phaseRadians = phase, nodeCount = rotated.Length,
                        alive = alternate.Rays.Count(r => r.Intensity > 0), rmsMilliradians = rms, points });
                }
            }
        }
        JsonFiles.Write(Path.Combine(directory, id + ".json"), new { id, request, data.Values, actual, expected, fields, angularVariants });
    }
}

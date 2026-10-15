using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis;

public enum ExitPupilGeometryMode { WorkingFNumber, GeometricStopImage }

/// <summary>Source coordinates and display coordinates are deliberately separate.
/// Physical coordinates and line-fit residuals are in millimeters.</summary>
public sealed record AnalysisSpatialPoint(double SourceX, double SourceY,
    double X, double Y, double Z, double PhysicalX, double PhysicalY, double PhysicalZ,
    double FitResidualMillimeters);

public sealed record ExitPupilGeometryResult(IReadOnlyList<AnalysisSpatialPoint> Points,
    Vector3D Center, double DisplayRadiusMillimeters, double MaximumFitResidualMillimeters);

/// <summary>Images each accepted stop point through the real post-stop system.
/// Five infinitesimal direction probes define a least-squares 3-D conjugate;
/// aberrated/astigmatic pencils need not have a unique exact intersection.</summary>
public static class ExitPupilGeometryEngine
{
    public static ExitPupilGeometryResult Compute(Optic optic, (double Hx, double Hy) field,
        Wavelength wavelength, IReadOnlyList<(double X, double Y)> pupils, double probeAngle = 1e-4)
    {
        ArgumentNullException.ThrowIfNull(optic);
        ArgumentNullException.ThrowIfNull(pupils);
        if (!double.IsFinite(probeAngle) || probeAngle is < 1e-6 or > .01)
            throw new ArgumentOutOfRangeException(nameof(probeAngle));
        if (pupils.Count == 0 || 6L * pupils.Count * optic.SurfaceGroup.Items.Count > AnalysisResourceLimits.MaximumAnalysisRayWork)
            throw new ArgumentOutOfRangeException(nameof(pupils));
        var surfaces = optic.SurfaceGroup.Items;
        var stopIndex = surfaces.ToList().FindIndex(surface => surface.IsStop);
        if (stopIndex < 1 || stopIndex >= surfaces.Count - 1)
            throw new AnalysisDataUnavailableException("Exit pupil geometry", "a finite physical stop before the image is required");
        var stop = surfaces[stopIndex];
        var cache = new Dictionary<(double X, double Y), (Vector3D Point, double Residual)>();
        (Vector3D Point, double Residual) Image((double X, double Y) pupil)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            if (!double.IsFinite(pupil.X) || !double.IsFinite(pupil.Y)) throw new ArgumentOutOfRangeException(nameof(pupils));
            if (cache.TryGetValue(pupil, out var retained)) return retained;
            var bundle = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(field.Hx, field.Hy,
                pupil.X, pupil.Y, wavelength.Micrometers, optic.RayAimingEnabled);
            using var trace = optic.SequentialRayTracer.Trace(bundle, TraceRequest.Selected(new[] { stopIndex, surfaces.Count - 1 }));
            if (!trace.TryGetSample(0, surfaces.Count - 1, out var end) || end.Vignetted
                || !trace.TryGetSample(0, stopIndex, out var hit) || hit.Vignetted)
                throw new AnalysisDataUnavailableException("Exit pupil geometry", "requested central ray is physically blocked");
            var d = Unit(hit.Direction);
            var a = Unit(Cross(d, Math.Abs(d.Z) < .9 ? new(0, 0, 1) : new(0, 1, 0)));
            var b = Cross(d, a);
            var material = hit.InteractionKind is RayInteractionKind.Reflected or RayInteractionKind.TotalInternalReflection
                ? stop.MaterialBefore : stop.MaterialAfter;
            var lines = new List<(Vector3D P, Vector3D D)>();
            foreach (var direction in new[] { d, Unit(d + a * probeAngle), Unit(d - a * probeAngle),
                Unit(d + b * probeAngle), Unit(d - b * probeAngle) })
            {
                var ray = new RealRay(hit.Position, direction, wavelength.Nanometers);
                var output = optic.SequentialRayTracer.TracePupilGeometryProbe(ray, stopIndex + 1, material)
                    ?? throw new AnalysisDataUnavailableException("Exit pupil geometry", "post-stop geometric pencil failed");
                lines.Add((output.Position, Unit(output.Direction)));
            }
            var fit = FitLines(lines);
            cache[pupil] = fit;
            return fit;
        }
        var center = Image((0, 0)).Point;
        var frame = surfaces[^1].CoordinateSystem;
        var xAxis = frame.ToGlobalDirection(new(1, 0, 0));
        var yAxis = frame.ToGlobalDirection(new(0, 1, 0));
        var zAxis = frame.ToGlobalDirection(new(0, 0, 1));
        var physical = pupils.Select(p => (Pupil: p, Fit: Image(p))).ToArray();
        var radius = physical.Max(p => Math.Sqrt(Math.Pow(Dot(p.Fit.Point - center, xAxis), 2)
            + Math.Pow(Dot(p.Fit.Point - center, yAxis), 2)));
        if (!double.IsFinite(radius) || radius <= 1e-12)
            throw new AnalysisDataUnavailableException("Exit pupil geometry", "no finite transverse pupil extent");
        var points = physical.Select(p =>
        {
            var delta = p.Fit.Point - center;
            return new AnalysisSpatialPoint(p.Pupil.X, p.Pupil.Y, Dot(delta, xAxis) / radius,
                Dot(delta, yAxis) / radius, Dot(delta, zAxis) / radius,
                p.Fit.Point.X, p.Fit.Point.Y, p.Fit.Point.Z, p.Fit.Residual);
        }).ToArray();
        return new(points, center, radius, points.Max(p => p.FitResidualMillimeters));
    }

    internal static (Vector3D Point, double Residual) FitLines(IReadOnlyList<(Vector3D P, Vector3D D)> lines)
    {
        var origin = lines[0].P;
        var matrix = new double[3, 4];
        foreach (var (point, direction) in lines)
        {
            var d = Unit(direction); var p = point - origin;
            var v = new[] { d.X, d.Y, d.Z }; var q = new[] { p.X, p.Y, p.Z };
            for (var r = 0; r < 3; r++)
            for (var c = 0; c < 3; c++)
            {
                var value = (r == c ? 1 : 0) - v[r] * v[c];
                matrix[r, c] += value; matrix[r, 3] += value * q[c];
            }
        }
        for (var c = 0; c < 3; c++)
        {
            var pivot = Enumerable.Range(c, 3 - c).MaxBy(r => Math.Abs(matrix[r, c]));
            if (!double.IsFinite(matrix[pivot, c]) || Math.Abs(matrix[pivot, c]) < 1e-13)
                throw new AnalysisDataUnavailableException("Exit pupil geometry", "parallel pencil has no finite conjugate");
            for (var k = c; k < 4; k++) (matrix[c, k], matrix[pivot, k]) = (matrix[pivot, k], matrix[c, k]);
            var divisor = matrix[c, c];
            for (var k = c; k < 4; k++) matrix[c, k] /= divisor;
            for (var r = 0; r < 3; r++) if (r != c)
            {
                var factor = matrix[r, c];
                for (var k = c; k < 4; k++) matrix[r, k] -= factor * matrix[c, k];
            }
        }
        var result = origin + new Vector3D(matrix[0, 3], matrix[1, 3], matrix[2, 3]);
        var residual = Math.Sqrt(lines.Average(line =>
        {
            var delta = result - line.P; var perpendicular = delta - Unit(line.D) * Dot(delta, Unit(line.D));
            return Dot(perpendicular, perpendicular);
        }));
        if (!double.IsFinite(result.Length) || !double.IsFinite(residual))
            throw new ArithmeticException("Non-finite pupil conjugate.");
        return (result, residual);
    }
    internal static double Dot(Vector3D a, Vector3D b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    internal static Vector3D Cross(Vector3D a, Vector3D b) => new(a.Y * b.Z - a.Z * b.Y,
        a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    internal static Vector3D Unit(Vector3D v) => v.Length > 1e-15 && double.IsFinite(v.Length)
        ? v / v.Length : throw new ArithmeticException("Invalid geometric direction.");
}

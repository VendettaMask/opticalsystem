using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Rays;

namespace OptilandWorkbench.Core.Services;

/// <summary>Manufacturing metrics evaluated from the formal trace at a single interface.</summary>
public static class RayManufacturingMetrics
{
    /// <summary>
    /// HYLD: |n - n'| (1 - cos(theta)), using incidence for n' > n and exitance otherwise.
    /// This is a sensitivity penalty, not a probability of passing tolerances.
    /// Reflection, TIR, diffraction and scattering conventions are not validated.
    /// </summary>
    public static double HighYieldContribution(OpticalSurface surface, RayTraceSample sample)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(sample);
        ComputationCancellation.ThrowIfCancellationRequested();
        if (sample.SurfaceNumber != surface.Number || sample.Vignetted
            || !double.IsFinite(sample.Intensity) || sample.Intensity <= 0
            || sample.InteractionKind is null || !Finite(sample.Position))
            throw new InvalidOperationException("HYLD 需要到达指定表面的有效光线交点。");
        if (sample.InteractionKind != RayInteractionKind.Transmitted || surface.IsReflective
            || surface.InteractionModel is not RefractiveReflectiveInteractionModel { IsReflective: false }
            || surface.Geometry is IGratingGeometry || surface.ScatteringModel is not null)
            throw new NotSupportedException("HYLD 当前仅支持普通折射界面；反射、全反射、衍射、理想薄透镜和散射尚未验证。");
        var before = Index(sample.RefractiveIndexBefore);
        var after = Index(sample.RefractiveIndexAfter);
        // Read indices captured by the tracer, including GRIN's local field and retained
        // medium after a reflection. A surface-list lookup cannot reconstruct that state.
        var incident = Unit(sample.IncidentDirection
            ?? throw new InvalidOperationException("HYLD 追迹缺少入射方向。"));
        var outgoing = Unit(sample.Direction);
        var point = surface.CoordinateSystem.ToLocalPoint(sample.Position);
        var normal = Unit(surface.CoordinateSystem.ToGlobalDirection(surface.Geometry.SurfaceNormal(point)));
        var direction = after > before ? incident : outgoing;
        if (Dot(direction, normal) < 0) normal = -normal;
        // For unit vectors, half the squared difference equals 1 - cos(theta).
        // This form retains the penalty at tiny angles where subtraction rounds to zero.
        var difference = direction - normal;
        var value = Math.Abs(after - before) * 0.5 * Dot(difference, difference);
        return double.IsFinite(value) ? value
            : throw new InvalidOperationException("HYLD 计算未得到有限值。");
    }

    private static double Index(double? value) => value is { } n && double.IsFinite(n) && n > 0 ? n
        : throw new InvalidOperationException("HYLD 追迹缺少有效的界面局部折射率。");

    private static Vector3D Unit(Vector3D value) => Finite(value) && double.IsFinite(value.Length) && value.Length > 0
        ? value / value.Length : throw new InvalidOperationException("HYLD 方向或法线无效。");

    private static bool Finite(Vector3D value) => double.IsFinite(value.X) && double.IsFinite(value.Y) && double.IsFinite(value.Z);
    private static double Dot(Vector3D a, Vector3D b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
}

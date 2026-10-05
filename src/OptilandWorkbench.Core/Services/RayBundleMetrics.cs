using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Raytrace;

namespace OptilandWorkbench.Core.Services;

public readonly record struct WeightedLocalRay(Vector3D Position, Vector3D Direction, double Weight);

/// <summary>Measurements of formal sequential traces; owns no ray or result cache.</summary>
public static class RayBundleMetrics
{
    public static IReadOnlyList<PupilSample> RectangularPupil(int side)
    {
        if (side is < 1 or > 513)
            throw new ArgumentOutOfRangeException(nameof(side), "瞳孔网格边长必须在 1..513 内。");
        var samples = ApertureSampler.Generate(checked(side * side), PupilSampling.UniformGrid);
        return samples.Count > 0 ? samples : throw new InvalidOperationException("所选网格在单位圆瞳内没有采样点。");
    }

    public static WeightedLocalRay[] Trace(Optic optic, int surfaceNumber, int wave,
        double hx, double hy, IReadOnlyList<PupilSample> pupils, bool requireUnvignetted = false,
        bool includeSurfaceTransmission = true, bool allowEmpty = false, bool? aimAtStop = null)
    {
        ComputationCancellation.ThrowIfCancellationRequested();
        if (wave < 0 || wave > optic.Wavelengths.Count || optic.Wavelengths.Count == 0)
            throw new ArgumentOutOfRangeException(nameof(wave), "波长编号不存在；0 表示多波长。");
        var surface = optic.SurfaceGroup.Items.FirstOrDefault(s => s.Number == surfaceNumber)
            ?? throw new ArgumentOutOfRangeException(nameof(surfaceNumber));
        var surfaceIndex = optic.SurfaceGroup.Items.IndexOf(surface);
        if (surfaceIndex == 0 && ObjectConjugate.IsInfinite(surface))
            throw new InvalidOperationException("无穷远物面没有有限质心。");
        var wavelengths = wave == 0 ? optic.Wavelengths.ToArray() : [optic.Wavelengths[wave - 1]];
        var rays = new List<WeightedLocalRay>();
        foreach (var wavelength in wavelengths)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var spectralWeight = wave == 0 ? wavelength.Weight : 1;
            if (!double.IsFinite(spectralWeight) || spectralWeight < 0)
                throw new InvalidOperationException("多波长权重必须为有限非负值。");
            if (spectralWeight == 0) continue;
            var bundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(
                hx, hy, wavelength.Micrometers, pupils, aimAtStop: aimAtStop ?? optic.RayAimingEnabled);
            using var trace = optic.SequentialRayTracer.Trace(bundle, TraceRequest.Selected([surfaceIndex]));
            var candidates = trace.GetSurfaceSamples(surfaceIndex);
            for (var rayIndex = 0; rayIndex < candidates.Count; rayIndex++)
            {
                var candidate = candidates[rayIndex];
                ComputationCancellation.ThrowIfCancellationRequested();
                if (candidate is not { } sample || sample.Vignetted || sample.InteractionKind is null)
                {
                    if (requireUnvignetted) throw new InvalidOperationException("所请求的参考光线或高斯求积光线被渐晕，或未完成表面相互作用。");
                    continue;
                }
                var intensity = includeSurfaceTransmission ? sample.Intensity : bundle.Rays[rayIndex].Intensity;
                var weight = intensity * spectralWeight;
                if (!double.IsFinite(weight) || weight < 0)
                    throw new InvalidOperationException("光线权重不是有限非负值。");
                if (weight == 0) continue;
                var point = surface.CoordinateSystem.ToLocalPoint(sample.Position);
                var direction = surface.CoordinateSystem.ToLocalDirection(sample.Direction);
                var length = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y + direction.Z * direction.Z);
                if (!Finite(point) || !Finite(direction) || !double.IsFinite(length) || length <= 0)
                    throw new InvalidOperationException("光线坐标或方向无效。");
                rays.Add(new(point, new(direction.X / length, direction.Y / length, direction.Z / length), weight));
            }
        }
        return rays.Count > 0 || allowEmpty ? rays.ToArray() : throw new InvalidOperationException("没有具有正权重的有效光线。");
    }

    public static Vector3D Centroid(IReadOnlyList<WeightedLocalRay> rays, bool direction = false)
    {
        if (rays.Count == 0) throw new InvalidOperationException("没有可计算质心的光线。");
        var scale = rays.Max(r => r.Weight);
        if (!double.IsFinite(scale) || scale <= 0) throw new InvalidOperationException("质心权重无效。");
        double sum = 0, x = 0, y = 0, z = 0;
        foreach (var ray in rays)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var weight = ray.Weight / scale;
            var value = direction ? ray.Direction : ray.Position;
            if (!Finite(value) || !double.IsFinite(weight) || weight < 0)
                throw new InvalidOperationException("质心样本无效。");
            sum += weight; x += weight * value.X; y += weight * value.Y; z += weight * value.Z;
        }
        var result = new Vector3D(x / sum, y / sum, z / sum);
        return Finite(result) ? result : throw new InvalidOperationException("质心不是有限值。");
    }

    public static double AngularCentroid(IReadOnlyList<WeightedLocalRay> rays, bool x)
    {
        var direction = Centroid(rays, direction: true);
        var transverse = x ? direction.X : direction.Y;
        if (Math.Abs(transverse) + Math.Abs(direction.Z) <= 1e-15)
            throw new InvalidOperationException("质心方向在所选局部投影中未定义。");
        return Math.Atan2(transverse, direction.Z);
    }

    public static double GeometricRadius(IReadOnlyList<WeightedLocalRay> rays, Vector3D reference)
    {
        if (rays.Count == 0 || !Finite(reference)) throw new InvalidOperationException("点列参考无效。");
        var maximum = 0.0;
        foreach (var ray in rays)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var dx = ray.Position.X - reference.X; var dy = ray.Position.Y - reference.Y;
            var radius = Math.Sqrt(dx * dx + dy * dy);
            if (!double.IsFinite(radius)) throw new InvalidOperationException("点列半径不是有限值。");
            maximum = Math.Max(maximum, radius);
        }
        return maximum;
    }

    private static bool Finite(Vector3D v) => double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z);
}

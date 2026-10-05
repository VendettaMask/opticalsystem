using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Raytrace;

namespace OptilandWorkbench.Core.Services;

public enum EnergyRegion { Circle = 1, XSlit = 2, YSlit = 3, Square = 4 }
public enum GeometricEnergyReference { ChiefRay, Centroid, Vertex, Middle }
public readonly record struct WeightedEnergyPoint(double X, double Y, double Weight);

/// <summary>Image-local scalar geometric energy. Coordinates and radii are in micrometers.</summary>
public static class GeometricEnergyMetrics
{
    public static GeometricEnergyDistribution Create(Optic optic, int samplingCode, int wave, int fieldNumber,
        EnergyRegion region, GeometricEnergyReference reference, bool multiplyByDiffractionLimit)
    {
        if (!Enum.IsDefined(region) || !Enum.IsDefined(reference)) throw new ArgumentOutOfRangeException(nameof(region));
        var (samples, wavelengths, field) = Trace(optic, samplingCode, wave, fieldNumber);
        var center = Reference(optic, wave, field, samples, reference);
        var airy = multiplyByDiffractionLimit ? wavelengths.Select(w =>
        {
            // The documented Airy scaling uses the on-axis working F number. Do not
            // substitute a smaller pupil zone when the full marginal rays fail.
            var axes = DiffractionEngine.WorkingFNumbers(optic, (0, 0), w,
                aimAtStop: optic.RayAimingEnabled, strictFullPupil: true);
            var inverseSquared = .5 * (1 / (axes.Tangential * axes.Tangential) + 1 / (axes.Sagittal * axes.Sagittal));
            var fNumber = 1 / Math.Sqrt(inverseSquared);
            if (!double.IsFinite(fNumber) || fNumber <= 0) throw new InvalidOperationException("衍射极限需要有效的全瞳工作 F 数。");
            return new AiryEnergyComponent(w.Micrometers, w.Weight, fNumber);
        }).ToArray() : [];
        return new(samples, center, region, airy);
    }

    public static double EdgePositionMillimeters(Optic optic, int samplingCode, int wave, int fieldNumber,
        int type, double fraction, double maximumRadiusMicrometers = 0)
    {
        if (type is < 0 or > 3 || !double.IsFinite(fraction) || fraction is < .01 or > .99)
            throw new ArgumentOutOfRangeException(nameof(type), "ERFP Type 为 0..3，Fraction 为 0.01..0.99。");
        if (!double.IsFinite(maximumRadiusMicrometers) || maximumRadiusMicrometers != 0)
            throw new NotSupportedException("ERFP 当前支持 Max Radius=0 的完整光斑积分；自定义窗口尚未验证。");
        var (samples, _, field) = Trace(optic, samplingCode, wave, fieldNumber);
        var center = Reference(optic, wave, field, samples,
            type < 2 ? GeometricEnergyReference.ChiefRay : GeometricEnergyReference.Vertex);
        var distribution = new WeightedEnergyDistribution(samples.Select(p =>
            ((type % 2 == 0 ? p.X - center.X : p.Y - center.Y), p.Weight)));
        return distribution.Quantile(fraction) / 1000;
    }

    private static (WeightedEnergyPoint[] Samples, Wavelength[] Wavelengths, (double Hx, double Hy) Field)
        Trace(Optic optic, int samplingCode, int wave, int fieldNumber)
    {
        ArgumentNullException.ThrowIfNull(optic); ComputationCancellation.ThrowIfCancellationRequested();
        if (optic.ImageSpaceAfocal) throw new NotSupportedException("几何能量操作数当前只支持有焦像空间。");
        if (samplingCode is < 1 or > 5) throw new ArgumentOutOfRangeException(nameof(samplingCode), "Samp 支持 1..5（32..512 个瞳孔区间）。");
        if (fieldNumber < 1 || fieldNumber > optic.Fields.Count) throw new ArgumentOutOfRangeException(nameof(fieldNumber));
        if (optic.SurfaceGroup.Items.Count < 2) throw new InvalidOperationException("缺少可追迹的像面。");
        var wavelengths = MetricWavelengthSelection.Select(optic, wave);
        (double Hx, double Hy) field = FieldCoordinates.Normalize(optic.Fields, optic.Fields[fieldNumber - 1].X, optic.Fields[fieldNumber - 1].Y);
        var pupils = SpotAnalysisEngine.CreatePupilSamples(32 << (samplingCode - 1), "uniform-intervals");
        if ((long)pupils.Count * wavelengths.Length > SequentialTraceLimits.MaximumRayCount)
            throw new InvalidOperationException("多波长几何能量超过共享光线数预算。");
        var samples = new List<WeightedEnergyPoint>();
        foreach (var w in wavelengths)
        {
            // Trace each monochromatic bundle with unit spectral weight, then apply
            // normalized spectral weights. This avoids overflow from large weights.
            var index = optic.Wavelengths.ToList().FindIndex(v => v.Nanometers == w.Nanometers) + 1;
            var rays = RayBundleMetrics.Trace(optic, optic.SurfaceGroup.Items[^1].Number, index,
                field.Hx, field.Hy, pupils, includeSurfaceTransmission: false, allowEmpty: true);
            samples.AddRange(rays.Select(ray => new WeightedEnergyPoint(ray.Position.X * 1000,
                ray.Position.Y * 1000, ray.Weight * w.Weight)));
        }
        return (samples.ToArray(), wavelengths, field);
    }

    private static (double X, double Y) Reference(Optic optic, int wave, (double Hx, double Hy) field,
        IReadOnlyList<WeightedEnergyPoint> samples, GeometricEnergyReference reference)
    {
        if (reference == GeometricEnergyReference.ChiefRay)
        {
            var primaryIndex = optic.Wavelengths.ToList().FindIndex(w => w.IsPrimary);
            var selected = wave > 0 ? wave : primaryIndex >= 0 ? primaryIndex + 1 : 1;
            var ray = RayBundleMetrics.Trace(optic, optic.SurfaceGroup.Items[^1].Number, selected,
                field.Hx, field.Hy, [new PupilSample(0, 0, 1)], requireUnvignetted: true, includeSurfaceTransmission: false)[0];
            return (ray.Position.X * 1000, ray.Position.Y * 1000);
        }
        return GeometricEnergyDistribution.Reference(samples, reference);
    }
}

internal readonly record struct AiryEnergyComponent(double Wavelength, double Weight, double FNumber);

public sealed class GeometricEnergyDistribution
{
    private readonly WeightedEnergyDistribution _distribution;
    private readonly EnergyRegion _region;
    private readonly AiryEnergyComponent[] _airy;
    public (double X, double Y) Center { get; }

    public GeometricEnergyDistribution(IReadOnlyList<WeightedEnergyPoint> samples,
        GeometricEnergyReference reference, EnergyRegion region)
        : this(samples, Reference(samples, reference), region, []) { }

    internal GeometricEnergyDistribution(IReadOnlyList<WeightedEnergyPoint> samples,
        (double X, double Y) center, EnergyRegion region, AiryEnergyComponent[] airy)
    {
        Validate(samples);
        if (!Enum.IsDefined(region) || !double.IsFinite(center.X) || !double.IsFinite(center.Y)) throw new ArgumentOutOfRangeException(nameof(region));
        Center = center; _region = region; _airy = airy;
        _distribution = new(samples.Select(p =>
        {
            var dx = Math.Abs(p.X - center.X); var dy = Math.Abs(p.Y - center.Y);
            var distance = region switch
            {
                EnergyRegion.XSlit => dx,
                EnergyRegion.YSlit => dy,
                EnergyRegion.Square => Math.Max(dx, dy),
                _ => double.Hypot(dx, dy)
            };
            return (distance, p.Weight);
        }));
    }

    public double FractionAtDistance(double distanceMicrometers)
    {
        if (!double.IsFinite(distanceMicrometers) || distanceMicrometers < 0) throw new ArgumentOutOfRangeException(nameof(distanceMicrometers));
        return _distribution.Cdf(distanceMicrometers) * DiffractionFraction(distanceMicrometers);
    }

    public double DistanceAtFraction(double fraction)
    {
        if (!double.IsFinite(fraction) || fraction is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(fraction));
        if (fraction == 0) return 0;
        if (_airy.Length == 0) return _distribution.Quantile(fraction);
        if (fraction == 1) throw new ArgumentOutOfRangeException(nameof(fraction), "Airy 光斑不能在有限半径内包住全部能量。");
        var high = Math.Max(1, _distribution.Maximum); var low = 0.0;
        for (var i = 0; FractionAtDistance(high) < fraction; i++)
        {
            if (i >= 100 || !double.IsFinite(high * 2)) throw new InvalidOperationException("未能找到能量分数的有限距离上界。");
            high *= 2;
        }
        for (var i = 0; i < 80; i++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var middle = low + (high - low) / 2;
            if (middle == low || middle == high) break;
            if (FractionAtDistance(middle) >= fraction) high = middle; else low = middle;
        }
        return high;
    }

    private double DiffractionFraction(double distance)
    {
        if (_airy.Length == 0) return 1;
        return _airy.Sum(a => a.Weight * AiryFraction(distance, a, _region)) / _airy.Sum(a => a.Weight);
    }

    internal static double AiryFraction(double distance, AiryEnergyComponent component, EnergyRegion region)
    {
        ComputationCancellation.ThrowIfCancellationRequested();
        if (distance == 0) return 0;
        double Radial(double r)
        {
            var value = DiffractionEncircledEnergyAnalysis.IdealAiryEncircledEnergy(r, component.Wavelength, component.FNumber);
            return double.IsFinite(value) ? value : throw new InvalidOperationException("Airy 能量计算产生非有限值。");
        }
        if (region == EnergyRegion.Circle) return Radial(distance);
        // Integrate the radial Airy CDF over polar angle: an infinite slit and
        // an axis-aligned square differ only in the angular integration bound.
        var angle = region == EnergyRegion.Square ? Math.PI / 4 : Math.PI / 2;
        var previous = double.NaN;
        for (var count = 32; count <= 16384; count *= 2)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var sum = 0.0;
            for (var i = 0; i < count; i++) sum += Radial(distance / Math.Cos(angle * (i + .5) / count));
            var current = sum / count;
            if (Math.Abs(current - previous) <= 1e-7) return current;
            previous = current;
        }
        throw new InvalidOperationException("Airy 狭缝/方框积分未在误差预算内收敛。");
    }

    internal static (double X, double Y) Reference(IReadOnlyList<WeightedEnergyPoint> samples, GeometricEnergyReference reference)
    {
        Validate(samples);
        if (reference == GeometricEnergyReference.Vertex) return (0, 0);
        if (reference == GeometricEnergyReference.Middle) return MinimumEnclosingCircle.Center(samples.Where(p => p.Weight > 0).Select(p => (p.X, p.Y)).ToArray());
        if (reference != GeometricEnergyReference.Centroid) throw new ArgumentOutOfRangeException(nameof(reference), "主光线参考需要光学系统。");
        var scale = samples.Max(p => p.Weight); var total = samples.Sum(p => p.Weight / scale);
        // Normalize before the coordinate product to avoid overflow of moments.
        var x = samples.Sum(p => p.X * ((p.Weight / scale) / total));
        var y = samples.Sum(p => p.Y * ((p.Weight / scale) / total));
        return (x, y);
    }

    private static void Validate(IReadOnlyList<WeightedEnergyPoint> samples)
    {
        ArgumentNullException.ThrowIfNull(samples); ComputationCancellation.ThrowIfCancellationRequested();
        if (samples.Count == 0 || samples.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y) || !double.IsFinite(p.Weight) || p.Weight < 0)
            || !samples.Any(p => p.Weight > 0)) throw new InvalidOperationException("能量样本必须具有有限坐标、非负权重及正总能量。");
    }
}

internal sealed class WeightedEnergyDistribution
{
    private readonly double[] _coordinates;
    private readonly double[] _cumulative;
    public double Maximum => _coordinates[^1];
    internal WeightedEnergyDistribution(IEnumerable<(double Coordinate, double Weight)> samples)
    {
        var items = samples.ToArray();
        if (items.Any(p => !double.IsFinite(p.Coordinate) || !double.IsFinite(p.Weight) || p.Weight < 0)) throw new InvalidOperationException("能量分布含非法样本。");
        var positive = items.Where(p => p.Weight > 0).OrderBy(p => p.Coordinate).ToArray();
        if (positive.Length == 0) throw new InvalidOperationException("没有正能量样本。");
        var scale = positive.Max(p => p.Weight);
        var groups = positive.GroupBy(p => p.Coordinate).Select(g => (Coordinate: g.Key, Weight: g.Sum(p => p.Weight / scale))).ToArray();
        _coordinates = groups.Select(p => p.Coordinate).ToArray(); _cumulative = new double[groups.Length];
        var sum = 0.0;
        for (var i = 0; i < groups.Length; i++) { ComputationCancellation.ThrowIfCancellationRequested(); sum += groups[i].Weight; _cumulative[i] = sum; }
        for (var i = 0; i < groups.Length; i++) _cumulative[i] /= sum;
    }
    internal double Cdf(double coordinate)
    {
        var index = Array.BinarySearch(_coordinates, coordinate);
        if (index < 0) index = ~index - 1;
        return index < 0 ? 0 : _cumulative[index];
    }
    internal double Quantile(double fraction)
    {
        var low = 0; var high = _coordinates.Length - 1;
        while (low < high) { var mid = low + (high - low) / 2; if (_cumulative[mid] >= fraction) high = mid; else low = mid + 1; }
        return _coordinates[low];
    }
}

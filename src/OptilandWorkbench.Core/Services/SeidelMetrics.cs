using OptilandWorkbench.Core.Capabilities;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Propagation;

namespace OptilandWorkbench.Core.Services;

public sealed record SeidelCoefficients(double Spherical, double Coma, double Astigmatism,
    double PetzvalFieldCurvature, double Distortion, double AxialColor, double LateralColor)
{
    public double[] ToArray() => [Spherical, Coma, Astigmatism, PetzvalFieldCurvature, Distortion, AxialColor, LateralColor];
    public double[] InWaves(double wavelengthMillimeters)
    {
        if (!double.IsFinite(wavelengthMillimeters) || wavelengthMillimeters <= 0)
            throw new ArgumentOutOfRangeException(nameof(wavelengthMillimeters));
        return [Spherical / (8 * wavelengthMillimeters), Coma / (2 * wavelengthMillimeters),
         Astigmatism / (2 * wavelengthMillimeters), PetzvalFieldCurvature / (4 * wavelengthMillimeters),
         Distortion / (2 * wavelengthMillimeters), AxialColor / (2 * wavelengthMillimeters), LateralColor / wavelengthMillimeters];
    }
}

public sealed record SeidelSurfaceContribution(int SurfaceNumber, SeidelCoefficients Coefficients);

public sealed record SeidelData(IReadOnlyList<SeidelSurfaceContribution> Surfaces, SeidelCoefficients Total,
    double ChiefSlopeObject, double ChiefSlopeImage, double MarginalSlopeObject, double MarginalSlopeImage,
    double OpticalInvariant, double ImageRefractiveIndex, double PetzvalCurvature);

/// <summary>Shared third-order spherical-surface coefficients and Petzval data.</summary>
public static class SeidelMetrics
{
    /// <summary>Third-order distortion: surface W311 in waves, or the total at paraxial focus in mm/percent.</summary>
    public static double Distortion(Optic optic, double wavelengthMicrometers, int surfaceNumber, bool absolute)
    {
        if (surfaceNumber < 0 || (surfaceNumber != 0 && !optic.SurfaceGroup.Items.Any(s => s.Number == surfaceNumber)))
            throw new ArgumentOutOfRangeException(nameof(surfaceNumber), "Surf 必须为实际表面编号，0 为全系统畸变。");
        if (surfaceNumber == 0 && optic.ImageSpaceAfocal)
            throw new NotSupportedException("DIST 全系统长度/百分比当前需要有焦像空间。");
        var data = Calculate(optic, wavelengthMicrometers);
        double value;
        if (surfaceNumber != 0)
        {
            value = data.Surfaces.Single(s => s.SurfaceNumber == surfaceNumber).Coefficients
                .InWaves(wavelengthMicrometers / 1000)[4];
        }
        else
        {
            // At paraxial focus H = -n' u' y'. TDIS = -S5/(2 n' u'),
            // hence percent = 100 TDIS/y' = 50 S5/H. These are Seidel
            // quantities, not actual-image chief-ray (DISG) distortion.
            var reducedSlope = data.ImageRefractiveIndex * data.MarginalSlopeImage;
            if (!double.IsFinite(reducedSlope) || reducedSlope == 0)
                throw new InvalidOperationException("DIST 全系统畸变需要有限近轴焦点。");
            if (absolute) value = -data.Total.Distortion / (2 * reducedSlope);
            else if (data.OpticalInvariant != 0) value = 50 * data.Total.Distortion / data.OpticalInvariant;
            else if (data.Total.Distortion == 0) value = 0;
            else throw new InvalidOperationException("近轴像高为零，DIST 百分比未定义。");
        }
        return double.IsFinite(value) ? value : throw new InvalidOperationException("DIST 未得到有限数值。");
    }

    public static SeidelData Calculate(Optic optic, double wavelengthMicrometers)
    {
        ValidateSystem(optic, wavelengthMicrometers, sphericalOnly: true);
        var surfaces = optic.SurfaceGroup.Items.ToArray();
        var wavelengths = optic.Wavelengths;
        var primary = wavelengths.FirstOrDefault(w => w.IsPrimary) ?? wavelengths[0];
        var marginal = optic.Paraxial.MarginalRay(wavelengthMicrometers);
        var chief = optic.Paraxial.ChiefRay(wavelengthMicrometers);
        RequireFiniteTrace(marginal, surfaces.Length); RequireFiniteTrace(chief, surfaces.Length);
        var stop = Array.FindIndex(surfaces, s => s.IsStop);
        if (stop > 0)
        {
            var primaryMarginal = optic.Paraxial.MarginalRay(primary.Micrometers);
            RequireFiniteTrace(primaryMarginal, surfaces.Length);
            var height = marginal.Heights[stop][0];
            if (height == 0 || primaryMarginal.Heights[stop][0] == 0)
                throw new InvalidOperationException("赛德尔归一化需要非零光阑边缘光线高度。");
            chief = Combine(chief, marginal, 1, -chief.Heights[stop][0] / height);
            marginal = Combine(marginal, marginal, primaryMarginal.Heights[stop][0] / height, 0);
        }
        var shortWave = wavelengths.Min(w => w.Nanometers);
        var longWave = wavelengths.Max(w => w.Nanometers);
        var contributions = new List<SeidelSurfaceContribution>();
        var total = new double[7];
        var invariant = 0.0;
        for (var i = 1; i < surfaces.Length; i++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var surface = surfaces[i]; var previous = surfaces[i - 1];
            var n0 = Index(previous, wavelengthMicrometers * 1000);
            var n1 = Index(surface, wavelengthMicrometers * 1000);
            var c = Curvature(surface);
            var y = marginal.Heights[i][0]; var ybar = chief.Heights[i][0];
            var u0 = marginal.Slopes[i - 1][0]; var u1 = marginal.Slopes[i][0];
            var ubar = chief.Slopes[i - 1][0];
            var a = n0 * (u0 + y * c); var abar = n0 * (ubar + ybar * c);
            var h = n0 * (ubar * y - u0 * ybar);
            if (Math.Abs(h) > Math.Abs(invariant)) invariant = h;
            var delta = u1 / n1 - u0 / n0;
            var s1 = -a * a * y * delta;
            var s2 = -a * abar * y * delta;
            var s3 = -abar * abar * y * delta;
            var s4 = -h * h * c * (1 / n1 - 1 / n0);
            // Stable at zero marginal incidence; equivalent to (abar/a)*(s3+s4).
            var s5 = -Math.Pow(abar, 3) * y * (1 / (n1 * n1) - 1 / (n0 * n0))
                + abar * ybar * c * (1 / n1 - 1 / n0) * (h + abar * y);
            var dispersion = (Index(surface, shortWave) - Index(surface, longWave)) / n1
                - (Index(previous, shortWave) - Index(previous, longWave)) / n0;
            var coefficients = new SeidelCoefficients(s1, s2, s3, s4, s5, -a * y * dispersion, -abar * y * dispersion);
            var values = coefficients.ToArray();
            for (var j = 0; j < total.Length; j++)
            {
                if (!double.IsFinite(values[j])) throw new InvalidOperationException("赛德尔系数不是有限数值。");
                total[j] += values[j];
            }
            contributions.Add(new(surface.Number, coefficients));
        }
        if (total.Any(v => !double.IsFinite(v))) throw new InvalidOperationException("赛德尔系数总和溢出。");
        return new(contributions.AsReadOnly(), new(total[0], total[1], total[2], total[3], total[4], total[5], total[6]),
            chief.Slopes[0][0], chief.Slopes[^1][0], marginal.Slopes[0][0], marginal.Slopes[^1][0], invariant,
            Index(surfaces[^1], wavelengthMicrometers * 1000), PetzvalCurvature(optic, wavelengthMicrometers));
    }

    public static double PetzvalCurvature(Optic optic, double wavelengthMicrometers)
    {
        ValidateSystem(optic, wavelengthMicrometers, sphericalOnly: false);
        var surfaces = optic.SurfaceGroup.Items;
        var wave = wavelengthMicrometers * 1000;
        var sum = 0.0;
        // Object and image radii are reference surfaces, not optical powers in this sum.
        for (var i = 1; i < surfaces.Count - 1; i++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var n0 = Index(surfaces[i - 1], wave); var n1 = Index(surfaces[i], wave);
            sum += Curvature(surfaces[i]) * (n1 - n0) / (n0 * n1);
        }
        var value = -Index(surfaces[^1], wave) * sum;
        return double.IsFinite(value) ? value : throw new InvalidOperationException("Petzval 曲率不是有限数值。");
    }

    private static void ValidateSystem(Optic optic, double wavelengthMicrometers, bool sphericalOnly)
    {
        ComputationCancellation.ThrowIfCancellationRequested();
        OpticCapabilityPreflight.EnsureSupported(optic, OpticCapabilityOperation.Analysis, "Seidel / Petzval");
        if (!double.IsFinite(wavelengthMicrometers) || wavelengthMicrometers <= 0 || optic.Wavelengths.Count == 0)
            throw new ArgumentOutOfRangeException(nameof(wavelengthMicrometers), "需要显式定义的有限正波长。");
        var surfaces = optic.SurfaceGroup.Items;
        if (surfaces.Count < 3) throw new InvalidOperationException("需要物面、实际光学面和像面。");
        foreach (var s in surfaces)
        {
            var frame = s.CoordinateSystem;
            if (frame.Origin.X != 0 || frame.Origin.Y != 0 || !double.IsFinite(frame.Origin.Z)
                || frame.RotationXDegrees != 0 || frame.RotationYDegrees != 0 || frame.RotationZDegrees != 0
                || s.IsReflective || s.InteractionModel is not RefractiveReflectiveInteractionModel
                || s.MaterialAfter.PropagationModel is not HomogeneousPropagationModel
                || s.Geometry is not (PlaneGeometry or StandardGeometry))
                throw new NotSupportedException("赛德尔/Petzval 当前支持共轴、未旋转、均匀介质的标准折射面；反射、GRIN、理想薄透镜、相位/衍射及多项式非球面尚未支持。");
            Index(s, wavelengthMicrometers * 1000);
            if (sphericalOnly && s.Geometry is StandardGeometry { Conic: not 0 })
                throw new NotSupportedException("赛德尔系数尚未实现圆锥与非球面修正，不能按球面值替代。");
        }
        if (!sphericalOnly) return;
        var image = surfaces[^1];
        if (!image.IsPlane || Index(image, wavelengthMicrometers * 1000) != Index(surfaces[^2], wavelengthMicrometers * 1000))
            throw new NotSupportedException("赛德尔系数当前需要不改变介质的平面像面。");
        for (var i = 1; i < surfaces.Count; i++)
        {
            if (i == 1 && ObjectConjugate.IsInfinite(surfaces[0])) continue;
            var thickness = surfaces[i - 1].Thickness;
            if (!double.IsFinite(thickness) || thickness < 0
                || Math.Abs(surfaces[i].CoordinateSystem.Origin.Z - surfaces[i - 1].CoordinateSystem.Origin.Z - thickness)
                    > 1e-9 * Math.Max(1, thickness))
                throw new NotSupportedException("赛德尔计算要求顶点间距与有限非负顺序厚度一致。");
        }
        var diameter = optic.Paraxial.EstimateEntrancePupilDiameter();
        if (!double.IsFinite(diameter) || diameter <= 0) throw new InvalidOperationException("需要有限正入瞳直径。");
        if (!ObjectConjugate.IsInfinite(surfaces[0])
            && Math.Abs(optic.Paraxial.EstimateEntrancePupilLocation(wavelengthMicrometers) - surfaces[0].CoordinateSystem.Origin.Z) <= 1e-15)
            throw new InvalidOperationException("有限物面与入瞳重合，不能定义近轴边缘光线。");
    }

    private static double Curvature(OpticalSurface surface)
    {
        var value = surface.IsPlane ? 0 : 1 / surface.Radius;
        return double.IsFinite(value) ? value : throw new InvalidOperationException("表面曲率不是有限数值。");
    }

    private static double Index(OpticalSurface surface, double nanometers)
    {
        if (!double.IsFinite(nanometers) || nanometers <= 0) throw new InvalidOperationException("波长必须为有限正数。");
        var value = surface.MaterialAfter.RefractiveIndex(nanometers);
        return double.IsFinite(value) && value > 0 ? value : throw new InvalidOperationException("折射率必须为有限正数。");
    }

    private static void RequireFiniteTrace(ParaxialTrace trace, int surfaceCount)
    {
        if (trace.Heights.Count != surfaceCount || trace.Slopes.Count != surfaceCount
            || trace.Heights.Concat(trace.Slopes).Any(row => row.Count != 1 || !double.IsFinite(row[0])))
            throw new InvalidOperationException("近轴追迹没有完整有限的逐面光线数据。");
    }

    private static ParaxialTrace Combine(ParaxialTrace first, ParaxialTrace second, double a, double b) => new(
        first.Heights.Select((row, i) => (IReadOnlyList<double>)new[] { a * row[0] + b * second.Heights[i][0] }).ToArray(),
        first.Slopes.Select((row, i) => (IReadOnlyList<double>)new[] { a * row[0] + b * second.Slopes[i][0] }).ToArray());
}

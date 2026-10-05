using System.Numerics;
using OptilandWorkbench.Core.Geometries;

namespace OptilandWorkbench.Core.Services;

public sealed record ParaxialGaussianBeamData(
    double Size, double Waist, double WaistToSurface, double PhaseRadius,
    double SemiDivergenceRadians, double RayleighRange);

public sealed partial class Paraxial
{
    /// <summary>Propagates the embedded Gaussian mode from surface 1 using the shared paraxial matrix.</summary>
    public ParaxialGaussianBeamData GaussianBeam(int surfaceNumber, double wavelengthMicrometers,
        double embeddedWaist, double surfaceOneToWaist, double mSquared = 1, bool useX = false)
    {
        EnsureTransmissiveOperandSystem(wavelengthMicrometers);
        if (!double.IsFinite(embeddedWaist) || embeddedWaist <= 0)
            throw new ArgumentOutOfRangeException(nameof(embeddedWaist), "输入嵌入模束腰半径必须为有限正数。");
        if (!double.IsFinite(surfaceOneToWaist))
            throw new ArgumentOutOfRangeException(nameof(surfaceOneToWaist), "面 1 到束腰的距离必须为有限数。");
        if (!double.IsFinite(mSquared) || mSquared < 1)
            throw new ArgumentOutOfRangeException(nameof(mSquared), "M² 必须为大于等于 1 的有限数。");
        var last = OperandSurfaceIndex(surfaceNumber);
        if (last == 0) throw new ArgumentOutOfRangeException(nameof(surfaceNumber), "高斯束数据需要面 1 或后续表面。");
        var surfaces = _optic.SurfaceGroup.Items;
        // This scalar shared matrix has identical X/Y power only for the supported
        // centered rotationally symmetric geometries. Never ignore an aspheric r² term.
        for (var i = 1; i <= last; i++)
        {
            var surface = surfaces[i];
            if (surface.Geometry is not (PlaneGeometry or StandardGeometry))
                throw new NotSupportedException("近轴高斯束当前支持共轴标准球面/圆锥、平面及理想薄透镜；非对称和非球面顶点功率尚未接入。");
            if (i < last && (!double.IsFinite(surface.Thickness) || surface.Thickness < 0))
                throw new InvalidOperationException("传播区间内厚度必须为有限非负数。");
            if (i < last && Math.Abs(surfaces[i + 1].CoordinateSystem.Origin.Z
                    - surface.CoordinateSystem.Origin.Z - surface.Thickness) > 1e-9 * Math.Max(1, surface.Thickness))
                throw new InvalidOperationException("表面坐标未与中心厚度同步，不能计算高斯光束。");
            RequireGaussianIndex(surface.MaterialAfter.RefractiveIndex(wavelengthMicrometers * 1000));
        }
        var inputIndex = RequireGaussianIndex(surfaces[0].MaterialAfter.RefractiveIndex(wavelengthMicrometers * 1000));
        var outputIndex = RequireGaussianIndex(surfaces[last].MaterialAfter.RefractiveIndex(wavelengthMicrometers * 1000));
        var lambda = wavelengthMicrometers / 1000;
        // W0 is the embedded TEM00 waist, not the already M²-scaled physical radius.
        var rayleighIn = Math.PI * inputIndex * embeddedWaist * embeddedWaist / lambda;
        var q = new Complex(-surfaceOneToWaist, rayleighIn);
        var matrix = TraceMatrix(1, last + 1, wavelengthMicrometers * 1000, includeTrailingTranslation: false);
        q = (matrix.A * q + matrix.B) / (matrix.C * q + matrix.D);
        var z = q.Real;
        var rayleigh = q.Imaginary;
        if (!double.IsFinite(z) || !double.IsFinite(rayleigh) || rayleigh <= 0)
            throw new InvalidOperationException("高斯束复参数不是有效的有限传播结果。");
        var scale = Math.Sqrt(mSquared);
        var waist = Math.Sqrt(lambda * rayleigh / (Math.PI * outputIndex));
        var size = waist * Complex.Abs(q) / rayleigh;
        var radius = z == 0 ? double.PositiveInfinity : z + rayleigh * (rayleigh / z);
        var divergence = waist / rayleigh;
        if (!double.IsFinite(waist * scale) || !double.IsFinite(size * scale)
            || !double.IsFinite(divergence * scale) || waist <= 0 || divergence <= 0)
            throw new InvalidOperationException("高斯束尺寸或发散角超出有限数值范围。");
        ComputationCancellation.ThrowIfCancellationRequested();
        return new(size * scale, waist * scale, z, radius, divergence * scale, rayleigh);
    }

    private static double RequireGaussianIndex(double index) => double.IsFinite(index) && index > 0 ? index
        : throw new InvalidOperationException("高斯束传播介质必须有有限正折射率。");
}

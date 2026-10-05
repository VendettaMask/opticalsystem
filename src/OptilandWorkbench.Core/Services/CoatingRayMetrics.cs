using System.Numerics;
using OptilandWorkbench.Core.Rays;
using OptilandWorkbench.Core.Raytrace;

namespace OptilandWorkbench.Core.Services;

public static class CoatingRayMetrics
{
    /// <summary>CODA data evaluated from one formal polarized trace, never a separate optical engine.</summary>
    public static double Value(PolarizedRayTraceResult result, int data)
    {
        ArgumentNullException.ThrowIfNull(result);
        var code = Math.Abs((long)data);
        if (code is >= 1 and <= 7)
        {
            var response = result.Interface ?? throw new InvalidOperationException("目标没有有效偏振界面。");
            var selected = data < 0 ? response.S : response.P;
            return code switch
            {
                1 => selected.Power.Reflectance,
                2 => selected.Power.Transmittance,
                3 => selected.Power.Absorptance,
                4 => (selected.PowerTransmission ?? throw new NotSupportedException("目标介质没有已实现的透射复振幅。")).Real,
                5 => (selected.PowerTransmission ?? throw new NotSupportedException("目标介质没有已实现的透射复振幅。")).Imaginary,
                6 => selected.Reflection.Real,
                _ => selected.Reflection.Imaginary
            };
        }
        var e = result.LocalField;
        return code switch
        {
            0 => result.PolarizedIntensity,
            8 => result.UnpolarizedIntensity,
            101 => e.X.Real,
            102 => e.X.Imaginary,
            103 => e.Y.Real,
            104 => e.Y.Imaginary,
            105 => e.Z.Real,
            106 => e.Z.Imaginary,
            110 => PolarizationMetrics.PhaseDifference(e),
            111 => ComponentPhase(e.X),
            112 => ComponentPhase(e.Y),
            113 => ComponentPhase(e.Z),
            121 => Ellipse(e).Major,
            122 => Ellipse(e).Minor,
            123 => Ellipse(e).AngleDegrees ?? throw new InvalidOperationException("圆偏振或零电场的椭圆主轴方向未定义。"),
            _ => throw new ArgumentOutOfRangeException(nameof(data), "CODA Data 未定义；支持 0..8、101..106、110..113、121..123 及其负值。")
        };
    }

    public static double ComponentPhase(Complex component) =>
        double.IsFinite(component.Magnitude) && component.Magnitude > 0 ? component.Phase
            : throw new InvalidOperationException("零分量或非有限电场没有定义的相位。");

    /// <summary>Axes of Re(E exp(i omega t)) projected into the surface XY plane; Z is not included.</summary>
    public static (double Major, double Minor, double? AngleDegrees) Ellipse(ComplexElectricField field)
    {
        if (!double.IsFinite(field.SquaredNorm)) throw new ArgumentException("椭圆需要有限电场。");
        var scale = Math.Max(field.X.Magnitude, field.Y.Magnitude);
        if (scale == 0) return (0, 0, null);
        var x = field.X / scale; var y = field.Y / scale;
        var xx = x.Magnitude * x.Magnitude; var yy = y.Magnitude * y.Magnitude;
        var cross = x * Complex.Conjugate(y);
        var difference = double.Hypot(xx - yy, 2 * cross.Real);
        var majorSquared = (xx + yy + difference) / 2;
        // det(M) = Im(Ex conj(Ey))^2 avoids subtracting two nearly equal eigenvalues.
        var minor = Math.Abs(cross.Imaginary) / Math.Sqrt(majorSquared) * scale;
        return (Math.Sqrt(majorSquared) * scale, minor,
            difference <= 1e-14 * (xx + yy) ? null : Math.Atan2(2 * cross.Real, xx - yy) * 90 / Math.PI);
    }
}

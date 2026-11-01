using OpticalSystemDesign.Core.Backend;

namespace OpticalSystemDesign.Core.Phase;

/// <summary>One convergent and one divergent construction beam on a plane hologram.</summary>
public sealed class TwoPointHologramPhaseProfile : IPhaseProfile
{
    public TwoPointHologramPhaseProfile(Vector3D first, Vector3D second, double constructionWavelengthNanometers, int order)
    {
        if (!double.IsFinite(first.X + first.Y + first.Z) || !double.IsFinite(second.X + second.Y + second.Z)
            || !double.IsFinite(constructionWavelengthNanometers) || constructionWavelengthNanometers <= 0)
            throw new ArgumentException("全息构建点和波长必须有效且有限。");
        if (order is < -1_000_000 or > 1_000_000) throw new ArgumentOutOfRangeException(nameof(order));
        First = first; Second = second; ConstructionWavelengthNanometers = constructionWavelengthNanometers; Order = order;
    }
    public string Kind => "two_point_hologram";
    public double Efficiency => 1;
    public Vector3D First { get; }
    public Vector3D Second { get; }
    public double ConstructionWavelengthNanometers { get; }
    public int Order { get; }
    private double Factor => -2 * Math.PI * Order / (ConstructionWavelengthNanometers * 1e-6);
    public double Phase(double x, double y, double wavelengthNanometers) => Factor *
        (Distance(x, y, First) + Distance(x, y, Second));
    public (double Dx, double Dy) Gradient(double x, double y, double wavelengthNanometers)
    {
        var a = Distance(x, y, First); var b = Distance(x, y, Second);
        if (a == 0 || b == 0) throw new ArithmeticException("构建点处的全息相位梯度无定义。");
        return (Factor * ((x - First.X) / a + (x - Second.X) / b),
            Factor * ((y - First.Y) / a + (y - Second.Y) / b));
    }
    public double ParaxialGradient(double y, double wavelengthNanometers)
    {
        var a = Distance(0, 0, First); var b = Distance(0, 0, Second);
        if (a == 0 || b == 0) throw new ArithmeticException("构建点处的全息一阶曲率无定义。");
        return Factor * y * ((1 - First.Y * First.Y / (a * a)) / a + (1 - Second.Y * Second.Y / (b * b)) / b);
    }
    public IPhaseProfile Clone() => new TwoPointHologramPhaseProfile(First, Second, ConstructionWavelengthNanometers, Order);
    private static double Distance(double x, double y, Vector3D source) =>
        Math.Sqrt((x - source.X) * (x - source.X) + (y - source.Y) * (y - source.Y) + source.Z * source.Z);
}

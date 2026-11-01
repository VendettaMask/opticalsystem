namespace OpticalSystemDesign.Core.Phase;

/// <summary>Order * sum A_i (r/R)^(2i), with A_i in radians and R in mm.</summary>
public sealed class NormalizedRadialPhaseProfile : IPhaseProfile
{
    public NormalizedRadialPhaseProfile(double normalizationRadius, IReadOnlyList<double> coefficients, int order = 1)
    {
        if (!double.IsFinite(normalizationRadius) || normalizationRadius <= 0) throw new ArgumentOutOfRangeException(nameof(normalizationRadius));
        if (coefficients.Count > 240 || coefficients.Any(c => !double.IsFinite(c))) throw new ArgumentException("最多支持 240 个有限相位系数。");
        if (order is < -1_000_000 or > 1_000_000) throw new ArgumentOutOfRangeException(nameof(order));
        NormalizationRadius = normalizationRadius; Coefficients = Array.AsReadOnly(coefficients.ToArray()); Order = order;
    }
    public string Kind => "normalized_radial";
    public double Efficiency => 1;
    public double NormalizationRadius { get; }
    public IReadOnlyList<double> Coefficients { get; }
    public int Order { get; }
    public double Phase(double x, double y, double wavelengthNanometers)
    {
        var u = x / NormalizationRadius; var v = y / NormalizationRadius; var r2 = u * u + v * v;
        var sum = 0d; for (var i = Coefficients.Count - 1; i >= 0; i--) sum = (sum + Coefficients[i]) * r2;
        return Order * sum;
    }
    public (double Dx, double Dy) Gradient(double x, double y, double wavelengthNanometers)
    {
        var u = x / NormalizationRadius; var v = y / NormalizationRadius; var r2 = u * u + v * v;
        var sum = 0d; for (var i = Coefficients.Count - 1; i >= 0; i--) sum = sum * r2 + (i + 1) * Coefficients[i];
        var factor = 2 * Order * sum / NormalizationRadius; return (factor * u, factor * v);
    }
    public double ParaxialGradient(double y, double wavelengthNanometers) => Coefficients.Count == 0 ? 0
        : 2 * Order * Coefficients[0] * (y / NormalizationRadius) / NormalizationRadius;
    public IPhaseProfile Clone() => new NormalizedRadialPhaseProfile(NormalizationRadius, Coefficients, Order);
}

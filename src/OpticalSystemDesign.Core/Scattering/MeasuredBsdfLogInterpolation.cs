namespace OpticalSystemDesign.Core.Scattering;

/// <summary>Explicit interpolation convention, not a measurement uncertainty or power normalization.</summary>
public sealed record MeasuredBsdfLogInterpolation(double ScalePerSteradian, string Method = "Log1pBilinearCosine/1")
{
    internal MeasuredBsdfLogInterpolation Freeze()
    {
        if (Method != "Log1pBilinearCosine/1" || !double.IsFinite(ScalePerSteradian) || ScalePerSteradian is < 1e-12 or > 1e12)
            throw new InvalidDataException("BSDF 对数插值须有明确的方法和有限正尺度。");
        return this;
    }
    internal double Encode(double value)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1e12) throw new InvalidDataException("BSDF 对数插值节点必须是有界非负辐射度。");
        return Log1p(value / ScalePerSteradian);
    }
    internal double Interpolate(double a, double b, double t)
    {
        if (t == 0) return a; if (t == 1) return b;
        return ScalePerSteradian * Expm1(Encode(a) * (1 - t) + Encode(b) * t);
    }
    internal static double Log1p(double x)
    {
        if (Math.Abs(x) > 1e-4) return Math.Log(1 + x);
        double term = x, sum = x; for (int i = 2; i <= 12; i++) { term *= -x; sum += term / i; } return sum;
    }
    internal static double Expm1(double x)
    {
        if (Math.Abs(x) > 1e-4) return Math.Exp(x) - 1;
        double term = x, sum = x; for (int i = 2; i <= 12; i++) { term *= x / i; sum += term; } return sum;
    }
    internal static double ExprelMinusOne(double x)
    {
        if (Math.Abs(x) > 1e-4) return Expm1(x) / x - 1;
        double term = x / 2, sum = term; for (int i = 3; i <= 12; i++) { term *= x / i; sum += term; } return sum;
    }
}

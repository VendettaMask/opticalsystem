namespace OptilandWorkbench.Core.Apodization;

public interface IApodizationModel
{
    string Kind { get; }

    double Intensity(double normalizedPupilX, double normalizedPupilY);

    IApodizationModel Clone();
}

public sealed class UniformApodization : IApodizationModel
{
    public string Kind => "uniform";

    public double Intensity(double normalizedPupilX, double normalizedPupilY) => 1.0;

    public IApodizationModel Clone() => new UniformApodization();
}

public sealed class GaussianApodization : IApodizationModel
{
    public GaussianApodization(double sigma = 1.0)
    {
        Sigma = ApodizationValidation.Positive(sigma, nameof(sigma));
    }

    public string Kind => "gaussian";

    public double Sigma { get; }

    public double Intensity(double normalizedPupilX, double normalizedPupilY)
    {
        var radiusSquared = (normalizedPupilX * normalizedPupilX) + (normalizedPupilY * normalizedPupilY);
        return Math.Exp(-radiusSquared / (2 * Sigma * Sigma));
    }

    public IApodizationModel Clone() => new GaussianApodization(Sigma);
}

public sealed class CosineSquaredApodization : IApodizationModel
{
    public CosineSquaredApodization(double radius = 1.0)
    {
        Radius = ApodizationValidation.Positive(radius, nameof(radius));
    }

    public string Kind => "cosine_squared";

    public double Radius { get; }

    public double Intensity(double normalizedPupilX, double normalizedPupilY)
    {
        var radius = ApodizationValidation.Radius(normalizedPupilX, normalizedPupilY);
        if (radius >= Radius)
        {
            return 0;
        }

        var cosine = Math.Cos(Math.PI * radius / (2 * Radius));
        return cosine * cosine;
    }

    public IApodizationModel Clone() => new CosineSquaredApodization(Radius);
}

public sealed class HannApodization : IApodizationModel
{
    public HannApodization(double diameter = 2.0)
    {
        Diameter = ApodizationValidation.Positive(diameter, nameof(diameter));
    }

    public string Kind => "hann";

    public double Diameter { get; }

    public double Intensity(double normalizedPupilX, double normalizedPupilY)
    {
        var radius = ApodizationValidation.Radius(normalizedPupilX, normalizedPupilY);
        if (radius >= Diameter / 2)
        {
            return 0;
        }

        return 0.5 * (1 - Math.Cos(2 * Math.PI * radius / Diameter));
    }

    public IApodizationModel Clone() => new HannApodization(Diameter);
}

public sealed class PolynomialApodization : IApodizationModel
{
    public PolynomialApodization(double radius = 1.0, double power = 1.0)
    {
        Radius = ApodizationValidation.Positive(radius, nameof(radius));
        Power = ApodizationValidation.NonNegative(power, nameof(power));
    }

    public string Kind => "polynomial";

    public double Radius { get; }

    public double Power { get; }

    public double Intensity(double normalizedPupilX, double normalizedPupilY)
    {
        var radius = ApodizationValidation.Radius(normalizedPupilX, normalizedPupilY);
        if (radius >= Radius)
        {
            return 0;
        }

        var normalizedRadius = radius / Radius;
        return Math.Pow(1 - (normalizedRadius * normalizedRadius), Power);
    }

    public IApodizationModel Clone() => new PolynomialApodization(Radius, Power);
}

public sealed class SuperGaussianApodization : IApodizationModel
{
    public SuperGaussianApodization(double width = 1.0, double exponent = 2.0)
    {
        Width = ApodizationValidation.Positive(width, nameof(width));
        if (!double.IsFinite(exponent) || exponent < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(exponent), "Exponent must be finite and at least 2.");
        }

        Exponent = exponent;
    }

    public string Kind => "super_gaussian";

    public double Width { get; }

    public double Exponent { get; }

    public double Intensity(double normalizedPupilX, double normalizedPupilY)
    {
        var radius = ApodizationValidation.Radius(normalizedPupilX, normalizedPupilY);
        return Math.Exp(-Math.Pow(radius / Width, Exponent));
    }

    public IApodizationModel Clone() => new SuperGaussianApodization(Width, Exponent);
}

public sealed class TukeyApodization : IApodizationModel
{
    public TukeyApodization(double radius = 1.0, double alpha = 0.5)
    {
        Radius = ApodizationValidation.Positive(radius, nameof(radius));
        if (!double.IsFinite(alpha) || alpha < 0 || alpha > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(alpha), "Alpha must be finite and in [0, 1].");
        }

        Alpha = alpha;
    }

    public string Kind => "tukey";

    public double Radius { get; }

    public double Alpha { get; }

    public double Intensity(double normalizedPupilX, double normalizedPupilY)
    {
        var radius = ApodizationValidation.Radius(normalizedPupilX, normalizedPupilY);
        var flatRegionEnd = Radius * (1 - (Alpha / 2));
        if (radius <= flatRegionEnd)
        {
            return 1;
        }

        if (radius >= Radius || Alpha == 0)
        {
            return 0;
        }

        var argument = Math.PI * (radius - flatRegionEnd) / (Radius * Alpha / 2);
        return 0.5 * (1 + Math.Cos(argument));
    }

    public IApodizationModel Clone() => new TukeyApodization(Radius, Alpha);
}

internal static class ApodizationValidation
{
    public static double Positive(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Value must be positive and finite.");
        }

        return value;
    }

    public static double NonNegative(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Value must be non-negative and finite.");
        }

        return value;
    }

    public static double Radius(double x, double y)
    {
        return Math.Sqrt((x * x) + (y * y));
    }
}

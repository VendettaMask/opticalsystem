namespace OptilandWorkbench.Core.Apodization;

public enum ZemaxApodizationType
{
    Uniform = 0,
    Gaussian = 1,
    CosineCubed = 2
}

/// <summary>Pupil illumination using the Zemax amplitude-factor convention.</summary>
public sealed class ZemaxApodization : IApodizationModel
{
    public ZemaxApodization(ZemaxApodizationType type, double factor)
    {
        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type));
        }

        Type = type;
        Factor = ApodizationValidation.NonNegative(factor, nameof(factor));
    }

    public string Kind => "zemax_pupil";

    public ZemaxApodizationType Type { get; }

    public double Factor { get; }

    public double Intensity(double normalizedPupilX, double normalizedPupilY)
    {
        if (Type == ZemaxApodizationType.CosineCubed)
        {
            throw new InvalidOperationException("Cosine-cubed apodization requires the entrance-pupil marginal slope.");
        }

        return Intensity(normalizedPupilX, normalizedPupilY, 0);
    }

    public double Intensity(double normalizedPupilX, double normalizedPupilY, double marginalSlope)
    {
        var radiusSquared = normalizedPupilX * normalizedPupilX + normalizedPupilY * normalizedPupilY;
        return Type switch
        {
            ZemaxApodizationType.Uniform => 1,
            // Zemax defines amplitude exp(-G*rho^2); rays carry intensity.
            ZemaxApodizationType.Gaussian => Math.Exp(-2 * Factor * radiusSquared),
            ZemaxApodizationType.CosineCubed => radiusSquared == 0
                ? 1
                : Math.Pow(1 + marginalSlope * marginalSlope * radiusSquared, -1.5),
            _ => throw new InvalidOperationException("Unknown Zemax apodization type.")
        };
    }

    public IApodizationModel Clone() => new ZemaxApodization(Type, Factor);
}

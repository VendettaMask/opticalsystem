namespace OptilandWorkbench.Core.Apertures;

public enum ApertureKind
{
    EntrancePupilDiameter,
    FNumber,
    NumericalAperture
}

public sealed class SystemAperture
{
    public ApertureKind Kind { get; set; } = ApertureKind.EntrancePupilDiameter;

    public double Value { get; set; } = 14.0;

    public double Diameter(double fallbackDiameter)
    {
        return Kind switch
        {
            ApertureKind.EntrancePupilDiameter => Math.Max(0.001, Value),
            ApertureKind.FNumber => Math.Max(0.001, fallbackDiameter),
            ApertureKind.NumericalAperture => Math.Max(0.001, fallbackDiameter),
            _ => Math.Max(0.001, fallbackDiameter)
        };
    }

    public SystemAperture Clone()
    {
        return new SystemAperture { Kind = Kind, Value = Value };
    }
}

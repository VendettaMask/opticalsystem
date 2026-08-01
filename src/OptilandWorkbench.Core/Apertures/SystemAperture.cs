namespace OptilandWorkbench.Core.Apertures;

public enum ApertureKind
{
    EntrancePupilDiameter,
    FNumber,
    NumericalAperture,
    FloatByStopSize
}

public sealed class SystemAperture : OptilandWorkbench.Core.Domain.NotifyObject
{
    private ApertureKind _kind = ApertureKind.EntrancePupilDiameter;
    private double _value = 14.0;
    private bool _objectSpaceTelecentric;

    public ApertureKind Kind
    {
        get => _kind;
        set => SetProperty(ref _kind, value);
    }

    public double Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }

    public bool ObjectSpaceTelecentric
    {
        get => _objectSpaceTelecentric;
        set => SetProperty(ref _objectSpaceTelecentric, value);
    }

    public double Diameter(double fallbackDiameter)
    {
        return Kind switch
        {
            ApertureKind.EntrancePupilDiameter => Math.Max(0.001, Value),
            ApertureKind.FNumber => Math.Max(0.001, fallbackDiameter),
            ApertureKind.NumericalAperture => Math.Max(0.001, fallbackDiameter),
            ApertureKind.FloatByStopSize => Math.Max(0.001, fallbackDiameter),
            _ => Math.Max(0.001, fallbackDiameter)
        };
    }

    public SystemAperture Clone()
    {
        return new SystemAperture
        {
            Kind = Kind,
            Value = Value,
            ObjectSpaceTelecentric = ObjectSpaceTelecentric
        };
    }
}

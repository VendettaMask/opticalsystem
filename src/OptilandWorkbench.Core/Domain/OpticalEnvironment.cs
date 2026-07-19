namespace OptilandWorkbench.Core.Domain;

public sealed class OpticalEnvironment
{
    private double _temperatureCelsius = 20.0;
    private double _pressureAtmospheres = 1.0;

    public bool MatchRefractiveIndexData { get; set; } = true;

    public double TemperatureCelsius
    {
        get => _temperatureCelsius;
        set => _temperatureCelsius = double.IsFinite(value) ? value : 20.0;
    }

    public double PressureAtmospheres
    {
        get => _pressureAtmospheres;
        set => _pressureAtmospheres = double.IsFinite(value) && value > 0 ? value : 1.0;
    }
}

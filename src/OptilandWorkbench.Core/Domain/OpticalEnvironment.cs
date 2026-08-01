namespace OptilandWorkbench.Core.Domain;

public sealed class OpticalEnvironment : NotifyObject
{
    private bool _matchRefractiveIndexData = true;
    private double _temperatureCelsius = 20.0;
    private double _pressureAtmospheres = 1.0;

    public bool MatchRefractiveIndexData
    {
        get => _matchRefractiveIndexData;
        set => SetProperty(ref _matchRefractiveIndexData, value);
    }

    public double TemperatureCelsius
    {
        get => _temperatureCelsius;
        set => SetProperty(ref _temperatureCelsius, double.IsFinite(value) ? value : 20.0);
    }

    public double PressureAtmospheres
    {
        get => _pressureAtmospheres;
        set => SetProperty(
            ref _pressureAtmospheres,
            double.IsFinite(value) && value > 0 ? value : 1.0);
    }
}

namespace OptilandWorkbench.Core.Domain;

public sealed class Wavelength : NotifyObject
{
    private string _label = "d";
    private double _nanometers = 587.6;
    private double _weight = 1.0;
    private bool _isPrimary = true;

    public string Label
    {
        get => _label;
        set => SetProperty(ref _label, value);
    }

    public double Nanometers
    {
        get => _nanometers;
        set => SetProperty(ref _nanometers, Math.Max(1, value));
    }

    public double Weight
    {
        get => _weight;
        set => SetProperty(ref _weight, Math.Max(0, value));
    }

    public bool IsPrimary
    {
        get => _isPrimary;
        set => SetProperty(ref _isPrimary, value);
    }

    public Wavelength Clone()
    {
        return new Wavelength
        {
            Label = Label,
            Nanometers = Nanometers,
            Weight = Weight,
            IsPrimary = IsPrimary
        };
    }

    public override string ToString()
    {
        return $"{Label} {Nanometers:0.#} nm";
    }
}

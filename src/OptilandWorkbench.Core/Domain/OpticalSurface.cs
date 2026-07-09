namespace OptilandWorkbench.Core.Domain;

public sealed class OpticalSurface : NotifyObject
{
    private int _number;
    private string _label = "Surface";
    private double _radius;
    private double _thickness = 1.0;
    private string _material = "Air";
    private string _coating = "None";
    private double _semiDiameter = 10.0;
    private double _conic;
    private bool _isStop;

    public int Number
    {
        get => _number;
        set => SetProperty(ref _number, value);
    }

    public string Label
    {
        get => _label;
        set => SetProperty(ref _label, value);
    }

    public double Radius
    {
        get => _radius;
        set => SetProperty(ref _radius, value);
    }

    public double Thickness
    {
        get => _thickness;
        set => SetProperty(ref _thickness, Math.Max(0, value));
    }

    public string Material
    {
        get => _material;
        set => SetProperty(ref _material, string.IsNullOrWhiteSpace(value) ? "Air" : value.Trim());
    }

    public string Coating
    {
        get => _coating;
        set => SetProperty(ref _coating, string.IsNullOrWhiteSpace(value) ? "None" : value.Trim());
    }

    public double SemiDiameter
    {
        get => _semiDiameter;
        set => SetProperty(ref _semiDiameter, Math.Max(0.1, value));
    }

    public double Conic
    {
        get => _conic;
        set => SetProperty(ref _conic, value);
    }

    public bool IsStop
    {
        get => _isStop;
        set => SetProperty(ref _isStop, value);
    }

    public bool IsPlane => Math.Abs(Radius) < 1e-9;

    public OpticalSurface Clone()
    {
        return new OpticalSurface
        {
            Number = Number,
            Label = Label,
            Radius = Radius,
            Thickness = Thickness,
            Material = Material,
            Coating = Coating,
            SemiDiameter = SemiDiameter,
            Conic = Conic,
            IsStop = IsStop
        };
    }

    public override string ToString()
    {
        return $"{Number}: {Label}";
    }
}

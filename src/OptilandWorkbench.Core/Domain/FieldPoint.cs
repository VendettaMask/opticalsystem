namespace OptilandWorkbench.Core.Domain;

public sealed class FieldPoint : NotifyObject
{
    private string _label = "Field";
    private double _xAngleDegrees;
    private double _yAngleDegrees;
    private double _weight = 1.0;

    public string Label
    {
        get => _label;
        set => SetProperty(ref _label, value);
    }

    public double XAngleDegrees
    {
        get => _xAngleDegrees;
        set => SetProperty(ref _xAngleDegrees, value);
    }

    public double YAngleDegrees
    {
        get => _yAngleDegrees;
        set => SetProperty(ref _yAngleDegrees, value);
    }

    public double Weight
    {
        get => _weight;
        set => SetProperty(ref _weight, Math.Max(0, value));
    }

    public FieldPoint Clone()
    {
        return new FieldPoint
        {
            Label = Label,
            XAngleDegrees = XAngleDegrees,
            YAngleDegrees = YAngleDegrees,
            Weight = Weight
        };
    }

    public override string ToString()
    {
        return $"{Label} ({YAngleDegrees:0.###} deg)";
    }
}

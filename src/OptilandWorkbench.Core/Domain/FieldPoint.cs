namespace OptilandWorkbench.Core.Domain;

public sealed class FieldPoint : NotifyObject
{
    private string _label = "Field";
    private double _x;
    private double _y;
    private double _weight = 1.0;
    private double _vignetteFactorX;
    private double _vignetteFactorY;

    public string Label
    {
        get => _label;
        set => SetProperty(ref _label, value);
    }

    public double X
    {
        get => _x;
        set
        {
            if (SetProperty(ref _x, value))
            {
                RaisePropertyChanged(nameof(XAngleDegrees));
            }
        }
    }

    public double Y
    {
        get => _y;
        set
        {
            if (SetProperty(ref _y, value))
            {
                RaisePropertyChanged(nameof(YAngleDegrees));
            }
        }
    }

    public double XAngleDegrees
    {
        get => X;
        set => X = value;
    }

    public double YAngleDegrees
    {
        get => Y;
        set => Y = value;
    }

    public double Weight
    {
        get => _weight;
        set => SetProperty(ref _weight, Math.Max(0, value));
    }

    public double VignetteFactorX
    {
        get => _vignetteFactorX;
        set => SetProperty(ref _vignetteFactorX, value);
    }

    public double VignetteFactorY
    {
        get => _vignetteFactorY;
        set => SetProperty(ref _vignetteFactorY, value);
    }

    public FieldPoint Clone()
    {
        return new FieldPoint
        {
            Label = Label,
            X = X,
            Y = Y,
            Weight = Weight,
            VignetteFactorX = VignetteFactorX,
            VignetteFactorY = VignetteFactorY
        };
    }

    public override string ToString()
    {
        return $"{Label} ({Y:0.###})";
    }
}

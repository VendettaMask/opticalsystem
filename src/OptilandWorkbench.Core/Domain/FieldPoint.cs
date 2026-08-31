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
            NumericParameterGuard.RequireFinite(value, nameof(X));
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
            NumericParameterGuard.RequireFinite(value, nameof(Y));
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
        set => SetProperty(ref _weight, NumericParameterGuard.ClampNonNegativeFinite(value, nameof(Weight)));
    }

    public double VignetteFactorX
    {
        get => _vignetteFactorX;
        set => SetProperty(ref _vignetteFactorX, NumericParameterGuard.RequireFinite(value, nameof(VignetteFactorX)));
    }

    public double VignetteFactorY
    {
        get => _vignetteFactorY;
        set => SetProperty(ref _vignetteFactorY, NumericParameterGuard.RequireFinite(value, nameof(VignetteFactorY)));
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

namespace OptilandWorkbench.Core.Domain;

public sealed class FieldPoint : NotifyObject
{
    private string _label = "Field";
    private double _x;
    private double _y;
    private double _weight = 1.0;
    private double _vignetteFactorX;
    private double _vignetteFactorY;
    private double _vignetteDecenterX;
    private double _vignetteDecenterY;
    private double _vignetteAngleDegrees;

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

    public double VignetteDecenterX
    {
        get => _vignetteDecenterX;
        set => SetProperty(ref _vignetteDecenterX, NumericParameterGuard.RequireFinite(value, nameof(VignetteDecenterX)));
    }

    public double VignetteDecenterY
    {
        get => _vignetteDecenterY;
        set => SetProperty(ref _vignetteDecenterY, NumericParameterGuard.RequireFinite(value, nameof(VignetteDecenterY)));
    }

    public double VignetteAngleDegrees
    {
        get => _vignetteAngleDegrees;
        set => SetProperty(ref _vignetteAngleDegrees, NumericParameterGuard.RequireFinite(value, nameof(VignetteAngleDegrees)));
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
            VignetteFactorY = VignetteFactorY,
            VignetteDecenterX = VignetteDecenterX,
            VignetteDecenterY = VignetteDecenterY,
            VignetteAngleDegrees = VignetteAngleDegrees
        };
    }

    public override string ToString()
    {
        return $"{Label} ({Y:0.###})";
    }
}

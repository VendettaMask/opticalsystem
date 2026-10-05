namespace OptilandWorkbench.Core.Rays;

/// <summary>Immutable system Jones input. CODA always uses it, even when Unpolarized is true.</summary>
public sealed record SystemPolarization(bool Unpolarized = true, double Jx = 1, double Jy = 0,
    double XPhaseDegrees = 0, double YPhaseDegrees = 0,
    PolarizationReferenceAxis ReferenceAxis = PolarizationReferenceAxis.X)
{
    public JonesInputState Input => new(Jx, Jy, XPhaseDegrees, YPhaseDegrees, ReferenceAxis);

    public void Validate()
    {
        if (!double.IsFinite(Jx) || !double.IsFinite(Jy) || Jx < 0 || Jy < 0 || Math.Max(Jx, Jy) <= 0
            || !double.IsFinite(XPhaseDegrees) || !double.IsFinite(YPhaseDegrees) || !Enum.IsDefined(ReferenceAxis))
            throw new ArgumentException("系统偏振需要非负有限 Jx/Jy（不能全零）、有限相位及有效参考轴。");
    }
}

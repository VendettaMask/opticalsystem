namespace OpticalSystemDesign.Application.Contracts;

public static class ToleranceCompensatorKinds
{
    public static bool IsElement(this ToleranceCompensatorKind kind) => kind is
        ToleranceCompensatorKind.ElementDecenterX or ToleranceCompensatorKind.ElementDecenterY
        or ToleranceCompensatorKind.ElementTiltX or ToleranceCompensatorKind.ElementTiltY;
}

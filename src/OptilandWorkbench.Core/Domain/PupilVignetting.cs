namespace OptilandWorkbench.Core.Domain;

/// <summary>Apparent-pupil compression, shift, then tangential rotation in normalized pupil units.</summary>
public readonly record struct PupilVignetting(double DecenterX, double DecenterY, double CompressionX, double CompressionY, double AngleDegrees)
{
    public static PupilVignetting Identity => default;

    public static PupilVignetting FromField(FieldPoint field) => new(field.VignetteDecenterX,
        field.VignetteDecenterY, field.VignetteFactorX, field.VignetteFactorY, field.VignetteAngleDegrees);

    public (double X, double Y) Transform(double pupilX, double pupilY)
    {
        var x = DecenterX + pupilX * (1 - CompressionX);
        var y = DecenterY + pupilY * (1 - CompressionY);
        var (sin, cos) = Math.SinCos((AngleDegrees % 360) * (Math.PI / 180));
        var result = (X: x * cos - y * sin, Y: x * sin + y * cos);
        if (!double.IsFinite(result.X) || !double.IsFinite(result.Y))
            throw new InvalidOperationException("渐晕变换后的瞳孔坐标不是有限数值。");
        return result;
    }

    public static void Clear(FieldPoint field)
    {
        field.VignetteFactorX = 0; field.VignetteFactorY = 0;
        field.VignetteDecenterX = 0; field.VignetteDecenterY = 0; field.VignetteAngleDegrees = 0;
    }
}

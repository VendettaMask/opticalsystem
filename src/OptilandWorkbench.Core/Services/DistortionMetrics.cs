using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Core.Services;

/// <summary>Generalized image distortion using the formal grid-distortion reference and ray tracer.</summary>
public static class DistortionMetrics
{
    /// <summary>Chief-ray distortion relative to an explicitly supplied grid-reference matrix.</summary>
    public static double WithReferenceMatrix(Optic optic, double wavelengthMicrometers, int referenceField,
        int fieldNumber, int component, double a, double b, double c, double d)
    {
        if (component is < 0 or > 2) throw new ArgumentOutOfRangeException(nameof(component), "Data 必须为 0/1/2（径向/X/Y）。");
        if (!double.IsFinite(a) || !double.IsFinite(b) || !double.IsFinite(c) || !double.IsFinite(d))
            throw new ArgumentOutOfRangeException(nameof(a), "A/B/C/D 必须全部为有限数值。");
        var working = Prepare(optic, wavelengthMicrometers, referenceField);
        if (fieldNumber < 1 || fieldNumber > working.Fields.Count)
            throw new ArgumentOutOfRangeException(nameof(fieldNumber), "DISA 的 Field 必须为已定义的正视场编号。");
        var reference = referenceField == 0 ? new FieldPoint() : working.Fields[referenceField - 1];
        var field = working.Fields[fieldNumber - 1];
        var origin = AnalysisTrace.ToDistortionLinearField(working, reference.X, reference.Y, "f-tan");
        var linear = AnalysisTrace.ToDistortionLinearField(working, field.X, field.Y, "f-tan");
        var imageOrigin = AnalysisTrace.TraceChiefAtLinearField(working, origin.X, origin.Y, wavelengthMicrometers,
            "f-tan", requireUnvignetted: true);
        var image = AnalysisTrace.TraceChiefAtLinearField(working, linear.X, linear.Y, wavelengthMicrometers,
            "f-tan", requireUnvignetted: true);
        // An explicit matrix requires no inverse or fitted derivative. Rank-one
        // matrices remain useful for a one-dimensional scan.
        var predictedX = a * (linear.X - origin.X) + b * (linear.Y - origin.Y);
        var predictedY = c * (linear.X - origin.X) + d * (linear.Y - origin.Y);
        var actualX = image.X - imageOrigin.X;
        var actualY = image.Y - imageOrigin.Y;
        if (linear == origin) return 0;
        if (component == 0)
            return Percent(SignedVectorDifference(actualX, actualY, predictedX, predictedY), double.Hypot(predictedX, predictedY));
        // Component percentages use the corresponding signed predicted height.
        // Native DISA component normalization still requires captured validation.
        return component == 1 ? Percent(actualX - predictedX, predictedX) : Percent(actualY - predictedY, predictedY);
    }

    internal static double SignedVectorDifference(double actualX, double actualY, double predictedX, double predictedY)
    {
        RequireFinite(actualX); RequireFinite(actualY); RequireFinite(predictedX); RequireFinite(predictedY);
        var deviation = double.Hypot(actualX - predictedX, actualY - predictedY);
        if (double.Hypot(actualX, actualY) < double.Hypot(predictedX, predictedY)) deviation = -deviation;
        return RequireFinite(deviation);
    }

    private static double Percent(double deviation, double reference)
    {
        if (!double.IsFinite(reference) || Math.Abs(reference) <= 1e-30)
            throw new InvalidOperationException("预测像高为零或非有限值，百分比畸变未定义。");
        return RequireFinite(100 * (deviation / reference));
    }

    public static double ReferenceCoefficient(Optic optic, double wavelengthMicrometers, int referenceField, int component)
    {
        if (component is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(component), "Data 必须为 0..3（A/B/C/D）。");
        var working = Prepare(optic, wavelengthMicrometers, referenceField);
        var mapping = Mapping(working, wavelengthMicrometers, referenceField);
        return RequireFinite(component switch { 0 => mapping.M00, 1 => mapping.M01, 2 => mapping.M10, _ => mapping.M11 });
    }

    public static double Generalized(Optic optic, double wavelengthMicrometers, int referenceField,
        double hx, double hy, double px, double py, bool absolute)
    {
        var working = Prepare(optic, wavelengthMicrometers, referenceField);
        return GeneralizedPrepared(working, wavelengthMicrometers, referenceField, hx, hy, px, py, absolute);
    }

    public static double AtField(Optic optic, double wavelengthMicrometers, int fieldNumber, bool absolute)
    {
        var working = Prepare(optic, wavelengthMicrometers, fieldNumber);
        var field = fieldNumber == 0
            ? working.Fields.MaxBy(f => (f.X * f.X) + (f.Y * f.Y))!
            : working.Fields[fieldNumber - 1];
        var normalized = FieldCoordinates.Normalize(working.Fields, field.X, field.Y);
        if (field.X == 0 && field.Y == 0)
        {
            AnalysisTrace.FinalSample(working, 0, 0, 0, 0, wavelengthMicrometers, requireUnvignetted: true);
            return 0;
        }
        return GeneralizedPrepared(working, wavelengthMicrometers, 0, normalized.X, normalized.Y, 0, 0, absolute);
    }

    public static double SmiaTv(Optic optic, double wavelengthMicrometers, int referenceField, double xWidth, double yWidth)
    {
        var working = Prepare(optic, wavelengthMicrometers, referenceField);
        if (!double.IsFinite(xWidth) || !double.IsFinite(yWidth) || xWidth <= 0 || yWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(xWidth), "SMIA 的 X/Y Width 必须是有限正的全视场宽度。");
        var center = referenceField == 0 ? new FieldPoint() : working.Fields[referenceField - 1];
        if (center.X != 0 || center.Y != 0)
            throw new NotSupportedException("SMIA 当前仅支持轴上参考；离轴参考的原生坐标约定尚未验证。");
        (double X, double Y) Trace(double x, double y)
        {
            var fieldX = center.X + x; var fieldY = center.Y + y;
            RequireAngularField(working, fieldX, fieldY);
            var normalized = FieldCoordinates.Normalize(working.Fields, fieldX, fieldY);
            if (Math.Abs(normalized.X) > 1 || Math.Abs(normalized.Y) > 1)
                throw new NotSupportedException("SMIA 矩形当前必须位于已定义视场的归一化 [-1,1] 范围内。");
            var sample = AnalysisTrace.FinalSample(working, normalized.X, normalized.Y, 0, 0, wavelengthMicrometers,
                requireUnvignetted: true);
            return (sample.Position.X, sample.Position.Y);
        }
        static double Distance((double X, double Y) a, (double X, double Y) b) =>
            Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
        var left = Distance(Trace(-xWidth / 2, yWidth / 2), Trace(-xWidth / 2, -yWidth / 2));
        var right = Distance(Trace(xWidth / 2, yWidth / 2), Trace(xWidth / 2, -yWidth / 2));
        var middle = Distance(Trace(0, yWidth / 2), Trace(0, -yWidth / 2));
        if (!double.IsFinite(middle) || middle <= 1e-30)
            throw new InvalidOperationException("SMIA 中间像高为零或非有限值，不能计算畸变。");
        return RequireFinite(100 * ((left + right) / (2 * middle) - 1));
    }

    private static double GeneralizedPrepared(Optic optic, double wavelength, int referenceField,
        double hx, double hy, double px, double py, bool absolute)
    {
        if (!double.IsFinite(hx) || !double.IsFinite(hy) || Math.Abs(hx) > 1 || Math.Abs(hy) > 1)
            throw new ArgumentOutOfRangeException(nameof(hx), "Hx/Hy 必须为 [-1,1] 内的有限坐标。");
        if (!double.IsFinite(px) || !double.IsFinite(py) || px * px + py * py > 1)
            throw new ArgumentOutOfRangeException(nameof(px), "Px/Py 必须在单位圆瞳内。");
        var maximum = FieldCoordinates.MaximumRadius(optic.Fields);
        RequireAngularField(optic, hx * maximum, hy * maximum);
        var linear = AnalysisTrace.ToDistortionLinearField(optic, hx * maximum, hy * maximum, "f-tan");
        var mapping = Mapping(optic, wavelength, referenceField);
        var predicted = mapping.MapFromReference(linear.X, linear.Y);
        var sample = AnalysisTrace.FinalSample(optic, hx, hy, px, py, wavelength, requireUnvignetted: true);
        var actualX = sample.Position.X - mapping.ReferenceImageX;
        var actualY = sample.Position.Y - mapping.ReferenceImageY;
        var predictedRadius = Math.Sqrt(predicted.X * predicted.X + predicted.Y * predicted.Y);
        var actualRadius = Math.Sqrt(actualX * actualX + actualY * actualY);
        var deviation = SignedVectorDifference(actualX, actualY, predicted.X, predicted.Y);
        if (absolute) return RequireFinite(deviation);
        if (predictedRadius <= 1e-30)
        {
            // The reference chief ray defines zero distortion. Other pupil rays at
            // that field have no nonzero reference height for a percentage.
            if (px == 0 && py == 0 && actualRadius <= 1e-12) return 0;
            throw new InvalidOperationException("参考像高为零，百分比畸变未定义；可改用绝对长度。");
        }
        return RequireFinite(100 * deviation / predictedRadius);
    }

    private static DistortionReferenceMapping Mapping(Optic optic, double wavelength, int referenceField) =>
        AnalysisTrace.BuildDistortionReferenceMapping(optic, wavelength, referenceField, "f-tan", requireUnvignetted: true);

    private static Optic Prepare(Optic optic, double wavelength, int fieldNumber)
    {
        ComputationCancellation.ThrowIfCancellationRequested();
        if (!double.IsFinite(wavelength) || wavelength <= 0) throw new ArgumentOutOfRangeException(nameof(wavelength));
        if (optic.Fields.Count == 0 || fieldNumber < 0 || fieldNumber > optic.Fields.Count)
            throw new ArgumentOutOfRangeException(nameof(fieldNumber), "需要已定义视场，Field 为有效编号或 0。");
        if (optic.ImageSpaceAfocal) throw new NotSupportedException("畸变操作数当前仅支持有焦像空间。");
        var working = RealImageFieldConversion.ForDistortion(optic);
        if (working.Fields.Any(f => !double.IsFinite(f.X) || !double.IsFinite(f.Y)))
            throw new InvalidOperationException("视场坐标必须有限。");
        if (working.FieldDefinition == FieldDefinitionKind.Angle && FieldCoordinates.MaximumRadius(working.Fields) >= 90)
            throw new NotSupportedException("角度视场的最大角必须小于 90 度。");
        return working;
    }

    private static void RequireAngularField(Optic optic, double x, double y)
    {
        if (optic.FieldDefinition == FieldDefinitionKind.Angle && (Math.Abs(x) >= 90 || Math.Abs(y) >= 90))
            throw new ArgumentOutOfRangeException(nameof(x), "畸变采样角必须小于 90 度。");
    }

    private static double RequireFinite(double value) => double.IsFinite(value)
        ? value : throw new InvalidOperationException("畸变计算未得到有限数值。");
}

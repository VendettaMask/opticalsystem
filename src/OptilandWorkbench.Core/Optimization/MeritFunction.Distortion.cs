using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Optimization;

public static partial class MeritFunctionCatalog
{
    private static double EvaluateDistortionMetric(Optic optic, MeritOperandDefinition definition)
    {
        var type = CanonicalType(definition.Type);
        var wave = ZemaxIntegerParameter(definition, 1, definition.Wavelength);
        if (type == "DISG" && (wave == 0 || wave == int.MinValue))
            throw new ArgumentOutOfRangeException(nameof(definition), "DISG 的 Wave 必须为非零的正/负波长编号。");
        var wavelength = RequireWavelength(optic, type == "DISG" ? Math.Abs(wave) : wave).Micrometers;
        var field = ZemaxIntegerParameter(definition, 0, definition.Field);
        return type switch
        {
            "DIST" => SeidelMetrics.Distortion(optic, wavelength,
                ZemaxIntegerParameter(definition, 0, definition.Surface), DistortionAbsoluteFlag(definition)),
            "DISA" => EvaluateExplicitDistortion(optic, definition, wavelength, field),
            "ABCD" => DistortionMetrics.ReferenceCoefficient(optic, wavelength, field, IntegerDataParameter(definition, 0, 0)),
            "DISG" => DistortionMetrics.Generalized(optic, wavelength, field,
                FiniteParameter(definition, 0, definition.Hx), FiniteParameter(definition, 1, definition.Hy),
                FiniteParameter(definition, 2, definition.Px), FiniteParameter(definition, 3, definition.Py), wave < 0),
            "DIMX" => BoundaryLessThanOrEqual(Math.Abs(DistortionMetrics.AtField(optic, wavelength, field,
                DistortionAbsoluteFlag(definition))), definition.Target),
            "SMIA" => DistortionMetrics.SmiaTv(optic, wavelength, field,
                FiniteParameter(definition, 0, definition.Hx), FiniteParameter(definition, 1, definition.Hy)),
            "FCGS" or "FCGT" => FieldCurvatureMetrics.Evaluate(optic, wavelength,
                FiniteParameter(definition, 0, definition.Hx), FiniteParameter(definition, 1, definition.Hy), type == "FCGS"),
            _ => throw new NotSupportedException(type)
        };
    }

    private static double EvaluateExplicitDistortion(Optic optic, MeritOperandDefinition definition, double wavelength, int referenceField)
    {
        if (definition.ZemaxDataParameters is not { Length: >= 6 })
            throw new ArgumentException("DISA 需要完整的 Field、Data、A、B、C、D 六个数据槽位。", nameof(definition));
        return DistortionMetrics.WithReferenceMatrix(optic, wavelength, referenceField,
            IntegerDataParameter(definition, 0, definition.Field), IntegerDataParameter(definition, 1, 0),
            FiniteParameter(definition, 2, 0), FiniteParameter(definition, 3, 0),
            FiniteParameter(definition, 4, 0), FiniteParameter(definition, 5, 0));
    }

    private static bool DistortionAbsoluteFlag(MeritOperandDefinition definition) => IntegerDataParameter(definition, 0, 0) switch
    {
        0 => false,
        1 => true,
        _ => throw new ArgumentOutOfRangeException(nameof(definition), "Absolute 必须为 0（%）或 1（镜头长度）。")
    };
}

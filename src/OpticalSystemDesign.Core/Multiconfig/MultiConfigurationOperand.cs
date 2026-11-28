using OpticalSystemDesign.Core.Optimization;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Multiconfig;

public enum MultiConfigurationOperandKind { Thickness, Curvature, Conic, SemiDiameter, ShapeCoefficient, GradientIndexCoefficient, ModelGlassCoefficient, Material, FieldX, FieldY, FieldWeight, Wavelength, WavelengthWeight }

/// <summary>One editor row; numeric or material values belong to the corresponding configuration surfaces.</summary>
public sealed record MultiConfigurationOperand(MultiConfigurationOperandKind Kind, int SurfaceNumber,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? Parameter = null)
{
    public static string Code(MultiConfigurationOperandKind kind) => kind switch
    {
        MultiConfigurationOperandKind.Thickness => "THIC",
        MultiConfigurationOperandKind.Curvature => "CRVT",
        MultiConfigurationOperandKind.Conic => "CONN",
        MultiConfigurationOperandKind.SemiDiameter => "SDIA",
        MultiConfigurationOperandKind.ShapeCoefficient or MultiConfigurationOperandKind.GradientIndexCoefficient => "PRAM",
        MultiConfigurationOperandKind.ModelGlassCoefficient => "GLAS",
        MultiConfigurationOperandKind.Material => "GLSS",
        MultiConfigurationOperandKind.FieldX => "XFIE",
        MultiConfigurationOperandKind.FieldY => "YFIE",
        MultiConfigurationOperandKind.FieldWeight => "FLWT",
        MultiConfigurationOperandKind.Wavelength => "WAVE",
        MultiConfigurationOperandKind.WavelengthWeight => "WLWT",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsNumeric => Kind != MultiConfigurationOperandKind.Material;

    // The persisted SurfaceNumber slot is a one-based field/wavelength number
    // for these typed bindings; it must never participate in surface remapping.
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsSystemParameter => Kind is MultiConfigurationOperandKind.FieldX or MultiConfigurationOperandKind.FieldY
        or MultiConfigurationOperandKind.FieldWeight or MultiConfigurationOperandKind.Wavelength or MultiConfigurationOperandKind.WavelengthWeight;

    [System.Text.Json.Serialization.JsonIgnore]
    public string Property => Kind switch
    {
        MultiConfigurationOperandKind.Thickness => "thickness",
        MultiConfigurationOperandKind.Curvature => "radius",
        MultiConfigurationOperandKind.Conic => "conic",
        MultiConfigurationOperandKind.SemiDiameter => "semiDiameter",
        MultiConfigurationOperandKind.ShapeCoefficient => SurfaceShapeParameters.Property(Parameter!),
        MultiConfigurationOperandKind.GradientIndexCoefficient => GradientIndexParameters.Property(Parameter!),
        MultiConfigurationOperandKind.ModelGlassCoefficient => ModelGlassParameters.Property(Parameter!),
        MultiConfigurationOperandKind.Material => "material",
        _ when IsSystemParameter => "system:" + Code(Kind).ToLowerInvariant(),
        _ => throw new ArgumentOutOfRangeException(nameof(Kind))
    };

    public double Read(Optic optic)
    {
        if (!IsNumeric) throw new InvalidOperationException("材料行使用材料名称，请读取或编辑文本值。");
        if (IsSystemParameter)
        {
            ValidateSystemTarget(optic);
            return Kind switch
            {
                MultiConfigurationOperandKind.FieldX => optic.Fields[SurfaceNumber - 1].X,
                MultiConfigurationOperandKind.FieldY => optic.Fields[SurfaceNumber - 1].Y,
                MultiConfigurationOperandKind.FieldWeight => optic.Fields[SurfaceNumber - 1].Weight,
                MultiConfigurationOperandKind.Wavelength => optic.Wavelengths[SurfaceNumber - 1].Micrometers,
                _ => optic.Wavelengths[SurfaceNumber - 1].Weight
            };
        }
        var surface = ValidateSurface(optic);
        var value = Kind switch
        {
            MultiConfigurationOperandKind.Thickness => surface.Thickness,
            MultiConfigurationOperandKind.Curvature => SurfaceCurvatureParameter.Read(surface),
            MultiConfigurationOperandKind.Conic => surface.Conic,
            MultiConfigurationOperandKind.SemiDiameter => surface.SemiDiameter,
            MultiConfigurationOperandKind.ShapeCoefficient => SurfaceShapeParameters.Read(surface, Parameter!),
            MultiConfigurationOperandKind.GradientIndexCoefficient => GradientIndexParameters.Read(surface, Parameter!),
            MultiConfigurationOperandKind.ModelGlassCoefficient => ModelGlassParameters.Read(surface, Parameter!),
            _ => throw new ArgumentOutOfRangeException(nameof(Kind))
        };
        if (!double.IsFinite(value)) throw new InvalidOperationException("多配置数值行必须是有限值。");
        return value;
    }

    public string ReadText(Optic optic)
    {
        if (IsNumeric) throw new InvalidOperationException("数值行使用数值读取接口。");
        return ValidateSurface(optic).Material;
    }

    public void ValidateSystemValue(Optic optic, double value)
    {
        ValidateSystemTarget(optic);
        if (!double.IsFinite(value) || Kind == MultiConfigurationOperandKind.Wavelength && value < .001
            || Kind is MultiConfigurationOperandKind.FieldWeight or MultiConfigurationOperandKind.WavelengthWeight && value < 0)
            throw new ArgumentOutOfRangeException(nameof(value), "波长必须至少为 0.001 µm，权重不能为负数，所有值必须有限。");
    }

    public void WriteSystemValue(Optic optic, double value)
    {
        ValidateSystemValue(optic, value);
        switch (Kind)
        {
            case MultiConfigurationOperandKind.FieldX: optic.Fields[SurfaceNumber - 1].X = value; break;
            case MultiConfigurationOperandKind.FieldY: optic.Fields[SurfaceNumber - 1].Y = value; break;
            case MultiConfigurationOperandKind.FieldWeight: optic.Fields[SurfaceNumber - 1].Weight = value; break;
            case MultiConfigurationOperandKind.Wavelength: optic.Wavelengths[SurfaceNumber - 1].Micrometers = value; break;
            case MultiConfigurationOperandKind.WavelengthWeight: optic.Wavelengths[SurfaceNumber - 1].Weight = value; break;
        }
        optic.InvalidateRayTraceCache();
    }

    private void ValidateSystemTarget(Optic optic)
    {
        if (!IsSystemParameter || Parameter is not null || SurfaceNumber < 1 || SurfaceNumber >
            (Kind is MultiConfigurationOperandKind.Wavelength or MultiConfigurationOperandKind.WavelengthWeight
                ? optic.Wavelengths.Count : optic.Fields.Count))
            throw new ArgumentException("多配置行必须引用有效的一基视场号或波长号，且不能附带表面参数。");
    }

    private Domain.OpticalSurface ValidateSurface(Optic optic)
    {
        if (!Enum.IsDefined(Kind) || SurfaceNumber < 0 || SurfaceNumber == 0 && Kind != MultiConfigurationOperandKind.Thickness)
            throw new ArgumentException("多配置行类型或表面号无效；物面仅支持有限物距。");
        var surface = optic.SurfaceGroup.Items.SingleOrDefault(surface => surface.Number == SurfaceNumber)
            ?? throw new ArgumentOutOfRangeException(nameof(SurfaceNumber), "多配置行引用的表面不存在。");
        if (SurfaceNumber == 0 && !double.IsFinite(surface.Thickness))
            throw new ArgumentException("物面多配置行仅支持有限物距。");
        if (Kind is MultiConfigurationOperandKind.ShapeCoefficient or MultiConfigurationOperandKind.GradientIndexCoefficient or MultiConfigurationOperandKind.ModelGlassCoefficient
            ? string.IsNullOrWhiteSpace(Parameter) : Parameter is not null)
            throw new ArgumentException("多配置参数行必须保留其实际系数身份。");
        return surface;
    }

    public static void ValidateRows(IReadOnlyList<MultiConfigurationOperand>? rows, IReadOnlyList<Optic> configurations)
    {
        if (rows is null) return;
        if (rows.Count > 4096) throw new ArgumentException("多配置操作数行数不能超过 4096。");
        if (rows.Distinct().Count() != rows.Count) throw new ArgumentException("同一参数和表面不能重复定义多配置行。");
        foreach (var row in rows)
        {
            if (row is null) throw new ArgumentException("多配置操作数行不能为空。");
            foreach (var optic in configurations)
                if (row.IsNumeric) row.Read(optic); else row.ReadText(optic);
        }
    }
}

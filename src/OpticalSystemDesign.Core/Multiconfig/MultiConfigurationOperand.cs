using OpticalSystemDesign.Core.Optimization;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Multiconfig;

public enum MultiConfigurationOperandKind { Thickness, Curvature, Conic, SemiDiameter, ShapeCoefficient, GradientIndexCoefficient, ModelGlassCoefficient }

/// <summary>One numeric editor row; values are owned by the corresponding configuration surfaces.</summary>
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
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

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
        _ => throw new ArgumentOutOfRangeException(nameof(Kind))
    };

    public double Read(Optic optic)
    {
        if (!Enum.IsDefined(Kind) || SurfaceNumber < 0 || SurfaceNumber == 0 && Kind != MultiConfigurationOperandKind.Thickness)
            throw new ArgumentException("多配置行类型或表面号无效；物面仅支持有限物距。");
        var surface = optic.SurfaceGroup.Items.SingleOrDefault(surface => surface.Number == SurfaceNumber)
            ?? throw new ArgumentOutOfRangeException(nameof(SurfaceNumber), "多配置行引用的表面不存在。");
        if (Kind is MultiConfigurationOperandKind.ShapeCoefficient or MultiConfigurationOperandKind.GradientIndexCoefficient or MultiConfigurationOperandKind.ModelGlassCoefficient
            ? string.IsNullOrWhiteSpace(Parameter) : Parameter is not null)
            throw new ArgumentException("多配置参数行必须保留其实际系数身份。");
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

    public static void ValidateRows(IReadOnlyList<MultiConfigurationOperand>? rows, IReadOnlyList<Optic> configurations)
    {
        if (rows is null) return;
        if (rows.Count > 4096) throw new ArgumentException("多配置操作数行数不能超过 4096。");
        if (rows.Distinct().Count() != rows.Count) throw new ArgumentException("同一参数和表面不能重复定义多配置行。");
        foreach (var row in rows)
        {
            if (row is null) throw new ArgumentException("多配置操作数行不能为空。");
            foreach (var optic in configurations) row.Read(optic);
        }
    }
}

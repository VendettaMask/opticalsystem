using OptilandWorkbench.Core.Optimization;

namespace OptilandWorkbench.Core.Multiconfig;

public enum MultiConfigurationOperandKind { Thickness, Curvature, Conic, SemiDiameter }

/// <summary>One numeric editor row; values are owned by the corresponding configuration surfaces.</summary>
public sealed record MultiConfigurationOperand(MultiConfigurationOperandKind Kind, int SurfaceNumber)
{
    public static string Code(MultiConfigurationOperandKind kind) => kind switch
    {
        MultiConfigurationOperandKind.Thickness => "THIC",
        MultiConfigurationOperandKind.Curvature => "CRVT",
        MultiConfigurationOperandKind.Conic => "CONN",
        MultiConfigurationOperandKind.SemiDiameter => "SDIA",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    [System.Text.Json.Serialization.JsonIgnore]
    public string Property => Kind switch
    {
        MultiConfigurationOperandKind.Thickness => "thickness",
        MultiConfigurationOperandKind.Curvature => "radius",
        MultiConfigurationOperandKind.Conic => "conic",
        MultiConfigurationOperandKind.SemiDiameter => "semiDiameter",
        _ => throw new ArgumentOutOfRangeException(nameof(Kind))
    };

    public double Read(Optic optic)
    {
        if (!Enum.IsDefined(Kind) || SurfaceNumber <= 0)
            throw new ArgumentException("多配置行类型或表面号无效；当前只支持正表面编号。");
        var surface = optic.SurfaceGroup.Items.SingleOrDefault(surface => surface.Number == SurfaceNumber)
            ?? throw new ArgumentOutOfRangeException(nameof(SurfaceNumber), "多配置行引用的表面不存在。");
        var value = Kind switch
        {
            MultiConfigurationOperandKind.Thickness => surface.Thickness,
            MultiConfigurationOperandKind.Curvature => SurfaceCurvatureParameter.Read(surface),
            MultiConfigurationOperandKind.Conic => surface.Conic,
            MultiConfigurationOperandKind.SemiDiameter => surface.SemiDiameter,
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

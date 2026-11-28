using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Materials;

namespace OpticalSystemDesign.Core.Capabilities;

public enum OpticCapabilityOperation
{
    RayTrace,
    Analysis,
    Optimization,
    Tolerancing,
    Export,
    Visualization,
    Conversion
}

public sealed record OpticCapabilityIssue(
    int SurfaceNumber,
    string OriginalType,
    string Reason);

public sealed class OpticCapabilityException : InvalidOperationException
{
    public OpticCapabilityException(
        OpticCapabilityOperation operation,
        IReadOnlyList<OpticCapabilityIssue> issues,
        string? context = null)
        : base(BuildMessage(operation, issues, context))
    {
        Operation = operation;
        Issues = issues;
        Context = context;
    }

    public OpticCapabilityOperation Operation { get; }

    public IReadOnlyList<OpticCapabilityIssue> Issues { get; }

    public string? Context { get; }

    private static string BuildMessage(
        OpticCapabilityOperation operation,
        IReadOnlyList<OpticCapabilityIssue> issues,
        string? context)
    {
        var operationName = operation switch
        {
            OpticCapabilityOperation.RayTrace => "光线追迹",
            OpticCapabilityOperation.Analysis => "分析",
            OpticCapabilityOperation.Optimization => "优化",
            OpticCapabilityOperation.Tolerancing => "公差",
            OpticCapabilityOperation.Export => "导出",
            OpticCapabilityOperation.Visualization => "可视化",
            OpticCapabilityOperation.Conversion => "格式转换",
            _ => operation.ToString()
        };
        var suffix = string.IsNullOrWhiteSpace(context) ? string.Empty : $"（{context}）";
        var details = string.Join(
            "；",
            issues.Select(issue =>
                $"表面 {issue.SurfaceNumber}，原始类型“{issue.OriginalType}”：{issue.Reason}"));
        return $"无法执行{operationName}{suffix}。{details}";
    }
}

public static class OpticCapabilityPreflight
{
    public static IReadOnlyList<OpticCapabilityIssue> Inspect(Optic optic)
    {
        ArgumentNullException.ThrowIfNull(optic);
        return optic.SurfaceGroup.Items
            .SelectMany(surface => InspectSurface(surface, includeMaterials: true))
            .ToArray();
    }

    public static void EnsureSupported(
        Optic optic,
        OpticCapabilityOperation operation,
        string? context = null)
    {
        List<OpticCapabilityIssue>? issues = null;
        var includeMaterials = operation != OpticCapabilityOperation.Visualization;
        foreach (var surface in optic.SurfaceGroup.Items)
            if (HasIssue(surface, includeMaterials))
                (issues ??= []).AddRange(InspectSurface(surface, includeMaterials));
        if (issues is not null)
        {
            throw new OpticCapabilityException(operation, issues, context);
        }
    }

    public static void EnsureSurfaceSupported(
        Domain.OpticalSurface surface,
        OpticCapabilityOperation operation,
        string? context = null)
    {
        ArgumentNullException.ThrowIfNull(surface);
        var includeMaterials = operation != OpticCapabilityOperation.Visualization;
        if (HasIssue(surface, includeMaterials))
        {
            var issues = InspectSurface(surface, includeMaterials).ToArray();
            throw new OpticCapabilityException(operation, issues, context);
        }
    }

    // The overwhelmingly common supported path must not allocate iterators,
    // arrays and material-name sets at every aiming ray and surface encounter.
    // Recheck current objects each time; mutable prescriptions are not cached.
    private static bool HasIssue(Domain.OpticalSurface surface, bool includeMaterials) =>
        surface.Geometry is INonComputableGeometry
        || (includeMaterials
            ? surface.MaterialBefore is UnresolvedMaterial || surface.MaterialAfter is UnresolvedMaterial
                || surface.Geometry is PolygonGroupGeometry { VolumeMaterial: UnresolvedMaterial }
            : surface.MaterialBefore is GradientIndexMaterial || surface.MaterialAfter is GradientIndexMaterial);

    private static IEnumerable<OpticCapabilityIssue> InspectSurface(Domain.OpticalSurface surface, bool includeMaterials)
    {
        if (surface.Geometry is INonComputableGeometry)
            yield return CreateIssue(surface);
        if (!includeMaterials)
        {
            if (surface.MaterialBefore is GradientIndexMaterial || surface.MaterialAfter is GradientIndexMaterial)
                yield return new OpticCapabilityIssue(surface.Number, "GRIN",
                    "GRIN 曲线节点尚未接入场景，不能以表面间直线代替真实路径。");
            yield break;
        }
        foreach (var material in new[] { surface.MaterialBefore, surface.MaterialAfter }
                     .OfType<UnresolvedMaterial>().DistinctBy(material => material.Name))
            yield return new OpticCapabilityIssue(surface.Number, material.Name, $"找不到玻璃“{material.Name}”，请匹配材料后再计算。");
        if (surface.Geometry is PolygonGroupGeometry { VolumeMaterial: UnresolvedMaterial volume })
            yield return new OpticCapabilityIssue(surface.Number, volume.Name, $"找不到多边形体材料“{volume.Name}”，请匹配材料后再计算。");
    }

    private static OpticCapabilityIssue CreateIssue(Domain.OpticalSurface surface)
    {
        var geometry = (INonComputableGeometry)surface.Geometry;
        return new OpticCapabilityIssue(
            surface.Number,
            geometry.OriginalType,
            geometry.BlockingReason);
    }
}

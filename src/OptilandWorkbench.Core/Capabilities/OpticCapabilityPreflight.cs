using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Materials;

namespace OptilandWorkbench.Core.Capabilities;

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
        var issues = optic.SurfaceGroup.Items
            .SelectMany(surface => InspectSurface(surface, operation != OpticCapabilityOperation.Visualization))
            .ToArray();
        if (issues.Length > 0)
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
        var issues = InspectSurface(surface, operation != OpticCapabilityOperation.Visualization).ToArray();
        if (issues.Length > 0)
        {
            throw new OpticCapabilityException(operation, issues, context);
        }
    }

    private static IEnumerable<OpticCapabilityIssue> InspectSurface(Domain.OpticalSurface surface, bool includeMaterials)
    {
        if (surface.Geometry is INonComputableGeometry)
            yield return CreateIssue(surface);
        if (!includeMaterials)
            yield break;
        foreach (var material in new[] { surface.MaterialBefore, surface.MaterialAfter }
                     .OfType<UnresolvedMaterial>().DistinctBy(material => material.Name))
            yield return new OpticCapabilityIssue(surface.Number, material.Name, $"找不到玻璃“{material.Name}”，请匹配材料后再计算。");
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

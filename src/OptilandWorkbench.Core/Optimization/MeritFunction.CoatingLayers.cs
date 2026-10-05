using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Optimization;

public static partial class MeritFunctionCatalog
{
    private static double EvaluateCoatingLayerConstraint(Optic optic, MeritOperandDefinition definition)
    {
        var code = CanonicalType(definition.Type);
        var surfaceNumber = ZemaxIntegerParameter(definition, 0, definition.Surface);
        var layerNumber = ZemaxIntegerParameter(definition, 1, 1);
        var valueOnly = code.EndsWith("VA", StringComparison.Ordinal);
        if (surfaceNumber < 0 || layerNumber < 0 || (valueOnly && (surfaceNumber == 0 || layerNumber == 0)))
            throw new ArgumentException("VA 需要正表面及膜层编号；GT/LT 可用 0 选择全部。");
        var kind = code[1] switch { 'M' => CoatingLayerParameter.Multiplier, 'I' => CoatingLayerParameter.IndexOffset, _ => CoatingLayerParameter.ExtinctionOffset };
        if (surfaceNumber == 0 && optic.SurfaceGroup.Items.Any(s => s.Number > 0
            && s.CoatingModel is not (NoneCoatingModel or CoherentMultilayerCoating)))
            throw new NotSupportedException("全部表面包含未接入物理层参数的膜层模型，不能返回部分表面的约束结果。");
        var surfaces = surfaceNumber == 0
            ? optic.SurfaceGroup.Items.Where(s => s.Number > 0 && s.CoatingModel is CoherentMultilayerCoating).ToArray()
            : [ResolveSurface(optic, surfaceNumber)];
        var values = new List<double>();
        foreach (var surface in surfaces)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var layers = CoatingLayerMetrics.RequireCoating(surface).Layers;
            if (layerNumber > layers.Count) throw new ArgumentException($"表面 {surface.Number} 没有第 {layerNumber} 膜层。");
            values.AddRange(layerNumber == 0 ? layers.Select(l => l.Adjustment.Value(kind))
                : [layers[layerNumber - 1].Adjustment.Value(kind)]);
        }
        if (values.Count == 0) throw new ArgumentException("没有符合条件的物理相干膜层。");
        if (valueOnly) return values.Single();
        return code.EndsWith("GT", StringComparison.Ordinal)
            ? BoundaryGreaterThanOrEqual(values.Min(), definition.Target)
            : BoundaryLessThanOrEqual(values.Max(), definition.Target);
    }
}

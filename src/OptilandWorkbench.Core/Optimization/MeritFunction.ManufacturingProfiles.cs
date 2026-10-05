using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Optimization;

public static partial class MeritFunctionCatalog
{
    private static double EvaluateBestFitSphere(Optic optic, MeritOperandDefinition definition)
    {
        var surface = ResolveSurface(optic, ZemaxIntegerParameter(definition, 0, definition.Surface));
        var data = ZemaxIntegerParameter(definition, 1, 0);
        if (data is < 0 or > 7) throw new ArgumentOutOfRangeException(nameof(definition), "BFSD Data 必须在 0..7。");
        var min = FiniteParameter(definition, 0, 0);
        var max = FiniteParameter(definition, 1, 0);
        return SurfaceBestFitSphere.MinimumVolume(surface, min, max).Value(data);
    }

    private static double EvaluateElementVolumeOrMass(Optic optic, MeritOperandDefinition definition)
    {
        var start = ZemaxIntegerParameter(definition, 0, definition.Surface);
        var end = ZemaxIntegerParameter(definition, 1, definition.Wavelength);
        if (start < 1 || end < start || end >= optic.SurfaceGroup.Items.Count - 1)
            throw new ArgumentOutOfRangeException(nameof(definition), "体积/质量范围必须是物面之后且像面之前的闭区间；单片使用相同起止面。");
        var mode = DiameterMode(definition);
        var mass = CanonicalType(definition.Type) == "TMAS";
        var sum = 0.0;
        for (var number = start; number <= end; number++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var front = ResolveSurface(optic, number); var back = ResolveNextSurface(optic, number);
            sum += mass ? ElementVolumeMetrics.MassGrams(front, back, mode)
                : ElementVolumeMetrics.VolumeCubicCentimeters(front, back, mode);
        }
        return sum;
    }

    private static double EvaluateSurfaceProfile(Optic optic, MeritOperandDefinition definition)
    {
        var type = CanonicalType(definition.Type);
        var data = ZemaxIntegerParameter(definition, 1, definition.Wavelength);
        if (data is < 1 or > 8) throw new NotSupportedException("面形统计当前支持 Data=1..8。");
        var sampling = IntegerDataParameter(definition, 0, 1);
        var offAxis = IntegerDataParameter(definition, 1, 0);
        var remove = IntegerDataParameter(definition, 2, 0);
        var bfs = IntegerDataParameter(definition, 3, 0);
        var orientation = type == "DSAG" ? 0 : IntegerDataParameter(definition, 4, 0);
        if (offAxis != 0) throw new NotSupportedException("面形统计的离轴坐标重建尚未实现，当前 Off-axis=0。");
        if (bfs is < 0 or > 2) throw new ArgumentOutOfRangeException(nameof(definition), "BFS 必须为 0..2；仅在减去拟合球面时生效。");
        var quantity = type switch
        { "DSAG" => SurfaceProfileQuantity.Sag, "DSLP" => SurfaceProfileQuantity.Slope, _ => SurfaceProfileQuantity.Curvature };
        return SurfaceProfileMetrics.Evaluate(ResolveSurface(optic, ZemaxIntegerParameter(definition, 0, definition.Surface)),
            quantity, sampling, remove, orientation).Value(data);
    }
}

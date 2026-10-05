using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Optimization;

public static partial class MeritFunctionCatalog
{
    private static double EvaluateDirectionalSag(Optic optic, MeritOperandDefinition definition) =>
        SurfaceDirectionalSag.Evaluate(
            ResolveSurface(optic, ZemaxIntegerParameter(definition, 0, definition.Surface)),
            ZemaxIntegerParameter(definition, 1, 0),
            new(ZemaxDataParameter(definition, 0, definition.Hx),
                ZemaxDataParameter(definition, 1, definition.Hy),
                ZemaxDataParameter(definition, 2, 0)),
            ZemaxDataParameter(definition, 3, 0),
            ZemaxDataParameter(definition, 4, 0),
            ZemaxDataParameter(definition, 5, 0)).Distance;

    private static double EvaluateSurfacePoint(Optic optic, MeritOperandDefinition definition)
    {
        var type = CanonicalType(definition.Type);
        var surface = ResolveSurface(optic, ZemaxIntegerParameter(definition, 0, definition.Surface));
        var mode = ZemaxIntegerParameter(definition, 1, 1);
        if (mode is not (0 or 1)) throw new ArgumentOutOfRangeException(nameof(definition), "Mode 为 0 机械半口径、1 净半口径。");
        var x = ZemaxDataParameter(definition, 0, definition.Hx);
        var y = ZemaxDataParameter(definition, 1, definition.Hy);
        var offAxis = IntegerDataParameter(definition, 2, 0);
        var remove = IntegerDataParameter(definition, 3, 0);
        var bfs = IntegerDataParameter(definition, 4, 0);
        var orientation = type == "SSAG" ? 0 : IntegerDataParameter(definition, 5, 0);
        if (offAxis != 0) throw new NotSupportedException("指定点面形当前支持 Off-axis=0；离轴零件坐标重建尚未实现。");
        if (bfs is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(definition), "BFS 取值为 0..3，仅在移除拟合球面时生效。");
        // Mode changes the fitting aperture, not the coordinate units. The current
        // unremoved/base-sphere modes therefore evaluate the same local point.
        var quantity = type switch
        { "SSAG" => SurfaceProfileQuantity.Sag, "SSLP" => SurfaceProfileQuantity.Slope, _ => SurfaceProfileQuantity.Curvature };
        return SurfaceProfileMetrics.AtPoint(surface, quantity, x, y, remove, orientation);
    }
}

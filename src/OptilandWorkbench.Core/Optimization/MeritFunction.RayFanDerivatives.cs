using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Optimization;

public static partial class MeritFunctionCatalog
{
    private static double EvaluateRayFanDerivative(Optic optic, MeritOperandDefinition definition)
    {
        var ray = SequentialRayDefinition(optic, definition, surface: ResolveImageSurfaceNumber(optic));
        var image = ResolveSurface(optic, ray.Surface);
        var type = CanonicalType(definition.Type);
        var outputX = type is "DXDX" or "DXDY";
        var inputX = type is "DXDX" or "DYDX";
        // The chief-ray reference is independent of pupil coordinates, so its derivative is zero.
        double Intercept(double coordinate)
        {
            var sampleRay = ray.Clone();
            if (inputX) sampleRay.Px = coordinate; else sampleRay.Py = coordinate;
            var point = image.CoordinateSystem.ToLocalPoint(RequireRaySample(optic, sampleRay).Position);
            return outputX ? point.X : point.Y;
        }
        // Validate the requested central ray even when the difference stencil does not include it.
        Intercept(inputX ? ray.Px : ray.Py);
        return RayFanDerivativeMetrics.Differentiate(Intercept,
            inputX ? ray.Px : ray.Py, inputX ? ray.Py : ray.Px);
    }
}

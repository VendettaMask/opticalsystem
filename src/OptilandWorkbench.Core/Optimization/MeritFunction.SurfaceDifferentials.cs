using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Propagation;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Optimization;

public static partial class MeritFunctionCatalog
{
    private static double EvaluateSurfaceDifferential(Optic optic, MeritOperandDefinition definition)
    {
        var surface = ResolveSurface(optic, ZemaxIntegerParameter(definition, 0, definition.Surface));
        var data = ZemaxIntegerParameter(definition, 1, definition.Wavelength);
        var x = FiniteParameter(definition, 0, definition.Hx);
        var y = FiniteParameter(definition, 1, definition.Hy);
        return CanonicalType(definition.Type) == "SDRV"
            ? SurfaceDifferentialMetrics.Derivative(surface, x, y, data)
            : SurfaceDifferentialMetrics.Curvature(surface, x, y, data);
    }

    private static double EvaluateIntermediateTransverseAberration(Optic optic, MeritOperandDefinition definition)
    {
        var ray = SequentialRayDefinition(optic, definition);
        var surface = ResolveSurface(optic, ray.Surface);
        var point = surface.CoordinateSystem.ToLocalPoint(RequireRaySample(optic, ray).Position);
        ray.Wavelength = 0; // Like TRAR, use the primary-wavelength chief reference.
        ray.Px = ray.Py = 0;
        var chief = surface.CoordinateSystem.ToLocalPoint(RequireRaySample(optic, ray).Position);
        return Math.Sqrt(Math.Pow(point.X - chief.X, 2) + Math.Pow(point.Y - chief.Y, 2));
    }

    private static double EvaluateBoresightError(Optic optic, MeritOperandDefinition definition)
    {
        if (optic.ImageSpaceAfocal) throw new NotSupportedException("BSER 当前支持有焦像空间。");
        // Parallel decentered surfaces are supported: their first-order power is unchanged.
        // Tilted axes, nonstandard powers and folded paths require a generalized EFL.
        foreach (var surface in optic.SurfaceGroup.Items)
            if (surface.IsReflective || surface.Geometry is not (PlaneGeometry or StandardGeometry)
                || surface.InteractionModel is not (RefractiveReflectiveInteractionModel or ThinLensInteractionModel)
                || surface.MaterialAfter.PropagationModel is not HomogeneousPropagationModel
                || Math.Abs(surface.CoordinateSystem.RotationXDegrees) > 1e-12
                || Math.Abs(surface.CoordinateSystem.RotationYDegrees) > 1e-12)
                throw new NotSupportedException("BSER 当前支持平行光轴的透射标准面及理想薄透镜；允许偏心，不支持倾斜、非标准面、反射或 GRIN。");
        var wavelength = ZemaxIntegerParameter(definition, 1, definition.Wavelength);
        RequireWavelength(optic, wavelength);
        var image = optic.SurfaceGroup.Items[^1];
        var ray = new MeritOperandDefinition { Surface = image.Number, Wavelength = wavelength, Field = 0 };
        var point = image.CoordinateSystem.ToLocalPoint(RequireRaySample(optic, ray).Position);
        // EFFL is the system's primary-wavelength reference, independent of the ray wavelength.
        var focal = optic.Paraxial.EstimateEffectiveFocalLength();
        if (!double.IsFinite(focal) || Math.Abs(focal) <= 1e-15)
            throw new InvalidOperationException("BSER 需要有限非零有效焦距。");
        return Math.Sqrt(point.X * point.X + point.Y * point.Y) / focal;
    }
}

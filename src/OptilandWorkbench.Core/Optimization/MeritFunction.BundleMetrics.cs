using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Optimization;

public static partial class MeritFunctionCatalog
{
    private static double EvaluateRayCentroid(Optic optic, MeritOperandDefinition definition)
    {
        var type = CanonicalType(definition.Type);
        var surface = ZemaxIntegerParameter(definition, 0, definition.Surface);
        if (surface == 0) surface = ResolveImageSurfaceNumber(optic);
        var wave = ZemaxIntegerParameter(definition, 1, definition.Wavelength);
        var numberedField = type is "CENX" or "CENY";
        double hx, hy;
        if (numberedField)
        {
            var field = IntegerDataParameter(definition, 0, definition.Field);
            if (field < 1 || field > optic.Fields.Count)
                throw new ArgumentOutOfRangeException(nameof(definition), "CENX/CENY 需要有效的一起始视场编号。");
            var selected = optic.Fields[field - 1];
            (hx, hy) = FieldCoordinates.Normalize(optic.Fields, selected.X, selected.Y);
        }
        else
        {
            hx = FiniteParameter(definition, 0, definition.Hx);
            hy = FiniteParameter(definition, 1, definition.Hy);
        }
        var polarization = IntegerDataParameter(definition, numberedField ? 1 : 2, 0);
        if (polarization != 0) throw new NotSupportedException("质心操作数当前仅支持 Pol=0；偏振加权尚未接入。");
        var sampling = IntegerDataParameter(definition, numberedField ? 2 : 3, 10);
        var rays = RayBundleMetrics.Trace(optic, surface, wave, hx, hy, RayBundleMetrics.RectangularPupil(sampling));
        if (type is "CNAX" or "CNAY") return RayBundleMetrics.AngularCentroid(rays, type == "CNAX");
        var centroid = RayBundleMetrics.Centroid(rays);
        return type is "CENX" or "CNPX" ? centroid.X : centroid.Y;
    }

    private static double EvaluateGeometricSpot(Optic optic, MeritOperandDefinition definition)
    {
        var type = CanonicalType(definition.Type);
        var sampling = ZemaxIntegerParameter(definition, 0, definition.PupilRings);
        var wave = ZemaxIntegerParameter(definition, 1, definition.Wavelength);
        // The current definition-level profile is monochromatic; do not invent the
        // undocumented GS Wave=0 reference or silently choose a wavelength.
        if (wave <= 0) throw new NotSupportedException("GS 点列半径当前需要明确的正波长编号；Wave=0 尚未验证。");
        RequireWavelength(optic, wave);
        var hx = FiniteParameter(definition, 0, definition.Hx);
        var hy = FiniteParameter(definition, 1, definition.Hy);
        var gaussian = type is "GSCE" or "GSCH";
        if (sampling < 1 || sampling > (gaussian ? 32 : 256))
            throw new ArgumentOutOfRangeException(nameof(definition), "高斯环数为 1..32；矩形 Samp 为 1..256。");
        var pupils = gaussian ? ApertureSampler.GenerateGaussianQuadrature(sampling, 6)
            : RayBundleMetrics.RectangularPupil(2 * sampling + 1);
        var image = ResolveImageSurfaceNumber(optic);
        var rays = RayBundleMetrics.Trace(optic, image, wave, hx, hy, pupils, requireUnvignetted: gaussian);
        Vector3D reference;
        if (type is "GSCH" or "GSRH")
        {
            var chief = new MeritOperandDefinition { Surface = image, Wavelength = 0, Hx = hx, Hy = hy, Field = 0 };
            reference = ResolveSurface(optic, image).CoordinateSystem.ToLocalPoint(RequireRaySample(optic, chief).Position);
        }
        else reference = RayBundleMetrics.Centroid(rays);
        return RayBundleMetrics.GeometricRadius(rays, reference);
    }
}

using OpticalSystemDesign.Core.Analysis;

namespace OpticalSystemDesign.Core.Services;

internal static class AfocalParabasalPupilScale
{
    // Keep finite real-ray scale conventions separate from analytic first-order
    // cardinal quantities and geometric stop-image display fits.
    internal static bool TryCompute(Optic optic, out double diameterMillimeters, out double positionMillimeters)
    {
        diameterMillimeters = positionMillimeters = double.NaN;
        if (!optic.SequentialRayTracer.RayGenerator.TryGenerateAfocalScaleProbes(out var bundle)) return false;
        var rays = optic.SequentialRayTracer.TraceFinalSamples(bundle!);
        if (rays.Any(r => r is not { Vignetted: false, Intensity: > 0 }))
            throw new AnalysisDataUnavailableException("Afocal pupil", "finite parabasal scale probe is blocked");
        var center = rays[0]!; var field = rays[1]!; var marginal = rays[2]!;
        var dy = field.Position.Y - center.Position.Y;
        var slope = field.Direction.Y / field.Direction.Z;
        if (!double.IsFinite(slope) || Math.Abs(slope) <= 1e-15)
            throw new AnalysisDataUnavailableException("Afocal pupil", "no finite parabasal pupil conjugate");
        var referenceZ = field.Position.Z - dy / slope;
        var height = marginal.Position.Y + (referenceZ - marginal.Position.Z) * marginal.Direction.Y / marginal.Direction.Z;
        positionMillimeters = referenceZ - optic.SurfaceGroup.Items[^1].CoordinateSystem.Origin.Z;
        diameterMillimeters = 2 * Math.Abs(height - center.Position.Y) / .001;
        if (!double.IsFinite(diameterMillimeters) || diameterMillimeters <= 1e-12 || !double.IsFinite(positionMillimeters))
            throw new AnalysisDataUnavailableException("Afocal pupil", "no finite positive parabasal pupil scale");
        return true;
    }
}

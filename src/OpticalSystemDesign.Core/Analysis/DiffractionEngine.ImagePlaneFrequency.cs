using OpticalSystemDesign.Core.Domain;

namespace OpticalSystemDesign.Core.Analysis;

public static partial class DiffractionEngine
{
    // Fourier frequency is conjugate to image-plane position: d(phi)/d(x,y)
    // = k*n*(l,m). Chief-normal cone angles remain the reported working F/#,
    // and the defocus phase still uses those angles independently.
    internal static (double Tangential, double Sagittal) ImagePlaneFourierFNumbers(
        Optic optic, (double Hx, double Hy) field, Wavelength wave, bool aiming)
    {
        var frame = optic.SurfaceGroup.Items[^1].CoordinateSystem;
        var imageIndex = optic.SurfaceGroup.Items[^1].MaterialAfter.RefractiveIndex(wave.Nanometers);
        foreach (var zone in new[] { 1d, .99, .95, .9, .8, .7, .5, .25 })
        {
            if (!TryWorkingPupilDirections(optic, field, wave, aiming, zone, out var directions)) continue;
            var local = directions.Select(d => frame.ToLocalDirection(d) / d.Length).ToArray();
            var t = imageIndex * (Math.Abs(local[1].Y - local[0].Y) + Math.Abs(local[2].Y - local[0].Y)) / zone;
            var s = imageIndex * (Math.Abs(local[3].X - local[0].X) + Math.Abs(local[4].X - local[0].X)) / zone;
            if (double.IsFinite(t) && t > 1e-30 && double.IsFinite(s) && s > 1e-30) return (1 / t, 1 / s);
        }
        throw new AnalysisDataUnavailableException("Fourier MTF", "the image-plane pupil frequency scale is not finite and positive");
    }
}

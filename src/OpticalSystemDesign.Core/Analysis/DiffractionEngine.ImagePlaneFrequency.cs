using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;

namespace OpticalSystemDesign.Core.Analysis;

public static partial class DiffractionEngine
{
    // Fourier frequency is conjugate to image-plane position: d(phi)/d(x,y)
    // = k*n*(l,m). Chief-normal cone angles remain the reported working F/#,
    // and the defocus phase still uses those angles independently.
    internal static (double Tangential, double Sagittal) ImagePlaneFourierFNumbers(
        Optic optic, (double Hx, double Hy) field, Wavelength wave, bool aiming)
    {
        if (optic.UseExitPupilFNumber)
        {
            var value = optic.Paraxial.ExitPupilFNumber();
            return (value, value);
        }
        var image = optic.SurfaceGroup.Items[^1];
        var frame = image.CoordinateSystem;
        var imageIndex = image.MaterialAfter.RefractiveIndex(wave.Nanometers);
        foreach (var zone in new[] { 1d, .99, .95, .9, .8, .7, .5, .25 })
        {
            if (!TryWorkingPupilDirections(optic, field, wave, aiming, zone, out var directions)) continue;
            var local = directions.Select(d => frame.ToLocalDirection(d) / d.Length).ToArray();
            var ty1 = local[1].Y - local[0].Y;
            var ty2 = local[2].Y - local[0].Y;
            var sx1 = local[3].X - local[0].X;
            var sx2 = local[4].X - local[0].X;
            if (image.Geometry is TiltedPlaneGeometry tilted)
            {
                // A TILTSURF changes the detector tangent axes without rotating
                // its coordinate frame. Project phase gradients onto unit axes.
                var lengthX = double.Hypot(1, tilted.TangentX);
                var lengthY = double.Hypot(1, tilted.TangentY);
                ty1 = (ty1 + tilted.TangentY * (local[1].Z - local[0].Z)) / lengthY;
                ty2 = (ty2 + tilted.TangentY * (local[2].Z - local[0].Z)) / lengthY;
                sx1 = (sx1 + tilted.TangentX * (local[3].Z - local[0].Z)) / lengthX;
                sx2 = (sx2 + tilted.TangentX * (local[4].Z - local[0].Z)) / lengthX;
            }
            var t = imageIndex * (Math.Abs(ty1) + Math.Abs(ty2)) / zone;
            var s = imageIndex * (Math.Abs(sx1) + Math.Abs(sx2)) / zone;
            if (double.IsFinite(t) && t > 1e-30 && double.IsFinite(s) && s > 1e-30) return (1 / t, 1 / s);
        }
        throw new AnalysisDataUnavailableException("Fourier MTF", "the image-plane pupil frequency scale is not finite and positive");
    }
}

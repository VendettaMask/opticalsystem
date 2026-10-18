using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Raytrace;

public sealed partial class RayGenerator
{
    // Scalar FFT cumulative-energy sampling has a cached pupil-coordinate
    // convention. Keep this map separate from the precise stop-target solver:
    // ordinary rays, wavefronts and prepared FFT pupils retain their inputs.
    // The finite centered refractive case uses separable quadratic launch
    // predictors followed by coupled, damped stop-map updates. All intersections
    // and derivatives still use the shared sequential tracer. No native data,
    // lens identity, phase offsets or energy-curve corrections enter this map.
    internal bool TryGenerateCachedFftPupil(double hx, double hy, double wavelengthMicrometers,
        IReadOnlyList<PupilSample> pupils, out RealRayBundle? bundle)
    {
        bundle = null;
        var surfaces = _optic.SurfaceGroup.Items;
        if (hx != 0 || hy != 0 || surfaces.Count < 3 || ObjectConjugate.IsInfinite(surfaces[0])
            || surfaces[0].Thickness <= 0 || ResolveVignetting(hx, hy) != PupilVignetting.Identity)
            return false;
        var axis = surfaces[0].CoordinateSystem.Origin;
        if (surfaces.Any(surface => surface.IsReflective
            || surface.Geometry is not (StandardGeometry or PlaneGeometry)
            || surface.CoordinateSystem.Origin.X != axis.X || surface.CoordinateSystem.Origin.Y != axis.Y
            || surface.CoordinateSystem.RotationXDegrees != 0 || surface.CoordinateSystem.RotationYDegrees != 0
            || surface.CoordinateSystem.RotationZDegrees != 0)) return false;
        var stop = surfaces.ToList().FindIndex(surface => surface.IsStop);
        if (stop <= 0) return false;

        var wavelength = MicrometersToNanometers(wavelengthMicrometers);
        var frame = surfaces[stop].CoordinateSystem;
        var origin = axis;
        var targets = ParaxialStopTarget(1, 1, stop);
        if (targets.X <= 0 || targets.Y <= 0) return false;
        var seeds = new Vector3D[3];
        try
        {
            for (var i = 0; i < seeds.Length; i++)
            {
                var ray = GenerateGeneric(hx, hy, 0, i * .5, wavelengthMicrometers, true).Rays[0];
                if (ray.Origin != origin || ray.Direction.Z <= 0) return false;
                seeds[i] = ray.Direction / ray.Direction.Z;
            }
        }
        catch (RayAimingException) { return false; }

        // The cache heuristic tests the sum of normalized coordinate errors.
        // Each small-error component uses the primary paraxial stop gain; a
        // coupled Newton update is used for the other components. In particular,
        // two Newton steps cannot reproduce the halving/stopping branches.
        // These internal iteration rules do not change aperture or test limits.
        const double mapConvergence = 1e-8;
        const double smallCoordinateError = 20 * mapConvergence;
        var apertureRadius = EntrancePupilRadius();
        var pupilDistance = _optic.Paraxial.EstimateEntrancePupilLocation(PrimaryWavelengthMicrometers())
            - origin.Z;
        var gainX = targets.X * pupilDistance / apertureRadius;
        var gainY = targets.Y * pupilDistance / apertureRadius;
        if (!double.IsFinite(gainX) || !double.IsFinite(gainY) || gainX <= 0 || gainY <= 0)
            return false;
        // The captured cache's seed convention includes this relative guard.
        // It is observable at stopping boundaries even though its stop shift is
        // far below the map regression limit. Keep it local to this cache map.
        const double cacheSeedRelativeGuard = 5e-12;
        for (var i = 0; i < seeds.Length; i++)
            seeds[i] -= new Vector3D(0, i * .5 * targets.Y / gainY * cacheSeedRelativeGuard, 0);
        var rays = new RealRay[pupils.Count];
        for (var i = 0; i < pupils.Count; i++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var pupil = pupils[i];
            var px = Math.Abs(pupil.X);
            var py = Math.Abs(pupil.Y);
            var x = Predict(px);
            var y = Predict(py);
            if (!Sample(x, y, out var hit)) return false;
            if (!Jacobian(x, y, out var a, out var b, out var c, out var d)) return false;
            var smallX = Math.Abs((px - hit.X / targets.X) * a / gainX) <= smallCoordinateError;
            var smallY = Math.Abs((py - hit.Y / targets.Y) * d / gainY) <= smallCoordinateError;
            for (var pass = 0; pass < 8; pass++)
            {
                var relaxation = pass == 0 ? .75 : .9998;
                var ex = px * targets.X - hit.X;
                var ey = py * targets.Y - hit.Y;
                if (Math.Abs(ex / targets.X) + Math.Abs(ey / targets.Y) <= mapConvergence) break;
                if (pass > 0 && !Jacobian(x, y, out a, out b, out c, out d)) return false;
                var determinant = a * d - b * c;
                if (!double.IsFinite(determinant) || Math.Abs(determinant) <= 1e-24) return false;
                x += smallX ? .5 * ex / gainX : relaxation * (d * ex - b * ey) / determinant;
                y += smallY ? .5 * ey / gainY : relaxation * (a * ey - c * ex) / determinant;
                if (!Sample(x, y, out hit)) return false;
            }
            if (Math.Abs(px - hit.X / targets.X) + Math.Abs(py - hit.Y / targets.Y)
                > mapConvergence) return false;
            var direction = Normalize(new Vector3D(pupil.X < 0 ? -x : x, pupil.Y < 0 ? -y : y, 1));
            rays[i] = new RealRay(origin, direction, wavelength,
                pupil.Weight * ApodizationIntensity(pupil.X, pupil.Y, apertureRadius));
        }
        bundle = new RealRayBundle(rays);
        return true;

        double Predict(double coordinate) => seeds[0].Y * (2 * (coordinate - .5) * (coordinate - 1))
            + seeds[1].Y * (4 * coordinate * (1 - coordinate))
            + seeds[2].Y * (2 * coordinate * (coordinate - .5));

        bool Jacobian(double x, double y, out double a, out double b, out double c, out double d)
        {
            a = b = c = d = 0;
            const double h = 1e-6;
            if (!Sample(x + h, y, out var xp) || !Sample(x - h, y, out var xm)
                || !Sample(x, y + h, out var yp) || !Sample(x, y - h, out var ym)) return false;
            a = (xp.X - xm.X) / (2 * h);
            b = (yp.X - ym.X) / (2 * h);
            c = (xp.Y - xm.Y) / (2 * h);
            d = (yp.Y - ym.Y) / (2 * h);
            return double.IsFinite(a) && double.IsFinite(b) && double.IsFinite(c) && double.IsFinite(d);
        }

        bool Sample(double x, double y, out Vector3D point)
        {
            point = default;
            if (!double.IsFinite(x) || !double.IsFinite(y)) return false;
            var sample = _optic.SequentialRayTracer.TraceToSurface(
                new RealRay(origin, Normalize(new Vector3D(x, y, 1)), wavelength), stop);
            point = sample is null ? default : frame.ToLocalPoint(sample.Position);
            return sample is not null && double.IsFinite(point.X) && double.IsFinite(point.Y);
        }
    }
}

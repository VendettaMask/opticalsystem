using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Raytrace;

public sealed partial class RayGenerator
{
    // Scalar afocal diffraction's finite parabasal convention. This is not the
    // physical stop solver or a general cached launch map. Only its positive
    // on-axis pupil probe and small physical-field chief are generated here.
    internal bool TryGenerateAfocalScaleProbes(out RealRayBundle? bundle)
    {
        bundle = null;
        var surfaces = _optic.SurfaceGroup.Items;
        if (!_optic.ImageSpaceAfocal || _optic.FieldDefinition != FieldDefinitionKind.Angle
            || surfaces.Count < 3 || !ObjectConjugate.IsInfinite(surfaces[0])
            || _optic.Fields.Any(f => PupilVignetting.FromField(f) != PupilVignetting.Identity)) return false;
        var axis = surfaces[1].CoordinateSystem.Origin;
        if (surfaces.Skip(1).Any(s => s.IsReflective
            || s.Geometry is not (StandardGeometry or PlaneGeometry)
            || s.CoordinateSystem.Origin.X != axis.X || s.CoordinateSystem.Origin.Y != axis.Y
            || s.CoordinateSystem.RotationXDegrees != 0 || s.CoordinateSystem.RotationYDegrees != 0)
            || surfaces.Skip(1).SkipLast(1).Any(s => s.CoordinateSystem.RotationZDegrees != 0)) return false;
        var stop = surfaces.ToList().FindIndex(s => s.IsStop);
        if (stop <= 0 || stop >= surfaces.Count - 1) return false;
        var wave = PrimaryWavelengthMicrometers();
        var wavelength = MicrometersToNanometers(wave);
        var target = _optic.Paraxial.TraceNormalizedPupil(0, [1], wave).Heights[stop][0];
        var gain = target / EntrancePupilRadius();
        if (!double.IsFinite(gain) || Math.Abs(gain) <= 1e-15) return false;
        bundle = new([Generate(0, 0, false), Generate(.01, 0, false), Generate(0, .001, true)]);
        return true;

        RealRay Generate(double fieldYDegrees, double pupilY, bool cached)
        {
            var initial = GenerateAtPhysicalField(0, fieldYDegrees, 0, pupilY, wave, false).Rays[0];
            if (!_optic.RayAimingEnabled) return initial;
            var y = initial.Origin.Y;
            if (cached && pupilY != 0)
            {
                var half = GenerateAtPhysicalField(0, 0, 0, .5, wave, true).Rays[0].Origin.Y - axis.Y;
                var full = GenerateAtPhysicalField(0, 0, 0, 1, wave, true).Rays[0].Origin.Y - axis.Y;
                y = axis.Y + (4 * half - full) * pupilY + 2 * (full - 2 * half) * pupilY * pupilY;
            }
            const double mapConvergence = 1e-8;
            const double derivativeStep = 1e-6;
            var hit = Sample(y);
            var derivative = Derivative(y);
            var small = Math.Abs((pupilY - hit / target) * derivative / gain) <= 20 * mapConvergence;
            for (var pass = 0; pass < 64; pass++)
            {
                ComputationCancellation.ThrowIfCancellationRequested();
                var residual = pupilY * target - hit;
                if (Math.Abs(residual / target) <= mapConvergence)
                    return new(new(initial.Origin.X, y, initial.Origin.Z), initial.Direction, wavelength);
                // These finite heuristic stopping branches define the scale
                // probes only; they do not relax any optical acceptance gate.
                derivative = Derivative(y);
                if (!double.IsFinite(derivative) || Math.Abs(derivative) <= 1e-15) break;
                y += small ? (pass == 0 ? .5 : .25) * residual / gain
                    : (pass == 0 ? .75 : .9998) * residual / derivative;
                hit = Sample(y);
            }
            throw new RayAimingException(surfaces[stop].Number, 64, Math.Abs(pupilY * target - hit));

            double Derivative(double coordinate) => (Sample(coordinate + derivativeStep) - Sample(coordinate - derivativeStep)) / (2 * derivativeStep);
            double Sample(double coordinate)
            {
                var ray = new RealRay(new(initial.Origin.X, coordinate, initial.Origin.Z), initial.Direction, wavelength);
                var sample = _optic.SequentialRayTracer.TraceToSurface(ray, stop);
                if (sample is not { Vignetted: false, Intensity: > 0 })
                    throw new RayAimingException(surfaces[stop].Number, 0, double.PositiveInfinity);
                return surfaces[stop].CoordinateSystem.ToLocalPoint(sample.Position).Y;
            }
        }
    }
}

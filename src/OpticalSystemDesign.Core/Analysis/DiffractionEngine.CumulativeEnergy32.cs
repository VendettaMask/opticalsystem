using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Raytrace;

namespace OpticalSystemDesign.Core.Analysis;

public static partial class DiffractionEngine
{
    private static bool TryComputeCircularEnergy32(Optic optic, (double Hx, double Hy) field,
        Wavelength wave, Wavelength reference, bool ignoreOpd, out (Wavelength Wavelength, PsfResult Result)[] spectrum)
    {
        spectrum = [];
        var referenceF = CircularEnergyFNumber(reference);
        var waveF = ReferenceEquals(wave, reference) ? referenceF : CircularEnergyFNumber(wave);
        if (referenceF is not { } scaleF || waveF is not { } selectedF) return false;
        const int sampling = 32;
        // Retain the image interval in micrometers, then form the ratio to the
        // default FFT interval. Canceling factors or converting units before
        // this ratio changes the four circular boundary samples.
        var imagePitch = wave.Micrometers * scaleF / Math.Sqrt(sampling);
        var defaultPitch = wave.Micrometers * selectedF * (sampling - 2) / (2 * sampling);
        var divisor = imagePitch / defaultPitch;
        var grid = new PupilGridSpecification(sampling, true, true, 1 / divisor)
        {
            CoordinateDivisor = divisor,
            ClipInBaseGrid = true,
            UseCachedFftStopMap = true
        };
        var sphere = CumulativeEnergyReferenceSphere(optic, field, reference);
        var result = ComputeFftPsfCore(optic, field, wave, sampling, 2 * sampling,
            false, true, 0, null, null, true, ignoreOpd, optic.RayAimingEnabled,
            reference, imagePitch, true, out _, sphere, grid);
        spectrum = [(wave, result with { SampleSpacingMicrometers = imagePitch })];
        return true;

        double? CircularEnergyFNumber(Wavelength wavelength)
        {
            Vector3D[] directions;
            PupilSample[] probes = [new(0, 0, 1), new(0, 1, 1), new(0, -1, 1), new(1, 0, 1), new(-1, 0, 1)];
            if (optic.RayAimingEnabled && optic.SequentialRayTracer.RayGenerator.TryGenerateCachedFftPupil(
                field.Hx, field.Hy, wavelength.Micrometers, probes, out var bundle))
            {
                // The coordinate cone and its FFT pupil must share the same
                // existing aiming map, including its checked cache semantics.
                var traced = bundle!.Rays.Select(ray => optic.SequentialRayTracer.TraceToSurface(ray,
                    optic.SurfaceGroup.Items.Count - 1)).ToArray();
                if (traced.Any(ray => ray is null || ray.Vignetted || ray.Intensity <= 0)) return null;
                directions = traced.Select(ray => ray!.Direction).ToArray();
            }
            else if (!TryWorkingPupilDirections(optic, field, wavelength, optic.RayAimingEnabled, 1, out directions)) return null;
            var chief = directions[0];
            var imageIndex = optic.SurfaceGroup.Items[^1].MaterialAfter.RefractiveIndex(wavelength.Nanometers);
            var squared = directions.Skip(1).Select(d =>
            {
                var cosine = Math.Clamp(chief.X * d.X + chief.Y * d.Y + chief.Z * d.Z, -1, 1);
                return imageIndex * imageIndex * (1 - cosine * cosine);
            }).ToArray();
            if (squared.Any(q => !double.IsFinite(q) || q <= 0)) return null;
            var tangential = (Math.Sqrt(squared[0]) + Math.Sqrt(squared[1])) / 2;
            var sagittal = (Math.Sqrt(squared[2]) + Math.Sqrt(squared[3])) / 2;
            if (Math.Abs(tangential - sagittal) > 1e-14 * Math.Max(tangential, sagittal)) return null;
            var f = .5 / Math.Sqrt(squared.Average());
            return double.IsFinite(f) && f <= 10000 ? f : null;
        }
    }
}

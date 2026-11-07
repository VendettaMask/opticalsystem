using System.Numerics;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis;

public static partial class DiffractionEngine
{
    internal static HuygensFourierSum.Plan? TryPlanBatchedHuygens(Optic optic, (double Hx, double Hy) field,
        Wavelength wavelength, int pupilSampling, int imageSize, double pitch)
    {
        if (pupilSampling < 2048 || !double.IsFinite(pitch) || pitch <= 0) return null;
        var distance = Math.Abs(optic.Paraxial.EstimateExitPupilLocation());
        return distance > 0 && double.IsFinite(distance)
            && !UsesSphericalFocusedHuygens(optic, wavelength, distance, imageSize, pitch, null)
            ? HuygensFourierSum.CreateBatchedPlan(pupilSampling, imageSize, field != (0d, 0d)) : null;
    }

    internal static PsfResult ComputeBatchedPlanarHuygens(Optic optic, (double Hx, double Hy) field,
        Wavelength wavelength, Wavelength referenceWavelength, int pupilSampling, int imageSize, double pitch,
        HuygensFourierSum.Plan plan)
    {
        var aimAtStop = optic.RayAimingEnabled;
        var frame = CreateHuygensImageFrame(optic, field, referenceWavelength, aimAtStop);
        var wavelengthMillimeters = wavelength.Micrometers * .001;
        var k = 2 * Math.PI / wavelengthMillimeters;
        var geometricChief = false;
        var separateNormalizer = field != (0d, 0d);
        var imageIndex = optic.SurfaceGroup.Items[^1].MaterialAfter.RefractiveIndex(wavelength.Nanometers);
        var idealAmplitude = separateNormalizer ? TracePupil((0d, 0d)).Sum(item => Amplitude(item.Sample)) : 0;
        var firstDirection = Vector3D.Zero;
        var allParallel = true;
        var emitted = 0;
        var result = HuygensFourierSum.EvaluateBatched(Emitters(), pupilSampling, imageSize, plan);
        if (!double.IsFinite(idealAmplitude) || idealAmplitude <= 0)
            throw new AnalysisDataUnavailableException("Huygens PSF", "no positive ideal pupil amplitude");

        var pupilDistance = Math.Abs(optic.Paraxial.EstimateExitPupilLocation());
        var pupilDiameter = Math.Abs(optic.Paraxial.EstimateExitPupilDiameter());
        var halfExtent = emitted > 1 && allParallel ? double.PositiveInfinity
            : FocusedHuygensHalfExtent(pupilSampling, wavelengthMillimeters, pupilDistance, pupilDiameter, imageIndex);
        var values = new double[imageSize, imageSize];
        for (var row = 0; row < imageSize; row++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var y = (row - imageSize / 2) * pitch;
            if (Math.Abs(y) >= halfExtent) continue;
            for (var column = 0; column < imageSize; column++)
            {
                var x = (column - imageSize / 2) * pitch;
                if (Math.Abs(x) >= halfExtent) continue;
                var footprint = HuygensCellSinc(Math.PI * x / halfExtent) * HuygensCellSinc(Math.PI * y / halfExtent);
                var amplitude = result.Field[row, column].Magnitude;
                values[row, column] = amplitude * amplitude * footprint * footprint / (idealAmplitude * idealAmplitude) * 100;
            }
        }
        return new PsfResult(values, pupilSampling, imageSize, WorkingFNumber(optic, field, wavelength, aimAtStop), pitch * 1000)
        {
            UseRayAiming = aimAtStop,
            FiniteCellHuygensUsed = true,
            GeometricChiefReferenceUsed = geometricChief,
            HuygensFourierChecks = [result.Diagnostics]
        };

        IEnumerable<(WavefrontSample Sample, double ReferenceRadius)> TracePupil((double Hx, double Hy) sourceField)
        {
            var geometricReference = false;
            var transmitted = false;
            double? referenceRadius = null;
            foreach (var batch in HuygensPupilNodes(pupilSampling).Chunk(HuygensFourierSum.PupilBatchSize))
            {
                ComputationCancellation.ThrowIfCancellationRequested();
                var wavefront = WavefrontEngine.GenerateChiefRayBatch(optic, sourceField, wavelength, batch, aimAtStop);
                if (wavefront.ImageRefractiveIndex != imageIndex || (referenceRadius.HasValue && referenceRadius != wavefront.Radius))
                    throw new AnalysisDataUnavailableException("Huygens PSF", "pupil batches did not share a phase reference");
                referenceRadius = wavefront.Radius;
                geometricReference |= wavefront.GeometricChiefReferenceUsed;
                foreach (var sample in wavefront.Samples)
                {
                    transmitted |= sample.Intensity > 0;
                    yield return (sample, wavefront.Radius);
                }
            }
            if (geometricReference && !transmitted)
                throw new AnalysisDataUnavailableException("Huygens PSF", "no transmitted pupil rays reached the image surface");
            geometricChief |= geometricReference;
        }

        IEnumerable<HuygensFourierEmitter> Emitters()
        {
            foreach (var (sample, radius) in TracePupil(field))
            {
                var amplitude = Amplitude(sample);
                if (!separateNormalizer) idealAmplitude += amplitude;
                if (amplitude <= 0 || sample.ImageDirection is not { Length: > 0 } direction) continue;
                if (emitted == 0) firstDirection = direction;
                else if (allParallel) allParallel = (direction - firstDirection).Length <= 1e-12;
                emitted++;
                var pupil = new Vector3D(sample.PupilX, sample.PupilY, sample.PupilZ);
                var phasePath = -sample.OpdWaves * wavelengthMillimeters
                    + imageIndex * (Dot(frame.Center - pupil, direction) - radius);
                yield return new HuygensFourierEmitter(Complex.FromPolarCoordinates(amplitude, k * phasePath),
                    k * imageIndex * pitch * Dot(frame.TangentX, direction),
                    k * imageIndex * pitch * Dot(frame.TangentY, direction));
            }
        }

        static double Amplitude(WavefrontSample sample) => sample.Intensity > 0
            ? FieldAmplitudeFromPower(sample.LaunchIntensity ?? sample.Intensity) : 0;
    }
}

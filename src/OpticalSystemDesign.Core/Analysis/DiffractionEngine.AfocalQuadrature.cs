using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis;

public static partial class DiffractionEngine
{
    private static PsfResult ComputeGaussianAfocalHuygensPsf(Optic optic, (double Hx, double Hy) field,
        Wavelength wavelength, int pupilSampling, int imageSize, double pitchMilliradians,
        bool aimAtStop, double defocus, Wavelength? referenceWavelength,
        (double X, double Y)? imageCenterOffset, bool useParaxialPupilDefocus,
        IReadOnlyList<PupilSample>? quadratureNodes = null,
        bool boundaryReference = false, IReadOnlyList<PupilSample>? normalizationNodes = null)
    {
        if (!double.IsFinite(defocus)) throw new ArgumentOutOfRangeException(nameof(defocus));
        var nodes = quadratureNodes ?? ApertureSampler.GenerateHuygensGaussianQuadrature(pupilSampling);
        WavefrontResult Trace((double Hx, double Hy) position, IReadOnlyList<PupilSample> samples) =>
            WavefrontEngine.GenerateQuadratureHuygensSamples(optic, position, wavelength, samples,
                aimAtStop, referenceWavelength, boundaryReference);
        var wavefront = Trace(field, nodes);
        var referenceNodes = normalizationNodes ?? nodes;
        var normalization = field.Hx == 0 && field.Hy == 0 ? wavefront : Trace((0, 0), referenceNodes);
        // Preserve the scalar analysis convention: physical apertures and launch
        // apodization remain active, material/coating attenuation is not applied.
        // Weights are integration measures and multiply FIELD amplitude once;
        // they must not be traced as powers then square-rooted.
        static double Amplitude(WavefrontSample sample) => sample.Intensity > 0
            ? FieldAmplitudeFromPower(sample.LaunchIntensity ?? sample.Intensity) : 0;
        double idealAmplitude = 0;
        for (var i = 0; i < referenceNodes.Count; i++) idealAmplitude += referenceNodes[i].Weight * Amplitude(normalization.Samples[i]);
        if (!double.IsFinite(idealAmplitude) || idealAmplitude <= 0)
            throw new AnalysisDataUnavailableException("Huygens PSF", "no transmitting on-axis reference pupil for Gaussian integration");
        var origin = wavefront.AfocalReferencePlaneOrigin
            ?? throw new AnalysisDataUnavailableException("Huygens PSF", "afocal reference plane is missing");
        var frame = optic.SurfaceGroup.Items[^1].CoordinateSystem;
        var axisX = frame.ToGlobalDirection(new Vector3D(1, 0, 0));
        var axisY = frame.ToGlobalDirection(new Vector3D(0, 1, 0));
        var wavelengthMm = wavelength.Micrometers / 1000;
        var k = 2 * Math.PI / wavelengthMm;
        var emitters = new List<(double X, double Y, double Phase, double Amplitude)>();
        for (var i = 0; i < nodes.Count; i++)
        {
            var sample = wavefront.Samples[i]; var amplitude = nodes[i].Weight * Amplitude(sample);
            if (amplitude == 0) continue;
            var offset = new Vector3D(sample.PupilX, sample.PupilY, sample.PupilZ) - origin;
            var opdWaves = sample.OpdWaves + (useParaxialPupilDefocus
                ? ImageSpaceAnalysisSupport.AfocalDefocusOpdWaves(sample, wavelength, defocus, wavefront.AfocalPupilDiameterMillimeters)
                : -defocus * Dot(offset, offset) / (2000 * wavelengthMm));
            emitters.Add((Dot(offset, axisX), Dot(offset, axisY), -2 * Math.PI * opdWaves, amplitude / idealAmplitude));
        }
        var values = new double[imageSize, imageSize]; var center = imageSize / 2;
        for (var row = 0; row < imageSize; row++)
            for (var column = 0; column < imageSize; column++)
            {
                ComputationCancellation.ThrowIfCancellationRequested();
                var tx = ((column - center) * pitchMilliradians + (imageCenterOffset?.X ?? 0)) / 1000;
                var ty = ((row - center) * pitchMilliradians + (imageCenterOffset?.Y ?? 0)) / 1000;
                double real = 0, imaginary = 0, realCorrection = 0, imaginaryCorrection = 0;
                var index = 0;
                foreach (var emitter in emitters)
                {
                    if ((index++ & 255) == 0) ComputationCancellation.ThrowIfCancellationRequested();
                    var phase = emitter.Phase - k * wavefront.ImageRefractiveIndex * (emitter.X * tx + emitter.Y * ty);
                    var (sin, cos) = Math.SinCos(phase);
                    Add(emitter.Amplitude * cos, ref real, ref realCorrection);
                    Add(emitter.Amplitude * sin, ref imaginary, ref imaginaryCorrection);
                }
                var value = 100 * (real * real + imaginary * imaginary);
                if (!double.IsFinite(value)) throw new AnalysisDataUnavailableException("Huygens PSF", "nonfinite Gaussian pupil integral");
                values[row, column] = value;
            }
        return new PsfResult(values, pupilSampling, imageSize, WorkingFNumber(optic, field, wavelength, aimAtStop),
            pitchMilliradians, SampleSpacingUnit: AnalysisAxisUnit.Milliradian)
        {
            UseRayAiming = aimAtStop,
            AfocalIntegration = AfocalHuygensIntegration.GaussianPupil,
            PupilQuadratureNodeCount = nodes.Count,
            HuygensReferenceAmplitude = idealAmplitude,
            GeometricChiefReferenceUsed = wavefront.GeometricChiefReferenceUsed || normalization.GeometricChiefReferenceUsed,
            FiniteCellHuygensUsed = false
        };
    }

    private static void Add(double value, ref double sum, ref double correction)
    {
        var adjusted = value - correction; var next = sum + adjusted;
        correction = (next - sum) - adjusted; sum = next;
    }
}

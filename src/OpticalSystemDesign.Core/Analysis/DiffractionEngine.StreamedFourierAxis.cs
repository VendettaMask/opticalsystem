using System.Numerics;
using OpticalSystemDesign.Core.Domain;

namespace OpticalSystemDesign.Core.Analysis;

public static partial class DiffractionEngine
{
    internal static (double[] Values, double Strehl, double WorkingFNumber, double UnaberratedPeakRatio,
        FftPupilSampling Phase, StreamedFourierPupilDiagnostics Diagnostics) ComputeStreamedFocalFftAxis(
        Optic optic, (double Hx, double Hy) field, Wavelength wavelength, int count,
        IReadOnlyList<double> coordinates, bool horizontal, long maximumTraceNodes)
    {
        const string analysis = "FFT PSF Cross Section";
        if (optic.ImageSpaceAfocal || optic.RayAimingEnabled || count < 256 || count > MaximumStreamedPhysicalPsfSampling
            || count % 256 != 0 || coordinates.Count is < 2 or > 401 || coordinates.Any(x => !double.IsFinite(x)))
            throw new AnalysisDataUnavailableException(analysis, "streamed Fourier axes require an eligible focal scalar pupil and a fixed physical interval");
        var spacing = (coordinates[^1] - coordinates[0]) / (coordinates.Count - 1);
        if (!(spacing > 0) || !double.IsFinite(spacing) || coordinates.Where((value, i) =>
            Math.Abs(value - (coordinates[0] + i * spacing)) > Math.Max(1e-10, spacing * 1e-10)).Any())
            throw new AnalysisDataUnavailableException(analysis, "streamed Fourier axes require equally spaced physical coordinates");
        if (!TryWorkingFNumbersAtPupilZone(optic, field, wavelength, false,
            WorkingApertureReduction.RootMeanSquare, 1, out var axes))
            throw new AnalysisDataUnavailableException(analysis, "streamed Fourier axes require a complete finite marginal cone");

        var source = PlanStreamedPupil(new(count, true, true, 1), maximumTraceNodes, analysis);
        var conjugateScale = wavelength.Micrometers * (horizontal ? axes.Sagittal : axes.Tangential) * (count - 2);
        if (Math.Max(Math.Abs(coordinates[0]), Math.Abs(coordinates[^1])) >= conjugateScale / 2)
            throw new AnalysisDataUnavailableException(analysis, "the requested physical profile exceeds the sampled Fourier interval");
        var bufferLength = (int)BitOperations.RoundUpToPowerOf2((uint)(count + coordinates.Count - 1));
        var interval = new PhysicalFourierInterval(count, coordinates.Count, spacing / conjugateScale,
            coordinates[0] / conjugateScale, (count - 1) / 2d, boundedSize: bufferLength);
        var marginal = new Complex[count];
        // The perpendicular image coordinate is zero on a central profile.
        // Sum the pupil rows or columns before evaluating the physical axis.
        var traced = TraceStreamedPupil(optic, field, wavelength, source, analysis, useLaunchAmplitude: true, batch =>
        {
            foreach (var (row, pupil) in batch)
            {
                if (horizontal)
                {
                    for (var column = 0; column < count; column++) marginal[column] += pupil[column];
                }
                else
                {
                    for (var column = 0; column < count; column++) marginal[row] += pupil[column];
                }
            }
        });
        if (!(traced.IdealAmplitude > 0) || !double.IsFinite(traced.IdealAmplitude))
            throw new AnalysisDataUnavailableException(analysis, "no finite illuminated streamed Fourier pupil");
        var output = interval.Evaluate(marginal).Select(Intensity).ToArray();
        var strehl = Intensity(marginal.Aggregate(Complex.Zero, (sum, value) => sum + value));
        var idealPeak = traced.IdealAmplitude / traced.TransmittedNodes;
        var butterflies = 3L * bufferLength * BitOperations.Log2((uint)bufferLength) / 2;
        var majorCells = (4L * source.RowsPerBatch + 12) * count + 2L * bufferLength;
        return (output, strehl, CombineWorkingFNumbers(axes, optic.UseExitPupilFNumber), idealPeak * idealPeak,
            traced.Phase, new(bufferLength, source.RowsPerBatch, source.TraceNodes, traced.TransmittedNodes, butterflies, majorCells));

        double Intensity(Complex value)
        {
            var relative = value.Magnitude / traced.IdealAmplitude;
            return relative * relative;
        }
    }
}

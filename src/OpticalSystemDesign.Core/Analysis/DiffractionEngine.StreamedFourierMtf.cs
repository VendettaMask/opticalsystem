using System.Numerics;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis;

public static partial class DiffractionEngine
{
    internal const int MaximumStreamedMtfSampling = 6144;
    private const long MaximumStreamedMtfButterflies = 3_000_000_000;

    internal static (MtfResult Mtf, FftPupilSampling Phase, StreamedFourierPupilDiagnostics Diagnostics)
        ComputeStreamedFocalFftMtf(Optic optic, (double Hx, double Hy) field, Wavelength wavelength,
            int count, double maximumFrequency, long maximumTraceNodes)
    {
        const string analysis = "FFT MTF";
        const int blockRows = 256;
        const int workers = 4;
        if (optic.ImageSpaceAfocal || optic.RayAimingEnabled || count < blockRows
            || count > MaximumStreamedMtfSampling || count % blockRows != 0
            || !(maximumFrequency > 0) || !double.IsFinite(maximumFrequency))
            throw new AnalysisDataUnavailableException(analysis,
                "streamed Fourier refinement requires a finite focal frequency interval and unaimed rays");
        if (!TryWorkingFNumbersAtPupilZone(optic, field, wavelength, false,
            WorkingApertureReduction.RootMeanSquare, 1, out var workingAxes))
            throw new AnalysisDataUnavailableException(analysis, "streamed Fourier refinement requires a complete finite marginal cone");
        var axes = ImagePlaneFourierFNumbers(optic, field, wavelength, false);
        var requestedLag = maximumFrequency * (count - 1) * wavelength.Micrometers * .001
            * Math.Max(axes.Tangential, axes.Sagittal);
        if (!(requestedLag > 0) || requestedLag > count - 1)
            throw new AnalysisDataUnavailableException(analysis, "the frequency interval exceeds the finite pupil cutoff");

        // The natural cubic on uniform OTF knots attenuates a remote endpoint
        // perturbation by 2 - sqrt(3) per knot. Keep 32 knots past the interval
        // (or the actual last knot) before applying the existing complex spline.
        var haloRows = Math.Min(count - 1, (int)Math.Ceiling(requestedLag) + 32);
        var slabRows = blockRows + haloRows;
        var horizontalSize = (int)BitOperations.RoundUpToPowerOf2((uint)(2 * count));
        var verticalSize = (int)BitOperations.RoundUpToPowerOf2((uint)(2 * slabRows));
        var source = PlanStreamedPupil(new(count, true, true, 1), maximumTraceNodes, analysis);
        var butterflies = Butterflies(horizontalSize) * (source.ActiveRows + 1L)
            + Butterflies(verticalSize) * (2L * count * (count / blockRows) + 1);
        var majorCells = (long)count * slabRows + (4L * source.RowsPerBatch + 8) * count
            + (workers + 1L) * (2L * horizontalSize + 3L * verticalSize);
        if (majorCells > AnalysisResourceLimits.MaximumAggregateGridCells || butterflies > MaximumStreamedMtfButterflies)
            throw new AnalysisDataUnavailableException(analysis, "streamed Fourier refinement exceeds its transform or allocation budget");

        var slab = new Complex[]?[slabRows];
        var rowPower = CreateBuffers(horizontalSize);
        var columnPower = CreateBuffers(verticalSize);
        var horizontal = CreateBuffers(horizontalSize);
        var vertical = CreateBuffers(verticalSize);
        var halo = CreateBuffers(verticalSize);
        var parallel = new ParallelOptions { MaxDegreeOfParallelism = workers };
        var blockStart = 0;
        var traced = TraceStreamedPupil(optic, field, wavelength, source, analysis, useLaunchAmplitude: false, batch =>
        {
            Parallel.For(0, workers, parallel, worker =>
            {
                var buffer = horizontal[worker];
                for (var row = worker; row < batch.Count; row += workers)
                {
                    Array.Clear(buffer);
                    batch[row].Pupil.CopyTo(buffer, 0);
                    Fft(buffer);
                    for (var i = 0; i < buffer.Length; i++) rowPower[worker][i] += Power(buffer[i]);
                }
            });
            foreach (var (row, pupil) in batch)
            {
                while (row >= blockStart + blockRows) FinishBlock();
                slab[haloRows + row - blockStart] = pupil;
            }
        });
        while (blockStart < count) FinishBlock();
        if (!(traced.TotalPower > 0) || !double.IsFinite(traced.TotalPower))
            throw new AnalysisDataUnavailableException(analysis, "no finite illuminated streamed Fourier pupil");

        var metadata = new PsfResult(new double[1, 1], count, 1,
            CombineWorkingFNumbers(workingAxes, optic.UseExitPupilFNumber), 1, FrequencySampleCount: count - 1)
        {
            TangentialFourierFNumber = axes.Tangential,
            SagittalFourierFNumber = axes.Sagittal
        };
        var mtf = CreateFftMtfResult(metadata, optic, wavelength, Correlation(columnPower), Correlation(rowPower));
        return (mtf, traced.Phase, new(horizontalSize, source.RowsPerBatch,
            source.TraceNodes, traced.TransmittedNodes, butterflies, majorCells));

        void FinishBlock()
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            Parallel.For(0, workers, parallel, worker =>
            {
                var whole = vertical[worker];
                var prefix = halo[worker];
                var power = columnPower[worker];
                for (var column = worker; column < count; column += workers)
                {
                    Array.Clear(whole);
                    Array.Clear(prefix);
                    for (var row = 0; row < slab.Length; row++)
                        if (slab[row] is { } pupil) whole[row] = pupil[column];
                    Array.Copy(whole, prefix, haloRows);
                    Fft(whole);
                    Fft(prefix);
                    // This difference counts each pair once, in the block
                    // containing its later row. The zero padding excludes wrap.
                    for (var i = 0; i < power.Length; i++) power[i] += Power(whole[i]) - Power(prefix[i]);
                }
            });
            Array.Copy(slab, blockRows, slab, 0, haloRows);
            Array.Clear(slab, haloRows, blockRows);
            blockStart += blockRows;
        }

        Complex[] Correlation(Complex[][] spectra)
        {
            var total = new Complex[spectra[0].Length];
            foreach (var spectrum in spectra)
                for (var i = 0; i < total.Length; i++) total[i] += spectrum[i];
            Fft(total);
            var dc = total[0].Real;
            if (!(dc > 0) || !double.IsFinite(dc))
                throw new AnalysisDataUnavailableException(analysis, "the streamed pupil correlation has no finite DC power");
            return total.Take(haloRows + 1).Select(value => value / dc).ToArray();
        }

        static Complex[][] CreateBuffers(int size) => Enumerable.Range(0, workers).Select(_ => new Complex[size]).ToArray();
        static double Power(Complex value) => value.Real * value.Real + value.Imaginary * value.Imaginary;
        static long Butterflies(int size) => (long)size * BitOperations.Log2((uint)size) / 2;
    }
}

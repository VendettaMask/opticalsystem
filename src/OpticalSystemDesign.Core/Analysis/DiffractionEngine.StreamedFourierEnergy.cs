using System.Numerics;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Apodization;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Propagation;

namespace OpticalSystemDesign.Core.Analysis;

public static partial class DiffractionEngine
{
    private const long MaximumStreamedEnergyButterflies = 2_500_000_000;
    internal const int MaximumSymmetricCentroidEnergySampling = 5888;

    internal sealed record StreamedFourierEnergy(FftCumulativeKnotGrid? Actual, FftCumulativeKnotGrid? Ideal,
        (double X, double Y) Center, FftPupilSampling Phase, StreamedFourierPupilDiagnostics Diagnostics,
        bool UsedSagittalSymmetry);

    internal static bool HasSagittalEnergySymmetry(Optic optic, (double Hx, double Hy) field)
    {
        var sagittal = new Vector3D(1, 0, 0);
        return field.Hx == 0 && optic.Apodization is null or UniformApodization or GaussianApodization or ZemaxApodization
            && optic.SequentialRayTracer.RayGenerator.GetPupilVignetting(field.Hx, field.Hy) == PupilVignetting.Identity
            && optic.SurfaceGroup.Items.All(surface => surface.CoordinateSystem.Origin.X == 0
                && surface.CoordinateSystem.ToGlobalDirection(sagittal) == sagittal
                && surface.Geometry is PlaneGeometry or StandardGeometry or EvenAsphereGeometry or TiltedPlaneGeometry { TangentX: 0 }
                && surface.InteractionModel is RefractiveReflectiveInteractionModel or CoordinateReferenceInteractionModel
                && surface.PhysicalAperture is null or CircularAperture or AnnularAperture
                && surface.CoatingModel is NoneCoatingModel && surface.ScatteringModel is null
                && surface.MaterialBefore.PropagationModel is HomogeneousPropagationModel
                && surface.MaterialAfter.PropagationModel is HomogeneousPropagationModel);
    }

    internal static StreamedFourierEnergy ComputeStreamedFocalFftEnergy(Optic optic, (double Hx, double Hy) field,
        Wavelength wave, int count, double maximumRadius, int knots, bool useCentroid, long maximumTraceNodes)
    {
        const string analysis = "Diffraction Encircled Energy";
        const int workers = 4;
        if (optic.ImageSpaceAfocal || optic.RayAimingEnabled || count < 256 || count > MaximumStreamedPhysicalPsfSampling
            || count % 256 != 0 || !(maximumRadius > 0) || !double.IsFinite(maximumRadius) || knots < 3)
            throw new AnalysisDataUnavailableException(analysis, "streamed energy requires a finite scalar pupil and an explicit radial readout");
        if (CumulativeEnergyCone(optic, field, wave) is not { } cone)
            throw new AnalysisDataUnavailableException(analysis, "streamed energy requires a complete finite marginal cone");

        var symmetry = useCentroid && count > 4096 && HasSagittalEnergySymmetry(optic, field);
        var passes = useCentroid ? (symmetry ? 2 : 3) : 1;
        var source = PlanStreamedPupil(new(count, true, true, 1), maximumTraceNodes / passes, analysis);
        var periodX = wave.Micrometers * (count - 2) * (.5 / cone.SagittalMean);
        var periodY = wave.Micrometers * (count - 2) * (.5 / cone.TangentialMean);
        if (!(periodX > 0) || !(periodY > 0) || !double.IsFinite(periodX) || !double.IsFinite(periodY))
            throw new AnalysisDataUnavailableException(analysis, "streamed energy has no finite positive Fourier period");
        // High-density symmetric tiers spend their samples on pupil phase.
        // Their window still has roughly 32 samples per diffraction scale.
        var scaleSamples = Math.Sqrt(count) / (symmetry ? 2.25 : 1);
        var pitch = Math.Min(Math.Min(periodX, periodY) / (count - 2) / scaleSamples, maximumRadius / 160);
        var readoutRadius = Math.Ceiling(maximumRadius / pitch + 1);
        if (!double.IsFinite(readoutRadius) || 2 * readoutRadius + 2 > AnalysisResourceLimits.MaximumFftGridSize)
            throw new AnalysisDataUnavailableException(analysis, "streamed energy exceeds its physical image window budget");
        var half = (int)readoutRadius;
        var side = 2 * half + 2;
        var intervalSize = (int)BitOperations.RoundUpToPowerOf2((uint)(count + side - 1));
        var momentSize = (int)BitOperations.RoundUpToPowerOf2((uint)(2 * count));
        var butterflies = 2L * (source.ActiveRows + side + 1) * intervalSize * BitOperations.Log2((uint)intervalSize)
            + (passes - 1L) * source.ActiveRows * momentSize * BitOperations.Log2((uint)momentSize) / 2;
        var cells = 2L * count * side + 2L * side * side + (4L * source.RowsPerBatch + 8) * count
            + workers * (2L * intervalSize + momentSize);
        if (cells > AnalysisResourceLimits.MaximumAggregateGridCells || butterflies > MaximumStreamedEnergyButterflies)
            throw new AnalysisDataUnavailableException(analysis, "streamed energy exceeds its transform or allocation budget");

        var reference = optic.Wavelengths.FirstOrDefault(w => w.IsPrimary) ?? wave;
        var sphere = CumulativeEnergyReferenceSphere(optic, field, reference);
        var parallel = new ParallelOptions { MaxDegreeOfParallelism = workers };
        var center = (X: 0d, Y: 0d);
        var tracedNodes = 0L;
        if (useCentroid)
        {
            var x = 0d;
            if (!symmetry)
            {
                var horizontal = MeasureMoment(false, periodX);
                if (horizontal.Trace.Phase.Status != FftPhaseSamplingStatus.ResolvedAtNodes) return Unresolved(horizontal.Trace);
                x = horizontal.Center;
            }
            var nyquist = symmetry ? new Complex[count] : null;
            var (y, vertical) = MeasureMoment(true, periodY, nyquist);
            if (vertical.Phase.Status != FftPhaseSamplingStatus.ResolvedAtNodes) return Unresolved(vertical);
            // Sagittal reflection pairs all positive and negative image bins.
            // Retain the single positive Nyquist bin of the independent period.
            if (nyquist is not null) x = nyquist.Sum(Power) * periodX / (2 * momentSize * vertical.TotalPower);
            center = (x, y);
        }

        var firstX = FirstPixel(center.X);
        var firstY = FirstPixel(center.Y);
        var actualX = Interval(periodX, firstX);
        var actualY = Interval(periodY, firstY);
        var idealX = useCentroid ? Interval(periodX, -half) : actualX;
        var idealY = useCentroid ? Interval(periodY, -half) : actualY;
        var actualRows = new Complex[count, side];
        var idealRows = new Complex[count, side];
        var traced = Trace(batch => Parallel.For(0, batch.Count, parallel, i =>
        {
            var (row, pupil) = batch[i];
            var actual = actualX.Evaluate(pupil);
            for (var column = 0; column < count; column++) pupil[column] = pupil[column].Magnitude;
            var ideal = idealX.Evaluate(pupil);
            for (var column = 0; column < side; column++)
            {
                actualRows[row, column] = actual[column];
                idealRows[row, column] = ideal[column];
            }
        }));
        if (traced.Phase.Status != FftPhaseSamplingStatus.ResolvedAtNodes) return Unresolved(traced);

        // Orthogonality on the full independent Fourier periods gives the
        // total power for the unscaled pupil sum. The local grid uses pitch²
        // per pixel, so apply the same area factor to the denominator.
        var total = traced.TotalPower * (periodX / pitch) * (periodY / pitch);
        var actualGrid = new FftCumulativeKnotGrid(ReadWindow(actualRows, actualY), firstX, firstY, pitch,
            center, total, pitch, maximumRadius, knots, "encircled", intervalSize);
        var idealGrid = new FftCumulativeKnotGrid(ReadWindow(idealRows, idealY), -half, -half, pitch,
            (0, 0), total, pitch, maximumRadius, knots, "encircled", intervalSize);
        return new(actualGrid, idealGrid, center, traced.Phase, Diagnostics(traced), symmetry);

        StreamedPupilTrace Trace(Action<IReadOnlyList<(int Row, Complex[] Pupil)>> read, bool transpose = false)
        {
            var result = TraceStreamedPupil(optic, field, wave, source, analysis, useLaunchAmplitude: false,
                read, transpose, sphere, reference);
            tracedNodes += source.TraceNodes;
            if (!(result.TotalPower > 0) || !double.IsFinite(result.TotalPower))
                throw new AnalysisDataUnavailableException(analysis, "no finite illuminated energy pupil");
            return result;
        }

        (double Center, StreamedPupilTrace Trace) MeasureMoment(bool transpose, double period, Complex[]? nyquist = null)
        {
            var sums = new double[count];
            var buffers = Enumerable.Range(0, workers).Select(_ => new Complex[momentSize]).ToArray();
            var result = Trace(batch =>
            {
                Parallel.For(0, workers, parallel, worker =>
                {
                    for (var i = worker; i < batch.Count; i += workers)
                    {
                        var (row, pupil) = batch[i];
                        var buffer = buffers[worker];
                        Array.Clear(buffer);
                        pupil.CopyTo(buffer, 0);
                        Fft(buffer);
                        sums[row] = FourierMarginalFirstMoment(buffer);
                    }
                });
                if (nyquist is null) return;
                foreach (var (row, pupil) in batch)
                {
                    var sign = (row & 1) == 0 ? 1d : -1d;
                    for (var column = 0; column < count; column++) nyquist[column] += sign * pupil[column];
                }
            }, transpose);
            return (sums.Sum() / (momentSize * (double)momentSize * result.TotalPower) * period, result);
        }

        PhysicalFourierInterval Interval(double period, int first)
        {
            if (Math.Max(Math.Abs(first * pitch), Math.Abs((first + side - 1) * pitch)) >= period / 2)
                throw new AnalysisDataUnavailableException(analysis, "the energy window lies outside the independent Fourier interval");
            return new(count, side, pitch / period, first * pitch / period, (count - 1) / 2d, intervalSize);
        }

        int FirstPixel(double coordinate)
        {
            var pixel = Math.Floor(coordinate / pitch);
            if (!double.IsFinite(pixel) || Math.Abs(pixel) > int.MaxValue - side)
                throw new AnalysisDataUnavailableException(analysis, "the energy centroid is outside the bounded image coordinate range");
            return (int)pixel - half;
        }

        Complex[,] ReadWindow(Complex[,] rows, PhysicalFourierInterval vertical)
        {
            var image = new Complex[side, side];
            Parallel.For(0, side, parallel, column =>
            {
                var pupil = new Complex[count];
                for (var row = 0; row < count; row++) pupil[row] = rows[row, column];
                var line = vertical.Evaluate(pupil);
                for (var row = 0; row < side; row++) image[row, column] = line[row];
            });
            return image;
        }

        StreamedFourierPupilDiagnostics Diagnostics(StreamedPupilTrace trace) =>
            new(intervalSize, source.RowsPerBatch, tracedNodes, trace.TransmittedNodes, butterflies, cells);
        StreamedFourierEnergy Unresolved(StreamedPupilTrace trace) => new(null, null, (0, 0), trace.Phase, Diagnostics(trace), symmetry);
        static double Power(Complex value) => value.Real * value.Real + value.Imaginary * value.Imaginary;
    }

    internal static double FourierMarginalFirstMoment(Complex[] spectrum)
    {
        // Use the same independent index interval as FourierImageMoments.
        var moment = 0d;
        for (var i = 1; i < spectrum.Length; i++)
        {
            var value = spectrum[i];
            var index = i <= spectrum.Length / 2 ? i : i - spectrum.Length;
            moment += index * (value.Real * value.Real + value.Imaginary * value.Imaginary);
        }
        return moment;
    }
}

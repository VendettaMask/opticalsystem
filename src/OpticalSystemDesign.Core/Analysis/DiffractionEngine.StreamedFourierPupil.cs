using System.Numerics;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis;

public sealed record StreamedFourierPupilDiagnostics(int LogicalTransformSize, int RowsPerTraceBatch,
    long TracedNodes, long TransmittedNodes, long EstimatedButterflies, long FourierWorkingCells);

public static partial class DiffractionEngine
{
    internal const int MaximumStreamedPsfSampling = 8192;
    internal const int MaximumStreamedPhysicalPsfSampling = 6144;
    private const int StreamedPsfTraceBatchSize = 32_768;
    private const long MaximumStreamedPsfButterflies = 1_000_000_000;

    internal static (PsfResult Psf, StreamedFourierPupilDiagnostics Diagnostics) ComputeStreamedFocalFftPsf(
        Optic optic, (double Hx, double Hy) field, Wavelength wavelength, int count, int displaySize,
        double pitch, long maximumTraceNodes, bool physicalWindow = false)
    {
        const string analysis = "FFT PSF";
        var maximumSampling = physicalWindow ? MaximumStreamedPhysicalPsfSampling : MaximumStreamedPsfSampling;
        if (optic.ImageSpaceAfocal || optic.RayAimingEnabled || count < 256 || count > maximumSampling
            || count % 256 != 0 || displaySize is < 2 or > 128 || !(pitch > 0) || !double.IsFinite(pitch))
            throw new AnalysisDataUnavailableException(analysis,
                "streamed Fourier refinement requires a fixed finite scalar image window of at most 128 pixels per side and unaimed rays");
        if (!TryWorkingFNumbersAtPupilZone(optic, field, wavelength, false,
            WorkingApertureReduction.RootMeanSquare, 1, out var workingAxes))
            throw new AnalysisDataUnavailableException(analysis, "streamed Fourier refinement requires a complete finite marginal cone");
        var fNumber = CombineWorkingFNumbers(workingAxes, optic.UseExitPupilFNumber);
        var logicalSize = 2 * count;
        var stretch = physicalWindow ? 1 : FftPupilStretch(optic, wavelength, count, logicalSize, true, fNumber, pitch);
        var baseGrid = new PupilGridSpecification(count, true, true, stretch);
        var grid = physicalWindow ? baseGrid : FiniteScalarFourierGrid(baseGrid, workingAxes, fNumber, pitch).Grid;
        if (grid.Coordinate(0) > -1 || grid.YCoordinate(0) > -1
            || grid.Coordinate(count - 1) < 1 || grid.YCoordinate(count - 1) < 1)
            throw new AnalysisDataUnavailableException(analysis, "streamed Fourier source does not cover the full physical pupil");
        var source = PlanStreamedPupil(grid, maximumTraceNodes, analysis);
        var center = (displaySize - 1) / 2;
        var plan = physicalWindow ? null : new StreamedFftWindowPlan(logicalSize, -center, displaySize);
        var step = pitch / (wavelength.Micrometers * (count - 2));
        var bufferLength = plan?.BufferLength ?? (int)BitOperations.RoundUpToPowerOf2((uint)(count + displaySize - 1));
        var xPlan = physicalWindow ? PhysicalInterval(workingAxes.Sagittal) : null;
        var yPlan = physicalWindow ? PhysicalInterval(workingAxes.Tangential) : null;
        var butterflies = physicalWindow
            ? (source.ActiveRows + (long)displaySize + 1) * bufferLength * BitOperations.Log2((uint)bufferLength)
            : (source.ActiveRows + (long)displaySize) * logicalSize * BitOperations.Log2((uint)bufferLength) / 2;
        var rowsPerBatch = source.RowsPerBatch;
        const int workers = 4;
        var majorCells = (long)count * displaySize + (long)displaySize * displaySize
            + (long)(rowsPerBatch + workers) * (count + bufferLength);
        if (majorCells > AnalysisResourceLimits.MaximumAggregateGridCells
            || butterflies > MaximumStreamedPsfButterflies)
            throw new AnalysisDataUnavailableException(analysis, "streamed Fourier refinement exceeds its ray, transform or allocation budget");

        var intermediate = new Complex[count, displaySize];
        var scratch = Enumerable.Range(0, rowsPerBatch + 1).Select(_ => new Complex[bufferLength]).ToArray();
        var parallel = new ParallelOptions { MaxDegreeOfParallelism = workers };
        var traced = TraceStreamedPupil(optic, field, wavelength, source, analysis, useLaunchAmplitude: true, batch =>
        {
            Parallel.For(0, batch.Count, parallel, index =>
            {
                var item = batch[index];
                var values = xPlan is null ? plan!.Evaluate(item.Pupil, scratch[index]) : xPlan.Evaluate(item.Pupil, scratch[index]);
                for (var column = 0; column < displaySize; column++) intermediate[item.Row, column] = values[column];
            });
        });
        if (!(traced.IdealAmplitude > 0) || !(traced.TotalPower > 0) || !double.IsFinite(traced.TotalPower))
            throw new AnalysisDataUnavailableException(analysis, "no finite illuminated streamed Fourier pupil");
        var values = new double[displaySize, displaySize];
        var normalization = traced.TransmittedNodes * traced.TotalPower;
        Parallel.For(0, displaySize, parallel, column =>
        {
            var input = new Complex[count];
            for (var row = 0; row < count; row++) input[row] = intermediate[row, column];
            var buffer = new Complex[bufferLength];
            var output = yPlan is null ? plan!.Evaluate(input, buffer) : yPlan.Evaluate(input, buffer);
            for (var row = 0; row < displaySize; row++)
                values[row, column] = 100 * output[row].Magnitude * output[row].Magnitude / normalization;
        });
        var directions = WorkingFNumbers(optic, field, wavelength, aimAtStop: false, zemaxDirectionalAverage: true);
        var psf = new PsfResult(values, count, displaySize, fNumber, pitch,
            directions.Tangential, directions.Sagittal, count)
        {
            PupilPhaseSampling = traced.Phase,
            PupilGridStretch = stretch,
            PupilGridStretchX = grid.Stretch,
            PupilGridStretchY = grid.StretchY ?? grid.Stretch,
            CroppedImageCenterIndex = center,
            UnaberratedPeakRatio = traced.IdealAmplitude * traced.IdealAmplitude / normalization
        };
        return (psf, new(physicalWindow ? bufferLength : logicalSize, rowsPerBatch,
            source.TraceNodes, traced.TransmittedNodes, butterflies, majorCells));

        PhysicalFourierInterval PhysicalInterval(double axisFNumber)
        {
            var axisStep = step / axisFNumber;
            if (!double.IsFinite(axisStep) || (displaySize - 1 - center) * axisStep >= .5)
                throw new AnalysisDataUnavailableException(analysis, "the requested physical image exceeds the sampled Fourier interval");
            return new(count, displaySize, axisStep, -center * axisStep, (count - 1) / 2d, boundedSize: bufferLength);
        }
    }

    private sealed record StreamedPupilPlan(double[] X, double[] Y, (int First, int End)[] RowWindows,
        int RowsPerBatch, int ActiveRows, long TraceNodes);

    private static StreamedPupilPlan PlanStreamedPupil(PupilGridSpecification grid, long maximumTraceNodes, string analysis)
    {
        var count = grid.SampleCount;
        var x = Enumerable.Range(0, count).Select(grid.Coordinate).ToArray();
        var y = Enumerable.Range(0, count).Select(grid.YCoordinate).ToArray();
        var rowWindows = new (int First, int End)[count];
        var activeRows = 0;
        long traceNodes = 0;
        for (var row = 0; row < count; row++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var ySquared = y[row] * y[row];
            if (ySquared > 1) continue;
            var first = 0;
            var end = count;
            while (first < end && x[first] * x[first] + ySquared > 1) first++;
            while (end > first && x[end - 1] * x[end - 1] + ySquared > 1) end--;
            rowWindows[row] = (first, end);
            if (first == end) continue;
            activeRows++;
            traceNodes += end - first;
        }
        if (traceNodes == 0 || traceNodes > maximumTraceNodes || traceNodes > AnalysisResourceLimits.MaximumAnalysisRayWork)
            throw new AnalysisDataUnavailableException(analysis, "streamed Fourier refinement exceeds its ray budget");
        return new(x, y, rowWindows, Math.Max(1, StreamedPsfTraceBatchSize / count), activeRows, traceNodes);
    }

    private sealed record StreamedPupilTrace(FftPupilSampling Phase, long TransmittedNodes, double IdealAmplitude, double TotalPower);

    private static StreamedPupilTrace TraceStreamedPupil(Optic optic, (double Hx, double Hy) field,
        Wavelength wavelength, StreamedPupilPlan source, string analysis, bool useLaunchAmplitude,
        Action<IReadOnlyList<(int Row, Complex[] Pupil)>> acceptRows, bool transpose = false,
        WavefrontReferenceSphere? referenceSphere = null, Wavelength? referenceWavelength = null)
    {
        var (x, y, rowWindows, rowsPerBatch, _, _) = source;
        var count = x.Length;
        var previous = new StreamedRawPupilRow(count);
        var current = new StreamedRawPupilRow(count);
        var priorFiltered = new double[count];
        Array.Fill(priorFiltered, double.NaN);
        var filtered = new double[count];
        var maximumPhaseStep = 0d;
        var connectedEdges = 0;
        var valid = 0L;
        var idealAmplitude = 0d;
        var totalPower = 0d;
        for (var first = 0; first < count; first += rowsPerBatch)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var rowCount = Math.Min(rowsPerBatch, count - first);
            var rows = Enumerable.Range(0, rowCount).Select(_ => new StreamedRawPupilRow(count)).ToArray();
            var samples = new List<PupilSample>(rowCount * count);
            for (var row = 0; row < rowCount; row++)
            {
                var (left, end) = rowWindows[first + row];
                for (var column = left; column < end; column++)
                    samples.Add(transpose ? new(y[first + row], x[column], 1) : new(x[column], y[first + row], 1));
            }
            if (samples.Count > 0)
            {
                var wavefront = WavefrontEngine.GenerateChiefRayBatch(optic, field, wavelength, samples, false,
                    referenceSphere, referenceWavelength);
                var index = 0;
                for (var row = 0; row < rowCount; row++)
                {
                    var (left, end) = rowWindows[first + row];
                    for (var column = left; column < end; column++)
                    {
                        var sample = wavefront.Samples[index++];
                        if (!(sample.Intensity > 0)) continue;
                        rows[row].Amplitude[column] = FieldAmplitudeFromPower(useLaunchAmplitude ? sample.LaunchIntensity ?? sample.Intensity : sample.Intensity);
                        rows[row].Phase[column] = sample.OpdWaves;
                    }
                }
            }
            var prepared = new List<(int Row, Complex[] Pupil)>(rowCount + 1);
            for (var row = 0; row < rowCount; row++)
            {
                var priorIndex = first + row - 1;
                if (priorIndex >= 0) PrepareRow(priorIndex, previous, current, rows[row], prepared);
                previous = current;
                current = rows[row];
            }
            if (first + rowCount == count) PrepareRow(count - 1, previous, current, new(count), prepared);
            acceptRows(prepared);
        }

        var phaseSampling = new FftPupilSampling(connectedEdges == 0 ? FftPhaseSamplingStatus.NoConnectedSamples
            : maximumPhaseStep > FftPupilSampling.MaximumAdjacentStepWaves * (1 + 1e-12)
                ? FftPhaseSamplingStatus.UndersampledPhase : FftPhaseSamplingStatus.ResolvedAtNodes,
            connectedEdges, maximumPhaseStep);
        return new(phaseSampling, valid, idealAmplitude, totalPower);

        void PrepareRow(int row, StreamedRawPupilRow above, StreamedRawPupilRow raw, StreamedRawPupilRow below,
            List<(int Row, Complex[] Pupil)> prepared)
        {
            Array.Fill(filtered, double.NaN);
            var (first, end) = rowWindows[row];
            var pupil = new Complex[count];
            var illuminated = false;
            for (var column = first; column < end; column++)
            {
                var amplitude = raw.Amplitude[column];
                if (!(amplitude > 0)) continue;
                var phase = raw.Phase[column];
                // Same scalar native checkerboard as the dense preparation.
                if (((row + column) & 1) == 0 && column > 0 && column + 1 < count
                    && raw.Amplitude[column - 1] > 0 && raw.Amplitude[column + 1] > 0
                    && above.Amplitude[column] > 0 && below.Amplitude[column] > 0)
                {
                    if (useLaunchAmplitude) amplitude = (raw.Amplitude[column - 1] + raw.Amplitude[column + 1]
                        + above.Amplitude[column] + below.Amplitude[column]) / 4;
                    if (column != count / 2 || row != count / 2)
                        phase = (raw.Phase[column - 1] + raw.Phase[column + 1]
                            + above.Phase[column] + below.Phase[column]) / 4;
                }
                if (!double.IsFinite(phase))
                    throw new AnalysisDataUnavailableException(analysis, "nonfinite phase on an illuminated streamed pupil node");
                filtered[column] = phase;
                if (column > 0) ObserveEdge(phase, filtered[column - 1]);
                ObserveEdge(phase, priorFiltered[column]);
                pupil[column] = Complex.FromPolarCoordinates(amplitude, -2 * Math.PI * phase);
                illuminated = true;
                idealAmplitude += amplitude;
                totalPower += amplitude * amplitude;
                valid++;
            }
            if (illuminated) prepared.Add((row, pupil));
            (filtered, priorFiltered) = (priorFiltered, filtered);
        }

        void ObserveEdge(double currentPhase, double otherPhase)
        {
            if (!double.IsFinite(otherPhase)) return;
            maximumPhaseStep = Math.Max(maximumPhaseStep, Math.Abs(currentPhase - otherPhase));
            connectedEdges++;
        }
    }

    private sealed class StreamedRawPupilRow(int count)
    {
        public double[] Phase { get; } = new double[count];
        public double[] Amplitude { get; } = new double[count];
    }

    // Mixed-radix decimation in time computes the exact logical DFT bins,
    // including sizes whose pupil tier is not a power of two.
    private sealed class StreamedFftWindowPlan
    {
        private readonly int _radix;
        private readonly int[] _bins;
        private readonly Complex[,] _roots;

        public StreamedFftWindowPlan(int size, int first, int count)
        {
            BufferLength = size & -size;
            _radix = size / BufferLength;
            _bins = Enumerable.Range(first, count).Select(bin => (bin + size) % BufferLength).ToArray();
            _roots = new Complex[count, _radix];
            for (var bin = 0; bin < count; bin++)
                for (var digit = 0; digit < _radix; digit++)
                    _roots[bin, digit] = Complex.FromPolarCoordinates(1, -2 * Math.PI * (first + bin) * digit / size);
        }

        public int BufferLength { get; }

        public Complex[] Evaluate(Complex[] input, Complex[] buffer)
        {
            var output = new Complex[_bins.Length];
            for (var digit = 0; digit < _radix; digit++)
            {
                Array.Clear(buffer);
                for (var i = digit; i < input.Length; i += _radix) buffer[i / _radix] = input[i];
                Fft(buffer);
                for (var bin = 0; bin < _bins.Length; bin++) output[bin] += buffer[_bins[bin]] * _roots[bin, digit];
            }
            return output;
        }
    }
}


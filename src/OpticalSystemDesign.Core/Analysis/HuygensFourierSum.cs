using System.Numerics;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis;

/// <summary>Computational error of a finite sum, not pupil/window sampling convergence.</summary>
public sealed record HuygensFourierDiagnostics(int Sources, int ImageSize, int FourierSize, int TaylorOrder,
    long EstimatedOperations, long WorkingBytes, double AbsoluteFieldErrorBound)
{
    public bool UsesAdaptiveSourceOrders { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public int PupilBatchSize { get; init; }
    public string Method => PupilBatchSize > 0 ? "BatchedPupilCompensatedFourierMoments"
        : UsesAdaptiveSourceOrders ? "AdaptiveCompensatedFourierMoments" : "CompensatedFourierMoments";
    public bool CertifiesPupilSampling => false;
}

internal readonly record struct HuygensFourierEmitter(Complex Weight, double PhaseStepX, double PhaseStepY);
internal sealed record HuygensFourierResult(Complex[,] Field, HuygensFourierDiagnostics Diagnostics);

/// <summary>Evaluate sum c exp(i(ax*x+ay*y)) on the unchanged integer image lattice.
/// Round frequencies onto an oversampled Fourier lattice and retain all Taylor
/// moments of total degree below P. The real exponential remainder is |t|^P/P!.
/// No pupil gridding, fitted correction, image interpolation or peak normalization.</summary>
internal static class HuygensFourierSum
{
    private const double TaylorTolerance = 1e-12;
    private const double DenseTaylorTolerance = 1e-10;
    private const long MaximumPupilTraceNodes = 4L * SequentialTraceLimits.MaximumRayCount;
    private const long MaximumWorkingBytes = 256L * 1024 * 1024;
    private const int MaximumBatchedPupilNodes = 14_000_000;
    private const long MaximumBatchedMomentOperations = 800_000_000;
    internal const int PupilBatchSize = 32_768;
    private const double Epsilon = 2.2204460492503131e-16;
    internal sealed record Plan(int Size, int Order, int Moments, long Work, long Bytes, double Remainder)
    {
        public byte[]? SourceOrders { get; init; }
    }

    private readonly record struct FrequencyCell(int Row, int Column, double Dx, double Dy)
    {
        public double PhaseRadius => Math.Abs(Dx) + Math.Abs(Dy);
    }

    internal static bool ExceedsDirectBudget(int pupilSampling, int imageSize) =>
        checked((long)pupilSampling * pupilSampling * imageSize * imageSize) > AnalysisResourceLimits.MaximumDirectPsfOperations;

    internal static void ValidateCandidateWork(int pupilSampling, int imageSize)
    {
        if (pupilSampling < 2 || imageSize < 1) { AnalysisResourceLimits.ValidateDirectPsfWork(pupilSampling, imageSize); return; }
        ValidatePupilTraceWork(pupilSampling);
        if (!ExceedsDirectBudget(pupilSampling, imageSize)) return;
        // Trace a bounded pupil first. Physical apertures can remove enough
        // sources for the actual Fourier plan to fit where a full disk cannot.
        if (CreatePlan(1, imageSize) is null)
            AnalysisResourceLimits.ValidateDirectPsfWork(pupilSampling, imageSize);
    }

    internal static void ValidatePupilTraceWork(int pupilSampling)
    {
        // Enclosing each disk node's unit lattice cell bounds the number
        // of rays generated before any clipping. Each trace keeps its cap.
        var bound = PupilNodeBound(pupilSampling);
        if (bound > MaximumPupilTraceNodes)
            throw new ArgumentOutOfRangeException(nameof(pupilSampling),
                $"Huygens pupil tracing exceeds the {MaximumPupilTraceNodes:N0}-node aggregate safety budget.");
    }

    internal static Plan? CreatePlan(int sourceCount, int imageSize) =>
        CreatePlan(sourceCount, imageSize, TaylorTolerance)
        ?? CreatePlan(sourceCount, imageSize, DenseTaylorTolerance);

    internal static Plan? CreateBatchedPlan(int pupilSampling, int imageSize, bool separateNormalizer)
    {
        if (pupilSampling < 2) return null;
        var bound = PupilNodeBound(pupilSampling);
        if (bound * (separateNormalizer ? 2 : 1) > MaximumBatchedPupilNodes) return null;
        return CreatePlan((int)bound, imageSize, DenseTaylorTolerance, batched: true);
    }

    private static double PupilNodeBound(int pupilSampling) => Math.Min((double)pupilSampling * pupilSampling,
        Math.Ceiling(Math.PI * Math.Pow(pupilSampling / 2d + 1, 2)));

    private static Plan? CreatePlan(int sourceCount, int imageSize, double tolerance, bool batched = false)
    {
        if (sourceCount < 0 || imageSize < 1 || imageSize > AnalysisResourceLimits.MaximumAnalysisGridDimension) return null;
        Plan? best = null;
        for (var scale = 2; scale <= 16; scale *= 2)
        {
            var size = FourierSize(imageSize, scale);
            if (size > AnalysisResourceLimits.MaximumFftGridSize) continue;
            var theta = 2 * Math.PI * (imageSize / 2) / size;
            double remainder = 1; var order = 0;
            do { order++; remainder *= theta / order; } while (remainder > tolerance && order < 32);
            if (remainder > tolerance) continue;
            var moments = order * (order + 1) / 2;
            var work = checked((long)moments * (sourceCount + (long)size * size * BitOperations.Log2((uint)size) + (long)imageSize * imageSize));
            var bytes = checked((long)moments * size * size * 32 + (long)imageSize * imageSize * 16
                + (batched ? 0 : (long)sourceCount * 32));
            var maximumOperations = batched ? MaximumBatchedMomentOperations : AnalysisResourceLimits.MaximumDirectPsfOperations;
            if (work > maximumOperations || bytes > MaximumWorkingBytes) continue;
            if (best is null || work < best.Work) best = new(size, order, moments, work, bytes, remainder);
        }
        return best;
    }

    private static Plan? CreateAdaptivePlan(IReadOnlyList<HuygensFourierEmitter> sources, int imageSize)
    {
        if (imageSize < 1 || imageSize > AnalysisResourceLimits.MaximumAnalysisGridDimension
            || sources.Count > MaximumPupilTraceNodes) return null;
        // The smallest oversampling grid minimizes the transform work. Sources
        // closer to a bin need fewer moments than the worst residual in its cell.
        var size = FourierSize(imageSize, 2);
        if (size > AnalysisResourceLimits.MaximumFftGridSize) return null;
        var half = imageSize / 2;
        var theta = 2 * Math.PI * half / size;
        var limits = new List<double>(32);
        var factorial = 1d;
        for (var order = 1; order <= 32; order++)
        {
            factorial *= order;
            // Reserve margin for rounding the analytic inverse of t^P / P!.
            var limit = Math.Pow(DenseTaylorTolerance * factorial, 1d / order) * (1 - 32 * Epsilon);
            limits.Add(limit);
            if (limit >= theta) break;
        }
        if (limits[^1] < theta) return null;

        var thresholds = limits.ToArray();
        var orders = new byte[sources.Count];
        var maxOrder = 1;
        var planningWork = 2 + BitOperations.Log2((uint)thresholds.Length);
        var transformWork = (long)size * size * BitOperations.Log2((uint)size) + (long)imageSize * imageSize;
        var gridBytes = (long)size * size * 32;
        var work = (long)planningWork * sources.Count;
        var minimumBytes = (long)imageSize * imageSize * 16 + (long)sources.Count * 33;
        for (var i = 0; i < sources.Count; i++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var residual = Map(sources[i], size, half).PhaseRadius;
            var found = Array.BinarySearch(thresholds, residual);
            var order = (found >= 0 ? found : ~found) + 1;
            if (order > thresholds.Length) return null;
            orders[i] = (byte)order;
            maxOrder = Math.Max(maxOrder, order);
            work += MomentCount(order);
            if (work + transformWork * MomentCount(maxOrder) > AnalysisResourceLimits.MaximumDirectPsfOperations
                || minimumBytes + gridBytes * MomentCount(maxOrder) > MaximumWorkingBytes) return null;
        }
        var moments = MomentCount(maxOrder);
        return new(size, maxOrder, moments, work + transformWork * moments, minimumBytes + gridBytes * moments,
            DenseTaylorTolerance) { SourceOrders = orders };
    }

    private static int MomentCount(int order) => order * (order + 1) / 2;

    private static int FourierSize(int imageSize, int scale)
    {
        var size = 1;
        while (size < (long)imageSize * scale && size <= AnalysisResourceLimits.MaximumFftGridSize) size *= 2;
        return size;
    }

    private static FrequencyCell Map(HuygensFourierEmitter source, int size, int half)
    {
        if (!double.IsFinite(source.Weight.Real) || !double.IsFinite(source.Weight.Imaginary)
            || !double.IsFinite(source.PhaseStepX) || !double.IsFinite(source.PhaseStepY))
            throw new ArgumentException("Fourier Huygens sources must be finite.", "sources");
        var fx = Math.IEEERemainder(source.PhaseStepX, 2 * Math.PI);
        var fy = Math.IEEERemainder(source.PhaseStepY, 2 * Math.PI);
        var bx = (int)Math.Round(fx * size / (2 * Math.PI));
        var by = (int)Math.Round(fy * size / (2 * Math.PI));
        return new((by + size) % size, (bx + size) % size,
            (fx - bx * (2 * Math.PI / size)) * half, (fy - by * (2 * Math.PI / size)) * half);
    }

    internal static HuygensFourierResult Evaluate(IReadOnlyList<HuygensFourierEmitter> sources, int imageSize)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var plan = CreatePlan(sources.Count, imageSize) ?? CreateAdaptivePlan(sources, imageSize)
            ?? throw new ArgumentOutOfRangeException(nameof(imageSize), "Bounded Fourier Huygens work exceeds the original operation or memory limit.");
        return Evaluate(sources, imageSize, plan);
    }

    internal static HuygensFourierResult EvaluateBatched(IEnumerable<HuygensFourierEmitter> sources,
        int pupilSampling, int imageSize, Plan plan) => Evaluate(sources, imageSize, plan, (int)PupilNodeBound(pupilSampling));

    private static HuygensFourierResult Evaluate(IEnumerable<HuygensFourierEmitter> sources, int imageSize,
        Plan plan, int? batchedSourceLimit = null)
    {
        ComputationCancellation.ThrowIfCancellationRequested();
        var sum = new Complex[plan.Moments][,]; var compensation = new Complex[plan.Moments][,];
        for (var index = 0; index < sum.Length; index++)
        { sum[index] = new Complex[plan.Size, plan.Size]; compensation[index] = new Complex[plan.Size, plan.Size]; }
        var half = imageSize / 2;
        double amplitudeSum = 0, remainderSum = 0;
        var xPowers = new Complex[plan.Order]; var yPowers = new Complex[plan.Order];
        var sourceCount = 0;
        foreach (var source in sources)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            if (sourceCount == batchedSourceLimit)
                throw new InvalidOperationException("Batched Huygens pupil exceeded its checked source count.");
            var (row, column, dx, dy) = Map(source, plan.Size, half);
            var order = plan.SourceOrders?[sourceCount] ?? plan.Order;
            sourceCount++;
            xPowers[0] = yPowers[0] = Complex.One;
            for (var p = 1; p < order; p++)
            { xPowers[p] = xPowers[p - 1] * new Complex(0, dx / p); yPowers[p] = yPowers[p - 1] * new Complex(0, dy / p); }
            var moment = 0;
            for (var p = 0; p < order; p++)
            {
                for (var q = 0; q < order - p; q++)
                {
                    var index = moment + q;
                    var value = source.Weight * xPowers[p] * yPowers[q] - compensation[index][row, column];
                    var next = sum[index][row, column] + value;
                    compensation[index][row, column] = (next - sum[index][row, column]) - value;
                    sum[index][row, column] = next;
                }
                moment += plan.Order - p;
            }
            var amplitude = source.Weight.Magnitude;
            var remainder = amplitude;
            for (var p = 1; p <= order; p++) remainder *= (Math.Abs(dx) + Math.Abs(dy)) / p;
            remainderSum += remainder; amplitudeSum += amplitude;
        }
        // Compensation arrays are no longer needed during transforms.
        compensation = [];
        var field = new Complex[imageSize, imageSize];
        var momentIndex = 0;
        for (var p = 0; p < plan.Order; p++)
            for (var q = 0; q < plan.Order - p; q++, momentIndex++)
            {
                ComputationCancellation.ThrowIfCancellationRequested();
                // Forward DFT has a minus sign. Reading negative coordinates
                // evaluates the required positive exponential without rescaling.
                DiffractionEngine.FourierTransform2D(sum[momentIndex]);
                for (var row = 0; row < imageSize; row++)
                {
                    var y = row - half;
                    var py = q == 0 ? 1 : Math.Pow(half == 0 ? 0 : (double)y / half, q);
                    var ri = (plan.Size - y) % plan.Size;
                    for (var column = 0; column < imageSize; column++)
                    {
                        var x = column - half;
                        var px = p == 0 ? 1 : Math.Pow(half == 0 ? 0 : (double)x / half, p);
                        field[row, column] += sum[momentIndex][ri, (plan.Size - x) % plan.Size] * (px * py);
                    }
                }
                sum[momentIndex] = new Complex[0, 0];
            }
        // Kahan bin accumulation, power construction and radix-2 transforms.
        // Keep a conservative floating-point allowance beside the analytic tail.
        var roundoff = Epsilon * (32 * plan.Order + 32 * BitOperations.Log2((uint)plan.Size) + 64)
            * Math.Exp(2 * Math.PI * half / plan.Size) * amplitudeSum;
        return new(field, new(sourceCount, imageSize, plan.Size, plan.Order, plan.Work, plan.Bytes, remainderSum + roundoff)
        {
            UsesAdaptiveSourceOrders = plan.SourceOrders is not null,
            PupilBatchSize = batchedSourceLimit.HasValue ? PupilBatchSize : 0
        });
    }
}

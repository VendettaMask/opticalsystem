using System.Numerics;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis;

/// <summary>Computational error of a finite sum, not pupil/window sampling convergence.</summary>
public sealed record HuygensFourierDiagnostics(int Sources, int ImageSize, int FourierSize, int TaylorOrder,
    long EstimatedOperations, long WorkingBytes, double AbsoluteFieldErrorBound)
{
    public string Method => "CompensatedFourierMoments";
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
    private const long MaximumWorkingBytes = 256L * 1024 * 1024;
    private const double Epsilon = 2.2204460492503131e-16;
    internal sealed record Plan(int Size, int Order, int Moments, long Work, long Bytes, double Remainder);

    internal static bool ExceedsDirectBudget(int pupilSampling, int imageSize) =>
        checked((long)pupilSampling * pupilSampling * imageSize * imageSize) > AnalysisResourceLimits.MaximumDirectPsfOperations;

    internal static void ValidateCandidateWork(int pupilSampling, int imageSize)
    {
        if (pupilSampling < 2 || imageSize < 1) { AnalysisResourceLimits.ValidateDirectPsfWork(pupilSampling, imageSize); return; }
        if (!ExceedsDirectBudget(pupilSampling, imageSize)) return;
        // Each focused/afocal native-grid source lies inside the sampled disk.
        // Enclosing its unit lattice cell gives a conservative source bound.
        var sources = (long)Math.Min((double)pupilSampling * pupilSampling,
            Math.Ceiling(Math.PI * Math.Pow(pupilSampling / 2d + 1, 2)));
        if (sources > int.MaxValue || CreatePlan((int)sources, imageSize) is null)
            AnalysisResourceLimits.ValidateDirectPsfWork(pupilSampling, imageSize);
    }

    internal static Plan? CreatePlan(int sourceCount, int imageSize)
    {
        if (sourceCount < 0 || imageSize < 1 || imageSize > AnalysisResourceLimits.MaximumAnalysisGridDimension) return null;
        Plan? best = null;
        for (var scale = 2; scale <= 16; scale *= 2)
        {
            var size = 1;
            while (size < (long)imageSize * scale && size <= AnalysisResourceLimits.MaximumFftGridSize) size *= 2;
            if (size > AnalysisResourceLimits.MaximumFftGridSize) continue;
            var theta = 2 * Math.PI * (imageSize / 2) / size;
            double remainder = 1; var order = 0;
            do { order++; remainder *= theta / order; } while (remainder > TaylorTolerance && order < 32);
            if (remainder > TaylorTolerance) continue;
            var moments = order * (order + 1) / 2;
            var work = checked((long)moments * (sourceCount + (long)size * size * BitOperations.Log2((uint)size) + (long)imageSize * imageSize));
            var bytes = checked((long)moments * size * size * 32 + (long)imageSize * imageSize * 16 + (long)sourceCount * 32);
            if (work > AnalysisResourceLimits.MaximumDirectPsfOperations || bytes > MaximumWorkingBytes) continue;
            if (best is null || work < best.Work) best = new(size, order, moments, work, bytes, remainder);
        }
        return best;
    }

    internal static HuygensFourierResult Evaluate(IReadOnlyList<HuygensFourierEmitter> sources, int imageSize)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var plan = CreatePlan(sources.Count, imageSize)
            ?? throw new ArgumentOutOfRangeException(nameof(imageSize), "Bounded Fourier Huygens work exceeds the original operation or memory limit.");
        ComputationCancellation.ThrowIfCancellationRequested();
        var sum = new Complex[plan.Moments][,]; var compensation = new Complex[plan.Moments][,];
        for (var index = 0; index < sum.Length; index++)
        { sum[index] = new Complex[plan.Size, plan.Size]; compensation[index] = new Complex[plan.Size, plan.Size]; }
        var half = imageSize / 2;
        double amplitudeSum = 0, remainderSum = 0;
        var xPowers = new Complex[plan.Order]; var yPowers = new Complex[plan.Order];
        foreach (var source in sources)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            if (!double.IsFinite(source.Weight.Real) || !double.IsFinite(source.Weight.Imaginary)
                || !double.IsFinite(source.PhaseStepX) || !double.IsFinite(source.PhaseStepY))
                throw new ArgumentException("Fourier Huygens sources must be finite.", nameof(sources));
            var fx = Math.IEEERemainder(source.PhaseStepX, 2 * Math.PI);
            var fy = Math.IEEERemainder(source.PhaseStepY, 2 * Math.PI);
            var bx = (int)Math.Round(fx * plan.Size / (2 * Math.PI));
            var by = (int)Math.Round(fy * plan.Size / (2 * Math.PI));
            var dx = (fx - bx * (2 * Math.PI / plan.Size)) * half;
            var dy = (fy - by * (2 * Math.PI / plan.Size)) * half;
            xPowers[0] = yPowers[0] = Complex.One;
            for (var p = 1; p < plan.Order; p++)
            { xPowers[p] = xPowers[p - 1] * new Complex(0, dx / p); yPowers[p] = yPowers[p - 1] * new Complex(0, dy / p); }
            var column = (bx + plan.Size) % plan.Size; var row = (by + plan.Size) % plan.Size;
            var moment = 0;
            for (var p = 0; p < plan.Order; p++)
                for (var q = 0; q < plan.Order - p; q++, moment++)
                {
                    var value = source.Weight * xPowers[p] * yPowers[q] - compensation[moment][row, column];
                    var next = sum[moment][row, column] + value;
                    compensation[moment][row, column] = (next - sum[moment][row, column]) - value;
                    sum[moment][row, column] = next;
                }
            var amplitude = source.Weight.Magnitude;
            var remainder = amplitude;
            for (var p = 1; p <= plan.Order; p++) remainder *= (Math.Abs(dx) + Math.Abs(dy)) / p;
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
        return new(field, new(sources.Count, imageSize, plan.Size, plan.Order, plan.Work, plan.Bytes, remainderSum + roundoff));
    }
}

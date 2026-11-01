using System.Numerics;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis;

// Exact linear distance-bin sums at the analysis's uniformly spaced knots.
// Range updates avoid retaining/sorting millions of individual image samples.
internal sealed class FftCumulativeKnotGrid : IEnergyImageGrid
{
    private readonly double[] _values;
    private readonly double _maximum;
    internal int FourierSampling { get; }
    internal double ImagePitch { get; }
    internal double BinWidth { get; }
    internal FftCumulativeKnotGrid(Complex[,] image, int firstX, int firstY, double imagePitch,
        (double X, double Y) center, double fullImagePower, double binWidth,
        double maximumDistance, int knotCount, string type, int fourierSampling)
    {
        if (knotCount < 3 || !(fullImagePower > 0) || !double.IsFinite(fullImagePower)
            || !(binWidth > 0) || !double.IsFinite(binWidth) || !(maximumDistance > 0)
            || !double.IsFinite(maximumDistance) || !(imagePitch > 0) || !double.IsFinite(imagePitch)
            || !double.IsFinite(center.X) || !double.IsFinite(center.Y))
            throw new ArgumentException("Finite positive FFT energy coordinates and complete power are required.");
        var width = image.GetLength(1); var height = image.GetLength(0);
        if (firstX * imagePitch > center.X - maximumDistance - binWidth / 2
            || firstY * imagePitch > center.Y - maximumDistance - binWidth / 2
            || (firstX + width - 1) * imagePitch < center.X + maximumDistance + binWidth / 2
            || (firstY + height - 1) * imagePitch < center.Y + maximumDistance + binWidth / 2
            || type.StartsWith("X", StringComparison.OrdinalIgnoreCase) || type.StartsWith("Y", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The complete radial/square bins must lie inside the evaluated image window.");
        _maximum = maximumDistance; FourierSampling = fourierSampling; ImagePitch = imagePitch; BinWidth = binWidth;
        var slopeChanges = new double[knotCount + 1]; var offsetChanges = new double[knotCount + 1];
        var step = maximumDistance / (knotCount - 1);
        var windowPower = 0d;
        for (var row = 0; row < height; row++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            for (var column = 0; column < width; column++)
            {
                var amplitude = image[row, column];
                var weight = (amplitude.Real * amplitude.Real + amplitude.Imaginary * amplitude.Imaginary) / fullImagePower;
                if (!double.IsFinite(weight)) throw new InvalidOperationException("Nonfinite Fourier energy");
                if (weight == 0) continue;
                windowPower += weight;
                var x = Math.Abs((firstX + column) * imagePitch - center.X);
                var y = Math.Abs((firstY + row) * imagePitch - center.Y);
                var distance = type.Contains("square", StringComparison.OrdinalIgnoreCase) ? Math.Max(x, y) : double.Hypot(x, y);
                var start = (int)Math.Clamp(Math.Ceiling((distance - binWidth / 2) / step), 0, knotCount);
                var end = (int)Math.Clamp(Math.Ceiling((distance + binWidth / 2) / step), 0, knotCount);
                if (start < end)
                {
                    var slope = weight / binWidth; var offset = (binWidth / 2 - distance) * slope;
                    slopeChanges[start] += slope; offsetChanges[start] += offset;
                    slopeChanges[end] -= slope; offsetChanges[end] -= offset;
                }
                if (end < knotCount) offsetChanges[end] += weight;
            }
        }
        if (windowPower > 1 + 1e-10) throw new InvalidOperationException("Window power exceeds complete-image Parseval normalization");
        _values = new double[knotCount]; var currentSlope = 0d; var currentOffset = 0d;
        for (var i = 0; i < knotCount; i++)
        {
            currentSlope += slopeChanges[i]; currentOffset += offsetChanges[i];
            _values[i] = Math.Clamp(currentSlope * (maximumDistance * i / (knotCount - 1d)) + currentOffset, 0, 1);
        }
        _values[0] = 0;
    }

    public double Fraction(double distance)
    {
        if (distance <= 0) return 0;
        if (distance > _maximum * (1 + 1e-12)) throw new ArgumentOutOfRangeException(nameof(distance));
        var index = Math.Clamp(distance / _maximum * (_values.Length - 1), 0, _values.Length - 1);
        var left = (int)Math.Floor(index); var right = Math.Min(left + 1, _values.Length - 1);
        return _values[left] + (index - left) * (_values[right] - _values[left]);
    }
    public double RadiusContaining(double target)
    {
        if (target <= 0) return 0;
        if (target > _values[^1]) throw new InvalidOperationException("Energy quantile lies outside the evaluated window.");
        var index = Array.FindIndex(_values, value => value >= target);
        return _maximum * index / (_values.Length - 1d);
    }
}

using System.Numerics;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis.Imaging;

public sealed record SfrRegion(int X, int Y, int Width, int Height);
public sealed record SfrSample(double CyclesPerPixel, double CyclesPerMillimeter, double Response);
public sealed record EdgeProfileSample(double DistancePixels, double Value);
public sealed record SlantedEdgeFit(double EdgeSlope, double EdgeIntercept, double EdgeFitRmsPixels, double Contrast, int Scanlines);
public sealed record SlantedEdgeMeasurement(IReadOnlyList<SfrSample> Curve,
    IReadOnlyList<EdgeProfileSample> Esf, IReadOnlyList<EdgeProfileSample> Lsf,
    double EdgeSlope, double EdgeIntercept, double EdgeFitRmsPixels, double Contrast,
    double? Mtf50CyclesPerPixel, double? Mtf10CyclesPerPixel);

/// <summary>Linear, near-vertical edge measurement. No target angle or optical MTF is supplied to the estimator.</summary>
public static class SlantedEdgeSfr
{
    public const string Algorithm = "LinearEdgeSfr/1; centroid-linear-fit; 8x bins; Tukey-0.6; central-difference/bin corrections";

    public static SlantedEdgeMeasurement Measure(IReadOnlyList<double> pixels, int width, int height,
        SfrRegion roi, double pixelPitchMicrometers, int halfWindowPixels = 24, Action<SlantedEdgeFit>? fitted = null)
    {
        ArgumentNullException.ThrowIfNull(pixels); ArgumentNullException.ThrowIfNull(roi);
        if (width is < 32 or > 256 || height is < 32 or > 256 || pixels.Count != width * height
            || roi.X < 0 || roi.Y < 0 || roi.Width < 32 || roi.Height < 32
            || (long)roi.X + roi.Width > width || (long)roi.Y + roi.Height > height
            || !double.IsFinite(pixelPitchMicrometers) || pixelPitchMicrometers <= 0 || halfWindowPixels is < 8 or > 48)
            throw new ArgumentException("SFR needs a valid 32–256 pixel linear image/ROI, positive pitch and an 8–48 pixel half-window.");
        if (pixels.Any(v => !double.IsFinite(v) || v < 0)) throw new ArgumentException("SFR input must be finite, nonnegative linear data.");
        double At(int x, int y) => pixels[(roi.Y + y) * width + roi.X + x];
        var left = Enumerable.Range(0, roi.Height).Average(y => Enumerable.Range(0, 4).Average(x => At(x, y)));
        var right = Enumerable.Range(0, roi.Height).Average(y => Enumerable.Range(roi.Width - 4, 4).Average(x => At(x, y)));
        var delta = right - left;
        if (Math.Abs(delta) < 1e-6 * Math.Max(1, Math.Max(left, right))) throw new InvalidOperationException("ROI has no measurable edge contrast.");
        var centers = new double[roi.Height];
        for (var y = 0; y < roi.Height; y++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            double mass = 0, moment = 0;
            for (var x = 1; x < roi.Width - 1; x++)
            {
                var gradient = (At(x + 1, y) - At(x - 1, y)) / 2;
                mass += gradient; moment += gradient * x;
            }
            if (mass * delta <= 0 || Math.Abs(mass) < Math.Abs(delta) * .5)
                throw new InvalidOperationException("Edge does not span the ROI consistently.");
            centers[y] = moment / mass;
        }
        var middleY = (roi.Height - 1) / 2.0;
        var mean = centers.Average();
        var slope = Enumerable.Range(0, roi.Height).Sum(y => (y - middleY) * (centers[y] - mean))
            / Enumerable.Range(0, roi.Height).Sum(y => Math.Pow(y - middleY, 2));
        var intercept = mean - slope * middleY;
        var fitRms = Math.Sqrt(Enumerable.Range(0, roi.Height).Average(y => Math.Pow(centers[y] - intercept - slope * y, 2)));
        var scale = Math.Sqrt(1 + slope * slope);
        fitted?.Invoke(new(slope, intercept + roi.X - slope * roi.Y, fitRms, Math.Abs(delta), roi.Height));
        if (Math.Abs(slope) is < .02 or > .25 || fitRms > .25)
            throw new InvalidOperationException("Use a single near-vertical edge, about 2–10 degrees from vertical; fit residual must be <= 0.25 pixel.");
        if (Enumerable.Range(0, roi.Height).Any(y => Math.Min(intercept + slope * y, roi.Width - 1 - intercept - slope * y) / scale < halfWindowPixels + 1))
            throw new InvalidOperationException("ROI is too narrow for the requested edge window; enlarge/reposition it or reduce the window explicitly.");

        const int oversampling = 8;
        var count = 2 * halfWindowPixels * oversampling + 1;
        var sums = new double[count]; var weights = new int[count];
        for (var y = 0; y < roi.Height; y++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            for (var x = 0; x < roi.Width; x++)
            {
                var distance = (x - intercept - slope * y) / scale;
                var bin = (int)Math.Floor(distance * oversampling + .5) + halfWindowPixels * oversampling;
                if (bin >= 0 && bin < count) { sums[bin] += At(x, y); weights[bin]++; }
            }
        }
        if (weights.Any(w => w == 0)) throw new InvalidOperationException("Edge projection has empty sampling bins; use a longer ROI or a different edge angle.");
        var esf = sums.Select((sum, i) => sum / weights[i]).ToArray();
        var lsf = new double[count];
        for (var i = 1; i < count - 1; i++)
        {
            var position = i / (count - 1.0);
            const double alpha = .6;
            var window = position < alpha / 2 ? .5 * (1 + Math.Cos(Math.PI * (2 * position / alpha - 1)))
                : position > 1 - alpha / 2 ? .5 * (1 + Math.Cos(Math.PI * (2 * position / alpha - 2 / alpha + 1))) : 1;
            lsf[i] = (esf[i + 1] - esf[i - 1]) * oversampling / 2 * window;
        }
        var dc = lsf.Sum();
        if (!double.IsFinite(dc) || Math.Abs(dc) < Math.Abs(delta) * oversampling * .5)
            throw new InvalidOperationException("Edge window does not contain a usable line-spread response.");
        var curve = Enumerable.Range(0, 129).Select(k =>
        {
            var f = k / 256.0; var otf = Complex.Zero;
            for (var i = 0; i < count; i++)
                otf += lsf[i] * Complex.FromPolarCoordinates(1, -2 * Math.PI * f * (i / (double)oversampling - halfWindowPixels));
            var correction = Sinc(2 * f / oversampling) * Sinc(f / oversampling);
            return new SfrSample(f, f * 1000 / pixelPitchMicrometers, otf.Magnitude / Math.Abs(dc) / correction);
        }).ToArray();
        return new(curve, esf.Select((v, i) => new EdgeProfileSample(i / (double)oversampling - halfWindowPixels, v)).ToArray(),
            lsf.Select((v, i) => new EdgeProfileSample(i / (double)oversampling - halfWindowPixels, v)).ToArray(),
            slope, intercept + roi.X - slope * roi.Y, fitRms, Math.Abs(delta), Crossing(curve, .5), Crossing(curve, .1));
    }

    public static double? Crossing(IReadOnlyList<SfrSample> curve, double level)
    {
        for (var i = 1; i < curve.Count; i++)
            if (curve[i - 1].Response >= level && curve[i].Response < level)
                return curve[i - 1].CyclesPerPixel + (curve[i].CyclesPerPixel - curve[i - 1].CyclesPerPixel)
                    * (curve[i - 1].Response - level) / (curve[i - 1].Response - curve[i].Response);
        return null;
    }

    internal static double Sinc(double x) => Math.Abs(x) < 1e-12 ? 1 : Math.Sin(Math.PI * x) / (Math.PI * x);
}

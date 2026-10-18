using System.Numerics;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis;

public enum SpatialCoherenceFunction
{
    Uniform,
    Gaussian,
    Sinc
}

public enum SpatialCoherenceMethod
{
    ExactSourcePairs,
    FiniteCorrelationWindow,
    FourierModes
}

public static partial class MutualIntensityEngine
{
    /// <summary>Stationary complex degree of coherence in image-space millimeters.
    /// Sinc is signed: sin(pi x)/(pi x), rather than its magnitude or intensity.</summary>
    public static double EvaluateSpatialCoherence(SpatialCoherenceFunction function, double dxMillimeters,
        double dyMillimeters, double alphaMillimeters)
    {
        if (function is not (SpatialCoherenceFunction.Gaussian or SpatialCoherenceFunction.Sinc)
            || !double.IsFinite(dxMillimeters) || !double.IsFinite(dyMillimeters)
            || !double.IsFinite(alphaMillimeters) || alphaMillimeters <= 0)
            throw new ArgumentOutOfRangeException(nameof(alphaMillimeters));
        var x = dxMillimeters / alphaMillimeters;
        var y = dyMillimeters / alphaMillimeters;
        return function == SpatialCoherenceFunction.Gaussian
            ? Math.Exp(-(x * x + y * y)) : Sinc(x) * Sinc(y);
    }

    /// <summary>Finite sampled-source mutual-intensity propagation, including complex response phase.
    /// ExactSourcePairs retains the full signed covariance. The finite-window compatibility quadrature
    /// and the positive Fourier-mode covariance are explicit alternatives; none fits an image gain.</summary>
    public static double[,] PropagateSpatialCoherence(double[,] sourceIntensity, Complex[,] impulseResponse,
        SpatialCoherenceFunction function, double alphaMillimeters, double pixelPitchMillimeters,
        double[,]? sourcePhaseRadians = null,
        SpatialCoherenceMethod method = SpatialCoherenceMethod.ExactSourcePairs,
        double fraction = .98, int coherenceSampling = 64)
    {
        ComputationCancellation.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(sourceIntensity);
        ArgumentNullException.ThrowIfNull(impulseResponse);
        _ = EvaluateSpatialCoherence(function, 0, 0, alphaMillimeters);
        ValidateSpatialMethod(method, fraction, coherenceSampling);
        if (!double.IsFinite(pixelPitchMillimeters) || pixelPitchMillimeters <= 0)
            throw new ArgumentOutOfRangeException(nameof(pixelPitchMillimeters));
        var height = sourceIntensity.GetLength(0);
        var width = sourceIntensity.GetLength(1);
        var kh = impulseResponse.GetLength(0);
        var kw = impulseResponse.GetLength(1);
        AnalysisResourceLimits.ValidateImageDimensions(width, height, "Mutual-intensity source");
        AnalysisResourceLimits.ValidateImageDimensions(kw, kh, "Complex impulse response");
        if (sourcePhaseRadians is not null && (sourcePhaseRadians.GetLength(0) != height || sourcePhaseRadians.GetLength(1) != width))
            throw new ArgumentException("Source phase must match source intensity dimensions.", nameof(sourcePhaseRadians));
        ValidateImpulseResponse(impulseResponse);
        var points = new List<(int Y, int X, Complex Amplitude)>();
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var intensity = sourceIntensity[y, x];
            var phase = sourcePhaseRadians?[y, x] ?? 0;
            if (!double.IsFinite(intensity) || intensity < 0 || !double.IsFinite(phase))
                throw new ArgumentException("Source intensity must be finite and nonnegative, and source phase finite.");
            if (intensity > 0) points.Add((y, x, Complex.FromPolarCoordinates(Math.Sqrt(intensity), phase)));
        }
        var count = points.Count;
        var work = (double)count * (count + 1) / 2 * width * height;
        if (work > AnalysisResourceLimits.MaximumImageSimulationConvolutionOperations)
            throw new ArgumentOutOfRangeException(nameof(sourceIntensity), "Exact spatial mutual-intensity propagation exceeds the image convolution work budget. Reduce the source sampling or active source area.");
        var correlations = new double[count][];
        var effectiveSampling = method == SpatialCoherenceMethod.FourierModes
            ? ResolveSpatialCoherenceSampling(coherenceSampling, kw, kh) : coherenceSampling;
        var spectrumCorrelation = method == SpatialCoherenceMethod.FourierModes
            ? BuildFourierCorrelation(function, alphaMillimeters, pixelPitchMillimeters, fraction, effectiveSampling) : null;
        // The finite-window compatibility quadrature uses ordered displacements [-r,r).
        // Keep it separate from the Hermitian, positive-semidefinite exact source model.
        var radius = Math.Ceiling(2.5 * alphaMillimeters / pixelPitchMillimeters);
        for (var i = 0; i < count; i++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            correlations[i] = new double[i];
            for (var j = 0; j < i; j++)
                correlations[i][j] = spectrumCorrelation is null ? EvaluateSpatialCoherence(function,
                    (points[i].X - points[j].X) * pixelPitchMillimeters,
                    (points[i].Y - points[j].Y) * pixelPitchMillimeters, alphaMillimeters)
                    : spectrumCorrelation[Modulo(points[i].Y - points[j].Y, effectiveSampling),
                        Modulo(points[i].X - points[j].X, effectiveSampling)];
        }
        var image = new double[height, width];
        var contributions = new Complex[count];
        var anchorY = (kh - 1) / 2;
        var anchorX = (kw - 1) / 2;
        var stablePair = count == 2 && method != SpatialCoherenceMethod.FiniteCorrelationWindow;
        var pairResidualScale = stablePair && method == SpatialCoherenceMethod.ExactSourcePairs
            ? SpatialDecorrelationAmplitude(function, (points[1].X - points[0].X) * pixelPitchMillimeters,
                (points[1].Y - points[0].Y) * pixelPitchMillimeters, alphaMillimeters)
            : stablePair ? Math.Sqrt(Math.Max(0, 1 - correlations[1][0])) : 0;
        for (var y = 0; y < height; y++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            for (var x = 0; x < width; x++)
            {
                ComputationCancellation.ThrowIfCancellationRequested();
                if (stablePair)
                {
                    var pairValue = StablePairPower(Contribution(0), Contribution(1), correlations[1][0], pairResidualScale);
                    if (!double.IsFinite(pairValue))
                        throw new AnalysisDataUnavailableException("Mutual Intensity", "spatial propagation exceeded finite intensity range");
                    image[y, x] = pairValue;
                    continue;
                }
                double value = 0, imaginary = 0, absoluteSum = 0;
                for (var i = 0; i < count; i++)
                {
                    var py = y - points[i].Y + anchorY;
                    var px = x - points[i].X + anchorX;
                    var a = py >= 0 && py < kh && px >= 0 && px < kw
                        ? points[i].Amplitude * impulseResponse[py, px] : Complex.Zero;
                    contributions[i] = a;
                    var diagonal = Power(a);
                    value += diagonal;
                    absoluteSum += diagonal;
                    for (var j = 0; j < i; j++)
                    {
                        var b = contributions[j];
                        var product = a * Complex.Conjugate(b);
                        var forward = 1;
                        var backward = 1;
                        if (method == SpatialCoherenceMethod.FiniteCorrelationWindow)
                        {
                            var dx = points[i].X - points[j].X;
                            var dy = points[i].Y - points[j].Y;
                            forward = dx >= -radius && dx < radius && dy >= -radius && dy < radius ? 1 : 0;
                            backward = -dx >= -radius && -dx < radius && -dy >= -radius && -dy < radius ? 1 : 0;
                        }
                        var term = (forward + backward) * correlations[i][j] * product.Real;
                        imaginary += (forward - backward) * correlations[i][j] * product.Imaginary;
                        value += term;
                        absoluteSum += Math.Abs(term);
                    }
                }
                if (method == SpatialCoherenceMethod.FiniteCorrelationWindow)
                {
                    // Native finite-window quadrature reports |sum|, including its unmatched boundary term.
                    // This is an explicit compatibility result, never silently applied to the exact PSD model.
                    image[y, x] = new Complex(value, imaginary).Magnitude;
                    if (!double.IsFinite(image[y, x]))
                        throw new AnalysisDataUnavailableException("Mutual Intensity", "finite-window propagation exceeded finite intensity range");
                    continue;
                }
                // A PSD covariance can only acquire a negative value through floating-point cancellation.
                if (!double.IsFinite(value) || !double.IsFinite(absoluteSum) || value < -1e-12 * absoluteSum)
                    throw new AnalysisDataUnavailableException("Mutual Intensity", "spatial propagation exceeded finite nonnegative intensity range");
                image[y, x] = Math.Max(0, value);

                Complex Contribution(int i)
                {
                    var py = y - points[i].Y + anchorY;
                    var px = x - points[i].X + anchorX;
                    return py >= 0 && py < kh && px >= 0 && px < kw
                        ? points[i].Amplitude * impulseResponse[py, px] : Complex.Zero;
                }
            }
        }
        return image;
    }

    internal static void ValidateSpatialMethod(SpatialCoherenceMethod method, double fraction, int sampling)
    {
        if (!Enum.IsDefined(method) || !double.IsFinite(fraction) || fraction <= 0 || fraction >= 1
            || sampling != 0 && (sampling is < 2 or > 512 || (sampling & (sampling - 1)) != 0))
            throw new ArgumentOutOfRangeException(nameof(method), "Invalid spatial coherence method, fraction or Fourier sampling (zero for automatic, or power of two from 2 to 512).");
    }

    internal static int ResolveSpatialCoherenceSampling(int requested, int kernelWidth, int kernelHeight)
    {
        var size = requested == 0
            ? AnalysisResourceLimits.RoundUpPowerOfTwo(Math.Max(2, Math.Max(kernelWidth, kernelHeight)), nameof(requested)) : requested;
        if (size > 512) throw new ArgumentOutOfRangeException(nameof(requested), "Automatic coherence Fourier sampling exceeds 512; reduce the complex kernel support.");
        return size;
    }

    private static double[,] BuildFourierCorrelation(SpatialCoherenceFunction function, double alpha, double pitch,
        double fraction, int size)
    {
        var spectrum = new Complex[size, size];
        for (var y = 0; y < size; y++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            for (var x = 0; x < size; x++)
                spectrum[y, x] = EvaluateSpatialCoherence(function,
                    (x < size / 2 ? x : x - size) * pitch, (y < size / 2 ? y : y - size) * pitch, alpha);
        }
        DiffractionEngine.FourierTransform2D(spectrum, false);
        var shells = new double[size / 2 + 1];
        double total = 0, peak = 0;
        for (var y = 0; y < size; y++) for (var x = 0; x < size; x++)
        {
            var radius = Math.Max(Math.Abs(x < size / 2 ? x : x - size), Math.Abs(y < size / 2 ? y : y - size));
            shells[radius] += spectrum[y, x].Magnitude;
            total += spectrum[y, x].Magnitude;
            peak = Math.Max(peak, spectrum[y, x].Real);
        }
        var retainedRadius = 0;
        var retained = shells[0];
        while (retainedRadius < size / 2 && retained < fraction * total) retained += shells[++retainedRadius];
        double weight = 0;
        for (var y = 0; y < size; y++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            for (var x = 0; x < size; x++)
            {
                var radius = Math.Max(Math.Abs(x < size / 2 ? x : x - size), Math.Abs(y < size / 2 ? y : y - size));
                var value = radius <= retainedRadius && spectrum[y, x].Real >= .001 * peak ? spectrum[y, x].Real : 0;
                spectrum[y, x] = value;
                weight += value;
            }
        }
        if (!double.IsFinite(weight) || weight <= 0)
            throw new AnalysisDataUnavailableException("Mutual Intensity", "no finite positive coherence modes remain");
        DiffractionEngine.FourierTransform2D(spectrum, true);
        var correlation = new double[size, size];
        // Normalize modal weights to preserve the prescribed source irradiance Gamma(0)=1.
        var scale = (double)size * size / weight;
        for (var y = 0; y < size; y++) for (var x = 0; x < size; x++) correlation[y, x] = spectrum[y, x].Real * scale;
        return correlation;
    }

    private static int Modulo(int value, int size) => (value % size + size) % size;

    private static double Sinc(double x) => x == 0 ? 1
        : double.IsFinite(x) && Math.Abs(x) <= double.MaxValue / Math.PI ? Math.Sin(Math.PI * x) / (Math.PI * x) : 0;
}

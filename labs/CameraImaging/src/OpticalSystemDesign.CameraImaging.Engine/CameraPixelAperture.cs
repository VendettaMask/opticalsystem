using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis.Imaging;

/// <summary>Centered square active area. QE may refer to the whole pixel or only its active area.</summary>
public sealed record CameraPixelSampling(int SamplesPerAxis = 1, double FillFactor = 1, bool QeIncludesFillFactor = true);
public readonly record struct CameraPixelQuadratureNode(double XOffsetPixels, double YOffsetPixels, double Weight);

/// <summary>Actual active-area ray quadrature plus the frozen V6 grid-averaging protocol.
/// New 2D application runs restrict the legacy averaging path to full fill.</summary>
public static class CameraPixelAperture
{
    public static void Validate(CameraPixelSampling? sampling)
    {
        if (sampling is null) return;
        if (sampling.SamplesPerAxis is not (1 or 2 or 4) || !double.IsFinite(sampling.FillFactor)
            || sampling.FillFactor is < .01 or > 1 || sampling.SamplesPerAxis == 1 && sampling.FillFactor != 1)
            throw new ArgumentException("像元采样须为中心、2×2 或 4×4；填充因子为 0.01–1，非满填充须使用面积采样。");
    }

    public static (int Width, int Height) OutputDimensions(int width, int height, CameraPixelSampling? sampling)
    {
        Validate(sampling); var n = sampling?.SamplesPerAxis ?? 1;
        // Preserve the existing image allocation limit, including the expanded optical grid.
        AnalysisResourceLimits.ValidateImageDimensions(width, height, "Camera optical samples");
        if (width % n != 0 || height % n != 0) throw new ArgumentException("Optical sample dimensions must be divisible by pixel sampling.");
        CameraSensorPipeline.ValidateDimensions(width / n, height / n);
        return (width / n, height / n);
    }

    public static double ChargeScale(CameraPixelSampling? sampling) => sampling is { QeIncludesFillFactor: false } ? sampling.FillFactor : 1;

    /// <summary>Normalized Gaussian area quadrature INSIDE the actual centered active square.
    /// Callers evaluate physical irradiance at these coordinates; no fixed-grid cell averaging is implied.</summary>
    public static IReadOnlyList<CameraPixelQuadratureNode> Quadrature(CameraPixelSampling? sampling)
    {
        Validate(sampling); var n = sampling?.SamplesPerAxis ?? 1;
        if (n == 1) return Array.AsReadOnly(new[] { new CameraPixelQuadratureNode(0, 0, 1) });
        var nodes = n == 2 ? new[] { -.57735026918962576451, .57735026918962576451 }
            : new[] { -.86113631159405257522, -.33998104358485626480, .33998104358485626480, .86113631159405257522 };
        var weights = n == 2 ? new[] { .5, .5 }
            : new[] { .17392742256872692869, .32607257743127307131, .32607257743127307131, .17392742256872692869 };
        var half = Math.Sqrt(sampling!.FillFactor) / 2;
        return Array.AsReadOnly((from y in Enumerable.Range(0, n) from x in Enumerable.Range(0, n)
            select new CameraPixelQuadratureNode(nodes[x] * half, nodes[y] * half, weights[x] * weights[y])).ToArray());
    }

    public static RgbImage Average(RgbImage samples, CameraPixelSampling? sampling)
    {
        ArgumentNullException.ThrowIfNull(samples);
        var (width, height) = OutputDimensions(samples.Width, samples.Height, sampling);
        var n = sampling?.SamplesPerAxis ?? 1;
        if (n == 1) return samples;
        var weights = new double[n]; var half = Math.Sqrt(sampling!.FillFactor) / 2;
        for (var i = 0; i < n; i++)
            weights[i] = Math.Max(0, Math.Min((i + 1d) / n - .5, half) - Math.Max((double)i / n - .5, -half)) / (2 * half);
        var values = new double[samples.Channels, height, width];
        for (var y = 0; y < height; y++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            for (var x = 0; x < width; x++) for (var c = 0; c < samples.Channels; c++)
                for (var yy = 0; yy < n; yy++) for (var xx = 0; xx < n; xx++)
                {
                    var value = samples[c, y * n + yy, x * n + xx];
                    if (!double.IsFinite(value) || value < 0) throw new ArgumentException("Irradiance must be finite and nonnegative.");
                    values[c, y, x] += value * weights[yy] * weights[xx];
                }
        }
        return new(values);
    }
}

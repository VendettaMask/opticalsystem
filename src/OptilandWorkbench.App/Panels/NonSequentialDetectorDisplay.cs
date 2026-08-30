using OptilandWorkbench.Application.Contracts;

namespace OptilandWorkbench.App.Panels;

internal enum DetectorDisplayNormalization
{
    Absolute,
    Peak,
    Sum
}

internal enum DetectorProfileAxis
{
    X,
    Y
}

internal sealed record DetectorDisplayFrame(
    IReadOnlyList<double> Values,
    string ValueUnit,
    double? ValueMinimum,
    double? ValueMaximum);

internal static class NonSequentialDetectorDisplay
{
    public const int MaximumSmoothingRadius = 8;

    public static DetectorDisplayFrame Transform(
        IReadOnlyList<double> source,
        int width,
        int height,
        string valueUnit,
        DetectorDisplayNormalization normalization,
        int smoothingRadius,
        bool logarithmic,
        double? manualMinimum,
        double? manualMaximum)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (width <= 0 || height <= 0 || source.Count != checked(width * height))
        {
            throw new ArgumentException("Detector dimensions do not match the value array.", nameof(source));
        }
        if (smoothingRadius is < 0 or > MaximumSmoothingRadius)
        {
            throw new ArgumentOutOfRangeException(nameof(smoothingRadius));
        }
        if (manualMinimum.HasValue != manualMaximum.HasValue
            || manualMinimum is { } minimum && (!double.IsFinite(minimum)
                || manualMaximum is not { } maximum
                || !double.IsFinite(maximum)
                || minimum >= maximum))
        {
            throw new ArgumentOutOfRangeException(nameof(manualMinimum));
        }

        var values = source.Select(value => double.IsFinite(value) ? value : 0).ToArray();
        if (smoothingRadius > 0)
        {
            values = BoxSmooth(values, width, height, smoothingRadius);
        }

        var scale = normalization switch
        {
            DetectorDisplayNormalization.Peak => values.Max(),
            DetectorDisplayNormalization.Sum => values.Sum(),
            _ => 1
        };
        if (normalization != DetectorDisplayNormalization.Absolute && scale > 0)
        {
            for (var index = 0; index < values.Length; index++)
            {
                values[index] /= scale;
            }

            valueUnit = normalization == DetectorDisplayNormalization.Peak
                ? "peak-normalized"
                : "sum-normalized";
        }

        if (logarithmic)
        {
            for (var index = 0; index < values.Length; index++)
            {
                values[index] = values[index] > 0 ? Math.Log10(values[index]) : double.NaN;
            }

            valueUnit = $"log10 {valueUnit}";
        }

        return new DetectorDisplayFrame(
            values,
            valueUnit,
            manualMinimum,
            manualMaximum);
    }

    public static IReadOnlyList<AnalysisPointDto> Profile(
        NonSequentialDetectorViewDto view,
        IReadOnlyList<double> values,
        DetectorProfileAxis axis,
        int index)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count != checked(view.PixelsX * view.PixelsY))
        {
            throw new ArgumentException("Detector dimensions do not match the display array.", nameof(values));
        }

        if (axis == DetectorProfileAxis.X)
        {
            index = Math.Clamp(index, 0, view.PixelsY - 1);
            return Enumerable.Range(0, view.PixelsX)
                .Select(x => new AnalysisPointDto(
                    view.XMinimum + (x + 0.5) * (view.XMaximum - view.XMinimum) / view.PixelsX,
                    values[(index * view.PixelsX) + x]))
                .ToArray();
        }

        index = Math.Clamp(index, 0, view.PixelsX - 1);
        return Enumerable.Range(0, view.PixelsY)
            .Select(y => new AnalysisPointDto(
                view.YMinimum + (y + 0.5) * (view.YMaximum - view.YMinimum) / view.PixelsY,
                values[(y * view.PixelsX) + index]))
            .ToArray();
    }

    public static (AnalysisAxisQuantity Quantity, AnalysisAxisUnit Unit) ValueAxis(
        NonSequentialDetectorDataType dataType,
        bool transformed) => transformed
        ? (AnalysisAxisQuantity.Unspecified, AnalysisAxisUnit.Dimensionless)
        : dataType switch
        {
            NonSequentialDetectorDataType.PixelPower =>
                (AnalysisAxisQuantity.Power, AnalysisAxisUnit.Watt),
            NonSequentialDetectorDataType.IncoherentIrradiance =>
                (AnalysisAxisQuantity.Irradiance, AnalysisAxisUnit.WattsPerSquareMillimeter),
            NonSequentialDetectorDataType.HitCount =>
                (AnalysisAxisQuantity.Count, AnalysisAxisUnit.Dimensionless),
            NonSequentialDetectorDataType.RadiantIntensity =>
                (AnalysisAxisQuantity.Intensity, AnalysisAxisUnit.WattsPerSteradian),
            _ => throw new ArgumentOutOfRangeException(nameof(dataType))
        };

    private static double[] BoxSmooth(
        IReadOnlyList<double> source,
        int width,
        int height,
        int radius)
    {
        var stride = width + 1;
        var integral = new double[(width + 1) * (height + 1)];
        for (var y = 0; y < height; y++)
        {
            var rowSum = 0.0;
            for (var x = 0; x < width; x++)
            {
                rowSum += source[(y * width) + x];
                integral[((y + 1) * stride) + x + 1] = integral[(y * stride) + x + 1] + rowSum;
            }
        }

        var output = new double[source.Count];
        for (var y = 0; y < height; y++)
        {
            var y0 = Math.Max(0, y - radius);
            var y1 = Math.Min(height - 1, y + radius);
            for (var x = 0; x < width; x++)
            {
                var x0 = Math.Max(0, x - radius);
                var x1 = Math.Min(width - 1, x + radius);
                var sum = integral[((y1 + 1) * stride) + x1 + 1]
                    - integral[(y0 * stride) + x1 + 1]
                    - integral[((y1 + 1) * stride) + x0]
                    + integral[(y0 * stride) + x0];
                output[(y * width) + x] = sum / ((x1 - x0 + 1) * (y1 - y0 + 1));
            }
        }

        return output;
    }
}

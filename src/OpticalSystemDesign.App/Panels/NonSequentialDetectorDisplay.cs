using OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.App.Panels;

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
internal enum DetectorDisplayDomain { Scalar, Signed, Phase }

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
        double? manualMaximum, DetectorDisplayDomain domain = DetectorDisplayDomain.Scalar)
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
        if (!Enum.IsDefined(domain)) throw new ArgumentOutOfRangeException(nameof(domain));
        if (domain == DetectorDisplayDomain.Phase && (normalization != DetectorDisplayNormalization.Absolute || logarithmic || smoothingRadius != 0))
            throw new ArgumentException("相位使用原始角度与有效像素掩码，不能按强度求和归一、取对数或跨越 ±180° 线性平滑。");
        if (domain == DetectorDisplayDomain.Signed && (normalization == DetectorDisplayNormalization.Sum || logarithmic))
            throw new ArgumentException("有符号分量不支持求和归一或对数显示。");
        if (manualMinimum.HasValue != manualMaximum.HasValue
            || manualMinimum is { } minimum && (!double.IsFinite(minimum)
                || manualMaximum is not { } maximum
                || !double.IsFinite(maximum)
                || minimum >= maximum))
        {
            throw new ArgumentOutOfRangeException(nameof(manualMinimum));
        }

        var values = domain == DetectorDisplayDomain.Phase ? source.ToArray() : source.Select(value => double.IsFinite(value) ? value : 0).ToArray();
        if (smoothingRadius > 0)
        {
            values = BoxSmooth(values, width, height, smoothingRadius);
        }

        var scale = normalization switch
        {
            DetectorDisplayNormalization.Peak => domain == DetectorDisplayDomain.Signed ? values.Max(v => Math.Abs(v)) : values.Max(),
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
            manualMinimum ?? (domain == DetectorDisplayDomain.Phase ? -180 : null),
            manualMaximum ?? (domain == DetectorDisplayDomain.Phase ? 180 : null));
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
            NonSequentialDetectorDataType.CoherentPower or NonSequentialDetectorDataType.RawCoherentPower
                or NonSequentialDetectorDataType.StokesI or NonSequentialDetectorDataType.StokesQ
                or NonSequentialDetectorDataType.StokesU or NonSequentialDetectorDataType.StokesV
                or NonSequentialDetectorDataType.LongitudinalPower => (AnalysisAxisQuantity.Power, AnalysisAxisUnit.Watt),
            NonSequentialDetectorDataType.FieldReal or NonSequentialDetectorDataType.FieldImaginary or NonSequentialDetectorDataType.FieldMagnitude
                => (AnalysisAxisQuantity.FieldAmplitude, AnalysisAxisUnit.SquareRootWatt),
            NonSequentialDetectorDataType.FieldPhase => (AnalysisAxisQuantity.Angle, AnalysisAxisUnit.Degree),
            _ => throw new ArgumentOutOfRangeException(nameof(dataType))
        };

    public static DetectorDisplayDomain Domain(NonSequentialDetectorDataType type) => type switch
    {
        NonSequentialDetectorDataType.FieldPhase => DetectorDisplayDomain.Phase,
        NonSequentialDetectorDataType.FieldReal or NonSequentialDetectorDataType.FieldImaginary
            or NonSequentialDetectorDataType.StokesQ or NonSequentialDetectorDataType.StokesU or NonSequentialDetectorDataType.StokesV => DetectorDisplayDomain.Signed,
        _ => DetectorDisplayDomain.Scalar
    };

    public static string DataLabel(NonSequentialDetectorDataType type) => type switch
    {
        NonSequentialDetectorDataType.CoherentPower => "相干功率", NonSequentialDetectorDataType.RawCoherentPower => "未归一幅平方",
        NonSequentialDetectorDataType.StokesI => "Stokes I", NonSequentialDetectorDataType.StokesQ => "Stokes Q",
        NonSequentialDetectorDataType.StokesU => "Stokes U", NonSequentialDetectorDataType.StokesV => "Stokes V",
        NonSequentialDetectorDataType.LongitudinalPower => "纵向功率",
        NonSequentialDetectorDataType.FieldReal => "复场实部", NonSequentialDetectorDataType.FieldImaginary => "复场虚部",
        NonSequentialDetectorDataType.FieldMagnitude => "复场幅值", NonSequentialDetectorDataType.FieldPhase => "相位",
        _ => type.ToString()
    };

    public static string ExportCsv(NonSequentialDetectorViewDto view)
    {
        var text = new System.Text.StringBuilder();
        var field = view.FieldSelection;
        if (field is not null)
        {
            static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
            text.AppendLine("#DataType," + view.DataType);
            text.AppendLine("#WavelengthNumber," + field.WavelengthNumber);
            text.AppendLine("#CoherenceGroup," + Csv(field.CoherenceGroup));
            text.AppendLine("#JonesMode," + (field.Mode + 1));
            text.AppendLine("#DetectorLocalComponent," + field.Component);
            text.AppendLine("#ValueUnit," + Csv(view.ValueUnit));
            text.AppendLine("#Scope,raw ray amplitude sum; wrapped phase; invalid phase cells are empty");
        }
        text.AppendLine(field is null ? "x,y,value" : "x_mm,y_mm,value,valid");
        for (var y = 0; y < view.PixelsY; y++)
            for (var x = 0; x < view.PixelsX; x++)
            {
                var px = view.XMinimum + (x + .5) * (view.XMaximum - view.XMinimum) / view.PixelsX;
                var py = view.YMinimum + (y + .5) * (view.YMaximum - view.YMinimum) / view.PixelsY;
                var index = y * view.PixelsX + x; var value = view.Values[index];
                var valid = view.ValidPixels?[index] ?? double.IsFinite(value);
                text.Append(px.ToString("G17", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                    .Append(py.ToString("G17", System.Globalization.CultureInfo.InvariantCulture)).Append(',');
                if (field is null || valid) text.Append(value.ToString("G17", System.Globalization.CultureInfo.InvariantCulture));
                if (field is not null) text.Append(valid ? ",1" : ",0");
                text.AppendLine();
                if (text.Length > Application.Services.BoundedApplicationFile.MaximumExportBytes)
                    throw new InvalidOperationException("探测器 CSV 超出既有导出限额。");
            }
        return text.ToString();
    }

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

using System.Globalization;
using System.Numerics;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis;

public sealed class MtfSurfaceAnalysis(
    Optic optic,
    MtfComputationMethod method,
    MtfComputationSettings settings,
    int fieldNumber = 1,
    int wavelengthNumber = 0,
    string showAs = "",
    FftMtfDataType dataType = FftMtfDataType.Modulation,
    double rotation = 0,
    double scale = 1) : BaseAnalysis(optic)
{
    public override string Name => method == MtfComputationMethod.Huygens ? "Huygens Surface MTF" : "FFT Surface MTF";

    public override AnalysisData GenerateData()
    {
        var fields = AnalysisTrace.DefinedFieldSamples(Optic);
        var wavelengths = MtfMethodEvaluator.SelectWavelengths(Optic, wavelengthNumber);
        if (fields.Count == 0 || wavelengths.Count == 0)
            return AnalysisData.Unavailable(Name, "No fields or wavelengths");
        if (fieldNumber < 1 || fieldNumber > fields.Count)
            throw new ArgumentOutOfRangeException(nameof(fieldNumber));
        if (method is not (MtfComputationMethod.Fourier or MtfComputationMethod.Huygens)
            || dataType is not (FftMtfDataType.Modulation or FftMtfDataType.Real or FftMtfDataType.Imaginary)
            || (method == MtfComputationMethod.Huygens && dataType != FftMtfDataType.Modulation))
            throw new ArgumentException("Unsupported MTF surface computation.");

        var sample = fields[fieldNumber - 1];
        var field = (sample.Hx, sample.Hy);
        var isHuygens = method == MtfComputationMethod.Huygens;
        if (!isHuygens && FocalImageDomain.CheckFftImageDomain(Name, Optic, field, wavelengths[0]) is { } unavailable)
            return unavailable;

        var display = string.IsNullOrEmpty(showAs) ? (isHuygens ? "伪彩色" : "表面") : showAs;
        var frequencyUnit = ImageSpaceAnalysisSupport.SpatialFrequencyUnit(Optic);
        var unitLabel = ImageSpaceAnalysisSupport.SpatialFrequencyUnitLabel(Optic);
        var valueLabel = dataType switch
        {
            FftMtfDataType.Real => "OTF 实部",
            FftMtfDataType.Imaginary => "OTF 虚部",
            _ => "MTF"
        };
        var referenceWavelength = wavelengths.FirstOrDefault(wave => wave.IsPrimary) ?? wavelengths[0];
        var spectra = new List<OtfGrid>();
        var pupilSampling = settings.PupilSampling;
        if (isHuygens)
        {
            var psf = MtfMethodEvaluator.ComputeHuygensPolychromaticPsf(Optic, field, wavelengths, settings);
            var transform = DiffractionEngine.ComputeCenteredPsfTransform(psf, doubleTransformSize: true);
            var unitScale = Optic.ImageSpaceAfocal ? 1 : 1000;
            var step = unitScale / (psf.SampleSpacingMicrometers * transform.GetLength(0));
            spectra.Add(new OtfGrid(transform, step, step, 1));
        }
        else
        {
            var gridSize = AnalysisResourceLimits.RoundUpPowerOfTwo(checked(2 * pupilSampling), nameof(pupilSampling));
            AnalysisResourceLimits.ValidateAggregateGridWork(gridSize, gridSize, 1, wavelengths.Count,
                checked(pupilSampling * pupilSampling), Name);
            var totalWeight = wavelengths.Sum(wave => wave.Weight);
            foreach (var wavelength in wavelengths)
            {
                if (totalWeight > 0 && wavelength.Weight <= 0) continue;
                var psf = DiffractionEngine.ComputeFftPsf(Optic, field, wavelength, pupilSampling, gridSize,
                    settings.UsePolarization, cellCenteredPupil: true, zemaxFftSampling: true,
                    referenceWavelength: referenceWavelength, imageDelta: -1);
                psf.PupilPhaseSampling?.EnforceForCurrentRequest(Name);
                psf = psf with { FrequencySampleCount = pupilSampling - 1 };
                var (yStep, xStep, _) = DiffractionEngine.FftMtfFrequencyScale(psf, Optic, wavelength);
                var weight = totalWeight > 0 ? wavelength.Weight / totalWeight : 1.0 / wavelengths.Count;
                spectra.Add(new OtfGrid(DiffractionEngine.ComputeCenteredPsfTransform(psf), xStep, yStep, weight));
            }
        }
        if (spectra.Any(grid => !double.IsFinite(grid.XStep) || grid.XStep <= 0
            || !double.IsFinite(grid.YStep) || grid.YStep <= 0))
            return AnalysisData.Unavailable(Name, "No finite spatial frequency scale");

        // Keep the full calculated bandwidth and the center sample while bounding the drawn mesh.
        var limit = spectra[0].Center - 1;
        var halfSamples = Math.Min(limit, 64);
        var xExtent = spectra.Max(grid => grid.XStep) * limit;
        var yExtent = spectra.Max(grid => grid.YStep) * limit;
        var points = new List<AnalysisPoint>((2 * halfSamples + 1) * (2 * halfSamples + 1));
        for (var row = -halfSamples; row <= halfSamples; row++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var y = yExtent * row / halfSamples;
            for (var column = -halfSamples; column <= halfSamples; column++)
            {
                var x = xExtent * column / halfSamples;
                var otf = Complex.Zero;
                foreach (var grid in spectra) otf += grid.Sample(x, y);
                var value = dataType switch
                {
                    FftMtfDataType.Real => otf.Real,
                    FftMtfDataType.Imaginary => otf.Imaginary,
                    _ => Math.Clamp(otf.Magnitude, 0, 1)
                };
                points.Add(new AnalysisPoint(x, y, Value: value));
            }
        }

        var series = new AnalysisSeries($"X 空间频率 ({unitLabel})", $"Y 空间频率 ({unitLabel})", points,
            AnalysisSeriesKind.Heatmap, ValueLabel: valueLabel, ColorMap: MtfGridOutput.ColorMap(display),
            ValueMinimum: dataType == FftMtfDataType.Modulation ? 0 : -1, ValueMaximum: 1,
            XQuantity: AnalysisAxisQuantity.SpatialFrequency, XUnit: frequencyUnit,
            YQuantity: AnalysisAxisQuantity.SpatialFrequency, YUnit: frequencyUnit,
            ValueQuantity: AnalysisAxisQuantity.Modulation, ValueUnit: AnalysisAxisUnit.Dimensionless);
        var rows = points.Select(point => MtfGridOutput.Row(point.X, point.Y, point.Value!.Value));
        return MtfGridOutput.Build(Name, display, series, rows, new Dictionary<string, object>
        {
            ["MtfGridField"] = MtfPresentation.FieldName(fieldNumber),
            ["MtfGridData"] = valueLabel,
            ["MtfGridSampling"] = isHuygens ? $"{pupilSampling} × {pupilSampling} / {settings.ImageSize} × {settings.ImageSize}" : $"{pupilSampling} × {pupilSampling}",
            ["MtfGridSize"] = $"{2 * halfSamples + 1} × {2 * halfSamples + 1}",
            ["MtfGridWavelengths"] = MtfGridOutput.WavelengthRange(wavelengths),
            ["Rotation"] = rotation,
            ["DisplayScale"] = scale
        });
    }

    private sealed record OtfGrid(Complex[,] Transform, double XStep, double YStep, double Weight)
    {
        public int Center => Transform.GetLength(0) / 2;

        public Complex Sample(double frequencyX, double frequencyY)
        {
            var x = Center + frequencyX / XStep;
            var y = Center + frequencyY / YStep;
            var last = Transform.GetLength(0) - 1;
            var dc = Transform[Center, Center];
            if (x < -1e-10 || y < -1e-10 || x > last + 1e-10 || y > last + 1e-10 || dc.Magnitude <= 1e-30)
                return Complex.Zero;
            x = Math.Clamp(x, 0, last);
            y = Math.Clamp(y, 0, last);
            var left = Math.Min((int)x, last - 1);
            var bottom = Math.Min((int)y, last - 1);
            var alongBottom = Transform[bottom, left] + (x - left) * (Transform[bottom, left + 1] - Transform[bottom, left]);
            var alongTop = Transform[bottom + 1, left] + (x - left) * (Transform[bottom + 1, left + 1] - Transform[bottom + 1, left]);
            return (alongBottom + (y - bottom) * (alongTop - alongBottom)) / dc * Weight;
        }
    }
}

public sealed record MtfFieldMapSettings(
    double Frequency = 20,
    int XSamples = 9,
    int YSamples = 9,
    double XWidth = 0,
    double YWidth = 0,
    int ReferenceField = 0,
    int WavelengthNumber = 0,
    string Data = "Average",
    string ShowAs = "伪彩色",
    bool RemoveVignettingFactors = true,
    bool FixedColorScale = false);

public sealed class MtfFieldMapAnalysis(
    Optic optic, MtfComputationMethod method, MtfComputationSettings sampling, MtfFieldMapSettings settings) : BaseAnalysis(optic)
{
    public override string Name => method == MtfComputationMethod.Geometric ? "Geometric MTF Map" : "FFT MTF Map";

    public override AnalysisData GenerateData()
    {
        if (method is not (MtfComputationMethod.Fourier or MtfComputationMethod.Geometric))
            throw new ArgumentOutOfRangeException(nameof(method));
        AnalysisResourceLimits.ValidateAnalysisGrid(settings.XSamples, settings.YSamples, Name);
        if (settings.XSamples < 2 || settings.YSamples < 2
            || !double.IsFinite(settings.Frequency) || settings.Frequency < 0
            || !double.IsFinite(settings.XWidth) || settings.XWidth < 0
            || !double.IsFinite(settings.YWidth) || settings.YWidth < 0)
            throw new ArgumentOutOfRangeException(nameof(settings));
        var opticForMap = AnalysisTrace.PrepareVignettingFactors(Optic, settings.RemoveVignettingFactors);
        var wavelengths = MtfMethodEvaluator.SelectWavelengths(opticForMap, settings.WavelengthNumber);
        var radius = FieldCoordinates.MaximumRadius(opticForMap.Fields);
        if (radius <= 1e-12 || wavelengths.Count == 0)
            return AnalysisData.Unavailable(Name, "Define an off-axis field and at least one wavelength before calculating a field map.");
        if (settings.ReferenceField < 0 || settings.ReferenceField > opticForMap.Fields.Count)
            throw new ArgumentOutOfRangeException(nameof(settings.ReferenceField));
        var center = settings.ReferenceField == 0 ? null : opticForMap.Fields[settings.ReferenceField - 1];
        var xWidth = settings.XWidth > 0 ? settings.XWidth : radius;
        var yWidth = settings.YWidth > 0 ? settings.YWidth : radius;
        var xMin = (center?.X ?? 0) - xWidth / 2;
        var yMin = (center?.Y ?? 0) - yWidth / 2;
        var rayWork = (double)settings.XSamples * settings.YSamples * wavelengths.Count
            * sampling.PupilSampling * sampling.PupilSampling * Math.Max(1, opticForMap.SurfaceGroup.Items.Count);
        if (rayWork > AnalysisResourceLimits.MaximumAnalysisRayWork)
            throw new ArgumentOutOfRangeException(nameof(settings), "Reduce the field grid or pupil sampling to fit the analysis ray budget.");
        var select = settings.Data switch
        {
            "Tangential" => ((Func<double, double, double>)((t, _) => t), "子午 MTF"),
            "Sagittal" => ((Func<double, double, double>)((_, s) => s), "弧矢 MTF"),
            "Min" => ((Func<double, double, double>)Math.Min, "最小 MTF"),
            "Max" => ((Func<double, double, double>)Math.Max, "最大 MTF"),
            "Average" => ((Func<double, double, double>)((t, s) => (t + s) / 2), "平均 MTF"),
            _ => throw new ArgumentException("Unknown MTF field map data selection.")
        };

        var effectiveSampling = method == MtfComputationMethod.Geometric
            ? sampling with { Distribution = "uniform-intervals" } : sampling;
        var points = new List<AnalysisPoint>(settings.XSamples * settings.YSamples);
        var rows = new List<IReadOnlyList<string>>(points.Capacity);
        for (var row = 0; row < settings.YSamples; row++)
        {
            var y = yMin + (row + 0.5) * yWidth / settings.YSamples;
            for (var column = 0; column < settings.XSamples; column++)
            {
                ComputationCancellation.ThrowIfCancellationRequested();
                var x = xMin + (column + 0.5) * xWidth / settings.XSamples;
                var (t, s) = MtfMethodEvaluator.EvaluatePolychromatic(opticForMap, method,
                    (x / radius, y / radius), wavelengths, settings.Frequency, effectiveSampling);
                var value = select.Item1(t, s);
                points.Add(new AnalysisPoint(x, y, Value: value));
                rows.Add(MtfGridOutput.Row(x, y, value, t, s));
            }
        }

        var unit = AnalysisTrace.FieldAxisUnit(Optic);
        var fieldUnit = unit == AnalysisAxisUnit.Degree ? "°" : "mm";
        var quantity = AnalysisTrace.FieldAxisQuantity(Optic);
        var minimum = points.Min(point => point.Value!.Value);
        var maximum = points.Max(point => point.Value!.Value);
        var useFixedScale = settings.FixedColorScale || maximum - minimum < 1e-10;
        var series = new AnalysisSeries($"X 视场 ({fieldUnit})", $"Y 视场 ({fieldUnit})", points, AnalysisSeriesKind.Heatmap,
            ValueLabel: select.Item2, ColorMap: MtfGridOutput.ColorMap(settings.ShowAs),
            ValueMinimum: useFixedScale ? 0 : minimum, ValueMaximum: useFixedScale ? 1 : maximum,
            XQuantity: quantity, XUnit: unit, YQuantity: quantity, YUnit: unit,
            ValueQuantity: AnalysisAxisQuantity.Modulation, ValueUnit: AnalysisAxisUnit.Dimensionless);
        var values = new Dictionary<string, object>
        {
            ["MtfGridField"] = center is null ? "视场原点" : MtfPresentation.FieldName(settings.ReferenceField),
            ["MtfGridData"] = select.Item2,
            ["MtfGridSampling"] = $"{sampling.PupilSampling} × {sampling.PupilSampling}",
            ["MtfGridSize"] = $"{settings.XSamples} × {settings.YSamples}",
            ["MtfGridWavelengths"] = MtfGridOutput.WavelengthRange(wavelengths),
            ["MtfGridColorScale"] = useFixedScale ? "0–1" : "按数据范围",
            ["MtfGridFrequency"] = $"{settings.Frequency.ToString("G", CultureInfo.InvariantCulture)} {ImageSpaceAnalysisSupport.SpatialFrequencyUnitLabel(Optic)}"
        };
        return MtfGridOutput.Build(Name, settings.ShowAs, series, rows, values, "子午 T", "弧矢 S");
    }
}

internal static class MtfGridOutput
{
    public static AnalysisColorMap ColorMap(string showAs) => showAs == "灰度" ? AnalysisColorMap.Gray : AnalysisColorMap.Viridis;

    public static string WavelengthRange(IReadOnlyList<Wavelength> wavelengths) => string.Join("–",
        new[] { wavelengths.Min(wave => wave.Micrometers), wavelengths.Max(wave => wave.Micrometers) }
            .Distinct().Select(value => value.ToString("F4", CultureInfo.InvariantCulture))) + " µm";

    public static IReadOnlyList<string> Row(params double[] values) =>
        values.Select(value => value.ToString("G10", CultureInfo.InvariantCulture)).ToArray();

    public static AnalysisData Build(string name, string showAs, AnalysisSeries series,
        IEnumerable<IReadOnlyList<string>> rows, Dictionary<string, object> values, params string[] extraColumns)
    {
        values["DisplayAs"] = showAs;
        values["MtfGridMinimum"] = series.Points.Min(point => point.Value!.Value);
        values["MtfGridMaximum"] = series.Points.Max(point => point.Value!.Value);
        var xs = series.Points.Select(point => point.X).Distinct().Order().ToArray();
        var ys = series.Points.Select(point => point.Y).Distinct().Order().ToArray();
        var width = (xs[^1] - xs[0]) * xs.Length / (xs.Length - 1);
        var height = (ys[^1] - ys[0]) * ys.Length / (ys.Length - 1);
        var span = Math.Max(width, height);
        return new AnalysisData(name, values, series,
            PlotOptions: new AnalysisPlotOptions(EqualAspect: true,
                SpatialDisplayScaleX: width / span, SpatialDisplayScaleY: height / span),
            Table: new AnalysisTable([series.XAxisLabel, series.YAxisLabel, series.ValueLabel, .. extraColumns], rows.ToArray()));
    }
}

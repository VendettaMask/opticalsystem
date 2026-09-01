using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Analysis;

public sealed class MmdftPsfAnalysis : BaseAnalysis
{
    private readonly int _numRays;
    private readonly int _imageSize;
    private readonly double? _pixelPitchMicrometers;

    public MmdftPsfAnalysis(
        Optic optic,
        int numRays = 16,
        int imageSize = 32,
        double? pixelPitchMicrometers = null) : base(optic)
    {
        _numRays = Math.Max(2, numRays);
        _imageSize = Math.Max(1, imageSize);
        _pixelPitchMicrometers = pixelPitchMicrometers;
    }

    public override string Name => "MMDFT PSF";

    public override AnalysisData GenerateData()
    {
        var wavelength = Optic.Wavelengths.FirstOrDefault(item => item.IsPrimary)
            ?? Optic.Wavelengths.FirstOrDefault();
        if (wavelength is null)
        {
            return new AnalysisData(Name, new Dictionary<string, object> { ["Status"] = "No wavelengths" });
        }

        var field = SpotAnalysisEngine.DefinedFields(Optic).LastOrDefault();
        var psf = DiffractionEngine.ComputeMmdftPsf(
            Optic,
            field,
            wavelength,
            _numRays,
            _imageSize,
            _pixelPitchMicrometers);
        return DiffractionAnalysisPresentation.CreatePsfData(
            Name,
            "MMDFT",
            "MMDFT PSF",
            psf,
            field,
            wavelength,
            psf.PeakStrehlRatio);
    }
}

public sealed class HuygensPsfAnalysis : BaseAnalysis
{
    private readonly int _numRays;
    private readonly int _imageSize;
    private readonly double _pixelPitchMillimeters;
    private readonly int _wavelengthNumber;
    private readonly int _fieldNumber;
    private readonly double _rotationDegrees;
    private readonly string _type;
    private readonly string _displayAs;
    private readonly bool _usePolarization;
    private readonly bool _normalize;
    private readonly bool _useCentroid;

    public HuygensPsfAnalysis(
        Optic optic,
        int numRays = 9,
        int imageSize = 32,
        double pixelPitchMillimeters = 0.005,
        int wavelengthNumber = -1,
        int fieldNumber = 0,
        double rotationDegrees = 0,
        string type = "线性",
        string displayAs = "伪彩色",
        bool usePolarization = false,
        bool normalize = false,
        bool useCentroid = false) : base(optic)
    {
        _numRays = Math.Max(2, numRays);
        _imageSize = Math.Max(1, imageSize);
        _pixelPitchMillimeters = Math.Max(0, pixelPitchMillimeters);
        _wavelengthNumber = Math.Max(-1, wavelengthNumber);
        _fieldNumber = Math.Max(0, fieldNumber);
        _rotationDegrees = rotationDegrees;
        _type = type;
        _displayAs = displayAs;
        _usePolarization = usePolarization;
        _normalize = normalize;
        _useCentroid = useCentroid;
    }

    public override string Name => "Huygens PSF";

    public override AnalysisData GenerateData()
    {
        var allWavelengths = Optic.Wavelengths.ToArray();
        if (allWavelengths.Length == 0)
        {
            return new AnalysisData(Name, new Dictionary<string, object> { ["Status"] = "No wavelengths" });
        }

        var wavelengths = _wavelengthNumber switch
        {
            < 0 => new[]
            {
                allWavelengths.FirstOrDefault(item => item.IsPrimary) ?? allWavelengths[0]
            },
            0 => allWavelengths,
            _ => new[]
            {
                allWavelengths[Math.Clamp(_wavelengthNumber - 1, 0, allWavelengths.Length - 1)]
            }
        };
        var allFields = SpotAnalysisEngine.DefinedFields(Optic);
        var field = allFields.Count == 0
            ? (Hx: 0.0, Hy: 0.0)
            : _fieldNumber <= 0
                ? allFields[^1]
                : allFields[Math.Clamp(_fieldNumber - 1, 0, allFields.Count - 1)];
        var imageDeltaWavelength = wavelengths.MaxBy(item => item.Micrometers) ?? wavelengths[0];
        var pixelPitchMillimeters = _pixelPitchMillimeters > 0
            ? _pixelPitchMillimeters
            : DiffractionEngine.DefaultHuygensImageDeltaMillimeters(
                Optic,
                field,
                imageDeltaWavelength,
                _numRays);
        var afocalImageSpace = Optic.ImageSpaceAfocal;
        var results = wavelengths
            .Select(wavelength => (
                Wavelength: wavelength,
                Result: DiffractionEngine.ComputeHuygensPsf(
                    Optic,
                    field,
                    wavelength,
                    _numRays,
                    _imageSize,
                    pixelPitchMillimeters,
                    _usePolarization)))
            .ToArray();
        var useConfiguredWeights = results.Any(item => item.Wavelength.Weight > 0);
        var totalWeight = results.Sum(item =>
            useConfiguredWeights ? item.Wavelength.Weight : 1);
        var values = new double[_imageSize, _imageSize];
        for (var row = 0; row < _imageSize; row++)
        {
            for (var column = 0; column < _imageSize; column++)
            {
                values[row, column] = results.Sum(item =>
                    (useConfiguredWeights ? item.Wavelength.Weight : 1)
                    * item.Result.Values[row, column] / 100.0) / totalWeight;
            }
        }

        var rawPeak = values.Cast<double>().DefaultIfEmpty(0).Max();
        var rawCenter = values[_imageSize / 2, _imageSize / 2];
        if (_normalize && rawPeak > 0)
        {
            for (var row = 0; row < _imageSize; row++)
            {
                for (var column = 0; column < _imageSize; column++)
                {
                    values[row, column] /= rawPeak;
                }
            }
        }

        var (centerColumn, centerRow) = _useCentroid
            ? IntensityCentroid(values)
            : (_imageSize / 2, _imageSize / 2);
        var logarithmic = _type.Contains("对数", StringComparison.Ordinal)
            || _type.Contains("log", StringComparison.OrdinalIgnoreCase);
        var sampleSpacing = afocalImageSpace ? pixelPitchMillimeters : pixelPitchMillimeters * 1000;
        var imageUnitLabel = afocalImageSpace
            ? ImageSpaceAnalysisSupport.MilliradianLabel
            : "µm";
        var imageAxisUnit = afocalImageSpace
            ? AnalysisAxisUnit.Milliradian
            : AnalysisAxisUnit.Micrometer;
        var imageQuantity = afocalImageSpace
            ? AnalysisAxisQuantity.IncidentAngle
            : AnalysisAxisQuantity.ImageHeight;
        var points = new List<AnalysisPoint>(_imageSize * _imageSize);
        for (var row = 0; row < _imageSize; row++)
        {
            var y = (row - centerRow) * sampleSpacing;
            for (var column = 0; column < _imageSize; column++)
            {
                var value = values[row, column];
                points.Add(new AnalysisPoint(
                    (column - centerColumn) * sampleSpacing,
                    y,
                    Value: logarithmic ? 10 * Math.Log10(Math.Max(1e-12, value)) : value));
            }
        }

        var extent = _imageSize * sampleSpacing;
        var series = new AnalysisSeries(
            $"X ({imageUnitLabel})",
            $"Y ({imageUnitLabel})",
            points,
            AnalysisSeriesKind.Heatmap,
            ValueLabel: logarithmic ? "Relative Intensity (dB)" : "Relative Intensity",
            XQuantity: imageQuantity,
            XUnit: imageAxisUnit,
            YQuantity: imageQuantity,
            YUnit: imageAxisUnit,
            ValueQuantity: AnalysisAxisQuantity.Irradiance,
            ValueUnit: logarithmic ? AnalysisAxisUnit.Decibel : AnalysisAxisUnit.Dimensionless);
        var title = _usePolarization
            ? wavelengths.Length > 1
                ? "复色光 Polarization-weighted scalar Huygens PSF（Experimental）"
                : "Polarization-weighted scalar Huygens PSF（Experimental）"
            : wavelengths.Length > 1 ? "复色光惠更斯PSF" : "惠更斯PSF";
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["Method"] = "Huygens-Fresnel",
            ["ImagePlane"] = "Chief ray tangent plane",
            ["PupilSampling"] = _numRays,
            ["ImageSize"] = _imageSize,
            ["GridSize"] = _imageSize,
            ["ImageDeltaMicrometers"] = afocalImageSpace ? 0 : sampleSpacing,
            ["PixelPitchMicrometers"] = afocalImageSpace ? 0 : sampleSpacing,
            ["ImageExtentMicrometers"] = afocalImageSpace ? 0 : extent,
            ["ImageDeltaMilliradians"] = afocalImageSpace ? sampleSpacing : 0,
            ["PixelPitchMilliradians"] = afocalImageSpace ? sampleSpacing : 0,
            ["ImageExtentMilliradians"] = afocalImageSpace ? extent : 0,
            ["ImageSpaceAfocal"] = afocalImageSpace,
            ["ImageCoordinateUnit"] = imageUnitLabel,
            ["WorkingFNumber"] = results.Average(item => item.Result.WorkingFNumber),
            ["StrehlRatio"] = rawCenter,
            ["PeakStrehlRatio"] = rawPeak,
            ["WavelengthNumber"] = _wavelengthNumber,
            ["WavelengthRange"] = $"{wavelengths.Min(item => item.Micrometers):0.0000}–{wavelengths.Max(item => item.Micrometers):0.0000} µm",
            ["FieldNumber"] = _fieldNumber,
            ["FieldHx"] = field.Hx,
            ["FieldHy"] = field.Hy,
            ["RotationDegrees"] = _rotationDegrees,
            ["Type"] = _type,
            ["DisplayAs"] = _displayAs,
            ["UsePolarization"] = _usePolarization,
            ["PolarizationModel"] = _usePolarization
                ? "Polarization-weighted scalar approximation"
                : "Scalar",
            ["PolarizationLimit"] = _usePolarization
                ? "Jones component powers are collapsed before scalar Huygens summation; complex cross-polarization interference and longitudinal high-NA fields are not modeled."
                : string.Empty,
            ["Normalized"] = _normalize,
            ["UseCentroid"] = _useCentroid,
            ["CentroidXMicrometers"] = afocalImageSpace ? 0 : (centerColumn - (_imageSize / 2)) * sampleSpacing,
            ["CentroidYMicrometers"] = afocalImageSpace ? 0 : (centerRow - (_imageSize / 2)) * sampleSpacing,
            ["CentroidXMilliradians"] = afocalImageSpace ? (centerColumn - (_imageSize / 2)) * sampleSpacing : 0,
            ["CentroidYMilliradians"] = afocalImageSpace ? (centerRow - (_imageSize / 2)) * sampleSpacing : 0
        }, series, new[] { series }, new AnalysisPlotOptions(
            Title: title,
            EqualAspect: true,
            XMinimum: -extent / 2,
            XMaximum: extent / 2,
            YMinimum: -extent / 2,
            YMaximum: extent / 2));
    }

    private static (double X, double Y) IntensityCentroid(double[,] values)
    {
        var rows = values.GetLength(0);
        var columns = values.GetLength(1);
        var total = 0.0;
        var weightedX = 0.0;
        var weightedY = 0.0;
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var value = Math.Max(0, values[row, column]);
                total += value;
                weightedX += column * value;
                weightedY += row * value;
            }
        }

        return total > 0
            ? (weightedX / total, weightedY / total)
            : (columns / 2, rows / 2);
    }
}

public sealed class HuygensMtfAnalysis : BaseAnalysis
{
    private readonly int _numRays;
    private readonly int _imageSize;
    private readonly double _pixelPitchMillimeters;
    private readonly IReadOnlyList<(double Hx, double Hy)>? _fields;
    private readonly double? _maximumFrequency;
    private readonly int _wavelengthNumber;
    private readonly int _fieldNumber;
    private readonly bool _zemaxCompatible;

    public HuygensMtfAnalysis(
        Optic optic,
        int numRays = 9,
        int imageSize = 32,
        double pixelPitchMillimeters = 0.005,
        IReadOnlyList<(double Hx, double Hy)>? fields = null,
        double? maximumFrequency = null,
        int wavelengthNumber = -1,
        int fieldNumber = 0,
        bool zemaxCompatible = false) : base(optic)
    {
        _numRays = Math.Max(2, numRays);
        _imageSize = Math.Max(1, imageSize);
        _pixelPitchMillimeters = Math.Max(0, pixelPitchMillimeters);
        _fields = fields;
        _maximumFrequency = maximumFrequency is > 0 && double.IsFinite(maximumFrequency.Value)
            ? maximumFrequency
            : null;
        _wavelengthNumber = wavelengthNumber;
        _fieldNumber = Math.Max(0, fieldNumber);
        _zemaxCompatible = zemaxCompatible;
    }

    public override string Name => "Huygens MTF";

    public override AnalysisData GenerateData()
    {
        var wavelengths = MtfMethodEvaluator.SelectWavelengths(Optic, _wavelengthNumber);
        if (wavelengths.Count == 0)
        {
            return new AnalysisData(Name, new Dictionary<string, object> { ["Status"] = "No wavelengths" });
        }

        var allFields = _fields ?? SpotAnalysisEngine.DefinedFields(Optic);
        var fieldIndices = _fieldNumber <= 0
            ? Enumerable.Range(0, allFields.Count).ToArray()
            : new[] { Math.Clamp(_fieldNumber - 1, 0, Math.Max(0, allFields.Count - 1)) };
        var fields = fieldIndices.Select(index => allFields[index]).ToArray();
        if (fields.Length == 0)
        {
            return new AnalysisData(Name, new Dictionary<string, object> { ["Status"] = "No fields" });
        }

        var sharedPixelPitchMillimeters = _pixelPitchMillimeters > 0
            ? _pixelPitchMillimeters
            : MtfMethodEvaluator.ResolveHuygensImageDeltaMillimeters(
                Optic,
                fields[0],
                wavelengths,
                new MtfComputationSettings(PupilSampling: _numRays, PixelPitchMillimeters: 0));
        var series = new List<AnalysisSeries>();
        var maximumFrequency = 0.0;
        var frequencyLabel = ImageSpaceAnalysisSupport.SpatialFrequencyLabel(Optic);
        var frequencyUnit = ImageSpaceAnalysisSupport.SpatialFrequencyUnit(Optic);
        var frequencyUnitLabel = ImageSpaceAnalysisSupport.SpatialFrequencyUnitLabel(Optic);
        for (var fieldIndex = 0; fieldIndex < fields.Length; fieldIndex++)
        {
            var field = fields[fieldIndex];
            MtfResult fullMtf;
            if (_zemaxCompatible)
            {
                var supportedMaximum = (_imageSize - 1.0)
                    / ((2 * _imageSize - 1.0) * sharedPixelPitchMillimeters);
                var requestedMaximum = _maximumFrequency.HasValue
                    ? Math.Min(_maximumFrequency.Value, supportedMaximum)
                    : supportedMaximum;
                var frequencies = Enumerable.Range(0, 300)
                    .Select(index => requestedMaximum * index / 299.0)
                    .ToArray();
                var values = MtfMethodEvaluator.EvaluatePolychromaticFrequencies(
                    Optic,
                    MtfComputationMethod.Huygens,
                    field,
                    wavelengths,
                    frequencies,
                    new MtfComputationSettings(
                        PupilSampling: _numRays,
                        ImageSize: _imageSize,
                        PixelPitchMillimeters: sharedPixelPitchMillimeters,
                        ZemaxCompatible: true,
                        UseZemaxHuygensSemantics: true));
                fullMtf = new MtfResult(
                    frequencies,
                    values.Select(item => item.Tangential).ToArray(),
                    values.Select(item => item.Sagittal).ToArray(),
                    supportedMaximum);
            }
            else
            {
                var wavelengthResults = wavelengths.Select(wavelength =>
                {
                    var psf = DiffractionEngine.ComputeHuygensPsf(
                        Optic,
                        field,
                        wavelength,
                        _numRays,
                        _imageSize,
                        sharedPixelPitchMillimeters);
                    return (wavelength, DiffractionEngine.ComputePsfMtf(psf));
                }).ToArray();
                fullMtf = MtfMethodEvaluator.CombinePolychromatic(wavelengthResults);
            }

            var mtf = DiffractionEngine.LimitFrequency(fullMtf, _maximumFrequency);
            maximumFrequency = Math.Max(maximumFrequency, fullMtf.Frequency.DefaultIfEmpty(0).Max());
            series.Add(new AnalysisSeries(
                frequencyLabel,
                "Modulation",
                mtf.Frequency.Select((frequency, index) => new AnalysisPoint(frequency, mtf.Tangential[index])).ToArray(),
                Name: MtfPresentation.SeriesName(Optic, field, "Tangential"),
                ColorIndex: fieldIndices[fieldIndex],
                XQuantity: AnalysisAxisQuantity.SpatialFrequency,
                XUnit: frequencyUnit,
                YQuantity: AnalysisAxisQuantity.Modulation,
                YUnit: AnalysisAxisUnit.Dimensionless));
            series.Add(new AnalysisSeries(
                frequencyLabel,
                "Modulation",
                mtf.Frequency.Select((frequency, index) => new AnalysisPoint(frequency, mtf.Sagittal[index])).ToArray(),
                Name: MtfPresentation.SeriesName(Optic, field, "Sagittal"),
                LineStyle: AnalysisLineStyle.Dashed,
                ColorIndex: fieldIndices[fieldIndex],
                XQuantity: AnalysisAxisQuantity.SpatialFrequency,
                XUnit: frequencyUnit,
                YQuantity: AnalysisAxisQuantity.Modulation,
                YUnit: AnalysisAxisUnit.Dimensionless));
        }

        var plottedMaximum = _maximumFrequency.HasValue
            ? Math.Min(_maximumFrequency.Value, maximumFrequency)
            : maximumFrequency;
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["Method"] = "Huygens-Fresnel",
            ["ImagePlane"] = "Chief ray tangent plane",
            ["NumRays"] = _numRays,
            ["ImageSize"] = _imageSize,
            ["PixelPitchMillimeters"] = sharedPixelPitchMillimeters,
            ["ImageDeltaMicrometers"] = Optic.ImageSpaceAfocal ? 0 : sharedPixelPitchMillimeters * 1000,
            ["ImageDeltaMilliradians"] = Optic.ImageSpaceAfocal ? sharedPixelPitchMillimeters : 0,
            ["MaximumFrequency"] = plottedMaximum,
            ["CutoffFrequency"] = maximumFrequency,
            ["FrequencyUnit"] = frequencyUnitLabel,
            ["ImageSpaceAfocal"] = Optic.ImageSpaceAfocal,
            ["WavelengthNumber"] = _wavelengthNumber,
            ["WavelengthsMicrometers"] = wavelengths.Select(item => item.Micrometers).ToArray(),
            ["FieldNumber"] = _fieldNumber,
            ["FieldCount"] = fields.Length,
            ["ZemaxCompatible"] = _zemaxCompatible
        }, series.FirstOrDefault(), series, new AnalysisPlotOptions(
            Title: "Huygens MTF",
            XMinimum: 0,
            XMaximum: plottedMaximum,
            YMinimum: 0,
            YMaximum: 1,
            ShowLegend: true,
            GridOpacity: 0.25));
    }
}

internal static class DiffractionAnalysisPresentation
{
    public static AnalysisData CreatePsfData(
        string name,
        string method,
        string title,
        PsfResult psf,
        (double Hx, double Hy) field,
        Wavelength wavelength,
        double strehlRatio)
    {
        var extent = psf.GridSize * psf.SampleSpacingMicrometers;
        var afocalImageSpace = psf.SampleSpacingUnit == AnalysisAxisUnit.Milliradian;
        var imageUnitLabel = afocalImageSpace
            ? ImageSpaceAnalysisSupport.MilliradianLabel
            : "µm";
        var imageQuantity = afocalImageSpace
            ? AnalysisAxisQuantity.IncidentAngle
            : AnalysisAxisQuantity.ImageHeight;
        var points = new List<AnalysisPoint>(psf.GridSize * psf.GridSize);
        for (var row = 0; row < psf.GridSize; row++)
        {
            var y = -extent / 2 + ((row + 0.5) * psf.SampleSpacingMicrometers);
            for (var column = 0; column < psf.GridSize; column++)
            {
                var x = -extent / 2 + ((column + 0.5) * psf.SampleSpacingMicrometers);
                points.Add(new AnalysisPoint(x, y, Value: psf.Values[row, column]));
            }
        }

        var series = new AnalysisSeries(
            $"X ({imageUnitLabel})",
            $"Y ({imageUnitLabel})",
            points,
            AnalysisSeriesKind.Heatmap,
            ValueLabel: "Relative Intensity (%)",
            XQuantity: imageQuantity,
            XUnit: psf.SampleSpacingUnit,
            YQuantity: imageQuantity,
            YUnit: psf.SampleSpacingUnit,
            ValueQuantity: AnalysisAxisQuantity.Irradiance,
            ValueUnit: AnalysisAxisUnit.Percent);
        return new AnalysisData(name, new Dictionary<string, object>
        {
            ["Method"] = method,
            ["PupilSampling"] = psf.PupilSampling,
            ["ImageSize"] = psf.GridSize,
            ["GridSize"] = psf.GridSize,
            ["PixelPitchMicrometers"] = afocalImageSpace ? 0 : psf.SampleSpacingMicrometers,
            ["PixelPitchMilliradians"] = afocalImageSpace ? psf.SampleSpacingMicrometers : 0,
            ["ImageSpaceAfocal"] = afocalImageSpace,
            ["ImageCoordinateUnit"] = imageUnitLabel,
            ["WorkingFNumber"] = psf.WorkingFNumber,
            ["StrehlRatio"] = strehlRatio,
            ["PeakStrehlRatio"] = psf.PeakStrehlRatio,
            ["WavelengthMicrometers"] = wavelength.Micrometers,
            ["FieldHx"] = field.Hx,
            ["FieldHy"] = field.Hy
        }, series, new[] { series }, new AnalysisPlotOptions(
            Title: title,
            EqualAspect: true,
            XMinimum: -extent / 2,
            XMaximum: extent / 2,
            YMinimum: -extent / 2,
            YMaximum: extent / 2));
    }
}

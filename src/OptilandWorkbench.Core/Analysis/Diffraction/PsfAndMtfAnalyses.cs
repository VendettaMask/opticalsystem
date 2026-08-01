using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Analysis;

public sealed class PsfAnalysis : BaseAnalysis
{
    private readonly int _requestedRays;
    private readonly int? _gridSize;
    private readonly int _wavelengthNumber;
    private readonly int _fieldNumber;
    private readonly int _surfaceNumber;
    private readonly double _imageDeltaMicrometers;
    private readonly double _rotationDegrees;
    private readonly string _type;
    private readonly string _displayAs;
    private readonly bool _usePolarization;
    private readonly bool _normalize;
    private readonly bool _zemaxCompatible;
    private readonly bool _ignoreOpd;

    public PsfAnalysis(
        Optic optic,
        int numRays = 128,
        int? gridSize = null,
        int wavelengthNumber = -1,
        int fieldNumber = 0,
        int surfaceNumber = -1,
        double imageDeltaMicrometers = 0,
        double rotationDegrees = 0,
        string type = "线性",
        string displayAs = "伪彩色",
        bool usePolarization = false,
        bool normalize = false,
        bool zemaxCompatible = false,
        bool ignoreOpd = false) : base(optic)
    {
        _requestedRays = Math.Max(2, numRays);
        _gridSize = gridSize;
        _wavelengthNumber = Math.Max(-1, wavelengthNumber);
        _fieldNumber = Math.Max(0, fieldNumber);
        _surfaceNumber = surfaceNumber;
        _imageDeltaMicrometers = Math.Max(0, imageDeltaMicrometers);
        _rotationDegrees = rotationDegrees;
        _type = type;
        _displayAs = displayAs;
        _usePolarization = usePolarization;
        _normalize = normalize;
        _zemaxCompatible = zemaxCompatible;
        _ignoreOpd = ignoreOpd;
    }

    public override string Name => "PSF";

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
        var pupilSampling = _gridSize.HasValue
            ? _requestedRays
            : (int)Math.Floor(32 * Math.Pow(2, (Math.Log2(_requestedRays) - 5) / 2));
        var gridSize = Math.Max(pupilSampling, _gridSize ?? (_requestedRays * 2));
        var results = wavelengths
            .Select(wavelength => (
                Wavelength: wavelength,
                Result: DiffractionEngine.ComputeFftPsf(
                    Optic,
                    field,
                    wavelength,
                    pupilSampling,
                    gridSize,
                    usePolarization: _usePolarization,
                    cellCenteredPupil: _zemaxCompatible,
                    zemaxFftSampling: _zemaxCompatible,
                    ignoreOpd: _ignoreOpd)))
            .ToArray();
        var primary = results.FirstOrDefault(item => item.Wavelength.IsPrimary);
        if (primary.Wavelength is null)
        {
            primary = results[0];
        }

        var sampleSpacing = _imageDeltaMicrometers > 0
            ? _imageDeltaMicrometers
            : _zemaxCompatible
                ? results.Min(item => item.Result.SampleSpacingMicrometers)
                : primary.Result.SampleSpacingMicrometers;
        var values = new double[gridSize, gridSize];
        var useConfiguredWeights = results.Any(item => item.Wavelength.Weight > 0);
        var totalWeight = results.Sum(item =>
            useConfiguredWeights ? item.Wavelength.Weight : 1);

        for (var row = 0; row < gridSize; row++)
        {
            var y = Coordinate(row, gridSize, sampleSpacing);
            for (var column = 0; column < gridSize; column++)
            {
                var x = Coordinate(column, gridSize, sampleSpacing);
                var sum = 0.0;
                foreach (var item in results)
                {
                    var weight = useConfiguredWeights ? item.Wavelength.Weight : 1;
                    sum += weight * BilinearSample(item.Result, x, y) / 100.0;
                }

                values[row, column] = sum / totalWeight;
            }
        }

        var peak = values.Cast<double>().DefaultIfEmpty(0).Max();
        if (_normalize && peak > 0)
        {
            for (var row = 0; row < gridSize; row++)
            {
                for (var column = 0; column < gridSize; column++)
                {
                    values[row, column] /= peak;
                }
            }
        }

        var logarithmic = _type.Contains("对数", StringComparison.Ordinal)
            || _type.Contains("log", StringComparison.OrdinalIgnoreCase);
        var xExtent = gridSize * sampleSpacing;
        var yExtent = gridSize * sampleSpacing;
        var points = new List<AnalysisPoint>(gridSize * gridSize);
        for (var row = 0; row < gridSize; row++)
        {
            var y = Coordinate(row, gridSize, sampleSpacing);
            for (var column = 0; column < gridSize; column++)
            {
                var value = values[row, column];
                points.Add(new AnalysisPoint(
                    Coordinate(column, gridSize, sampleSpacing),
                    y,
                    Value: logarithmic ? 10 * Math.Log10(Math.Max(1e-12, value)) : value));
            }
        }

        var series = new AnalysisSeries(
            "X (\u00B5m)",
            "Y (\u00B5m)",
            points,
            AnalysisSeriesKind.Heatmap,
            ValueLabel: logarithmic ? "Relative Intensity (dB)" : "Relative Intensity",
            XQuantity: AnalysisAxisQuantity.ImageHeight,
            XUnit: AnalysisAxisUnit.Micrometer,
            YQuantity: AnalysisAxisQuantity.ImageHeight,
            YUnit: AnalysisAxisUnit.Micrometer,
            ValueQuantity: AnalysisAxisQuantity.Irradiance,
            ValueUnit: logarithmic ? AnalysisAxisUnit.Decibel : AnalysisAxisUnit.Dimensionless);
        var centerValue = values[gridSize / 2, gridSize / 2];
        var title = wavelengths.Length > 1 ? "复色光FFT PSF" : "FFT PSF";
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["Method"] = "FFT",
            ["PupilSampling"] = pupilSampling,
            ["GridSize"] = gridSize,
            ["ImageDeltaMicrometers"] = sampleSpacing,
            ["WorkingFNumber"] = results.Average(item => item.Result.WorkingFNumber),
            ["StrehlRatio"] = centerValue,
            ["PeakStrehlRatio"] = values.Cast<double>().DefaultIfEmpty(0).Max(),
            ["WavelengthNumber"] = _wavelengthNumber,
            ["WavelengthRange"] = $"{wavelengths.Min(item => item.Micrometers):0.0000}–{wavelengths.Max(item => item.Micrometers):0.0000} µm",
            ["FieldNumber"] = _fieldNumber,
            ["FieldHx"] = field.Hx,
            ["FieldHy"] = field.Hy,
            ["SurfaceNumber"] = _surfaceNumber,
            ["RotationDegrees"] = _rotationDegrees,
            ["Type"] = _type,
            ["DisplayAs"] = _displayAs,
            ["UsePolarization"] = _usePolarization,
            ["Normalized"] = _normalize
        }, series, new[] { series }, new AnalysisPlotOptions(
            Title: title,
            EqualAspect: true,
            XMinimum: -xExtent / 2,
            XMaximum: xExtent / 2,
            YMinimum: -yExtent / 2,
            YMaximum: yExtent / 2));
    }

    private static double Coordinate(int index, int size, double spacing)
    {
        return ((index + 0.5) - (size / 2.0)) * spacing;
    }

    private static double BilinearSample(PsfResult source, double x, double y)
    {
        var column = (x / source.SampleSpacingMicrometers) + (source.GridSize / 2.0) - 0.5;
        var row = (y / source.SampleSpacingMicrometers) + (source.GridSize / 2.0) - 0.5;
        var left = (int)Math.Floor(column);
        var top = (int)Math.Floor(row);
        if (left < 0 || top < 0 || left + 1 >= source.GridSize || top + 1 >= source.GridSize)
        {
            return 0;
        }

        var tx = column - left;
        var ty = row - top;
        var topValue = source.Values[top, left] * (1 - tx)
            + source.Values[top, left + 1] * tx;
        var bottomValue = source.Values[top + 1, left] * (1 - tx)
            + source.Values[top + 1, left + 1] * tx;
        return topValue * (1 - ty) + bottomValue * ty;
    }
}

public sealed class MtfAnalysis : BaseAnalysis
{
    private readonly int _requestedRays;
    private readonly int? _gridSize;
    private readonly double? _maximumFrequency;
    private readonly int _wavelengthNumber;
    private readonly int _fieldNumber;
    private readonly int _surfaceNumber;
    private readonly FftMtfDataType _dataType;
    private readonly bool _showDiffractionLimit;
    private readonly bool _usePolarization;
    private readonly bool _useDashes;
    private readonly bool _zemaxCompatible;

    public MtfAnalysis(
        Optic optic,
        int numRays = 128,
        int? gridSize = null,
        double? maximumFrequency = null,
        int wavelengthNumber = -1,
        int fieldNumber = 0,
        int surfaceNumber = 0,
        string type = "Modulation",
        bool showDiffractionLimit = false,
        bool usePolarization = false,
        bool useDashes = false,
        bool zemaxCompatible = false) : base(optic)
    {
        _requestedRays = Math.Max(2, numRays);
        _gridSize = gridSize;
        _maximumFrequency = maximumFrequency is > 0 && double.IsFinite(maximumFrequency.Value)
            ? maximumFrequency
            : null;
        _wavelengthNumber = wavelengthNumber;
        _fieldNumber = Math.Max(0, fieldNumber);
        _surfaceNumber = Math.Max(0, surfaceNumber);
        _dataType = ParseDataType(type);
        _showDiffractionLimit = showDiffractionLimit;
        _usePolarization = usePolarization;
        _useDashes = useDashes;
        _zemaxCompatible = zemaxCompatible;
    }

    public override string Name => "MTF";

    public override AnalysisData GenerateData()
    {
        var analysisOptic = ResolveAnalysisOptic();
        var wavelengths = MtfMethodEvaluator.SelectWavelengths(analysisOptic, _wavelengthNumber);
        if (wavelengths.Count == 0)
        {
            return new AnalysisData(Name, new Dictionary<string, object> { ["Status"] = "No wavelengths" });
        }

        var pupilSampling = _gridSize.HasValue
            ? _requestedRays
            : (int)Math.Floor(32 * Math.Pow(2, (Math.Log2(_requestedRays) - 5) / 2));
        var gridSize = _gridSize ?? (_requestedRays * 2);
        var allFields = SpotAnalysisEngine.DefinedFields(analysisOptic);
        var fieldIndices = _fieldNumber <= 0
            ? Enumerable.Range(0, allFields.Count).ToArray()
            : new[] { Math.Clamp(_fieldNumber - 1, 0, Math.Max(0, allFields.Count - 1)) };
        var fields = fieldIndices.Select(index => allFields[index]).ToArray();
        var series = new List<AnalysisSeries>();
        var cutoff = 0.0;
        IReadOnlyList<AnalysisPoint>? diffractionLimit = null;
        var yAxisLabel = DataTypeLabel(_dataType);
        for (var fieldIndex = 0; fieldIndex < fields.Length; fieldIndex++)
        {
            var field = fields[fieldIndex];
            var wavelengthResults = wavelengths.Select(wavelength =>
            {
                var psf = DiffractionEngine.ComputeFftPsf(
                    analysisOptic,
                    field,
                    wavelength,
                    pupilSampling,
                    gridSize,
                    _usePolarization,
                    cellCenteredPupil: _zemaxCompatible);
                return (wavelength, DiffractionEngine.ComputeFftMtf(psf, analysisOptic, wavelength));
            }).ToArray();
            var fullMtf = MtfMethodEvaluator.CombinePolychromatic(wavelengthResults);
            cutoff = cutoff <= 0
                ? fullMtf.CutoffFrequency
                : Math.Min(cutoff, fullMtf.CutoffFrequency);
            var fieldPlottedMaximum = _maximumFrequency ?? fullMtf.CutoffFrequency;
            var mtf = _zemaxCompatible
                ? Resample(fullMtf, fieldPlottedMaximum, _dataType, 300)
                : DiffractionEngine.LimitFrequency(fullMtf, _maximumFrequency);
            if (_showDiffractionLimit && diffractionLimit is null)
            {
                diffractionLimit = mtf.Frequency.Select(frequency => new AnalysisPoint(
                    frequency,
                    DiffractionLimit(wavelengthResults, frequency))).ToArray();
            }

            series.Add(new AnalysisSeries(
                "Frequency (cycles/mm)",
                yAxisLabel,
                mtf.Frequency.Select((frequency, index) => new AnalysisPoint(frequency, mtf.Tangential[index])).ToArray(),
                Name: MtfPresentation.SeriesName(Optic, field, "Tangential"),
                ColorIndex: _useDashes ? 0 : fieldIndices[fieldIndex],
                XQuantity: AnalysisAxisQuantity.SpatialFrequency,
                XUnit: AnalysisAxisUnit.CyclesPerMillimeter,
                YQuantity: AnalysisAxisQuantity.Modulation,
                YUnit: _dataType == FftMtfDataType.Phase ? AnalysisAxisUnit.Radian : AnalysisAxisUnit.Dimensionless));
            series.Add(new AnalysisSeries(
                "Frequency (cycles/mm)",
                yAxisLabel,
                mtf.Frequency.Select((frequency, index) => new AnalysisPoint(frequency, mtf.Sagittal[index])).ToArray(),
                Name: MtfPresentation.SeriesName(Optic, field, "Sagittal"),
                LineStyle: AnalysisLineStyle.Dashed,
                ColorIndex: _useDashes ? 0 : fieldIndices[fieldIndex],
                XQuantity: AnalysisAxisQuantity.SpatialFrequency,
                XUnit: AnalysisAxisUnit.CyclesPerMillimeter,
                YQuantity: AnalysisAxisQuantity.Modulation,
                YUnit: _dataType == FftMtfDataType.Phase ? AnalysisAxisUnit.Radian : AnalysisAxisUnit.Dimensionless));
        }

        if (diffractionLimit is not null)
        {
            series.Add(new AnalysisSeries(
                "Frequency (cycles/mm)",
                "Modulation",
                diffractionLimit,
                Name: "Diffraction Limit",
                ColorIndex: fields.Length,
                XQuantity: AnalysisAxisQuantity.SpatialFrequency,
                XUnit: AnalysisAxisUnit.CyclesPerMillimeter,
                YQuantity: AnalysisAxisQuantity.Modulation,
                YUnit: AnalysisAxisUnit.Dimensionless));
        }

        var plottedMaximum = _zemaxCompatible
            ? _maximumFrequency ?? cutoff
            : _maximumFrequency.HasValue
                ? Math.Min(_maximumFrequency.Value, cutoff)
                : cutoff;
        var values = new Dictionary<string, object>
        {
            ["Method"] = "FFT",
            ["PupilSampling"] = pupilSampling,
            ["GridSize"] = gridSize,
            ["MaximumFrequency"] = plottedMaximum,
            ["CutoffFrequency"] = cutoff,
            ["WavelengthNumber"] = _wavelengthNumber,
            ["WavelengthsMicrometers"] = wavelengths.Select(item => item.Micrometers).ToArray(),
            ["FieldNumber"] = _fieldNumber,
            ["FieldCount"] = fields.Length,
            ["SurfaceNumber"] = _surfaceNumber,
            ["Type"] = DataTypeName(_dataType),
            ["ShowDiffractionLimit"] = _showDiffractionLimit,
            ["UsePolarization"] = _usePolarization,
            ["UseDashes"] = _useDashes,
            ["ZemaxCompatible"] = _zemaxCompatible,
            ["PlotPointCount"] = series.FirstOrDefault()?.Points.Count ?? 0
        };
        var (yMinimum, yMaximum) = _dataType switch
        {
            FftMtfDataType.Real or FftMtfDataType.Imaginary => (-1.0, 1.0),
            FftMtfDataType.Phase => (-Math.PI, Math.PI),
            _ => (0.0, _zemaxCompatible ? 1.05 : 1.0)
        };
        return new AnalysisData(Name, values, series.FirstOrDefault(), series, new AnalysisPlotOptions(
            XMinimum: 0,
            XMaximum: plottedMaximum,
            YMinimum: yMinimum,
            YMaximum: yMaximum,
            ShowLegend: true,
            GridOpacity: 0.25));
    }

    private Optic ResolveAnalysisOptic()
    {
        if (_surfaceNumber <= 0)
        {
            return Optic;
        }

        var surfaceIndex = Optic.SurfaceGroup.Items.ToList()
            .FindIndex(surface => surface.Number == _surfaceNumber);
        if (surfaceIndex < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(_surfaceNumber),
                $"FFT MTF surface {_surfaceNumber} does not exist.");
        }

        if (surfaceIndex == Optic.SurfaceGroup.Items.Count - 1)
        {
            return Optic;
        }

        var clone = Optic.FromSnapshot(Optic.ToSnapshot());
        var surfaces = clone.SurfaceGroup.Items
            .Take(surfaceIndex + 1)
            .Select(surface => surface.Clone())
            .ToArray();
        clone.SurfaceGroup.Replace(surfaces, syncComposition: false);
        return clone;
    }

    private static MtfResult Resample(
        MtfResult source,
        double maximumFrequency,
        FftMtfDataType type,
        int pointCount)
    {
        pointCount = Math.Max(2, pointCount);
        maximumFrequency = Math.Max(0, maximumFrequency);
        var frequency = Enumerable.Range(0, pointCount)
            .Select(index => maximumFrequency * index / (pointCount - 1.0))
            .ToArray();
        var tangential = frequency.Select(value => Sample(source, value, type, tangential: true)).ToArray();
        var sagittal = frequency.Select(value => Sample(source, value, type, tangential: false)).ToArray();
        return new MtfResult(frequency, tangential, sagittal, source.CutoffFrequency);
    }

    private static double Sample(
        MtfResult source,
        double frequency,
        FftMtfDataType type,
        bool tangential)
    {
        var complex = tangential ? source.TangentialOtf : source.SagittalOtf;
        var scalar = tangential ? source.Tangential : source.Sagittal;
        var sourceFrequency = tangential
            ? source.TangentialFrequency ?? source.Frequency
            : source.SagittalFrequency ?? source.Frequency;
        if (type == FftMtfDataType.SquareWave)
        {
            if (frequency <= 1e-12)
            {
                return 1;
            }

            var sum = 0.0;
            var sign = 1.0;
            for (var harmonic = 1; harmonic <= 999; harmonic += 2)
            {
                var harmonicFrequency = harmonic * frequency;
                if (sourceFrequency.Count == 0 || harmonicFrequency > sourceFrequency[^1])
                {
                    break;
                }

                var modulation = InterpolateUnbounded(sourceFrequency, scalar, harmonicFrequency);
                sum += sign * modulation / harmonic;
                sign *= -1;
            }

            return Math.Max(0, 4 * sum / Math.PI);
        }

        if (complex is null || type == FftMtfDataType.Modulation)
        {
            return InterpolateUnbounded(sourceFrequency, scalar, frequency);
        }

        var value = MtfMethodEvaluator.InterpolateComplex(sourceFrequency, complex, frequency);
        return type switch
        {
            FftMtfDataType.Real => value.Real,
            FftMtfDataType.Imaginary => value.Imaginary,
            FftMtfDataType.Phase => value.Phase,
            _ => value.Magnitude
        };
    }

    private static double InterpolateUnbounded(
        IReadOnlyList<double> x,
        IReadOnlyList<double> y,
        double target)
    {
        if (x.Count == 0 || y.Count == 0 || target > x[^1])
        {
            return 0;
        }

        if (target <= x[0])
        {
            return y[0];
        }

        for (var index = 1; index < x.Count; index++)
        {
            if (target > x[index])
            {
                continue;
            }

            var width = x[index] - x[index - 1];
            var fraction = width <= 1e-30 ? 0 : (target - x[index - 1]) / width;
            return y[index - 1] + ((y[index] - y[index - 1]) * fraction);
        }

        return 0;
    }

    private static double DiffractionLimit(
        IReadOnlyList<(Wavelength Wavelength, MtfResult Result)> results,
        double frequency)
    {
        var totalWeight = results.Sum(item => item.Wavelength.Weight);
        var equalWeights = totalWeight <= 1e-30;
        if (equalWeights)
        {
            totalWeight = Math.Max(1, results.Count);
        }

        var value = 0.0;
        foreach (var item in results)
        {
            var normalized = item.Result.CutoffFrequency <= 1e-30
                ? 1
                : frequency / item.Result.CutoffFrequency;
            var monochromatic = normalized >= 1
                ? 0
                : 2 * (Math.Acos(normalized)
                    - (normalized * Math.Sqrt(Math.Max(0, 1 - (normalized * normalized))))) / Math.PI;
            value += monochromatic * (equalWeights ? 1 : item.Wavelength.Weight);
        }

        return value / totalWeight;
    }

    private static FftMtfDataType ParseDataType(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "real" or "实部" => FftMtfDataType.Real,
            "imaginary" or "虚部" => FftMtfDataType.Imaginary,
            "phase" or "相位" => FftMtfDataType.Phase,
            "squarewave" or "square wave" or "方波" => FftMtfDataType.SquareWave,
            _ => FftMtfDataType.Modulation
        };
    }

    private static string DataTypeName(FftMtfDataType type)
    {
        return type switch
        {
            FftMtfDataType.Real => "Real",
            FftMtfDataType.Imaginary => "Imaginary",
            FftMtfDataType.Phase => "Phase",
            FftMtfDataType.SquareWave => "SquareWave",
            _ => "Modulation"
        };
    }

    private static string DataTypeLabel(FftMtfDataType type)
    {
        return type switch
        {
            FftMtfDataType.Real => "Real MTF",
            FftMtfDataType.Imaginary => "Imaginary MTF",
            FftMtfDataType.Phase => "Phase (radians)",
            FftMtfDataType.SquareWave => "Square Wave MTF",
            _ => "Modulation"
        };
    }
}

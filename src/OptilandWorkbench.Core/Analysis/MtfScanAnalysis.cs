using System.Numerics;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Core.Analysis;

public enum MtfComputationMethod
{
    Fourier,
    Huygens,
    Geometric
}

public sealed record MtfComputationSettings(
    int PupilSampling = 64,
    int ImageSize = 64,
    double PixelPitchMillimeters = 0.005,
    int GeometricRayCount = 64,
    string Distribution = "uniform",
    bool ScaleGeometricByDiffractionLimit = true,
    bool UsePolarization = false,
    bool ZemaxCompatible = false,
    bool UseZemaxHuygensSemantics = false);

public sealed class MtfThroughFocusAnalysis : BaseAnalysis
{
    private readonly MtfComputationMethod _method;
    private readonly double _frequencyInput;
    private readonly double _spatialFrequency;
    private readonly double _deltaFocus;
    private readonly int _focusPlaneCount;
    private readonly MtfComputationSettings _settings;
    private readonly int _wavelengthNumber;
    private readonly int _fieldNumber;
    private readonly FftMtfDataType _dataType;
    private readonly bool _useDashes;

    public MtfThroughFocusAnalysis(
        Optic optic,
        MtfComputationMethod method,
        double spatialFrequency = 50,
        double deltaFocus = 0.1,
        int focusPlaneCount = 5,
        MtfComputationSettings? settings = null,
        int wavelengthNumber = 0,
        int fieldNumber = 0,
        string type = "Modulation",
        bool useDashes = false) : base(optic)
    {
        _method = method;
        _settings = settings ?? new MtfComputationSettings();
        _frequencyInput = Math.Max(0, spatialFrequency);
        _spatialFrequency = _settings.ZemaxCompatible && _frequencyInput <= 0
            ? 50
            : _frequencyInput;
        _deltaFocus = Math.Abs(deltaFocus);
        _focusPlaneCount = Math.Clamp(focusPlaneCount, 1, 101);
        _wavelengthNumber = Math.Max(0, wavelengthNumber);
        _fieldNumber = Math.Max(0, fieldNumber);
        _dataType = MtfDataTypeSupport.Parse(type);
        _useDashes = useDashes;
    }

    public override string Name => $"{MtfMethodEvaluator.MethodName(_method)} Through Focus MTF";

    public override AnalysisData GenerateData()
    {
        var wavelengths = MtfMethodEvaluator.SelectWavelengths(Optic, _wavelengthNumber);
        var imageSurface = Optic.SurfaceGroup.Items.LastOrDefault();
        if (wavelengths.Count == 0 || imageSurface is null)
        {
            return AnalysisData.Unavailable(Name, "No optical data");
        }

        var allFields = SpotAnalysisEngine.DefinedFields(Optic);
        var fieldIndices = _fieldNumber <= 0
            ? Enumerable.Range(0, allFields.Count).ToArray()
            : new[] { Math.Clamp(_fieldNumber - 1, 0, Math.Max(0, allFields.Count - 1)) };
        var fields = fieldIndices.Select(index => allFields[index]).ToArray();
        var focus = _focusPlaneCount == 1
            ? new[] { 0.0 }
            : Enumerable.Range(0, _focusPlaneCount)
                .Select(index => -_deltaFocus + ((2 * _deltaFocus * index) / (_focusPlaneCount - 1.0)))
                .ToArray();
        var tangential = fields.Select(_ => new double[focus.Length]).ToArray();
        var sagittal = fields.Select(_ => new double[focus.Length]).ToArray();
        var originalCoordinateSystem = imageSurface.CoordinateSystem;
        if (_method == MtfComputationMethod.Fourier && _settings.ZemaxCompatible)
        {
            for (var fieldIndex = 0; fieldIndex < fields.Length; fieldIndex++)
            {
                var values = MtfMethodEvaluator.EvaluateFourierThroughFocus(
                    Optic,
                    fields[fieldIndex],
                    wavelengths,
                    focus,
                    _spatialFrequency,
                    _settings,
                    _dataType);
                tangential[fieldIndex] = values.Tangential;
                sagittal[fieldIndex] = values.Sagittal;
            }
        }
        else if (Optic.ImageSpaceAfocal)
        {
            for (var focusIndex = 0; focusIndex < focus.Length; focusIndex++)
            {
                for (var fieldIndex = 0; fieldIndex < fields.Length; fieldIndex++)
                {
                    var value = MtfMethodEvaluator.EvaluatePolychromatic(
                        Optic,
                        _method,
                        fields[fieldIndex],
                        wavelengths,
                        _spatialFrequency,
                        _settings,
                        _dataType,
                        defocusMillimeters: focus[focusIndex]);
                    tangential[fieldIndex][focusIndex] = value.Tangential;
                    sagittal[fieldIndex][focusIndex] = value.Sagittal;
                }
            }
        }
        else
        {
            var previous = Optic.SurfaceGroup.Items[^2];
            var originalThickness = previous.Thickness;
            // Defocus is a displacement along the preceding surface's local axis.
            // Keep the prescription gap and the traced image coordinate consistent.
            Optic.InvalidateRayTraceCache();
            try
            {
                for (var focusIndex = 0; focusIndex < focus.Length; focusIndex++)
                {
                    var shift = previous.CoordinateSystem.ToGlobalDirection(new Vector3D(0, 0, focus[focusIndex]));
                    previous.Thickness = originalThickness + focus[focusIndex];
                    imageSurface.CoordinateSystem = originalCoordinateSystem with
                    {
                        Origin = originalCoordinateSystem.Origin + shift
                    };
                    for (var fieldIndex = 0; fieldIndex < fields.Length; fieldIndex++)
                    {
                        var value = MtfMethodEvaluator.EvaluatePolychromatic(
                            Optic,
                            _method,
                            fields[fieldIndex],
                            wavelengths,
                            _spatialFrequency,
                            _settings,
                            _dataType);
                        tangential[fieldIndex][focusIndex] = value.Tangential;
                        sagittal[fieldIndex][focusIndex] = value.Sagittal;
                    }
                }
            }
            finally
            {
                previous.Thickness = originalThickness;
                imageSurface.CoordinateSystem = originalCoordinateSystem;
            }
        }

        var displayPointCount = _settings.ZemaxCompatible
            ? _method == MtfComputationMethod.Huygens ? 101 : 300
            : 101;
        var displayFocus = focus.Length < 2
            ? focus
            : Enumerable.Range(0, displayPointCount)
                .Select(index => -_deltaFocus
                    + ((2 * _deltaFocus * index)
                        / (displayPointCount - 1.0)))
                .ToArray();
        var series = new List<AnalysisSeries>(fields.Length * 2);
        var defocusLabel = ImageSpaceAnalysisSupport.DefocusLabel(Optic);
        var defocusUnit = ImageSpaceAnalysisSupport.DefocusUnit(Optic);
        var frequencyUnitLabel = ImageSpaceAnalysisSupport.SpatialFrequencyUnitLabel(Optic);
        for (var fieldIndex = 0; fieldIndex < fields.Length; fieldIndex++)
        {
            var field = fields[fieldIndex];
            var tangentialDisplay = _settings.ZemaxCompatible
                ? CubicSplineInterpolate(focus, tangential[fieldIndex], displayFocus)
                : ThroughFocusMtfAnalysis.Interpolate(focus, tangential[fieldIndex], displayFocus);
            var sagittalDisplay = _settings.ZemaxCompatible
                ? CubicSplineInterpolate(focus, sagittal[fieldIndex], displayFocus)
                : ThroughFocusMtfAnalysis.Interpolate(focus, sagittal[fieldIndex], displayFocus);
            var colorIndex = fieldIndices[fieldIndex];
            series.Add(new AnalysisSeries(
                defocusLabel,
                MtfDataTypeSupport.Label(_dataType, "MTF"),
                displayFocus.Select((value, index) => new AnalysisPoint(
                    value,
                DisplayValue(tangentialDisplay[index]))).ToArray(),
                Name: MtfPresentation.SeriesName(Optic, field, "Tangential"),
                ColorIndex: _useDashes ? 0 : colorIndex,
                XQuantity: AnalysisAxisQuantity.Defocus,
                XUnit: defocusUnit,
                YQuantity: AnalysisAxisQuantity.Modulation,
                YUnit: _dataType == FftMtfDataType.Phase ? AnalysisAxisUnit.Radian : AnalysisAxisUnit.Dimensionless));
            series.Add(new AnalysisSeries(
                defocusLabel,
                MtfDataTypeSupport.Label(_dataType, "MTF"),
                displayFocus.Select((value, index) => new AnalysisPoint(
                    value,
                    DisplayValue(sagittalDisplay[index]))).ToArray(),
                Name: MtfPresentation.SeriesName(Optic, field, "Sagittal"),
                LineStyle: AnalysisLineStyle.Dashed,
                ColorIndex: _useDashes ? 0 : colorIndex,
                XQuantity: AnalysisAxisQuantity.Defocus,
                XUnit: defocusUnit,
                YQuantity: AnalysisAxisQuantity.Modulation,
                YUnit: _dataType == FftMtfDataType.Phase ? AnalysisAxisUnit.Radian : AnalysisAxisUnit.Dimensionless));
        }

        var wavelengthLabel = _wavelengthNumber <= 0
            ? "Polychromatic"
            : $"\u03BB={wavelengths[0].Micrometers:0.000} \u00B5m";
        var (yMinimum, yMaximum) = _dataType switch
        {
            FftMtfDataType.Real or FftMtfDataType.Imaginary => (-1.0, 1.0),
            FftMtfDataType.Phase => (-Math.PI, Math.PI),
            _ => (0.0, 1.05)
        };
        var resolvedImageDelta = _method == MtfComputationMethod.Huygens
            ? fields.Select(field => MtfMethodEvaluator.ResolveHuygensImageDeltaMillimeters(
                Optic,
                field,
                wavelengths,
                _settings)).ToArray()
            : Array.Empty<double>();
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["Method"] = MtfMethodEvaluator.MethodName(_method),
            ["FrequencyInput"] = _frequencyInput,
            ["SpatialFrequency"] = _spatialFrequency,
            ["DeltaFocus"] = _deltaFocus,
            ["DefocusUnit"] = ImageSpaceAnalysisSupport.DefocusUnitLabel(Optic),
            ["FrequencyUnit"] = frequencyUnitLabel,
            ["ImageSpaceAfocal"] = Optic.ImageSpaceAfocal,
            ["Steps"] = _focusPlaneCount,
            ["NumberOfSteps"] = _focusPlaneCount,
            ["WavelengthNumber"] = _wavelengthNumber,
            ["FieldNumber"] = _fieldNumber,
            ["Type"] = MtfDataTypeSupport.Name(_dataType),
            ["UsePolarization"] = _settings.UsePolarization,
            ["UseDashes"] = _useDashes,
            ["ZemaxCompatible"] = _settings.ZemaxCompatible,
            ["PupilSampling"] = _settings.PupilSampling,
            ["ImageSampling"] = _settings.ImageSize,
            ["HuygensMtfTransformSize"] = _method == MtfComputationMethod.Huygens ? Math.Max(4, _settings.ImageSize) : 0,
            ["HuygensFrequencySampling"] = _method != MtfComputationMethod.Huygens ? "NotApplicable"
                : _settings.UseZemaxHuygensSemantics ? "NaturalCubicEndpointSpan" : "LinearDftPeriod",
            ["ImageDeltaMicrometers"] = Optic.ImageSpaceAfocal
                ? 0
                : _settings.PixelPitchMillimeters * 1000,
            ["ImageDeltaMilliradians"] = Optic.ImageSpaceAfocal
                ? _settings.PixelPitchMillimeters
                : 0,
            ["ResolvedImageDeltaMicrometers"] = Optic.ImageSpaceAfocal
                ? Array.Empty<double>()
                : resolvedImageDelta.Select(value => value * 1000).ToArray(),
            ["ResolvedImageDeltaMilliradians"] = Optic.ImageSpaceAfocal
                ? resolvedImageDelta
                : Array.Empty<double>(),
            ["WavelengthsMicrometers"] = wavelengths.Select(item => item.Micrometers).ToArray(),
            ["RawTangential"] = tangential,
            ["RawSagittal"] = sagittal
        }, series.FirstOrDefault(), series, new AnalysisPlotOptions(
            Title: $"{MtfMethodEvaluator.MethodName(_method)} Through-Focus MTF at {_spatialFrequency:0.###} {frequencyUnitLabel}, {wavelengthLabel}",
            XMinimum: -_deltaFocus,
            XMaximum: _deltaFocus,
            YMinimum: yMinimum,
            YMaximum: yMaximum,
            ShowLegend: true,
            DottedGrid: true,
            GridOpacity: 0.35));
    }

    private double DisplayValue(double value)
    {
        return _dataType is FftMtfDataType.Modulation or FftMtfDataType.SquareWave
            ? Math.Clamp(value, 0, 1)
            : value;
    }

    internal static double[] CubicSplineInterpolate(
        IReadOnlyList<double> x,
        IReadOnlyList<double> y,
        IReadOnlyList<double> targets)
    {
        if (x.Count != y.Count || x.Count == 0)
        {
            return targets.Select(_ => 0.0).ToArray();
        }

        if (x.Count == 1)
        {
            return targets.Select(_ => y[0]).ToArray();
        }

        var secondDerivatives = new double[x.Count];
        var work = new double[x.Count - 1];
        secondDerivatives[0] = 0;
        work[0] = 0;
        for (var index = 1; index < x.Count - 1; index++)
        {
            var span = x[index + 1] - x[index - 1];
            var sigma = span <= 1e-30 ? 0.5 : (x[index] - x[index - 1]) / span;
            var pivot = (sigma * secondDerivatives[index - 1]) + 2;
            secondDerivatives[index] = (sigma - 1) / pivot;
            var leftWidth = x[index] - x[index - 1];
            var rightWidth = x[index + 1] - x[index];
            var slopeChange = leftWidth <= 1e-30 || rightWidth <= 1e-30
                ? 0
                : ((y[index + 1] - y[index]) / rightWidth)
                    - ((y[index] - y[index - 1]) / leftWidth);
            work[index] = ((6 * slopeChange / Math.Max(span, 1e-30))
                - (sigma * work[index - 1])) / pivot;
        }

        secondDerivatives[^1] = 0;
        for (var index = x.Count - 2; index >= 0; index--)
        {
            secondDerivatives[index] = (secondDerivatives[index] * secondDerivatives[index + 1])
                + work[index];
        }

        return targets.Select(target =>
        {
            var upper = 1;
            while (upper < x.Count - 1 && target > x[upper])
            {
                upper++;
            }

            var lower = upper - 1;
            var width = x[upper] - x[lower];
            if (width <= 1e-30)
            {
                return y[lower];
            }

            var a = (x[upper] - target) / width;
            var b = (target - x[lower]) / width;
            return (a * y[lower])
                + (b * y[upper])
                + ((((a * a * a) - a) * secondDerivatives[lower]
                    + (((b * b * b) - b) * secondDerivatives[upper]))
                    * width * width / 6);
        }).ToArray();
    }
}

public sealed class MtfVsFieldAnalysis : BaseAnalysis
{
    private readonly MtfComputationMethod _method;
    private readonly double[] _spatialFrequencies;
    private readonly int _fieldPointCount;
    private readonly MtfComputationSettings _settings;
    private readonly int _wavelengthNumber;
    private readonly string _scanType;
    private readonly bool _removeVignettingFactors;
    private readonly bool _zemaxCompatibleOutput;
    private readonly bool _useDashes;

    public MtfVsFieldAnalysis(
        Optic optic,
        MtfComputationMethod method,
        double spatialFrequency = 20,
        int fieldPointCount = 21,
        MtfComputationSettings? settings = null,
        int wavelengthNumber = 0,
        IReadOnlyList<double>? spatialFrequencies = null,
        string scanType = "+y",
        bool removeVignettingFactors = false,
        bool zemaxCompatibleOutput = false,
        bool useDashes = false) : base(optic)
    {
        _method = method;
        _spatialFrequencies = (spatialFrequencies ?? new[] { spatialFrequency })
            .Where(double.IsFinite)
            .Select(value => Math.Max(0, value))
            .Distinct()
            .ToArray();
        if (_spatialFrequencies.Length == 0)
        {
            _spatialFrequencies = new[] { 0.0 };
        }
        _fieldPointCount = Math.Clamp(fieldPointCount, 2, 101);
        _settings = settings ?? new MtfComputationSettings();
        _wavelengthNumber = Math.Max(0, wavelengthNumber);
        _scanType = scanType;
        _removeVignettingFactors = removeVignettingFactors;
        _zemaxCompatibleOutput = zemaxCompatibleOutput;
        _useDashes = useDashes;
    }

    public override string Name => $"{MtfMethodEvaluator.MethodName(_method)} MTF vs Field";

    public override AnalysisData GenerateData()
    {
        var workingOptic = AnalysisTrace.PrepareVignettingFactors(Optic, _removeVignettingFactors);
        var wavelengths = MtfMethodEvaluator.SelectWavelengths(workingOptic, _wavelengthNumber);
        if (wavelengths.Count == 0)
        {
            return AnalysisData.Unavailable(Name, "No wavelengths");
        }

        var fields = _zemaxCompatibleOutput
            ? AnalysisTrace.ScanFieldSamples(workingOptic, _scanType, _fieldPointCount + 1)
            : AnalysisTrace.DefinedFieldSamples(workingOptic).OrderBy(field => field.Coordinate).ToArray();
        var calculationCoordinates = _zemaxCompatibleOutput
            ? Enumerable.Range(0, fields.Count)
                .Select(index => index / (fields.Count - 1.0))
                .ToArray()
            : fields.Select(field => field.Coordinate).ToArray();
        var tangential = _spatialFrequencies.Select(_ => new double[fields.Count]).ToArray();
        var sagittal = _spatialFrequencies.Select(_ => new double[fields.Count]).ToArray();
        for (var index = 0; index < fields.Count; index++)
        {
            var field = fields[index];
            var values = MtfMethodEvaluator.EvaluatePolychromaticFrequencies(
                workingOptic,
                _method,
                (field.Hx, field.Hy),
                wavelengths,
                _spatialFrequencies,
                _settings);
            for (var frequencyIndex = 0; frequencyIndex < _spatialFrequencies.Length; frequencyIndex++)
            {
                tangential[frequencyIndex][index] = values[frequencyIndex].Tangential;
                sagittal[frequencyIndex][index] = values[frequencyIndex].Sagittal;
            }
        }

        var (axisLabel, fieldUnit) = _zemaxCompatibleOutput
            ? ("Relative Field", string.Empty)
            : Optic.FieldDefinition switch
            {
                FieldDefinitionKind.Angle => ("Field angle (deg)", "deg"),
                FieldDefinitionKind.ObjectHeight => ("Object height (mm)", "mm"),
                FieldDefinitionKind.ParaxialImageHeight => ("Paraxial image height (mm)", "mm"),
                FieldDefinitionKind.RealImageHeight => ("Real image height (mm)", "mm"),
                _ => ("Field", string.Empty)
            };
        var plotCoordinates = _zemaxCompatibleOutput
            ? Enumerable.Range(0, 300).Select(index => index / 299.0).ToArray()
            : calculationCoordinates;
        var frequencyUnitLabel = ImageSpaceAnalysisSupport.SpatialFrequencyUnitLabel(workingOptic);
        var series = new List<AnalysisSeries>(_spatialFrequencies.Length * 2);
        for (var frequencyIndex = 0; frequencyIndex < _spatialFrequencies.Length; frequencyIndex++)
        {
            var frequency = _spatialFrequencies[frequencyIndex];
            var tangentialDisplay = _zemaxCompatibleOutput
                ? MtfThroughFocusAnalysis.CubicSplineInterpolate(
                    calculationCoordinates,
                    tangential[frequencyIndex],
                    plotCoordinates)
                : tangential[frequencyIndex];
            var sagittalDisplay = _zemaxCompatibleOutput
                ? MtfThroughFocusAnalysis.CubicSplineInterpolate(
                    calculationCoordinates,
                    sagittal[frequencyIndex],
                    plotCoordinates)
                : sagittal[frequencyIndex];
            series.Add(new AnalysisSeries(
                axisLabel,
                "MTF",
                plotCoordinates.Select((value, index) => new AnalysisPoint(
                    value,
                    tangentialDisplay[index],
                    Label: _zemaxCompatibleOutput ? string.Empty : fields[index].Label)).ToArray(),
                Name: $"{frequency:0.###} {frequencyUnitLabel}, Tangential",
                LineStyle: _useDashes && frequencyIndex % 2 == 1
                    ? AnalysisLineStyle.Dashed
                    : AnalysisLineStyle.Solid,
                ColorIndex: frequencyIndex,
                XQuantity: _zemaxCompatibleOutput ? AnalysisAxisQuantity.NormalizedField : AnalysisTrace.FieldAxisQuantity(Optic),
                XUnit: _zemaxCompatibleOutput ? AnalysisAxisUnit.Dimensionless : AnalysisTrace.FieldAxisUnit(Optic),
                YQuantity: AnalysisAxisQuantity.Modulation,
                YUnit: AnalysisAxisUnit.Dimensionless));
            series.Add(new AnalysisSeries(
                axisLabel,
                "MTF",
                plotCoordinates.Select((value, index) => new AnalysisPoint(
                    value,
                    sagittalDisplay[index],
                    Label: _zemaxCompatibleOutput ? string.Empty : fields[index].Label)).ToArray(),
                Name: $"{frequency:0.###} {frequencyUnitLabel}, Sagittal",
                LineStyle: AnalysisLineStyle.Dashed,
                ColorIndex: frequencyIndex,
                XQuantity: _zemaxCompatibleOutput ? AnalysisAxisQuantity.NormalizedField : AnalysisTrace.FieldAxisQuantity(Optic),
                XUnit: _zemaxCompatibleOutput ? AnalysisAxisUnit.Dimensionless : AnalysisTrace.FieldAxisUnit(Optic),
                YQuantity: AnalysisAxisQuantity.Modulation,
                YUnit: AnalysisAxisUnit.Dimensionless));
        }

        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["Method"] = MtfMethodEvaluator.MethodName(_method),
            ["SpatialFrequency"] = _spatialFrequencies[0],
            ["SpatialFrequencies"] = _spatialFrequencies,
            ["HuygensMtfTransformSize"] = _method == MtfComputationMethod.Huygens
                ? Math.Max(4, _settings.ImageSize) * (_settings.UseZemaxHuygensSemantics ? 2 : 1) : 0,
            ["HuygensFrequencySampling"] = _method != MtfComputationMethod.Huygens ? "NotApplicable"
                : _settings.UseZemaxHuygensSemantics ? "NaturalCubicEndpointSpan" : "LinearDftPeriod",
            ["FrequencyUnit"] = frequencyUnitLabel,
            ["ImageSpaceAfocal"] = workingOptic.ImageSpaceAfocal,
            ["FieldPointCount"] = fields.Count,
            ["FieldDensity"] = _fieldPointCount,
            ["PlotPointCount"] = plotCoordinates.Length,
            ["MaximumField"] = AnalysisTrace.MaxFieldValue(workingOptic),
            ["FieldUnit"] = fieldUnit,
            ["ScanType"] = _scanType,
            ["RemoveVignettingFactors"] = _removeVignettingFactors,
            ["VignettingFactorsApplied"] = !_removeVignettingFactors,
            ["WavelengthNumber"] = _wavelengthNumber,
            ["WavelengthsMicrometers"] = wavelengths.Select(item => item.Micrometers).ToArray(),
            ["Tangential"] = tangential[0],
            ["Sagittal"] = sagittal[0],
            ["TangentialByFrequency"] = tangential,
            ["SagittalByFrequency"] = sagittal
        }, series[0], series, new AnalysisPlotOptions(
            Title: $"{MtfMethodEvaluator.MethodName(_method)} MTF vs Field",
            XMinimum: plotCoordinates.DefaultIfEmpty(0).Min(),
            XMaximum: plotCoordinates.DefaultIfEmpty(0).Max(),
            YMinimum: 0,
            YMaximum: 1.05,
            ShowLegend: true,
            DottedGrid: true,
            GridOpacity: 0.35));
    }
}

internal static class MtfMethodEvaluator
{
    public static IReadOnlyList<Wavelength> SelectWavelengths(Optic optic, int wavelengthNumber)
    {
        var wavelengths = optic.Wavelengths.ToArray();
        if (wavelengths.Length == 0)
        {
            return Array.Empty<Wavelength>();
        }

        if (wavelengthNumber < 0)
        {
            return new[]
            {
                wavelengths.FirstOrDefault(item => item.IsPrimary) ?? wavelengths[0]
            };
        }

        if (wavelengthNumber == 0)
        {
            return wavelengths;
        }

        return new[] { wavelengths[Math.Clamp(wavelengthNumber - 1, 0, wavelengths.Length - 1)] };
    }

    public static string MethodName(MtfComputationMethod method)
    {
        return method switch
        {
            MtfComputationMethod.Fourier => "Fourier",
            MtfComputationMethod.Huygens => "Huygens",
            MtfComputationMethod.Geometric => "Geometric",
            _ => method.ToString()
        };
    }

    public static (double Tangential, double Sagittal) Evaluate(
        Optic optic,
        MtfComputationMethod method,
        (double Hx, double Hy) field,
        Wavelength wavelength,
        double spatialFrequency,
        MtfComputationSettings settings,
        double defocus = 0)
    {
        return method switch
        {
            MtfComputationMethod.Fourier => EvaluateFourier(optic, field, wavelength, spatialFrequency, settings),
            MtfComputationMethod.Huygens => EvaluateHuygens(
                optic,
                field,
                wavelength,
                spatialFrequency,
                settings,
                defocus),
            MtfComputationMethod.Geometric => EvaluateGeometric(
                optic,
                field,
                wavelength,
                spatialFrequency,
                settings,
                defocus),
            _ => throw new ArgumentOutOfRangeException(nameof(method))
        };
    }

    public static (double Tangential, double Sagittal) EvaluatePolychromatic(
        Optic optic,
        MtfComputationMethod method,
        (double Hx, double Hy) field,
        IReadOnlyList<Wavelength> wavelengths,
        double spatialFrequency,
        MtfComputationSettings settings,
        FftMtfDataType dataType = FftMtfDataType.Modulation,
        double defocusMillimeters = 0)
    {
        if (wavelengths.Count == 0)
        {
            return (0, 0);
        }

        if (method == MtfComputationMethod.Fourier)
        {
            if (settings.ZemaxCompatible && dataType != FftMtfDataType.SquareWave)
            {
                // Field scans and focus scans use the same pupil autocorrelation.
                // A short, unpadded image FFT aliases the pupil overlap at low frequencies.
                var value = EvaluateFourierThroughFocus(optic, field, wavelengths,
                    new[] { defocusMillimeters }, spatialFrequency, settings, dataType);
                return (value.Tangential[0], value.Sagittal[0]);
            }
            var pupilSampling = Math.Max(8, settings.PupilSampling);
            var gridSize = NextPowerOfTwo(Math.Max(pupilSampling, settings.ImageSize));
            var results = wavelengths.Select(wavelength =>
            {
                var psf = DiffractionEngine.ComputeFftPsf(
                    optic,
                    field,
                    wavelength,
                    pupilSampling,
                    gridSize,
                    settings.UsePolarization,
                    cellCenteredPupil: settings.ZemaxCompatible,
                    defocusMillimeters: defocusMillimeters,
                    referenceWavelength: wavelengths.FirstOrDefault(item => item.IsPrimary) ?? wavelengths[0]);
                return (wavelength, DiffractionEngine.ComputeFftMtf(psf, optic, wavelength));
            }).ToArray();
            var combined = CombinePolychromatic(results);
            return (
                Sample(combined, spatialFrequency, dataType, tangential: true),
                Sample(combined, spatialFrequency, dataType, tangential: false));
        }

        if (method == MtfComputationMethod.Huygens)
        {
            return EvaluateHuygensPolychromatic(
                optic,
                field,
                wavelengths,
                spatialFrequency,
                settings);
        }

        var totalWeight = wavelengths.Sum(wavelength => wavelength.Weight);
        var useEqualWeights = totalWeight <= 1e-30;
        if (useEqualWeights)
        {
            totalWeight = wavelengths.Count;
        }

        var tangential = Complex.Zero;
        var sagittal = Complex.Zero;
        foreach (var wavelength in wavelengths)
        {
            var weight = useEqualWeights ? 1.0 : wavelength.Weight;
            var value = EvaluateGeometricOtf(optic, field, wavelength, spatialFrequency, settings, defocusMillimeters);
            tangential += value.Tangential * weight;
            sagittal += value.Sagittal * weight;
        }

        return (
            DataTypeValue(tangential / totalWeight, dataType),
            DataTypeValue(sagittal / totalWeight, dataType));
    }

    internal static (double[] Tangential, double[] Sagittal) EvaluateFourierThroughFocus(
        Optic optic,
        (double Hx, double Hy) field,
        IReadOnlyList<Wavelength> wavelengths,
        IReadOnlyList<double> focus,
        double spatialFrequency,
        MtfComputationSettings settings,
        FftMtfDataType dataType)
    {
        if (dataType == FftMtfDataType.SquareWave)
        {
            var responses = focus.Select(defocus => EvaluatePolychromatic(optic, MtfComputationMethod.Fourier,
                field, wavelengths, spatialFrequency, settings, dataType, defocus)).ToArray();
            return (responses.Select(item => item.Tangential).ToArray(), responses.Select(item => item.Sagittal).ToArray());
        }
        var pupilSampling = Math.Max(8, settings.PupilSampling);
        var referenceWavelength = wavelengths.FirstOrDefault(item => item.IsPrimary) ?? wavelengths[0];
        var wavelengthResults = wavelengths.Select(wavelength =>
        {
            var wavefront = WavefrontEngine.GenerateChiefRayUniform(
                optic,
                field,
                wavelength,
                pupilSampling,
                cellCentered: true,
                aimAtStop: true,
                referenceWavelength: referenceWavelength);
            var polarization = settings.UsePolarization
                ? JonesPupilEngine.Generate(
                    optic,
                    field,
                    wavelength,
                    pupilSampling,
                    useFresnelCoatings: true,
                    cellCentered: true)
                : null;
            var results = focus.Select(defocus =>
                DiffractionEngine.ComputeFastFftMtfAtFrequency(
                    optic,
                    field,
                    wavelength,
                    pupilSampling,
                    spatialFrequency,
                    defocus,
                    settings.UsePolarization,
                    wavefront,
                    polarization,
                    referenceWavelength)).ToArray();
            return (Wavelength: wavelength, Results: results);
        }).ToArray();

        var tangential = new double[focus.Count];
        var sagittal = new double[focus.Count];
        var totalWeight = wavelengthResults.Sum(item => item.Wavelength.Weight);
        var useEqualWeights = totalWeight <= 1e-30;
        if (useEqualWeights)
        {
            totalWeight = Math.Max(1, wavelengthResults.Length);
        }

        for (var focusIndex = 0; focusIndex < focus.Count; focusIndex++)
        {
            var tangentialComplex = Complex.Zero;
            var sagittalComplex = Complex.Zero;
            foreach (var item in wavelengthResults)
            {
                var weight = useEqualWeights ? 1 : item.Wavelength.Weight;
                tangentialComplex += item.Results[focusIndex].Tangential * weight;
                sagittalComplex += item.Results[focusIndex].Sagittal * weight;
            }

            tangential[focusIndex] = DataTypeValue(
                tangentialComplex / totalWeight,
                dataType);
            sagittal[focusIndex] = DataTypeValue(
                sagittalComplex / totalWeight,
                dataType);
        }

        return (tangential, sagittal);
    }

    private static double DataTypeValue(
        Complex value,
        FftMtfDataType dataType)
    {
        return dataType switch
        {
            FftMtfDataType.Real => value.Real,
            FftMtfDataType.Imaginary => value.Imaginary,
            FftMtfDataType.Phase => value.Phase,
            _ => Math.Clamp(value.Magnitude, 0, 1)
        };
    }

    public static MtfResult CombinePolychromatic(
        IReadOnlyList<(Wavelength Wavelength, MtfResult Result)> results)
    {
        if (results.Count == 0)
        {
            return new MtfResult(Array.Empty<double>(), Array.Empty<double>(), Array.Empty<double>(), 0);
        }

        var totalWeight = results.Sum(item => item.Wavelength.Weight);
        var useEqualWeights = totalWeight <= 1e-30;
        if (useEqualWeights)
        {
            totalWeight = results.Count;
        }

        var active = results.Where(item => useEqualWeights || item.Wavelength.Weight > 0).ToArray();
        var tangentialFrequency = active.Select(item => item.Result.TangentialFrequency ?? item.Result.Frequency)
            .MaxBy(axis => axis.LastOrDefault())!.ToArray();
        var sagittalFrequency = active.Select(item => item.Result.SagittalFrequency ?? item.Result.Frequency)
            .MaxBy(axis => axis.LastOrDefault())!.ToArray();
        var tangential = new double[tangentialFrequency.Length];
        var sagittal = new double[sagittalFrequency.Length];
        var hasComplexOtf = active.All(item =>
            item.Result.TangentialOtf is not null
            && item.Result.SagittalOtf is not null);
        if (active.Length > 1 && !hasComplexOtf)
        {
            throw new ArgumentException("Polychromatic MTF requires complex OTF data for every active wavelength.", nameof(results));
        }
        var tangentialOtf = hasComplexOtf ? new Complex[tangentialFrequency.Length] : null;
        var sagittalOtf = hasComplexOtf ? new Complex[sagittalFrequency.Length] : null;
        foreach (var item in active)
        {
            var weight = useEqualWeights ? 1.0 : item.Wavelength.Weight;
            var itemTangentialFrequency = item.Result.TangentialFrequency ?? item.Result.Frequency;
            var itemSagittalFrequency = item.Result.SagittalFrequency ?? item.Result.Frequency;
            for (var index = 0; index < tangentialFrequency.Length; index++)
            {
                tangential[index] += Interpolate(
                    itemTangentialFrequency,
                    item.Result.Tangential,
                    tangentialFrequency[index]) * weight;
                if (hasComplexOtf)
                {
                    tangentialOtf![index] += InterpolateComplex(
                        itemTangentialFrequency,
                        item.Result.TangentialOtf!,
                        tangentialFrequency[index]) * weight;
                }
            }

            for (var index = 0; index < sagittalFrequency.Length; index++)
            {
                sagittal[index] += Interpolate(
                    itemSagittalFrequency,
                    item.Result.Sagittal,
                    sagittalFrequency[index]) * weight;
                if (hasComplexOtf)
                {
                    sagittalOtf![index] += InterpolateComplex(
                        itemSagittalFrequency,
                        item.Result.SagittalOtf!,
                        sagittalFrequency[index]) * weight;
                }
            }
        }

        if (hasComplexOtf)
        {
            for (var index = 0; index < tangentialFrequency.Length; index++)
            {
                tangentialOtf![index] /= totalWeight;
                tangential[index] = Math.Clamp(tangentialOtf[index].Magnitude, 0, 1);
            }

            for (var index = 0; index < sagittalFrequency.Length; index++)
            {
                sagittalOtf![index] /= totalWeight;
                sagittal[index] = Math.Clamp(sagittalOtf[index].Magnitude, 0, 1);
            }
        }
        else
        {
            tangential = tangential.Select(value => Math.Clamp(value / totalWeight, 0, 1)).ToArray();
            sagittal = sagittal.Select(value => Math.Clamp(value / totalWeight, 0, 1)).ToArray();
        }

        return new MtfResult(
            tangentialFrequency,
            tangential,
            sagittal,
            active.Max(item => item.Result.CutoffFrequency),
            tangentialOtf,
            sagittalOtf,
            tangentialFrequency,
            sagittalFrequency);
    }

    private static (double Tangential, double Sagittal) EvaluateFourier(
        Optic optic,
        (double Hx, double Hy) field,
        Wavelength wavelength,
        double spatialFrequency,
        MtfComputationSettings settings)
    {
        var pupilSampling = Math.Max(8, settings.PupilSampling);
        var gridSize = NextPowerOfTwo(Math.Max(pupilSampling, settings.ImageSize));
        var psf = DiffractionEngine.ComputeFftPsf(
            optic,
            field,
            wavelength,
            pupilSampling,
            gridSize,
            settings.UsePolarization,
            cellCenteredPupil: settings.ZemaxCompatible);
        return AtFrequency(DiffractionEngine.ComputeFftMtf(psf, optic, wavelength), spatialFrequency);
    }

    private static (double Tangential, double Sagittal) EvaluateHuygens(
        Optic optic,
        (double Hx, double Hy) field,
        Wavelength wavelength,
        double spatialFrequency,
        MtfComputationSettings settings,
        double defocus = 0)
    {
        var resolvedSettings = settings.PixelPitchMillimeters > 0
            ? settings
            : settings with
            {
                PixelPitchMillimeters = ResolveHuygensImageDeltaMillimeters(
                    optic,
                    field,
                    new[] { wavelength },
                    settings)
            };
        var psf = DiffractionEngine.ComputeHuygensPsf(
            optic,
            field,
            wavelength,
            Math.Max(2, resolvedSettings.PupilSampling),
            Math.Max(4, resolvedSettings.ImageSize),
            resolvedSettings.PixelPitchMillimeters,
            usePolarization: resolvedSettings.UsePolarization,
            aimAtStop: resolvedSettings.UseZemaxHuygensSemantics && optic.RayAimingEnabled,
            defocus: optic.ImageSpaceAfocal ? defocus : 0);
        return SampleHuygensMtf(
            DiffractionEngine.ComputePsfMtf(psf), psf.GridSize, spatialFrequency,
            resolvedSettings.UseZemaxHuygensSemantics);
    }

    internal static (double Tangential, double Sagittal)[] EvaluatePolychromaticFrequencies(
        Optic optic,
        MtfComputationMethod method,
        (double Hx, double Hy) field,
        IReadOnlyList<Wavelength> wavelengths,
        IReadOnlyList<double> spatialFrequencies,
        MtfComputationSettings settings)
    {
        if (method == MtfComputationMethod.Huygens && settings.UseZemaxHuygensSemantics)
        {
            var psf = ComputeHuygensPolychromaticPsf(optic, field, wavelengths, settings);
            // Frequency/field plots use a 2N transform; through-focus uses N.
            // Both native contracts interpolate on the transform endpoint span.
            var mtf = DiffractionEngine.ComputePsfMtf(psf, doubleTransformSize: true);
            return spatialFrequencies
                .Select(frequency => SampleHuygensMtf(mtf, 2 * psf.GridSize, frequency, true))
                .ToArray();
        }

        return spatialFrequencies.Select(frequency => EvaluatePolychromatic(
            optic,
            method,
            field,
            wavelengths,
            frequency,
            settings)).ToArray();
    }

    private static (double Tangential, double Sagittal) EvaluateHuygensPolychromatic(
        Optic optic,
        (double Hx, double Hy) field,
        IReadOnlyList<Wavelength> wavelengths,
        double spatialFrequency,
        MtfComputationSettings settings)
    {
        return SampleHuygensMtf(
            ComputeHuygensPolychromaticMtf(optic, field, wavelengths, settings),
            Math.Max(4, settings.ImageSize), spatialFrequency, settings.UseZemaxHuygensSemantics);
    }

    internal static (double Tangential, double Sagittal) SampleHuygensMtf(
        MtfResult result, int transformSize, double frequency, bool zemaxCompatible)
    {
        if (!zemaxCompatible)
        {
            return AtFrequency(result, frequency);
        }

        // Captured 2026 R1 Huygens output uses the endpoint span (N-1)*dx
        // for its frequency axis, then a natural cubic spline. Keep this display
        // convention separate from the physical DFT grid, whose period is N*dx.
        var target = Math.Max(0, frequency) * (transformSize - 1.0) / transformSize;
        double SampleCurve(IReadOnlyList<double> axis, IReadOnlyList<double> values)
        {
            if (axis.Count < 2 || target > axis[^1])
            {
                return Interpolate(axis, values, target);
            }

            return Math.Clamp(MtfThroughFocusAnalysis.CubicSplineInterpolate(axis, values, [target])[0], 0, 1);
        }

        return (
            SampleCurve(result.TangentialFrequency ?? result.Frequency, result.Tangential),
            SampleCurve(result.SagittalFrequency ?? result.Frequency, result.Sagittal));
    }

    private static MtfResult ComputeHuygensPolychromaticMtf(
        Optic optic,
        (double Hx, double Hy) field,
        IReadOnlyList<Wavelength> wavelengths,
        MtfComputationSettings settings)
    {
        return DiffractionEngine.ComputePsfMtf(
            ComputeHuygensPolychromaticPsf(optic, field, wavelengths, settings));
    }

    private static PsfResult ComputeHuygensPolychromaticPsf(
        Optic optic,
        (double Hx, double Hy) field,
        IReadOnlyList<Wavelength> wavelengths,
        MtfComputationSettings settings)
    {
        var pupilSampling = Math.Max(2, settings.PupilSampling);
        var imageSize = Math.Max(4, settings.ImageSize);
        var pixelPitchMillimeters = ResolveHuygensImageDeltaMillimeters(
            optic,
            field,
            wavelengths,
            settings);
        var shortestWavelength = wavelengths.Min(item => item.Micrometers);
        var useConfiguredWeights = wavelengths.Any(item => item.Weight > 0);
        var results = wavelengths.Select(wavelength =>
        {
            var wavelengthWeight = useConfiguredWeights ? wavelength.Weight : 1;
            var zemaxHuygensWeight = wavelengthWeight
                * (settings.UseZemaxHuygensSemantics ? Math.Pow(shortestWavelength / wavelength.Micrometers, 2) : 1);
            var psf = DiffractionEngine.ComputeHuygensPsf(
                optic,
                field,
                wavelength,
                pupilSampling,
                imageSize,
                pixelPitchMillimeters,
                settings.UsePolarization,
                aimAtStop: optic.RayAimingEnabled,
                referenceWavelength: wavelengths.FirstOrDefault(item => item.IsPrimary) ?? wavelengths[0]);
            return (Psf: psf, Weight: zemaxHuygensWeight);
        }).ToArray();
        var combinedValues = new double[imageSize, imageSize];
        for (var row = 0; row < imageSize; row++)
        {
            for (var column = 0; column < imageSize; column++)
            {
                combinedValues[row, column] = results.Sum(item =>
                    item.Weight * item.Psf.Values[row, column]);
            }
        }

        var combinedPsf = new PsfResult(
            combinedValues,
            pupilSampling,
            imageSize,
            results.Average(item => item.Psf.WorkingFNumber),
            optic.ImageSpaceAfocal ? pixelPitchMillimeters : pixelPitchMillimeters * 1000,
            SampleSpacingUnit: optic.ImageSpaceAfocal
                ? AnalysisAxisUnit.Milliradian
                : AnalysisAxisUnit.Micrometer);
        return combinedPsf;
    }

    internal static double ResolveHuygensImageDeltaMillimeters(
        Optic optic,
        (double Hx, double Hy) field,
        IReadOnlyList<Wavelength> wavelengths,
        MtfComputationSettings settings)
    {
        if (settings.PixelPitchMillimeters > 0)
        {
            return settings.PixelPitchMillimeters;
        }

        var longestWavelength = wavelengths.MaxBy(item => item.Micrometers)
            ?? throw new ArgumentException("At least one wavelength is required.", nameof(wavelengths));
        return DiffractionEngine.DefaultHuygensImageDeltaMillimeters(
            optic,
            field,
            longestWavelength,
            Math.Max(2, settings.PupilSampling));
    }

    private static (double Tangential, double Sagittal) EvaluateGeometric(
        Optic optic,
        (double Hx, double Hy) field,
        Wavelength wavelength,
        double spatialFrequency,
        MtfComputationSettings settings,
        double defocus = 0)
    {
        var otf = EvaluateGeometricOtf(optic, field, wavelength, spatialFrequency, settings, defocus);
        return (otf.Tangential.Magnitude, otf.Sagittal.Magnitude);
    }

    private static (Complex Tangential, Complex Sagittal) EvaluateGeometricOtf(
        Optic optic,
        (double Hx, double Hy) field,
        Wavelength wavelength,
        double spatialFrequency,
        MtfComputationSettings settings,
        double defocus = 0)
    {
        var result = SpotAnalysisEngine.Generate(
            optic,
            new[] { field },
            new[] { wavelength },
            Math.Max(2, settings.GeometricRayCount),
            settings.Distribution,
            imagePlaneOffset: optic.ImageSpaceAfocal ? defocus : 0,
            reference: "absolute", aimAtStop: optic.RayAimingEnabled,
            includeSurfaceTransmission: settings.UsePolarization, usePolarization: settings.UsePolarization);
        var rays = result.Fields.FirstOrDefault()?.Wavelengths.FirstOrDefault()?.Rays
            ?? Array.Empty<SpotRayData>();
        var fNumber = Math.Abs(optic.Paraxial.EstimateFNumber());
        var cutoff = optic.ImageSpaceAfocal
            ? ImageSpaceAnalysisSupport.AfocalCutoffFrequencyCyclesPerMilliradian(optic, wavelength)
            : fNumber <= 1e-30 ? 0 : 1 / (wavelength.Micrometers * 1e-3 * fNumber);
        var scale = settings.ScaleGeometricByDiffractionLimit
            ? DiffractionScale(spatialFrequency, cutoff)
            : 1.0;
        return (
            GeometricOtfAtFrequency(rays, spatialFrequency, tangential: true) * scale,
            GeometricOtfAtFrequency(rays, spatialFrequency, tangential: false) * scale);
    }

    private static (double Tangential, double Sagittal) AtFrequency(MtfResult result, double frequency)
    {
        var tangentialFrequency = result.TangentialFrequency ?? result.Frequency;
        var sagittalFrequency = result.SagittalFrequency ?? result.Frequency;
        return (
            Interpolate(tangentialFrequency, result.Tangential, frequency),
            Interpolate(sagittalFrequency, result.Sagittal, frequency));
    }

    internal static double Sample(
        MtfResult result,
        double frequency,
        FftMtfDataType type,
        bool tangential)
    {
        var sourceFrequency = tangential
            ? result.TangentialFrequency ?? result.Frequency
            : result.SagittalFrequency ?? result.Frequency;
        var scalar = tangential ? result.Tangential : result.Sagittal;
        var complex = tangential ? result.TangentialOtf : result.SagittalOtf;
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

                sum += sign * Interpolate(sourceFrequency, scalar, harmonicFrequency) / harmonic;
                sign *= -1;
            }

            return Math.Max(0, 4 * sum / Math.PI);
        }

        if (complex is null)
        {
            return Interpolate(sourceFrequency, scalar, frequency);
        }

        var value = InterpolateComplex(sourceFrequency, complex, frequency);
        return type switch
        {
            FftMtfDataType.Real => value.Real,
            FftMtfDataType.Imaginary => value.Imaginary,
            FftMtfDataType.Phase => value.Phase,
            _ => value.Magnitude
        };
    }

    internal static double Interpolate(IReadOnlyList<double> x, IReadOnlyList<double> y, double target)
    {
        if (x.Count == 0 || y.Count == 0 || target > x[^1])
        {
            return 0;
        }

        if (target <= x[0])
        {
            return Math.Clamp(y[0], 0, 1);
        }

        for (var index = 1; index < x.Count; index++)
        {
            if (target > x[index])
            {
                continue;
            }

            var width = x[index] - x[index - 1];
            var fraction = width <= 1e-30 ? 0 : (target - x[index - 1]) / width;
            return Math.Clamp(y[index - 1] + ((y[index] - y[index - 1]) * fraction), 0, 1);
        }

        return 0;
    }

    internal static Complex InterpolateComplex(
        IReadOnlyList<double> x,
        IReadOnlyList<Complex> y,
        double target)
    {
        if (x.Count == 0 || y.Count == 0 || target > x[^1])
        {
            return Complex.Zero;
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

        return Complex.Zero;
    }

    private static Complex GeometricOtfAtFrequency(IReadOnlyList<SpotRayData> rays, double frequency, bool tangential)
    {
        var totalWeight = rays.Sum(ray => Math.Max(0, ray.Intensity));
        if (!(totalWeight > 0) || !double.IsFinite(totalWeight))
        {
            throw new AnalysisDataUnavailableException("Geometric MTF", "no finite positive-intensity rays");
        }
        var otf = Complex.Zero;
        foreach (var ray in rays)
        {
            otf += Math.Max(0, ray.Intensity) * Complex.FromPolarCoordinates(1,
                -2 * Math.PI * frequency * (tangential ? ray.Y : ray.X));
        }
        return otf / totalWeight;
    }

    private static double DiffractionScale(double frequency, double cutoff)
    {
        if (cutoff <= 1e-30 || frequency >= cutoff)
        {
            return frequency <= 1e-30 ? 1 : 0;
        }

        var ratio = Math.Clamp(frequency / cutoff, 0, 1);
        var phi = Math.Acos(ratio);
        return (2 / Math.PI) * (phi - (Math.Cos(phi) * Math.Sin(phi)));
    }

    private static int NextPowerOfTwo(int value)
        => AnalysisResourceLimits.RoundUpPowerOfTwo(value, nameof(value));
}

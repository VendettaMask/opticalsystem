using System.Numerics;
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
        _dataType = MtfMethodEvaluator.ParseDataType(type);
        _useDashes = useDashes;
    }

    public override string Name => $"{MtfMethodEvaluator.MethodName(_method)} Through Focus MTF";

    public override AnalysisData GenerateData()
    {
        var wavelengths = MtfMethodEvaluator.SelectWavelengths(Optic, _wavelengthNumber);
        var imageSurface = Optic.SurfaceGroup.Items.LastOrDefault();
        if (wavelengths.Count == 0 || imageSurface is null)
        {
            return new AnalysisData(Name, new Dictionary<string, object> { ["Status"] = "No optical data" });
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
        else
        {
            try
            {
                for (var focusIndex = 0; focusIndex < focus.Length; focusIndex++)
                {
                    imageSurface.CoordinateSystem = originalCoordinateSystem with
                    {
                        Origin = originalCoordinateSystem.Origin with
                        {
                            Z = originalCoordinateSystem.Origin.Z + focus[focusIndex]
                        }
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
                "Defocus (mm)",
                MtfMethodEvaluator.DataTypeLabel(_dataType),
                displayFocus.Select((value, index) => new AnalysisPoint(
                    value,
                DisplayValue(tangentialDisplay[index]))).ToArray(),
                Name: MtfPresentation.SeriesName(Optic, field, "Tangential"),
                ColorIndex: _useDashes ? 0 : colorIndex,
                XQuantity: AnalysisAxisQuantity.Defocus,
                XUnit: AnalysisAxisUnit.Millimeter,
                YQuantity: AnalysisAxisQuantity.Modulation,
                YUnit: _dataType == FftMtfDataType.Phase ? AnalysisAxisUnit.Radian : AnalysisAxisUnit.Dimensionless));
            series.Add(new AnalysisSeries(
                "Defocus (mm)",
                MtfMethodEvaluator.DataTypeLabel(_dataType),
                displayFocus.Select((value, index) => new AnalysisPoint(
                    value,
                    DisplayValue(sagittalDisplay[index]))).ToArray(),
                Name: MtfPresentation.SeriesName(Optic, field, "Sagittal"),
                LineStyle: AnalysisLineStyle.Dashed,
                ColorIndex: _useDashes ? 0 : colorIndex,
                XQuantity: AnalysisAxisQuantity.Defocus,
                XUnit: AnalysisAxisUnit.Millimeter,
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
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["Method"] = MtfMethodEvaluator.MethodName(_method),
            ["FrequencyInput"] = _frequencyInput,
            ["SpatialFrequency"] = _spatialFrequency,
            ["DeltaFocus"] = _deltaFocus,
            ["Steps"] = _focusPlaneCount,
            ["NumberOfSteps"] = _focusPlaneCount,
            ["WavelengthNumber"] = _wavelengthNumber,
            ["FieldNumber"] = _fieldNumber,
            ["Type"] = MtfMethodEvaluator.DataTypeName(_dataType),
            ["UsePolarization"] = _settings.UsePolarization,
            ["UseDashes"] = _useDashes,
            ["ZemaxCompatible"] = _settings.ZemaxCompatible,
            ["PupilSampling"] = _settings.PupilSampling,
            ["ImageSampling"] = _settings.ImageSize,
            ["ImageDeltaMicrometers"] = _settings.PixelPitchMillimeters * 1000,
            ["ResolvedImageDeltaMicrometers"] = _method == MtfComputationMethod.Huygens
                ? fields.Select(field => MtfMethodEvaluator.ResolveHuygensImageDeltaMillimeters(
                    Optic,
                    field,
                    wavelengths,
                    _settings) * 1000).ToArray()
                : Array.Empty<double>(),
            ["WavelengthsMicrometers"] = wavelengths.Select(item => item.Micrometers).ToArray(),
            ["RawTangential"] = tangential,
            ["RawSagittal"] = sagittal
        }, series.FirstOrDefault(), series, new AnalysisPlotOptions(
            Title: $"{MtfMethodEvaluator.MethodName(_method)} Through-Focus MTF at {_spatialFrequency:0.###} cycles/mm, {wavelengthLabel}",
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
            return new AnalysisData(Name, new Dictionary<string, object> { ["Status"] = "No wavelengths" });
        }

        var fields = _zemaxCompatibleOutput
            ? AnalysisTrace.ScanFieldSamples(workingOptic, _scanType, _fieldPointCount + 1)
            : AnalysisTrace.DefinedFieldSamples(workingOptic);
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
                Name: $"{frequency:0.###} cycles/mm, Tangential",
                LineStyle: _useDashes && frequencyIndex % 2 == 1
                    ? AnalysisLineStyle.Dashed
                    : AnalysisLineStyle.Solid,
                ColorIndex: frequencyIndex,
                XQuantity: AnalysisTrace.FieldAxisQuantity(Optic),
                XUnit: AnalysisTrace.FieldAxisUnit(Optic),
                YQuantity: AnalysisAxisQuantity.Modulation,
                YUnit: AnalysisAxisUnit.Dimensionless));
            series.Add(new AnalysisSeries(
                axisLabel,
                "MTF",
                plotCoordinates.Select((value, index) => new AnalysisPoint(
                    value,
                    sagittalDisplay[index],
                    Label: _zemaxCompatibleOutput ? string.Empty : fields[index].Label)).ToArray(),
                Name: $"{frequency:0.###} cycles/mm, Sagittal",
                LineStyle: AnalysisLineStyle.Dashed,
                ColorIndex: frequencyIndex,
                XQuantity: AnalysisTrace.FieldAxisQuantity(Optic),
                XUnit: AnalysisTrace.FieldAxisUnit(Optic),
                YQuantity: AnalysisAxisQuantity.Modulation,
                YUnit: AnalysisAxisUnit.Dimensionless));
        }

        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["Method"] = MtfMethodEvaluator.MethodName(_method),
            ["SpatialFrequency"] = _spatialFrequencies[0],
            ["SpatialFrequencies"] = _spatialFrequencies,
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

    public static Wavelength? PrimaryWavelength(Optic optic)
    {
        return optic.Wavelengths.FirstOrDefault(item => item.IsPrimary)
            ?? optic.Wavelengths.FirstOrDefault();
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
        MtfComputationSettings settings)
    {
        return method switch
        {
            MtfComputationMethod.Fourier => EvaluateFourier(optic, field, wavelength, spatialFrequency, settings),
            MtfComputationMethod.Huygens => EvaluateHuygens(optic, field, wavelength, spatialFrequency, settings),
            MtfComputationMethod.Geometric => EvaluateGeometric(optic, field, wavelength, spatialFrequency, settings),
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
                    defocusMillimeters: defocusMillimeters);
                return (wavelength, DiffractionEngine.ComputeFftMtf(psf, optic, wavelength));
            }).ToArray();
            var combined = CombinePolychromatic(results);
            return (
                Sample(combined, spatialFrequency, dataType, tangential: true),
                Sample(combined, spatialFrequency, dataType, tangential: false));
        }

        if (method == MtfComputationMethod.Huygens && settings.UseZemaxHuygensSemantics)
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

        var tangential = 0.0;
        var sagittal = 0.0;
        foreach (var wavelength in wavelengths)
        {
            var weight = useEqualWeights ? 1.0 : wavelength.Weight;
            var value = Evaluate(optic, method, field, wavelength, spatialFrequency, settings);
            tangential += value.Tangential * weight;
            sagittal += value.Sagittal * weight;
        }

        return (
            Math.Clamp(tangential / totalWeight, 0, 1),
            Math.Clamp(sagittal / totalWeight, 0, 1));
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
        var pupilSampling = Math.Max(8, settings.PupilSampling);
        var gridSize = NextPowerOfTwo(Math.Max(pupilSampling, settings.ImageSize));
        var wavelengthResults = wavelengths.Select(wavelength =>
        {
            var wavefront = WavefrontEngine.GenerateChiefRayUniform(
                optic,
                field,
                wavelength,
                pupilSampling,
                cellCentered: true,
                aimAtStop: true);
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
            {
                if (dataType == FftMtfDataType.Modulation)
                {
                    return DiffractionEngine.ComputeFastFftMtfAtFrequency(
                        optic,
                        field,
                        wavelength,
                        pupilSampling,
                        spatialFrequency,
                        defocus,
                        settings.UsePolarization,
                        wavefront,
                        polarization);
                }

                var defocusedWavefront = Math.Abs(defocus) <= 1e-30
                    ? wavefront
                    : DiffractionEngine.GenerateDefocusedWavefront(
                        optic,
                        field,
                        wavelength,
                        wavefront.Samples
                            .Select(sample => (
                                sample.NormalizedPupilX,
                                sample.NormalizedPupilY))
                            .ToArray(),
                        defocus);
                var psf = DiffractionEngine.ComputeFftPsf(
                    optic,
                    field,
                    wavelength,
                    pupilSampling,
                    gridSize,
                    settings.UsePolarization,
                    cellCenteredPupil: true,
                    defocusMillimeters: 0,
                    preparedWavefront: defocusedWavefront,
                    preparedPolarization: polarization);
                var mtf = DiffractionEngine.ComputeFftMtf(psf, optic, wavelength);
                return (
                    Tangential: InterpolateComplex(
                        mtf.TangentialFrequency ?? mtf.Frequency,
                        mtf.TangentialOtf ?? Array.Empty<Complex>(),
                        spatialFrequency),
                    Sagittal: InterpolateComplex(
                        mtf.SagittalFrequency ?? mtf.Frequency,
                        mtf.SagittalOtf ?? Array.Empty<Complex>(),
                        spatialFrequency));
            }).ToArray();
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
            var tangentialModulation = 0.0;
            var sagittalModulation = 0.0;
            foreach (var item in wavelengthResults)
            {
                var weight = useEqualWeights ? 1 : item.Wavelength.Weight;
                tangentialComplex += item.Results[focusIndex].Tangential * weight;
                sagittalComplex += item.Results[focusIndex].Sagittal * weight;
                tangentialModulation += item.Results[focusIndex].Tangential.Magnitude * weight;
                sagittalModulation += item.Results[focusIndex].Sagittal.Magnitude * weight;
            }

            tangential[focusIndex] = DataTypeValue(
                tangentialComplex / totalWeight,
                tangentialModulation / totalWeight,
                dataType);
            sagittal[focusIndex] = DataTypeValue(
                sagittalComplex / totalWeight,
                sagittalModulation / totalWeight,
                dataType);
        }

        return (tangential, sagittal);
    }

    private static double DataTypeValue(
        Complex value,
        double modulation,
        FftMtfDataType dataType)
    {
        return dataType switch
        {
            FftMtfDataType.Real => value.Real,
            FftMtfDataType.Imaginary => value.Imaginary,
            FftMtfDataType.Phase => value.Phase,
            FftMtfDataType.SquareWave => modulation,
            _ => modulation
        };
    }

    public static MtfResult CombinePolychromatic(
        IReadOnlyList<(Wavelength Wavelength, MtfResult Result)> results)
    {
        if (results.Count == 0)
        {
            return new MtfResult(Array.Empty<double>(), Array.Empty<double>(), Array.Empty<double>(), 0);
        }

        var reference = results.FirstOrDefault(item => item.Wavelength.IsPrimary);
        if (reference.Wavelength is null)
        {
            reference = results[0];
        }

        var totalWeight = results.Sum(item => item.Wavelength.Weight);
        var useEqualWeights = totalWeight <= 1e-30;
        if (useEqualWeights)
        {
            totalWeight = results.Count;
        }

        var tangentialFrequency = (reference.Result.TangentialFrequency ?? reference.Result.Frequency).ToArray();
        var sagittalFrequency = (reference.Result.SagittalFrequency ?? reference.Result.Frequency).ToArray();
        var tangential = new double[tangentialFrequency.Length];
        var sagittal = new double[sagittalFrequency.Length];
        var hasComplexOtf = results.All(item =>
            item.Result.TangentialOtf is not null
            && item.Result.SagittalOtf is not null);
        var tangentialOtf = hasComplexOtf ? new Complex[tangentialFrequency.Length] : null;
        var sagittalOtf = hasComplexOtf ? new Complex[sagittalFrequency.Length] : null;
        foreach (var item in results)
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
                tangential[index] = Math.Clamp(tangential[index] / totalWeight, 0, 1);
            }

            for (var index = 0; index < sagittalFrequency.Length; index++)
            {
                sagittalOtf![index] /= totalWeight;
                sagittal[index] = Math.Clamp(sagittal[index] / totalWeight, 0, 1);
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
            results.Min(item => item.Result.CutoffFrequency),
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
        MtfComputationSettings settings)
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
            aimAtStop: resolvedSettings.UseZemaxHuygensSemantics && optic.RayAimingEnabled);
        return AtFrequency(DiffractionEngine.ComputePsfMtf(psf), spatialFrequency);
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
            var mtf = DiffractionEngine.ComputePsfMtfAtFrequencies(psf, spatialFrequencies);
            return Enumerable.Range(0, spatialFrequencies.Count)
                .Select(index => (mtf.Tangential[index], mtf.Sagittal[index]))
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
        return AtFrequency(
            ComputeHuygensPolychromaticMtf(optic, field, wavelengths, settings),
            spatialFrequency);
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
                * Math.Pow(shortestWavelength / wavelength.Micrometers, 2);
            var psf = DiffractionEngine.ComputeHuygensPsf(
                optic,
                field,
                wavelength,
                pupilSampling,
                imageSize,
                pixelPitchMillimeters,
                settings.UsePolarization,
                aimAtStop: optic.RayAimingEnabled);
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
            pixelPitchMillimeters * 1000);
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
        MtfComputationSettings settings)
    {
        var result = SpotAnalysisEngine.Generate(
            optic,
            new[] { field },
            new[] { wavelength },
            Math.Max(2, settings.GeometricRayCount),
            settings.Distribution);
        var rays = result.Fields.FirstOrDefault()?.Wavelengths.FirstOrDefault()?.Rays
            ?? Array.Empty<SpotRayData>();
        var fNumber = Math.Abs(optic.Paraxial.EstimateFNumber());
        var cutoff = fNumber <= 1e-30 ? 0 : 1 / (wavelength.Micrometers * 1e-3 * fNumber);
        var scale = settings.ScaleGeometricByDiffractionLimit
            ? DiffractionScale(spatialFrequency, cutoff)
            : 1.0;
        return (
            GeometricAtFrequency(rays.Select(ray => ray.Y), spatialFrequency) * scale,
            GeometricAtFrequency(rays.Select(ray => ray.X), spatialFrequency) * scale);
    }

    private static (double Tangential, double Sagittal) AtFrequency(MtfResult result, double frequency)
    {
        var tangentialFrequency = result.TangentialFrequency ?? result.Frequency;
        var sagittalFrequency = result.SagittalFrequency ?? result.Frequency;
        return (
            Interpolate(tangentialFrequency, result.Tangential, frequency),
            Interpolate(sagittalFrequency, result.Sagittal, frequency));
    }

    internal static FftMtfDataType ParseDataType(string? value)
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

    internal static string DataTypeName(FftMtfDataType type)
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

    internal static string DataTypeLabel(FftMtfDataType type)
    {
        return type switch
        {
            FftMtfDataType.Real => "Real MTF",
            FftMtfDataType.Imaginary => "Imaginary MTF",
            FftMtfDataType.Phase => "Phase (radians)",
            FftMtfDataType.SquareWave => "Square Wave MTF",
            _ => "MTF"
        };
    }

    private static double Sample(
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

        if (complex is null || type == FftMtfDataType.Modulation)
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

    private static double GeometricAtFrequency(IEnumerable<double> coordinateValues, double frequency)
    {
        var coordinates = coordinateValues.ToArray();
        if (coordinates.Length == 0)
        {
            return 0;
        }

        var center = coordinates.Average();
        var real = coordinates.Average(value => Math.Cos(2 * Math.PI * frequency * (value - center)));
        var imaginary = coordinates.Average(value => Math.Sin(2 * Math.PI * frequency * (value - center)));
        return Math.Clamp(Math.Sqrt((real * real) + (imaginary * imaginary)), 0, 1);
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
    {
        var result = 1;
        while (result < value)
        {
            result <<= 1;
        }

        return result;
    }
}

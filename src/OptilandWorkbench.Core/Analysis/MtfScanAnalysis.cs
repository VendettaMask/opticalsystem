using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Core.Analysis;

public enum MtfComputationMethod
{
    Fourier,
    Huygens,
    Geometric
}

public sealed record MtfComputationSettings(
    int PupilSampling = 32,
    int ImageSize = 64,
    double PixelPitchMillimeters = 0.005,
    int GeometricRayCount = 32,
    string Distribution = "uniform",
    bool ScaleGeometricByDiffractionLimit = true);

public sealed class MtfThroughFocusAnalysis : BaseAnalysis
{
    private readonly MtfComputationMethod _method;
    private readonly double _spatialFrequency;
    private readonly double _focusStep;
    private readonly int _focusPlaneCount;
    private readonly MtfComputationSettings _settings;

    public MtfThroughFocusAnalysis(
        Optic optic,
        MtfComputationMethod method,
        double spatialFrequency = 20,
        double focusStep = 0.1,
        int focusPlaneCount = 5,
        MtfComputationSettings? settings = null) : base(optic)
    {
        _method = method;
        _spatialFrequency = Math.Max(0, spatialFrequency);
        _focusStep = Math.Abs(focusStep);
        _focusPlaneCount = Math.Clamp(focusPlaneCount % 2 == 0 ? focusPlaneCount + 1 : focusPlaneCount, 1, 31);
        _settings = settings ?? new MtfComputationSettings();
    }

    public override string Name => $"{MtfMethodEvaluator.MethodName(_method)} Through Focus MTF";

    public override AnalysisData GenerateData()
    {
        var wavelength = MtfMethodEvaluator.PrimaryWavelength(Optic);
        var imageSurface = Optic.SurfaceGroup.Items.LastOrDefault();
        if (wavelength is null || imageSurface is null)
        {
            return new AnalysisData(Name, new Dictionary<string, object> { ["Status"] = "No optical data" });
        }

        var fields = SpotAnalysisEngine.DefinedFields(Optic);
        var focus = Enumerable.Range(0, _focusPlaneCount)
            .Select(index => (index - (_focusPlaneCount / 2)) * _focusStep)
            .ToArray();
        var tangential = fields.Select(_ => new double[focus.Length]).ToArray();
        var sagittal = fields.Select(_ => new double[focus.Length]).ToArray();
        var originalCoordinateSystem = imageSurface.CoordinateSystem;
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
                for (var fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
                {
                    var value = MtfMethodEvaluator.Evaluate(
                        Optic,
                        _method,
                        fields[fieldIndex],
                        wavelength,
                        _spatialFrequency,
                        _settings);
                    tangential[fieldIndex][focusIndex] = value.Tangential;
                    sagittal[fieldIndex][focusIndex] = value.Sagittal;
                }
            }
        }
        finally
        {
            imageSurface.CoordinateSystem = originalCoordinateSystem;
        }

        var series = new List<AnalysisSeries>(fields.Count * 2);
        for (var fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
        {
            var field = fields[fieldIndex];
            series.Add(new AnalysisSeries(
                "Defocus (mm)",
                "MTF",
                focus.Select((value, index) => new AnalysisPoint(value, tangential[fieldIndex][index])).ToArray(),
                Name: $"Hx: {field.Hx:0.0}, Hy: {field.Hy:0.0}, Tangential",
                ColorIndex: fieldIndex));
            series.Add(new AnalysisSeries(
                "Defocus (mm)",
                "MTF",
                focus.Select((value, index) => new AnalysisPoint(value, sagittal[fieldIndex][index])).ToArray(),
                Name: $"Hx: {field.Hx:0.0}, Hy: {field.Hy:0.0}, Sagittal",
                LineStyle: AnalysisLineStyle.Dashed,
                ColorIndex: fieldIndex));
        }

        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["Method"] = MtfMethodEvaluator.MethodName(_method),
            ["SpatialFrequency"] = _spatialFrequency,
            ["FocusStep"] = _focusStep,
            ["FocusPlaneCount"] = _focusPlaneCount,
            ["WavelengthMicrometers"] = wavelength.Micrometers,
            ["RawTangential"] = tangential,
            ["RawSagittal"] = sagittal
        }, series.FirstOrDefault(), series, new AnalysisPlotOptions(
            Title: $"{MtfMethodEvaluator.MethodName(_method)} Through-Focus MTF at {_spatialFrequency:0.###} cycles/mm",
            XMinimum: focus.FirstOrDefault(),
            XMaximum: focus.LastOrDefault(),
            YMinimum: 0,
            YMaximum: 1.05,
            ShowLegend: true,
            DottedGrid: true,
            GridOpacity: 0.35));
    }
}

public sealed class MtfVsFieldAnalysis : BaseAnalysis
{
    private readonly MtfComputationMethod _method;
    private readonly double _spatialFrequency;
    private readonly int _fieldPointCount;
    private readonly MtfComputationSettings _settings;

    public MtfVsFieldAnalysis(
        Optic optic,
        MtfComputationMethod method,
        double spatialFrequency = 20,
        int fieldPointCount = 21,
        MtfComputationSettings? settings = null) : base(optic)
    {
        _method = method;
        _spatialFrequency = Math.Max(0, spatialFrequency);
        _fieldPointCount = Math.Clamp(fieldPointCount, 2, 101);
        _settings = settings ?? new MtfComputationSettings();
    }

    public override string Name => $"{MtfMethodEvaluator.MethodName(_method)} MTF vs Field";

    public override AnalysisData GenerateData()
    {
        var wavelength = MtfMethodEvaluator.PrimaryWavelength(Optic);
        if (wavelength is null)
        {
            return new AnalysisData(Name, new Dictionary<string, object> { ["Status"] = "No wavelengths" });
        }

        var definedFields = SpotAnalysisEngine.DefinedFields(Optic);
        var endField = definedFields
            .OrderBy(field => (field.Hx * field.Hx) + (field.Hy * field.Hy))
            .LastOrDefault();
        var maximumField = FieldCoordinates.MaximumRadius(Optic.Fields);
        var fieldCoordinates = Enumerable.Range(0, _fieldPointCount)
            .Select(index => maximumField * index / (_fieldPointCount - 1.0))
            .ToArray();
        var tangential = new double[_fieldPointCount];
        var sagittal = new double[_fieldPointCount];
        for (var index = 0; index < _fieldPointCount; index++)
        {
            var fraction = index / (_fieldPointCount - 1.0);
            var field = (endField.Hx * fraction, endField.Hy * fraction);
            var value = MtfMethodEvaluator.Evaluate(
                Optic,
                _method,
                field,
                wavelength,
                _spatialFrequency,
                _settings);
            tangential[index] = value.Tangential;
            sagittal[index] = value.Sagittal;
        }

        var (axisLabel, fieldUnit) = Optic.FieldDefinition switch
        {
            FieldDefinitionKind.Angle => ("Field angle (deg)", "deg"),
            FieldDefinitionKind.ObjectHeight => ("Object height (mm)", "mm"),
            FieldDefinitionKind.ParaxialImageHeight => ("Paraxial image height (mm)", "mm"),
            FieldDefinitionKind.RealImageHeight => ("Real image height (mm)", "mm"),
            _ => ("Field", string.Empty)
        };
        var series = new[]
        {
            new AnalysisSeries(
                axisLabel,
                "MTF",
                fieldCoordinates.Select((value, index) => new AnalysisPoint(value, tangential[index])).ToArray(),
                Name: $"{_spatialFrequency:0.###} cycles/mm, Tangential"),
            new AnalysisSeries(
                axisLabel,
                "MTF",
                fieldCoordinates.Select((value, index) => new AnalysisPoint(value, sagittal[index])).ToArray(),
                Name: $"{_spatialFrequency:0.###} cycles/mm, Sagittal",
                LineStyle: AnalysisLineStyle.Dashed)
        };

        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["Method"] = MtfMethodEvaluator.MethodName(_method),
            ["SpatialFrequency"] = _spatialFrequency,
            ["FieldPointCount"] = _fieldPointCount,
            ["MaximumField"] = maximumField,
            ["FieldUnit"] = fieldUnit,
            ["WavelengthMicrometers"] = wavelength.Micrometers,
            ["Tangential"] = tangential,
            ["Sagittal"] = sagittal
        }, series[0], series, new AnalysisPlotOptions(
            Title: $"{MtfMethodEvaluator.MethodName(_method)} MTF vs Field at {_spatialFrequency:0.###} cycles/mm",
            XMinimum: 0,
            XMaximum: maximumField,
            YMinimum: 0,
            YMaximum: 1.05,
            ShowLegend: true,
            DottedGrid: true,
            GridOpacity: 0.35));
    }
}

internal static class MtfMethodEvaluator
{
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

    private static (double Tangential, double Sagittal) EvaluateFourier(
        Optic optic,
        (double Hx, double Hy) field,
        Wavelength wavelength,
        double spatialFrequency,
        MtfComputationSettings settings)
    {
        var pupilSampling = Math.Max(8, settings.PupilSampling);
        var gridSize = NextPowerOfTwo(Math.Max(pupilSampling, settings.ImageSize));
        var psf = DiffractionEngine.ComputeFftPsf(optic, field, wavelength, pupilSampling, gridSize);
        return AtFrequency(DiffractionEngine.ComputeFftMtf(psf, optic, wavelength), spatialFrequency);
    }

    private static (double Tangential, double Sagittal) EvaluateHuygens(
        Optic optic,
        (double Hx, double Hy) field,
        Wavelength wavelength,
        double spatialFrequency,
        MtfComputationSettings settings)
    {
        var psf = DiffractionEngine.ComputeHuygensPsf(
            optic,
            field,
            wavelength,
            Math.Max(2, settings.PupilSampling),
            Math.Max(4, settings.ImageSize),
            Math.Max(1e-9, settings.PixelPitchMillimeters));
        return AtFrequency(DiffractionEngine.ComputePsfMtf(psf), spatialFrequency);
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
        return (
            Interpolate(result.Frequency, result.Tangential, frequency),
            Interpolate(result.Frequency, result.Sagittal, frequency));
    }

    private static double Interpolate(IReadOnlyList<double> x, IReadOnlyList<double> y, double target)
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

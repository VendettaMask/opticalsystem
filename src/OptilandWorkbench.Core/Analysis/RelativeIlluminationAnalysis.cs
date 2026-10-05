using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Analysis;

public sealed class RelativeIlluminationAnalysis : BaseAnalysis
{
    private readonly int _rayDensity;
    private readonly int _fieldDensity;
    private readonly int _wavelengthNumber;
    private readonly string _scanDirection;
    private readonly bool _removeVignettingFactors;
    private readonly bool _usePolarization;

    public RelativeIlluminationAnalysis(
        Optic optic,
        int rayDensity = 10,
        int fieldDensity = 21,
        int wavelengthNumber = 0,
        string scanDirection = "+y",
        bool removeVignettingFactors = true,
        bool usePolarization = false) : base(optic)
    {
        _rayDensity = Math.Clamp(rayDensity, 5, 128);
        _fieldDensity = Math.Clamp(fieldDensity, 2, 201);
        ArgumentOutOfRangeException.ThrowIfNegative(wavelengthNumber);
        _wavelengthNumber = wavelengthNumber;
        _scanDirection = AnalysisTrace.NormalizeScanDirection(scanDirection);
        _removeVignettingFactors = removeVignettingFactors;
        _usePolarization = usePolarization;
    }

    public override string Name => "Relative Illumination";

    public override AnalysisData GenerateData()
    {
        if (Optic.SurfaceGroup.Items.Count == 0 || Optic.Wavelengths.Count == 0)
        {
            return Status("No optical data");
        }

        var workingOptic = AnalysisTrace.PrepareVignettingFactors(Optic, _removeVignettingFactors);

        var wavelength = SelectWavelength(workingOptic);
        var maximumField = FieldCoordinates.MaximumRadius(workingOptic.Fields);
        var rawIllumination = new double[_fieldDensity];
        var effectiveFNumbers = new double[_fieldDensity];
        var validRays = new int[_fieldDensity];
        var sampledNodes = new int[_fieldDensity];
        var foldedCells = new int[_fieldDensity];
        var fields = Enumerable.Range(0, _fieldDensity)
            .Select(index => AnalysisTrace.ScanField(_scanDirection, index / (_fieldDensity - 1.0))).ToArray();
        var results = IlluminationMetrics.EvaluateFields(workingOptic, fields, wavelength.Micrometers, _rayDensity, _usePolarization);

        for (var index = 0; index < _fieldDensity; index++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var result = results[index];
            rawIllumination[index] = result.ProjectedCosineArea;
            validRays[index] = result.ValidRays;
            sampledNodes[index] = result.SampledPupilNodes;
            // Folded mappings are rejected by the shared integral.
            foldedCells[index] = 0;
            effectiveFNumbers[index] = result.EffectiveFNumber;
        }

        var maximumIllumination = rawIllumination.DefaultIfEmpty(0).Max();
        if (maximumIllumination <= 0) return Status("No transmitted pupil area");
        var points = Enumerable.Range(0, _fieldDensity)
            .Select(index => new AnalysisPoint(
                AnalysisTrace.ScanFieldValue(
                    _scanDirection,
                    maximumField * index / (_fieldDensity - 1.0)),
                maximumIllumination > 0 ? rawIllumination[index] / maximumIllumination : 0))
            .ToArray();
        var fieldAxisLabel = ScanFieldAxisLabel(workingOptic, _scanDirection);
        var fieldUnit = workingOptic.FieldDefinition == FieldDefinitionKind.Angle ? "deg" : "mm";
        var series = new AnalysisSeries(
            fieldAxisLabel,
            "Relative Illumination",
            points,
            Name: $"{wavelength.Micrometers:0.0000} µm",
            XQuantity: AnalysisTrace.FieldAxisQuantity(workingOptic),
            XUnit: AnalysisTrace.FieldAxisUnit(workingOptic),
            YQuantity: AnalysisAxisQuantity.Irradiance,
            YUnit: AnalysisAxisUnit.Dimensionless);

        return new AnalysisData(Name, new Dictionary<string, object>
        {
            [AnalysisTrace.MaximumFieldValueKey(workingOptic)] = maximumField,
            ["FieldUnit"] = fieldUnit,
            ["RayDensity"] = _rayDensity,
            ["FieldDensity"] = _fieldDensity,
            ["WavelengthNumber"] = WavelengthNumber(workingOptic, wavelength),
            ["WavelengthMicrometers"] = wavelength.Micrometers,
            ["ScanDirection"] = _scanDirection,
            ["RemoveVignettingFactors"] = _removeVignettingFactors,
            ["UsePolarization"] = _usePolarization,
            ["PolarizationModel"] = _usePolarization ? "Unpolarized power Jones chain; transparent incident media, scalar and coherent multilayer coatings" : "Scalar ray power",
            ["RawProjectedCosineArea"] = rawIllumination,
            ["RelativeIllumination"] = points.Select(point => point.Y).ToArray(),
            ["EffectiveFNumbers"] = effectiveFNumbers,
            ["ValidRayCounts"] = validRays,
            ["SampledPupilNodeCounts"] = sampledNodes,
            ["FoldedCellCounts"] = foldedCells,
            ["MaximumProjectedCosineArea"] = maximumIllumination,
            ["PupilIntegration"] = "Adaptive full-pupil scalar quadrature"
        }, series, new[] { series }, new AnalysisPlotOptions(
            Title: $"Relative Illumination, λ = {wavelength.Micrometers:0.0000} µm",
            YMinimum: 0,
            YMaximum: 1.05,
            ShowLegend: false,
            DottedGrid: true,
            GridOpacity: 0.35));
    }

    private static string ScanFieldAxisLabel(Optic optic, string scanDirection)
    {
        var axis = scanDirection.EndsWith('x') ? "X" : "Y";
        return optic.FieldDefinition switch
        {
            FieldDefinitionKind.ObjectHeight => $"{axis} Object Height (mm)",
            FieldDefinitionKind.ParaxialImageHeight => $"{axis} Paraxial Image Height (mm)",
            FieldDefinitionKind.RealImageHeight => $"{axis} Real Image Height (mm)",
            _ => $"{axis} Field Angle (deg)"
        };
    }

    internal static double ProjectedCosineArea(
        Optic optic,
        (double Hx, double Hy) normalizedField,
        double wavelengthMicrometers,
        int rayDensity)
    {
        return IlluminationMetrics.Evaluate(optic, normalizedField, wavelengthMicrometers, rayDensity).ProjectedCosineArea;
    }

    private Wavelength SelectWavelength(Optic optic)
    {
        if (_wavelengthNumber > 0)
        {
            if (_wavelengthNumber > optic.Wavelengths.Count)
                throw new AnalysisDataUnavailableException(Name, "the wavelength number is outside the optical system");
            return optic.Wavelengths[_wavelengthNumber - 1];
        }

        return optic.Wavelengths.FirstOrDefault(item => item.IsPrimary) ?? optic.Wavelengths[0];
    }

    private static int WavelengthNumber(Optic optic, Wavelength wavelength)
    {
        for (var index = 0; index < optic.Wavelengths.Count; index++)
        {
            if (ReferenceEquals(optic.Wavelengths[index], wavelength))
            {
                return index + 1;
            }
        }

        return 1;
    }

    private AnalysisData Status(string message) => AnalysisData.Unavailable(Name, message);
}

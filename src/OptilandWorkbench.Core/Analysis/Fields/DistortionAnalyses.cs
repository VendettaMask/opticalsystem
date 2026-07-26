using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Analysis;

public sealed class DistortionAnalysis : BaseAnalysis
{
    private readonly int _numPoints;
    private readonly string _distortionType;
    private readonly int _wavelengthNumber;
    private readonly string _scanDirection;
    private readonly string _displayMode;
    private readonly int _referenceFieldNumber;
    private readonly bool _ignoreVignettingFactors;
    private readonly double _maximumDistortion;

    public DistortionAnalysis(
        Optic optic,
        int numPoints = 128,
        string distortionType = "f-tan",
        int wavelengthNumber = 0,
        string scanDirection = "+y",
        string displayMode = "percent",
        int referenceFieldNumber = 1,
        bool ignoreVignettingFactors = true,
        double maximumDistortion = 0) : base(optic)
    {
        _numPoints = Math.Max(2, numPoints);
        _distortionType = AnalysisTrace.NormalizeDistortionType(distortionType);
        _wavelengthNumber = Math.Max(0, wavelengthNumber);
        _scanDirection = AnalysisTrace.NormalizeScanDirection(scanDirection);
        _displayMode = AnalysisTrace.NormalizeDistortionDisplayMode(displayMode);
        _referenceFieldNumber = Math.Max(1, referenceFieldNumber);
        _ignoreVignettingFactors = ignoreVignettingFactors;
        _maximumDistortion = Math.Max(0, maximumDistortion);
    }

    public override string Name => "Distortion";

    public override AnalysisData GenerateData()
    {
        if (Optic.FieldDefinition == FieldDefinitionKind.RealImageHeight)
        {
            var converted = RealImageFieldConversion.ForDistortion(Optic);
            return new DistortionAnalysis(
                converted,
                _numPoints,
                _distortionType,
                _wavelengthNumber,
                _scanDirection,
                _displayMode,
                _referenceFieldNumber,
                _ignoreVignettingFactors,
                _maximumDistortion).GenerateData();
        }

        var maxField = AnalysisTrace.MaxFieldValue(Optic);
        var fieldAxisLabel = AnalysisTrace.FieldAxisLabel(Optic);
        var fieldValueKey = AnalysisTrace.MaximumFieldValueKey(Optic);
        var effectiveDistortionType = Optic.FieldDefinition == FieldDefinitionKind.Angle
            ? _distortionType
            : "linear-height";
        var wavelengths = AnalysisTrace.SelectWavelengths(Optic, _wavelengthNumber);
        var fields = AnalysisTrace.DefinedFieldSamples(Optic);
        var series = new List<AnalysisSeries>();
        var maximumAbsoluteDistortion = 0.0;

        for (var wavelengthIndex = 0; wavelengthIndex < wavelengths.Length; wavelengthIndex++)
        {
            var wavelength = wavelengths[wavelengthIndex];
            var mapping = AnalysisTrace.BuildDistortionReferenceMapping(
                Optic,
                wavelength.Micrometers,
                _referenceFieldNumber,
                _distortionType);
            var points = new AnalysisPoint[fields.Count];

            for (var index = 0; index < fields.Count; index++)
            {
                var field = fields[index];
                var linearField = AnalysisTrace.ToDistortionLinearField(Optic, field.X, field.Y, _distortionType);
                var actualImage = AnalysisTrace.TraceChiefAtLinearField(
                    Optic,
                    linearField.X,
                    linearField.Y,
                    wavelength.Micrometers,
                    _distortionType);
                var actualX = actualImage.X - mapping.ReferenceImageX;
                var actualY = actualImage.Y - mapping.ReferenceImageY;
                var predicted = mapping.MapFromReference(linearField.X, linearField.Y);
                var actualRadius = Math.Sqrt((actualX * actualX) + (actualY * actualY));
                var predictedRadius = Math.Sqrt((predicted.X * predicted.X) + (predicted.Y * predicted.Y));
                var distortion = predictedRadius <= 1e-30
                    ? 0
                    : _displayMode == "absolute"
                        ? actualRadius - predictedRadius
                        : 100.0 * (actualRadius - predictedRadius) / predictedRadius;
                maximumAbsoluteDistortion = Math.Max(maximumAbsoluteDistortion, Math.Abs(distortion));
                points[index] = new AnalysisPoint(distortion, field.Coordinate, field.Label);
            }

            series.Add(new AnalysisSeries(
                _displayMode == "absolute" ? "Distortion (mm)" : "Distortion (%)",
                fieldAxisLabel,
                points,
                Name: $"{wavelength.Micrometers:0.0000} \u00B5m",
                ColorIndex: wavelengthIndex));
        }

        var first = series.FirstOrDefault();
        var values = new Dictionary<string, object>
        {
            [fieldValueKey] = maxField,
            ["DistortionType"] = effectiveDistortionType,
            ["DisplayMode"] = _displayMode,
            ["ScanDirection"] = "defined-fields",
            ["ReferenceFieldNumber"] = _referenceFieldNumber,
            ["WavelengthNumber"] = _wavelengthNumber,
            ["IgnoreVignettingFactors"] = _ignoreVignettingFactors,
            ["Samples"] = fields.Count,
            ["WavelengthCount"] = wavelengths.Length,
            [_displayMode == "absolute" ? "MaximumAbsoluteDistortionMillimeters" : "MaximumAbsoluteDistortionPercent"] = maximumAbsoluteDistortion
        };
        var minimumField = fields.Select(field => field.Coordinate).DefaultIfEmpty(0).Min();
        var maximumDefinedField = fields.Select(field => field.Coordinate).DefaultIfEmpty(0).Max();
        return new AnalysisData(Name, values, first, series, new AnalysisPlotOptions(
            SymmetricX: true,
            ShowVerticalZeroLine: true,
            VerticalZeroLineStyle: AnalysisLineStyle.Dashed,
            VerticalZeroLineWidth: 1,
            XMinimum: _maximumDistortion > 0 ? -_maximumDistortion : null,
            XMaximum: _maximumDistortion > 0 ? _maximumDistortion : null,
            YMinimum: minimumField,
            YMaximum: maximumDefinedField,
            ShowLegend: true));
    }
}

public sealed class FieldCurvatureAnalysis : BaseAnalysis
{
    private readonly int _numPoints;
    private readonly double _parabasalDelta;

    public FieldCurvatureAnalysis(Optic optic, int numPoints = 128, double parabasalDelta = 1e-5) : base(optic)
    {
        _numPoints = Math.Max(2, numPoints);
        _parabasalDelta = Math.Abs(parabasalDelta) <= 1e-12 ? 1e-5 : Math.Abs(parabasalDelta);
    }

    public override string Name => "Field Curvature";

    public override AnalysisData GenerateData()
    {
        var maxField = AnalysisTrace.MaxFieldValue(Optic);
        var fieldAxisLabel = AnalysisTrace.FieldAxisLabel(Optic);
        var fieldValueKey = AnalysisTrace.MaximumFieldValueKey(Optic);
        var fields = AnalysisTrace.DefinedFieldSamples(Optic);
        var wavelengths = Optic.Wavelengths.ToArray();
        var series = new List<AnalysisSeries>();
        var maximumAbsoluteDelta = 0.0;
        var maximumTangentialFieldCurvature = 0.0;
        var maximumSagittalFieldCurvature = 0.0;

        for (var wavelengthIndex = 0; wavelengthIndex < wavelengths.Length; wavelengthIndex++)
        {
            var wavelength = wavelengths[wavelengthIndex];
            var tangential = new AnalysisPoint[fields.Count];
            var sagittal = new AnalysisPoint[fields.Count];

            for (var index = 0; index < fields.Count; index++)
            {
                var field = fields[index];
                var t1 = AnalysisTrace.FinalSample(Optic, field.Hx, field.Hy, 0, -_parabasalDelta, wavelength.Micrometers);
                var t2 = AnalysisTrace.FinalSample(Optic, field.Hx, field.Hy, 0, _parabasalDelta, wavelength.Micrometers);
                var tDenominator = (t1.Direction.Y * t2.Direction.Z) - (t2.Direction.Y * t1.Direction.Z);
                var tangentialDelta = Math.Abs(tDenominator) <= 1e-30
                    ? 0
                    : ((t2.Direction.Y * t1.Position.Z)
                        - (t2.Direction.Y * t2.Position.Z)
                        - (t2.Direction.Z * t1.Position.Y)
                        + (t2.Direction.Z * t2.Position.Y)) / tDenominator * t1.Direction.Z;

                var s1 = AnalysisTrace.FinalSample(Optic, field.Hx, field.Hy, -_parabasalDelta, 0, wavelength.Micrometers);
                var s2 = AnalysisTrace.FinalSample(Optic, field.Hx, field.Hy, _parabasalDelta, 0, wavelength.Micrometers);
                var sDenominator = (s1.Direction.X * s2.Direction.Z) - (s2.Direction.X * s1.Direction.Z);
                var sagittalDelta = Math.Abs(sDenominator) <= 1e-30
                    ? 0
                    : ((s2.Direction.X * s1.Position.Z)
                        - (s2.Direction.X * s2.Position.Z)
                        - (s2.Direction.Z * s1.Position.X)
                        + (s2.Direction.Z * s2.Position.X)) / sDenominator * s1.Direction.Z;

                tangential[index] = new AnalysisPoint(tangentialDelta, field.Coordinate, field.Label);
                sagittal[index] = new AnalysisPoint(sagittalDelta, field.Coordinate, field.Label);
                maximumAbsoluteDelta = Math.Max(maximumAbsoluteDelta, Math.Max(Math.Abs(tangentialDelta), Math.Abs(sagittalDelta)));
                if (Math.Abs(tangentialDelta) > Math.Abs(maximumTangentialFieldCurvature))
                {
                    maximumTangentialFieldCurvature = tangentialDelta;
                }

                if (Math.Abs(sagittalDelta) > Math.Abs(maximumSagittalFieldCurvature))
                {
                    maximumSagittalFieldCurvature = sagittalDelta;
                }
            }

            var wavelengthLabel = $"{wavelength.Micrometers:0.0000} \u00B5m";
            series.Add(new AnalysisSeries(
                "Image Plane Delta (mm)",
                fieldAxisLabel,
                tangential,
                Name: $"{wavelengthLabel}, Tangential",
                ColorIndex: wavelengthIndex));
            series.Add(new AnalysisSeries(
                "Image Plane Delta (mm)",
                fieldAxisLabel,
                sagittal,
                Name: $"{wavelengthLabel}, Sagittal",
                LineStyle: AnalysisLineStyle.Dashed,
                ColorIndex: wavelengthIndex));
        }

        var first = series.FirstOrDefault();
        var values = new Dictionary<string, object>
        {
            [fieldValueKey] = maxField,
            ["Samples"] = fields.Count,
            ["ParabasalDelta"] = _parabasalDelta,
            ["WavelengthCount"] = wavelengths.Length,
            ["MaximumTangentialFieldCurvatureMillimeters"] = maximumTangentialFieldCurvature,
            ["MaximumSagittalFieldCurvatureMillimeters"] = maximumSagittalFieldCurvature,
            ["MaximumAbsoluteImagePlaneDelta"] = maximumAbsoluteDelta
        };
        return new AnalysisData(Name, values, first, series, new AnalysisPlotOptions(
            Title: "Field Curvature",
            SymmetricX: true,
            ShowVerticalZeroLine: true,
            YMinimum: fields.Select(field => field.Coordinate).DefaultIfEmpty(0).Min(),
            YMaximum: fields.Select(field => field.Coordinate).DefaultIfEmpty(0).Max(),
            ShowLegend: true));
    }
}

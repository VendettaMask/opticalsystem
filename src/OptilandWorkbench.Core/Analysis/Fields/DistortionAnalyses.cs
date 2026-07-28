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
        if (_distortionType == "smia-tv" && wavelengths.Length > 0)
        {
            values["SmiaTvDistortionPercent"] = ComputeSmiaTvDistortion(Optic, wavelengths[0]);
        }

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

    private static double ComputeSmiaTvDistortion(Optic optic, Wavelength wavelength)
    {
        if (optic.Fields.Count == 0)
        {
            return 0;
        }

        var maxX = optic.Fields.Select(field => Math.Abs(field.X)).DefaultIfEmpty(0).Max();
        var maxY = optic.Fields.Select(field => Math.Abs(field.Y)).DefaultIfEmpty(0).Max();
        var maximum = FieldCoordinates.MaximumRadius(optic.Fields);
        if (maxX <= 1e-12)
        {
            maxX = maximum;
        }

        if (maxY <= 1e-12)
        {
            maxY = maximum;
        }

        if (maxX <= 1e-12 || maxY <= 1e-12)
        {
            return 0;
        }

        try
        {
            var leftTop = TraceChief(optic, -maxX, maxY, wavelength);
            var leftBottom = TraceChief(optic, -maxX, -maxY, wavelength);
            var rightTop = TraceChief(optic, maxX, maxY, wavelength);
            var rightBottom = TraceChief(optic, maxX, -maxY, wavelength);
            var centerTop = TraceChief(optic, 0, maxY, wavelength);
            var centerBottom = TraceChief(optic, 0, -maxY, wavelength);
            var a1 = Distance(leftTop, leftBottom);
            var a2 = Distance(rightTop, rightBottom);
            var b = Distance(centerTop, centerBottom);
            return b <= 1e-30 ? 0 : 100.0 * ((((a1 + a2) / 2.0) - b) / b);
        }
        catch (InvalidOperationException)
        {
            return 0;
        }
    }

    private static (double X, double Y) TraceChief(Optic optic, double fieldX, double fieldY, Wavelength wavelength)
    {
        var normalized = FieldCoordinates.Normalize(optic.Fields, fieldX, fieldY);
        var sample = AnalysisTrace.FinalSample(
            optic,
            normalized.X,
            normalized.Y,
            0,
            0,
            wavelength.Micrometers);
        return (sample.Position.X, sample.Position.Y);
    }

    private static double Distance((double X, double Y) first, (double X, double Y) second)
    {
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}

public sealed class FieldCurvatureAndDistortionAnalysis : BaseAnalysis
{
    private readonly FieldCurvatureAnalysis _fieldCurvature;
    private readonly DistortionAnalysis _distortion;

    public FieldCurvatureAndDistortionAnalysis(
        Optic optic,
        int numPoints = 128,
        double parabasalDelta = 1e-5,
        double maximumCurvature = 0,
        string distortionType = "f-tan",
        int wavelengthNumber = 0,
        string scanDirection = "+y",
        string displayMode = "percent",
        int referenceFieldNumber = 1,
        bool ignoreVignettingFactors = true,
        double maximumDistortion = 0) : base(optic)
    {
        _fieldCurvature = new FieldCurvatureAnalysis(optic, numPoints, parabasalDelta, maximumCurvature);
        _distortion = new DistortionAnalysis(
            optic,
            numPoints,
            distortionType,
            wavelengthNumber,
            scanDirection,
            displayMode,
            referenceFieldNumber,
            ignoreVignettingFactors,
            maximumDistortion);
    }

    public override string Name => "Field Curvature and Distortion";

    public override AnalysisData GenerateData()
    {
        var curvature = _fieldCurvature.GenerateData();
        var distortion = _distortion.GenerateData();
        var values = new Dictionary<string, object>();
        foreach (var item in curvature.Values)
        {
            values[$"FieldCurvature.{item.Key}"] = item.Value;
        }

        foreach (var item in distortion.Values)
        {
            values[$"Distortion.{item.Key}"] = item.Value;
        }

        var panes = new[]
        {
            new AnalysisPlotPane(
                "Field Curvature",
                curvature.PlotSeries,
                curvature.PlotOptions ?? new AnalysisPlotOptions(Title: "Field Curvature")),
            new AnalysisPlotPane(
                "Distortion",
                distortion.PlotSeries,
                distortion.PlotOptions ?? new AnalysisPlotOptions(Title: "Distortion"))
        };
        return new AnalysisData(
            Name,
            values,
            curvature.Series,
            curvature.PlotSeries,
            new AnalysisPlotOptions(Title: Name),
            panes,
            PlotPaneColumns: 2);
    }
}

public sealed class FieldCurvatureAnalysis : BaseAnalysis
{
    private readonly int _numPoints;
    private readonly double _parabasalDelta;
    private readonly double _maximumCurvature;

    public FieldCurvatureAnalysis(
        Optic optic,
        int numPoints = 128,
        double parabasalDelta = 1e-5,
        double maximumCurvature = 0) : base(optic)
    {
        _numPoints = Math.Max(2, numPoints);
        _parabasalDelta = Math.Abs(parabasalDelta) <= 1e-12 ? 1e-5 : Math.Abs(parabasalDelta);
        _maximumCurvature = Math.Max(0, maximumCurvature);
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
            ["MaximumCurvatureScale"] = _maximumCurvature,
            ["WavelengthCount"] = wavelengths.Length,
            ["MaximumTangentialFieldCurvatureMillimeters"] = maximumTangentialFieldCurvature,
            ["MaximumSagittalFieldCurvatureMillimeters"] = maximumSagittalFieldCurvature,
            ["MaximumAbsoluteImagePlaneDelta"] = maximumAbsoluteDelta
        };
        return new AnalysisData(Name, values, first, series, new AnalysisPlotOptions(
            Title: "Field Curvature",
            SymmetricX: true,
            ShowVerticalZeroLine: true,
            XMinimum: _maximumCurvature > 0 ? -_maximumCurvature : null,
            XMaximum: _maximumCurvature > 0 ? _maximumCurvature : null,
            YMinimum: fields.Select(field => field.Coordinate).DefaultIfEmpty(0).Min(),
            YMaximum: fields.Select(field => field.Coordinate).DefaultIfEmpty(0).Max(),
            ShowLegend: true));
    }
}

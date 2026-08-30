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
        var calculationOptic = Optic;
        Optic? displayOptic = null;
        if (Optic.FieldDefinition == FieldDefinitionKind.RealImageHeight)
        {
            calculationOptic = RealImageFieldConversion.ForDistortion(Optic);
            displayOptic = Optic;
        }

        var workingOptic = AnalysisTrace.PrepareVignettingFactors(calculationOptic, _ignoreVignettingFactors);
        var displayReference = displayOptic ?? workingOptic;
        var maxField = AnalysisTrace.MaxFieldValue(workingOptic);
        var fieldAxisLabel = AnalysisTrace.FieldAxisLabel(displayReference);
        var fieldValueKey = AnalysisTrace.MaximumFieldValueKey(workingOptic);
        var effectiveDistortionType = workingOptic.FieldDefinition == FieldDefinitionKind.Angle
            ? _distortionType
            : "linear-height";
        var wavelengths = AnalysisTrace.SelectWavelengths(workingOptic, _wavelengthNumber);
        var fields = AnalysisTrace.ScanFieldSamples(workingOptic, _scanDirection, _numPoints);
        var displayFields = displayOptic is null
            ? fields
            : AnalysisTrace.ScanFieldSamples(displayReference, _scanDirection, _numPoints);
        var series = new List<AnalysisSeries>();
        var maximumAbsoluteDistortion = 0.0;

        for (var wavelengthIndex = 0; wavelengthIndex < wavelengths.Length; wavelengthIndex++)
        {
            var wavelength = wavelengths[wavelengthIndex];
            DistortionReferenceMapping mapping;
            try
            {
                mapping = AnalysisTrace.BuildDistortionReferenceMapping(
                    workingOptic,
                    wavelength.Micrometers,
                    _referenceFieldNumber,
                    _distortionType);
            }
            catch (InvalidOperationException exception) when (_distortionType == "smia-tv")
            {
                throw new AnalysisDataUnavailableException(
                    "SMIA-TV distortion",
                    $"reference calibration failed: {exception.Message}");
            }
            var points = new AnalysisPoint[fields.Count];

            for (var index = 0; index < fields.Count; index++)
            {
                var field = fields[index];
                var linearField = AnalysisTrace.ToDistortionLinearField(workingOptic, field.X, field.Y, _distortionType);
                var actualImage = AnalysisTrace.TraceChiefAtLinearField(
                    workingOptic,
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
                var displayField = displayFields[index];
                maximumAbsoluteDistortion = Math.Max(maximumAbsoluteDistortion, Math.Abs(distortion));
                points[index] = new AnalysisPoint(distortion, displayField.Coordinate, displayField.Label);
            }

            series.Add(new AnalysisSeries(
                _displayMode == "absolute" ? "Distortion (mm)" : "Distortion (%)",
                fieldAxisLabel,
                points,
                Name: $"{wavelength.Micrometers:0.0000} \u00B5m",
                ColorIndex: wavelengthIndex,
                XQuantity: AnalysisAxisQuantity.Distortion,
                XUnit: _displayMode == "absolute" ? AnalysisAxisUnit.Millimeter : AnalysisAxisUnit.Percent,
                YQuantity: AnalysisTrace.FieldAxisQuantity(displayReference),
                YUnit: AnalysisTrace.FieldAxisUnit(displayReference)));
        }

        var first = series.FirstOrDefault();
        var values = new Dictionary<string, object>
        {
            [fieldValueKey] = maxField,
            ["DistortionType"] = effectiveDistortionType,
            ["DisplayMode"] = _displayMode,
            ["ScanDirection"] = _scanDirection,
            ["ReferenceFieldNumber"] = _referenceFieldNumber,
            ["WavelengthNumber"] = _wavelengthNumber,
            ["IgnoreVignettingFactors"] = _ignoreVignettingFactors,
            ["VignettingFactorsApplied"] = !_ignoreVignettingFactors,
            ["Applicability"] = "Strictly valid for rotationally symmetric systems with plane object and image surfaces; generalized otherwise.",
            ["Samples"] = fields.Count,
            ["WavelengthCount"] = wavelengths.Length,
            [_displayMode == "absolute" ? "MaximumAbsoluteDistortionMillimeters" : "MaximumAbsoluteDistortionPercent"] = maximumAbsoluteDistortion
        };
        if (_distortionType == "smia-tv" && wavelengths.Length > 0)
        {
            values["SmiaTvDistortionPercent"] = ComputeSmiaTvDistortion(workingOptic, wavelengths[0]);
        }

        var minimumField = displayFields.Select(field => field.Coordinate).DefaultIfEmpty(0).Min();
        var maximumDefinedField = displayFields.Select(field => field.Coordinate).DefaultIfEmpty(0).Max();
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
            throw new AnalysisDataUnavailableException("SMIA-TV distortion", "the optical system has no fields");
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
            throw new AnalysisDataUnavailableException(
                "SMIA-TV distortion",
                "the defined fields do not span both image axes");
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
            if (b <= 1e-30 || !double.IsFinite(b))
            {
                throw new AnalysisDataUnavailableException(
                    "SMIA-TV distortion",
                    "the center-edge image height is zero or non-finite");
            }

            return 100.0 * ((((a1 + a2) / 2.0) - b) / b);
        }
        catch (AnalysisDataUnavailableException)
        {
            throw;
        }
        catch (InvalidOperationException exception)
        {
            throw new AnalysisDataUnavailableException(
                "SMIA-TV distortion",
                $"chief-ray tracing failed: {exception.Message}");
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
        _fieldCurvature = new FieldCurvatureAnalysis(
            optic,
            numPoints,
            parabasalDelta,
            maximumCurvature,
            wavelengthNumber,
            scanDirection,
            ignoreVignettingFactors);
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
    private readonly int _wavelengthNumber;
    private readonly string _scanDirection;
    private readonly bool _ignoreVignettingFactors;

    public FieldCurvatureAnalysis(
        Optic optic,
        int numPoints = 128,
        double parabasalDelta = 1e-5,
        double maximumCurvature = 0,
        int wavelengthNumber = 0,
        string scanDirection = "+y",
        bool ignoreVignettingFactors = true) : base(optic)
    {
        _numPoints = Math.Max(2, numPoints);
        _parabasalDelta = Math.Abs(parabasalDelta) <= 1e-12 ? 1e-5 : Math.Abs(parabasalDelta);
        _maximumCurvature = Math.Max(0, maximumCurvature);
        _wavelengthNumber = Math.Max(0, wavelengthNumber);
        _scanDirection = AnalysisTrace.NormalizeScanDirection(scanDirection);
        _ignoreVignettingFactors = ignoreVignettingFactors;
    }

    public override string Name => "Field Curvature";

    public override AnalysisData GenerateData()
    {
        var workingOptic = AnalysisTrace.PrepareVignettingFactors(Optic, _ignoreVignettingFactors);
        var maxField = AnalysisTrace.MaxFieldValue(workingOptic);
        var fieldAxisLabel = AnalysisTrace.FieldAxisLabel(workingOptic);
        var fieldValueKey = AnalysisTrace.MaximumFieldValueKey(workingOptic);
        var fields = AnalysisTrace.ScanFieldSamples(workingOptic, _scanDirection, _numPoints);
        var wavelengths = AnalysisTrace.SelectWavelengths(workingOptic, _wavelengthNumber);
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
                var scanUsesX = _scanDirection.EndsWith('x');
                var tangentialDelta = ParabasalImagePlaneDelta(
                    workingOptic, field, wavelength.Micrometers, _parabasalDelta, scanUsesX);
                var sagittalDelta = ParabasalImagePlaneDelta(
                    workingOptic, field, wavelength.Micrometers, _parabasalDelta, !scanUsesX);

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
                ColorIndex: wavelengthIndex,
                XQuantity: AnalysisAxisQuantity.Defocus,
                XUnit: AnalysisAxisUnit.Millimeter,
                YQuantity: AnalysisTrace.FieldAxisQuantity(workingOptic),
                YUnit: AnalysisTrace.FieldAxisUnit(workingOptic)));
            series.Add(new AnalysisSeries(
                "Image Plane Delta (mm)",
                fieldAxisLabel,
                sagittal,
                Name: $"{wavelengthLabel}, Sagittal",
                LineStyle: AnalysisLineStyle.Dashed,
                ColorIndex: wavelengthIndex,
                XQuantity: AnalysisAxisQuantity.Defocus,
                XUnit: AnalysisAxisUnit.Millimeter,
                YQuantity: AnalysisTrace.FieldAxisQuantity(workingOptic),
                YUnit: AnalysisTrace.FieldAxisUnit(workingOptic)));
        }

        var first = series.FirstOrDefault();
        var values = new Dictionary<string, object>
        {
            [fieldValueKey] = maxField,
            ["Samples"] = fields.Count,
            ["ParabasalDelta"] = _parabasalDelta,
            ["MaximumCurvatureScale"] = _maximumCurvature,
            ["WavelengthCount"] = wavelengths.Length,
            ["WavelengthNumber"] = _wavelengthNumber,
            ["ScanDirection"] = _scanDirection,
            ["IgnoreVignettingFactors"] = _ignoreVignettingFactors,
            ["VignettingFactorsApplied"] = !_ignoreVignettingFactors,
            ["Applicability"] = "Strictly valid for rotationally symmetric systems with plane object and image surfaces; generalized otherwise.",
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

    private static double ParabasalImagePlaneDelta(
        Optic optic,
        AnalysisFieldSample field,
        double wavelengthMicrometers,
        double pupilDelta,
        bool xAxis)
    {
        var first = AnalysisTrace.FinalSample(
            optic,
            field.Hx,
            field.Hy,
            xAxis ? -pupilDelta : 0,
            xAxis ? 0 : -pupilDelta,
            wavelengthMicrometers);
        var second = AnalysisTrace.FinalSample(
            optic,
            field.Hx,
            field.Hy,
            xAxis ? pupilDelta : 0,
            xAxis ? 0 : pupilDelta,
            wavelengthMicrometers);
        var firstDirection = xAxis ? first.Direction.X : first.Direction.Y;
        var secondDirection = xAxis ? second.Direction.X : second.Direction.Y;
        var firstPosition = xAxis ? first.Position.X : first.Position.Y;
        var secondPosition = xAxis ? second.Position.X : second.Position.Y;
        var denominator = (firstDirection * second.Direction.Z)
            - (secondDirection * first.Direction.Z);
        return Math.Abs(denominator) <= 1e-30
            ? 0
            : ((secondDirection * first.Position.Z)
                - (secondDirection * second.Position.Z)
                - (second.Direction.Z * firstPosition)
                + (second.Direction.Z * secondPosition)) / denominator * first.Direction.Z;
    }
}

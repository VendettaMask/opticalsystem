using System.Globalization;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis;

public enum SpotDiagramVariant
{
    FullField,
    Matrix,
    ConfigurationMatrix
}

public sealed class SpotDiagramVariantAnalysis : BaseAnalysis
{
    private readonly SpotDiagramVariant _variant;
    private readonly SpotDiagramSettings _settings;
    private readonly IReadOnlyList<Optic> _configurations;

    public SpotDiagramVariantAnalysis(
        Optic optic,
        SpotDiagramVariant variant,
        SpotDiagramSettings? settings = null,
        IReadOnlyList<Optic>? configurations = null) : base(optic)
    {
        _variant = variant;
        _settings = settings ?? new SpotDiagramSettings();
        _configurations = configurations?.ToArray() ?? [optic];
        if (_configurations.Count == 0)
            throw new ArgumentException("At least one optical configuration is required.", nameof(configurations));
    }

    public override string Name => _variant switch
    {
        SpotDiagramVariant.FullField => "Full Field Spot Diagram",
        SpotDiagramVariant.Matrix => "Matrix Spot Diagram",
        _ => "Configuration Matrix Spot Diagram"
    };

    public override AnalysisData GenerateData()
    {
        if (_variant == SpotDiagramVariant.FullField)
        {
            return BuildFullField();
        }
        if (_variant == SpotDiagramVariant.ConfigurationMatrix) return BuildConfigurationMatrix();

        var source = new SpotDiagramAnalysis(Optic, _settings).GenerateData();
        var values = source.Values
            .Concat(new[] { new KeyValuePair<string, object>("Variant", Name) })
            .ToDictionary(item => item.Key, item => item.Value);
        return BuildMatrix(source, values);
    }

    private AnalysisData BuildConfigurationMatrix()
    {
        var sources = _configurations.Select((configuration, index) =>
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            if (_settings.FieldNumber > Math.Max(1, configuration.Fields.Count))
                throw new ArgumentException($"结构 {index + 1} 未定义所选视场 {_settings.FieldNumber}，请选择所有视场或有效视场。");
            if (_settings.WavelengthNumber > Math.Max(1, configuration.Wavelengths.Count))
                throw new ArgumentException($"结构 {index + 1} 未定义所选波长 {_settings.WavelengthNumber}，请选择所有波长或有效波长。");
            return new SpotDiagramAnalysis(configuration, _settings).GenerateData();
        }).ToArray();
        var sourcePanes = sources.Select(source => source.PlotPanes ?? []).ToArray();
        var fieldCount = sourcePanes.Max(panes => panes.Count);
        var allPanes = sourcePanes.SelectMany(panes => panes).ToArray();
        var units = allPanes.SelectMany(pane => pane.Series).Select(series => (series.XUnit, series.YUnit)).Distinct().ToArray();
        // A single scale makes configuration columns directly comparable. Keep
        // differently typed coordinates separate rather than comparing units.
        var sharedLimit = units.Length == 1
            ? allPanes.SelectMany(pane => new[] { pane.PlotOptions.XMinimum, pane.PlotOptions.XMaximum,
                    pane.PlotOptions.YMinimum, pane.PlotOptions.YMaximum })
                .Where(value => value.HasValue && double.IsFinite(value.Value))
                .Select(value => Math.Abs(value!.Value)).DefaultIfEmpty(0).Max()
            : 0;
        var panes = new List<AnalysisPlotPane>();
        for (var fieldIndex = 0; fieldIndex < fieldCount; fieldIndex++)
        {
            for (var configurationIndex = 0; configurationIndex < sources.Length; configurationIndex++)
            {
                var fieldNumber = _settings.FieldNumber > 0 ? _settings.FieldNumber : fieldIndex + 1;
                var source = sourcePanes[configurationIndex].ElementAtOrDefault(fieldIndex);
                var fieldLabel = source?.Title ?? $"视场 {fieldNumber}";
                var title = $"结构 {configurationIndex + 1} · {fieldLabel}";
                source ??= new AnalysisPlotPane(fieldLabel, [], new AnalysisPlotOptions(), Footer: "此结构未定义该视场");
                var options = source.PlotOptions with { Title = title, HideTickLabels = true };
                if (sharedLimit > 0)
                    options = options with { XMinimum = -sharedLimit, XMaximum = sharedLimit, YMinimum = -sharedLimit, YMaximum = sharedLimit };
                panes.Add(source with
                {
                    Title = title,
                    PlotOptions = options,
                    MatrixCell = new AnalysisMatrixCell(configurationIndex + 1, fieldNumber, fieldLabel)
                });
            }
        }
        var values = sources[0].Values.ToDictionary(item => item.Key, item => item.Value);
        values["Variant"] = Name;
        values["ConfigurationCount"] = sources.Length;
        values["FieldCount"] = fieldCount;
        values["WavelengthCount"] = allPanes.Select(pane => pane.Series.Count(series => series.Kind == AnalysisSeriesKind.Scatter)).DefaultIfEmpty(0).Max();
        values["RayCount"] = sources.Sum(source => (int)source.Values["RayCount"]);
        values["VignettedRayCount"] = sources.Sum(source => (int)source.Values["VignettedRayCount"]);
        values["MaximumGeometricSpotRadius"] = sources.Max(source => (double)source.Values["MaximumGeometricSpotRadius"]);
        var firstSeries = panes.FirstOrDefault()?.Series.FirstOrDefault();
        return new AnalysisData(Name, values, firstSeries, firstSeries is null ? null : [firstSeries],
            PlotPanes: panes, PlotPaneColumns: sources.Length);
    }

    private AnalysisData BuildFullField()
    {
        var allFields = SpotAnalysisEngine.DefinedFields(Optic);
        var fieldIndices = _settings.FieldNumber <= 0
            ? Enumerable.Range(0, allFields.Count).ToArray()
            : new[]
            {
                Math.Clamp(_settings.FieldNumber - 1, 0, Math.Max(0, allFields.Count - 1))
            };
        var fields = fieldIndices.Select(index => allFields[index]).ToArray();
        var wavelengths = AnalysisTrace.SelectWavelengths(Optic, _settings.WavelengthNumber);
        var absolute = SpotAnalysisEngine.Generate(
            Optic,
            fields,
            wavelengths,
            _settings.RayDensity,
            _settings.Pattern,
            surfaceNumber: _settings.SurfaceNumber,
            reference: "absolute",
            usePolarization: _settings.UsePolarization);
        var referenced = SpotAnalysisEngine.Generate(
            Optic,
            fields,
            wavelengths,
            _settings.RayDensity,
            _settings.Pattern,
            surfaceNumber: _settings.SurfaceNumber,
            reference: _settings.Reference,
            usePolarization: _settings.UsePolarization);
        var imageSpace = ImageSpaceAnalysisSupport.CoordinateDescriptor(
            Optic,
            _settings.SurfaceNumber);
        var displayScale = imageSpace.IsAfocalAngle ? 1.0 : 1_000.0;
        var displayUnitLabel = imageSpace.IsAfocalAngle
            ? ImageSpaceAnalysisSupport.MilliradianLabel
            : "µm";
        var displayAxisUnit = imageSpace.IsAfocalAngle
            ? AnalysisAxisUnit.Milliradian
            : AnalysisAxisUnit.Micrometer;
        var magnification = double.IsFinite(_settings.Magnification)
            ? Math.Max(0, _settings.Magnification)
            : 1;
        AnalysisPoint[] BuildPoints(int fieldIndex, int wavelengthIndex)
        {
            var absoluteRays = absolute.Fields[fieldIndex].Wavelengths[wavelengthIndex].Rays;
            var referencedRays = referenced.Fields[fieldIndex].Wavelengths[wavelengthIndex].Rays;
            return absoluteRays.Zip(referencedRays, (absoluteRay, referencedRay) =>
            {
                var x = absoluteRay.X + ((magnification - 1) * referencedRay.X);
                var y = absoluteRay.Y + ((magnification - 1) * referencedRay.Y);
                return new AnalysisPoint(x * displayScale, y * displayScale);
            }).ToArray();
        }

        var series = IsColorByField()
            ? fields.Select((field, fieldIndex) =>
            {
                var globalFieldIndex = fieldIndices[fieldIndex];
                var points = Enumerable.Range(0, wavelengths.Count())
                    .SelectMany(wavelengthIndex => BuildPoints(fieldIndex, wavelengthIndex))
                    .ToArray();
                return new AnalysisSeries(
                    $"X ({displayUnitLabel})",
                    $"Y ({displayUnitLabel})",
                    points,
                    AnalysisSeriesKind.Scatter,
                    MtfPresentation.FieldName(Optic, field),
                    ColorIndex: globalFieldIndex,
                    MarkerStyle: _settings.UseSymbols
                        ? (AnalysisMarkerStyle)(globalFieldIndex % 4)
                        : AnalysisMarkerStyle.Circle,
                    MarkerSize: _settings.UseSymbols ? 2.8 : 2.2,
                    Opacity: 0.75,
                    LegendKey: $"field:{globalFieldIndex}",
                    XQuantity: imageSpace.Quantity,
                    XUnit: displayAxisUnit,
                    YQuantity: imageSpace.Quantity,
                    YUnit: displayAxisUnit);
            }).ToArray()
            : wavelengths.Select((wavelength, wavelengthIndex) => new AnalysisSeries(
                $"X ({displayUnitLabel})",
                $"Y ({displayUnitLabel})",
                fields.SelectMany((_, fieldIndex) => BuildPoints(fieldIndex, wavelengthIndex)).ToArray(),
                AnalysisSeriesKind.Scatter,
                $"{wavelength.Micrometers:0.0000} µm",
                ColorIndex: wavelengthIndex,
                MarkerStyle: _settings.UseSymbols
                    ? (AnalysisMarkerStyle)(wavelengthIndex % 4)
                    : AnalysisMarkerStyle.Circle,
                MarkerSize: _settings.UseSymbols ? 2.8 : 2.2,
                Opacity: 0.75,
                LegendKey: $"wavelength:{wavelength.Micrometers:R}",
                XQuantity: imageSpace.Quantity,
                XUnit: displayAxisUnit,
                YQuantity: imageSpace.Quantity,
                YUnit: displayAxisUnit)).ToArray();
        var allPoints = series.SelectMany(item => item.Points)
            .Where(point => double.IsFinite(point.X) && double.IsFinite(point.Y))
            .ToArray();
        var xMinimum = allPoints.Select(point => point.X).DefaultIfEmpty(-0.5).Min();
        var xMaximum = allPoints.Select(point => point.X).DefaultIfEmpty(0.5).Max();
        var yMinimum = allPoints.Select(point => point.Y).DefaultIfEmpty(-0.5).Min();
        var yMaximum = allPoints.Select(point => point.Y).DefaultIfEmpty(0.5).Max();
        var span = Math.Max(xMaximum - xMinimum, yMaximum - yMinimum);
        span = Math.Max(span, _settings.PlotScaleMicrometers);
        span = Math.Max(1, span * 1.1);
        var xCenter = (xMinimum + xMaximum) / 2;
        var yCenter = (yMinimum + yMaximum) / 2;
        var referencedRays = referenced.Fields
            .SelectMany(field => field.WeightedRays)
            .ToArray();
        var rmsRadiusDisplay = SpotAnalysisEngine.RmsRadius(referencedRays) * displayScale;
        var geometricRadiusDisplay = referencedRays
            .Select(ray => Math.Sqrt((ray.X * ray.X) + (ray.Y * ray.Y)))
            .DefaultIfEmpty(0)
            .Max() * displayScale;
        var scaleBar = NiceScale(span);
        var values = new Dictionary<string, object>
        {
            ["Variant"] = Name,
            ["RayCount"] = absolute.RayCount,
            ["VignettedRayCount"] = absolute.VignettedRayCount,
            ["FieldCount"] = absolute.Fields.Count,
            ["WavelengthCount"] = wavelengths.Count(),
            ["RayDensity"] = _settings.RayDensity,
            ["Pattern"] = _settings.Pattern,
            ["ColorRaysBy"] = _settings.ColorRaysBy,
            ["Reference"] = _settings.Reference,
            ["Magnification"] = magnification,
            ["UsePolarization"] = _settings.UsePolarization,
            ["ShowAiryDisk"] = _settings.ShowAiryDisk,
            ["WavelengthNumber"] = _settings.WavelengthNumber,
            ["FieldNumber"] = _settings.FieldNumber,
            ["SurfaceNumber"] = _settings.SurfaceNumber,
            ["DisplayScale"] = _settings.DisplayScale,
            ["PlotScaleMicrometers"] = _settings.PlotScaleMicrometers,
            ["PlotScaleMilliradians"] = imageSpace.IsAfocalAngle ? _settings.PlotScaleMicrometers : 0,
            ["ScaleBarMicrometers"] = imageSpace.IsAfocalAngle ? 0 : scaleBar,
            ["ScaleBarMilliradians"] = imageSpace.IsAfocalAngle ? scaleBar : 0,
            ["ScatterRays"] = _settings.ScatterRays,
            ["UseSymbols"] = _settings.UseSymbols,
            ["ImageSpaceAfocal"] = imageSpace.IsAfocalAngle,
            ["ImageCoordinateUnit"] = displayUnitLabel,
            ["RmsRadius"] = rmsRadiusDisplay,
            ["RmsRadiusUnit"] = displayUnitLabel,
            ["RmsRadiusMicrometers"] = imageSpace.IsAfocalAngle ? 0 : rmsRadiusDisplay,
            ["RmsRadiusMilliradians"] = imageSpace.IsAfocalAngle ? rmsRadiusDisplay : 0,
            ["GeometricRadius"] = geometricRadiusDisplay,
            ["GeometricRadiusUnit"] = displayUnitLabel,
            ["GeometricRadiusMicrometers"] = imageSpace.IsAfocalAngle ? 0 : geometricRadiusDisplay,
            ["GeometricRadiusMilliradians"] = imageSpace.IsAfocalAngle ? geometricRadiusDisplay : 0,
            ["MaximumGeometricSpotRadius"] = geometricRadiusDisplay / displayScale
        };
        return new AnalysisData(
            Name,
            values,
            series.FirstOrDefault(),
            series,
            new AnalysisPlotOptions(
                Title: "全视场点列图",
                EqualAspect: true,
                XMinimum: xCenter - (span / 2),
                XMaximum: xCenter + (span / 2),
                YMinimum: yCenter - (span / 2),
                YMaximum: yCenter + (span / 2),
                ShowLegend: true,
                GridOpacity: 0.25,
                HideTickLabels: true,
                DefaultSquareViewport: true));
    }

    private static double NiceScale(double value)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            return 1;
        }

        var exponent = Math.Floor(Math.Log10(value));
        var power = Math.Pow(10, exponent);
        var normalized = value / power;
        var rounded = normalized <= 1 ? 1
            : normalized <= 2 ? 2
            : normalized <= 5 ? 5
            : 10;
        return rounded * power;
    }

    private bool IsColorByField()
    {
        return string.Equals(_settings.ColorRaysBy, "field", StringComparison.OrdinalIgnoreCase)
            || string.Equals(_settings.ColorRaysBy, "视场", StringComparison.Ordinal);
    }

    private AnalysisData BuildMatrix(
        AnalysisData source,
        IReadOnlyDictionary<string, object> values)
    {
        var sourcePanes = source.PlotPanes ?? Array.Empty<AnalysisPlotPane>();
        var panes = sourcePanes
                .SelectMany(pane => pane.Series.Select(series => new AnalysisPlotPane(
                    series.Name,
                    new[] { series },
                    pane.PlotOptions with
                    {
                        Title = string.Empty,
                        HideTickLabels = true
                    },
                    Footer: MatrixFieldLabel(pane.Title))))
                .ToArray();
        var matrixValues = values.ToDictionary(item => item.Key, item => item.Value);
        var imageSpaceAfocal = matrixValues.TryGetValue("ImageSpaceAfocal", out var afocalValue)
            && afocalValue is bool afocal
            && afocal;
        var firstOptions = panes.FirstOrDefault()?.PlotOptions;
        var scaleBar = firstOptions?.XMinimum is { } xMinimum
            && firstOptions.XMaximum is { } xMaximum
                ? Math.Abs(xMaximum - xMinimum) * (imageSpaceAfocal ? 1 : 1000)
                : 0;
        matrixValues["ScaleBarMicrometers"] = imageSpaceAfocal ? 0 : scaleBar;
        matrixValues["ScaleBarMilliradians"] = imageSpaceAfocal ? scaleBar : 0;

        var firstSeries = panes.FirstOrDefault()?.Series.FirstOrDefault();
        return new AnalysisData(
            Name,
            matrixValues,
            firstSeries,
            firstSeries is null ? null : new[] { firstSeries },
            PlotPanes: panes,
            PlotPaneColumns: Math.Max(1, sourcePanes.FirstOrDefault()?.Series.Count ?? 1));
    }

    private static string MatrixFieldLabel(string title)
    {
        foreach (var prefix in new[] { "Y=", "X=" })
        {
            var start = title.LastIndexOf(prefix, StringComparison.Ordinal);
            if (start < 0)
            {
                continue;
            }

            var valueStart = start + prefix.Length;
            var valueEnd = title.IndexOfAny(new[] { ' ', ',', ')' }, valueStart);
            if (valueEnd < 0)
            {
                valueEnd = title.Length;
            }

            if (double.TryParse(
                title[valueStart..valueEnd],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var value))
            {
                return $"{value:0.0000} mm";
            }
        }

        return title;
    }
}

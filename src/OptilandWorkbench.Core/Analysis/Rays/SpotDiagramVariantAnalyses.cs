using System.Globalization;

namespace OptilandWorkbench.Core.Analysis;

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

    public SpotDiagramVariantAnalysis(
        Optic optic,
        SpotDiagramVariant variant,
        SpotDiagramSettings? settings = null) : base(optic)
    {
        _variant = variant;
        _settings = settings ?? new SpotDiagramSettings();
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

        var source = new SpotDiagramAnalysis(Optic, _settings).GenerateData();
        var values = source.Values
            .Concat(new[] { new KeyValuePair<string, object>("Variant", Name) })
            .ToDictionary(item => item.Key, item => item.Value);
        return _variant == SpotDiagramVariant.Matrix
            ? BuildMatrix(source, values, configurationMatrix: false)
            : BuildMatrix(source, values, configurationMatrix: true);
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
        var magnification = double.IsFinite(_settings.Magnification)
            ? Math.Max(0, _settings.Magnification)
            : 1;
        var series = wavelengths.Select((wavelength, wavelengthIndex) =>
        {
            var points = absolute.Fields.SelectMany((field, fieldIndex) =>
            {
                var absoluteRays = field.Wavelengths[wavelengthIndex].Rays;
                var referencedRays = referenced.Fields[fieldIndex].Wavelengths[wavelengthIndex].Rays;
                return absoluteRays.Zip(referencedRays, (absoluteRay, referencedRay) =>
                {
                    var x = absoluteRay.X + ((magnification - 1) * referencedRay.X);
                    var y = absoluteRay.Y + ((magnification - 1) * referencedRay.Y);
                    return new AnalysisPoint(x * 1000, y * 1000);
                });
            }).ToArray();
            return new AnalysisSeries(
                "X (µm)",
                "Y (µm)",
                points,
                AnalysisSeriesKind.Scatter,
                $"{wavelength.Micrometers:0.0000} µm",
                ColorIndex: wavelengthIndex,
                MarkerStyle: _settings.UseSymbols
                    ? (AnalysisMarkerStyle)(wavelengthIndex % 4)
                    : AnalysisMarkerStyle.Circle,
                MarkerSize: _settings.UseSymbols ? 2.8 : 2.2,
                Opacity: 0.75);
        }).ToArray();
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
            .SelectMany(field => field.Wavelengths)
            .SelectMany(wavelength => wavelength.Rays)
            .ToArray();
        var rmsRadiusMicrometers = SpotAnalysisEngine.RmsRadius(referencedRays) * 1000;
        var geometricRadiusMicrometers = referencedRays
            .Select(ray => Math.Sqrt((ray.X * ray.X) + (ray.Y * ray.Y)))
            .DefaultIfEmpty(0)
            .Max() * 1000;
        var scaleBarMicrometers = NiceScale(span);
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
            ["ScaleBarMicrometers"] = scaleBarMicrometers,
            ["ScatterRays"] = _settings.ScatterRays,
            ["UseSymbols"] = _settings.UseSymbols,
            ["RmsRadiusMicrometers"] = rmsRadiusMicrometers,
            ["GeometricRadiusMicrometers"] = geometricRadiusMicrometers,
            ["MaximumGeometricSpotRadius"] = geometricRadiusMicrometers / 1000
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
                HideTickLabels: true));
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

    private AnalysisData BuildMatrix(
        AnalysisData source,
        IReadOnlyDictionary<string, object> values,
        bool configurationMatrix)
    {
        var panes = (source.PlotPanes ?? Array.Empty<AnalysisPlotPane>())
            .SelectMany(pane => pane.Series.Select(series => new AnalysisPlotPane(
                configurationMatrix
                    ? $"结构 1 · {pane.Title} · {series.Name}"
                    : series.Name,
                new[] { series },
                pane.PlotOptions with
                {
                    Title = configurationMatrix
                        ? $"结构 1 · {pane.Title} · {series.Name}"
                        : string.Empty,
                    HideTickLabels = true
                },
                configurationMatrix ? pane.Metrics : null,
                configurationMatrix ? pane.Footer : MatrixFieldLabel(pane.Title))))
            .ToArray();
        var matrixValues = values.ToDictionary(item => item.Key, item => item.Value);
        if (!configurationMatrix)
        {
            var firstOptions = panes.FirstOrDefault()?.PlotOptions;
            var scaleBarMicrometers = firstOptions?.XMinimum is { } xMinimum
                && firstOptions.XMaximum is { } xMaximum
                    ? Math.Abs(xMaximum - xMinimum) * 1000
                    : 0;
            matrixValues["ScaleBarMicrometers"] = scaleBarMicrometers;
        }

        var firstSeries = panes.FirstOrDefault()?.Series.FirstOrDefault();
        return new AnalysisData(
            Name,
            matrixValues,
            firstSeries,
            firstSeries is null ? null : new[] { firstSeries },
            PlotPanes: panes,
            PlotPaneColumns: Math.Max(1, source.PlotPanes?.FirstOrDefault()?.Series.Count ?? 1));
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

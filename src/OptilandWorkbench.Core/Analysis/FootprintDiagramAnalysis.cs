using System.Globalization;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Rays;
using OptilandWorkbench.Core.Raytrace;

namespace OptilandWorkbench.Core.Analysis;

public sealed class FootprintDiagramAnalysis : BaseAnalysis
{
    private readonly int _rayDensity;
    private readonly int _surfaceNumber;
    private readonly int _wavelengthNumber;
    private readonly int _fieldNumber;
    private readonly bool _deleteVignetted;
    private readonly bool _useSymbols;
    private readonly string _colorRaysBy;

    public FootprintDiagramAnalysis(
        Optic optic,
        int rayDensity = 10,
        int surfaceNumber = -1,
        int wavelengthNumber = 0,
        int fieldNumber = 0,
        bool deleteVignetted = false,
        bool useSymbols = true,
        string colorRaysBy = "wavelength") : base(optic)
    {
        _rayDensity = Math.Clamp(rayDensity, 1, 64);
        _surfaceNumber = surfaceNumber;
        _wavelengthNumber = wavelengthNumber;
        _fieldNumber = fieldNumber;
        _deleteVignetted = deleteVignetted;
        _useSymbols = useSymbols;
        _colorRaysBy = string.Equals(colorRaysBy, "field", StringComparison.OrdinalIgnoreCase)
            ? "field"
            : "wavelength";
    }

    public override string Name => "Footprint Diagram";

    public override AnalysisData GenerateData()
    {
        if (Optic.SurfaceGroup.Items.Count == 0)
        {
            throw new InvalidOperationException("A footprint diagram requires at least one optical surface.");
        }

        var surface = ResolveSurface();
        var fields = SelectFields();
        var wavelengths = AnalysisTrace.SelectWavelengths(Optic, _wavelengthNumber);
        if (fields.Length == 0 || wavelengths.Length == 0)
        {
            throw new InvalidOperationException("A footprint diagram requires at least one field and wavelength.");
        }

        var pupilSamples = BuildPupilGrid(_rayDensity);
        var series = new List<AnalysisSeries>();
        var launchedRayCount = 0;
        var plottedRayCount = 0;
        for (var fieldIndex = 0; fieldIndex < fields.Length; fieldIndex++)
        {
            var field = fields[fieldIndex];
            for (var wavelengthIndex = 0; wavelengthIndex < wavelengths.Length; wavelengthIndex++)
            {
                var wavelength = wavelengths[wavelengthIndex];
                var bundle = Optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(
                    field.NormalizedX,
                    field.NormalizedY,
                    wavelength.Micrometers,
                    pupilSamples);
                var surfaceIndex = Optic.SurfaceGroup.Items.IndexOf(surface);
                var finalSurfaceIndex = Optic.SurfaceGroup.Items.Count - 1;
                var retainedSurfaces = _deleteVignetted && surfaceIndex != finalSurfaceIndex
                    ? new[] { surfaceIndex, finalSurfaceIndex }
                    : new[] { surfaceIndex };
                using var trace = Optic.SequentialRayTracer.Trace(
                    bundle,
                    TraceRequest.Selected(retainedSurfaces));
                launchedRayCount += bundle.Rays.Count;

                var points = Enumerable.Range(0, trace.RayCount)
                    .Select(rayIndex => FootprintPoint(
                        trace,
                        rayIndex,
                        surfaceIndex,
                        finalSurfaceIndex,
                        surface))
                    .Where(point => point is not null)
                    .Select(point => point!)
                    .ToArray();
                plottedRayCount += points.Length;
                var colorIndex = _colorRaysBy == "wavelength" ? wavelengthIndex : fieldIndex;
                var legendKey = _colorRaysBy == "wavelength"
                    ? $"wavelength:{wavelength.Micrometers.ToString("R", CultureInfo.InvariantCulture)}"
                    : $"field:{field.Number}";
                var legendLabel = _colorRaysBy == "wavelength"
                    ? $"{wavelength.Micrometers.ToString("0.0000", CultureInfo.InvariantCulture)} \u00B5m"
                    : FieldLegendLabel(field);
                var markerStyle = _useSymbols
                    ? (AnalysisMarkerStyle)((fieldIndex + wavelengthIndex) % 4)
                    : AnalysisMarkerStyle.Circle;
                series.Add(new AnalysisSeries(
                    "X (mm)",
                    "Y (mm)",
                    points,
                    AnalysisSeriesKind.Scatter,
                    $"F{field.Number}  {wavelength.Micrometers:0.0000} \u00B5m",
                    ColorIndex: colorIndex,
                    MarkerStyle: markerStyle,
                    MarkerSize: _useSymbols ? 3.2 : 2.4,
                    Opacity: 0.8,
                    LegendKey: legendKey,
                    LegendLabel: legendLabel));
            }
        }

        series.AddRange(BuildApertureOutline(surface));
        var bounds = PlotBounds(surface, series);
        var values = new Dictionary<string, object>
        {
            ["SurfaceNumber"] = surface.Number,
            ["SurfaceLabel"] = surface.Label,
            ["RayDensity"] = _rayDensity,
            ["FieldNumber"] = _fieldNumber,
            ["WavelengthNumber"] = _wavelengthNumber,
            ["DeleteVignetted"] = _deleteVignetted,
            ["UseSymbols"] = _useSymbols,
            ["ColorRaysBy"] = _colorRaysBy,
            ["LaunchedRayCount"] = launchedRayCount,
            ["PlottedRayCount"] = plottedRayCount,
            ["TransmissionPercent"] = launchedRayCount == 0 ? 0 : 100.0 * plottedRayCount / launchedRayCount
        };
        var plotOptions = new AnalysisPlotOptions(
            Title: $"Surface {surface.Number}: {surface.Label}",
            EqualAspect: true,
            ShowVerticalZeroLine: true,
            ShowHorizontalZeroLine: true,
            XMinimum: bounds.MinimumX,
            XMaximum: bounds.MaximumX,
            YMinimum: bounds.MinimumY,
            YMaximum: bounds.MaximumY,
            ShowLegend: true,
            GridOpacity: 0.25);
        return new AnalysisData(Name, values, series.FirstOrDefault(), series, plotOptions);
    }

    private OpticalSurface ResolveSurface()
    {
        if (_surfaceNumber < 0)
        {
            return Optic.SurfaceGroup.Items[^1];
        }

        return Optic.SurfaceGroup.Items.FirstOrDefault(surface => surface.Number == _surfaceNumber)
            ?? Optic.SurfaceGroup.Items[Math.Clamp(_surfaceNumber, 0, Optic.SurfaceGroup.Items.Count - 1)];
    }

    private SelectedField[] SelectFields()
    {
        var normalized = SpotAnalysisEngine.DefinedFields(Optic);
        var fields = Optic.Fields.Select((field, index) => new SelectedField(
            index + 1,
            index < normalized.Count ? normalized[index].Hx : 0,
            index < normalized.Count ? normalized[index].Hy : 0,
            field.X,
            field.Y)).ToArray();
        if (_fieldNumber <= 0 || fields.Length == 0)
        {
            return fields;
        }

        return new[] { fields[Math.Clamp(_fieldNumber - 1, 0, fields.Length - 1)] };
    }

    private string FieldLegendLabel(SelectedField field)
    {
        var unit = Optic.FieldDefinition == FieldDefinitionKind.Angle ? "\u00B0" : "mm";
        return $"F{field.Number}  " +
            $"({field.X.ToString("0.####", CultureInfo.InvariantCulture)}, " +
            $"{field.Y.ToString("0.####", CultureInfo.InvariantCulture)}) {unit}";
    }

    private AnalysisPoint? FootprintPoint(
        RequestedTrace trace,
        int rayIndex,
        int surfaceIndex,
        int finalSurfaceIndex,
        OpticalSurface surface)
    {
        if (!trace.TryGetSample(rayIndex, surfaceIndex, out var selected))
        {
            return null;
        }

        var local = surface.CoordinateSystem.ToLocalPoint(selected.Position);
        var sag = surface.Geometry.Sag(local.X, local.Y);
        var surfaceTolerance = 1e-7 * Math.Max(1, Math.Abs(sag));
        if (!double.IsFinite(sag) || Math.Abs(local.Z - sag) > surfaceTolerance)
        {
            return null;
        }

        if (_deleteVignetted
            && (selected.Vignetted
                || selected.Intensity <= 0
                || (surfaceIndex != finalSurfaceIndex
                    && (!trace.TryGetSample(rayIndex, finalSurfaceIndex, out var finalSample)
                        || finalSample.Vignetted
                        || finalSample.Intensity <= 0))))
        {
            return null;
        }

        return double.IsFinite(local.X) && double.IsFinite(local.Y)
            ? new AnalysisPoint(local.X, local.Y)
            : null;
    }

    private static IReadOnlyList<PupilSample> BuildPupilGrid(int rayDensity)
    {
        var samples = new List<PupilSample>();
        for (var row = -rayDensity; row <= rayDensity; row++)
        {
            var y = (double)row / rayDensity;
            for (var column = -rayDensity; column <= rayDensity; column++)
            {
                var x = (double)column / rayDensity;
                if ((x * x) + (y * y) <= 1 + 1e-12)
                {
                    samples.Add(new PupilSample(x, y, 1));
                }
            }
        }

        return samples;
    }

    private static IEnumerable<AnalysisSeries> BuildApertureOutline(OpticalSurface surface)
    {
        var aperture = surface.PhysicalAperture ?? new CircularAperture(surface.SemiDiameter);
        var outlines = ApertureOutlines(aperture);
        return outlines.Select((outline, index) => new AnalysisSeries(
            "X (mm)",
            "Y (mm)",
            outline.Select(point => new AnalysisPoint(point.X, point.Y)).ToArray(),
            AnalysisSeriesKind.Line,
            index == 0 ? "Surface aperture" : string.Empty,
            LineStyle: AnalysisLineStyle.Dashed,
            ColorIndex: 8,
            LineWidth: 1,
            Opacity: 0.75));
    }

    private static IReadOnlyList<IReadOnlyList<(double X, double Y)>> ApertureOutlines(IPhysicalAperture aperture)
    {
        return aperture switch
        {
            CircularAperture circular => new[] { Ellipse(circular.Radius, circular.Radius) },
            AnnularAperture annular => new[]
            {
                Ellipse(annular.OuterRadius, annular.OuterRadius),
                Ellipse(annular.InnerRadius, annular.InnerRadius)
            },
            OffsetRadialAperture offset => new[]
            {
                Ellipse(offset.OuterRadius, offset.OuterRadius, offset.OffsetX, offset.OffsetY),
                Ellipse(offset.InnerRadius, offset.InnerRadius, offset.OffsetX, offset.OffsetY)
            },
            RectangularAperture rectangular => new[]
            {
                new[]
                {
                    (rectangular.XMinimum, rectangular.YMinimum),
                    (rectangular.XMaximum, rectangular.YMinimum),
                    (rectangular.XMaximum, rectangular.YMaximum),
                    (rectangular.XMinimum, rectangular.YMaximum),
                    (rectangular.XMinimum, rectangular.YMinimum)
                }
            },
            EllipticalAperture elliptical => new[]
            {
                Ellipse(elliptical.SemiAxisX, elliptical.SemiAxisY, elliptical.OffsetX, elliptical.OffsetY)
            },
            PolygonAperture polygon => new[]
            {
                polygon.Vertices.Concat(polygon.Vertices.Take(1)).ToArray()
            },
            _ => Array.Empty<IReadOnlyList<(double X, double Y)>>()
        };
    }

    private static IReadOnlyList<(double X, double Y)> Ellipse(
        double radiusX,
        double radiusY,
        double centerX = 0,
        double centerY = 0)
    {
        if (radiusX <= 0 || radiusY <= 0)
        {
            return Array.Empty<(double X, double Y)>();
        }

        return Enumerable.Range(0, 97)
            .Select(index => 2 * Math.PI * index / 96)
            .Select(angle => (centerX + (radiusX * Math.Cos(angle)), centerY + (radiusY * Math.Sin(angle))))
            .ToArray();
    }

    private static PlotExtent PlotBounds(OpticalSurface surface, IReadOnlyList<AnalysisSeries> series)
    {
        var points = series.SelectMany(item => item.Points).ToArray();
        var minimumX = points.Select(point => point.X).DefaultIfEmpty(-surface.SemiDiameter).Min();
        var maximumX = points.Select(point => point.X).DefaultIfEmpty(surface.SemiDiameter).Max();
        var minimumY = points.Select(point => point.Y).DefaultIfEmpty(-surface.SemiDiameter).Min();
        var maximumY = points.Select(point => point.Y).DefaultIfEmpty(surface.SemiDiameter).Max();
        var span = Math.Max(maximumX - minimumX, maximumY - minimumY);
        var margin = Math.Max(0.05, span * 0.05);
        return new PlotExtent(minimumX - margin, maximumX + margin, minimumY - margin, maximumY + margin);
    }

    private sealed record SelectedField(
        int Number,
        double NormalizedX,
        double NormalizedY,
        double X,
        double Y);

    private sealed record PlotExtent(double MinimumX, double MaximumX, double MinimumY, double MaximumY);
}

using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Analysis;

public sealed record SpotDiagramSettings(
    int RayDensity = 6,
    string Pattern = "hexapolar",
    int WavelengthNumber = 0,
    int FieldNumber = 0,
    int SurfaceNumber = -1,
    string ColorRaysBy = "wavelength",
    string Reference = "centroid",
    bool UsePolarization = false,
    bool DirectionCosines = false,
    bool ShowAiryDisk = false,
    string DisplayScale = "scale-bar",
    double PlotScaleMicrometers = 0,
    bool ScatterRays = false,
    bool UseSymbols = true,
    double Magnification = 1,
    bool IgnoreLateralColor = false);

public sealed class SpotDiagramAnalysis : BaseAnalysis
{
    private readonly SpotDiagramSettings _settings;

    public SpotDiagramAnalysis(Optic optic, int numRings = 6, string distribution = "hexapolar") : base(optic)
    {
        _settings = new SpotDiagramSettings(
            RayDensity: Math.Max(1, numRings),
            Pattern: distribution);
    }

    public SpotDiagramAnalysis(Optic optic, SpotDiagramSettings settings) : base(optic)
    {
        _settings = settings with
        {
            RayDensity = Math.Clamp(settings.RayDensity, 1, 32),
            PlotScaleMicrometers = double.IsFinite(settings.PlotScaleMicrometers)
                ? Math.Max(0, settings.PlotScaleMicrometers)
                : 0
        };
    }

    public override string Name => "Spot Diagram";

    public override AnalysisData GenerateData()
    {
        var analysisOptic = ResolveAnalysisOptic();
        var allFields = SpotAnalysisEngine.DefinedFields(analysisOptic);
        var fieldIndices = _settings.FieldNumber <= 0
            ? Enumerable.Range(0, allFields.Count).ToArray()
            : new[]
            {
                Math.Clamp(_settings.FieldNumber - 1, 0, Math.Max(0, allFields.Count - 1))
            };
        var fields = fieldIndices.Select(index => allFields[index]).ToArray();
        var wavelengths = AnalysisTrace.SelectWavelengths(analysisOptic, _settings.WavelengthNumber);
        var result = SpotAnalysisEngine.Generate(
            analysisOptic,
            fields,
            wavelengths,
            _settings.RayDensity,
            _settings.Pattern,
            surfaceNumber: _settings.SurfaceNumber,
            directionCosines: _settings.DirectionCosines,
            reference: _settings.Reference,
            usePolarization: _settings.UsePolarization,
            ignoreLateralColor: _settings.IgnoreLateralColor);
        var imageSpace = ImageSpaceAnalysisSupport.CoordinateDescriptor(
            analysisOptic,
            _settings.SurfaceNumber,
            _settings.DirectionCosines);
        var maximumRadius = result.Fields
            .SelectMany(field => field.Wavelengths)
            .SelectMany(wavelength => wavelength.Rays)
            .Select(ray => Math.Sqrt((ray.X * ray.X) + (ray.Y * ray.Y)))
            .DefaultIfEmpty(0.01)
            .Max();
        var airyRadius = AiryDiskSupport.CalculateRadius(analysisOptic, fields, wavelengths, _settings.ShowAiryDisk);
        var requiredRadius = Math.Max(maximumRadius, airyRadius);
        var axisLimit = _settings.PlotScaleMicrometers > 0
            ? imageSpace.IsAfocalAngle
                ? _settings.PlotScaleMicrometers
                : _settings.PlotScaleMicrometers / 1000.0
            : NiceAxisLimit((requiredRadius <= 1e-12 ? 0.01 : requiredRadius) * 1.05);
        var panes = result.Fields.Select((field, fieldIndex) =>
        {
            var fieldTitle = MtfPresentation.FieldName(analysisOptic, (field.Hx, field.Hy));
            var axisUnit = imageSpace.AxisUnitLabel;
            var series = field.Wavelengths.Select((wavelength, index) => new AnalysisSeries(
                $"X ({axisUnit})",
                $"Y ({axisUnit})",
                wavelength.Rays.Select(ray => new AnalysisPoint(ray.X, ray.Y)).ToArray(),
                AnalysisSeriesKind.Scatter,
                $"{wavelength.Wavelength.Micrometers:0.0000} \u00B5m",
                ColorIndex: IsColorByField() ? fieldIndex : index,
                MarkerStyle: _settings.UseSymbols
                    ? (AnalysisMarkerStyle)(index % 4)
                    : AnalysisMarkerStyle.Circle,
                MarkerSize: _settings.UseSymbols ? 2.8 : 2.2,
                Opacity: 0.7,
                LegendKey: IsColorByField()
                    ? $"field:{fieldIndex}"
                    : $"wavelength:{wavelength.Wavelength.Micrometers:R}",
                XQuantity: imageSpace.Quantity,
                XUnit: imageSpace.Unit,
                YQuantity: imageSpace.Quantity,
                YUnit: imageSpace.Unit)).ToList();
            if (_settings.ShowAiryDisk
                && imageSpace.Kind != ImageSpaceCoordinateKind.DirectionCosine
                && airyRadius > 0)
            {
                series.Add(AiryDiskSupport.CreateSeries(airyRadius, imageSpace));
            }

            var fieldRays = field.WeightedRays.ToArray();
            var rmsRadiusDisplay = SpotAnalysisEngine.RmsRadius(fieldRays) * imageSpace.MetricScale;
            var geometricRadiusDisplay = fieldRays
                .Select(ray => Math.Sqrt((ray.X * ray.X) + (ray.Y * ray.Y)))
                .DefaultIfEmpty(0)
                .Max() * imageSpace.MetricScale;
            return new AnalysisPlotPane(
                fieldTitle,
                series,
                new AnalysisPlotOptions(
                    Title: fieldTitle,
                    EqualAspect: true,
                    XMinimum: -axisLimit,
                    XMaximum: axisLimit,
                    YMinimum: -axisLimit,
                    YMaximum: axisLimit,
                    GridOpacity: 0.25,
                    HideTickLabels: true),
                _settings.UseSymbols
                    ? new[]
                {
                    new AnalysisPlotMetric("RMS 半径", rmsRadiusDisplay, imageSpace.MetricUnitLabel),
                    new AnalysisPlotMetric("GEO 半径", geometricRadiusDisplay, imageSpace.MetricUnitLabel)
                }
                    : null,
                _settings.UseSymbols
                    ? $"参考：{ReferenceLabel()}"
                    : "");
        }).ToArray();
        var firstSeries = panes.FirstOrDefault()?.Series.FirstOrDefault();
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["RayCount"] = result.RayCount,
            ["VignettedRayCount"] = result.VignettedRayCount,
            ["FieldCount"] = result.Fields.Count,
            ["WavelengthCount"] = Optic.Wavelengths.Count,
            ["NumRings"] = _settings.RayDensity,
            ["RayDensity"] = _settings.RayDensity,
            ["Distribution"] = _settings.Pattern,
            ["Pattern"] = _settings.Pattern,
            ["WavelengthNumber"] = _settings.WavelengthNumber,
            ["FieldNumber"] = _settings.FieldNumber,
            ["SurfaceNumber"] = _settings.SurfaceNumber,
            ["ColorRaysBy"] = _settings.ColorRaysBy,
            ["Reference"] = _settings.Reference,
            ["UsePolarization"] = _settings.UsePolarization,
            ["DirectionCosines"] = _settings.DirectionCosines,
            ["ImageSpaceAfocal"] = imageSpace.IsAfocalAngle,
            ["ImageCoordinateUnit"] = imageSpace.AxisUnitLabel,
            ["ShowAiryDisk"] = _settings.ShowAiryDisk,
            ["AiryRadius"] = airyRadius,
            ["DisplayScale"] = _settings.DisplayScale,
            ["PlotScaleMicrometers"] = _settings.PlotScaleMicrometers,
            ["PlotScaleMilliradians"] = imageSpace.IsAfocalAngle ? _settings.PlotScaleMicrometers : 0,
            ["ScatterRays"] = _settings.ScatterRays,
            ["UseSymbols"] = _settings.UseSymbols,
            ["IgnoreLateralColor"] = _settings.IgnoreLateralColor,
            ["MaximumGeometricSpotRadius"] = maximumRadius
        }, firstSeries, firstSeries is null ? null : new[] { firstSeries }, PlotPanes: panes, PlotPaneColumns: 3);
    }

    private bool IsColorByField()
    {
        return string.Equals(_settings.ColorRaysBy, "field", StringComparison.OrdinalIgnoreCase)
            || string.Equals(_settings.ColorRaysBy, "视场", StringComparison.Ordinal);
    }

    private Optic ResolveAnalysisOptic()
    {
        if (_settings.ScatterRays
            || Optic.SurfaceGroup.Items.All(surface => surface.ScatteringModel is null))
        {
            return Optic;
        }

        var clone = Optic.FromSnapshot(Optic.ToSnapshot());
        foreach (var surface in clone.SurfaceGroup.Items)
        {
            surface.ScatteringModel = null;
        }

        return clone;
    }

    private string ReferenceLabel()
    {
        return string.Equals(_settings.Reference, "centroid", StringComparison.OrdinalIgnoreCase)
            || string.Equals(_settings.Reference, "质心", StringComparison.Ordinal)
                ? "主波长质心"
                : "主光线";
    }

    private static double NiceAxisLimit(double minimum)
    {
        if (!double.IsFinite(minimum) || minimum <= 0)
        {
            return 0.01;
        }

        var exponent = Math.Floor(Math.Log10(minimum));
        var scale = Math.Pow(10, exponent);
        var normalized = minimum / scale;
        var nice = normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10;
        return nice * scale;
    }
}

public sealed class RayFanAnalysis : BaseAnalysis
{
    private readonly int _numPoints;
    private readonly int _numberOfRaysEachSide;
    private readonly double _plotScaleMicrometers;
    private readonly bool _useDashes;
    private readonly bool _vignettedPupil;
    private readonly bool _checkApertures;
    private readonly int _wavelengthNumber;
    private readonly int _fieldNumber;
    private readonly RayFanAberrationComponent _tangentialComponent;
    private readonly RayFanAberrationComponent _sagittalComponent;
    private readonly int _surfaceNumber;
    private readonly bool _zemaxCompatible;

    public RayFanAnalysis(
        Optic optic,
        int numPoints = 256,
        double plotScaleMicrometers = 0,
        int? numberOfRaysEachSide = null,
        bool useDashes = false,
        bool vignettedPupil = false,
        bool checkApertures = true,
        int wavelengthNumber = 0,
        int fieldNumber = 0,
        string tangentialAberration = "Y Aberration",
        string sagittalAberration = "X Aberration",
        int surfaceNumber = -1,
        bool zemaxCompatible = false) : base(optic)
    {
        if (numberOfRaysEachSide.HasValue)
        {
            _numberOfRaysEachSide = Math.Clamp(numberOfRaysEachSide.Value, 1, 4096);
            _numPoints = (_numberOfRaysEachSide * 2) + 1;
        }
        else
        {
            _numPoints = Math.Max(3, numPoints % 2 == 0 ? numPoints + 1 : numPoints);
            _numberOfRaysEachSide = (_numPoints - 1) / 2;
        }

        _plotScaleMicrometers = double.IsFinite(plotScaleMicrometers)
            ? Math.Max(0, plotScaleMicrometers)
            : 0;
        _useDashes = useDashes;
        _vignettedPupil = vignettedPupil;
        _checkApertures = checkApertures;
        _wavelengthNumber = Math.Max(0, wavelengthNumber);
        _fieldNumber = Math.Max(0, fieldNumber);
        _tangentialComponent = ParseComponent(tangentialAberration, RayFanAberrationComponent.Y);
        _sagittalComponent = ParseComponent(sagittalAberration, RayFanAberrationComponent.X);
        _surfaceNumber = surfaceNumber;
        _zemaxCompatible = zemaxCompatible;
    }

    public override string Name => "Ray Fan";

    public override AnalysisData GenerateData()
    {
        var analysisOptic = ResolveAnalysisOptic();
        var allFields = SpotAnalysisEngine.DefinedFields(analysisOptic);
        var fieldIndices = _fieldNumber <= 0
            ? Enumerable.Range(0, allFields.Count).ToArray()
            : new[] { Math.Clamp(_fieldNumber - 1, 0, Math.Max(0, allFields.Count - 1)) };
        var fields = fieldIndices.Select(index => allFields[index]).ToArray();
        var allWavelengths = analysisOptic.Wavelengths.ToArray();
        var wavelengthIndices = _wavelengthNumber <= 0
            ? Enumerable.Range(0, allWavelengths.Length).ToArray()
            : new[] { Math.Clamp(_wavelengthNumber - 1, 0, Math.Max(0, allWavelengths.Length - 1)) };
        var wavelengths = wavelengthIndices.Select(index => allWavelengths[index]).ToArray();
        var primaryIndex = Array.FindIndex(wavelengths, wavelength => wavelength.IsPrimary);
        primaryIndex = primaryIndex < 0 ? 0 : primaryIndex;
        var targetSurface = ResolveTargetSurface(analysisOptic);
        var imageSpace = ImageSpaceAnalysisSupport.CoordinateDescriptor(
            analysisOptic,
            targetSurface.Number);
        var pupil = Enumerable.Range(0, _numPoints)
            .Select(index => -1 + (2.0 * index / (_numPoints - 1.0)))
            .ToArray();
        var fieldFans = new List<(double Hx, double Hy, List<RayFanWave> Waves)>();

        foreach (var field in fields)
        {
            var waves = new List<RayFanWave>();
            for (var wavelengthIndex = 0; wavelengthIndex < wavelengths.Length; wavelengthIndex++)
            {
                var wavelength = wavelengths[wavelengthIndex];
                var xSamples = TraceFan(
                    analysisOptic,
                    targetSurface,
                    field,
                    wavelength,
                    pupil,
                    pupilAxisY: false,
                    _sagittalComponent,
                    _vignettedPupil,
                    _zemaxCompatible,
                    imageSpace);
                var ySamples = TraceFan(
                    analysisOptic,
                    targetSurface,
                    field,
                    wavelength,
                    pupil,
                    pupilAxisY: true,
                    _tangentialComponent,
                    _vignettedPupil,
                    _zemaxCompatible,
                    imageSpace);
                waves.Add(new RayFanWave(
                    wavelength,
                    wavelengthIndices[wavelengthIndex],
                    xSamples,
                    ySamples));
            }

            var reference = waves[Math.Min(primaryIndex, waves.Count - 1)];
            var center = _numPoints / 2;
            var xOffset = reference.X[center].Intensity > 0
                ? reference.X[center].Value
                : reference.X.Where(point => point.Intensity > 0).Select(point => point.Value).DefaultIfEmpty(0).Average();
            var yOffset = reference.Y[center].Intensity > 0
                ? reference.Y[center].Value
                : reference.Y.Where(point => point.Intensity > 0).Select(point => point.Value).DefaultIfEmpty(0).Average();
            waves = waves.Select(wave => wave with
            {
                X = wave.X.Select(point => point with { Value = point.Value - xOffset }).ToArray(),
                Y = wave.Y.Select(point => point with { Value = point.Value - yOffset }).ToArray()
            }).ToList();
            fieldFans.Add((field.Hx, field.Hy, waves));
        }

        var allFinite = fieldFans.SelectMany(field => field.Waves)
            .SelectMany(wave => wave.X.Concat(wave.Y))
            .Where(point => point.Intensity > 0 && double.IsFinite(point.Value))
            .Select(point => point.Value)
            .ToArray();
        var yMinimum = allFinite.DefaultIfEmpty(-1).Min();
        var yMaximum = allFinite.DefaultIfEmpty(1).Max();
        if (_plotScaleMicrometers > 0)
        {
            var limit = imageSpace.IsAfocalAngle
                ? _plotScaleMicrometers
                : _plotScaleMicrometers / 1000.0;
            yMinimum = -limit;
            yMaximum = limit;
        }
        else if (_zemaxCompatible)
        {
            var limit = Math.Max(Math.Abs(yMinimum), Math.Abs(yMaximum));
            limit = limit <= 1e-15 ? 1e-6 : limit;
            yMinimum = -limit;
            yMaximum = limit;
        }
        else
        {
            ExpandPlotRange(ref yMinimum, ref yMaximum);
        }

        var panes = new List<AnalysisPlotPane>();
        for (var fieldIndex = 0; fieldIndex < fieldFans.Count; fieldIndex++)
        {
            var field = fieldFans[fieldIndex];
            var title = MtfPresentation.FieldName(analysisOptic, (field.Hx, field.Hy));
            panes.Add(new AnalysisPlotPane(title, BuildFanSeries(
                field.Waves,
                pupil,
                yFan: true,
                _tangentialComponent,
                _useDashes,
                imageSpace), new AnalysisPlotOptions(
                Title: title,
                ShowVerticalZeroLine: true,
                ShowHorizontalZeroLine: true,
                XMinimum: -1,
                XMaximum: 1,
                YMinimum: yMinimum,
                YMaximum: yMaximum,
                HideTickLabels: _zemaxCompatible)));
            panes.Add(new AnalysisPlotPane(title, BuildFanSeries(
                field.Waves,
                pupil,
                yFan: false,
                _sagittalComponent,
                _useDashes,
                imageSpace), new AnalysisPlotOptions(
                Title: title,
                ShowVerticalZeroLine: true,
                ShowHorizontalZeroLine: true,
                XMinimum: -1,
                XMaximum: 1,
                YMinimum: yMinimum,
                YMaximum: yMaximum,
                HideTickLabels: _zemaxCompatible)));
        }

        var firstSeries = panes.FirstOrDefault()?.Series.FirstOrDefault();
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["Samples"] = _numPoints,
            ["NumberOfRaysEachSide"] = _numberOfRaysEachSide,
            ["FieldCount"] = fields.Length,
            ["WavelengthCount"] = wavelengths.Length,
            ["FieldNumber"] = _fieldNumber,
            ["WavelengthNumber"] = _wavelengthNumber,
            ["TangentialAberration"] = ComponentName(_tangentialComponent),
            ["SagittalAberration"] = ComponentName(_sagittalComponent),
            ["SurfaceNumber"] = targetSurface.Number,
            ["SurfaceLabel"] = targetSurface.Label,
            ["PlotScaleMicrometers"] = _plotScaleMicrometers,
            ["PlotScaleMilliradians"] = imageSpace.IsAfocalAngle ? _plotScaleMicrometers : 0,
            ["ImageSpaceAfocal"] = imageSpace.IsAfocalAngle,
            ["RayAberrationUnit"] = imageSpace.AxisUnitLabel,
            ["UseDashes"] = _useDashes,
            ["VignettedPupil"] = _vignettedPupil,
            ["CheckApertures"] = _checkApertures,
            ["MinimumRayAberration"] = allFinite.DefaultIfEmpty(0).Min(),
            ["MaximumRayAberration"] = allFinite.DefaultIfEmpty(0).Max()
        }, firstSeries, firstSeries is null ? null : new[] { firstSeries }, PlotPanes: panes, PlotPaneColumns: 2);
    }

    private static IReadOnlyList<AnalysisSeries> BuildFanSeries(
        IReadOnlyList<RayFanWave> waves,
        IReadOnlyList<double> pupil,
        bool yFan,
        RayFanAberrationComponent component,
        bool useDashes,
        ImageSpaceCoordinateDescriptor imageSpace)
    {
        return waves.Select(wave =>
        {
            var samples = yFan ? wave.Y : wave.X;
            return new AnalysisSeries(
                yFan ? "P_y" : "P_x",
                component == RayFanAberrationComponent.Y
                    ? $"epsilon_y ({imageSpace.AxisUnitLabel})"
                    : $"epsilon_x ({imageSpace.AxisUnitLabel})",
                samples.Select((sample, index) => new AnalysisPoint(
                    pupil[index],
                    sample.Intensity > 0 ? sample.Value : double.NaN)).ToArray(),
                Name: $"{wave.Wavelength.Micrometers:0.0000} \u00B5m",
                LineStyle: useDashes
                    ? (wave.WavelengthIndex % 3) switch
                    {
                        1 => AnalysisLineStyle.Dashed,
                        2 => AnalysisLineStyle.Dotted,
                        _ => AnalysisLineStyle.Solid
                    }
                    : AnalysisLineStyle.Solid,
                ColorIndex: wave.WavelengthIndex,
                XQuantity: AnalysisAxisQuantity.PupilCoordinate,
                XUnit: AnalysisAxisUnit.Dimensionless,
                YQuantity: imageSpace.Quantity,
                YUnit: imageSpace.Unit);
        }).ToArray();
    }

    private static IReadOnlyList<RayFanSample> TraceFan(
        Optic optic,
        OpticalSurface targetSurface,
        (double Hx, double Hy) field,
        Wavelength wavelength,
        IReadOnlyList<double> pupil,
        bool pupilAxisY,
        RayFanAberrationComponent component,
        bool vignettedPupil,
        bool localCoordinates,
        ImageSpaceCoordinateDescriptor imageSpace)
    {
        var pupilSamples = pupil.Select(value => new PupilSample(
            pupilAxisY ? 0 : value,
            pupilAxisY ? value : 0,
            1));
        var bundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(
            field.Hx,
            field.Hy,
            wavelength.Micrometers,
            pupilSamples,
            aimAtStop: optic.RayAimingEnabled,
            applyVignettingFactors: !vignettedPupil);
        var surfaceIndex = optic.SurfaceGroup.Items.IndexOf(targetSurface);
        using var trace = optic.SequentialRayTracer.Trace(
            bundle,
            TraceRequest.Selected(new[] { surfaceIndex }));
        return trace.GetSurfaceSamples(surfaceIndex).Select(sampleValue =>
        {
            if (sampleValue is not { } sample || sample.Vignetted || sample.Intensity <= 0)
            {
                return new RayFanSample(double.NaN, 0);
            }

            if (imageSpace.Kind == ImageSpaceCoordinateKind.AfocalAngle)
            {
                var angle = ImageSpaceAnalysisSupport.DirectionAnglesMilliradians(
                    targetSurface,
                    sample.Direction);
                var angleValue = component == RayFanAberrationComponent.Y ? angle.Y : angle.X;
                return new RayFanSample(angleValue, sample.Intensity);
            }

            var position = localCoordinates
                ? targetSurface.CoordinateSystem.ToLocalPoint(sample.Position)
                : sample.Position;
            var value = component == RayFanAberrationComponent.Y ? position.Y : position.X;
            return new RayFanSample(value, sample.Intensity);
        }).ToArray();
    }

    private Optic ResolveAnalysisOptic()
    {
        if (_checkApertures)
        {
            return Optic;
        }

        var clone = Optic.FromSnapshot(Optic.ToSnapshot());
        foreach (var surface in clone.SurfaceGroup.Items)
        {
            surface.PhysicalAperture = null;
        }

        return clone;
    }

    private OpticalSurface ResolveTargetSurface(Optic optic)
    {
        if (_surfaceNumber < 0)
        {
            return optic.SurfaceGroup.Items.Last();
        }

        return optic.SurfaceGroup.Items.FirstOrDefault(surface => surface.Number == _surfaceNumber)
            ?? throw new InvalidOperationException(
                $"Ray Fan surface {_surfaceNumber} does not exist.");
    }

    private static RayFanAberrationComponent ParseComponent(
        string value,
        RayFanAberrationComponent fallback)
    {
        if (value.Contains("X", StringComparison.OrdinalIgnoreCase))
        {
            return RayFanAberrationComponent.X;
        }

        if (value.Contains("Y", StringComparison.OrdinalIgnoreCase))
        {
            return RayFanAberrationComponent.Y;
        }

        return fallback;
    }

    private static string ComponentName(RayFanAberrationComponent component)
    {
        return component == RayFanAberrationComponent.Y ? "Y Aberration" : "X Aberration";
    }

    private static void ExpandPlotRange(ref double minimum, ref double maximum)
    {
        if (Math.Abs(maximum - minimum) < 1e-12)
        {
            minimum -= 1;
            maximum += 1;
            return;
        }

        var padding = (maximum - minimum) * 0.05;
        minimum -= padding;
        maximum += padding;
    }

    private sealed record RayFanSample(double Value, double Intensity);

    private sealed record RayFanWave(
        Wavelength Wavelength,
        int WavelengthIndex,
        IReadOnlyList<RayFanSample> X,
        IReadOnlyList<RayFanSample> Y);

    private enum RayFanAberrationComponent
    {
        X,
        Y
    }
}

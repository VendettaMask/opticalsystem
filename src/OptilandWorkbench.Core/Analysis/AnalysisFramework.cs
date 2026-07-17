using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Raytrace;

namespace OptilandWorkbench.Core.Analysis;

public abstract class BaseAnalysis
{
    protected BaseAnalysis(Optic optic)
    {
        Optic = optic;
    }

    protected Optic Optic { get; }

    public abstract string Name { get; }

    public abstract AnalysisData GenerateData();
}

public enum AnalysisSeriesKind
{
    Line,
    Scatter,
    Bar,
    Heatmap,
    Raster,
    ColoredLine
}

public enum AnalysisLineStyle
{
    Solid,
    Dashed,
    Dotted
}

public enum AnalysisMarkerStyle
{
    Circle,
    Square,
    Triangle
}

public enum AnalysisColorMap
{
    Viridis,
    Inferno,
    Jet
}

public sealed record AnalysisPoint(
    double X,
    double Y,
    string Label = "",
    double? Value = null,
    double? Red = null,
    double? Green = null,
    double? Blue = null);

public sealed record AnalysisSeries(
    string XAxisLabel,
    string YAxisLabel,
    IReadOnlyList<AnalysisPoint> Points,
    AnalysisSeriesKind Kind = AnalysisSeriesKind.Line,
    string Name = "",
    AnalysisLineStyle LineStyle = AnalysisLineStyle.Solid,
    int ColorIndex = 0,
    bool ShowMarkers = false,
    double LineWidth = 1.5,
    AnalysisMarkerStyle MarkerStyle = AnalysisMarkerStyle.Circle,
    double MarkerSize = 3.2,
    double Opacity = 1,
    string ValueLabel = "",
    AnalysisColorMap ColorMap = AnalysisColorMap.Viridis,
    double? ValueMinimum = null,
    double? ValueMaximum = null);

public sealed record AnalysisPlotOptions(
    string Title = "",
    bool SymmetricX = false,
    bool EqualAspect = false,
    bool ShowVerticalZeroLine = false,
    bool ShowHorizontalZeroLine = false,
    AnalysisLineStyle VerticalZeroLineStyle = AnalysisLineStyle.Solid,
    double VerticalZeroLineWidth = 0.5,
    double? XMinimum = null,
    double? XMaximum = null,
    double? YMinimum = null,
    double? YMaximum = null,
    bool ShowLegend = false,
    bool HideTopAndRightAxes = false,
    bool DottedGrid = false,
    double GridOpacity = 1,
    bool HideAxes = false);

public sealed record AnalysisPlotPane(
    string Title,
    IReadOnlyList<AnalysisSeries> Series,
    AnalysisPlotOptions PlotOptions);

public sealed record AnalysisData(
    string Name,
    IReadOnlyDictionary<string, object> Values,
    AnalysisSeries? Series = null,
    IReadOnlyList<AnalysisSeries>? SeriesList = null,
    AnalysisPlotOptions? PlotOptions = null,
    IReadOnlyList<AnalysisPlotPane>? PlotPanes = null,
    int PlotPaneColumns = 3)
{
    public IReadOnlyList<AnalysisSeries> PlotSeries => SeriesList
        ?? (Series is null ? Array.Empty<AnalysisSeries>() : new[] { Series });

    public string ExportText()
    {
        return string.Join(Environment.NewLine, Values.Select(item => $"{item.Key}: {item.Value}"));
    }
}

public sealed class SpotDiagramAnalysis : BaseAnalysis
{
    private readonly int _numRings;
    private readonly string _distribution;

    public SpotDiagramAnalysis(Optic optic, int numRings = 6, string distribution = "hexapolar") : base(optic)
    {
        _numRings = Math.Max(1, numRings);
        _distribution = distribution;
    }

    public override string Name => "Spot Diagram";

    public override AnalysisData GenerateData()
    {
        var fields = SpotAnalysisEngine.DefinedFields(Optic);
        var result = SpotAnalysisEngine.Generate(Optic, fields, Optic.Wavelengths, _numRings, _distribution);
        var maximumRadius = result.Fields
            .SelectMany(field => field.Wavelengths)
            .SelectMany(wavelength => wavelength.Rays)
            .Select(ray => Math.Sqrt((ray.X * ray.X) + (ray.Y * ray.Y)))
            .DefaultIfEmpty(0.01)
            .Max();
        var axisLimit = (maximumRadius <= 1e-12 ? 0.01 : maximumRadius) * 1.05;
        var panes = result.Fields.Select(field =>
        {
            var series = field.Wavelengths.Select((wavelength, index) => new AnalysisSeries(
                "X (mm)",
                "Y (mm)",
                wavelength.Rays.Select(ray => new AnalysisPoint(ray.X, ray.Y)).ToArray(),
                AnalysisSeriesKind.Scatter,
                $"{wavelength.Wavelength.Micrometers:0.0000} \u00B5m",
                ColorIndex: index,
                MarkerStyle: (AnalysisMarkerStyle)(index % 3),
                MarkerSize: 2.5,
                Opacity: 0.7)).ToArray();
            return new AnalysisPlotPane(
                $"Hx: {field.Hx:0.000}, Hy: {field.Hy:0.000}",
                series,
                new AnalysisPlotOptions(
                    Title: $"Hx: {field.Hx:0.000}, Hy: {field.Hy:0.000}",
                    EqualAspect: true,
                    XMinimum: -axisLimit,
                    XMaximum: axisLimit,
                    YMinimum: -axisLimit,
                    YMaximum: axisLimit,
                    GridOpacity: 0.25));
        }).ToArray();
        var firstSeries = panes.FirstOrDefault()?.Series.FirstOrDefault();
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["RayCount"] = result.RayCount,
            ["VignettedRayCount"] = result.VignettedRayCount,
            ["FieldCount"] = result.Fields.Count,
            ["WavelengthCount"] = Optic.Wavelengths.Count,
            ["NumRings"] = _numRings,
            ["Distribution"] = _distribution,
            ["MaximumGeometricSpotRadius"] = axisLimit / 1.05
        }, firstSeries, firstSeries is null ? null : new[] { firstSeries }, PlotPanes: panes);
    }
}

public sealed class RayFanAnalysis : BaseAnalysis
{
    private readonly int _numPoints;

    public RayFanAnalysis(Optic optic, int numPoints = 256) : base(optic)
    {
        _numPoints = Math.Max(3, numPoints % 2 == 0 ? numPoints + 1 : numPoints);
    }

    public override string Name => "Ray Fan";

    public override AnalysisData GenerateData()
    {
        var fields = SpotAnalysisEngine.DefinedFields(Optic);
        var wavelengths = Optic.Wavelengths.ToArray();
        var primaryIndex = Array.FindIndex(wavelengths, wavelength => wavelength.IsPrimary);
        primaryIndex = primaryIndex < 0 ? 0 : primaryIndex;
        var pupil = Enumerable.Range(0, _numPoints)
            .Select(index => -1 + (2.0 * index / (_numPoints - 1.0)))
            .ToArray();
        var fieldFans = new List<(double Hx, double Hy, List<RayFanWave> Waves)>();

        foreach (var field in fields)
        {
            var waves = new List<RayFanWave>();
            foreach (var wavelength in wavelengths)
            {
                var xSamples = TraceFan(Optic, field, wavelength, pupil, xFan: true);
                var ySamples = TraceFan(Optic, field, wavelength, pupil, xFan: false);
                waves.Add(new RayFanWave(wavelength, xSamples, ySamples));
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
        ExpandPlotRange(ref yMinimum, ref yMaximum);

        var panes = new List<AnalysisPlotPane>();
        foreach (var field in fieldFans)
        {
            var title = $"Hx: {field.Hx:0.000}, Hy: {field.Hy:0.000}";
            panes.Add(new AnalysisPlotPane(title, BuildFanSeries(field.Waves, pupil, yFan: true), new AnalysisPlotOptions(
                Title: title,
                ShowVerticalZeroLine: true,
                ShowHorizontalZeroLine: true,
                XMinimum: -1,
                XMaximum: 1,
                YMinimum: yMinimum,
                YMaximum: yMaximum)));
            panes.Add(new AnalysisPlotPane(title, BuildFanSeries(field.Waves, pupil, yFan: false), new AnalysisPlotOptions(
                Title: title,
                ShowVerticalZeroLine: true,
                ShowHorizontalZeroLine: true,
                XMinimum: -1,
                XMaximum: 1,
                YMinimum: yMinimum,
                YMaximum: yMaximum)));
        }

        var firstSeries = panes.FirstOrDefault()?.Series.FirstOrDefault();
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["Samples"] = _numPoints,
            ["FieldCount"] = fields.Count,
            ["WavelengthCount"] = wavelengths.Length,
            ["MinimumRayAberration"] = allFinite.DefaultIfEmpty(0).Min(),
            ["MaximumRayAberration"] = allFinite.DefaultIfEmpty(0).Max()
        }, firstSeries, firstSeries is null ? null : new[] { firstSeries }, PlotPanes: panes, PlotPaneColumns: 2);
    }

    private static IReadOnlyList<AnalysisSeries> BuildFanSeries(
        IReadOnlyList<RayFanWave> waves,
        IReadOnlyList<double> pupil,
        bool yFan)
    {
        return waves.Select((wave, wavelengthIndex) =>
        {
            var samples = yFan ? wave.Y : wave.X;
            return new AnalysisSeries(
                yFan ? "P_y" : "P_x",
                yFan ? "epsilon_y (mm)" : "epsilon_x (mm)",
                samples.Select((sample, index) => new AnalysisPoint(
                    pupil[index],
                    sample.Intensity > 0 ? sample.Value : double.NaN)).ToArray(),
                Name: $"{wave.Wavelength.Micrometers:0.0000} \u00B5m",
                ColorIndex: wavelengthIndex);
        }).ToArray();
    }

    private static IReadOnlyList<RayFanSample> TraceFan(
        Optic optic,
        (double Hx, double Hy) field,
        Wavelength wavelength,
        IReadOnlyList<double> pupil,
        bool xFan)
    {
        var pupilSamples = pupil.Select(value => new PupilSample(xFan ? value : 0, xFan ? 0 : value, 1));
        var bundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(
            field.Hx,
            field.Hy,
            wavelength.Micrometers,
            pupilSamples);
        return optic.SequentialRayTracer.Trace(bundle).RayHistories.Select(history =>
        {
            if (history.Count == 0)
            {
                return new RayFanSample(double.NaN, 0);
            }

            var sample = history[^1];
            return new RayFanSample(xFan ? sample.Position.X : sample.Position.Y, sample.Intensity);
        }).ToArray();
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
        IReadOnlyList<RayFanSample> X,
        IReadOnlyList<RayFanSample> Y);
}

public sealed class FirstOrderAnalysis : BaseAnalysis
{
    public FirstOrderAnalysis(Optic optic) : base(optic)
    {
    }

    public override string Name => "First Order";

    public override AnalysisData GenerateData()
    {
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["EffectiveFocalLength"] = Optic.Paraxial.EstimateEffectiveFocalLength(),
            ["FNumber"] = Optic.Paraxial.EstimateFNumber(),
            ["TotalTrack"] = Optic.SurfaceGroup.TotalTrack
        });
    }
}

public sealed class DistortionAnalysis : BaseAnalysis
{
    private readonly int _numPoints;
    private readonly string _distortionType;

    public DistortionAnalysis(Optic optic, int numPoints = 128, string distortionType = "f-tan") : base(optic)
    {
        _numPoints = Math.Max(2, numPoints);
        _distortionType = AnalysisTrace.NormalizeDistortionType(distortionType);
    }

    public override string Name => "Distortion";

    public override AnalysisData GenerateData()
    {
        const double epsilon = 1e-10;
        var maxField = AnalysisTrace.MaxFieldDegrees(Optic);
        var fieldRadians = maxField * Math.PI / 180.0;
        var wavelengths = Optic.Wavelengths.ToArray();
        var series = new List<AnalysisSeries>();
        var maximumAbsoluteDistortion = 0.0;

        for (var wavelengthIndex = 0; wavelengthIndex < wavelengths.Length; wavelengthIndex++)
        {
            var wavelength = wavelengths[wavelengthIndex];
            var referenceHeight = AnalysisTrace.FinalSample(Optic, 0, epsilon, 0, 0, wavelength.Micrometers).Position.Y;
            var referenceAngle = epsilon * fieldRadians;
            var constant = referenceHeight / Math.Tan(referenceAngle);
            var points = new AnalysisPoint[_numPoints];

            for (var index = 0; index < _numPoints; index++)
            {
                var normalizedField = epsilon + ((1.0 - epsilon) * index / (_numPoints - 1.0));
                var actualHeight = AnalysisTrace.FinalSample(Optic, 0, normalizedField, 0, 0, wavelength.Micrometers).Position.Y;
                var angle = normalizedField * fieldRadians;
                var idealHeight = constant * (_distortionType == "f-theta" ? angle : Math.Tan(angle));
                var distortion = Math.Abs(idealHeight) <= 1e-30 ? 0 : 100.0 * (actualHeight - idealHeight) / idealHeight;
                maximumAbsoluteDistortion = Math.Max(maximumAbsoluteDistortion, Math.Abs(distortion));
                points[index] = new AnalysisPoint(distortion, normalizedField * maxField);
            }

            series.Add(new AnalysisSeries(
                "Distortion (%)",
                "Field",
                points,
                Name: $"{wavelength.Micrometers:0.0000} \u00B5m",
                ColorIndex: wavelengthIndex));
        }

        var first = series.FirstOrDefault();
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["MaxFieldDegrees"] = maxField,
            ["DistortionType"] = _distortionType,
            ["Samples"] = _numPoints,
            ["WavelengthCount"] = wavelengths.Length,
            ["MaximumAbsoluteDistortionPercent"] = maximumAbsoluteDistortion
        }, first, series, new AnalysisPlotOptions(
            SymmetricX: true,
            ShowVerticalZeroLine: true,
            VerticalZeroLineStyle: AnalysisLineStyle.Dashed,
            VerticalZeroLineWidth: 1,
            YMinimum: 0,
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
        var maxField = AnalysisTrace.MaxFieldDegrees(Optic);
        var wavelengths = Optic.Wavelengths.ToArray();
        var series = new List<AnalysisSeries>();
        var maximumAbsoluteDelta = 0.0;

        for (var wavelengthIndex = 0; wavelengthIndex < wavelengths.Length; wavelengthIndex++)
        {
            var wavelength = wavelengths[wavelengthIndex];
            var tangential = new AnalysisPoint[_numPoints];
            var sagittal = new AnalysisPoint[_numPoints];

            for (var index = 0; index < _numPoints; index++)
            {
                var normalizedField = index / (_numPoints - 1.0);
                var t1 = AnalysisTrace.FinalSample(Optic, 0, normalizedField, 0, -_parabasalDelta, wavelength.Micrometers);
                var t2 = AnalysisTrace.FinalSample(Optic, 0, normalizedField, 0, _parabasalDelta, wavelength.Micrometers);
                var tDenominator = (t1.Direction.Y * t2.Direction.Z) - (t2.Direction.Y * t1.Direction.Z);
                var tangentialDelta = Math.Abs(tDenominator) <= 1e-30
                    ? 0
                    : ((t2.Direction.Y * t1.Position.Z)
                        - (t2.Direction.Y * t2.Position.Z)
                        - (t2.Direction.Z * t1.Position.Y)
                        + (t2.Direction.Z * t2.Position.Y)) / tDenominator * t1.Direction.Z;

                var s1 = AnalysisTrace.FinalSample(Optic, 0, normalizedField, -_parabasalDelta, 0, wavelength.Micrometers);
                var s2 = AnalysisTrace.FinalSample(Optic, 0, normalizedField, _parabasalDelta, 0, wavelength.Micrometers);
                var sDenominator = (s1.Direction.X * s2.Direction.Z) - (s2.Direction.X * s1.Direction.Z);
                var sagittalDelta = Math.Abs(sDenominator) <= 1e-30
                    ? 0
                    : ((s2.Direction.X * s1.Position.Z)
                        - (s2.Direction.X * s2.Position.Z)
                        - (s2.Direction.Z * s1.Position.X)
                        + (s2.Direction.Z * s2.Position.X)) / sDenominator * s1.Direction.Z;

                var field = normalizedField * maxField;
                tangential[index] = new AnalysisPoint(tangentialDelta, field);
                sagittal[index] = new AnalysisPoint(sagittalDelta, field);
                maximumAbsoluteDelta = Math.Max(maximumAbsoluteDelta, Math.Max(Math.Abs(tangentialDelta), Math.Abs(sagittalDelta)));
            }

            var wavelengthLabel = $"{wavelength.Micrometers:0.0000} \u00B5m";
            series.Add(new AnalysisSeries(
                "Image Plane Delta (mm)",
                "Field",
                tangential,
                Name: $"{wavelengthLabel}, Tangential",
                ColorIndex: wavelengthIndex));
            series.Add(new AnalysisSeries(
                "Image Plane Delta (mm)",
                "Field",
                sagittal,
                Name: $"{wavelengthLabel}, Sagittal",
                LineStyle: AnalysisLineStyle.Dashed,
                ColorIndex: wavelengthIndex));
        }

        var first = series.FirstOrDefault();
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["MaxFieldDegrees"] = maxField,
            ["Samples"] = _numPoints,
            ["ParabasalDelta"] = _parabasalDelta,
            ["WavelengthCount"] = wavelengths.Length,
            ["MaximumAbsoluteImagePlaneDelta"] = maximumAbsoluteDelta
        }, first, series, new AnalysisPlotOptions(
            Title: "Field Curvature",
            SymmetricX: true,
            ShowVerticalZeroLine: true,
            YMinimum: 0,
            YMaximum: maxField,
            ShowLegend: true));
    }
}

public sealed class EncircledEnergyAnalysis : BaseAnalysis
{
    private readonly int _numRays;
    private readonly string _distribution;
    private readonly int _numPoints;

    public EncircledEnergyAnalysis(
        Optic optic,
        int numRays = 100_000,
        string distribution = "random",
        int numPoints = 256) : base(optic)
    {
        _numRays = Math.Max(1, numRays);
        _distribution = distribution;
        _numPoints = Math.Max(2, numPoints);
    }

    public override string Name => "Encircled Energy";

    public override AnalysisData GenerateData()
    {
        var fields = SpotAnalysisEngine.DefinedFields(Optic);
        var primary = Optic.Wavelengths.FirstOrDefault(wavelength => wavelength.IsPrimary)
            ?? Optic.Wavelengths.FirstOrDefault();
        if (primary is null)
        {
            return new AnalysisData(Name, new Dictionary<string, object> { ["Status"] = "No wavelengths" });
        }

        var result = SpotAnalysisEngine.Generate(Optic, fields, new[] { primary }, _numRays, _distribution);
        var geometricRadius = result.Fields
            .SelectMany(field => field.Wavelengths)
            .SelectMany(wavelength => wavelength.Rays)
            .Select(ray => Math.Sqrt((ray.X * ray.X) + (ray.Y * ray.Y)))
            .DefaultIfEmpty(0)
            .Max();
        var radiusMaximum = geometricRadius * 1.2;
        var series = result.Fields.Select((field, fieldIndex) =>
        {
            var rays = field.Wavelengths[0].Rays;
            var points = Enumerable.Range(0, _numPoints).Select(index =>
            {
                var radius = radiusMaximum * index / (_numPoints - 1.0);
                var energy = rays
                    .Where(ray => Math.Sqrt((ray.X * ray.X) + (ray.Y * ray.Y)) <= radius)
                    .Sum(ray => ray.Intensity);
                return new AnalysisPoint(radius, energy);
            }).ToArray();
            return new AnalysisSeries(
                "Radius (mm)",
                "Encircled Energy (-)",
                points,
                Name: $"Hx: {field.Hx:0.000}, Hy: {field.Hy:0.000}",
                ColorIndex: fieldIndex);
        }).ToArray();
        var allRays = result.Fields
            .SelectMany(field => field.Wavelengths)
            .SelectMany(wavelength => wavelength.Rays)
            .ToArray();
        var totalWeight = allRays.Sum(ray => ray.Intensity);
        var weightedRadii = allRays
            .Select(ray => (
                Radius: Math.Sqrt((ray.X * ray.X) + (ray.Y * ray.Y)),
                Weight: ray.Intensity))
            .OrderBy(item => item.Radius)
            .ToArray();
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["RayCount"] = result.RayCount,
            ["VignettedRayCount"] = result.VignettedRayCount,
            ["FieldCount"] = result.Fields.Count,
            ["WavelengthMicrometers"] = primary.Micrometers,
            ["NumRays"] = _numRays,
            ["Distribution"] = _distribution,
            ["PlotPointCount"] = _numPoints,
            ["MaximumGeometricSpotRadius"] = geometricRadius,
            ["TotalWeight"] = totalWeight,
            ["Radius50"] = RadiusAtEnergy(weightedRadii, totalWeight, 0.50),
            ["Radius80"] = RadiusAtEnergy(weightedRadii, totalWeight, 0.80),
            ["Radius95"] = RadiusAtEnergy(weightedRadii, totalWeight, 0.95)
        }, series.FirstOrDefault(), series, new AnalysisPlotOptions(
            Title: $"Wavelength: {primary.Micrometers:0.0000} \u00B5m",
            XMinimum: 0,
            YMinimum: 0,
            ShowLegend: true));
    }

    private static double RadiusAtEnergy(
        IReadOnlyList<(double Radius, double Weight)> radii,
        double totalWeight,
        double fraction)
    {
        var target = totalWeight * fraction;
        var cumulative = 0.0;
        foreach (var item in radii)
        {
            cumulative += item.Weight;
            if (cumulative >= target)
            {
                return item.Radius;
            }
        }

        return radii.Count == 0 ? 0 : radii[^1].Radius;
    }
}

public sealed class PupilAberrationAnalysis : BaseAnalysis
{
    private readonly int _numPoints;

    public PupilAberrationAnalysis(Optic optic, int numPoints = 256) : base(optic)
    {
        _numPoints = Math.Max(3, numPoints % 2 == 0 ? numPoints + 1 : numPoints);
    }

    public override string Name => "Pupil Aberration";

    public override AnalysisData GenerateData()
    {
        var fields = SpotAnalysisEngine.DefinedFields(Optic);
        var wavelengths = Optic.Wavelengths.ToArray();
        var primary = wavelengths.FirstOrDefault(wavelength => wavelength.IsPrimary)
            ?? wavelengths.FirstOrDefault();
        if (primary is null)
        {
            return new AnalysisData(Name, new Dictionary<string, object> { ["Status"] = "No wavelengths" });
        }

        var stopIndex = Optic.SurfaceGroup.Items.ToList().FindIndex(surface => surface.IsStop);
        stopIndex = stopIndex < 0 ? 0 : stopIndex;
        var pupil = Enumerable.Range(0, _numPoints)
            .Select(index => -1 + (2.0 * index / (_numPoints - 1.0)))
            .ToArray();
        var paraxial = Optic.Paraxial.TraceNormalizedPupil(0, pupil, primary.Micrometers);
        var paraxialReference = paraxial.Heights[stopIndex].ToArray();
        var stopRadius = Optic.Paraxial.TraceNormalizedPupil(0, new[] { 1.0 }, primary.Micrometers).Heights[stopIndex][0];
        var fieldData = new List<(double Hx, double Hy, List<PupilWave> Waves)>();
        foreach (var field in fields)
        {
            var waves = new List<PupilWave>();
            foreach (var wavelength in wavelengths)
            {
                var realX = TraceAtSurface(Optic, field, wavelength, pupil, stopIndex, xFan: true);
                var realY = TraceAtSurface(Optic, field, wavelength, pupil, stopIndex, xFan: false);
                var errorX = realX.Select((sample, index) => new RayFanSample(
                    Math.Abs(stopRadius) <= 1e-30 ? 0 : (paraxialReference[index] - sample.Value) / stopRadius * 100,
                    sample.Intensity)).ToArray();
                var errorY = realY.Select((sample, index) => new RayFanSample(
                    Math.Abs(stopRadius) <= 1e-30 ? 0 : (paraxialReference[index] - sample.Value) / stopRadius * 100,
                    sample.Intensity)).ToArray();
                waves.Add(new PupilWave(wavelength, errorX, errorY));
            }

            fieldData.Add((field.Hx, field.Hy, waves));
        }

        var finite = fieldData.SelectMany(field => field.Waves)
            .SelectMany(wave => wave.X.Concat(wave.Y))
            .Where(point => point.Intensity > 0 && double.IsFinite(point.Value))
            .Select(point => point.Value)
            .ToArray();
        var yMinimum = finite.DefaultIfEmpty(-1).Min();
        var yMaximum = finite.DefaultIfEmpty(1).Max();
        ExpandRange(ref yMinimum, ref yMaximum);
        var panes = new List<AnalysisPlotPane>();
        foreach (var field in fieldData)
        {
            var title = $"Hx: {field.Hx:0.000}, Hy: {field.Hy:0.000}";
            panes.Add(PupilPane(field.Waves, pupil, title, yMinimum, yMaximum, yFan: true));
            panes.Add(PupilPane(field.Waves, pupil, title, yMinimum, yMaximum, yFan: false));
        }

        var firstSeries = panes.FirstOrDefault()?.Series.FirstOrDefault();
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["Samples"] = _numPoints,
            ["FieldCount"] = fields.Count,
            ["WavelengthCount"] = wavelengths.Length,
            ["ParaxialStopRadius"] = stopRadius,
            ["MinimumPupilAberration"] = finite.DefaultIfEmpty(0).Min(),
            ["MaximumPupilAberration"] = finite.DefaultIfEmpty(0).Max()
        }, firstSeries, firstSeries is null ? null : new[] { firstSeries }, PlotPanes: panes, PlotPaneColumns: 2);
    }

    private static AnalysisPlotPane PupilPane(
        IReadOnlyList<PupilWave> waves,
        IReadOnlyList<double> pupil,
        string title,
        double yMinimum,
        double yMaximum,
        bool yFan)
    {
        var series = waves.Select((wave, wavelengthIndex) =>
        {
            var samples = yFan ? wave.Y : wave.X;
            return new AnalysisSeries(
                yFan ? "P_y" : "P_x",
                "Pupil Aberration (%)",
                samples.Select((sample, index) => new AnalysisPoint(
                    pupil[index],
                    sample.Intensity > 0 ? sample.Value : double.NaN)).ToArray(),
                Name: $"{wave.Wavelength.Micrometers:0.0000} \u00B5m",
                ColorIndex: wavelengthIndex);
        }).ToArray();
        return new AnalysisPlotPane(title, series, new AnalysisPlotOptions(
            Title: title,
            ShowVerticalZeroLine: true,
            ShowHorizontalZeroLine: true,
            XMinimum: -1,
            XMaximum: 1,
            YMinimum: yMinimum,
            YMaximum: yMaximum));
    }

    private static IReadOnlyList<RayFanSample> TraceAtSurface(
        Optic optic,
        (double Hx, double Hy) field,
        Wavelength wavelength,
        IReadOnlyList<double> pupil,
        int surfaceIndex,
        bool xFan)
    {
        var pupilSamples = pupil.Select(value => new PupilSample(xFan ? value : 0, xFan ? 0 : value, 1));
        var bundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(
            field.Hx,
            field.Hy,
            wavelength.Micrometers,
            pupilSamples);
        return optic.SequentialRayTracer.Trace(bundle).RayHistories.Select(history =>
        {
            var sample = history.FirstOrDefault(item => item.SurfaceNumber == surfaceIndex);
            return sample is null
                ? new RayFanSample(double.NaN, 0)
                : new RayFanSample(xFan ? sample.Position.X : sample.Position.Y, sample.Intensity);
        }).ToArray();
    }

    private static void ExpandRange(ref double minimum, ref double maximum)
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

    private sealed record PupilWave(
        Wavelength Wavelength,
        IReadOnlyList<RayFanSample> X,
        IReadOnlyList<RayFanSample> Y);
}

public sealed class RmsVsFieldAnalysis : BaseAnalysis
{
    private readonly int _numFields;
    private readonly int _numRings;
    private readonly string _distribution;

    public RmsVsFieldAnalysis(
        Optic optic,
        int numFields = 64,
        int numRings = 6,
        string distribution = "hexapolar") : base(optic)
    {
        _numFields = Math.Max(2, numFields);
        _numRings = Math.Max(1, numRings);
        _distribution = distribution;
    }

    public override string Name => "RMS vs Field";

    public override AnalysisData GenerateData()
    {
        var fields = Enumerable.Range(0, _numFields)
            .Select(index => (Hx: 0.0, Hy: index / (_numFields - 1.0)))
            .ToArray();
        var result = SpotAnalysisEngine.Generate(Optic, fields, Optic.Wavelengths, _numRings, _distribution);
        var series = Optic.Wavelengths.Select((wavelength, wavelengthIndex) => new AnalysisSeries(
            "Normalized Y Field Coordinate",
            "RMS Spot Size (mm)",
            result.Fields.Select(field => new AnalysisPoint(
                field.Hy,
                SpotAnalysisEngine.RmsRadius(field.Wavelengths[wavelengthIndex].Rays))).ToArray(),
            Name: $"{wavelength.Micrometers:0.0000} \u00B5m",
            ColorIndex: wavelengthIndex)).ToArray();
        var maximum = series.SelectMany(item => item.Points).Select(point => point.Y).DefaultIfEmpty(0).Max();
        var values = new Dictionary<string, object>
        {
            ["FieldCount"] = _numFields,
            ["WavelengthCount"] = Optic.Wavelengths.Count,
            ["NumRings"] = _numRings,
            ["Distribution"] = _distribution,
            ["MaximumRmsSpotSize"] = maximum
        };
        var definedFields = new AnalysisRunner(Optic).EvaluateRmsByField();
        foreach (var field in definedFields)
        {
            values[$"Field {field.FieldLabel}"] = field.RmsSpotRadius;
        }

        var includedWeight = definedFields.Where(field => field.FieldWeight > 0).Sum(field => field.FieldWeight);
        values["IncludedFieldWeight"] = includedWeight;
        values["WeightedMean"] = includedWeight <= 1e-12
            ? 0
            : definedFields.Where(field => field.FieldWeight > 0)
                .Sum(field => field.RmsSpotRadius * field.FieldWeight) / includedWeight;
        return new AnalysisData(Name, values, series.FirstOrDefault(), series, new AnalysisPlotOptions(
            XMinimum: 0,
            XMaximum: 1,
            YMinimum: 0,
            ShowLegend: true));
    }
}

public sealed class RmsWavefrontVsFieldAnalysis : BaseAnalysis
{
    private readonly int _numFields;
    private readonly int _numRings;

    public RmsWavefrontVsFieldAnalysis(Optic optic, int numFields = 32, int numRings = 12) : base(optic)
    {
        _numFields = Math.Max(2, numFields);
        _numRings = Math.Max(1, numRings);
    }

    public override string Name => "RMS Wavefront vs Field";

    public override AnalysisData GenerateData()
    {
        var fields = Enumerable.Range(0, _numFields)
            .Select(index => index / (double)(_numFields - 1))
            .ToArray();
        var series = Optic.Wavelengths.Select((wavelength, wavelengthIndex) => new AnalysisSeries(
            "Normalized Y Field Coordinate",
            "RMS Wavefront Error (waves)",
            fields.Select(field =>
            {
                var wavefront = WavefrontEngine.GenerateChiefRay(Optic, (0, field), wavelength, _numRings);
                return new AnalysisPoint(field, wavefront.Rms);
            }).ToArray(),
            Name: $"{wavelength.Micrometers:0.0000} \u00B5m",
            ColorIndex: wavelengthIndex)).ToArray();
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["FieldCount"] = fields.Length,
            ["WavelengthCount"] = Optic.Wavelengths.Count,
            ["NumRings"] = _numRings,
            ["MaximumRmsWavefrontError"] = series.SelectMany(item => item.Points).Select(point => point.Y).DefaultIfEmpty(0).Max()
        }, series.FirstOrDefault(), series, new AnalysisPlotOptions(
            XMinimum: 0,
            XMaximum: 1,
            YMinimum: 0,
            ShowLegend: true,
            GridOpacity: 0.25));
    }
}

public enum AngleScanMode
{
    ThroughPupil,
    ThroughField
}

public sealed class IncidentAngleVsHeightAnalysis : BaseAnalysis
{
    private readonly AngleScanMode _mode;
    private readonly int _surfaceIndex;
    private readonly int _axis;
    private readonly int _numPoints;
    private readonly (double X, double Y) _fixedCoordinate;

    public IncidentAngleVsHeightAnalysis(
        Optic optic,
        AngleScanMode mode,
        int surfaceIndex = -1,
        int axis = 1,
        int numPoints = 128,
        (double X, double Y)? fixedCoordinate = null) : base(optic)
    {
        _mode = mode;
        _surfaceIndex = surfaceIndex;
        _axis = axis == 0 ? 0 : 1;
        _numPoints = Math.Max(2, numPoints);
        _fixedCoordinate = fixedCoordinate ?? (0, 0);
    }

    public override string Name => _mode == AngleScanMode.ThroughPupil
        ? "Angle vs Image Height - Through Pupil"
        : "Angle vs Image Height - Through Field";

    public override AnalysisData GenerateData()
    {
        var wavelength = Optic.Wavelengths.FirstOrDefault(item => item.IsPrimary)
            ?? Optic.Wavelengths.FirstOrDefault();
        if (wavelength is null || Optic.SurfaceGroup.Items.Count == 0)
        {
            return new AnalysisData(Name, new Dictionary<string, object> { ["Status"] = "No optical data" });
        }

        var surfaceIndex = _surfaceIndex < 0
            ? Optic.SurfaceGroup.Items.Count + _surfaceIndex
            : _surfaceIndex;
        surfaceIndex = Math.Clamp(surfaceIndex, 0, Optic.SurfaceGroup.Items.Count - 1);
        var scan = Enumerable.Range(0, _numPoints)
            .Select(index => -1 + (2.0 * index / (_numPoints - 1)))
            .ToArray();
        var points = new List<AnalysisPoint>(_numPoints);
        foreach (var coordinate in scan)
        {
            var hx = _mode == AngleScanMode.ThroughField && _axis == 0 ? coordinate : _fixedCoordinate.X;
            var hy = _mode == AngleScanMode.ThroughField && _axis == 1 ? coordinate : _fixedCoordinate.Y;
            var px = _mode == AngleScanMode.ThroughPupil && _axis == 0 ? coordinate : _fixedCoordinate.X;
            var py = _mode == AngleScanMode.ThroughPupil && _axis == 1 ? coordinate : _fixedCoordinate.Y;
            var history = Optic.TraceGeneric(hx, hy, px, py, wavelength.Micrometers).RayHistories.Single();
            if (history.Count <= surfaceIndex)
            {
                points.Add(new AnalysisPoint(double.NaN, double.NaN, Value: coordinate));
                continue;
            }

            var sample = history[surfaceIndex];
            var height = _axis == 1 ? sample.Position.Y : sample.Position.X;
            var directionCosine = _axis == 1 ? sample.Direction.Y : sample.Direction.X;
            var angle = Math.Asin(Math.Clamp(directionCosine, -1, 1)) * 180 / Math.PI;
            points.Add(new AnalysisPoint(height, angle, Value: coordinate));
        }

        var fixedLabel = _mode == AngleScanMode.ThroughPupil
            ? $"Hx={_fixedCoordinate.X:0.####} Hy={_fixedCoordinate.Y:0.####}"
            : $"Px={_fixedCoordinate.X:0.####} Py={_fixedCoordinate.Y:0.####}";
        var valueLabel = _mode == AngleScanMode.ThroughPupil
            ? $"Normalized Pupil Coordinate ({(_axis == 0 ? "Px" : "Py")})"
            : $"Normalized Field Coordinate ({(_axis == 0 ? "Hx" : "Hy")})";
        var series = new AnalysisSeries(
            "Image Height in Millimeters",
            "Incident Angle in Degrees",
            points,
            AnalysisSeriesKind.ColoredLine,
            $"{fixedLabel}, {wavelength.Micrometers:0.0000} \u00B5m",
            LineWidth: 3,
            ValueLabel: valueLabel);
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["ScanMode"] = _mode.ToString(),
            ["SurfaceIndex"] = surfaceIndex,
            ["Axis"] = _axis == 0 ? "X" : "Y",
            ["WavelengthMicrometers"] = wavelength.Micrometers,
            ["PointCount"] = points.Count,
            ["FixedCoordinates"] = fixedLabel
        }, series, new[] { series }, new AnalysisPlotOptions(
            Title: $"Incident Angle vs Image Height{(_axis == 0 ? " (x-axis)" : string.Empty)}",
            GridOpacity: 0.25));
    }
}

public sealed class ThroughFocusAnalysis : BaseAnalysis
{
    private readonly double _deltaFocus;
    private readonly int _numSteps;
    private readonly int _numRings;
    private readonly string _distribution;

    public ThroughFocusAnalysis(
        Optic optic,
        double deltaFocus = 0.1,
        int numSteps = 5,
        int numRings = 6,
        string distribution = "hexapolar") : base(optic)
    {
        if (numSteps < 1 || numSteps > 7 || numSteps % 2 == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(numSteps), "Through-focus steps must be an odd integer from 1 through 7.");
        }

        _deltaFocus = deltaFocus;
        _numSteps = numSteps;
        _numRings = Math.Max(1, numRings);
        _distribution = distribution;
    }

    public override string Name => "Through Focus";

    public override AnalysisData GenerateData()
    {
        var fields = SpotAnalysisEngine.DefinedFields(Optic);
        var offsets = Enumerable.Range(0, _numSteps)
            .Select(index => (index - (_numSteps / 2)) * _deltaFocus)
            .ToArray();
        var results = offsets.Select(offset => SpotAnalysisEngine.Generate(
            Optic,
            fields,
            Optic.Wavelengths,
            _numRings,
            _distribution,
            offset)).ToArray();
        var axisLimit = results
            .SelectMany(result => result.Fields)
            .SelectMany(field => field.Wavelengths)
            .SelectMany(wavelength => wavelength.Rays)
            .Select(ray => Math.Sqrt((ray.X * ray.X) + (ray.Y * ray.Y)))
            .DefaultIfEmpty(0.01)
            .Max() * 1.05;
        axisLimit = axisLimit <= 1e-12 ? 0.01 : axisLimit;
        var panes = new List<AnalysisPlotPane>(_numSteps * fields.Count);
        for (var fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
        {
            for (var stepIndex = 0; stepIndex < _numSteps; stepIndex++)
            {
                var field = fields[fieldIndex];
                var title = fieldIndex == 0
                    ? $"Defocus: {offsets[stepIndex]:+0.000;-0.000;+0.000} mm\nField: ({field.Hx:0.00},{field.Hy:0.00})"
                    : $"Field: ({field.Hx:0.00},{field.Hy:0.00})";
                var series = results[stepIndex].Fields[fieldIndex].Wavelengths
                    .Select((wavelength, wavelengthIndex) => new AnalysisSeries(
                        fieldIndex == fields.Count - 1 ? "X (mm)" : "",
                        stepIndex == 0 ? "Y (mm)" : "",
                        wavelength.Rays.Select(ray => new AnalysisPoint(ray.X, ray.Y)).ToArray(),
                        AnalysisSeriesKind.Scatter,
                        $"{wavelength.Wavelength.Micrometers:0.0000} \u00B5m",
                        ColorIndex: wavelengthIndex,
                        MarkerStyle: (AnalysisMarkerStyle)(wavelengthIndex % 3),
                        MarkerSize: 2.5,
                        Opacity: 0.7)).ToArray();
                panes.Add(new AnalysisPlotPane(title, series, new AnalysisPlotOptions(
                    Title: title,
                    EqualAspect: true,
                    XMinimum: -axisLimit,
                    XMaximum: axisLimit,
                    YMinimum: -axisLimit,
                    YMaximum: axisLimit,
                    GridOpacity: 0.25)));
            }
        }

        var legacy = new AnalysisRunner(Optic).EvaluateThroughFocus();
        var points = legacy.Points.ToArray();
        var legacySeries = new AnalysisSeries(
            "Focus shift (mm)",
            "RMS spot radius (mm)",
            points.Select(point => new AnalysisPoint(point.FocusShift, point.RmsSpotRadius)).ToArray());
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["FocusStep"] = _deltaFocus,
            ["FocusPlaneCount"] = _numSteps,
            ["NumRings"] = _numRings,
            ["Distribution"] = _distribution,
            ["Minus2StepRms"] = points.ElementAtOrDefault(0)?.RmsSpotRadius ?? 0,
            ["Minus1StepRms"] = points.ElementAtOrDefault(1)?.RmsSpotRadius ?? 0,
            ["NominalRms"] = points.ElementAtOrDefault(2)?.RmsSpotRadius ?? 0,
            ["Plus1StepRms"] = points.ElementAtOrDefault(3)?.RmsSpotRadius ?? 0,
            ["Plus2StepRms"] = points.ElementAtOrDefault(4)?.RmsSpotRadius ?? 0,
            ["BestFocusShift"] = legacy.BestFocusShift,
            ["BestRmsSpotRadius"] = legacy.BestRmsSpotRadius,
            ["Radius80AtBest"] = points.OrderBy(point => point.RmsSpotRadius).FirstOrDefault()?.Radius80 ?? 0
        }, legacySeries, new[] { legacySeries }, PlotPanes: panes, PlotPaneColumns: _numSteps);
    }
}

public sealed class ThroughFocusMtfAnalysis : BaseAnalysis
{
    private readonly double _spatialFrequency;
    private readonly double _deltaFocus;
    private readonly int _numSteps;
    private readonly int _pupilSampling;

    public ThroughFocusMtfAnalysis(
        Optic optic,
        double spatialFrequency = 20,
        double deltaFocus = 0.1,
        int numSteps = 5,
        int pupilSampling = 128) : base(optic)
    {
        _spatialFrequency = Math.Max(0, spatialFrequency);
        _deltaFocus = Math.Abs(deltaFocus);
        _numSteps = Math.Clamp(numSteps % 2 == 0 ? numSteps + 1 : numSteps, 1, 15);
        _pupilSampling = Math.Max(8, pupilSampling);
    }

    public override string Name => "Through Focus MTF";

    public override AnalysisData GenerateData()
    {
        var wavelength = Optic.Wavelengths.FirstOrDefault(item => item.IsPrimary)
            ?? Optic.Wavelengths.FirstOrDefault();
        var imageSurface = Optic.SurfaceGroup.Items.LastOrDefault();
        if (wavelength is null || imageSurface is null)
        {
            return new AnalysisData(Name, new Dictionary<string, object> { ["Status"] = "No optical data" });
        }

        var fields = SpotAnalysisEngine.DefinedFields(Optic);
        var defocus = Enumerable.Range(0, _numSteps)
            .Select(index => (index - (_numSteps / 2)) * _deltaFocus)
            .ToArray();
        var tangential = fields.Select(_ => new double[_numSteps]).ToArray();
        var sagittal = fields.Select(_ => new double[_numSteps]).ToArray();
        var originalCoordinateSystem = imageSurface.CoordinateSystem;
        try
        {
            for (var step = 0; step < _numSteps; step++)
            {
                imageSurface.CoordinateSystem = originalCoordinateSystem with
                {
                    Origin = originalCoordinateSystem.Origin with
                    {
                        Z = originalCoordinateSystem.Origin.Z + defocus[step]
                    }
                };
                for (var fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
                {
                    tangential[fieldIndex][step] = SampledMtfEngine.Calculate(
                        Optic, fields[fieldIndex], wavelength, _spatialFrequency, 0, _pupilSampling);
                    sagittal[fieldIndex][step] = SampledMtfEngine.Calculate(
                        Optic, fields[fieldIndex], wavelength, 0, _spatialFrequency, _pupilSampling);
                }
            }
        }
        finally
        {
            imageSurface.CoordinateSystem = originalCoordinateSystem;
        }

        var smoothDefocus = _numSteps < 2
            ? defocus
            : Enumerable.Range(0, 256)
                .Select(index => defocus[0] + ((defocus[^1] - defocus[0]) * index / 255.0))
                .ToArray();
        var series = new List<AnalysisSeries>(fields.Count * 2);
        for (var fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
        {
            var field = fields[fieldIndex];
            var tangentialSmooth = Interpolate(defocus, tangential[fieldIndex], smoothDefocus);
            var sagittalSmooth = Interpolate(defocus, sagittal[fieldIndex], smoothDefocus);
            series.Add(new AnalysisSeries(
                "Defocus (mm)",
                "MTF",
                smoothDefocus.Select((x, index) => new AnalysisPoint(x, Math.Clamp(tangentialSmooth[index], 0, 1))).ToArray(),
                Name: $"Hx: {field.Hx:0.0}, Hy: {field.Hy:0.0}, Tangential",
                ColorIndex: fieldIndex));
            series.Add(new AnalysisSeries(
                "Defocus (mm)",
                "MTF",
                smoothDefocus.Select((x, index) => new AnalysisPoint(x, Math.Clamp(sagittalSmooth[index], 0, 1))).ToArray(),
                Name: $"Hx: {field.Hx:0.0}, Hy: {field.Hy:0.0}, Sagittal",
                LineStyle: AnalysisLineStyle.Dashed,
                ColorIndex: fieldIndex));
        }

        var values = new Dictionary<string, object>
        {
            ["SpatialFrequency"] = _spatialFrequency,
            ["WavelengthMicrometers"] = wavelength.Micrometers,
            ["FocusPlaneCount"] = _numSteps,
            ["FocusStep"] = _deltaFocus,
            ["PupilSampling"] = _pupilSampling,
            ["RawTangential"] = tangential,
            ["RawSagittal"] = sagittal
        };
        return new AnalysisData(Name, values, series.FirstOrDefault(), series, new AnalysisPlotOptions(
            Title: $"Through-Focus MTF at {_spatialFrequency:0.###} cycles/mm, \u03BB={wavelength.Micrometers:0.000} \u00B5m",
            XMinimum: defocus[0],
            XMaximum: defocus[^1],
            YMinimum: 0,
            YMaximum: 1.05,
            ShowLegend: true,
            DottedGrid: true,
            GridOpacity: 0.5));
    }

    private static double[] Interpolate(IReadOnlyList<double> x, IReadOnlyList<double> y, IReadOnlyList<double> target)
    {
        if (x.Count == 1)
        {
            return target.Select(_ => y[0]).ToArray();
        }

        if (x.Count < 4)
        {
            return target.Select(value => LinearInterpolate(x, y, value)).ToArray();
        }

        var intervals = x.Count - 1;
        var unknowns = intervals * 4;
        var matrix = new double[unknowns, unknowns];
        var rightHandSide = new double[unknowns];
        var equation = 0;
        for (var interval = 0; interval < intervals; interval++)
        {
            var h = x[interval + 1] - x[interval];
            matrix[equation, (interval * 4)] = 1;
            rightHandSide[equation++] = y[interval];
            matrix[equation, (interval * 4)] = 1;
            matrix[equation, (interval * 4) + 1] = h;
            matrix[equation, (interval * 4) + 2] = h * h;
            matrix[equation, (interval * 4) + 3] = h * h * h;
            rightHandSide[equation++] = y[interval + 1];
        }

        for (var knot = 1; knot < x.Count - 1; knot++)
        {
            var leftInterval = knot - 1;
            var h = x[knot] - x[knot - 1];
            matrix[equation, (leftInterval * 4) + 1] = 1;
            matrix[equation, (leftInterval * 4) + 2] = 2 * h;
            matrix[equation, (leftInterval * 4) + 3] = 3 * h * h;
            matrix[equation, (knot * 4) + 1] = -1;
            equation++;
            matrix[equation, (leftInterval * 4) + 2] = 2;
            matrix[equation, (leftInterval * 4) + 3] = 6 * h;
            matrix[equation, (knot * 4) + 2] = -2;
            equation++;
        }

        matrix[equation, 3] = 1;
        matrix[equation++, 7] = -1;
        matrix[equation, ((intervals - 2) * 4) + 3] = 1;
        matrix[equation, ((intervals - 1) * 4) + 3] = -1;
        var coefficients = Solve(matrix, rightHandSide);
        return target.Select(value =>
        {
            var interval = Math.Clamp(x.ToList().FindLastIndex(item => item <= value), 0, intervals - 1);
            var t = value - x[interval];
            return coefficients[interval * 4]
                + (coefficients[(interval * 4) + 1] * t)
                + (coefficients[(interval * 4) + 2] * t * t)
                + (coefficients[(interval * 4) + 3] * t * t * t);
        }).ToArray();
    }

    private static double LinearInterpolate(IReadOnlyList<double> x, IReadOnlyList<double> y, double value)
    {
        var interval = Math.Clamp(x.ToList().FindLastIndex(item => item <= value), 0, x.Count - 2);
        var fraction = (value - x[interval]) / (x[interval + 1] - x[interval]);
        return y[interval] + ((y[interval + 1] - y[interval]) * fraction);
    }

    private static double[] Solve(double[,] matrix, double[] rightHandSide)
    {
        var size = rightHandSide.Length;
        var augmented = new double[size, size + 1];
        for (var row = 0; row < size; row++)
        {
            for (var column = 0; column < size; column++)
            {
                augmented[row, column] = matrix[row, column];
            }

            augmented[row, size] = rightHandSide[row];
        }

        for (var pivot = 0; pivot < size; pivot++)
        {
            var best = Enumerable.Range(pivot, size - pivot).MaxBy(row => Math.Abs(augmented[row, pivot]));
            for (var column = pivot; column <= size; column++)
            {
                (augmented[pivot, column], augmented[best, column]) = (augmented[best, column], augmented[pivot, column]);
            }

            var divisor = augmented[pivot, pivot];
            for (var column = pivot; column <= size; column++)
            {
                augmented[pivot, column] /= divisor;
            }

            for (var row = 0; row < size; row++)
            {
                if (row == pivot)
                {
                    continue;
                }

                var factor = augmented[row, pivot];
                for (var column = pivot; column <= size; column++)
                {
                    augmented[row, column] -= factor * augmented[pivot, column];
                }
            }
        }

        return Enumerable.Range(0, size).Select(row => augmented[row, size]).ToArray();
    }
}

public sealed class YYbarAnalysis : BaseAnalysis
{
    public YYbarAnalysis(Optic optic) : base(optic)
    {
    }

    public override string Name => "Y-Ybar";

    public override AnalysisData GenerateData()
    {
        var wavelength = Optic.Wavelengths.FirstOrDefault(item => item.IsPrimary)
            ?? Optic.Wavelengths.FirstOrDefault();
        if (wavelength is null)
        {
            return new AnalysisData(Name, new Dictionary<string, object> { ["Status"] = "No wavelengths" });
        }

        var marginal = Optic.Paraxial.MarginalRay(wavelength.Micrometers);
        var chief = Optic.Paraxial.ChiefRay(wavelength.Micrometers);
        var ya = marginal.Heights.Select(values => values[0]).ToArray();
        var yb = chief.Heights.Select(values => values[0]).ToArray();
        var stopIndex = Optic.SurfaceGroup.Items.ToList().FindIndex(surface => surface.IsStop);
        var series = Enumerable.Range(1, Math.Max(0, Optic.SurfaceGroup.Items.Count - 1))
            .Select(index =>
            {
                var surface = Optic.SurfaceGroup.Items[index];
                var name = index == Optic.SurfaceGroup.Items.Count - 1
                    ? "Image"
                    : index == 1 || index == stopIndex
                        ? surface.Label + (index == stopIndex ? " (Stop)" : "")
                        : "";
                return new AnalysisSeries(
                    "Chief Ray Height (mm)",
                    "Marginal Ray Height (mm)",
                    new[]
                    {
                        new AnalysisPoint(yb[index - 1], ya[index - 1]),
                        new AnalysisPoint(yb[index], ya[index])
                    },
                    Name: name,
                    ColorIndex: index - 1,
                    ShowMarkers: true,
                    MarkerSize: 4);
            }).ToArray();
        var values = new Dictionary<string, object>
        {
            ["WavelengthMicrometers"] = wavelength.Micrometers,
            ["SurfaceCount"] = Optic.SurfaceGroup.Items.Count
        };
        for (var index = 0; index < ya.Length; index++)
        {
            values[$"Surface {index} Marginal"] = ya[index];
            values[$"Surface {index} Chief"] = yb[index];
        }

        return new AnalysisData(Name, values, series.FirstOrDefault(), series, new AnalysisPlotOptions(
            Title: $"Y Y-bar Diagram (\u03BB={wavelength.Micrometers:0.000} \u00B5m)",
            ShowVerticalZeroLine: true,
            ShowHorizontalZeroLine: true,
            VerticalZeroLineWidth: 0.5,
            ShowLegend: true));
    }
}

public sealed class WavefrontAnalysis : BaseAnalysis
{
    private readonly int _numRings;
    private readonly int _mapSize;

    public WavefrontAnalysis(Optic optic, int numRings = 15, int mapSize = 65) : base(optic)
    {
        _numRings = Math.Max(2, numRings);
        _mapSize = Math.Max(17, mapSize);
    }

    public override string Name => "Wavefront";

    public override AnalysisData GenerateData()
    {
        var wavelength = Optic.Wavelengths.FirstOrDefault(item => item.IsPrimary)
            ?? Optic.Wavelengths.FirstOrDefault();
        if (wavelength is null)
        {
            return new AnalysisData(Name, new Dictionary<string, object> { ["Status"] = "No wavelengths" });
        }

        var field = SpotAnalysisEngine.DefinedFields(Optic).LastOrDefault();
        var wavefront = WavefrontEngine.GenerateChiefRay(Optic, field, wavelength, _numRings);
        var valid = wavefront.Samples.Where(sample => sample.Intensity > 0).ToArray();
        var mean = valid.Select(sample => sample.OpdWaves).DefaultIfEmpty(0).Average();
        var minimum = valid.Select(sample => sample.OpdWaves).DefaultIfEmpty(0).Min();
        var maximum = valid.Select(sample => sample.OpdWaves).DefaultIfEmpty(0).Max();
        var mapPoints = BuildWavefrontMap(valid, _mapSize);
        var series = new AnalysisSeries(
            "Pupil X",
            "Pupil Y",
            mapPoints,
            AnalysisSeriesKind.Heatmap,
            ValueLabel: "OPD (waves)");
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["RayCount"] = wavefront.Samples.Count,
            ["VignettedRayCount"] = wavefront.VignettedRayCount,
            ["ReferenceOpticalPathLength"] = wavefront.ReferenceOpticalPath,
            ["MeanOpticalPathDifference"] = mean * wavelength.Micrometers * 1e-3,
            ["RmsOpticalPathDifference"] = wavefront.Rms * wavelength.Micrometers * 1e-3,
            ["PeakToValleyOpticalPathDifference"] = (maximum - minimum) * wavelength.Micrometers * 1e-3,
            ["RmsWaves"] = wavefront.Rms,
            ["ReferenceSphereRadius"] = wavefront.Radius,
            ["FieldHx"] = field.Hx,
            ["FieldHy"] = field.Hy,
            ["WavelengthMicrometers"] = wavelength.Micrometers,
            ["Reference"] = "chief_ray"
        }, series, new[] { series }, new AnalysisPlotOptions(
            Title: $"OPD Map: RMS={wavefront.Rms:0.000} waves",
            EqualAspect: true,
            XMinimum: -1,
            XMaximum: 1,
            YMinimum: -1,
            YMaximum: 1));
    }

    internal static IReadOnlyList<AnalysisPoint> BuildWavefrontMap(
        IReadOnlyList<WavefrontSample> samples,
        int mapSize)
    {
        var points = new List<AnalysisPoint>(mapSize * mapSize);
        for (var row = 0; row < mapSize; row++)
        {
            var y = -1 + (2.0 * row / (mapSize - 1.0));
            for (var column = 0; column < mapSize; column++)
            {
                var x = -1 + (2.0 * column / (mapSize - 1.0));
                if ((x * x) + (y * y) > 1)
                {
                    continue;
                }

                var nearest = samples
                    .Select(sample => (Sample: sample, DistanceSquared:
                        ((sample.NormalizedPupilX - x) * (sample.NormalizedPupilX - x))
                        + ((sample.NormalizedPupilY - y) * (sample.NormalizedPupilY - y))))
                    .OrderBy(item => item.DistanceSquared)
                    .Take(8)
                    .ToArray();
                var exact = nearest.FirstOrDefault(item => item.DistanceSquared <= 1e-20);
                var value = exact.Sample is not null
                    ? exact.Sample.OpdWaves
                    : nearest.Sum(item => item.Sample.OpdWaves / Math.Max(1e-20, item.DistanceSquared))
                        / nearest.Sum(item => 1 / Math.Max(1e-20, item.DistanceSquared));
                points.Add(new AnalysisPoint(x, y, Value: value));
            }
        }

        return points;
    }
}

public sealed class ZernikeAnalysis : BaseAnalysis
{
    private readonly int _numRings;
    private readonly int _numTerms;
    private readonly int _mapSize;

    public ZernikeAnalysis(Optic optic, int numRings = 15, int numTerms = 37, int mapSize = 65) : base(optic)
    {
        _numRings = Math.Max(2, numRings);
        _numTerms = Math.Max(1, numTerms);
        _mapSize = Math.Max(17, mapSize);
    }

    public override string Name => "Zernike";

    public override AnalysisData GenerateData()
    {
        var wavelength = Optic.Wavelengths.FirstOrDefault(item => item.IsPrimary)
            ?? Optic.Wavelengths.FirstOrDefault();
        if (wavelength is null)
        {
            return new AnalysisData(Name, new Dictionary<string, object> { ["Status"] = "No wavelengths" });
        }

        var field = SpotAnalysisEngine.DefinedFields(Optic).LastOrDefault();
        var wavefront = WavefrontEngine.GenerateChiefRay(Optic, field, wavelength, _numRings);
        var coefficients = ZernikeFitEngine.FitFringe(wavefront.Samples, _numTerms);
        var values = coefficients.ToDictionary(
            coefficient => $"Z{coefficient.Number} (n={coefficient.RadialOrder}, m={coefficient.AzimuthalOrder})",
            coefficient => (object)coefficient.Value);
        values["ZernikeType"] = "fringe";
        values["WavelengthMicrometers"] = wavelength.Micrometers;
        values["FieldHx"] = field.Hx;
        values["FieldHy"] = field.Hy;
        var heatmapPoints = new List<AnalysisPoint>(_mapSize * _mapSize);
        for (var row = 0; row < _mapSize; row++)
        {
            var y = -1 + (2.0 * row / (_mapSize - 1.0));
            for (var column = 0; column < _mapSize; column++)
            {
                var x = -1 + (2.0 * column / (_mapSize - 1.0));
                if ((x * x) + (y * y) <= 1)
                {
                    heatmapPoints.Add(new AnalysisPoint(x, y, Value: ZernikeFitEngine.Evaluate(coefficients, x, y)));
                }
            }
        }

        var heatmap = new AnalysisSeries(
            "Pupil X",
            "Pupil Y",
            heatmapPoints,
            AnalysisSeriesKind.Heatmap,
            ValueLabel: "OPD (waves)");
        var coefficientBars = new AnalysisSeries(
            "Zernike term",
            "Coefficient",
            coefficients.Select(coefficient => new AnalysisPoint(
                coefficient.Number,
                coefficient.Value,
                $"Z{coefficient.Number}")).ToArray(),
            AnalysisSeriesKind.Bar);
        return new AnalysisData(Name, values, coefficientBars, new[] { heatmap }, new AnalysisPlotOptions(
            Title: "Zernike Fringe Fit",
            EqualAspect: true,
            XMinimum: -1,
            XMaximum: 1,
            YMinimum: -1,
            YMaximum: 1));
    }
}

public sealed class PsfAnalysis : BaseAnalysis
{
    private readonly int _requestedRays;
    private readonly int? _gridSize;

    public PsfAnalysis(Optic optic, int numRays = 128, int? gridSize = null) : base(optic)
    {
        _requestedRays = Math.Max(2, numRays);
        _gridSize = gridSize;
    }

    public override string Name => "PSF";

    public override AnalysisData GenerateData()
    {
        var wavelength = Optic.Wavelengths.FirstOrDefault(item => item.IsPrimary)
            ?? Optic.Wavelengths.FirstOrDefault();
        if (wavelength is null)
        {
            return new AnalysisData(Name, new Dictionary<string, object> { ["Status"] = "No wavelengths" });
        }

        var field = SpotAnalysisEngine.DefinedFields(Optic).LastOrDefault();
        var pupilSampling = _gridSize.HasValue
            ? _requestedRays
            : (int)Math.Floor(32 * Math.Pow(2, (Math.Log2(_requestedRays) - 5) / 2));
        var gridSize = _gridSize ?? (_requestedRays * 2);
        var psf = DiffractionEngine.ComputeFftPsf(Optic, field, wavelength, pupilSampling, gridSize);
        var bounds = FindPsfBounds(psf.Values, 0.05);
        var width = Math.Max(1, bounds.MaxColumn - bounds.MinColumn);
        var height = Math.Max(1, bounds.MaxRow - bounds.MinRow);
        var xExtent = width * psf.SampleSpacingMicrometers;
        var yExtent = height * psf.SampleSpacingMicrometers;
        var points = new List<AnalysisPoint>(width * height);
        for (var row = bounds.MinRow; row < bounds.MaxRow; row++)
        {
            var y = -yExtent / 2 + ((row - bounds.MinRow + 0.5) * psf.SampleSpacingMicrometers);
            for (var column = bounds.MinColumn; column < bounds.MaxColumn; column++)
            {
                var x = -xExtent / 2 + ((column - bounds.MinColumn + 0.5) * psf.SampleSpacingMicrometers);
                points.Add(new AnalysisPoint(x, y, Value: psf.Values[row, column]));
            }
        }

        var series = new AnalysisSeries(
            "X (\u00B5m)",
            "Y (\u00B5m)",
            points,
            AnalysisSeriesKind.Heatmap,
            ValueLabel: "Relative Intensity (%)");
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["Method"] = "FFT",
            ["PupilSampling"] = pupilSampling,
            ["GridSize"] = gridSize,
            ["WorkingFNumber"] = psf.WorkingFNumber,
            ["StrehlRatio"] = psf.StrehlRatio,
            ["WavelengthMicrometers"] = wavelength.Micrometers,
            ["FieldHx"] = field.Hx,
            ["FieldHy"] = field.Hy
        }, series, new[] { series }, new AnalysisPlotOptions(
            Title: "FFT PSF",
            EqualAspect: true,
            XMinimum: -xExtent / 2,
            XMaximum: xExtent / 2,
            YMinimum: -yExtent / 2,
            YMaximum: yExtent / 2));
    }

    private static (int MinRow, int MinColumn, int MaxRow, int MaxColumn) FindPsfBounds(
        double[,] psf,
        double threshold)
    {
        var size = psf.GetLength(0);
        var rows = new List<int>();
        var columns = new List<int>();
        for (var row = 0; row < size; row++)
        {
            for (var column = 0; column < size; column++)
            {
                if (psf[row, column] > threshold)
                {
                    rows.Add(row);
                    columns.Add(column);
                }
            }
        }

        if (rows.Count == 0)
        {
            return (0, 0, size, size);
        }

        var extent = Math.Max(rows.Max() - rows.Min(), columns.Max() - columns.Min());
        var center = size / 2;
        var minimum = Math.Max(0, (int)(center - (extent / 2.0)));
        var maximum = Math.Min(size, (int)(center + (extent / 2.0)));
        return (minimum, minimum, Math.Max(minimum + 1, maximum), Math.Max(minimum + 1, maximum));
    }
}

public sealed class MtfAnalysis : BaseAnalysis
{
    private readonly int _requestedRays;
    private readonly int? _gridSize;

    public MtfAnalysis(Optic optic, int numRays = 128, int? gridSize = null) : base(optic)
    {
        _requestedRays = Math.Max(2, numRays);
        _gridSize = gridSize;
    }

    public override string Name => "MTF";

    public override AnalysisData GenerateData()
    {
        var wavelength = Optic.Wavelengths.FirstOrDefault(item => item.IsPrimary)
            ?? Optic.Wavelengths.FirstOrDefault();
        if (wavelength is null)
        {
            return new AnalysisData(Name, new Dictionary<string, object> { ["Status"] = "No wavelengths" });
        }

        var pupilSampling = _gridSize.HasValue
            ? _requestedRays
            : (int)Math.Floor(32 * Math.Pow(2, (Math.Log2(_requestedRays) - 5) / 2));
        var gridSize = _gridSize ?? (_requestedRays * 2);
        var fields = SpotAnalysisEngine.DefinedFields(Optic);
        var series = new List<AnalysisSeries>();
        var cutoff = 0.0;
        for (var fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
        {
            var field = fields[fieldIndex];
            var psf = DiffractionEngine.ComputeFftPsf(Optic, field, wavelength, pupilSampling, gridSize);
            var mtf = DiffractionEngine.ComputeFftMtf(psf, Optic, wavelength);
            cutoff = mtf.CutoffFrequency;
            series.Add(new AnalysisSeries(
                "Frequency (cycles/mm)",
                "Modulation",
                mtf.Frequency.Select((frequency, index) => new AnalysisPoint(frequency, mtf.Tangential[index])).ToArray(),
                Name: $"Hx: {field.Hx:0.0}, Hy: {field.Hy:0.0}, Tangential",
                ColorIndex: fieldIndex));
            series.Add(new AnalysisSeries(
                "Frequency (cycles/mm)",
                "Modulation",
                mtf.Frequency.Select((frequency, index) => new AnalysisPoint(frequency, mtf.Sagittal[index])).ToArray(),
                Name: $"Hx: {field.Hx:0.0}, Hy: {field.Hy:0.0}, Sagittal",
                LineStyle: AnalysisLineStyle.Dashed,
                ColorIndex: fieldIndex));
        }

        var values = new Dictionary<string, object>
        {
            ["Method"] = "FFT",
            ["PupilSampling"] = pupilSampling,
            ["GridSize"] = gridSize,
            ["CutoffFrequency"] = cutoff,
            ["WavelengthMicrometers"] = wavelength.Micrometers,
            ["FieldCount"] = fields.Count
        };
        return new AnalysisData(Name, values, series.FirstOrDefault(), series, new AnalysisPlotOptions(
            XMinimum: 0,
            XMaximum: cutoff,
            YMinimum: 0,
            YMaximum: 1,
            ShowLegend: true,
            GridOpacity: 0.25));
    }
}

public sealed class MmdftPsfAnalysis : BaseAnalysis
{
    private readonly int _numRays;
    private readonly int _imageSize;
    private readonly double? _pixelPitchMicrometers;

    public MmdftPsfAnalysis(
        Optic optic,
        int numRays = 16,
        int imageSize = 32,
        double? pixelPitchMicrometers = null) : base(optic)
    {
        _numRays = Math.Max(2, numRays);
        _imageSize = Math.Max(1, imageSize);
        _pixelPitchMicrometers = pixelPitchMicrometers;
    }

    public override string Name => "MMDFT PSF";

    public override AnalysisData GenerateData()
    {
        var wavelength = Optic.Wavelengths.FirstOrDefault(item => item.IsPrimary)
            ?? Optic.Wavelengths.FirstOrDefault();
        if (wavelength is null)
        {
            return new AnalysisData(Name, new Dictionary<string, object> { ["Status"] = "No wavelengths" });
        }

        var field = SpotAnalysisEngine.DefinedFields(Optic).LastOrDefault();
        var psf = DiffractionEngine.ComputeMmdftPsf(
            Optic,
            field,
            wavelength,
            _numRays,
            _imageSize,
            _pixelPitchMicrometers);
        return DiffractionAnalysisPresentation.CreatePsfData(
            Name,
            "MMDFT",
            "MMDFT PSF",
            psf,
            field,
            wavelength,
            psf.PeakStrehlRatio);
    }
}

public sealed class HuygensPsfAnalysis : BaseAnalysis
{
    private readonly int _numRays;
    private readonly int _imageSize;
    private readonly double _pixelPitchMillimeters;

    public HuygensPsfAnalysis(
        Optic optic,
        int numRays = 9,
        int imageSize = 32,
        double pixelPitchMillimeters = 0.005) : base(optic)
    {
        _numRays = Math.Max(2, numRays);
        _imageSize = Math.Max(1, imageSize);
        _pixelPitchMillimeters = Math.Max(1e-9, pixelPitchMillimeters);
    }

    public override string Name => "Huygens PSF";

    public override AnalysisData GenerateData()
    {
        var wavelength = Optic.Wavelengths.FirstOrDefault(item => item.IsPrimary)
            ?? Optic.Wavelengths.FirstOrDefault();
        if (wavelength is null)
        {
            return new AnalysisData(Name, new Dictionary<string, object> { ["Status"] = "No wavelengths" });
        }

        var field = SpotAnalysisEngine.DefinedFields(Optic).LastOrDefault();
        var psf = DiffractionEngine.ComputeHuygensPsf(
            Optic,
            field,
            wavelength,
            _numRays,
            _imageSize,
            _pixelPitchMillimeters);
        return DiffractionAnalysisPresentation.CreatePsfData(
            Name,
            "Huygens-Fresnel",
            "Huygens PSF",
            psf,
            field,
            wavelength,
            psf.StrehlRatio);
    }
}

public sealed class HuygensMtfAnalysis : BaseAnalysis
{
    private readonly int _numRays;
    private readonly int _imageSize;
    private readonly double _pixelPitchMillimeters;
    private readonly IReadOnlyList<(double Hx, double Hy)>? _fields;

    public HuygensMtfAnalysis(
        Optic optic,
        int numRays = 9,
        int imageSize = 32,
        double pixelPitchMillimeters = 0.005,
        IReadOnlyList<(double Hx, double Hy)>? fields = null) : base(optic)
    {
        _numRays = Math.Max(2, numRays);
        _imageSize = Math.Max(1, imageSize);
        _pixelPitchMillimeters = Math.Max(1e-9, pixelPitchMillimeters);
        _fields = fields;
    }

    public override string Name => "Huygens MTF";

    public override AnalysisData GenerateData()
    {
        var wavelength = Optic.Wavelengths.FirstOrDefault(item => item.IsPrimary)
            ?? Optic.Wavelengths.FirstOrDefault();
        if (wavelength is null)
        {
            return new AnalysisData(Name, new Dictionary<string, object> { ["Status"] = "No wavelengths" });
        }

        var fields = _fields ?? SpotAnalysisEngine.DefinedFields(Optic);
        var series = new List<AnalysisSeries>();
        var maximumFrequency = 0.0;
        for (var fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
        {
            var field = fields[fieldIndex];
            var psf = DiffractionEngine.ComputeHuygensPsf(
                Optic,
                field,
                wavelength,
                _numRays,
                _imageSize,
                _pixelPitchMillimeters);
            var mtf = DiffractionEngine.ComputePsfMtf(psf);
            maximumFrequency = Math.Max(maximumFrequency, mtf.Frequency.DefaultIfEmpty(0).Max());
            series.Add(new AnalysisSeries(
                "Frequency (cycles/mm)",
                "Modulation",
                mtf.Frequency.Select((frequency, index) => new AnalysisPoint(frequency, mtf.Tangential[index])).ToArray(),
                Name: $"Hx: {field.Hx:0.0}, Hy: {field.Hy:0.0}, Tangential",
                ColorIndex: fieldIndex));
            series.Add(new AnalysisSeries(
                "Frequency (cycles/mm)",
                "Modulation",
                mtf.Frequency.Select((frequency, index) => new AnalysisPoint(frequency, mtf.Sagittal[index])).ToArray(),
                Name: $"Hx: {field.Hx:0.0}, Hy: {field.Hy:0.0}, Sagittal",
                LineStyle: AnalysisLineStyle.Dashed,
                ColorIndex: fieldIndex));
        }

        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["Method"] = "Huygens-Fresnel",
            ["NumRays"] = _numRays,
            ["ImageSize"] = _imageSize,
            ["PixelPitchMillimeters"] = _pixelPitchMillimeters,
            ["WavelengthMicrometers"] = wavelength.Micrometers,
            ["FieldCount"] = fields.Count
        }, series.FirstOrDefault(), series, new AnalysisPlotOptions(
            Title: "Huygens MTF",
            XMinimum: 0,
            XMaximum: maximumFrequency,
            YMinimum: 0,
            YMaximum: 1,
            ShowLegend: true,
            GridOpacity: 0.25));
    }
}

internal static class DiffractionAnalysisPresentation
{
    public static AnalysisData CreatePsfData(
        string name,
        string method,
        string title,
        PsfResult psf,
        (double Hx, double Hy) field,
        Wavelength wavelength,
        double strehlRatio)
    {
        var extent = psf.GridSize * psf.SampleSpacingMicrometers;
        var points = new List<AnalysisPoint>(psf.GridSize * psf.GridSize);
        for (var row = 0; row < psf.GridSize; row++)
        {
            var y = -extent / 2 + ((row + 0.5) * psf.SampleSpacingMicrometers);
            for (var column = 0; column < psf.GridSize; column++)
            {
                var x = -extent / 2 + ((column + 0.5) * psf.SampleSpacingMicrometers);
                points.Add(new AnalysisPoint(x, y, Value: psf.Values[row, column]));
            }
        }

        var series = new AnalysisSeries(
            "X (\u00B5m)",
            "Y (\u00B5m)",
            points,
            AnalysisSeriesKind.Heatmap,
            ValueLabel: "Relative Intensity (%)");
        return new AnalysisData(name, new Dictionary<string, object>
        {
            ["Method"] = method,
            ["PupilSampling"] = psf.PupilSampling,
            ["ImageSize"] = psf.GridSize,
            ["GridSize"] = psf.GridSize,
            ["PixelPitchMicrometers"] = psf.SampleSpacingMicrometers,
            ["WorkingFNumber"] = psf.WorkingFNumber,
            ["StrehlRatio"] = strehlRatio,
            ["PeakStrehlRatio"] = psf.PeakStrehlRatio,
            ["WavelengthMicrometers"] = wavelength.Micrometers,
            ["FieldHx"] = field.Hx,
            ["FieldHy"] = field.Hy
        }, series, new[] { series }, new AnalysisPlotOptions(
            Title: title,
            EqualAspect: true,
            XMinimum: -extent / 2,
            XMaximum: extent / 2,
            YMinimum: -extent / 2,
            YMaximum: extent / 2));
    }
}

public sealed class ImageSimulationAnalysis : BaseAnalysis
{
    private readonly ImageSimulationConfig _config;

    public ImageSimulationAnalysis(Optic optic, ImageSimulationConfig? config = null) : base(optic)
    {
        _config = config ?? new ImageSimulationConfig
        {
            PsfGridRows = 3,
            PsfGridColumns = 3,
            PsfSize = 32,
            NumRays = 16,
            Components = 3,
            Padding = 16,
            DistortionGridSize = 9,
            DistortionPolynomialDegree = 5
        };
    }

    public override string Name => "Image Simulation";

    public override AnalysisData GenerateData()
    {
        var source = ImageSimulationEngine.CreateTestChart(64, 48);
        var result = ImageSimulationEngine.Simulate(Optic, source, _config);
        var original = RasterSeries(result.Source);
        var simulated = RasterSeries(result.Simulated);
        var panes = new[]
        {
            RasterPane("Original Image [0]", original, result.Source),
            RasterPane("Simulated Image [0]", simulated, result.Simulated)
        };
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["Pipeline"] = "EigenPSF spatially variable convolution + geometric distortion + lateral color",
            ["OutputShape"] = $"(1, {result.Simulated.Channels}, {result.Simulated.Height}, {result.Simulated.Width})",
            ["WavelengthsMicrometers"] = string.Join(", ", _config.WavelengthsMicrometers.Select(value => value.ToString("0.00"))),
            ["PsfGridShape"] = $"({_config.PsfGridRows}, {_config.PsfGridColumns})",
            ["PsfSize"] = _config.PsfSize,
            ["NumRays"] = _config.NumRays,
            ["EigenPsfComponents"] = _config.Components,
            ["DistortionGridSize"] = _config.DistortionGridSize,
            ["DistortionPolynomialDegree"] = _config.DistortionPolynomialDegree,
            ["MeanAbsoluteChange"] = result.MeanAbsoluteChange,
            ["MaximumOutputValue"] = result.MaximumValue
        }, original, new[] { original }, PlotPanes: panes, PlotPaneColumns: 2);
    }

    private static AnalysisSeries RasterSeries(RgbImage image)
    {
        var points = new List<AnalysisPoint>(image.Width * image.Height);
        for (var row = 0; row < image.Height; row++)
        {
            for (var column = 0; column < image.Width; column++)
            {
                points.Add(new AnalysisPoint(
                    column,
                    image.Height - 1 - row,
                    Red: image.Values[0, row, column],
                    Green: image.Values[Math.Min(1, image.Channels - 1), row, column],
                    Blue: image.Values[Math.Min(2, image.Channels - 1), row, column]));
            }
        }

        return new AnalysisSeries("", "", points, AnalysisSeriesKind.Raster);
    }

    private static AnalysisPlotPane RasterPane(string title, AnalysisSeries series, RgbImage image)
    {
        return new AnalysisPlotPane(title, new[] { series }, new AnalysisPlotOptions(
            Title: title,
            EqualAspect: true,
            XMinimum: -0.5,
            XMaximum: image.Width - 0.5,
            YMinimum: -0.5,
            YMaximum: image.Height - 0.5,
            GridOpacity: 0,
            HideAxes: true));
    }
}

public sealed class JonesPupilAnalysis : BaseAnalysis
{
    private readonly int _gridSize;

    public JonesPupilAnalysis(Optic optic, int gridSize = 65) : base(optic)
    {
        _gridSize = Math.Max(3, gridSize);
    }

    public override string Name => "Jones Pupil";

    public override AnalysisData GenerateData()
    {
        var wavelength = Optic.Wavelengths.FirstOrDefault(item => item.IsPrimary)
            ?? Optic.Wavelengths.FirstOrDefault();
        if (wavelength is null)
        {
            return new AnalysisData(Name, new Dictionary<string, object> { ["Status"] = "No wavelengths" });
        }

        var result = JonesPupilEngine.Generate(Optic, (0, 0), wavelength, _gridSize, useFresnelCoatings: true);
        var elements = new (string Name, Func<JonesPupilSample, System.Numerics.Complex> Select)[]
        {
            ("Jxx", sample => sample.Jxx),
            ("Jxy", sample => sample.Jxy),
            ("Jyx", sample => sample.Jyx),
            ("Jyy", sample => sample.Jyy)
        };
        var panes = new List<AnalysisPlotPane>(8);
        foreach (var component in new[] { "Re", "Im" })
        {
            foreach (var element in elements)
            {
                var series = new AnalysisSeries(
                    "Px",
                    "Py",
                    result.Samples.Select(sample => new AnalysisPoint(
                        sample.Px,
                        sample.Py,
                        Value: sample.IsValid
                            ? (component == "Re" ? element.Select(sample).Real : element.Select(sample).Imaginary)
                            : double.NaN)).ToArray(),
                    AnalysisSeriesKind.Heatmap,
                    ValueLabel: $"{component}({element.Name})");
                panes.Add(new AnalysisPlotPane(
                    $"{component}({element.Name})",
                    new[] { series },
                    new AnalysisPlotOptions(
                        Title: $"{component}({element.Name})",
                        EqualAspect: true,
                        XMinimum: -1,
                        XMaximum: 1,
                        YMinimum: -1,
                        YMaximum: 1,
                        HideTopAndRightAxes: true,
                        GridOpacity: 0)));
            }
        }

        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["Field"] = "(0, 0)",
            ["WavelengthMicrometers"] = wavelength.Micrometers,
            ["GridSize"] = _gridSize,
            ["ValidRayCount"] = result.Samples.Count(sample => sample.IsValid),
            ["CoatingMode"] = "Fresnel",
            ["Layout"] = "2 rows (real, imaginary) x 4 columns (Jxx, Jxy, Jyx, Jyy)"
        }, PlotPanes: panes, PlotPaneColumns: 4);
    }
}

public sealed class PrescriptionReportAnalysis : BaseAnalysis
{
    public PrescriptionReportAnalysis(Optic optic) : base(optic)
    {
    }

    public override string Name => "Prescription Report";

    public override AnalysisData GenerateData()
    {
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["Name"] = Optic.Name,
            ["SurfaceCount"] = Optic.SurfaceGroup.Items.Count,
            ["FieldCount"] = Optic.Fields.Count,
            ["WavelengthCount"] = Optic.Wavelengths.Count,
            ["EFL"] = Optic.Paraxial.EstimateEffectiveFocalLength(),
            ["FNumber"] = Optic.Paraxial.EstimateFNumber(),
            ["TotalTrack"] = Optic.SurfaceGroup.TotalTrack
        });
    }
}

public sealed class GridDistortionAnalysis : BaseAnalysis
{
    private readonly int _numPoints;
    private readonly string _distortionType;

    public GridDistortionAnalysis(Optic optic, int numPoints = 10, string distortionType = "f-tan") : base(optic)
    {
        _numPoints = Math.Max(2, numPoints);
        _distortionType = AnalysisTrace.NormalizeDistortionType(distortionType);
    }

    public override string Name => "Grid Distortion";

    public override AnalysisData GenerateData()
    {
        const double epsilon = 1e-10;
        var maxField = AnalysisTrace.MaxFieldDegrees(Optic);
        var maxFieldRadians = maxField * Math.PI / 180.0;
        var wavelength = Optic.Wavelengths.FirstOrDefault(item => item.IsPrimary)
            ?? Optic.Wavelengths.FirstOrDefault();
        if (wavelength is null)
        {
            return new AnalysisData(Name, new Dictionary<string, object>
            {
                ["Status"] = "No wavelengths"
            });
        }

        var chief = AnalysisTrace.FinalSample(Optic, 0, 0, 0, 0, wavelength.Micrometers);
        var reference = AnalysisTrace.FinalSample(Optic, 0, epsilon, 0, 0, wavelength.Micrometers);
        var referenceAngle = epsilon * maxFieldRadians;
        var constant = _distortionType == "f-theta"
            ? (reference.Position.Y - chief.Position.Y) / referenceAngle
            : (reference.Position.Y - chief.Position.Y) / Math.Tan(referenceAngle);
        var extent = Math.Sqrt(2) / 2.0;
        var normalized = Enumerable.Range(0, _numPoints)
            .Select(index => -extent + ((2 * extent) * index / (_numPoints - 1.0)))
            .ToArray();
        var idealX = new double[_numPoints, _numPoints];
        var idealY = new double[_numPoints, _numPoints];
        var actualX = new double[_numPoints, _numPoints];
        var actualY = new double[_numPoints, _numPoints];
        var maximumDistortion = 0.0;

        for (var row = 0; row < _numPoints; row++)
        {
            for (var column = 0; column < _numPoints; column++)
            {
                var hx = normalized[column];
                var hy = normalized[row];
                idealX[row, column] = constant * (_distortionType == "f-theta"
                    ? hx * maxFieldRadians
                    : Math.Tan(hx * maxFieldRadians));
                idealY[row, column] = constant * (_distortionType == "f-theta"
                    ? hy * maxFieldRadians
                    : Math.Tan(hy * maxFieldRadians));
                var sample = AnalysisTrace.FinalSample(Optic, hx, hy, 0, 0, wavelength.Micrometers);
                actualX[row, column] = sample.Position.X - chief.Position.X;
                actualY[row, column] = sample.Position.Y - chief.Position.Y;
                var idealRadius = Math.Sqrt((idealX[row, column] * idealX[row, column]) + (idealY[row, column] * idealY[row, column]));
                if (idealRadius > 1e-30)
                {
                    var dx = idealX[row, column] - actualX[row, column];
                    var dy = idealY[row, column] - actualY[row, column];
                    maximumDistortion = Math.Max(maximumDistortion, 100 * Math.Sqrt((dx * dx) + (dy * dy)) / idealRadius);
                }
            }
        }

        var series = new List<AnalysisSeries>(_numPoints * 4);
        for (var index = 0; index < _numPoints; index++)
        {
            series.Add(GridLine(idealX, idealY, index, false, "Ideal Grid", 1, AnalysisLineStyle.Solid, 1));
        }

        for (var index = 0; index < _numPoints; index++)
        {
            series.Add(GridLine(idealX, idealY, index, true, "", 1, AnalysisLineStyle.Solid, 1));
        }

        for (var index = 0; index < _numPoints; index++)
        {
            series.Add(GridLine(actualX, actualY, index, false, "Distorted Grid", 0, AnalysisLineStyle.Dashed, 1.5));
        }

        for (var index = 0; index < _numPoints; index++)
        {
            series.Add(GridLine(actualX, actualY, index, true, "", 0, AnalysisLineStyle.Dashed, 1.5));
        }

        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["MaximumDistortionPercent"] = maximumDistortion,
            ["DistortionType"] = _distortionType,
            ["GridSize"] = _numPoints,
            ["WavelengthMicrometers"] = wavelength.Micrometers
        }, series[0], series, new AnalysisPlotOptions(
            Title: $"Grid Distortion (Max: {maximumDistortion:0.00}%)",
            EqualAspect: true,
            ShowLegend: true,
            HideTopAndRightAxes: true,
            DottedGrid: true));
    }

    private AnalysisSeries GridLine(
        double[,] x,
        double[,] y,
        int fixedIndex,
        bool row,
        string name,
        int colorIndex,
        AnalysisLineStyle lineStyle,
        double lineWidth)
    {
        var points = new AnalysisPoint[_numPoints];
        for (var index = 0; index < _numPoints; index++)
        {
            var r = row ? fixedIndex : index;
            var c = row ? index : fixedIndex;
            points[index] = new AnalysisPoint(x[r, c], y[r, c]);
        }

        return new AnalysisSeries(
            "Image X (mm)",
            "Image Y (mm)",
            points,
            Name: name,
            LineStyle: lineStyle,
            ColorIndex: colorIndex,
            LineWidth: lineWidth);
    }
}

internal static class AnalysisTrace
{
    public static double MaxFieldDegrees(Optic optic)
    {
        return optic.Fields.Select(field => Math.Abs(field.YAngleDegrees)).DefaultIfEmpty(0).Max();
    }

    public static string NormalizeDistortionType(string distortionType)
    {
        if (string.Equals(distortionType, "f-tan", StringComparison.OrdinalIgnoreCase))
        {
            return "f-tan";
        }

        if (string.Equals(distortionType, "f-theta", StringComparison.OrdinalIgnoreCase))
        {
            return "f-theta";
        }

        throw new ArgumentException("Distortion type must be 'f-tan' or 'f-theta'.", nameof(distortionType));
    }

    public static Rays.RayTraceSample FinalSample(
        Optic optic,
        double hx,
        double hy,
        double px,
        double py,
        double wavelengthMicrometers)
    {
        var history = optic.TraceGeneric(hx, hy, px, py, wavelengthMicrometers).RayHistories.Single();
        if (history.Count == 0)
        {
            throw new InvalidOperationException("Ray tracing did not produce an image-plane sample.");
        }

        return history[^1];
    }
}

internal sealed record SpotRayData(double X, double Y, double Intensity);

internal sealed record SpotWavelengthData(Wavelength Wavelength, IReadOnlyList<SpotRayData> Rays);

internal sealed record SpotFieldData(
    double Hx,
    double Hy,
    IReadOnlyList<SpotWavelengthData> Wavelengths);

internal sealed record SpotAnalysisResult(
    IReadOnlyList<SpotFieldData> Fields,
    int RayCount,
    int VignettedRayCount);

internal static class SpotAnalysisEngine
{
    public static IReadOnlyList<(double Hx, double Hy)> DefinedFields(Optic optic)
    {
        var maxField = optic.Fields.Select(field => Math.Sqrt(
                (field.XAngleDegrees * field.XAngleDegrees)
                + (field.YAngleDegrees * field.YAngleDegrees)))
            .DefaultIfEmpty(0)
            .Max();
        return optic.Fields.Select(field => (
            Hx: maxField <= 1e-12 ? 0 : field.XAngleDegrees / maxField,
            Hy: maxField <= 1e-12 ? 0 : field.YAngleDegrees / maxField)).ToArray();
    }

    public static SpotAnalysisResult Generate(
        Optic optic,
        IEnumerable<(double Hx, double Hy)> fields,
        IEnumerable<Wavelength> wavelengths,
        int sampleParameter,
        string distribution,
        double imagePlaneOffset = 0)
    {
        var fieldArray = fields.ToArray();
        var wavelengthArray = wavelengths.ToArray();
        var pupilSamples = CreatePupilSamples(sampleParameter, distribution);
        var rawFields = new List<SpotFieldData>(fieldArray.Length);
        var rayCount = 0;
        var vignettedRayCount = 0;

        foreach (var field in fieldArray)
        {
            var waveData = new List<SpotWavelengthData>(wavelengthArray.Length);
            foreach (var wavelength in wavelengthArray)
            {
                var bundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(
                    field.Hx,
                    field.Hy,
                    wavelength.Micrometers,
                    pupilSamples);
                var trace = optic.SequentialRayTracer.Trace(bundle);
                var finalSamples = trace.RayHistories
                    .Where(history => history.Count > 0)
                    .Select(history => history[^1])
                    .ToArray();
                var valid = finalSamples
                    .Where(sample => sample.Intensity > 0)
                    .Select(sample =>
                    {
                        var position = sample.Position;
                        if (Math.Abs(imagePlaneOffset) > 1e-12 && Math.Abs(sample.Direction.Z) > 1e-12)
                        {
                            position += sample.Direction * (imagePlaneOffset / sample.Direction.Z);
                        }

                        return new SpotRayData(position.X, position.Y, sample.Intensity);
                    })
                    .ToArray();
                rayCount += finalSamples.Length;
                vignettedRayCount += finalSamples.Length - valid.Length;
                waveData.Add(new SpotWavelengthData(wavelength, valid));
            }

            rawFields.Add(new SpotFieldData(field.Hx, field.Hy, waveData));
        }

        var referenceIndex = Array.FindIndex(wavelengthArray, wavelength => wavelength.IsPrimary);
        referenceIndex = referenceIndex < 0 ? 0 : referenceIndex;
        var centeredFields = rawFields.Select(field =>
        {
            var reference = field.Wavelengths.Count == 0
                ? Array.Empty<SpotRayData>()
                : field.Wavelengths[Math.Min(referenceIndex, field.Wavelengths.Count - 1)].Rays;
            var centroidX = reference.Select(ray => ray.X).DefaultIfEmpty(0).Average();
            var centroidY = reference.Select(ray => ray.Y).DefaultIfEmpty(0).Average();
            var centeredWavelengths = field.Wavelengths.Select(wavelength => new SpotWavelengthData(
                wavelength.Wavelength,
                wavelength.Rays.Select(ray => new SpotRayData(
                    ray.X - centroidX,
                    ray.Y - centroidY,
                    ray.Intensity)).ToArray())).ToArray();
            return new SpotFieldData(field.Hx, field.Hy, centeredWavelengths);
        }).ToArray();
        return new SpotAnalysisResult(centeredFields, rayCount, vignettedRayCount);
    }

    public static double RmsRadius(IReadOnlyList<SpotRayData> rays)
    {
        return rays.Count == 0
            ? 0
            : Math.Sqrt(rays.Average(ray => (ray.X * ray.X) + (ray.Y * ray.Y)));
    }

    public static IReadOnlyList<PupilSample> CreatePupilSamples(int sampleParameter, string distribution)
    {
        if (string.Equals(distribution, "hexapolar", StringComparison.OrdinalIgnoreCase))
        {
            return ApertureSampler.GenerateHexapolarRings(sampleParameter);
        }

        if (string.Equals(distribution, "uniform", StringComparison.OrdinalIgnoreCase))
        {
            var axis = Enumerable.Range(0, sampleParameter)
                .Select(index => sampleParameter == 1 ? 0 : -1 + (2.0 * index / (sampleParameter - 1)))
                .ToArray();
            return axis.SelectMany(y => axis.Select(x => new PupilSample(x, y, 1)))
                .Where(sample => (sample.X * sample.X) + (sample.Y * sample.Y) <= 1)
                .ToArray();
        }

        return ApertureSampler.Generate(sampleParameter, RayGenerator.ParseSampling(distribution));
    }
}

public sealed class IncoherentIrradianceAnalysis : BaseAnalysis
{
    private readonly int _numRays;
    private readonly int _resolutionX;
    private readonly int _resolutionY;
    private readonly int _detectorSurfaceIndex;
    private readonly string _distribution;
    private readonly bool _normalize;

    public IncoherentIrradianceAnalysis(
        Optic optic,
        int numRays = 5,
        int resolutionX = 128,
        int resolutionY = 128,
        int detectorSurfaceIndex = -1,
        string distribution = "random",
        bool normalize = true) : base(optic)
    {
        _numRays = Math.Max(1, numRays);
        _resolutionX = Math.Max(1, resolutionX);
        _resolutionY = Math.Max(1, resolutionY);
        _detectorSurfaceIndex = detectorSurfaceIndex;
        _distribution = distribution;
        _normalize = normalize;
    }

    public override string Name => "Incoherent Irradiance";

    public override AnalysisData GenerateData()
    {
        if (Optic.SurfaceGroup.Items.Count == 0)
        {
            return Status("No detector surface");
        }

        var detectorIndex = _detectorSurfaceIndex < 0
            ? Optic.SurfaceGroup.Items.Count + _detectorSurfaceIndex
            : _detectorSurfaceIndex;
        if (detectorIndex < 0 || detectorIndex >= Optic.SurfaceGroup.Items.Count)
        {
            return Status("Detector surface index is out of range");
        }

        var detector = Optic.SurfaceGroup.Items[detectorIndex];
        if (!TryGetExtent(detector.PhysicalAperture, out var extent))
        {
            return Status("Detector surface has no supported physical aperture");
        }

        var fields = SpotAnalysisEngine.DefinedFields(Optic);
        var wavelengths = Optic.Wavelengths.ToArray();
        if (fields.Count == 0 || wavelengths.Length == 0)
        {
            return Status("No fields or wavelengths");
        }

        var xStep = (extent.XMaximum - extent.XMinimum) / _resolutionX;
        var yStep = (extent.YMaximum - extent.YMinimum) / _resolutionY;
        var pixelArea = xStep * yStep;
        var pupilSamples = SpotAnalysisEngine.CreatePupilSamples(_numRays, _distribution);
        var panes = new List<AnalysisPlotPane>(fields.Count * wavelengths.Length);
        var peaks = new List<double>(fields.Count * wavelengths.Length);
        var validRayCount = 0;

        for (var fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
        {
            var field = fields[fieldIndex];
            for (var wavelengthIndex = 0; wavelengthIndex < wavelengths.Length; wavelengthIndex++)
            {
                var wavelength = wavelengths[wavelengthIndex];
                var bundle = Optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(
                    field.Hx,
                    field.Hy,
                    wavelength.Micrometers,
                    pupilSamples);
                var trace = Optic.SequentialRayTracer.Trace(bundle);
                var irradiance = new double[_resolutionX, _resolutionY];
                foreach (var history in trace.RayHistories)
                {
                    if (history.Count <= detectorIndex)
                    {
                        continue;
                    }

                    var sample = history[detectorIndex];
                    if (sample.Intensity <= 0 || sample.Vignetted)
                    {
                        continue;
                    }

                    var local = detector.CoordinateSystem.ToLocalPoint(sample.Position);
                    var xBin = BinIndex(local.X, extent.XMinimum, extent.XMaximum, _resolutionX);
                    var yBin = BinIndex(local.Y, extent.YMinimum, extent.YMaximum, _resolutionY);
                    if (xBin < 0 || yBin < 0)
                    {
                        continue;
                    }

                    irradiance[xBin, yBin] += sample.Intensity / pixelArea;
                    validRayCount++;
                }

                var peak = irradiance.Cast<double>().DefaultIfEmpty(0).Max();
                peaks.Add(peak);
                var points = new List<AnalysisPoint>(_resolutionX * _resolutionY);
                for (var x = 0; x < _resolutionX; x++)
                {
                    var xCenter = extent.XMinimum + ((x + 0.5) * xStep);
                    for (var y = 0; y < _resolutionY; y++)
                    {
                        var yCenter = extent.YMinimum + ((y + 0.5) * yStep);
                        var value = _normalize && peak > 0 ? irradiance[x, y] / peak : irradiance[x, y];
                        points.Add(new AnalysisPoint(xCenter, yCenter, Value: value));
                    }
                }

                var title = $"Field {fieldIndex} ({field.Hx:0.0###}, {field.Hy:0.0###}), "
                    + $"\u03BB{wavelengthIndex} = {wavelength.Micrometers:0.000} \u00B5m";
                var series = new AnalysisSeries(
                    "X (mm)",
                    "Y (mm)",
                    points,
                    AnalysisSeriesKind.Heatmap,
                    ValueLabel: _normalize ? "Normalized Irradiance" : "Irradiance (W/mm\u00B2)",
                    ColorMap: AnalysisColorMap.Inferno,
                    ValueMinimum: _normalize ? 0 : null,
                    ValueMaximum: _normalize ? 1 : null);
                panes.Add(new AnalysisPlotPane(title, new[] { series }, new AnalysisPlotOptions(
                    Title: title,
                    EqualAspect: true,
                    XMinimum: extent.XMinimum,
                    XMaximum: extent.XMaximum,
                    YMinimum: extent.YMinimum,
                    YMaximum: extent.YMaximum,
                    GridOpacity: 0)));
            }
        }

        var firstSeries = panes.FirstOrDefault()?.Series.FirstOrDefault();
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["DetectorSurfaceIndex"] = detectorIndex,
            ["DetectorExtent"] = $"[{extent.XMinimum:R}, {extent.XMaximum:R}] x [{extent.YMinimum:R}, {extent.YMaximum:R}] mm",
            ["Resolution"] = $"{_resolutionX} x {_resolutionY}",
            ["NumRays"] = _numRays,
            ["Distribution"] = _distribution,
            ["Normalized"] = _normalize,
            ["ValidRayCount"] = validRayCount,
            ["PeakIrradiance"] = peaks.DefaultIfEmpty(0).Max(),
            ["FieldCount"] = fields.Count,
            ["WavelengthCount"] = wavelengths.Length
        }, firstSeries, firstSeries is null ? null : new[] { firstSeries }, PlotPanes: panes, PlotPaneColumns: wavelengths.Length);
    }

    private AnalysisData Status(string message)
    {
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["Status"] = message,
            ["PythonRequirement"] = "Set a physical aperture on the detector surface"
        });
    }

    private static bool TryGetExtent(
        IPhysicalAperture? aperture,
        out (double XMinimum, double XMaximum, double YMinimum, double YMaximum) extent)
    {
        if (PhysicalApertureBoundsCalculator.TryGetBounds(aperture, out var bounds))
        {
            extent = (bounds.XMinimum, bounds.XMaximum, bounds.YMinimum, bounds.YMaximum);
            return true;
        }

        extent = default;
        return false;
    }

    private static int BinIndex(double value, double minimum, double maximum, int count)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
        {
            return -1;
        }

        if (value == maximum)
        {
            return count - 1;
        }

        return Math.Clamp((int)Math.Floor((value - minimum) / (maximum - minimum) * count), 0, count - 1);
    }
}

public class PlaceholderAnalysis : BaseAnalysis
{
    public PlaceholderAnalysis(Optic optic, string name) : base(optic)
    {
        Name = name;
    }

    public override string Name { get; }

    public override AnalysisData GenerateData()
    {
        var spot = new AnalysisRunner(Optic).EvaluateSpotDiagram();
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["WeightedMetric"] = spot.RmsSpotRadius,
            ["Status"] = "framework-ready"
        });
    }
}

public sealed class AnalysisCatalog
{
    private readonly Optic _optic;

    public AnalysisCatalog(Optic optic)
    {
        _optic = optic;
    }

    public IReadOnlyList<string> Names { get; } = new[]
    {
        "First Order",
        "Spot Diagram",
        "Ray Fan",
        "Best Fit Ray Fan",
        "Distortion",
        "Grid Distortion",
        "Field Curvature",
        "Encircled Energy",
        "Pupil Aberration",
        "RMS vs Field",
        "RMS Wavefront vs Field",
        "Through Focus",
        "Through Focus MTF",
        "Angle vs Image Height - Through Pupil",
        "Angle vs Image Height - Through Field",
        "Incoherent Irradiance",
        "Radiant Intensity",
        "Y-Ybar",
        "PSF",
        "MMDFT PSF",
        "Huygens PSF",
        "MTF",
        "Huygens MTF",
        "Geometric MTF",
        "Sampled MTF",
        "Wavefront",
        "Centroid Sphere Wavefront",
        "Best Fit Sphere Wavefront",
        "Zernike",
        "Image Simulation",
        "Jones Pupil",
        "Prescription Report"
    };

    public BaseAnalysis Create(string name)
    {
        return name switch
        {
            "First Order" => new FirstOrderAnalysis(_optic),
            "Spot Diagram" => new SpotDiagramAnalysis(_optic),
            "Ray Fan" => new RayFanAnalysis(_optic),
            "Best Fit Ray Fan" => new BestFitRayFanAnalysis(_optic, numRingsForFit: 8),
            "Distortion" => new DistortionAnalysis(_optic),
            "Grid Distortion" => new GridDistortionAnalysis(_optic),
            "Field Curvature" => new FieldCurvatureAnalysis(_optic),
            "Encircled Energy" => new EncircledEnergyAnalysis(_optic),
            "Pupil Aberration" => new PupilAberrationAnalysis(_optic),
            "RMS vs Field" => new RmsVsFieldAnalysis(_optic),
            "RMS Wavefront vs Field" => new RmsWavefrontVsFieldAnalysis(_optic),
            "Through Focus" => new ThroughFocusAnalysis(_optic),
            "Through Focus MTF" => new ThroughFocusMtfAnalysis(_optic),
            "Angle vs Image Height - Through Pupil" => new IncidentAngleVsHeightAnalysis(_optic, AngleScanMode.ThroughPupil),
            "Angle vs Image Height - Through Field" => new IncidentAngleVsHeightAnalysis(_optic, AngleScanMode.ThroughField),
            "Incoherent Irradiance" => new IncoherentIrradianceAnalysis(_optic),
            "Radiant Intensity" => new RadiantIntensityAnalysis(_optic, numRays: 2048),
            "Y-Ybar" => new YYbarAnalysis(_optic),
            "PSF" => new PsfAnalysis(_optic),
            "MMDFT PSF" => new MmdftPsfAnalysis(_optic),
            "Huygens PSF" => new HuygensPsfAnalysis(_optic),
            "MTF" => new MtfAnalysis(_optic),
            "Huygens MTF" => new HuygensMtfAnalysis(_optic),
            "Geometric MTF" => new GeometricMtfAnalysis(_optic, numRays: 32, numPoints: 128),
            "Sampled MTF" => new SampledMtfAnalysis(_optic, pupilSampling: 32, numPoints: 128),
            "Wavefront" => new WavefrontAnalysis(_optic),
            "Centroid Sphere Wavefront" => new ReferenceSphereWavefrontAnalysis(
                _optic,
                ReferenceSphereStrategy.CentroidSphere,
                numRings: 8),
            "Best Fit Sphere Wavefront" => new ReferenceSphereWavefrontAnalysis(
                _optic,
                ReferenceSphereStrategy.BestFitSphere,
                numRings: 8),
            "Zernike" => new ZernikeAnalysis(_optic),
            "Image Simulation" => new ImageSimulationAnalysis(_optic),
            "Jones Pupil" => new JonesPupilAnalysis(_optic),
            "Prescription Report" => new PrescriptionReportAnalysis(_optic),
            _ => new PlaceholderAnalysis(_optic, name)
        };
    }
}

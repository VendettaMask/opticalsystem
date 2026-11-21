using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Domain;

namespace OpticalSystemDesign.Core.Analysis;

public sealed class LateralColorAnalysis : BaseAnalysis
{
    private readonly double _graphScaleMicrometers;
    private readonly bool _allWavelengths;
    private readonly bool _useRealRays;
    private readonly bool _showAiryDisk;
    private readonly int _sampleCount;

    public LateralColorAnalysis(
        Optic optic,
        double graphScaleMicrometers = 0,
        bool allWavelengths = false,
        bool useRealRays = true,
        bool showAiryDisk = true,
        int sampleCount = 101) : base(optic)
    {
        _graphScaleMicrometers = Math.Max(0, graphScaleMicrometers);
        _allWavelengths = allWavelengths;
        _useRealRays = useRealRays;
        _showAiryDisk = showAiryDisk;
        _sampleCount = Math.Max(21, sampleCount);
    }

    public override string Name => "Lateral Color";

    public override AnalysisData GenerateData()
    {
        var wavelengths = Optic.Wavelengths.OrderBy(wavelength => wavelength.Micrometers).ToArray();
        if (wavelengths.Length == 0)
        {
            return AnalysisData.Unavailable(Name, "No wavelengths");
        }

        var primary = wavelengths.FirstOrDefault(wavelength => wavelength.IsPrimary)
            ?? wavelengths[wavelengths.Length / 2];
        var shortest = wavelengths[0];
        var longest = wavelengths[^1];
        var maximumField = AnalysisTrace.MaxFieldValue(Optic);
        var comparisons = BuildComparisons(wavelengths, primary);
        var series = comparisons
            .Select((comparison, index) => BuildColorSeries(
                comparison.First,
                comparison.Second,
                comparison.Name,
                maximumField,
                index))
            .ToList();
        var airyRadius = AiryRadius(0, primary);
        if (_showAiryDisk && airyRadius > 0)
        {
            series.Add(AiryBoundary(-1, maximumField, primary, "艾里斑", airyRadius));
            series.Add(AiryBoundary(1, maximumField, primary, "", airyRadius));
        }

        var curveMaximum = series
            .Where(item => item.LineStyle == AnalysisLineStyle.Solid)
            .SelectMany(item => item.Points)
            .Select(point => Math.Abs(point.X))
            .DefaultIfEmpty(0)
            .Max();
        var autoLimit = NiceAxisLimit(Math.Max(curveMaximum, _showAiryDisk ? airyRadius : 0));
        var axisLimit = _graphScaleMicrometers > 0 ? _graphScaleMicrometers : autoLimit;
        var values = new Dictionary<string, object>
        {
            ["ShortestWavelengthMicrometers"] = shortest.Micrometers,
            ["LongestWavelengthMicrometers"] = longest.Micrometers,
            ["UseRealRays"] = _useRealRays,
            ["AllWavelengths"] = _allWavelengths,
            ["ShowAiryDisk"] = _showAiryDisk,
            [Optic.ImageSpaceAfocal ? "AiryRadiusMilliradians" : "AiryRadiusMicrometers"] = airyRadius,
            [Optic.ImageSpaceAfocal ? "GraphScaleMilliradians" : "GraphScaleMicrometers"] = _graphScaleMicrometers,
            ["ImageSpaceAfocal"] = Optic.ImageSpaceAfocal,
            ["LateralColorUnit"] = ColorAxisLabel(),
            [AnalysisTrace.MaximumFieldValueKey(Optic)] = maximumField
        };
        return new AnalysisData(
            Name,
            values,
            series.FirstOrDefault(),
            series,
            new AnalysisPlotOptions(
                Title: $"最大视场：{maximumField:0.0000} {FieldUnit()}",
                XMinimum: -axisLimit,
                XMaximum: axisLimit,
                YMinimum: 0,
                YMaximum: maximumField,
                ShowVerticalZeroLine: true,
                VerticalZeroLineStyle: AnalysisLineStyle.Dashed,
                VerticalZeroLineWidth: 1,
                ShowLegend: true,
                HideTopAndRightAxes: true,
                LegendBelow: true));
    }

    private IReadOnlyList<(Wavelength First, Wavelength Second, string Name)> BuildComparisons(
        IReadOnlyList<Wavelength> wavelengths,
        Wavelength primary)
    {
        if (!_allWavelengths || wavelengths.Count <= 2)
        {
            return new[]
            {
                (wavelengths[0], wavelengths[^1], "最短的-最长的")
            };
        }

        return wavelengths
            .Where(wavelength => !ReferenceEquals(wavelength, primary))
            .Select(wavelength => (
                First: wavelength,
                Second: primary,
                Name: $"{wavelength.Micrometers:0.0000}-{primary.Micrometers:0.0000} µm"))
            .ToArray();
    }

    private AnalysisSeries BuildColorSeries(
        Wavelength first,
        Wavelength second,
        string name,
        double maximumField,
        int colorIndex)
    {
        var points = Enumerable.Range(0, _sampleCount)
            .Select(index =>
            {
                var fraction = index / (double)(_sampleCount - 1);
                var firstCoordinate = ImageCoordinate(fraction, first.Micrometers);
                var secondCoordinate = ImageCoordinate(fraction, second.Micrometers);
                return new AnalysisPoint(
                    secondCoordinate - firstCoordinate,
                    fraction * maximumField);
            })
            .Where(point => double.IsFinite(point.X) && double.IsFinite(point.Y))
            .ToArray();
        return new AnalysisSeries(
            ColorAxisLabel(),
            FieldAxisLabel(),
            points,
            Name: name,
            ColorIndex: _allWavelengths ? colorIndex : 10,
            LineWidth: 1.25,
            XQuantity: Optic.ImageSpaceAfocal ? AnalysisAxisQuantity.Angle : AnalysisAxisQuantity.ImageHeight,
            XUnit: Optic.ImageSpaceAfocal ? AnalysisAxisUnit.Milliradian : AnalysisAxisUnit.Micrometer,
            YQuantity: AnalysisTrace.FieldAxisQuantity(Optic),
            YUnit: AnalysisTrace.FieldAxisUnit(Optic));
    }

    private double ImageCoordinate(double normalizedField, double wavelengthMicrometers)
    {
        try
        {
            if (_useRealRays)
            {
                var bundle = Optic.SequentialRayTracer.RayGenerator.GenerateGeneric(
                    0,
                    normalizedField,
                    0,
                    0,
                    wavelengthMicrometers,
                    aimAtStop: Optic.RayAimingEnabled);
                var sample = Optic.SequentialRayTracer.TraceFinalSamples(bundle).SingleOrDefault();
                if (sample is null || sample.Intensity <= 0) return double.NaN;
                return Optic.ImageSpaceAfocal
                    ? ImageSpaceAnalysisSupport.DirectionAnglesMilliradians(
                        Optic.SurfaceGroup.Items[^1], sample.Direction).Y
                    : sample.Position.Y * 1000;
            }

            var trace = Optic.Paraxial.TraceNormalizedPupil(
                normalizedField,
                new[] { 0.0 },
                wavelengthMicrometers);
            if (trace.Heights.Count == 0) return double.NaN;
            return Optic.ImageSpaceAfocal
                ? ImageSpaceAnalysisSupport.DirectionAnglesMilliradians(
                    Optic.SurfaceGroup.Items[^1], new Vector3D(0, trace.Slopes[^1][0], 1)).Y
                : trace.Heights[^1][0] * 1000;
        }
        catch (InvalidOperationException)
        {
            return double.NaN;
        }
    }

    private AnalysisSeries AiryBoundary(
        double sign,
        double maximumField,
        Wavelength primary,
        string name,
        double onAxisRadius)
    {
        return new AnalysisSeries(
            ColorAxisLabel(),
            FieldAxisLabel(),
            Enumerable.Range(0, _sampleCount)
                .Select(index =>
                {
                    var fraction = index / (double)(_sampleCount - 1);
                    return new AnalysisPoint(
                        sign * (Optic.ImageSpaceAfocal ? onAxisRadius : AiryRadius(fraction, primary)),
                        fraction * maximumField);
                })
                .ToArray(),
            Name: name,
            LineStyle: AnalysisLineStyle.Dotted,
            ColorIndex: 10,
            LineWidth: 1,
            XQuantity: Optic.ImageSpaceAfocal ? AnalysisAxisQuantity.Angle : AnalysisAxisQuantity.ImageHeight,
            XUnit: Optic.ImageSpaceAfocal ? AnalysisAxisUnit.Milliradian : AnalysisAxisUnit.Micrometer,
            YQuantity: AnalysisTrace.FieldAxisQuantity(Optic),
            YUnit: AnalysisTrace.FieldAxisUnit(Optic));
    }

    private double AiryRadius(double normalizedField, Wavelength primary)
    {
        if (Optic.ImageSpaceAfocal)
            return ImageSpaceAnalysisSupport.AfocalAiryRadiusMilliradians(Optic, primary);
        var workingFNumber = DiffractionEngine.WorkingFNumber(
            Optic,
            (0, normalizedField),
            primary,
            aimAtStop: Optic.RayAimingEnabled);
        return 1.22 * primary.Micrometers * Math.Max(0, workingFNumber);
    }

    private string ColorAxisLabel() => Optic.ImageSpaceAfocal ? "mrad" : "µm";

    private string FieldAxisLabel()
    {
        return Optic.FieldDefinition switch
        {
            FieldDefinitionKind.ObjectHeight => "视场：物高 单位：毫米",
            FieldDefinitionKind.ParaxialImageHeight => "视场：近轴像高 单位：毫米",
            FieldDefinitionKind.RealImageHeight => "视场：实际像高 单位：毫米",
            _ => "视场：角度 单位：度"
        };
    }

    private string FieldUnit()
    {
        return Optic.FieldDefinition == FieldDefinitionKind.Angle ? "度" : "毫米";
    }

    private static double NiceAxisLimit(double maximum)
    {
        if (!double.IsFinite(maximum) || maximum <= 1e-12)
        {
            return 1;
        }

        var target = maximum * 1.12;
        var exponent = Math.Floor(Math.Log10(target));
        var scale = Math.Pow(10, exponent);
        var normalized = target / scale;
        var nice = normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10;
        return nice * scale;
    }
}

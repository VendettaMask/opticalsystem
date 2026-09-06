namespace OptilandWorkbench.Core.Analysis;

public sealed class FftPsfCrossSectionAnalysis : BaseAnalysis
{
    private readonly int _sampling;
    private readonly int? _gridSize;
    private readonly string _row;
    private readonly double _graphScaleMicrometers;
    private readonly int _wavelengthNumber;
    private readonly int _fieldNumber;
    private readonly string _type;
    private readonly bool _usePolarization;
    private readonly bool _normalize;

    public FftPsfCrossSectionAnalysis(
        Optic optic,
        int sampling = 64,
        int? gridSize = null,
        string row = "中心",
        double graphScaleMicrometers = 0,
        int wavelengthNumber = 0,
        int fieldNumber = 1,
        string type = "X-线性",
        bool usePolarization = false,
        bool normalize = false)
        : base(optic)
    {
        _sampling = Math.Max(2, sampling);
        _gridSize = gridSize;
        _row = row;
        _graphScaleMicrometers = Math.Max(0, graphScaleMicrometers);
        _wavelengthNumber = Math.Max(0, wavelengthNumber);
        _fieldNumber = Math.Max(1, fieldNumber);
        _type = type;
        _usePolarization = usePolarization;
        _normalize = normalize;
    }

    public override string Name => "FFT PSF Cross Section";

    public override AnalysisData GenerateData()
    {
        var source = new PsfAnalysis(
            Optic,
            _sampling,
            _gridSize ?? (_sampling * 2),
            _wavelengthNumber,
            _fieldNumber,
            imageDeltaMicrometers: 0,
            type: _type,
            displayAs: "截面",
            usePolarization: _usePolarization,
            normalize: _normalize, zemaxCompatible: true).GenerateData();
        return PsfProfilePresentation.CreateCrossSectionData(
            Name,
            _usePolarization
                ? "Polarization-weighted scalar FFT PSF 截面（Experimental）"
                : "PSF截面图",
            source,
            _type,
            _row,
            _graphScaleMicrometers,
            interpolateFftProfile: true);
    }
}

public sealed class FftLineEdgeSpreadAnalysis : BaseAnalysis
{
    private readonly int _sampling;
    private readonly int? _gridSize;
    private readonly string _spread;
    private readonly double _graphScaleMicrometers;
    private readonly int _wavelengthNumber;
    private readonly int _fieldNumber;
    private readonly string _type;
    private readonly bool _usePolarization;
    private readonly bool _useCoherentPsf;

    public FftLineEdgeSpreadAnalysis(
        Optic optic,
        int sampling = 64,
        int? gridSize = null,
        string spread = "线",
        double graphScaleMicrometers = 0,
        int wavelengthNumber = 0,
        int fieldNumber = 1,
        string type = "X-线性",
        bool usePolarization = false,
        bool useCoherentPsf = false)
        : base(optic)
    {
        _sampling = Math.Max(2, sampling);
        _gridSize = gridSize;
        _spread = spread;
        _graphScaleMicrometers = Math.Max(0, graphScaleMicrometers);
        _wavelengthNumber = Math.Max(0, wavelengthNumber);
        _fieldNumber = Math.Max(1, fieldNumber);
        _type = type;
        _usePolarization = usePolarization;
        _useCoherentPsf = useCoherentPsf;
    }

    public override string Name => "FFT Line Edge Spread";

    public override AnalysisData GenerateData()
    {
        var source = new PsfAnalysis(
            Optic,
            _sampling,
            _gridSize ?? (_sampling * 2),
            _wavelengthNumber,
            _fieldNumber,
            imageDeltaMicrometers: 0,
            type: "线性",
            displayAs: "截面",
            usePolarization: _usePolarization,
            normalize: false, zemaxCompatible: true).GenerateData();
        return PsfProfilePresentation.CreateLineEdgeSpreadData(
            Name,
            source,
            _spread,
            _graphScaleMicrometers,
            _type,
            _useCoherentPsf,
            interpolateFftProfile: true);
    }
}

public sealed class HuygensPsfCrossSectionAnalysis : BaseAnalysis
{
    private readonly int _numRays;
    private readonly int _imageSize;
    private readonly double _pixelPitchMillimeters;
    private readonly int _wavelengthNumber;
    private readonly int _fieldNumber;
    private readonly string _profileType;
    private readonly bool _usePolarization;
    private readonly bool _useCentroid;

    public HuygensPsfCrossSectionAnalysis(
        Optic optic,
        int numRays = 9,
        int imageSize = 32,
        double pixelPitchMillimeters = 0.005,
        int wavelengthNumber = -1,
        int fieldNumber = 0,
        string profileType = "Both",
        bool usePolarization = false,
        bool useCentroid = false)
        : base(optic)
    {
        _numRays = Math.Max(2, numRays);
        _imageSize = Math.Max(1, imageSize);
        _pixelPitchMillimeters = Math.Max(0, pixelPitchMillimeters);
        _wavelengthNumber = Math.Max(0, wavelengthNumber);
        _fieldNumber = Math.Max(1, fieldNumber);
        _profileType = profileType;
        _usePolarization = usePolarization;
        _useCentroid = useCentroid;
    }

    public override string Name => "Huygens PSF Cross Section";

    public override AnalysisData GenerateData()
    {
        var source = new HuygensPsfAnalysis(
            Optic,
            _numRays,
            _imageSize,
            _pixelPitchMillimeters,
            wavelengthNumber: _wavelengthNumber,
            fieldNumber: _fieldNumber,
            type: "线性",
            displayAs: "截面",
            usePolarization: _usePolarization,
            normalize: false,
            useCentroid: _useCentroid).GenerateData();
        var graphScaleMicrometers = source.Values.TryGetValue("ImageExtentMicrometers", out var extent)
            ? Convert.ToDouble(extent) / 2
            : 0;
        return PsfProfilePresentation.CreateCrossSectionData(
            Name,
            _usePolarization
                ? "Polarization-weighted scalar Huygens PSF 截面（Experimental）"
                : "惠更斯 PSF 截面图",
            source,
            _profileType,
            graphScaleMicrometers: graphScaleMicrometers);
    }
}

internal static class PsfProfilePresentation
{
    public static AnalysisData CreateCrossSectionData(
        string name,
        string title,
        AnalysisData source,
        string profileType = "Both",
        string row = "中心",
        double graphScaleMicrometers = 0,
        bool interpolateFftProfile = false)
    {
        if (!TryReadHeatmap(source, out var heatmap))
        {
            return Empty(name, source);
        }

        var horizontal = CenterProfile(heatmap.Points, horizontal: true);
        var vertical = CenterProfile(heatmap.Points, horizontal: false);
        if (interpolateFftProfile)
        {
            horizontal = InterpolatePeriodicFftProfile(horizontal);
            vertical = InterpolatePeriodicFftProfile(vertical);
        }
        var logarithmic = profileType.Contains("对数", StringComparison.Ordinal)
            || profileType.Contains("log", StringComparison.OrdinalIgnoreCase);
        var series = profileType.StartsWith("X", StringComparison.OrdinalIgnoreCase)
            ? new[]
            {
                CreateProfileSeries(
                    horizontal,
                    "X 截面",
                    0,
                    AnalysisLineStyle.Solid,
                    normalize: false,
                    logarithmic,
                    independentAxisIsX: true)
            }
            : profileType.StartsWith("Y", StringComparison.OrdinalIgnoreCase)
                ? new[]
                {
                    CreateProfileSeries(
                        vertical,
                        "Y 截面",
                        0,
                        AnalysisLineStyle.Solid,
                        normalize: false,
                        logarithmic,
                        independentAxisIsX: false)
                }
                : new[]
                {
                    CreateProfileSeries(
                        horizontal,
                        "X 截面",
                        0,
                        AnalysisLineStyle.Solid,
                        normalize: true,
                        logarithmic: false,
                        independentAxisIsX: true),
                    CreateProfileSeries(
                        vertical,
                        "Y 截面",
                        1,
                        AnalysisLineStyle.Dashed,
                        normalize: true,
                        logarithmic: false,
                        independentAxisIsX: false)
                };
        var values = CopyValues(source, ("Display", "Cross Section")).ToDictionary(
            item => item.Key,
            item => item.Value);
        values["Row"] = row;
        values["GraphScaleMicrometers"] = graphScaleMicrometers;
        values["ProfileType"] = profileType;
        double? xMinimum = graphScaleMicrometers > 0 ? -graphScaleMicrometers : null;
        double? xMaximum = graphScaleMicrometers > 0 ? graphScaleMicrometers : null;
        return new AnalysisData(
            name,
            values,
            series[0],
            series,
            new AnalysisPlotOptions(
                Title: title,
                ShowVerticalZeroLine: false,
                XMinimum: xMinimum,
                XMaximum: xMaximum,
                YMinimum: logarithmic ? null : 0,
                YMaximum: logarithmic ? null : 1,
                ShowLegend: series.Length > 1,
                GridOpacity: 0.3,
                LegendBelow: series.Length > 1));
    }

    private static IReadOnlyList<AnalysisPoint> TransformProfile(
        IReadOnlyList<AnalysisPoint> points,
        bool normalize,
        bool logarithmic)
    {
        var transformed = normalize ? Normalize(points) : points;
        if (!logarithmic)
        {
            return transformed;
        }

        return transformed
            .Select(point => new AnalysisPoint(
                point.X,
                10 * Math.Log10(Math.Max(1e-12, point.Y))))
            .ToArray();
    }

    public static AnalysisData CreateLineEdgeSpreadData(
        string name,
        AnalysisData source,
        string spread = "线",
        double graphScaleMicrometers = 0,
        string profileType = "X-线性",
        bool useCoherentPsf = false,
        bool interpolateFftProfile = false)
    {
        if (!TryReadHeatmap(source, out var heatmap))
        {
            return Empty(name, source);
        }

        var lineRunsAlongX = profileType.StartsWith("X", StringComparison.OrdinalIgnoreCase);
        var logarithmic = profileType.Contains("对数", StringComparison.Ordinal)
            || profileType.Contains("log", StringComparison.OrdinalIgnoreCase);
        var lineProfile = LineSpreadProfile(
            heatmap.Points,
            independentAxisIsX: !lineRunsAlongX,
            useCoherentPsf);
        if (interpolateFftProfile) lineProfile = InterpolatePeriodicFftProfile(lineProfile);
        var linePoints = Normalize(lineProfile);
        var total = linePoints.Sum(point => Math.Max(0, point.Y));
        var cumulative = 0.0;
        var edgePoints = linePoints.Select(point =>
        {
            cumulative += Math.Max(0, point.Y);
            return new AnalysisPoint(point.X, total > 0 ? cumulative / total : 0);
        }).ToArray();
        var edgeSpread = spread.Contains("边缘", StringComparison.Ordinal);
        var selected = edgeSpread ? edgePoints : linePoints;
        if (logarithmic)
        {
            selected = selected
                .Select(point => new AnalysisPoint(
                    point.X,
                    10 * Math.Log10(Math.Max(1e-12, point.Y))))
                .ToArray();
        }

        var functionName = edgeSpread ? "边缘扩散函数" : "线扩散函数";
        var series = new AnalysisSeries(
            lineRunsAlongX ? "Y-位置 µm" : "X-位置 µm",
            logarithmic ? "相对辐射照度 (dB)" : "相对辐射照度",
            selected,
            Name: functionName,
            ColorIndex: 0,
            LineWidth: 1.8,
            XQuantity: AnalysisAxisQuantity.ImageHeight,
            XUnit: AnalysisAxisUnit.Micrometer,
            YQuantity: AnalysisAxisQuantity.Irradiance,
            YUnit: logarithmic ? AnalysisAxisUnit.Decibel : AnalysisAxisUnit.Dimensionless);
        var values = CopyValues(source, ("Display", functionName)).ToDictionary(
            item => item.Key,
            item => item.Value);
        values["Spread"] = spread;
        values["GraphScaleMicrometers"] = graphScaleMicrometers;
        values["ProfileType"] = profileType;
        values["UseCoherentPsf"] = useCoherentPsf;
        double? xMinimum = graphScaleMicrometers > 0 ? -graphScaleMicrometers : null;
        double? xMaximum = graphScaleMicrometers > 0 ? graphScaleMicrometers : null;
        return new AnalysisData(
            name,
            values,
            series,
            new[] { series },
            new AnalysisPlotOptions(
                Title: $"FFT {functionName}",
                XMinimum: xMinimum,
                XMaximum: xMaximum,
                YMinimum: logarithmic ? null : 0,
                YMaximum: logarithmic ? null : 1,
                ShowLegend: false,
                GridOpacity: 0.3,
                LegendBelow: false));
    }

    private static bool TryReadHeatmap(AnalysisData source, out AnalysisSeries heatmap)
    {
        heatmap = source.PlotSeries.FirstOrDefault(series => series.Kind == AnalysisSeriesKind.Heatmap)
            ?? new AnalysisSeries("", "", Array.Empty<AnalysisPoint>());
        return heatmap.Kind == AnalysisSeriesKind.Heatmap && heatmap.Points.Count > 0;
    }

    private static IReadOnlyList<AnalysisPoint> InterpolatePeriodicFftProfile(IReadOnlyList<AnalysisPoint> points)
    {
        if (points.Count < 3) return points;
        // An FFT samples a periodic interval. Include the matching left endpoint
        // before interpolation; this is the existing right endpoint, not extrapolation.
        var spacing = points[1].X - points[0].X;
        var x = new[] { points[0].X - spacing }.Concat(points.Select(p => p.X)).ToArray();
        var y = new[] { points[^1].Y }.Concat(points.Select(p => p.Y)).ToArray();
        var count = Math.Max(201, 2 * points.Count + 1);
        var coordinates = Enumerable.Range(0, count).Select(i => x[0] + (x[^1] - x[0]) * i / (count - 1.0)).ToArray();
        var values = MtfThroughFocusAnalysis.CubicSplineInterpolate(x, y, coordinates);
        return coordinates.Select((v, i) => new AnalysisPoint(v, Math.Max(0, values[i]))).ToArray();
    }

    private static IReadOnlyList<AnalysisPoint> CenterProfile(
        IReadOnlyList<AnalysisPoint> points,
        bool horizontal)
    {
        var centerCoordinate = points
            .Select(point => horizontal ? point.Y : point.X)
            .Distinct()
            .OrderBy(Math.Abs)
            .First();
        var scale = Math.Max(1, Math.Abs(centerCoordinate));
        var tolerance = scale * 1e-9;
        return points
            .Where(point => Math.Abs((horizontal ? point.Y : point.X) - centerCoordinate) <= tolerance)
            .OrderBy(point => horizontal ? point.X : point.Y)
            .Select(point => new AnalysisPoint(
                horizontal ? point.X : point.Y,
                Math.Max(0, point.Value ?? 0)))
            .ToArray();
    }

    private static IReadOnlyList<AnalysisPoint> LineSpreadProfile(
        IReadOnlyList<AnalysisPoint> points,
        bool independentAxisIsX,
        bool useCoherentPsf)
    {
        return points
            .GroupBy(point => independentAxisIsX ? point.X : point.Y)
            .OrderBy(group => group.Key)
            .Select(group => new AnalysisPoint(
                group.Key,
                useCoherentPsf
                    ? Math.Pow(group.Sum(point => Math.Sqrt(Math.Max(0, point.Value ?? 0))), 2)
                    : group.Sum(point => Math.Max(0, point.Value ?? 0))))
            .ToArray();
    }

    private static AnalysisSeries CreateProfileSeries(
        IReadOnlyList<AnalysisPoint> points,
        string name,
        int colorIndex,
        AnalysisLineStyle lineStyle,
        bool normalize,
        bool logarithmic,
        bool independentAxisIsX)
    {
        var axisLabel = independentAxisIsX ? "X-位置 µm" : "Y-位置 µm";
        return new AnalysisSeries(
            axisLabel,
            logarithmic ? "相对辐射照度 (dB)" : "相对辐射照度",
            TransformProfile(points, normalize, logarithmic),
            Name: name,
            LineStyle: lineStyle,
            ColorIndex: colorIndex,
            LineWidth: 1.8,
            XQuantity: AnalysisAxisQuantity.ImageHeight,
            XUnit: AnalysisAxisUnit.Micrometer,
            YQuantity: AnalysisAxisQuantity.Irradiance,
            YUnit: logarithmic ? AnalysisAxisUnit.Decibel : AnalysisAxisUnit.Dimensionless);
    }

    private static IReadOnlyList<AnalysisPoint> Normalize(IReadOnlyList<AnalysisPoint> points)
    {
        var maximum = points.Count == 0 ? 0 : points.Max(point => point.Y);
        return points
            .Select(point => new AnalysisPoint(point.X, maximum > 0 ? point.Y / maximum : 0))
            .ToArray();
    }

    private static IReadOnlyDictionary<string, object> CopyValues(
        AnalysisData source,
        (string Key, object Value) extra)
    {
        var values = source.Values.ToDictionary(item => item.Key, item => item.Value);
        values[extra.Key] = extra.Value;
        return values;
    }

    private static AnalysisData Empty(string name, AnalysisData source)
    {
        return new AnalysisData(
            name,
            source.Values.Count > 0
                ? source.Values
                : new Dictionary<string, object> { ["Status"] = "No PSF data" },
            Outcome: source.Outcome == AnalysisOutcome.Success ? AnalysisOutcome.Unavailable : source.Outcome,
            OutcomeReason: source.OutcomeReason ?? "No PSF data");
    }
}

namespace OptilandWorkbench.Core.Analysis;

public sealed class FoucaultAnalysis : BaseAnalysis
{
    private readonly int _sampling;
    private readonly string _type;
    private readonly string _displayAs;
    private readonly string _knifeEdge;
    private readonly string _dataSource;
    private readonly int _wavelengthNumber;
    private readonly int _fieldNumber;
    private readonly double _positionMicrometers;
    private readonly bool _usePolarization;

    public FoucaultAnalysis(
        Optic optic,
        int sampling = 32,
        string type = "线性",
        string displayAs = "灰度",
        string knifeEdge = "水平线上",
        string dataSource = "计算的",
        int wavelengthNumber = 0,
        int fieldNumber = 1,
        double positionMicrometers = 0,
        bool usePolarization = false) : base(optic)
    {
        _sampling = Math.Clamp(sampling, 8, 512);
        _type = type;
        _displayAs = displayAs;
        _knifeEdge = knifeEdge;
        _dataSource = dataSource;
        _wavelengthNumber = Math.Max(0, wavelengthNumber);
        _fieldNumber = Math.Max(1, fieldNumber);
        _positionMicrometers = double.IsFinite(positionMicrometers) ? positionMicrometers : 0;
        _usePolarization = usePolarization;
    }

    public override string Name => "Foucault Analysis";

    public override AnalysisData GenerateData()
    {
        var wavelengths = Optic.Wavelengths.ToArray();
        var wavelength = _wavelengthNumber > 0
            ? wavelengths.ElementAtOrDefault(Math.Clamp(_wavelengthNumber - 1, 0, Math.Max(0, wavelengths.Length - 1)))
            : wavelengths.FirstOrDefault(item => item.IsPrimary) ?? wavelengths.FirstOrDefault();
        if (wavelength is null)
        {
            return new AnalysisData(Name, new Dictionary<string, object> { ["Status"] = "No wavelengths" });
        }

        var fields = SpotAnalysisEngine.DefinedFields(Optic);
        var field = fields[Math.Clamp(_fieldNumber - 1, 0, Math.Max(0, fields.Count - 1))];
        var wavefront = WavefrontEngine.GenerateChiefRayUniform(
            Optic,
            field,
            wavelength,
            _sampling,
            cellCentered: true,
            aimAtStop: true);
        var valid = wavefront.Samples
            .Where(sample => sample.Intensity > 0 && double.IsFinite(sample.OpdWaves))
            .ToArray();
        var horizontalKnife = _knifeEdge.Contains("水平", StringComparison.Ordinal);
        var reverse = _knifeEdge.Contains("下", StringComparison.Ordinal)
            || _knifeEdge.Contains("右", StringComparison.Ordinal);
        var gradients = valid.Select(sample =>
        {
            var gradient = EstimateGradient(valid, sample, horizontalKnife);
            return (Sample: sample, Gradient: reverse ? -gradient : gradient);
        }).ToArray();
        var center = gradients.Select(item => item.Gradient).DefaultIfEmpty(0).Average();
        var maximumMagnitude = gradients
            .Select(item => Math.Abs(item.Gradient - center))
            .DefaultIfEmpty(1)
            .Order()
            .ElementAtOrDefault(Math.Max(0, (int)Math.Floor(gradients.Length * 0.95) - 1));
        maximumMagnitude = Math.Max(1e-12, maximumMagnitude);
        var knifeShift = Math.Clamp(_positionMicrometers / 100.0, -0.45, 0.45);
        var points = gradients.Select(item =>
        {
            var radius = Math.Sqrt(
                (item.Sample.NormalizedPupilX * item.Sample.NormalizedPupilX)
                + (item.Sample.NormalizedPupilY * item.Sample.NormalizedPupilY));
            var slopeSignal = 0.28
                + (0.34 * ((item.Gradient - center) / maximumMagnitude))
                - knifeShift;
            var edgeSignal = radius <= 0.84
                ? 0
                : Math.Pow(Math.Clamp((radius - 0.84) / 0.16, 0, 1), 1.6) * 0.72;
            var signal = Math.Clamp(slopeSignal + edgeSignal, 0, 1);
            return new AnalysisPoint(
                item.Sample.NormalizedPupilX,
                item.Sample.NormalizedPupilY,
                Value: signal);
        }).ToArray();
        var series = new AnalysisSeries(
            "相对光瞳位置",
            "相对光瞳位置",
            points,
            AnalysisSeriesKind.Heatmap,
            ValueLabel: "归一化刀口响应",
            ValueMinimum: 0,
            ValueMaximum: 1,
            XQuantity: AnalysisAxisQuantity.PupilCoordinate,
            XUnit: AnalysisAxisUnit.Dimensionless,
            YQuantity: AnalysisAxisQuantity.PupilCoordinate,
            YUnit: AnalysisAxisUnit.Dimensionless,
            ValueQuantity: AnalysisAxisQuantity.Intensity,
            ValueUnit: AnalysisAxisUnit.Dimensionless);
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["Sampling"] = $"{_sampling} x {_sampling}",
            ["Type"] = _type,
            ["DisplayAs"] = _displayAs,
            ["KnifeEdge"] = _knifeEdge,
            ["DataSource"] = _dataSource,
            ["WavelengthMicrometers"] = wavelength.Micrometers,
            ["WavelengthNumber"] = Array.IndexOf(wavelengths, wavelength) + 1,
            ["FieldNumber"] = _fieldNumber,
            ["FieldHx"] = field.Hx,
            ["FieldHy"] = field.Hy,
            ["KnifePositionMicrometers"] = _positionMicrometers,
            ["UsePolarization"] = _usePolarization,
            ["MinimumResponse"] = points.Select(point => point.Value ?? 0).DefaultIfEmpty(0).Min(),
            ["MaximumResponse"] = points.Select(point => point.Value ?? 0).DefaultIfEmpty(0).Max()
        }, series, new[] { series }, new AnalysisPlotOptions(
            Title: "Foucault Analysis",
            EqualAspect: true,
            XMinimum: -1,
            XMaximum: 1,
            YMinimum: -1,
            YMaximum: 1));
    }

    private static double EstimateGradient(
        IReadOnlyList<WavefrontSample> samples,
        WavefrontSample origin,
        bool alongY)
    {
        var sameAxis = samples
            .Where(sample => alongY
                ? Math.Abs(sample.NormalizedPupilX - origin.NormalizedPupilX) <= 1e-9
                : Math.Abs(sample.NormalizedPupilY - origin.NormalizedPupilY) <= 1e-9)
            .Select(sample => (
                Coordinate: alongY ? sample.NormalizedPupilY : sample.NormalizedPupilX,
                sample.OpdWaves))
            .OrderBy(item => item.Coordinate)
            .ToArray();
        var coordinate = alongY ? origin.NormalizedPupilY : origin.NormalizedPupilX;
        var lower = sameAxis.LastOrDefault(item => item.Coordinate < coordinate);
        var upper = sameAxis.FirstOrDefault(item => item.Coordinate > coordinate);
        if (upper.Coordinate > lower.Coordinate)
        {
            return (upper.OpdWaves - lower.OpdWaves) / (upper.Coordinate - lower.Coordinate);
        }

        return 0;
    }
}

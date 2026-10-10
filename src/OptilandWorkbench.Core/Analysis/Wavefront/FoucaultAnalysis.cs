namespace OptilandWorkbench.Core.Analysis;

public sealed class FoucaultAnalysis : BaseAnalysis
{
    private readonly int _sampling;
    private readonly string _type;
    private readonly string _displayAs;
    private readonly string _knifeEdge;
    private readonly FoucaultKnifeEdge _knife;
    private readonly int _wavelengthNumber;
    private readonly int _fieldNumber;
    private readonly double _positionMicrometers;
    private readonly bool _usePolarization;

    public FoucaultAnalysis(Optic optic, int sampling = 32, string type = "线性",
        string displayAs = "灰度", string knifeEdge = "水平线上", string dataSource = "计算的",
        int wavelengthNumber = 0, int fieldNumber = 1, double positionMicrometers = 0,
        bool usePolarization = false) : base(optic)
    {
        if (sampling is < 8 or > 512) throw new ArgumentOutOfRangeException(nameof(sampling));
        if (!double.IsFinite(positionMicrometers)) throw new ArgumentOutOfRangeException(nameof(positionMicrometers));
        if (wavelengthNumber < 0) throw new ArgumentOutOfRangeException(nameof(wavelengthNumber));
        if (fieldNumber < 1) throw new ArgumentOutOfRangeException(nameof(fieldNumber));
        if (type is not ("线性" or "对数")) throw new ArgumentException("Foucault 支持线性或对数显示。", nameof(type));
        if (displayAs is not ("灰度" or "伪彩色")) throw new ArgumentException("Unsupported Foucault display.", nameof(displayAs));
        if (dataSource != "计算的") throw new NotSupportedException("Foucault 参考位图及配准差值尚未实现。");
        _knife = knifeEdge switch
        {
            "水平线上" => FoucaultKnifeEdge.HorizontalAbove,
            "水平线下" => FoucaultKnifeEdge.HorizontalBelow,
            "垂直线左" => FoucaultKnifeEdge.VerticalLeft,
            "垂直线右" => FoucaultKnifeEdge.VerticalRight,
            _ => throw new ArgumentException("Unsupported knife direction.", nameof(knifeEdge))
        };
        _sampling = sampling; _type = type; _displayAs = displayAs; _knifeEdge = knifeEdge;
        _wavelengthNumber = wavelengthNumber; _fieldNumber = fieldNumber;
        _positionMicrometers = positionMicrometers; _usePolarization = usePolarization;
    }

    public override string Name => "Foucault Analysis";

    public override AnalysisData GenerateData()
    {
        var wavelengths = Optic.Wavelengths.ToArray();
        if (wavelengths.Length == 0) return AnalysisData.Unavailable(Name, "No wavelengths");
        if (_wavelengthNumber > wavelengths.Length) throw new ArgumentOutOfRangeException("wavelengthNumber");
        var wavelength = _wavelengthNumber > 0 ? wavelengths[_wavelengthNumber - 1]
            : wavelengths.FirstOrDefault(item => item.IsPrimary) ?? wavelengths[0];
        var fields = SpotAnalysisEngine.DefinedFields(Optic);
        if (_fieldNumber > fields.Count) throw new ArgumentOutOfRangeException("fieldNumber");
        var field = fields[_fieldNumber - 1];
        var result = FoucaultEngine.Compute(Optic, field, wavelength, _sampling, _knife,
            _positionMicrometers, _usePolarization);
        var points = Enumerable.Range(0, _sampling).SelectMany(row => Enumerable.Range(0, _sampling)
            .Select(column => new AnalysisPoint(-1 + (2d * column + 1) / _sampling,
                -1 + (2d * row + 1) / _sampling, Value: result.Intensity[row, column]))).ToArray();
        // Display modes do not peak-normalize, clip or fabricate the physical intensity.
        var series = new AnalysisSeries("相对光瞳位置", "相对光瞳位置", points,
            AnalysisSeriesKind.Heatmap, ValueLabel: "相对入射强度", ValueMinimum: 0,
            ValueMaximum: Math.Max(1, points.Max(point => point.Value!.Value)),
            XQuantity: AnalysisAxisQuantity.PupilCoordinate, XUnit: AnalysisAxisUnit.Dimensionless,
            YQuantity: AnalysisAxisQuantity.PupilCoordinate, YUnit: AnalysisAxisUnit.Dimensionless,
            ValueQuantity: AnalysisAxisQuantity.Intensity, ValueUnit: AnalysisAxisUnit.Dimensionless);
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["Sampling"] = $"{_sampling} x {_sampling}", ["Type"] = _type, ["DisplayAs"] = _displayAs,
            ["KnifeEdge"] = _knifeEdge, ["KnifeAxis"] = _knife is FoucaultKnifeEdge.HorizontalAbove or FoucaultKnifeEdge.HorizontalBelow ? "Y" : "X",
            ["DataSource"] = "计算的", ["WavelengthMicrometers"] = wavelength.Micrometers,
            ["WavelengthNumber"] = Array.IndexOf(wavelengths, wavelength) + 1,
            ["FieldNumber"] = _fieldNumber, ["FieldHx"] = field.Hx, ["FieldHy"] = field.Hy,
            ["KnifePositionMicrometers"] = _positionMicrometers, ["UsePolarization"] = _usePolarization,
            ["UseRayAiming"] = Optic.RayAimingEnabled,
            ["ComputationModel"] = "FFT complex-field knife filtering and inverse-FFT ideal pupil reimaging",
            ["PolarizationModel"] = !_usePolarization ? "Scalar ray amplitude"
                : Optic.Polarization.Unpolarized ? "Incoherent equal-weight X/Y Jones inputs; coherent XYZ output fields"
                : "System Jones input; coherent XYZ output fields",
            ["Normalization"] = "Unit incident ray power; no shadowgram peak normalization",
            ["FieldConvention"] = "Conjugated positive-time pupil; -2pi OPD; negative-exponent focal FFT",
            ["FourierGridSize"] = result.FourierGridSize,
            ["ImageSpacingXMicrometers"] = result.ImageSpacingXMicrometers,
            ["ImageSpacingYMicrometers"] = result.ImageSpacingYMicrometers,
            ["InputPupilPowerSum"] = result.InputPower, ["OutputNearFieldPowerSum"] = result.OutputPower,
            ["DisplayedNearFieldPowerSum"] = result.DisplayedPower, ["KnifeThroughput"] = result.KnifeThroughput,
            ["CoherentChannels"] = result.CoherentChannels, ["IlluminatedSamples"] = result.IlluminatedSamples,
            ["VignettedRayCount"] = result.VignettedSamples,
            ["MinimumResponse"] = points.Min(point => point.Value!.Value), ["MaximumResponse"] = points.Max(point => point.Value!.Value)
        }, series, new[] { series }, new AnalysisPlotOptions(Title: "Foucault 物理刀口阴影图（FFT）",
            EqualAspect: true, XMinimum: -1, XMaximum: 1, YMinimum: -1, YMaximum: 1,
            LogarithmicIntensity: _type == "对数"));
    }
}

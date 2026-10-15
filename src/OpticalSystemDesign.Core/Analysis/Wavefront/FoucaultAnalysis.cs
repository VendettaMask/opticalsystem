namespace OpticalSystemDesign.Core.Analysis;

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
    private readonly FoucaultPropagationMode _propagationMode;
    private readonly double _knifeDistance, _observationDistance, _observationHalfWidth;

    public FoucaultAnalysis(Optic optic, int sampling = 32, string type = "线性",
        string displayAs = "灰度", string knifeEdge = "水平线上", string dataSource = "计算的",
        int wavelengthNumber = 0, int fieldNumber = 1, double positionMicrometers = 0,
        bool usePolarization = false,
        FoucaultPropagationMode propagationMode = FoucaultPropagationMode.FocalFourierReimage,
        double knifeDistanceMillimeters = 0, double observationDistanceMillimeters = 0,
        double observationHalfWidthMillimeters = 0) : base(optic)
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
        if (!Enum.IsDefined(propagationMode) || !double.IsFinite(knifeDistanceMillimeters)
            || !double.IsFinite(observationDistanceMillimeters) || !double.IsFinite(observationHalfWidthMillimeters)
            || knifeDistanceMillimeters < 0 || observationDistanceMillimeters < 0 || observationHalfWidthMillimeters < 0)
            throw new ArgumentOutOfRangeException(nameof(propagationMode));
        _propagationMode = propagationMode; _knifeDistance = knifeDistanceMillimeters;
        _observationDistance = observationDistanceMillimeters; _observationHalfWidth = observationHalfWidthMillimeters;
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
            _positionMicrometers, _usePolarization, _propagationMode, _knifeDistance,
            _observationDistance, _observationHalfWidth);
        var spatial = _propagationMode != FoucaultPropagationMode.FocalFourierReimage;
        var extent = spatial ? result.ObservationHalfWidthMicrometers : 1;
        var points = Enumerable.Range(0, _sampling).SelectMany(row => Enumerable.Range(0, _sampling)
            .Select(column => new AnalysisPoint(extent * (-1 + (2d * column + 1) / _sampling),
                extent * (-1 + (2d * row + 1) / _sampling), Value: result.Intensity[row, column]))).ToArray();
        // Display modes do not peak-normalize, clip or fabricate the physical intensity.
        var series = new AnalysisSeries(spatial ? "观察平面 X（µm）" : "相对光瞳位置",
            spatial ? "观察平面 Y（µm）" : "相对光瞳位置", points,
            AnalysisSeriesKind.Heatmap,
            ValueLabel: !spatial ? "相对入射强度"
                : _propagationMode == FoucaultPropagationMode.VectorAngularSpectrum ? "相对电场能量密度" : "相对标量强度密度",
            ValueMinimum: 0,
            ValueMaximum: Math.Max(1, points.Max(point => point.Value!.Value)),
            XQuantity: spatial ? AnalysisAxisQuantity.Coordinate : AnalysisAxisQuantity.PupilCoordinate,
            XUnit: spatial ? AnalysisAxisUnit.Micrometer : AnalysisAxisUnit.Dimensionless,
            YQuantity: spatial ? AnalysisAxisQuantity.Coordinate : AnalysisAxisQuantity.PupilCoordinate,
            YUnit: spatial ? AnalysisAxisUnit.Micrometer : AnalysisAxisUnit.Dimensionless,
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
            ["ComputationModel"] = !spatial ? "FFT complex-field knife filtering and inverse-FFT ideal pupil reimaging"
                : _propagationMode == FoucaultPropagationMode.VectorAngularSpectrum
                    ? "Homogeneous Maxwell vector angular spectrum; ideal thin-amplitude knife and explicit transverse projection"
                    : "Homogeneous scalar angular spectrum; physical-plane thin-amplitude knife",
            ["PropagationMode"] = _propagationMode.ToString(),
            ["KnifeDistanceMillimeters"] = _knifeDistance, ["ObservationDistanceMillimeters"] = _observationDistance,
            ["ObservationHalfWidthMillimeters"] = result.ObservationHalfWidthMicrometers / 1000,
            ["TransverseProjectionDifferenceNormIntegral"] = result.TransverseProjectionDifferenceNormIntegral,
            ["SpatialFieldBoundary"] = spatial ? "Chief-normal plane at image chief intercept; triangle Jacobian; no caustic/multibranch reconstruction" : "Focal Fourier plane",
            ["SpatialPowerModel"] = _propagationMode == FoucaultPropagationMode.VectorAngularSpectrum
                ? "Integrated normal Poynting flux (2 Z0 Sz); displayed |E| squared is not power"
                : spatial ? "Scalar squared-amplitude integral with physical pixel area" : "Discrete pupil power sums",
            ["PolarizationModel"] = !_usePolarization
                ? _propagationMode == FoucaultPropagationMode.VectorAngularSpectrum
                    ? "Transverse X scalar-source surrogate; no Jones interface effects" : "Scalar ray amplitude"
                : Optic.Polarization.Unpolarized ? "Incoherent equal-weight X/Y Jones inputs; coherent XYZ output fields"
                : "System Jones input; coherent XYZ output fields",
            ["Normalization"] = spatial ? "Unit ray power per normalized-pupil area, mapped to physical plane by triangle Jacobian; no peak normalization"
                : "Unit incident ray power; no shadowgram peak normalization",
            ["FieldConvention"] = spatial ? "exp(-i omega t); negative geometric OPD phase; coating phase once; forward exp(+i kz z)"
                : "Conjugated positive-time pupil; -2pi OPD; negative-exponent focal FFT",
            ["FourierGridSize"] = result.FourierGridSize,
            ["ImageSpacingXMicrometers"] = result.ImageSpacingXMicrometers,
            ["ImageSpacingYMicrometers"] = result.ImageSpacingYMicrometers,
            ["InputPupilPowerSum"] = result.InputPower, ["OutputNearFieldPowerSum"] = result.OutputPower,
            ["DisplayedNearFieldPowerSum"] = result.DisplayedPower, ["KnifeThroughput"] = result.KnifeThroughput,
            ["CoherentChannels"] = result.CoherentChannels, ["IlluminatedSamples"] = result.IlluminatedSamples,
            ["VignettedRayCount"] = result.VignettedSamples,
            ["MinimumResponse"] = points.Min(point => point.Value!.Value), ["MaximumResponse"] = points.Max(point => point.Value!.Value)
        }, series, new[] { series }, new AnalysisPlotOptions(Title: spatial ? "Foucault 物理平面刀口（角谱传播）" : "Foucault 物理刀口阴影图（FFT）",
            EqualAspect: true, XMinimum: -extent, XMaximum: extent, YMinimum: -extent, YMaximum: extent,
            LogarithmicIntensity: _type == "对数"));
    }
}

using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Core.Analysis;

public sealed class FullFieldAberrationAnalysis : BaseAnalysis
{
    private readonly string _fieldShape;
    private readonly double _xFieldWidth;
    private readonly double _yFieldWidth;
    private readonly string _decomposition;
    private readonly int _maximumTerm;
    private readonly string _aberration;
    private readonly int _fieldNumber;
    private readonly int _wavelengthNumber;
    private readonly int _xFieldSamples;
    private readonly int _yFieldSamples;
    private readonly int _pupilSampling;
    private readonly string _displayAs;
    private readonly string _displayMode;

    public FullFieldAberrationAnalysis(
        Optic optic,
        string fieldShape = "椭圆",
        double xFieldWidth = 0,
        double yFieldWidth = 0,
        string decomposition = "Zernike项",
        int maximumTerm = 37,
        string aberration = "离焦",
        int fieldNumber = 1,
        int wavelengthNumber = 0,
        int xFieldSamples = 11,
        int yFieldSamples = 11,
        int pupilSampling = 32,
        string displayAs = "图标",
        string displayMode = "绝对值") : base(optic)
    {
        _fieldShape = fieldShape;
        var defaultWidth = Math.Max(1e-9, AnalysisTrace.MaxFieldValue(optic));
        _xFieldWidth = xFieldWidth > 0 ? xFieldWidth : defaultWidth;
        _yFieldWidth = yFieldWidth > 0 ? yFieldWidth : defaultWidth;
        _decomposition = decomposition;
        _maximumTerm = Math.Clamp(maximumTerm, 4, 256);
        _aberration = aberration;
        _fieldNumber = Math.Max(1, fieldNumber);
        _wavelengthNumber = Math.Max(0, wavelengthNumber);
        _xFieldSamples = Math.Clamp(xFieldSamples, 3, 101);
        _yFieldSamples = Math.Clamp(yFieldSamples, 3, 101);
        _pupilSampling = Math.Clamp(pupilSampling, 8, 128);
        _displayAs = displayAs;
        _displayMode = displayMode;
    }

    public override string Name => "Full Field Aberration";

    public override AnalysisData GenerateData()
    {
        var wavelengths = Optic.Wavelengths.ToArray();
        if (wavelengths.Length == 0)
        {
            return new AnalysisData(Name, new Dictionary<string, object> { ["Status"] = "No wavelengths" });
        }

        var primaryIndex = Array.FindIndex(wavelengths, wavelength => wavelength.IsPrimary);
        var selectedIndex = _wavelengthNumber > 0
            ? Math.Clamp(_wavelengthNumber - 1, 0, wavelengths.Length - 1)
            : Math.Max(0, primaryIndex);
        var wavelength = wavelengths[selectedIndex];
        var systemMaximumField = Math.Max(1e-12, AnalysisTrace.MaxFieldValue(Optic));
        var points = new List<AnalysisPoint>(_xFieldSamples * _yFieldSamples);
        for (var row = 0; row < _yFieldSamples; row++)
        {
            var y = -_yFieldWidth + (2 * _yFieldWidth * row / (_yFieldSamples - 1.0));
            for (var column = 0; column < _xFieldSamples; column++)
            {
                var x = -_xFieldWidth + (2 * _xFieldWidth * column / (_xFieldSamples - 1.0));
                if (IsOutsideShape(x, y))
                {
                    continue;
                }

                try
                {
                    var wavefront = WavefrontEngine.GenerateChiefRayUniform(
                        Optic,
                        (x / systemMaximumField, y / systemMaximumField),
                        wavelength,
                        _pupilSampling,
                        cellCentered: true,
                        aimAtStop: true);
                    var coefficients = ZernikeFitEngine.FitFringe(wavefront.Samples, _maximumTerm);
                    var value = SelectAberration(coefficients, wavefront);
                    if (_displayMode.Contains("绝对", StringComparison.Ordinal))
                    {
                        value = Math.Abs(value);
                    }

                    if (double.IsFinite(value))
                    {
                        points.Add(new AnalysisPoint(x, y, Value: value));
                    }
                }
                catch (InvalidOperationException)
                {
                    // Fields that cannot reach the image surface are omitted from the map.
                }
            }
        }

        var valuesOnly = points.Select(point => point.Value ?? 0).ToArray();
        var minimum = valuesOnly.DefaultIfEmpty(0).Min();
        var maximum = valuesOnly.DefaultIfEmpty(0).Max();
        var mean = valuesOnly.DefaultIfEmpty(0).Average();
        var series = new AnalysisSeries(
            FieldXAxisLabel(),
            FieldYAxisLabel(),
            points,
            AnalysisSeriesKind.Scatter,
            Name: _aberration,
            ColorIndex: 10,
            ValueLabel: "波长",
            ValueMinimum: minimum,
            ValueMaximum: maximum);
        return new AnalysisData(
            Name,
            new Dictionary<string, object>
            {
                ["WavelengthMicrometers"] = wavelength.Micrometers,
                ["FieldShape"] = _fieldShape,
                ["XFieldWidth"] = _xFieldWidth,
                ["YFieldWidth"] = _yFieldWidth,
                ["Decomposition"] = _decomposition,
                ["MaximumTerm"] = _maximumTerm,
                ["Aberration"] = _aberration,
                ["FieldNumber"] = _fieldNumber,
                ["XFieldSamples"] = _xFieldSamples,
                ["YFieldSamples"] = _yFieldSamples,
                ["PupilSampling"] = _pupilSampling,
                ["DisplayAs"] = _displayAs,
                ["DisplayMode"] = _displayMode,
                ["MeanAberrationWaves"] = mean,
                ["PlotMinimumWaves"] = minimum,
                ["PlotMaximumWaves"] = maximum,
                ["ValidFieldSamples"] = points.Count
            },
            series,
            new[] { series },
            new AnalysisPlotOptions(
                EqualAspect: true,
                XMinimum: -_xFieldWidth,
                XMaximum: _xFieldWidth,
                YMinimum: -_yFieldWidth,
                YMaximum: _yFieldWidth,
                HideTopAndRightAxes: true));
    }

    private bool IsOutsideShape(double x, double y)
    {
        if (_fieldShape.Contains("矩形", StringComparison.Ordinal))
        {
            return false;
        }

        var normalizedX = x / _xFieldWidth;
        var normalizedY = y / _yFieldWidth;
        return (normalizedX * normalizedX) + (normalizedY * normalizedY) > 1 + 1e-12;
    }

    private double SelectAberration(
        IReadOnlyList<ZernikeCoefficient> coefficients,
        WavefrontResult wavefront)
    {
        if (_aberration.Contains("RMS", StringComparison.OrdinalIgnoreCase))
        {
            return wavefront.Rms;
        }

        var numbers = _aberration switch
        {
            "离焦" => new[] { 4 },
            "像散" => new[] { 5, 6 },
            "彗差" => new[] { 7, 8 },
            "球差" => new[] { 9 },
            "X 倾斜" => new[] { 2 },
            "Y 倾斜" => new[] { 3 },
            _ => new[] { 4 }
        };
        var selected = coefficients.Where(coefficient => numbers.Contains(coefficient.Number)).ToArray();
        return selected.Length == 1
            ? selected[0].Value
            : Math.Sqrt(selected.Sum(coefficient => coefficient.Value * coefficient.Value));
    }

    private string FieldXAxisLabel()
    {
        return Optic.FieldDefinition == FieldDefinitionKind.Angle
            ? "X视场，单位：度"
            : "X视场，单位：毫米";
    }

    private string FieldYAxisLabel()
    {
        return Optic.FieldDefinition == FieldDefinitionKind.Angle
            ? "Y视场，单位：度"
            : "Y视场，单位：毫米";
    }
}

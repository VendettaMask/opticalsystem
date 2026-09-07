using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Core.Analysis;

public sealed class FullFieldAberrationAnalysis : BaseAnalysis
{
    private const double FieldTolerance = 1e-12;
    private readonly string _fieldShape;
    private readonly double _xFieldWidth;
    private readonly double _yFieldWidth;
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
        double xFieldWidth = double.NaN,
        double yFieldWidth = double.NaN,
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
        var defaultWidth = Math.Max(0, AnalysisTrace.MaxFieldValue(optic));
        _xFieldWidth = double.IsFinite(xFieldWidth) && xFieldWidth >= 0 ? xFieldWidth : defaultWidth;
        _yFieldWidth = double.IsFinite(yFieldWidth) && yFieldWidth >= 0 ? yFieldWidth : defaultWidth;
        _maximumTerm = Math.Clamp(maximumTerm, 4, ZernikeFitEngine.MaximumStandardTerm);
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
            throw new AnalysisDataUnavailableException(Name, "the optical system has no wavelengths");
        }

        var primaryIndex = Array.FindIndex(wavelengths, wavelength => wavelength.IsPrimary);
        var selectedIndex = _wavelengthNumber > 0
            ? Math.Clamp(_wavelengthNumber - 1, 0, wavelengths.Length - 1)
            : Math.Max(0, primaryIndex);
        var wavelength = wavelengths[selectedIndex];
        var systemMaximumField = AnalysisTrace.MaxFieldValue(Optic);
        var definedFields = AnalysisTrace.DefinedFieldSamples(Optic);
        var center = definedFields.Count == 0
            ? (X: 0.0, Y: 0.0)
            : (definedFields[Math.Clamp(_fieldNumber - 1, 0, definedFields.Count - 1)].X,
                definedFields[Math.Clamp(_fieldNumber - 1, 0, definedFields.Count - 1)].Y);
        var xOffsets = FieldOffsets(_xFieldWidth, _xFieldSamples);
        var yOffsets = FieldOffsets(_yFieldWidth, _yFieldSamples);
        var points = new List<AnalysisPoint>(xOffsets.Count * yOffsets.Count);
        var components = new List<double[]>();
        var failedFieldSamples = 0;
        var skippedOutsideNormalization = 0;
        foreach (var yOffset in yOffsets)
        {
            var y = center.Y + yOffset;
            foreach (var xOffset in xOffsets)
            {
                var x = center.X + xOffset;
                if (IsOutsideShape(xOffset, yOffset))
                {
                    continue;
                }

                if (!TryNormalizeRadialField(x, y, systemMaximumField, out var normalizedField))
                {
                    skippedOutsideNormalization++;
                    continue;
                }

                try
                {
                    var wavefront = WavefrontEngine.GenerateChiefRayUniform(
                        Optic,
                        normalizedField,
                        wavelength,
                        _pupilSampling,
                        cellCentered: false,
                        aimAtStop: Optic.RayAimingEnabled,
                        zemaxCentered: true);
                    var coefficients = ZernikeFitEngine.FitStandard(wavefront.Samples, _maximumTerm);
                    var selected = SelectComponents(coefficients, wavefront);
                    var value = Magnitude(selected);

                    if (double.IsFinite(value))
                    {
                        points.Add(new AnalysisPoint(x, y, Value: value));
                        components.Add(selected);
                    }
                }
                catch (InvalidOperationException)
                {
                    failedFieldSamples++;
                }
            }
        }

        if (points.Count == 0)
        {
            throw new AnalysisDataUnavailableException(
                Name,
                $"no valid samples remained: {skippedOutsideNormalization} fell outside the radial field normalization boundary and {failedFieldSamples} failed ray tracing or wavefront fitting");
        }

        // Absolute means the signed coefficient itself, not Math.Abs. For vector aberrations,
        // Relative/Average operate on the components before computing the magnitude.
        var relative = _displayMode.Contains("相对", StringComparison.Ordinal) || _displayMode.Equals("Relative", StringComparison.OrdinalIgnoreCase);
        var average = _displayMode.Contains("平均", StringComparison.Ordinal) || _displayMode.Equals("Average", StringComparison.OrdinalIgnoreCase);
        if (relative || average)
        {
            var meanComponents = Enumerable.Range(0, components[0].Length).Select(i => components.Average(c => c[i])).ToArray();
            for (var i = 0; i < points.Count; i++)
                points[i] = points[i] with { Value = Magnitude(average ? meanComponents : components[i].Select((v, j) => v - meanComponents[j]).ToArray()) };
        }

        var valuesOnly = points.Select(point => point.Value ?? 0).ToArray();
        var minimum = valuesOnly.Min();
        var maximum = valuesOnly.Max();
        var mean = valuesOnly.Average();
        var series = new AnalysisSeries(
            FieldXAxisLabel(),
            FieldYAxisLabel(),
            points,
            AnalysisSeriesKind.Scatter,
            Name: _aberration,
            ColorIndex: 10,
            ValueLabel: "波前差",
            ValueMinimum: minimum,
            ValueMaximum: maximum,
            XQuantity: AnalysisTrace.FieldAxisQuantity(Optic),
            XUnit: AnalysisTrace.FieldAxisUnit(Optic),
            YQuantity: AnalysisTrace.FieldAxisQuantity(Optic),
            YUnit: AnalysisTrace.FieldAxisUnit(Optic),
            ValueQuantity: AnalysisAxisQuantity.WavefrontError,
            ValueUnit: AnalysisAxisUnit.Wave);
        return new AnalysisData(
            Name,
            new Dictionary<string, object>
            {
                ["WavelengthMicrometers"] = wavelength.Micrometers,
                ["FieldShape"] = _fieldShape,
                ["XFieldWidth"] = _xFieldWidth,
                ["YFieldWidth"] = _yFieldWidth,
                ["Decomposition"] = "Zernike Standard",
                ["MaximumTerm"] = _maximumTerm,
                ["Aberration"] = _aberration,
                ["FieldNumber"] = _fieldNumber,
                ["FieldCenterX"] = center.X,
                ["FieldCenterY"] = center.Y,
                ["XFieldSamples"] = _xFieldSamples,
                ["YFieldSamples"] = _yFieldSamples,
                ["PupilSampling"] = _pupilSampling,
                ["DisplayAs"] = _displayAs,
                ["DisplayMode"] = _displayMode,
                ["FieldNormalization"] = "Radial",
                ["MeanAberrationWaves"] = mean,
                ["PlotMinimumWaves"] = minimum,
                ["PlotMaximumWaves"] = maximum,
                ["ValidFieldSamples"] = points.Count,
                ["FailedFieldSamples"] = failedFieldSamples,
                ["SkippedOutsideFieldNormalization"] = skippedOutsideNormalization
            },
            series,
            new[] { series },
            new AnalysisPlotOptions(
                EqualAspect: true,
                XMinimum: center.X - _xFieldWidth,
                XMaximum: center.X + _xFieldWidth,
                YMinimum: center.Y - _yFieldWidth,
                YMaximum: center.Y + _yFieldWidth,
                HideTopAndRightAxes: true));
    }

    private bool IsOutsideShape(double x, double y)
    {
        if (_fieldShape.Contains("矩形", StringComparison.Ordinal))
        {
            return false;
        }

        var normalizedX = _xFieldWidth <= FieldTolerance ? 0 : x / _xFieldWidth;
        var normalizedY = _yFieldWidth <= FieldTolerance ? 0 : y / _yFieldWidth;
        return (normalizedX * normalizedX) + (normalizedY * normalizedY) > 1 + 1e-12;
    }

    private static IReadOnlyList<double> FieldOffsets(double halfWidth, int sampleCount)
    {
        if (halfWidth <= FieldTolerance)
        {
            return new[] { 0.0 };
        }

        return Enumerable.Range(0, sampleCount)
            .Select(index => -halfWidth + (2 * halfWidth * index / (sampleCount - 1.0)))
            .ToArray();
    }

    private static bool TryNormalizeRadialField(
        double fieldX,
        double fieldY,
        double maximumField,
        out (double X, double Y) normalized)
    {
        if (maximumField <= FieldTolerance)
        {
            normalized = (0, 0);
            return Math.Abs(fieldX) <= FieldTolerance && Math.Abs(fieldY) <= FieldTolerance;
        }

        normalized = (fieldX / maximumField, fieldY / maximumField);
        return double.IsFinite(normalized.X)
            && double.IsFinite(normalized.Y)
            && ((normalized.X * normalized.X) + (normalized.Y * normalized.Y) <= 1 + FieldTolerance);
    }

    private double[] SelectComponents(
        IReadOnlyList<ZernikeCoefficient> coefficients,
        WavefrontResult wavefront)
    {
        if (_aberration.Contains("RMS", StringComparison.OrdinalIgnoreCase))
        {
            var mean = wavefront.Samples.Average(s => s.OpdWaves);
            return new[] { Math.Sqrt(wavefront.Samples.Average(s => Math.Pow(s.OpdWaves - mean, 2))) };
        }

        var numbers = _aberration switch
        {
            "离焦" => new[] { 4 },
            "像散" => new[] { 5, 6 },
            "彗差" => new[] { 7, 8 },
            "球差" => new[] { 11 },
            "X 倾斜" => new[] { 2 },
            "Y 倾斜" => new[] { 3 },
            _ => new[] { 4 }
        };
        var selected = coefficients.Where(coefficient => numbers.Contains(coefficient.Number)).ToArray();
        return selected.Select(c => c.Value).ToArray();
    }

    private static double Magnitude(double[] components) => components.Length == 1 ? components[0] : Math.Sqrt(components.Sum(v => v * v));

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

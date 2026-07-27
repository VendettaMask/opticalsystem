using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Core.Analysis;

public sealed class RmsVsWavelengthAnalysis : BaseAnalysis
{
    private readonly int _waveDensity;
    private readonly int _numRings;
    private readonly string _distribution;
    private readonly int _fieldNumber;
    private readonly string _reference;

    public RmsVsWavelengthAnalysis(
        Optic optic,
        int waveDensity = 21,
        int numRings = 6,
        string distribution = "hexapolar",
        int fieldNumber = 0,
        string reference = "centroid") : base(optic)
    {
        _waveDensity = Math.Clamp(waveDensity, 2, 100);
        _numRings = Math.Clamp(numRings, 1, 32);
        _distribution = distribution;
        _fieldNumber = Math.Max(0, fieldNumber);
        _reference = reference;
    }

    public override string Name => "RMS vs Wavelength";

    public override AnalysisData GenerateData()
    {
        var definedWavelengths = Optic.Wavelengths.ToArray();
        var fields = RmsScanSupport.SelectedFields(Optic, _fieldNumber);
        if (definedWavelengths.Length == 0 || fields.Count == 0)
        {
            return RmsScanSupport.Empty(Name);
        }

        var minimum = definedWavelengths.Min(wavelength => wavelength.Micrometers);
        var maximum = definedWavelengths.Max(wavelength => wavelength.Micrometers);
        var wavelengths = Enumerable.Range(0, _waveDensity)
            .Select(index => new Wavelength
            {
                Label = $"W{index + 1}",
                Micrometers = minimum + ((maximum - minimum) * index / (_waveDensity - 1.0)),
                Weight = 1,
                IsPrimary = true
            })
            .ToArray();
        var series = fields.Select((field, fieldIndex) => new AnalysisSeries(
            "Wavelength (\u00B5m)",
            "RMS Spot Radius (mm)",
            wavelengths.Select(wavelength => new AnalysisPoint(
                wavelength.Micrometers,
                RmsScanSupport.SpotRadius(
                    Optic,
                    (field.Hx, field.Hy),
                    new[] { wavelength },
                    _numRings,
                    _distribution,
                    _reference))).ToArray(),
            Name: field.Label,
            ColorIndex: fieldIndex)).ToArray();

        return new AnalysisData(
            Name,
            new Dictionary<string, object>
            {
                ["WaveDensity"] = _waveDensity,
                ["RayDensity"] = _numRings,
                ["Distribution"] = _distribution,
                ["FieldNumber"] = _fieldNumber,
                ["Reference"] = _reference,
                ["MinimumWavelengthMicrometers"] = minimum,
                ["MaximumWavelengthMicrometers"] = maximum
            },
            series.FirstOrDefault(),
            series,
            new AnalysisPlotOptions(
                Title: "RMS vs. Wavelength",
                XMinimum: minimum,
                XMaximum: maximum,
                YMinimum: 0,
                ShowLegend: true,
                HideTopAndRightAxes: true,
                GridOpacity: 0.25,
                LegendBelow: true));
    }
}

public sealed class RmsVsFocusAnalysis : BaseAnalysis
{
    private readonly int _focusDensity;
    private readonly double _minimumFocus;
    private readonly double _maximumFocus;
    private readonly int _numRings;
    private readonly string _distribution;
    private readonly int _wavelengthNumber;
    private readonly string _reference;

    public RmsVsFocusAnalysis(
        Optic optic,
        int focusDensity = 21,
        double minimumFocus = -1,
        double maximumFocus = 1,
        int numRings = 6,
        string distribution = "hexapolar",
        int wavelengthNumber = 0,
        string reference = "centroid") : base(optic)
    {
        _focusDensity = Math.Clamp(focusDensity, 2, 100);
        _minimumFocus = Math.Min(minimumFocus, maximumFocus);
        _maximumFocus = Math.Max(minimumFocus, maximumFocus);
        _numRings = Math.Clamp(numRings, 1, 32);
        _distribution = distribution;
        _wavelengthNumber = Math.Max(0, wavelengthNumber);
        _reference = reference;
    }

    public override string Name => "RMS vs Focus";

    public override AnalysisData GenerateData()
    {
        var fields = AnalysisTrace.DefinedFieldSamples(Optic);
        var wavelengths = RmsScanSupport.SelectedWavelengths(Optic, _wavelengthNumber);
        if (fields.Count == 0 || wavelengths.Count == 0)
        {
            return RmsScanSupport.Empty(Name);
        }

        var focusValues = Enumerable.Range(0, _focusDensity)
            .Select(index => _minimumFocus
                + ((_maximumFocus - _minimumFocus) * index / (_focusDensity - 1.0)))
            .ToArray();
        var series = fields.Select((field, fieldIndex) => new AnalysisSeries(
            "Focus Shift (mm)",
            "RMS Spot Radius (mm)",
            focusValues.Select(focus => new AnalysisPoint(
                focus,
                RmsScanSupport.SpotRadius(
                    Optic,
                    (field.Hx, field.Hy),
                    wavelengths,
                    _numRings,
                    _distribution,
                    _reference,
                    focus))).ToArray(),
            Name: field.Label,
            ColorIndex: fieldIndex)).ToArray();

        return new AnalysisData(
            Name,
            new Dictionary<string, object>
            {
                ["FocusDensity"] = _focusDensity,
                ["MinimumFocus"] = _minimumFocus,
                ["MaximumFocus"] = _maximumFocus,
                ["RayDensity"] = _numRings,
                ["Distribution"] = _distribution,
                ["WavelengthNumber"] = _wavelengthNumber,
                ["Reference"] = _reference
            },
            series.FirstOrDefault(),
            series,
            new AnalysisPlotOptions(
                Title: "RMS vs. Focus",
                XMinimum: _minimumFocus,
                XMaximum: _maximumFocus,
                YMinimum: 0,
                ShowLegend: true,
                HideTopAndRightAxes: true,
                GridOpacity: 0.25,
                LegendBelow: true));
    }
}

public sealed class RmsFieldMapAnalysis : BaseAnalysis
{
    private readonly int _xFieldSamples;
    private readonly int _yFieldSamples;
    private readonly double _xFieldWidth;
    private readonly double _yFieldWidth;
    private readonly int _numRings;
    private readonly string _distribution;
    private readonly int _wavelengthNumber;
    private readonly string _reference;

    public RmsFieldMapAnalysis(
        Optic optic,
        int xFieldSamples = 11,
        int yFieldSamples = 11,
        double xFieldWidth = 0,
        double yFieldWidth = 0,
        int numRings = 6,
        string distribution = "hexapolar",
        int wavelengthNumber = 0,
        string reference = "centroid") : base(optic)
    {
        var defaultWidth = Math.Max(1e-9, AnalysisTrace.MaxFieldValue(optic));
        _xFieldSamples = Math.Clamp(xFieldSamples, 3, 101);
        _yFieldSamples = Math.Clamp(yFieldSamples, 3, 101);
        _xFieldWidth = xFieldWidth > 0 ? xFieldWidth : defaultWidth;
        _yFieldWidth = yFieldWidth > 0 ? yFieldWidth : defaultWidth;
        _numRings = Math.Clamp(numRings, 1, 32);
        _distribution = distribution;
        _wavelengthNumber = Math.Max(0, wavelengthNumber);
        _reference = reference;
    }

    public override string Name => "RMS Field Map";

    public override AnalysisData GenerateData()
    {
        var wavelengths = RmsScanSupport.SelectedWavelengths(Optic, _wavelengthNumber);
        var maximumField = Math.Max(1e-12, AnalysisTrace.MaxFieldValue(Optic));
        if (wavelengths.Count == 0)
        {
            return RmsScanSupport.Empty(Name);
        }

        var points = new List<AnalysisPoint>(_xFieldSamples * _yFieldSamples);
        for (var row = 0; row < _yFieldSamples; row++)
        {
            var y = -_yFieldWidth + (2 * _yFieldWidth * row / (_yFieldSamples - 1.0));
            for (var column = 0; column < _xFieldSamples; column++)
            {
                var x = -_xFieldWidth + (2 * _xFieldWidth * column / (_xFieldSamples - 1.0));
                var rms = RmsScanSupport.SpotRadius(
                    Optic,
                    (x / maximumField, y / maximumField),
                    wavelengths,
                    _numRings,
                    _distribution,
                    _reference);
                points.Add(new AnalysisPoint(x, y, Value: rms));
            }
        }

        var values = points.Select(point => point.Value ?? 0).ToArray();
        var series = new AnalysisSeries(
            RmsScanSupport.FieldXAxisLabel(Optic),
            RmsScanSupport.FieldYAxisLabel(Optic),
            points,
            AnalysisSeriesKind.Heatmap,
            Name: "RMS Spot Radius",
            ValueLabel: "RMS Spot Radius (mm)",
            ValueMinimum: values.DefaultIfEmpty(0).Min(),
            ValueMaximum: values.DefaultIfEmpty(0).Max());
        return new AnalysisData(
            Name,
            new Dictionary<string, object>
            {
                ["XFieldSamples"] = _xFieldSamples,
                ["YFieldSamples"] = _yFieldSamples,
                ["XFieldWidth"] = _xFieldWidth,
                ["YFieldWidth"] = _yFieldWidth,
                ["RayDensity"] = _numRings,
                ["Distribution"] = _distribution,
                ["WavelengthNumber"] = _wavelengthNumber,
                ["Reference"] = _reference,
                ["MinimumRmsSpotRadius"] = values.DefaultIfEmpty(0).Min(),
                ["MaximumRmsSpotRadius"] = values.DefaultIfEmpty(0).Max()
            },
            series,
            new[] { series },
            new AnalysisPlotOptions(
                Title: "RMS Field Map",
                EqualAspect: true,
                XMinimum: -_xFieldWidth,
                XMaximum: _xFieldWidth,
                YMinimum: -_yFieldWidth,
                YMaximum: _yFieldWidth,
                HideTopAndRightAxes: true));
    }
}

internal static class RmsScanSupport
{
    public static AnalysisData Empty(string name)
    {
        return new AnalysisData(name, new Dictionary<string, object> { ["Status"] = "No optical data" });
    }

    public static IReadOnlyList<AnalysisFieldSample> SelectedFields(Optic optic, int fieldNumber)
    {
        var fields = AnalysisTrace.DefinedFieldSamples(optic);
        return fieldNumber > 0
            ? fields.Skip(fieldNumber - 1).Take(1).ToArray()
            : fields;
    }

    public static IReadOnlyList<Wavelength> SelectedWavelengths(Optic optic, int wavelengthNumber)
    {
        var wavelengths = optic.Wavelengths.ToArray();
        return wavelengthNumber > 0
            ? wavelengths.Skip(wavelengthNumber - 1).Take(1).ToArray()
            : wavelengths;
    }

    public static double SpotRadius(
        Optic optic,
        (double Hx, double Hy) field,
        IReadOnlyList<Wavelength> wavelengths,
        int numRings,
        string distribution,
        string reference,
        double imagePlaneOffset = 0)
    {
        var result = SpotAnalysisEngine.Generate(
            optic,
            new[] { field },
            wavelengths,
            numRings,
            distribution,
            imagePlaneOffset,
            reference: reference);
        var rays = result.Fields.FirstOrDefault()?.Wavelengths
            .SelectMany(wavelength => wavelength.Rays)
            .ToArray() ?? Array.Empty<SpotRayData>();
        return SpotAnalysisEngine.RmsRadius(rays);
    }

    public static string FieldXAxisLabel(Optic optic)
    {
        return optic.FieldDefinition == FieldDefinitionKind.Angle
            ? "X Field (degrees)"
            : "X Field (mm)";
    }

    public static string FieldYAxisLabel(Optic optic)
    {
        return optic.FieldDefinition == FieldDefinitionKind.Angle
            ? "Y Field (degrees)"
            : "Y Field (mm)";
    }
}

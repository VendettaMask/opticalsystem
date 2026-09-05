namespace OptilandWorkbench.Core.Analysis;

public sealed class ColorFocusShiftAnalysis : BaseAnalysis
{
    private readonly double _maximumShiftMicrometers;
    private readonly double _pupilZone;
    private readonly int _sampleCount;

    public ColorFocusShiftAnalysis(
        Optic optic,
        double maximumShiftMicrometers = 0,
        double pupilZone = 0,
        int sampleCount = 101) : base(optic)
    {
        _maximumShiftMicrometers = Math.Max(0, maximumShiftMicrometers);
        _pupilZone = Math.Clamp(pupilZone, 0, 1);
        _sampleCount = Math.Max(21, sampleCount);
    }

    public override string Name => "Color Focus Shift";

    public override AnalysisData GenerateData()
    {
        var definedWavelengths = Optic.Wavelengths.ToArray();
        if (definedWavelengths.Length == 0)
        {
            return AnalysisData.Unavailable(Name, "No wavelengths");
        }

        var primary = definedWavelengths.FirstOrDefault(wavelength => wavelength.IsPrimary)
            ?? definedWavelengths[0];
        var minimumWavelength = definedWavelengths.Min(wavelength => wavelength.Micrometers);
        var maximumWavelength = definedWavelengths.Max(wavelength => wavelength.Micrometers);
        if (maximumWavelength - minimumWavelength <= 1e-12)
        {
            minimumWavelength = Math.Max(0.1, primary.Micrometers * 0.9);
            maximumWavelength = primary.Micrometers * 1.1;
        }

        var points = Enumerable.Range(0, _sampleCount)
            .Select(index =>
            {
                var fraction = index / (double)(_sampleCount - 1);
                var wavelength = minimumWavelength
                    + (fraction * (maximumWavelength - minimumWavelength));
                return new AnalysisPoint(FocusShiftMicrometers(wavelength), wavelength);
            })
            .Where(point => double.IsFinite(point.X) && double.IsFinite(point.Y))
            .ToArray();
        var minimumShift = points.Select(point => point.X).DefaultIfEmpty(0).Min();
        var maximumShift = points.Select(point => point.X).DefaultIfEmpty(0).Max();
        var maximumChange = maximumShift - minimumShift;
        var automaticLimit = NiceAxisLimit(
            points.Select(point => Math.Abs(point.X)).DefaultIfEmpty(1).Max());
        var axisLimit = _maximumShiftMicrometers > 0
            ? _maximumShiftMicrometers
            : automaticLimit;
        var diffractionLimit = 2
            * primary.Micrometers
            * Math.Pow(Math.Max(0, Optic.Paraxial.EstimateFNumber()), 2);
        var series = new AnalysisSeries(
            "焦移：µm",
            "波长：µm",
            points,
            Name: "色焦移",
            ColorIndex: 0,
            LineWidth: 1.4,
            XQuantity: AnalysisAxisQuantity.Defocus,
            XUnit: AnalysisAxisUnit.Micrometer,
            YQuantity: AnalysisAxisQuantity.Wavelength,
            YUnit: AnalysisAxisUnit.Micrometer);
        return new AnalysisData(
            Name,
            new Dictionary<string, object>
            {
                ["MaximumFocalShiftChangeMicrometers"] = maximumChange,
                ["DiffractionLimitChangeMicrometers"] = diffractionLimit,
                ["PupilZone"] = _pupilZone,
                ["MaximumShiftMicrometers"] = _maximumShiftMicrometers,
                ["MinimumWavelengthMicrometers"] = minimumWavelength,
                ["MaximumWavelengthMicrometers"] = maximumWavelength,
                ["SampleCount"] = points.Length
            },
            series,
            new[] { series },
            new AnalysisPlotOptions(
                XMinimum: -axisLimit,
                XMaximum: axisLimit,
                YMinimum: minimumWavelength,
                YMaximum: maximumWavelength,
                ShowVerticalZeroLine: true,
                VerticalZeroLineWidth: 1,
                ShowLegend: false,
                HideTopAndRightAxes: true));
    }

    private double FocusShiftMicrometers(double wavelengthMicrometers)
    {
        if (_pupilZone <= 1e-12)
        {
            var trace = Optic.Paraxial.MarginalRay(wavelengthMicrometers);
            if (trace.Heights.Count == 0 || trace.Slopes.Count == 0)
            {
                return 0;
            }

            var height = trace.Heights[^1][0];
            var slope = trace.Slopes[^1][0];
            return Math.Abs(slope) <= 1e-15 ? 0 : (-height / slope) * 1000;
        }

        var positive = TraceZonalRay(_pupilZone, wavelengthMicrometers);
        var negative = TraceZonalRay(-_pupilZone, wavelengthMicrometers);
        var valid = new[] { positive, negative }.Where(double.IsFinite).ToArray();
        return valid.Length == 0 ? 0 : valid.Average();
    }

    private double TraceZonalRay(double pupilY, double wavelengthMicrometers)
    {
        try
        {
            var sample = Optic.TraceGenericFinalSample(0, 0, 0, pupilY, wavelengthMicrometers);
            if (sample is null || sample.Intensity <= 0 || Math.Abs(sample.Direction.Y) <= 1e-15)
            {
                return double.NaN;
            }

            return (-sample.Position.Y * sample.Direction.Z / sample.Direction.Y) * 1000;
        }
        catch (InvalidOperationException)
        {
            return double.NaN;
        }
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

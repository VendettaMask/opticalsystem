namespace OptilandWorkbench.Core.Analysis;

public sealed class AxialAberrationAnalysis : BaseAnalysis
{
    private readonly double _graphScaleMillimeters;
    private readonly int _wavelengthNumber;
    private readonly bool _useDashes;
    private readonly int _sampleCount;

    public AxialAberrationAnalysis(
        Optic optic,
        double graphScaleMillimeters = 0,
        int wavelengthNumber = 0,
        bool useDashes = false,
        int sampleCount = 101) : base(optic)
    {
        _graphScaleMillimeters = Math.Max(0, graphScaleMillimeters);
        _wavelengthNumber = Math.Max(0, wavelengthNumber);
        _useDashes = useDashes;
        _sampleCount = Math.Max(21, sampleCount);
    }

    public override string Name => "Axial Aberration";

    public override AnalysisData GenerateData()
    {
        var allWavelengths = Optic.Wavelengths.OrderBy(wavelength => wavelength.Micrometers).ToArray();
        if (allWavelengths.Length == 0)
        {
            return new AnalysisData(Name, new Dictionary<string, object> { ["Status"] = "No wavelengths" });
        }

        var wavelengths = _wavelengthNumber <= 0
            ? allWavelengths
            : new[] { allWavelengths[Math.Clamp(_wavelengthNumber - 1, 0, allWavelengths.Length - 1)] };
        var series = wavelengths
            .Select((wavelength, index) => new AnalysisSeries(
                "毫米",
                "归一化光瞳坐标",
                Enumerable.Range(0, _sampleCount)
                    .Select(sampleIndex =>
                    {
                        var pupil = sampleIndex / (double)(_sampleCount - 1);
                        return new AnalysisPoint(
                            LongitudinalFocusShift(pupil, wavelength.Micrometers),
                            pupil);
                    })
                    .Where(point => double.IsFinite(point.X))
                    .ToArray(),
                Name: $"{wavelength.Micrometers:0.0000} µm",
                LineStyle: _useDashes
                    ? (index % 3) switch
                    {
                        1 => AnalysisLineStyle.Dashed,
                        2 => AnalysisLineStyle.Dotted,
                        _ => AnalysisLineStyle.Solid
                    }
                    : AnalysisLineStyle.Solid,
                ColorIndex: index,
                LineWidth: 1.35,
                LegendKey: $"wavelength:{wavelength.Micrometers:R}",
                XQuantity: AnalysisAxisQuantity.Defocus,
                XUnit: AnalysisAxisUnit.Millimeter,
                YQuantity: AnalysisAxisQuantity.PupilCoordinate,
                YUnit: AnalysisAxisUnit.Dimensionless))
            .ToArray();
        var maximumMagnitude = series
            .SelectMany(item => item.Points)
            .Select(point => Math.Abs(point.X))
            .DefaultIfEmpty(0)
            .Max();
        var axisLimit = _graphScaleMillimeters > 0
            ? _graphScaleMillimeters
            : NiceAxisLimit(maximumMagnitude);
        var pupilRadius = Optic.Paraxial.EstimateEntrancePupilDiameter() / 2;
        return new AnalysisData(
            Name,
            new Dictionary<string, object>
            {
                ["PupilRadiusMillimeters"] = pupilRadius,
                ["ShortestWavelengthMicrometers"] = wavelengths.Min(wavelength => wavelength.Micrometers),
                ["LongestWavelengthMicrometers"] = wavelengths.Max(wavelength => wavelength.Micrometers),
                ["WavelengthCount"] = wavelengths.Length,
                ["GraphScaleMillimeters"] = _graphScaleMillimeters,
                ["UseDashes"] = _useDashes
            },
            series.FirstOrDefault(),
            series,
            new AnalysisPlotOptions(
                Title: $"光瞳半径：{pupilRadius:0.0000} 毫米",
                XMinimum: -axisLimit,
                XMaximum: axisLimit,
                YMinimum: 0,
                YMaximum: 1,
                ShowVerticalZeroLine: true,
                VerticalZeroLineWidth: 1,
                ShowLegend: true,
                HideTopAndRightAxes: true,
                LegendBelow: true));
    }

    private double LongitudinalFocusShift(double pupil, double wavelengthMicrometers)
    {
        if (pupil <= 1e-10)
        {
            var paraxial = Optic.Paraxial.MarginalRay(wavelengthMicrometers);
            if (paraxial.Heights.Count == 0 || paraxial.Slopes.Count == 0)
            {
                return 0;
            }

            var height = paraxial.Heights[^1][0];
            var slope = paraxial.Slopes[^1][0];
            return Math.Abs(slope) <= 1e-15 ? 0 : -height / slope;
        }

        try
        {
            var sample = Optic.TraceGenericFinalSample(0, 0, 0, pupil, wavelengthMicrometers);
            if (sample is null || sample.Intensity <= 0 || Math.Abs(sample.Direction.Y) <= 1e-15)
            {
                return double.NaN;
            }

            return -sample.Position.Y * sample.Direction.Z / sample.Direction.Y;
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
            return 0.001;
        }

        var target = maximum * 1.08;
        var exponent = Math.Floor(Math.Log10(target));
        var scale = Math.Pow(10, exponent);
        var normalized = target / scale;
        var nice = normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10;
        return nice * scale;
    }
}

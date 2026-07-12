using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Raytrace;

namespace OptilandWorkbench.Core.Analysis;

public sealed class BestFitRayFanAnalysis : BaseAnalysis
{
    private readonly int _numPoints;
    private readonly int _numRingsForFit;

    public BestFitRayFanAnalysis(Optic optic, int numPoints = 256, int numRingsForFit = 15) : base(optic)
    {
        _numPoints = Math.Max(3, numPoints % 2 == 0 ? numPoints + 1 : numPoints);
        _numRingsForFit = Math.Max(1, numRingsForFit);
    }

    public override string Name => "Best Fit Ray Fan";

    public override AnalysisData GenerateData()
    {
        var fields = SpotAnalysisEngine.DefinedFields(Optic);
        var wavelengths = Optic.Wavelengths.ToArray();
        var primary = wavelengths.FirstOrDefault(item => item.IsPrimary) ?? wavelengths.FirstOrDefault();
        if (primary is null || fields.Count == 0)
        {
            return new AnalysisData(Name, new Dictionary<string, object> { ["Status"] = "No fields or wavelengths" });
        }

        var pupil = Enumerable.Range(0, _numPoints)
            .Select(index => -1 + (2.0 * index / (_numPoints - 1.0)))
            .ToArray();
        var fieldFans = new List<FieldFan>(fields.Count);
        foreach (var field in fields)
        {
            var sphere = BestFitSphereEngine.Calculate(Optic, field, primary, _numRingsForFit);
            var waves = new List<WaveFan>(wavelengths.Length);
            foreach (var wavelength in wavelengths)
            {
                var x = TraceFan(Optic, field, wavelength, pupil, xFan: true)
                    .Select(sample => sample with { Value = sample.Value - sphere.CenterX })
                    .ToArray();
                var y = TraceFan(Optic, field, wavelength, pupil, xFan: false)
                    .Select(sample => sample with { Value = sample.Value - sphere.CenterY })
                    .ToArray();
                waves.Add(new WaveFan(wavelength, x, y));
            }

            fieldFans.Add(new FieldFan(field.Hx, field.Hy, sphere, waves));
        }

        var finite = fieldFans.SelectMany(field => field.Waves)
            .SelectMany(wave => wave.X.Concat(wave.Y))
            .Where(sample => sample.Intensity > 0 && double.IsFinite(sample.Value))
            .Select(sample => sample.Value)
            .ToArray();
        var yMinimum = finite.DefaultIfEmpty(-1).Min();
        var yMaximum = finite.DefaultIfEmpty(1).Max();
        ExpandRange(ref yMinimum, ref yMaximum);

        var panes = new List<AnalysisPlotPane>(fields.Count * 2);
        foreach (var field in fieldFans)
        {
            var title = $"Hx: {field.Hx:0.000}, Hy: {field.Hy:0.000}";
            panes.Add(new AnalysisPlotPane(title, BuildSeries(field.Waves, pupil, yFan: true), new AnalysisPlotOptions(
                Title: title,
                ShowVerticalZeroLine: true,
                ShowHorizontalZeroLine: true,
                XMinimum: -1,
                XMaximum: 1,
                YMinimum: yMinimum,
                YMaximum: yMaximum)));
            panes.Add(new AnalysisPlotPane(title, BuildSeries(field.Waves, pupil, yFan: false), new AnalysisPlotOptions(
                Title: title,
                ShowVerticalZeroLine: true,
                ShowHorizontalZeroLine: true,
                XMinimum: -1,
                XMaximum: 1,
                YMinimum: yMinimum,
                YMaximum: yMaximum)));
        }

        var firstSeries = panes.FirstOrDefault()?.Series.FirstOrDefault();
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["Samples"] = _numPoints,
            ["FitRings"] = _numRingsForFit,
            ["FieldCount"] = fields.Count,
            ["WavelengthCount"] = wavelengths.Length,
            ["ReferenceCenters"] = string.Join("; ", fieldFans.Select(field =>
                $"({field.Sphere.CenterX:R}, {field.Sphere.CenterY:R}, {field.Sphere.CenterZ:R})")),
            ["MinimumRayAberration"] = finite.DefaultIfEmpty(0).Min(),
            ["MaximumRayAberration"] = finite.DefaultIfEmpty(0).Max()
        }, firstSeries, firstSeries is null ? null : new[] { firstSeries }, PlotPanes: panes, PlotPaneColumns: 2);
    }

    private static IReadOnlyList<AnalysisSeries> BuildSeries(
        IReadOnlyList<WaveFan> waves,
        IReadOnlyList<double> pupil,
        bool yFan)
    {
        return waves.Select((wave, wavelengthIndex) =>
        {
            var samples = yFan ? wave.Y : wave.X;
            return new AnalysisSeries(
                yFan ? "P_y" : "P_x",
                yFan ? "epsilon_y (mm)" : "epsilon_x (mm)",
                samples.Select((sample, index) => new AnalysisPoint(
                    pupil[index],
                    sample.Intensity > 0 ? sample.Value : double.NaN)).ToArray(),
                Name: $"{wave.Wavelength.Micrometers:0.0000} \u00B5m",
                ColorIndex: wavelengthIndex);
        }).ToArray();
    }

    private static IReadOnlyList<FanSample> TraceFan(
        Optic optic,
        (double Hx, double Hy) field,
        Wavelength wavelength,
        IReadOnlyList<double> pupil,
        bool xFan)
    {
        var samples = pupil.Select(value => new PupilSample(xFan ? value : 0, xFan ? 0 : value, 1));
        var bundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(
            field.Hx,
            field.Hy,
            wavelength.Micrometers,
            samples);
        return optic.SequentialRayTracer.Trace(bundle).RayHistories.Select(history =>
        {
            if (history.Count == 0)
            {
                return new FanSample(double.NaN, 0);
            }

            var sample = history[^1];
            return new FanSample(xFan ? sample.Position.X : sample.Position.Y, sample.Intensity);
        }).ToArray();
    }

    private static void ExpandRange(ref double minimum, ref double maximum)
    {
        if (Math.Abs(maximum - minimum) < 1e-12)
        {
            minimum -= 1;
            maximum += 1;
            return;
        }

        var padding = (maximum - minimum) * 0.05;
        minimum -= padding;
        maximum += padding;
    }

    private sealed record FanSample(double Value, double Intensity);

    private sealed record WaveFan(Wavelength Wavelength, IReadOnlyList<FanSample> X, IReadOnlyList<FanSample> Y);

    private sealed record FieldFan(
        double Hx,
        double Hy,
        BestFitSphereResult Sphere,
        IReadOnlyList<WaveFan> Waves);
}

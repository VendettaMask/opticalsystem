using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Core.Analysis;

public sealed class OpticalPathDifferenceAnalysis : BaseAnalysis
{
    private readonly double _graphScaleWaves;
    private readonly int _numberOfRaysEachSide;
    private readonly bool _useDashes;
    private readonly bool _vignettedPupil;
    private readonly bool _checkApertures;
    private readonly int _wavelengthNumber;
    private readonly int _fieldNumber;
    private readonly int _surfaceNumber;

    public OpticalPathDifferenceAnalysis(
        Optic optic,
        double graphScaleWaves = 0,
        int numberOfRaysEachSide = 20,
        bool useDashes = false,
        bool vignettedPupil = true,
        bool checkApertures = true,
        int wavelengthNumber = 0,
        int fieldNumber = 0,
        int surfaceNumber = -1) : base(optic)
    {
        _graphScaleWaves = double.IsFinite(graphScaleWaves) ? Math.Max(0, graphScaleWaves) : 0;
        _numberOfRaysEachSide = Math.Clamp(numberOfRaysEachSide, 1, 4096);
        _useDashes = useDashes;
        _vignettedPupil = vignettedPupil;
        _checkApertures = checkApertures;
        _wavelengthNumber = Math.Max(0, wavelengthNumber);
        _fieldNumber = Math.Max(0, fieldNumber);
        _surfaceNumber = surfaceNumber;
    }

    public override string Name => "Optical Path Difference";

    public override AnalysisData GenerateData()
    {
        var analysisOptic = ResolveAnalysisOptic();
        var allFields = SpotAnalysisEngine.DefinedFields(analysisOptic);
        var fieldIndices = _fieldNumber <= 0
            ? Enumerable.Range(0, allFields.Count).ToArray()
            : new[] { Math.Clamp(_fieldNumber - 1, 0, Math.Max(0, allFields.Count - 1)) };
        var fields = fieldIndices.Select(index => allFields[index]).ToArray();
        var allWavelengths = analysisOptic.Wavelengths.ToArray();
        var wavelengthIndices = _wavelengthNumber <= 0
            ? Enumerable.Range(0, allWavelengths.Length).ToArray()
            : new[] { Math.Clamp(_wavelengthNumber - 1, 0, Math.Max(0, allWavelengths.Length - 1)) };
        var wavelengths = wavelengthIndices.Select(index => allWavelengths[index]).ToArray();
        var pupil = Enumerable.Range(0, (_numberOfRaysEachSide * 2) + 1)
            .Select(index => -1 + (2.0 * index / (_numberOfRaysEachSide * 2.0)))
            .ToArray();
        var fieldFans = new List<(double Hx, double Hy, List<OpdWave> Waves)>();

        foreach (var field in fields)
        {
            var waves = new List<OpdWave>(wavelengths.Length);
            for (var wavelengthIndex = 0; wavelengthIndex < wavelengths.Length; wavelengthIndex++)
            {
                var samples = pupil.Select(value => (X: 0.0, Y: value))
                    .Concat(pupil.Select(value => (X: value, Y: 0.0)))
                    .ToArray();
                var wavefront = WavefrontEngine.GenerateChiefRaySamples(
                    analysisOptic,
                    field,
                    wavelengths[wavelengthIndex],
                    samples,
                    aimAtStop: _vignettedPupil);
                waves.Add(new OpdWave(
                    wavelengths[wavelengthIndex],
                    wavelengthIndices[wavelengthIndex],
                    wavefront.Samples.Take(pupil.Length).ToArray(),
                    wavefront.Samples.Skip(pupil.Length).ToArray()));
            }

            fieldFans.Add((field.Hx, field.Hy, waves));
        }

        var finiteValues = fieldFans
            .SelectMany(field => field.Waves)
            .SelectMany(wave => wave.Y.Concat(wave.X))
            .Where(sample => sample.Intensity > 0 && double.IsFinite(sample.OpdWaves))
            .Select(sample => sample.OpdWaves)
            .ToArray();
        var scale = _graphScaleWaves > 0
            ? _graphScaleWaves
            : NiceScale(finiteValues.Select(Math.Abs).DefaultIfEmpty(0).Max());
        var panes = new List<AnalysisPlotPane>(fields.Length * 2);
        foreach (var field in fieldFans)
        {
            var title = MtfPresentation.FieldName(analysisOptic, (field.Hx, field.Hy));
            panes.Add(BuildPane(title, field.Waves, pupil, yFan: true, scale));
            panes.Add(BuildPane(title, field.Waves, pupil, yFan: false, scale));
        }

        var firstSeries = panes.FirstOrDefault()?.Series.FirstOrDefault();
        var targetSurface = _surfaceNumber < 0
            ? analysisOptic.SurfaceGroup.Items.LastOrDefault()?.Number ?? 0
            : _surfaceNumber;
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["GraphScaleWaves"] = scale,
            ["NumberOfRaysEachSide"] = _numberOfRaysEachSide,
            ["SampleCount"] = (_numberOfRaysEachSide * 2) + 1,
            ["FieldCount"] = fields.Length,
            ["WavelengthCount"] = wavelengths.Length,
            ["WavelengthNumber"] = _wavelengthNumber,
            ["FieldNumber"] = _fieldNumber,
            ["SurfaceNumber"] = targetSurface,
            ["UseDashes"] = _useDashes,
            ["VignettedPupil"] = _vignettedPupil,
            ["CheckApertures"] = _checkApertures,
            ["MinimumOpticalPathDifferenceWaves"] = finiteValues.DefaultIfEmpty(0).Min(),
            ["MaximumOpticalPathDifferenceWaves"] = finiteValues.DefaultIfEmpty(0).Max()
        }, firstSeries, firstSeries is null ? null : new[] { firstSeries },
            PlotPanes: panes,
            PlotPaneColumns: 2);
    }

    private AnalysisPlotPane BuildPane(
        string title,
        IReadOnlyList<OpdWave> waves,
        IReadOnlyList<double> pupil,
        bool yFan,
        double scale)
    {
        var series = waves.Select(wave =>
        {
            var samples = yFan ? wave.Y : wave.X;
            return new AnalysisSeries(
                yFan ? "P_y" : "P_x",
                "W (waves)",
                samples.Select((sample, index) => new AnalysisPoint(
                    pupil[index],
                    sample.Intensity > 0 ? sample.OpdWaves : double.NaN)).ToArray(),
                Name: $"{wave.Wavelength.Micrometers:0.0000} \u00B5m",
                LineStyle: _useDashes
                    ? (wave.WavelengthIndex % 3) switch
                    {
                        1 => AnalysisLineStyle.Dashed,
                        2 => AnalysisLineStyle.Dotted,
                        _ => AnalysisLineStyle.Solid
                    }
                    : AnalysisLineStyle.Solid,
                ColorIndex: wave.WavelengthIndex);
        }).ToArray();
        return new AnalysisPlotPane(title, series, new AnalysisPlotOptions(
            Title: title,
            ShowVerticalZeroLine: true,
            ShowHorizontalZeroLine: true,
            XMinimum: -1,
            XMaximum: 1,
            YMinimum: -scale,
            YMaximum: scale,
            HideTickLabels: true));
    }

    private Optic ResolveAnalysisOptic()
    {
        if (_checkApertures)
        {
            return Optic;
        }

        var clone = Optic.FromSnapshot(Optic.ToSnapshot());
        foreach (var surface in clone.SurfaceGroup.Items)
        {
            surface.PhysicalAperture = null;
        }

        return clone;
    }

    private static double NiceScale(double maximum)
    {
        if (!double.IsFinite(maximum) || maximum <= 1e-12)
        {
            return 0.5;
        }

        var exponent = Math.Floor(Math.Log10(maximum));
        var unit = Math.Pow(10, exponent);
        var normalized = maximum / unit;
        var rounded = normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10;
        return rounded * unit;
    }

    private sealed record OpdWave(
        Wavelength Wavelength,
        int WavelengthIndex,
        IReadOnlyList<WavefrontSample> Y,
        IReadOnlyList<WavefrontSample> X);
}

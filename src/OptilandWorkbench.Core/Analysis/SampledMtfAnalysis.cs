using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Core.Analysis;

public sealed class SampledMtfAnalysis : BaseAnalysis
{
    private readonly int _pupilSampling;
    private readonly int _zernikeTerms;
    private readonly int _numPoints;
    private readonly double? _maximumFrequency;

    public SampledMtfAnalysis(
        Optic optic,
        int pupilSampling = 128,
        int zernikeTerms = 37,
        int numPoints = 256,
        double? maximumFrequency = null) : base(optic)
    {
        _pupilSampling = Math.Max(8, pupilSampling);
        _zernikeTerms = Math.Max(1, zernikeTerms);
        _numPoints = Math.Max(2, numPoints);
        _maximumFrequency = maximumFrequency;
    }

    public override string Name => "Sampled MTF";

    public override AnalysisData GenerateData()
    {
        var wavelength = Optic.Wavelengths.FirstOrDefault(item => item.IsPrimary)
            ?? Optic.Wavelengths.FirstOrDefault();
        if (wavelength is null)
        {
            return new AnalysisData(Name, new Dictionary<string, object> { ["Status"] = "No wavelengths" });
        }

        var fNumber = Math.Abs(Optic.Paraxial.EstimateFNumber());
        var cutoff = _maximumFrequency
            ?? (fNumber <= 1e-30 ? 0 : 1 / (wavelength.Micrometers * 1e-3 * fNumber));
        var frequency = Enumerable.Range(0, _numPoints)
            .Select(index => cutoff * index / (_numPoints - 1.0))
            .ToArray();
        var fields = SpotAnalysisEngine.DefinedFields(Optic);
        var series = new List<AnalysisSeries>(fields.Count * 2);
        for (var fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
        {
            var field = fields[fieldIndex];
            var evaluator = SampledMtfEngine.Create(Optic, field, wavelength, _pupilSampling, _zernikeTerms);
            var tangential = frequency.Select(value => evaluator.Calculate(0, value)).ToArray();
            var sagittal = frequency.Select(value => evaluator.Calculate(value, 0)).ToArray();
            series.Add(new AnalysisSeries(
                "Frequency (cycles/mm)",
                "Modulation",
                frequency.Select((value, index) => new AnalysisPoint(value, tangential[index])).ToArray(),
                Name: MtfPresentation.SeriesName(Optic, field, "Tangential"),
                ColorIndex: fieldIndex));
            series.Add(new AnalysisSeries(
                "Frequency (cycles/mm)",
                "Modulation",
                frequency.Select((value, index) => new AnalysisPoint(value, sagittal[index])).ToArray(),
                Name: MtfPresentation.SeriesName(Optic, field, "Sagittal"),
                LineStyle: AnalysisLineStyle.Dashed,
                ColorIndex: fieldIndex));
        }

        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["Method"] = "Sampled",
            ["PupilSampling"] = _pupilSampling,
            ["ZernikeTerms"] = _zernikeTerms,
            ["PlotPointCount"] = _numPoints,
            ["CutoffFrequency"] = cutoff,
            ["FNumber"] = fNumber,
            ["WavelengthMicrometers"] = wavelength.Micrometers,
            ["FieldCount"] = fields.Count
        }, series.FirstOrDefault(), series, new AnalysisPlotOptions(
            XMinimum: 0,
            XMaximum: cutoff,
            YMinimum: 0,
            YMaximum: 1,
            ShowLegend: true,
            GridOpacity: 0.25));
    }
}

public sealed class ContrastLossMapAnalysis : BaseAnalysis
{
    private readonly int _sampling;
    private readonly double _frequency;
    private readonly bool _normalize;
    private readonly int _wavelengthNumber;
    private readonly int _fieldNumber;
    private readonly bool _showOpd;

    public ContrastLossMapAnalysis(
        Optic optic,
        int sampling = 32,
        double frequency = 0,
        bool normalize = false,
        int wavelengthNumber = 1,
        int fieldNumber = 1,
        bool showOpd = false) : base(optic)
    {
        _sampling = Math.Clamp(sampling, 8, 512);
        _frequency = Math.Max(0, frequency);
        _normalize = normalize;
        _wavelengthNumber = Math.Max(0, wavelengthNumber);
        _fieldNumber = Math.Max(1, fieldNumber);
        _showOpd = showOpd;
    }

    public override string Name => "Contrast Loss Map";

    public override AnalysisData GenerateData()
    {
        var wavelengths = Optic.Wavelengths.ToArray();
        var fields = SpotAnalysisEngine.DefinedFields(Optic);
        if (wavelengths.Length == 0 || fields.Count == 0)
        {
            return new AnalysisData(Name, new Dictionary<string, object> { ["Status"] = "No optical data" });
        }

        var wavelength = _wavelengthNumber > 0
            ? wavelengths[Math.Clamp(_wavelengthNumber - 1, 0, wavelengths.Length - 1)]
            : wavelengths.FirstOrDefault(item => item.IsPrimary) ?? wavelengths[0];
        var field = fields[Math.Clamp(_fieldNumber - 1, 0, fields.Count - 1)];
        var fNumber = DiffractionEngine.WorkingFNumber(Optic, field, wavelength);
        var cutoff = fNumber <= 1e-30
            ? 0
            : 1 / (wavelength.Micrometers * 1e-3 * fNumber);
        var frequency = _frequency > 0
            ? _frequency
            : 0.05 * cutoff;
        // The normalized pupil spans one full diameter from -1 to +1. In the
        // Moore-Elliott autocorrelation, cutoff is reached when the two pupil
        // samples are separated by that full diameter, hence the factor of 2.
        var pupilSeparation = cutoff <= 1e-30
            ? 0
            : Math.Clamp(2 * frequency / cutoff, 0, 1.999);

        var sagittal = BuildMap(field, wavelength, pupilSeparation, xShift: true);
        var tangential = BuildMap(field, wavelength, pupilSeparation, xShift: false);
        var maximumRawLoss = sagittal.Losses.Concat(tangential.Losses)
            .Where(double.IsFinite)
            .DefaultIfEmpty(0)
            .Max();
        if (_normalize && maximumRawLoss > 1e-30)
        {
            sagittal = Normalize(sagittal, maximumRawLoss);
            tangential = Normalize(tangential, maximumRawLoss);
        }

        var panes = new List<AnalysisPlotPane>
        {
            MapPane("Sagittal Contrast Loss", sagittal.LossSeries),
            MapPane("Tangential Contrast Loss", tangential.LossSeries)
        };
        if (_showOpd)
        {
            panes.Add(MapPane("Sagittal OPD Phase", sagittal.OpdSeries));
            panes.Add(MapPane("Tangential OPD Phase", tangential.OpdSeries));
        }

        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["Method"] = "Moore-Elliott",
            ["Sampling"] = _sampling,
            ["Frequency"] = frequency,
            ["RequestedFrequency"] = _frequency,
            ["CutoffFrequency"] = cutoff,
            ["PupilSeparation"] = pupilSeparation,
            ["Normalize"] = _normalize,
            ["WavelengthNumber"] = Array.IndexOf(wavelengths, wavelength) + 1,
            ["WavelengthMicrometers"] = wavelength.Micrometers,
            ["FieldNumber"] = _fieldNumber,
            ["FieldHx"] = field.Hx,
            ["FieldHy"] = field.Hy,
            ["ShowOPD"] = _showOpd,
            ["MaximumContrastLoss"] = maximumRawLoss,
            ["ValidSampleCount"] = sagittal.ValidCount + tangential.ValidCount
        }, sagittal.LossSeries, new[] { sagittal.LossSeries, tangential.LossSeries }, PlotPanes: panes, PlotPaneColumns: 2);
    }

    private ContrastLossMap BuildMap(
        (double Hx, double Hy) field,
        Wavelength wavelength,
        double pupilSeparation,
        bool xShift)
    {
        var lossPoints = new List<AnalysisPoint>(_sampling * _sampling);
        var opdPoints = new List<AnalysisPoint>(_sampling * _sampling);
        var losses = new List<double>(_sampling * _sampling);
        var validCount = 0;
        for (var row = 0; row < _sampling; row++)
        {
            var py = -1 + (2.0 * row / (_sampling - 1.0));
            for (var column = 0; column < _sampling; column++)
            {
                var px = -1 + (2.0 * column / (_sampling - 1.0));
                var result = ContrastLossAt(field, wavelength, px, py, pupilSeparation, xShift);
                if (double.IsFinite(result.Loss))
                {
                    validCount++;
                    losses.Add(result.Loss);
                }

                lossPoints.Add(new AnalysisPoint(px, py, Value: result.Loss));
                opdPoints.Add(new AnalysisPoint(px, py, Value: result.OpdPhase));
            }
        }

        var finiteLosses = losses.Where(double.IsFinite).ToArray();
        var lossSeries = new AnalysisSeries(
            "Px",
            "Py",
            lossPoints,
            AnalysisSeriesKind.Heatmap,
            Name: xShift ? "Sagittal" : "Tangential",
            ValueLabel: "Contrast Loss",
            ValueMinimum: 0,
            ValueMaximum: Math.Max(1e-12, finiteLosses.DefaultIfEmpty(0).Max()));
        var opdSeries = new AnalysisSeries(
            "Px",
            "Py",
            opdPoints,
            AnalysisSeriesKind.Heatmap,
            Name: xShift ? "Sagittal OPD" : "Tangential OPD",
            ValueLabel: "OPD Phase (waves modulo 1)",
            ValueMinimum: 0,
            ValueMaximum: 1);
        return new ContrastLossMap(lossSeries, opdSeries, finiteLosses, validCount);
    }

    private (double Loss, double OpdPhase) ContrastLossAt(
        (double Hx, double Hy) field,
        Wavelength wavelength,
        double px,
        double py,
        double pupilSeparation,
        bool xShift)
    {
        if ((px * px) + (py * py) > 1)
        {
            return (double.NaN, double.NaN);
        }

        var half = pupilSeparation / 2.0;
        var first = xShift ? (X: px - half, Y: py) : (X: px, Y: py - half);
        var second = xShift ? (X: px + half, Y: py) : (X: px, Y: py + half);
        if (((first.X * first.X) + (first.Y * first.Y) > 1)
            || ((second.X * second.X) + (second.Y * second.Y) > 1))
        {
            return (double.NaN, double.NaN);
        }

        try
        {
            var wavefront = WavefrontEngine.GenerateChiefRaySamples(
                Optic,
                field,
                wavelength,
                new[] { first, second });
            if (wavefront.Samples.Count < 2
                || wavefront.Samples[0].Intensity <= 0
                || wavefront.Samples[1].Intensity <= 0)
            {
                return (double.NaN, double.NaN);
            }

            var phaseDifference = 2 * Math.PI * (wavefront.Samples[0].OpdWaves - wavefront.Samples[1].OpdWaves);
            var loss = Math.Clamp(0.5 * (1 - Math.Cos(phaseDifference)), 0, 1);
            var opdPhase = PositiveModulo(0.5 * (wavefront.Samples[0].OpdWaves + wavefront.Samples[1].OpdWaves), 1);
            return (loss, opdPhase);
        }
        catch (InvalidOperationException)
        {
            return (double.NaN, double.NaN);
        }
    }

    private static ContrastLossMap Normalize(ContrastLossMap map, double maximum)
    {
        var normalizedPoints = map.LossSeries.Points
            .Select(point => point with
            {
                Value = point.Value.HasValue && double.IsFinite(point.Value.Value)
                    ? point.Value.Value / maximum
                    : point.Value
            })
            .ToArray();
        var series = map.LossSeries with
        {
            Points = normalizedPoints,
            ValueMaximum = 1
        };
        return map with { LossSeries = series };
    }

    private static AnalysisPlotPane MapPane(string title, AnalysisSeries series)
    {
        return new AnalysisPlotPane(title, new[] { series }, new AnalysisPlotOptions(
            Title: title,
            EqualAspect: true,
            XMinimum: -1,
            XMaximum: 1,
            YMinimum: -1,
            YMaximum: 1,
            HideTopAndRightAxes: true,
            GridOpacity: 0.2));
    }

    private static double PositiveModulo(double value, double period)
    {
        var result = value % period;
        return result < 0 ? result + period : result;
    }

    private sealed record ContrastLossMap(
        AnalysisSeries LossSeries,
        AnalysisSeries OpdSeries,
        IReadOnlyList<double> Losses,
        int ValidCount);
}

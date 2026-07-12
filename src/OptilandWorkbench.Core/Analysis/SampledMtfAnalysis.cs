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
            var tangential = frequency.Select(value => evaluator.Calculate(value, 0)).ToArray();
            var sagittal = frequency.Select(value => evaluator.Calculate(0, value)).ToArray();
            series.Add(new AnalysisSeries(
                "Frequency (cycles/mm)",
                "Modulation",
                frequency.Select((value, index) => new AnalysisPoint(value, tangential[index])).ToArray(),
                Name: $"Hx: {field.Hx:0.0}, Hy: {field.Hy:0.0}, Tangential",
                ColorIndex: fieldIndex));
            series.Add(new AnalysisSeries(
                "Frequency (cycles/mm)",
                "Modulation",
                frequency.Select((value, index) => new AnalysisPoint(value, sagittal[index])).ToArray(),
                Name: $"Hx: {field.Hx:0.0}, Hy: {field.Hy:0.0}, Sagittal",
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

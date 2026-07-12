using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Core.Analysis;

public sealed class GeometricMtfAnalysis : BaseAnalysis
{
    private readonly int _numRays;
    private readonly string _distribution;
    private readonly int _numPoints;
    private readonly double? _maximumFrequency;
    private readonly bool _scale;

    public GeometricMtfAnalysis(
        Optic optic,
        int numRays = 100,
        string distribution = "uniform",
        int numPoints = 256,
        double? maximumFrequency = null,
        bool scale = true) : base(optic)
    {
        _numRays = Math.Max(2, numRays);
        _distribution = distribution;
        _numPoints = Math.Max(2, numPoints);
        _maximumFrequency = maximumFrequency;
        _scale = scale;
    }

    public override string Name => "Geometric MTF";

    public override AnalysisData GenerateData()
    {
        var wavelength = Optic.Wavelengths.FirstOrDefault(item => item.IsPrimary)
            ?? Optic.Wavelengths.FirstOrDefault();
        if (wavelength is null)
        {
            return new AnalysisData(Name, new Dictionary<string, object> { ["Status"] = "No wavelengths" });
        }

        var fields = SpotAnalysisEngine.DefinedFields(Optic);
        var result = SpotAnalysisEngine.Generate(Optic, fields, new[] { wavelength }, _numRays, _distribution);
        var fNumber = Math.Abs(Optic.Paraxial.EstimateFNumber());
        var cutoff = _maximumFrequency
            ?? (fNumber <= 1e-30 ? 0 : 1 / (wavelength.Micrometers * 1e-3 * fNumber));
        var frequency = Enumerable.Range(0, _numPoints)
            .Select(index => cutoff * index / (_numPoints - 1.0))
            .ToArray();
        var diffractionScale = frequency.Select(value =>
        {
            if (!_scale || cutoff <= 1e-30)
            {
                return 1.0;
            }

            var ratio = Math.Clamp(value / cutoff, 0, 1);
            var phi = Math.Acos(ratio);
            return (2 / Math.PI) * (phi - (Math.Cos(phi) * Math.Sin(phi)));
        }).ToArray();

        var series = new List<AnalysisSeries>(result.Fields.Count * 2);
        foreach (var field in result.Fields)
        {
            var rays = field.Wavelengths.Single().Rays;
            var tangential = Compute(rays.Select(ray => ray.Y).ToArray(), frequency, diffractionScale);
            var sagittal = Compute(rays.Select(ray => ray.X).ToArray(), frequency, diffractionScale);
            var colorIndex = series.Count / 2;
            series.Add(new AnalysisSeries(
                "Frequency (cycles/mm)",
                "Modulation",
                frequency.Select((value, index) => new AnalysisPoint(value, tangential[index])).ToArray(),
                Name: $"Hx: {field.Hx:0.0}, Hy: {field.Hy:0.0}, Tangential",
                ColorIndex: colorIndex));
            series.Add(new AnalysisSeries(
                "Frequency (cycles/mm)",
                "Modulation",
                frequency.Select((value, index) => new AnalysisPoint(value, sagittal[index])).ToArray(),
                Name: $"Hx: {field.Hx:0.0}, Hy: {field.Hy:0.0}, Sagittal",
                LineStyle: AnalysisLineStyle.Dashed,
                ColorIndex: colorIndex));
        }

        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["Method"] = "Geometric",
            ["NumRays"] = _numRays,
            ["Distribution"] = _distribution,
            ["PlotPointCount"] = _numPoints,
            ["ScaleByDiffractionLimit"] = _scale,
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

    private static double[] Compute(
        IReadOnlyList<double> coordinates,
        IReadOnlyList<double> frequency,
        IReadOnlyList<double> scale)
    {
        if (coordinates.Count == 0)
        {
            return frequency.Select(_ => 0.0).ToArray();
        }

        var binCount = frequency.Count + 1;
        var minimum = coordinates.Min();
        var maximum = coordinates.Max();
        if (Math.Abs(maximum - minimum) <= 1e-30)
        {
            minimum -= 0.5;
            maximum += 0.5;
        }

        var binWidth = (maximum - minimum) / binCount;
        var counts = new double[binCount];
        foreach (var coordinate in coordinates)
        {
            var index = coordinate == maximum
                ? binCount - 1
                : Math.Clamp((int)Math.Floor((coordinate - minimum) / binWidth), 0, binCount - 1);
            counts[index]++;
        }

        var centers = Enumerable.Range(0, binCount)
            .Select(index => minimum + ((index + 0.5) * binWidth))
            .ToArray();
        var denominator = counts.Sum() * binWidth;
        var output = new double[frequency.Count];
        for (var index = 0; index < frequency.Count; index++)
        {
            var cosine = 0.0;
            var sine = 0.0;
            for (var bin = 0; bin < binCount; bin++)
            {
                var phase = 2 * Math.PI * frequency[index] * centers[bin];
                cosine += counts[bin] * Math.Cos(phase) * binWidth;
                sine += counts[bin] * Math.Sin(phase) * binWidth;
            }

            cosine /= denominator;
            sine /= denominator;
            output[index] = Math.Sqrt((cosine * cosine) + (sine * sine)) * scale[index];
        }

        return output;
    }
}

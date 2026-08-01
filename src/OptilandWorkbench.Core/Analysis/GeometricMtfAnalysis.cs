using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Core.Analysis;

public sealed class GeometricMtfAnalysis : BaseAnalysis
{
    private readonly int _numRays;
    private readonly string _distribution;
    private readonly int _numPoints;
    private readonly double? _maximumFrequency;
    private readonly bool _scale;
    private readonly int _wavelengthNumber;
    private readonly int _fieldNumber;

    public GeometricMtfAnalysis(
        Optic optic,
        int numRays = 100,
        string distribution = "uniform",
        int numPoints = 256,
        double? maximumFrequency = null,
        bool scale = true,
        int wavelengthNumber = -1,
        int fieldNumber = 0) : base(optic)
    {
        _numRays = Math.Max(2, numRays);
        _distribution = distribution;
        _numPoints = Math.Max(2, numPoints);
        _maximumFrequency = maximumFrequency;
        _scale = scale;
        _wavelengthNumber = wavelengthNumber;
        _fieldNumber = Math.Max(0, fieldNumber);
    }

    public override string Name => "Geometric MTF";

    public override AnalysisData GenerateData()
    {
        var wavelengths = MtfMethodEvaluator.SelectWavelengths(Optic, _wavelengthNumber);
        if (wavelengths.Count == 0)
        {
            return new AnalysisData(Name, new Dictionary<string, object> { ["Status"] = "No wavelengths" });
        }

        var allFields = SpotAnalysisEngine.DefinedFields(Optic);
        var fieldIndices = _fieldNumber <= 0
            ? Enumerable.Range(0, allFields.Count).ToArray()
            : new[] { Math.Clamp(_fieldNumber - 1, 0, Math.Max(0, allFields.Count - 1)) };
        var fields = fieldIndices.Select(index => allFields[index]).ToArray();
        var result = SpotAnalysisEngine.Generate(Optic, fields, wavelengths, _numRays, _distribution);
        var fNumber = Math.Abs(Optic.Paraxial.EstimateFNumber());
        var referenceWavelength = wavelengths.FirstOrDefault(item => item.IsPrimary) ?? wavelengths[0];
        var diffractionCutoff = fNumber <= 1e-30
            ? 0
            : 1 / (referenceWavelength.Micrometers * 1e-3 * fNumber);
        var maximumFrequency = _maximumFrequency ?? diffractionCutoff;
        var frequency = Enumerable.Range(0, _numPoints)
            .Select(index => maximumFrequency * index / (_numPoints - 1.0))
            .ToArray();

        var series = new List<AnalysisSeries>(result.Fields.Count * 2);
        for (var fieldIndex = 0; fieldIndex < result.Fields.Count; fieldIndex++)
        {
            var field = result.Fields[fieldIndex];
            var totalWeight = wavelengths.Sum(item => item.Weight);
            var useEqualWeights = totalWeight <= 1e-30;
            if (useEqualWeights)
            {
                totalWeight = wavelengths.Count;
            }

            var tangential = new double[frequency.Length];
            var sagittal = new double[frequency.Length];
            for (var wavelengthIndex = 0; wavelengthIndex < wavelengths.Count; wavelengthIndex++)
            {
                var wavelength = wavelengths[wavelengthIndex];
                var cutoff = fNumber <= 1e-30
                    ? 0
                    : 1 / (wavelength.Micrometers * 1e-3 * fNumber);
                var diffractionScale = frequency.Select(value => DiffractionScale(value, cutoff)).ToArray();
                var rays = field.Wavelengths[wavelengthIndex].Rays;
                var wavelengthTangential = Compute(
                    rays.Select(ray => ray.Y).ToArray(),
                    frequency,
                    diffractionScale);
                var wavelengthSagittal = Compute(
                    rays.Select(ray => ray.X).ToArray(),
                    frequency,
                    diffractionScale);
                var weight = useEqualWeights ? 1.0 : wavelength.Weight;
                for (var index = 0; index < frequency.Length; index++)
                {
                    tangential[index] += wavelengthTangential[index] * weight;
                    sagittal[index] += wavelengthSagittal[index] * weight;
                }
            }

            tangential = tangential.Select(value => Math.Clamp(value / totalWeight, 0, 1)).ToArray();
            sagittal = sagittal.Select(value => Math.Clamp(value / totalWeight, 0, 1)).ToArray();
            var colorIndex = fieldIndices[fieldIndex];
            series.Add(new AnalysisSeries(
                "Frequency (cycles/mm)",
                "Modulation",
                frequency.Select((value, index) => new AnalysisPoint(value, tangential[index])).ToArray(),
                Name: MtfPresentation.SeriesName(Optic, (field.Hx, field.Hy), "Tangential"),
                ColorIndex: colorIndex,
                XQuantity: AnalysisAxisQuantity.SpatialFrequency,
                XUnit: AnalysisAxisUnit.CyclesPerMillimeter,
                YQuantity: AnalysisAxisQuantity.Modulation,
                YUnit: AnalysisAxisUnit.Dimensionless));
            series.Add(new AnalysisSeries(
                "Frequency (cycles/mm)",
                "Modulation",
                frequency.Select((value, index) => new AnalysisPoint(value, sagittal[index])).ToArray(),
                Name: MtfPresentation.SeriesName(Optic, (field.Hx, field.Hy), "Sagittal"),
                LineStyle: AnalysisLineStyle.Dashed,
                ColorIndex: colorIndex,
                XQuantity: AnalysisAxisQuantity.SpatialFrequency,
                XUnit: AnalysisAxisUnit.CyclesPerMillimeter,
                YQuantity: AnalysisAxisQuantity.Modulation,
                YUnit: AnalysisAxisUnit.Dimensionless));
        }

        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["Method"] = "Geometric",
            ["NumRays"] = _numRays,
            ["Distribution"] = _distribution,
            ["PlotPointCount"] = _numPoints,
            ["ScaleByDiffractionLimit"] = _scale,
            ["MaximumFrequency"] = maximumFrequency,
            ["CutoffFrequency"] = diffractionCutoff,
            ["FNumber"] = fNumber,
            ["WavelengthNumber"] = _wavelengthNumber,
            ["WavelengthsMicrometers"] = wavelengths.Select(item => item.Micrometers).ToArray(),
            ["FieldNumber"] = _fieldNumber,
            ["FieldCount"] = fields.Length
        }, series.FirstOrDefault(), series, new AnalysisPlotOptions(
            XMinimum: 0,
            XMaximum: maximumFrequency,
            YMinimum: 0,
            YMaximum: 1,
            ShowLegend: true,
            GridOpacity: 0.25));
    }

    private double DiffractionScale(double frequency, double cutoff)
    {
        if (!_scale || cutoff <= 1e-30)
        {
            return 1.0;
        }

        if (frequency >= cutoff)
        {
            return 0;
        }

        var ratio = Math.Clamp(frequency / cutoff, 0, 1);
        var phi = Math.Acos(ratio);
        return (2 / Math.PI) * (phi - (Math.Cos(phi) * Math.Sin(phi)));
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

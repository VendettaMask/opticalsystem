using System.Numerics;
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
            return AnalysisData.Unavailable(Name, "No wavelengths");
        }

        var allFields = SpotAnalysisEngine.DefinedFields(Optic);
        var fieldIndices = _fieldNumber <= 0
            ? Enumerable.Range(0, allFields.Count).ToArray()
            : new[] { Math.Clamp(_fieldNumber - 1, 0, Math.Max(0, allFields.Count - 1)) };
        var fields = fieldIndices.Select(index => allFields[index]).ToArray();
        var result = SpotAnalysisEngine.Generate(Optic, fields, wavelengths, _numRays, _distribution);
        var fNumber = Math.Abs(Optic.Paraxial.EstimateFNumber());
        var referenceWavelength = wavelengths.FirstOrDefault(item => item.IsPrimary) ?? wavelengths[0];
        var afocalImageSpace = Optic.ImageSpaceAfocal;
        var diffractionCutoff = afocalImageSpace
            ? ImageSpaceAnalysisSupport.AfocalCutoffFrequencyCyclesPerMilliradian(Optic, referenceWavelength)
            : fNumber <= 1e-30
                ? 0
                : 1 / (referenceWavelength.Micrometers * 1e-3 * fNumber);
        var maximumFrequency = _maximumFrequency ?? diffractionCutoff;
        var frequency = Enumerable.Range(0, _numPoints)
            .Select(index => maximumFrequency * index / (_numPoints - 1.0))
            .ToArray();
        var frequencyLabel = ImageSpaceAnalysisSupport.SpatialFrequencyLabel(Optic);
        var frequencyUnit = ImageSpaceAnalysisSupport.SpatialFrequencyUnit(Optic);

        var series = new List<AnalysisSeries>(result.Fields.Count * 2);
        for (var fieldIndex = 0; fieldIndex < result.Fields.Count; fieldIndex++)
        {
            var field = result.Fields[fieldIndex];
            var validWavelengths = wavelengths
                .Select((wavelength, wavelengthIndex) => new
                {
                    Wavelength = wavelength,
                    Rays = field.Wavelengths[wavelengthIndex].Rays
                        .Where(ray => double.IsFinite(ray.X)
                            && double.IsFinite(ray.Y)
                            && double.IsFinite(ray.Intensity)
                            && ray.Intensity > 0)
                        .ToArray()
                })
                .Where(item => item.Rays.Length > 0)
                .ToArray();
            if (validWavelengths.Length == 0)
            {
                throw new AnalysisDataUnavailableException(
                    Name,
                    $"no finite positive-intensity rays reached the image for field {fieldIndex + 1}");
            }

            var totalWeight = validWavelengths.Sum(item => Math.Max(0, item.Wavelength.Weight));
            var useEqualWeights = totalWeight <= 1e-30 || !double.IsFinite(totalWeight);
            if (useEqualWeights)
            {
                totalWeight = validWavelengths.Length;
            }

            var tangentialOtf = new Complex[frequency.Length];
            var sagittalOtf = new Complex[frequency.Length];
            foreach (var item in validWavelengths)
            {
                var wavelength = item.Wavelength;
                var cutoff = afocalImageSpace
                    ? ImageSpaceAnalysisSupport.AfocalCutoffFrequencyCyclesPerMilliradian(Optic, wavelength)
                    : fNumber <= 1e-30
                        ? 0
                        : 1 / (wavelength.Micrometers * 1e-3 * fNumber);
                var diffractionScale = frequency.Select(value => DiffractionScale(value, cutoff)).ToArray();
                var wavelengthTangential = ComputeOtf(
                    item.Rays.Select(ray => ray.Y).ToArray(),
                    item.Rays.Select(ray => ray.Intensity).ToArray(),
                    frequency,
                    diffractionScale);
                var wavelengthSagittal = ComputeOtf(
                    item.Rays.Select(ray => ray.X).ToArray(),
                    item.Rays.Select(ray => ray.Intensity).ToArray(),
                    frequency,
                    diffractionScale);
                var weight = useEqualWeights ? 1.0 : Math.Max(0, wavelength.Weight);
                for (var index = 0; index < frequency.Length; index++)
                {
                    tangentialOtf[index] += wavelengthTangential[index] * weight;
                    sagittalOtf[index] += wavelengthSagittal[index] * weight;
                }
            }

            var tangential = tangentialOtf.Select(value => Math.Clamp(value.Magnitude / totalWeight, 0, 1)).ToArray();
            var sagittal = sagittalOtf.Select(value => Math.Clamp(value.Magnitude / totalWeight, 0, 1)).ToArray();
            var colorIndex = fieldIndices[fieldIndex];
            series.Add(new AnalysisSeries(
                frequencyLabel,
                "Modulation",
                frequency.Select((value, index) => new AnalysisPoint(value, tangential[index])).ToArray(),
                Name: MtfPresentation.SeriesName(Optic, (field.Hx, field.Hy), "Tangential"),
                ColorIndex: colorIndex,
                XQuantity: AnalysisAxisQuantity.SpatialFrequency,
                XUnit: frequencyUnit,
                YQuantity: AnalysisAxisQuantity.Modulation,
                YUnit: AnalysisAxisUnit.Dimensionless));
            series.Add(new AnalysisSeries(
                frequencyLabel,
                "Modulation",
                frequency.Select((value, index) => new AnalysisPoint(value, sagittal[index])).ToArray(),
                Name: MtfPresentation.SeriesName(Optic, (field.Hx, field.Hy), "Sagittal"),
                LineStyle: AnalysisLineStyle.Dashed,
                ColorIndex: colorIndex,
                XQuantity: AnalysisAxisQuantity.SpatialFrequency,
                XUnit: frequencyUnit,
                YQuantity: AnalysisAxisQuantity.Modulation,
                YUnit: AnalysisAxisUnit.Dimensionless));
        }

        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["Method"] = "Geometric",
            ["RayWeighting"] = "Image-plane intensity",
            ["NumRays"] = _numRays,
            ["Distribution"] = _distribution,
            ["PlotPointCount"] = _numPoints,
            ["ScaleByDiffractionLimit"] = _scale,
            ["MaximumFrequency"] = maximumFrequency,
            ["CutoffFrequency"] = diffractionCutoff,
            ["FrequencyUnit"] = ImageSpaceAnalysisSupport.SpatialFrequencyUnitLabel(Optic),
            ["ImageSpaceAfocal"] = afocalImageSpace,
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

    internal static double[] Compute(
        IReadOnlyList<double> coordinates,
        IReadOnlyList<double> intensities,
        IReadOnlyList<double> frequency,
        IReadOnlyList<double> scale) => ComputeOtf(coordinates, intensities, frequency, scale)
            .Select(value => Math.Clamp(value.Magnitude, 0, 1)).ToArray();

    internal static Complex[] ComputeOtf(
        IReadOnlyList<double> coordinates,
        IReadOnlyList<double> intensities,
        IReadOnlyList<double> frequency,
        IReadOnlyList<double> scale)
    {
        if (coordinates.Count != intensities.Count)
        {
            throw new ArgumentException("Coordinate and intensity arrays must have the same length.");
        }

        if (frequency.Count != scale.Count)
        {
            throw new ArgumentException("Frequency and scale arrays must have the same length.");
        }

        if (coordinates.Count == 0)
        {
            throw new AnalysisDataUnavailableException("Geometric MTF", "no valid rays");
        }

        var binCount = frequency.Count + 1;
        var minimum = coordinates.Min();
        var maximum = coordinates.Max();
        if (Math.Abs(maximum - minimum) <= 1e-30)
        {
            if (!(intensities.Sum() > 0))
                throw new AnalysisDataUnavailableException("Geometric MTF", "no positive intensity");
            return frequency.Select((value, index) =>
                Complex.FromPolarCoordinates(scale[index], -2 * Math.PI * value * minimum)).ToArray();
        }

        var binWidth = (maximum - minimum) / binCount;
        var weights = new double[binCount];
        for (var rayIndex = 0; rayIndex < coordinates.Count; rayIndex++)
        {
            var coordinate = coordinates[rayIndex];
            var index = coordinate == maximum
                ? binCount - 1
                : Math.Clamp((int)Math.Floor((coordinate - minimum) / binWidth), 0, binCount - 1);
            weights[index] += intensities[rayIndex];
        }

        var centers = Enumerable.Range(0, binCount)
            .Select(index => minimum + ((index + 0.5) * binWidth))
            .ToArray();
        var denominator = weights.Sum();
        if (!(denominator > 0) || !double.IsFinite(denominator))
        {
            throw new AnalysisDataUnavailableException(
                "Geometric MTF",
                "valid rays have no finite positive intensity");
        }

        var output = new Complex[frequency.Count];
        for (var index = 0; index < frequency.Count; index++)
        {
            var cosine = 0.0;
            var sine = 0.0;
            for (var bin = 0; bin < binCount; bin++)
            {
                var phase = 2 * Math.PI * frequency[index] * centers[bin];
                cosine += weights[bin] * Math.Cos(phase);
                sine += weights[bin] * Math.Sin(phase);
            }

            cosine /= denominator;
            sine /= denominator;
            output[index] = new Complex(cosine, -sine) * scale[index];
        }

        return output;
    }
}

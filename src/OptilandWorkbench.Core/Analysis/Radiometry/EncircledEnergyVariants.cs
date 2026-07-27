using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Core.Analysis;

public sealed class DiffractionEncircledEnergyAnalysis : BaseAnalysis
{
    private readonly int _pupilSampling;
    private readonly int _imageSampling;
    private readonly int _numPoints;
    private readonly int _wavelengthNumber;
    private readonly int _fieldNumber;
    private readonly string _type;
    private readonly string _reference;
    private readonly double _maximumDistanceMicrometers;

    public DiffractionEncircledEnergyAnalysis(
        Optic optic,
        int pupilSampling = 64,
        int imageSampling = 128,
        int numPoints = 256,
        int wavelengthNumber = 0,
        int fieldNumber = 1,
        string type = "encircled",
        string reference = "centroid",
        double maximumDistanceMicrometers = 0) : base(optic)
    {
        _pupilSampling = Math.Clamp(pupilSampling, 8, 512);
        _imageSampling = Math.Clamp(imageSampling, 16, 1024);
        _numPoints = Math.Clamp(numPoints, 2, 2048);
        _wavelengthNumber = Math.Max(0, wavelengthNumber);
        _fieldNumber = Math.Max(1, fieldNumber);
        _type = type;
        _reference = reference;
        _maximumDistanceMicrometers = Math.Max(0, maximumDistanceMicrometers);
    }

    public override string Name => "Diffraction Encircled Energy";

    public override AnalysisData GenerateData()
    {
        var source = new PsfAnalysis(
            Optic,
            _pupilSampling,
            _imageSampling,
            _wavelengthNumber,
            _fieldNumber,
            type: "linear",
            displayAs: "heatmap").GenerateData();
        var heatmap = source.PlotSeries.FirstOrDefault(
            series => series.Kind == AnalysisSeriesKind.Heatmap);
        if (heatmap is null || heatmap.Points.Count == 0)
        {
            return EnergyCurveSupport.Empty(Name);
        }

        var samples = heatmap.Points
            .Where(point => point.Value is > 0 && double.IsFinite(point.Value.Value))
            .Select(point => new EnergySample(point.X, point.Y, point.Value!.Value))
            .ToArray();
        var center = string.Equals(_reference, "centroid", StringComparison.OrdinalIgnoreCase)
            ? EnergyCurveSupport.Centroid(samples)
            : (X: 0.0, Y: 0.0);
        return EnergyCurveSupport.CreateCurve(
            Name,
            samples,
            center,
            _type,
            _maximumDistanceMicrometers,
            _numPoints,
            new Dictionary<string, object>
            {
                ["Method"] = "FFT PSF integration",
                ["PupilSampling"] = _pupilSampling,
                ["ImageSampling"] = _imageSampling,
                ["WavelengthNumber"] = _wavelengthNumber,
                ["FieldNumber"] = _fieldNumber,
                ["Reference"] = _reference
            });
    }
}

public sealed class GeometricLineEdgeSpreadAnalysis : BaseAnalysis
{
    private readonly int _pupilSampling;
    private readonly int _numPoints;
    private readonly int _wavelengthNumber;
    private readonly int _fieldNumber;
    private readonly string _orientation;
    private readonly string _display;
    private readonly double _maximumRadiusMicrometers;

    public GeometricLineEdgeSpreadAnalysis(
        Optic optic,
        int pupilSampling = 32,
        int numPoints = 257,
        int wavelengthNumber = 0,
        int fieldNumber = 1,
        string orientation = "X",
        string display = "line and edge",
        double maximumRadiusMicrometers = 0) : base(optic)
    {
        _pupilSampling = Math.Clamp(pupilSampling, 3, 256);
        _numPoints = Math.Clamp(numPoints, 33, 2049);
        _wavelengthNumber = Math.Max(0, wavelengthNumber);
        _fieldNumber = Math.Max(1, fieldNumber);
        _orientation = orientation;
        _display = display;
        _maximumRadiusMicrometers = Math.Max(0, maximumRadiusMicrometers);
    }

    public override string Name => "Geometric Line Edge Spread";

    public override AnalysisData GenerateData()
    {
        var wavelengths = EnergyCurveSupport.SelectedWavelengths(Optic, _wavelengthNumber);
        var field = EnergyCurveSupport.SelectedField(Optic, _fieldNumber);
        if (wavelengths.Count == 0)
        {
            return EnergyCurveSupport.Empty(Name);
        }

        var result = SpotAnalysisEngine.Generate(
            Optic,
            new[] { field },
            wavelengths,
            _pupilSampling,
            "uniform",
            reference: "chief");
        var useXLine = _orientation.StartsWith("X", StringComparison.OrdinalIgnoreCase);
        var samples = result.Fields
            .SelectMany(item => item.Wavelengths)
            .SelectMany(wavelength => wavelength.Rays.Select(ray => (
                Coordinate: 1000 * (useXLine ? ray.Y : ray.X),
                Weight: ray.Intensity * EnergyCurveSupport.WavelengthWeight(wavelength.Wavelength))))
            .Where(sample => double.IsFinite(sample.Coordinate) && sample.Weight > 0)
            .ToArray();
        var automaticRadius = samples.Select(sample => Math.Abs(sample.Coordinate))
            .DefaultIfEmpty(1)
            .Max() * 1.05;
        var radius = _maximumRadiusMicrometers > 0
            ? _maximumRadiusMicrometers
            : Math.Max(1e-9, automaticRadius);
        var histogram = new double[_numPoints];
        var step = 2 * radius / (_numPoints - 1.0);
        foreach (var sample in samples)
        {
            var index = (int)Math.Round((sample.Coordinate + radius) / step);
            if (index >= 0 && index < histogram.Length)
            {
                histogram[index] += sample.Weight;
            }
        }

        var peak = histogram.DefaultIfEmpty(0).Max();
        var total = histogram.Sum();
        var cumulative = 0.0;
        var linePoints = new AnalysisPoint[_numPoints];
        var edgePoints = new AnalysisPoint[_numPoints];
        for (var index = 0; index < _numPoints; index++)
        {
            var coordinate = -radius + (step * index);
            cumulative += histogram[index];
            linePoints[index] = new AnalysisPoint(
                coordinate,
                peak > 0 ? histogram[index] / peak : 0);
            edgePoints[index] = new AnalysisPoint(
                coordinate,
                total > 0 ? cumulative / total : 0);
        }

        var series = new List<AnalysisSeries>();
        if (!_display.Equals("edge", StringComparison.OrdinalIgnoreCase))
        {
            series.Add(new AnalysisSeries(
                useXLine ? "Y Position (\u00B5m)" : "X Position (\u00B5m)",
                "Relative Intensity",
                linePoints,
                Name: "Line Spread",
                ColorIndex: 0));
        }

        if (!_display.Equals("line", StringComparison.OrdinalIgnoreCase))
        {
            series.Add(new AnalysisSeries(
                useXLine ? "Y Position (\u00B5m)" : "X Position (\u00B5m)",
                "Relative Response",
                edgePoints,
                Name: "Edge Spread",
                LineStyle: AnalysisLineStyle.Dashed,
                ColorIndex: 1));
        }

        return new AnalysisData(
            Name,
            new Dictionary<string, object>
            {
                ["Method"] = "Geometric ray histogram",
                ["PupilSampling"] = _pupilSampling,
                ["WavelengthNumber"] = _wavelengthNumber,
                ["FieldNumber"] = _fieldNumber,
                ["Orientation"] = _orientation,
                ["Display"] = _display,
                ["MaximumRadiusMicrometers"] = radius,
                ["RayCount"] = result.RayCount,
                ["VignettedRayCount"] = result.VignettedRayCount
            },
            series.FirstOrDefault(),
            series,
            new AnalysisPlotOptions(
                Title: "Geometric Line/Edge Spread",
                XMinimum: -radius,
                XMaximum: radius,
                YMinimum: 0,
                YMaximum: 1,
                ShowLegend: series.Count > 1,
                HideTopAndRightAxes: true,
                LegendBelow: series.Count > 1));
    }
}

public sealed class ExtendedSourceEncircledEnergyAnalysis : BaseAnalysis
{
    private readonly double _fieldSize;
    private readonly int _sourceSampling;
    private readonly int _numRays;
    private readonly int _numPoints;
    private readonly int _wavelengthNumber;
    private readonly int _fieldNumber;
    private readonly string _type;
    private readonly string _reference;
    private readonly double _maximumDistanceMicrometers;

    public ExtendedSourceEncircledEnergyAnalysis(
        Optic optic,
        double fieldSize = 0,
        int sourceSampling = 5,
        int numRays = 5000,
        int numPoints = 256,
        int wavelengthNumber = 0,
        int fieldNumber = 1,
        string type = "encircled",
        string reference = "centroid",
        double maximumDistanceMicrometers = 0) : base(optic)
    {
        _fieldSize = Math.Max(0, fieldSize);
        _sourceSampling = Math.Clamp(sourceSampling, 1, 21);
        _numRays = Math.Clamp(numRays, 100, 200_000);
        _numPoints = Math.Clamp(numPoints, 2, 2048);
        _wavelengthNumber = Math.Max(0, wavelengthNumber);
        _fieldNumber = Math.Max(1, fieldNumber);
        _type = type;
        _reference = reference;
        _maximumDistanceMicrometers = Math.Max(0, maximumDistanceMicrometers);
    }

    public override string Name => "Extended Source Encircled Energy";

    public override AnalysisData GenerateData()
    {
        var wavelengths = EnergyCurveSupport.SelectedWavelengths(Optic, _wavelengthNumber);
        if (wavelengths.Count == 0)
        {
            return EnergyCurveSupport.Empty(Name);
        }

        var centerField = EnergyCurveSupport.SelectedField(Optic, _fieldNumber);
        var maximumField = Math.Max(1e-9, FieldCoordinates.MaximumRadius(Optic.Fields));
        var fieldSize = _fieldSize > 0 ? _fieldSize : maximumField * 0.1;
        var normalizedWidth = fieldSize / maximumField;
        var sourceAxis = Enumerable.Range(0, _sourceSampling)
            .Select(index => _sourceSampling == 1
                ? 0
                : -0.5 + (index / (_sourceSampling - 1.0)))
            .ToArray();
        var sourceFields = sourceAxis.SelectMany(y => sourceAxis.Select(x => (
            Hx: centerField.Hx + (x * normalizedWidth),
            Hy: centerField.Hy + (y * normalizedWidth)))).ToArray();
        var raysPerSource = Math.Max(
            8,
            _numRays / Math.Max(1, sourceFields.Length * wavelengths.Count));
        var result = SpotAnalysisEngine.Generate(
            Optic,
            sourceFields,
            wavelengths,
            raysPerSource,
            "sobol",
            reference: "absolute");
        var samples = result.Fields
            .SelectMany(field => field.Wavelengths)
            .SelectMany(wavelength => wavelength.Rays.Select(ray => new EnergySample(
                ray.X * 1000,
                ray.Y * 1000,
                ray.Intensity * EnergyCurveSupport.WavelengthWeight(wavelength.Wavelength))))
            .Where(sample => sample.Weight > 0)
            .ToArray();
        var center = EnergyCurveSupport.ReferencePoint(
            Optic,
            centerField,
            wavelengths,
            samples,
            _reference);
        return EnergyCurveSupport.CreateCurve(
            Name,
            samples,
            center,
            _type,
            _maximumDistanceMicrometers,
            _numPoints,
            new Dictionary<string, object>
            {
                ["Method"] = "Extended uniform square source",
                ["FieldSize"] = fieldSize,
                ["SourceSampling"] = _sourceSampling,
                ["RequestedRayCount"] = _numRays,
                ["RayCount"] = result.RayCount,
                ["VignettedRayCount"] = result.VignettedRayCount,
                ["WavelengthNumber"] = _wavelengthNumber,
                ["FieldNumber"] = _fieldNumber,
                ["Reference"] = _reference
            });
    }
}

internal sealed record EnergySample(double X, double Y, double Weight);

internal static class EnergyCurveSupport
{
    public static AnalysisData Empty(string name)
    {
        return new AnalysisData(name, new Dictionary<string, object> { ["Status"] = "No energy data" });
    }

    public static IReadOnlyList<Wavelength> SelectedWavelengths(Optic optic, int wavelengthNumber)
    {
        var wavelengths = optic.Wavelengths.ToArray();
        return wavelengthNumber > 0
            ? wavelengths.Skip(wavelengthNumber - 1).Take(1).ToArray()
            : wavelengths;
    }

    public static (double Hx, double Hy) SelectedField(Optic optic, int fieldNumber)
    {
        var fields = SpotAnalysisEngine.DefinedFields(optic);
        return fields.Count == 0
            ? (0, 0)
            : fields[Math.Clamp(fieldNumber - 1, 0, fields.Count - 1)];
    }

    public static double WavelengthWeight(Wavelength wavelength)
    {
        return wavelength.Weight > 0 ? wavelength.Weight : 1;
    }

    public static (double X, double Y) Centroid(IReadOnlyList<EnergySample> samples)
    {
        var total = samples.Sum(sample => sample.Weight);
        return total <= 0
            ? (0, 0)
            : (
                samples.Sum(sample => sample.X * sample.Weight) / total,
                samples.Sum(sample => sample.Y * sample.Weight) / total);
    }

    public static (double X, double Y) ReferencePoint(
        Optic optic,
        (double Hx, double Hy) field,
        IReadOnlyList<Wavelength> wavelengths,
        IReadOnlyList<EnergySample> samples,
        string reference)
    {
        if (string.Equals(reference, "vertex", StringComparison.OrdinalIgnoreCase))
        {
            return (0, 0);
        }

        if (!string.Equals(reference, "chief", StringComparison.OrdinalIgnoreCase))
        {
            return Centroid(samples);
        }

        var primary = wavelengths.FirstOrDefault(wavelength => wavelength.IsPrimary)
            ?? wavelengths.First();
        var chief = SpotAnalysisEngine.Generate(
            optic,
            new[] { field },
            new[] { primary },
            1,
            "uniform",
            reference: "absolute");
        var ray = chief.Fields.FirstOrDefault()?.Wavelengths.FirstOrDefault()?.Rays.FirstOrDefault();
        return ray is null ? Centroid(samples) : (ray.X * 1000, ray.Y * 1000);
    }

    public static AnalysisData CreateCurve(
        string name,
        IReadOnlyList<EnergySample> samples,
        (double X, double Y) center,
        string type,
        double requestedMaximumDistance,
        int numPoints,
        IReadOnlyDictionary<string, object> values)
    {
        if (samples.Count == 0)
        {
            return Empty(name);
        }

        var weightedDistances = samples
            .Select(sample => (
                Distance: Distance(sample.X - center.X, sample.Y - center.Y, type),
                sample.Weight))
            .Where(sample => double.IsFinite(sample.Distance) && sample.Weight > 0)
            .OrderBy(sample => sample.Distance)
            .ToArray();
        if (weightedDistances.Length == 0)
        {
            return Empty(name);
        }

        var maximumDistance = requestedMaximumDistance > 0
            ? requestedMaximumDistance
            : Math.Max(1e-9, weightedDistances[^1].Distance * 1.05);
        var cumulative = new double[weightedDistances.Length];
        var total = 0.0;
        for (var index = 0; index < weightedDistances.Length; index++)
        {
            total += weightedDistances[index].Weight;
            cumulative[index] = total;
        }

        var points = Enumerable.Range(0, numPoints)
            .Select(index =>
            {
                var distance = maximumDistance * index / (numPoints - 1.0);
                var insertion = UpperBound(weightedDistances, distance);
                var energy = insertion == 0 || total <= 0
                    ? 0
                    : cumulative[insertion - 1] / total;
                return new AnalysisPoint(distance, energy);
            })
            .ToArray();
        var resultValues = values.ToDictionary(item => item.Key, item => item.Value);
        resultValues["Type"] = type;
        resultValues["MaximumDistanceMicrometers"] = maximumDistance;
        resultValues["TotalWeight"] = total;
        resultValues["SampleCount"] = weightedDistances.Length;
        var series = new AnalysisSeries(
            "Distance (\u00B5m)",
            "Fraction of Energy",
            points,
            Name: type,
            ColorIndex: 0);
        return new AnalysisData(
            name,
            resultValues,
            series,
            new[] { series },
            new AnalysisPlotOptions(
                Title: name,
                XMinimum: 0,
                XMaximum: maximumDistance,
                YMinimum: 0,
                YMaximum: 1,
                HideTopAndRightAxes: true,
                GridOpacity: 0.25));
    }

    private static double Distance(double x, double y, string type)
    {
        if (type.StartsWith("X", StringComparison.OrdinalIgnoreCase))
        {
            return Math.Abs(x);
        }

        if (type.StartsWith("Y", StringComparison.OrdinalIgnoreCase))
        {
            return Math.Abs(y);
        }

        return type.Contains("square", StringComparison.OrdinalIgnoreCase)
            ? Math.Max(Math.Abs(x), Math.Abs(y))
            : Math.Sqrt((x * x) + (y * y));
    }

    private static int UpperBound(
        IReadOnlyList<(double Distance, double Weight)> samples,
        double distance)
    {
        var lower = 0;
        var upper = samples.Count;
        while (lower < upper)
        {
            var middle = lower + ((upper - lower) / 2);
            if (samples[middle].Distance <= distance)
            {
                lower = middle + 1;
            }
            else
            {
                upper = middle;
            }
        }

        return lower;
    }
}

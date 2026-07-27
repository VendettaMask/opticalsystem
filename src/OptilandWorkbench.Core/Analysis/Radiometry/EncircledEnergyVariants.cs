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
        int fieldNumber = 0,
        string type = "encircled",
        string reference = "centroid",
        double maximumDistanceMicrometers = 0) : base(optic)
    {
        _pupilSampling = Math.Clamp(pupilSampling, 8, 512);
        _imageSampling = Math.Clamp(imageSampling, 16, 1024);
        _numPoints = Math.Clamp(numPoints, 2, 2048);
        _wavelengthNumber = Math.Max(0, wavelengthNumber);
        _fieldNumber = Math.Max(0, fieldNumber);
        _type = type;
        _reference = reference;
        _maximumDistanceMicrometers = Math.Max(0, maximumDistanceMicrometers);
    }

    public override string Name => "Diffraction Encircled Energy";

    public override AnalysisData GenerateData()
    {
        var allFields = SpotAnalysisEngine.DefinedFields(Optic);
        if (allFields.Count == 0)
        {
            return EnergyCurveSupport.Empty(Name);
        }

        var fieldIndices = _fieldNumber <= 0
            ? Enumerable.Range(0, allFields.Count).ToArray()
            : new[] { Math.Clamp(_fieldNumber - 1, 0, allFields.Count - 1) };
        var curves = new List<DiffractionEnergyCurve>(fieldIndices.Length);
        foreach (var fieldIndex in fieldIndices)
        {
            var source = new PsfAnalysis(
                Optic,
                _pupilSampling,
                _imageSampling,
                _wavelengthNumber,
                fieldIndex + 1,
                type: "linear",
                displayAs: "heatmap").GenerateData();
            var heatmap = source.PlotSeries.FirstOrDefault(
                series => series.Kind == AnalysisSeriesKind.Heatmap);
            if (heatmap is null || heatmap.Points.Count == 0)
            {
                continue;
            }

            var samples = heatmap.Points
                .Where(point => point.Value is > 0 && double.IsFinite(point.Value.Value))
                .Select(point => new EnergySample(point.X, point.Y, point.Value!.Value))
                .ToArray();
            if (samples.Length == 0)
            {
                continue;
            }

            var center = string.Equals(_reference, "centroid", StringComparison.OrdinalIgnoreCase)
                ? EnergyCurveSupport.Centroid(samples)
                : (X: 0.0, Y: 0.0);
            var automatic = EnergyCurveSupport.CreateCurve(
                Name,
                samples,
                center,
                _type,
                0,
                _numPoints,
                new Dictionary<string, object>());
            var automaticMaximum = Convert.ToDouble(
                automatic.Values["MaximumDistanceMicrometers"],
                System.Globalization.CultureInfo.InvariantCulture);
            curves.Add(new DiffractionEnergyCurve(
                fieldIndex,
                allFields[fieldIndex],
                samples,
                center,
                automaticMaximum));
        }

        if (curves.Count == 0)
        {
            return EnergyCurveSupport.Empty(Name);
        }

        var maximumDistance = _maximumDistanceMicrometers > 0
            ? _maximumDistanceMicrometers
            : curves.Max(curve => curve.AutomaticMaximumDistance);
        var series = new List<AnalysisSeries>(curves.Count + 1);
        if (IsRadialEncircledEnergy(_type))
        {
            var wavelengths = EnergyCurveSupport.SelectedWavelengths(Optic, _wavelengthNumber);
            var ideal = BuildDiffractionLimit(
                curves[0].Field,
                wavelengths,
                maximumDistance,
                _numPoints);
            if (ideal.Count > 0)
            {
                series.Add(new AnalysisSeries(
                    RadiusAxisLabel(),
                    "\u5708\u5165\u80fd\u91cf\u5206\u6570",
                    ideal,
                    Name: "\u884d\u5c04\u6781\u9650",
                    ColorIndex: 10,
                    LineWidth: 1.25));
            }
        }

        foreach (var curve in curves)
        {
            var data = EnergyCurveSupport.CreateCurve(
                Name,
                curve.Samples,
                curve.Center,
                _type,
                maximumDistance,
                _numPoints,
                new Dictionary<string, object>());
            var points = data.Series?.Points ?? Array.Empty<AnalysisPoint>();
            series.Add(new AnalysisSeries(
                RadiusAxisLabel(),
                "\u5708\u5165\u80fd\u91cf\u5206\u6570",
                points,
                Name: FieldSeriesName(curve.FieldIndex, curve.Field),
                ColorIndex: curve.FieldIndex,
                LineWidth: 1.25));
        }

        return new AnalysisData(
            Name,
            new Dictionary<string, object>
            {
                ["Method"] = "FFT PSF integration",
                ["PupilSampling"] = _pupilSampling,
                ["ImageSampling"] = _imageSampling,
                ["WavelengthNumber"] = _wavelengthNumber,
                ["FieldNumber"] = _fieldNumber,
                ["FieldCount"] = curves.Count,
                ["Reference"] = _reference,
                ["Type"] = _type,
                ["MaximumDistanceMicrometers"] = maximumDistance,
                ["DiffractionLimit"] = IsRadialEncircledEnergy(_type)
            },
            series.FirstOrDefault(),
            series,
            new AnalysisPlotOptions(
                Title: "FFT \u884d\u5c04\u5708\u5165\u80fd\u91cf",
                XMinimum: 0,
                XMaximum: maximumDistance,
                YMinimum: 0,
                YMaximum: 1,
                ShowLegend: true,
                HideTopAndRightAxes: true,
                GridOpacity: 0.25,
                LegendBelow: true));
    }

    private string RadiusAxisLabel()
    {
        if (_type.StartsWith("X", StringComparison.OrdinalIgnoreCase))
        {
            return "X \u65b9\u5411\u8ddd\u79bb\uff08\u00b5m\uff09";
        }

        if (_type.StartsWith("Y", StringComparison.OrdinalIgnoreCase))
        {
            return "Y \u65b9\u5411\u8ddd\u79bb\uff08\u00b5m\uff09";
        }

        if (_type.Contains("square", StringComparison.OrdinalIgnoreCase))
        {
            return "\u534a\u8fb9\u957f\uff08\u4ece\u8d28\u5fc3\u8d77\uff0c\u00b5m\uff09";
        }

        return _reference.Equals("centroid", StringComparison.OrdinalIgnoreCase)
            ? "\u534a\u5f84\uff08\u4ece\u8d28\u5fc3\u8d77\uff0c\u00b5m\uff09"
            : "\u534a\u5f84\uff08\u00b5m\uff09";
    }

    private string FieldSeriesName(int fieldIndex, (double Hx, double Hy) field)
    {
        var actual = FieldCoordinates.Denormalize(Optic.Fields, field.Hx, field.Hy);
        var unit = Optic.FieldDefinition == FieldDefinitionKind.Angle ? "\u00b0" : "mm";
        if (Math.Abs(actual.X) <= 1e-12)
        {
            return $"{actual.Y:0.0000} {unit}";
        }

        if (Math.Abs(actual.Y) <= 1e-12)
        {
            return $"{actual.X:0.0000} {unit}";
        }

        return $"F{fieldIndex + 1}: ({actual.X:0.0000}, {actual.Y:0.0000}) {unit}";
    }

    private static bool IsRadialEncircledEnergy(string type)
    {
        return !type.StartsWith("X", StringComparison.OrdinalIgnoreCase)
            && !type.StartsWith("Y", StringComparison.OrdinalIgnoreCase)
            && !type.Contains("square", StringComparison.OrdinalIgnoreCase);
    }

    private IReadOnlyList<AnalysisPoint> BuildDiffractionLimit(
        (double Hx, double Hy) field,
        IReadOnlyList<Wavelength> wavelengths,
        double maximumDistance,
        int numPoints)
    {
        var components = wavelengths
            .Select(wavelength => (
                Wavelength: wavelength.Micrometers,
                Weight: EnergyCurveSupport.WavelengthWeight(wavelength),
                FNumber: DiffractionEngine.WorkingFNumber(Optic, field, wavelength)))
            .Where(component => component.Wavelength > 0
                && component.Weight > 0
                && double.IsFinite(component.FNumber)
                && component.FNumber > 0)
            .ToArray();
        var totalWeight = components.Sum(component => component.Weight);
        if (components.Length == 0 || totalWeight <= 0)
        {
            return Array.Empty<AnalysisPoint>();
        }

        return Enumerable.Range(0, numPoints)
            .Select(index =>
            {
                var radius = maximumDistance * index / (numPoints - 1.0);
                var energy = components.Sum(component =>
                {
                    var argument = Math.PI * radius / (component.Wavelength * component.FNumber);
                    var j0 = BesselJ0(argument);
                    var j1 = BesselJ1(argument);
                    return component.Weight * Math.Clamp(1 - (j0 * j0) - (j1 * j1), 0, 1);
                }) / totalWeight;
                return new AnalysisPoint(radius, energy);
            })
            .ToArray();
    }

    private static double BesselJ0(double value)
    {
        var x = Math.Abs(value);
        if (x < 8)
        {
            var y = x * x;
            var numerator = 57568490574.0 + (y * (-13362590354.0
                + (y * (651619640.7 + (y * (-11214424.18
                + (y * (77392.33017 + (y * -184.9052456)))))))));
            var denominator = 57568490411.0 + (y * (1029532985.0
                + (y * (9494680.718 + (y * (59272.64853
                + (y * (267.8532712 + y))))))));
            return numerator / denominator;
        }

        var z = 8 / x;
        var yLarge = z * z;
        var phase = x - 0.785398164;
        var first = 1 + (yLarge * (-0.001098628627
            + (yLarge * (0.00002734510407
            + (yLarge * (-0.000002073370639
            + (yLarge * 0.0000002093887211)))))));
        var second = -0.01562499995 + (yLarge * (0.0001430488765
            + (yLarge * (-0.000006911147651
            + (yLarge * (0.0000007621095161
            - (yLarge * 0.0000000934945152)))))));
        return Math.Sqrt(0.636619772 / x)
            * ((Math.Cos(phase) * first) - (z * Math.Sin(phase) * second));
    }

    private static double BesselJ1(double value)
    {
        var x = Math.Abs(value);
        double result;
        if (x < 8)
        {
            var y = x * x;
            var numerator = x * (72362614232.0 + (y * (-7895059235.0
                + (y * (242396853.1 + (y * (-2972611.439
                + (y * (15704.48260 + (y * -30.16036606))))))))));
            var denominator = 144725228442.0 + (y * (2300535178.0
                + (y * (18583304.74 + (y * (99447.43394
                + (y * (376.9991397 + y))))))));
            result = numerator / denominator;
        }
        else
        {
            var z = 8 / x;
            var yLarge = z * z;
            var phase = x - 2.356194491;
            var first = 1 + (yLarge * (0.00183105
                + (yLarge * (-0.00003516396496
                + (yLarge * (0.000002457520174
                + (yLarge * -0.000000240337019)))))));
            var second = 0.04687499995 + (yLarge * (-0.0002002690873
                + (yLarge * (0.000008449199096
                + (yLarge * (-0.00000088228987
                + (yLarge * 0.000000105787412)))))));
            result = Math.Sqrt(0.636619772 / x)
                * ((Math.Cos(phase) * first) - (z * Math.Sin(phase) * second));
        }

        return value < 0 ? -result : result;
    }

    private sealed record DiffractionEnergyCurve(
        int FieldIndex,
        (double Hx, double Hy) Field,
        IReadOnlyList<EnergySample> Samples,
        (double X, double Y) Center,
        double AutomaticMaximumDistance);
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

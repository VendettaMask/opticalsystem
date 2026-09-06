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
            var pixelGrid = CreatePixelGrid(fieldIndex, ignoreOpd: false);
            if (pixelGrid is null)
            {
                continue;
            }

            var automaticMaximum = PsfPixelEnergyGrid.DefaultMaximumDistance(
                pixelGrid.RadiusContaining(0.99));
            curves.Add(new DiffractionEnergyCurve(
                fieldIndex,
                allFields[fieldIndex],
                pixelGrid,
                automaticMaximum));
        }

        if (curves.Count == 0)
        {
            return EnergyCurveSupport.Empty(Name);
        }

        var radial = IsRadialEncircledEnergy(_type);
        var idealGrid = radial
            ? CreatePixelGrid(curves[0].FieldIndex, ignoreOpd: true)
            : null;
        var automaticDistance = idealGrid is null
            ? curves.Max(curve => curve.AutomaticMaximumDistance)
            : PsfPixelEnergyGrid.DefaultMaximumDistance(idealGrid.RadiusContaining(0.99));
        var maximumDistance = _maximumDistanceMicrometers > 0
            ? _maximumDistanceMicrometers
            : automaticDistance;
        var series = new List<AnalysisSeries>(curves.Count + 1);
        if (idealGrid is not null)
        {
            var ideal = BuildCurve(idealGrid, maximumDistance);
            series.Add(new AnalysisSeries(
                RadiusAxisLabel(),
                "\u5708\u5165\u80fd\u91cf\u5206\u6570",
                ideal,
                Name: "\u884d\u5c04\u6781\u9650",
                ColorIndex: 10,
                LineWidth: 1.25,
                XQuantity: AnalysisAxisQuantity.Radius,
                XUnit: AnalysisAxisUnit.Micrometer,
                YQuantity: AnalysisAxisQuantity.EnergyFraction,
                YUnit: AnalysisAxisUnit.Dimensionless));
        }

        foreach (var curve in curves)
        {
            var points = BuildCurve(curve.PixelGrid, maximumDistance);
            series.Add(new AnalysisSeries(
                RadiusAxisLabel(),
                "\u5708\u5165\u80fd\u91cf\u5206\u6570",
                points,
                Name: FieldSeriesName(curve.FieldIndex, curve.Field),
                ColorIndex: curve.FieldIndex,
                LineWidth: 1.25,
                XQuantity: AnalysisAxisQuantity.Radius,
                XUnit: AnalysisAxisUnit.Micrometer,
                YQuantity: AnalysisAxisQuantity.EnergyFraction,
                YUnit: AnalysisAxisUnit.Dimensionless));
        }

        return new AnalysisData(
            Name,
            new Dictionary<string, object>
            {
                ["Method"] = "FFT PSF pixel-area integration",
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

    private PsfPixelEnergyGrid? CreatePixelGrid(int fieldIndex, bool ignoreOpd)
    {
        var source = new PsfAnalysis(
            Optic,
            _pupilSampling,
            _imageSampling,
            _wavelengthNumber,
            fieldIndex + 1,
            type: "linear",
            displayAs: "heatmap",
            zemaxCompatible: true,
            ignoreOpd: ignoreOpd).GenerateData();
        var heatmap = source.PlotSeries.FirstOrDefault(
            series => series.Kind == AnalysisSeriesKind.Heatmap);
        if (heatmap is null || heatmap.Points.Count == 0)
        {
            return null;
        }

        var samples = CentralFftWindow(heatmap.Points, _pupilSampling, _imageSampling)
            .Where(point => point.Value is > 0 && double.IsFinite(point.Value.Value))
            .Select(point => new EnergySample(point.X, point.Y, point.Value!.Value))
            .ToArray();
        if (samples.Length == 0)
        {
            return null;
        }

        var center = string.Equals(_reference, "centroid", StringComparison.OrdinalIgnoreCase)
            ? EnergyCurveSupport.Centroid(samples)
            : (X: 0.0, Y: 0.0);
        return new PsfPixelEnergyGrid(samples, center, _type);
    }

    private IReadOnlyList<AnalysisPoint> BuildCurve(
        PsfPixelEnergyGrid pixelGrid,
        double maximumDistance)
    {
        return Enumerable.Range(0, _numPoints)
            .Select(index =>
            {
                var distance = maximumDistance * index / (_numPoints - 1.0);
                return new AnalysisPoint(distance, pixelGrid.Fraction(distance));
            })
            .ToArray();
    }

    private static IReadOnlyList<AnalysisPoint> CentralFftWindow(
        IReadOnlyList<AnalysisPoint> points,
        int pupilSampling,
        int imageSampling)
    {
        var xCoordinates = points.Select(point => point.X).Distinct().Order().ToArray();
        var yCoordinates = points.Select(point => point.Y).Distinct().Order().ToArray();
        var xCount = Math.Min(Math.Min(pupilSampling, imageSampling), xCoordinates.Length);
        var yCount = Math.Min(Math.Min(pupilSampling, imageSampling), yCoordinates.Length);
        var xStart = (xCoordinates.Length - xCount) / 2;
        var yStart = (yCoordinates.Length - yCount) / 2;
        var xSelected = xCoordinates.Skip(xStart).Take(xCount).ToHashSet();
        var ySelected = yCoordinates.Skip(yStart).Take(yCount).ToHashSet();
        return points
            .Where(point => xSelected.Contains(point.X) && ySelected.Contains(point.Y))
            .ToArray();
    }

    internal static double IdealAiryEncircledEnergy(
        double radiusMicrometers,
        double wavelengthMicrometers,
        double fNumber)
    {
        if (radiusMicrometers <= 0 || wavelengthMicrometers <= 0 || fNumber <= 0)
        {
            return 0;
        }

        var argument = Math.PI * radiusMicrometers / (wavelengthMicrometers * fNumber);
        var j0 = BesselJ0(argument);
        var j1 = BesselJ1(argument);
        return Math.Clamp(1 - (j0 * j0) - (j1 * j1), 0, 1);
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
        PsfPixelEnergyGrid PixelGrid,
        double AutomaticMaximumDistance);
}

internal sealed class PsfPixelEnergyGrid
{
    private static readonly double[] GaussNodes =
    {
        0.09501250983763744,
        0.2816035507792589,
        0.45801677765722737,
        0.6178762444026438,
        0.755404408355003,
        0.8656312023878318,
        0.9445750230732326,
        0.9894009349916499
    };

    private static readonly double[] GaussWeights =
    {
        0.1894506104550685,
        0.18260341504492358,
        0.16915651939500254,
        0.14959598881657673,
        0.12462897125553388,
        0.09515851168249278,
        0.06225352393864789,
        0.027152459411754096
    };

    private readonly Pixel[] _pixels;
    private readonly string _type;
    private readonly double _totalWeight;
    private readonly double _maximumRadius;

    public PsfPixelEnergyGrid(
        IReadOnlyList<EnergySample> samples,
        (double X, double Y) center,
        string type)
    {
        var pitchX = GridPitch(samples.Select(sample => sample.X));
        var pitchY = GridPitch(samples.Select(sample => sample.Y));
        var halfX = pitchX / 2;
        var halfY = pitchY / 2;
        _type = type;
        _pixels = samples
            .Where(sample => sample.Weight > 0 && double.IsFinite(sample.Weight))
            .Select(sample => new Pixel(
                sample.X - center.X - halfX,
                sample.X - center.X + halfX,
                sample.Y - center.Y - halfY,
                sample.Y - center.Y + halfY,
                sample.Weight))
            .ToArray();
        _totalWeight = _pixels.Sum(pixel => pixel.Weight);
        _maximumRadius = _pixels.Length == 0
            ? 0
            : _pixels.Max(pixel => MaximumDistance(pixel, type));
    }

    public double Fraction(double distance)
    {
        if (distance <= 0 || _totalWeight <= 0)
        {
            return 0;
        }

        if (distance >= _maximumRadius)
        {
            return 1;
        }

        var enclosed = 0.0;
        foreach (var pixel in _pixels)
        {
            enclosed += pixel.Weight * AreaFraction(pixel, distance, _type);
        }

        return Math.Clamp(enclosed / _totalWeight, 0, 1);
    }

    public double RadiusContaining(double fraction)
    {
        if (_maximumRadius <= 0 || _totalWeight <= 0)
        {
            return 0;
        }

        var target = Math.Clamp(fraction, 0, 1);
        var lower = 0.0;
        var upper = _maximumRadius;
        for (var iteration = 0; iteration < 48; iteration++)
        {
            var middle = (lower + upper) / 2;
            if (Fraction(middle) < target)
            {
                lower = middle;
            }
            else
            {
                upper = middle;
            }
        }

        return upper;
    }

    public static double DefaultMaximumDistance(double requiredDistance)
    {
        if (!double.IsFinite(requiredDistance) || requiredDistance <= 0)
        {
            return 1e-9;
        }

        var exponent = Math.Floor(Math.Log10(requiredDistance));
        var scale = Math.Pow(10, exponent);
        var normalized = requiredDistance / scale;
        var nice = normalized <= 1 + 1e-12
            ? 1
            : normalized <= 2 + 1e-12
                ? 2
                : normalized <= 5 + 1e-12
                    ? 5
                    : 10;
        return nice * scale;
    }

    private static double AreaFraction(Pixel pixel, double distance, string type)
    {
        if (type.StartsWith("X", StringComparison.OrdinalIgnoreCase))
        {
            return Overlap(pixel.XMinimum, pixel.XMaximum, -distance, distance)
                / (pixel.XMaximum - pixel.XMinimum);
        }

        if (type.StartsWith("Y", StringComparison.OrdinalIgnoreCase))
        {
            return Overlap(pixel.YMinimum, pixel.YMaximum, -distance, distance)
                / (pixel.YMaximum - pixel.YMinimum);
        }

        if (type.Contains("square", StringComparison.OrdinalIgnoreCase))
        {
            var xFraction = Overlap(pixel.XMinimum, pixel.XMaximum, -distance, distance)
                / (pixel.XMaximum - pixel.XMinimum);
            var yFraction = Overlap(pixel.YMinimum, pixel.YMaximum, -distance, distance)
                / (pixel.YMaximum - pixel.YMinimum);
            return xFraction * yFraction;
        }

        var minimum = MinimumRadialDistance(pixel);
        if (minimum >= distance)
        {
            return 0;
        }

        var maximum = MaximumRadialDistance(pixel);
        if (maximum <= distance)
        {
            return 1;
        }

        var area = CircleRectangleIntersectionArea(pixel, distance);
        var pixelArea = (pixel.XMaximum - pixel.XMinimum)
            * (pixel.YMaximum - pixel.YMinimum);
        return Math.Clamp(area / pixelArea, 0, 1);
    }

    private static double CircleRectangleIntersectionArea(Pixel pixel, double radius)
    {
        var left = Math.Max(pixel.XMinimum, -radius);
        var right = Math.Min(pixel.XMaximum, radius);
        if (right <= left)
        {
            return 0;
        }

        var midpoint = (left + right) / 2;
        var halfWidth = (right - left) / 2;
        var integral = 0.0;
        for (var index = 0; index < GaussNodes.Length; index++)
        {
            var offset = halfWidth * GaussNodes[index];
            integral += GaussWeights[index]
                * (VerticalOverlap(midpoint - offset, pixel, radius)
                    + VerticalOverlap(midpoint + offset, pixel, radius));
        }

        return halfWidth * integral;
    }

    private static double VerticalOverlap(double x, Pixel pixel, double radius)
    {
        var halfHeight = Math.Sqrt(Math.Max(0, (radius * radius) - (x * x)));
        return Overlap(pixel.YMinimum, pixel.YMaximum, -halfHeight, halfHeight);
    }

    private static double Overlap(double firstMinimum, double firstMaximum, double secondMinimum, double secondMaximum)
    {
        return Math.Max(0, Math.Min(firstMaximum, secondMaximum) - Math.Max(firstMinimum, secondMinimum));
    }

    private static double MinimumRadialDistance(Pixel pixel)
    {
        var x = pixel.XMinimum > 0
            ? pixel.XMinimum
            : pixel.XMaximum < 0 ? -pixel.XMaximum : 0;
        var y = pixel.YMinimum > 0
            ? pixel.YMinimum
            : pixel.YMaximum < 0 ? -pixel.YMaximum : 0;
        return Math.Sqrt((x * x) + (y * y));
    }

    private static double MaximumRadialDistance(Pixel pixel)
    {
        var x = Math.Max(Math.Abs(pixel.XMinimum), Math.Abs(pixel.XMaximum));
        var y = Math.Max(Math.Abs(pixel.YMinimum), Math.Abs(pixel.YMaximum));
        return Math.Sqrt((x * x) + (y * y));
    }

    private static double MaximumDistance(Pixel pixel, string type)
    {
        if (type.StartsWith("X", StringComparison.OrdinalIgnoreCase))
        {
            return Math.Max(Math.Abs(pixel.XMinimum), Math.Abs(pixel.XMaximum));
        }

        if (type.StartsWith("Y", StringComparison.OrdinalIgnoreCase))
        {
            return Math.Max(Math.Abs(pixel.YMinimum), Math.Abs(pixel.YMaximum));
        }

        if (type.Contains("square", StringComparison.OrdinalIgnoreCase))
        {
            return Math.Max(
                Math.Max(Math.Abs(pixel.XMinimum), Math.Abs(pixel.XMaximum)),
                Math.Max(Math.Abs(pixel.YMinimum), Math.Abs(pixel.YMaximum)));
        }

        return MaximumRadialDistance(pixel);
    }

    private static double GridPitch(IEnumerable<double> coordinates)
    {
        var sorted = coordinates.Distinct().Order().ToArray();
        var minimum = Enumerable.Range(1, sorted.Length - 1)
            .Select(index => sorted[index] - sorted[index - 1])
            .Where(delta => delta > 1e-12 && double.IsFinite(delta))
            .DefaultIfEmpty(1)
            .Min();
        return minimum;
    }

    private sealed record Pixel(
        double XMinimum,
        double XMaximum,
        double YMinimum,
        double YMaximum,
        double Weight);
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
        IReadOnlyList<Wavelength> wavelengths = AnalysisTrace.SelectWavelengths(Optic, _wavelengthNumber);
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
            "uniform-intervals",
            reference: "chief", aimAtStop: Optic.RayAimingEnabled, includeSurfaceTransmission: false);
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
        var binWidth = 2 * radius / _numPoints;
        foreach (var sample in samples)
        {
            var index = (int)Math.Floor(sample.Coordinate / binWidth + (_numPoints - 1) / 2d + 0.5);
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
                total > 0 ? (cumulative - histogram[index] / 2) / total : 0);
        }

        var series = new List<AnalysisSeries>();
        if (!_display.Equals("edge", StringComparison.OrdinalIgnoreCase))
        {
            series.Add(new AnalysisSeries(
                useXLine ? "Y Position (\u00B5m)" : "X Position (\u00B5m)",
                "Relative Intensity",
                linePoints,
                Name: "Line Spread",
                ColorIndex: 0,
                XQuantity: AnalysisAxisQuantity.ImageHeight,
                XUnit: AnalysisAxisUnit.Micrometer,
                YQuantity: AnalysisAxisQuantity.Irradiance,
                YUnit: AnalysisAxisUnit.Dimensionless));
        }

        if (!_display.Equals("line", StringComparison.OrdinalIgnoreCase))
        {
            series.Add(new AnalysisSeries(
                useXLine ? "Y Position (\u00B5m)" : "X Position (\u00B5m)",
                "Relative Response",
                edgePoints,
                Name: "Edge Spread",
                LineStyle: AnalysisLineStyle.Dashed,
                ColorIndex: 1,
                XQuantity: AnalysisAxisQuantity.ImageHeight,
                XUnit: AnalysisAxisUnit.Micrometer,
                YQuantity: AnalysisAxisQuantity.Irradiance,
                YUnit: AnalysisAxisUnit.Dimensionless));
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
                ["HistogramBinWidthMicrometers"] = binWidth,
                ["DisplayCoordinateStepMicrometers"] = step,
                ["PupilGridConvention"] = "N intervals, N+1 axis nodes, inclusive disk boundary",
                ["EdgeIntegration"] = "Cumulative histogram through the center of each bin",
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
    public bool ZemaxCompatibleOutput { get; init; }
    private readonly double _fieldSize;
    private readonly int _sourceSampling;
    private readonly int _numRays;
    private readonly int _numPoints;
    private readonly int _wavelengthNumber;
    private readonly int _fieldNumber;
    private readonly string _type;
    private readonly string _reference;
    private readonly double _maximumDistanceMicrometers;
    private readonly ExtendedSourceImage? _sourceImage;
    private readonly string _sourceName;

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
        double maximumDistanceMicrometers = 0,
        ExtendedSourceImage? sourceImage = null,
        string sourceName = "uniform square") : base(optic)
    {
        _fieldSize = Math.Max(0, fieldSize);
        _sourceSampling = Math.Clamp(sourceSampling, 1, 21);
        _numRays = Math.Clamp(numRays, 100, 2_000_000);
        _numPoints = Math.Clamp(numPoints, 2, 2048);
        _wavelengthNumber = Math.Max(0, wavelengthNumber);
        _fieldNumber = Math.Max(1, fieldNumber);
        _type = type;
        _reference = reference;
        _maximumDistanceMicrometers = Math.Max(0, maximumDistanceMicrometers);
        _sourceImage = sourceImage;
        _sourceName = sourceName;
    }

    public override string Name => "Extended Source Encircled Energy";

    public override AnalysisData GenerateData()
    {
        IReadOnlyList<Wavelength> wavelengths = AnalysisTrace.SelectWavelengths(Optic, _wavelengthNumber);
        if (wavelengths.Count == 0)
        {
            return EnergyCurveSupport.Empty(Name);
        }

        var centerField = EnergyCurveSupport.SelectedField(Optic, _fieldNumber);
        var maximumField = Math.Max(1e-9, FieldCoordinates.MaximumRadius(Optic.Fields));
        var fieldSize = _fieldSize > 0 ? _fieldSize : maximumField * 0.1;
        var normalizedWidth = fieldSize / maximumField;
        var sourceFields = CreateSourceFields(centerField, normalizedWidth);
        if (sourceFields.Length == 0)
        {
            return EnergyCurveSupport.Empty(Name);
        }
        var raysPerSource = Math.Max(
            8,
            _numRays / Math.Max(1, sourceFields.Length * wavelengths.Count));
        var result = SpotAnalysisEngine.Generate(
            Optic,
            sourceFields.Select(source => (source.Hx, source.Hy)).ToArray(),
            wavelengths,
            raysPerSource,
            "sobol",
            reference: "absolute", aimAtStop: Optic.RayAimingEnabled,
            includeSurfaceTransmission: false);
        var samples = result.Fields
            .SelectMany((field, sourceIndex) => field.Wavelengths
                .SelectMany(wavelength => wavelength.Rays.Select(ray => new EnergySample(
                    ray.X * 1000,
                    ray.Y * 1000,
                    ray.Intensity
                    * sourceFields[sourceIndex].Weight
                    * EnergyCurveSupport.WavelengthWeight(wavelength.Wavelength)))))
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
                ["Method"] = _sourceImage is null
                    ? "Extended uniform square source"
                    : "Zemax IMA weighted-pixel source",
                ["Source"] = _sourceName,
                ["FieldSize"] = fieldSize,
                ["SourceSampling"] = _sourceSampling,
                ["ActiveSourcePointCount"] = sourceFields.Length,
                ["ActiveSourcePixelCount"] = _sourceImage?.Values.Count(value =>
                    double.IsFinite(value) && value > 0) ?? sourceFields.Length,
                ["RequestedRayCount"] = _numRays,
                ["RayCount"] = result.RayCount,
                ["VignettedRayCount"] = result.VignettedRayCount,
                ["WavelengthNumber"] = _wavelengthNumber,
                ["FieldNumber"] = _fieldNumber,
                ["Reference"] = _reference,
                ["ReferenceXMicrometers"] = center.X,
                ["ReferenceYMicrometers"] = center.Y
            }, zemaxExtendedPlot: ZemaxCompatibleOutput);
    }

    private (double Hx, double Hy, double Weight)[] CreateSourceFields(
        (double Hx, double Hy) centerField,
        double normalizedWidth)
    {
        if (_sourceImage is null)
        {
            var sourceAxis = Enumerable.Range(0, _sourceSampling)
                .Select(index => _sourceSampling == 1
                    ? 0
                    : -0.5 + (index / (_sourceSampling - 1.0)))
                .ToArray();
            return sourceAxis.SelectMany(y => sourceAxis.Select(x => (
                Hx: centerField.Hx + (x * normalizedWidth),
                Hy: centerField.Hy + (y * normalizedWidth),
                Weight: 1.0))).ToArray();
        }

        var fields = new List<(double Hx, double Hy, double Weight)>();
        for (var row = 0; row < _sourceImage.Height; row++)
        {
            for (var column = 0; column < _sourceImage.Width; column++)
            {
                var weight = _sourceImage.Value(row, column);
                if (!double.IsFinite(weight) || weight <= 0)
                {
                    continue;
                }

                // An IMA pixel is an emitting area, not a point at its center.
                // Deterministic sub-pixel centers reproduce Zemax's uniform ray
                // distribution over every active source pixel.
                for (var subRow = 0; subRow < _sourceSampling; subRow++)
                {
                    for (var subColumn = 0; subColumn < _sourceSampling; subColumn++)
                    {
                        var pixelX = column + ((subColumn + 0.5) / _sourceSampling);
                        var pixelY = row + ((subRow + 0.5) / _sourceSampling);
                        var x = (pixelX / _sourceImage.Width) - 0.5;
                        var y = 0.5 - (pixelY / _sourceImage.Height);
                        fields.Add((
                            centerField.Hx + (x * normalizedWidth),
                            centerField.Hy + (y * normalizedWidth),
                            weight / (_sourceSampling * _sourceSampling)));
                    }
                }
            }
        }

        return fields.ToArray();
    }
}

internal sealed record EnergySample(double X, double Y, double Weight);

internal static class EnergyCurveSupport
{
    public static AnalysisData Empty(string name)
    {
        return AnalysisData.Unavailable(name, "No energy data");
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
        return double.IsFinite(wavelength.Weight) ? Math.Max(0, wavelength.Weight) : 0;
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
        IReadOnlyDictionary<string, object> values,
        bool zemaxExtendedPlot = false)
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

        var knotCount = zemaxExtendedPlot ? 100 : numPoints;
        IReadOnlyList<AnalysisPoint> points = Enumerable.Range(0, knotCount)
            .Select(index =>
            {
                var distance = maximumDistance * index / (knotCount - 1.0);
                // The captured 2026 R1 extended-source histogram displays each cumulative
                // knot at the next bin boundary. Independent 5/10/20 µm captures verify
                // this one-bin convention; it does not move or rescale the traced rays.
                var evaluationRadius = zemaxExtendedPlot
                    ? maximumDistance * (index - 1) / (knotCount - 1.0)
                    : distance;
                var insertion = UpperBound(weightedDistances, evaluationRadius);
                var energy = insertion == 0 || total <= 0
                    ? 0
                    : cumulative[insertion - 1] / total;
                return new AnalysisPoint(distance, energy);
            })
            .ToArray();
        if (zemaxExtendedPlot) points = EnergyPlotSampling.Geometric(points);
        var resultValues = values.ToDictionary(item => item.Key, item => item.Value);
        resultValues["Type"] = type;
        resultValues["MaximumDistanceMicrometers"] = maximumDistance;
        resultValues["TotalWeight"] = total;
        resultValues["SampleCount"] = weightedDistances.Length;
        resultValues["ZemaxCompatibleOutput"] = zemaxExtendedPlot;
        resultValues["CumulativeKnotCount"] = knotCount;
        resultValues["PlotPointCount"] = points.Count;
        resultValues["CumulativeKnotEvaluationOffsetMicrometers"] = zemaxExtendedPlot ? -maximumDistance / (knotCount - 1) : 0;
        var series = new AnalysisSeries(
            "Distance (\u00B5m)",
            "Fraction of Energy",
            points,
            Name: type,
            ColorIndex: 0,
            XQuantity: AnalysisAxisQuantity.Radius,
            XUnit: AnalysisAxisUnit.Micrometer,
            YQuantity: AnalysisAxisQuantity.EnergyFraction,
            YUnit: AnalysisAxisUnit.Dimensionless);
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

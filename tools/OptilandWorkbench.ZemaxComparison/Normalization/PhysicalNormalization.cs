namespace OptilandWorkbench.ZemaxComparison.Normalization;

public static class PhysicalNormalization
{
    private static readonly Dictionary<string, (string Dimension, double Scale)> Units = new(StringComparer.Ordinal)
    {
        ["Millimeter"] = ("length", 1),
        ["Micrometer"] = ("length", 1e-3),
        ["Nanometer"] = ("length", 1e-6),
        ["Radian"] = ("angle", 1),
        ["Degree"] = ("angle", Math.PI / 180),
        ["Milliradian"] = ("angle", 1e-3),
        ["Dimensionless"] = ("ratio", 1),
        ["Percent"] = ("ratio", 0.01),
        ["Wave"] = ("wave", 1),
        ["Diopter"] = ("power", 1),
        ["CyclesPerMillimeter"] = ("frequency", 1),
        ["InverseMicrometer"] = ("frequency", 1000),
        ["InverseMillimeter"] = ("inverseLength", 1),
        ["CyclesPerMilliradian"] = ("angularFrequency", 1),
        ["Watt"] = ("powerFlux", 1),
        ["WattsPerSquareMillimeter"] = ("irradiance", 1)
    };
    public static double UnitScale(string from, string to)
    {
        if (!Units.TryGetValue(from, out var a) || !Units.TryGetValue(to, out var b) || a.Dimension != b.Dimension)
            throw new InvalidDataException($"Incompatible or unspecified units: {from}/{to}");
        return a.Scale / b.Scale;
    }
    public static Series1DResult Convert(Series1DResult series, Axis xAxis, Axis yAxis, List<string> log)
    {
        if (series.XAxis.Quantity != xAxis.Quantity || series.YAxis.Quantity != yAxis.Quantity)
            throw new InvalidDataException($"Physical quantity mismatch for {series.Id}");
        var xScale = UnitScale(series.XAxis.Unit, xAxis.Unit);
        var yScale = UnitScale(series.YAxis.Unit, yAxis.Unit);
        if (xScale != 1 || yScale != 1) log.Add($"{series.Id}: unit conversion X {series.XAxis.Unit}->{xAxis.Unit} x{xScale:R}; Y {series.YAxis.Unit}->{yAxis.Unit} x{yScale:R}");
        return series with { X = series.X.Select(x => x * xScale).ToArray(), Y = series.Y.Select(y => y * yScale).ToArray(), XAxis = xAxis, YAxis = yAxis };
    }
    public static (double[] X, double[] Y) Sort(double[] x, double[] y)
    {
        if (x.Length != y.Length || x.Length < 2 || x.Any(v => !double.IsFinite(v)) || y.Any(v => !double.IsFinite(v)))
            throw new InvalidDataException("A curve requires at least two finite coordinate/value pairs");
        var points = x.Zip(y).OrderBy(p => p.First).ToArray();
        if (points.Zip(points.Skip(1)).Any(p => p.First.First >= p.Second.First))
            throw new InvalidDataException("Duplicate or non-increasing physical coordinates");
        return (points.Select(p => p.First).ToArray(), points.Select(p => p.Second).ToArray());
    }
    public static double Interpolate(double[] x, double[] y, double target)
    {
        if (target < x[0] || target > x[^1]) throw new InvalidDataException("Extrapolation is forbidden");
        var i = Array.BinarySearch(x, target);
        if (i >= 0) return y[i];
        i = ~i;
        return y[i - 1] + (y[i] - y[i - 1]) * (target - x[i - 1]) / (x[i] - x[i - 1]);
    }
    public static Grid2DResult Orient(Grid2DResult grid, bool transpose, bool flipX, bool flipY, string reason, List<string> log)
    {
        if ((transpose || flipX || flipY) && string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Orientation requires an explicit physical convention");
        var x = grid.X.ToArray(); var y = grid.Y.ToArray(); var z = grid.Z.Select(row => row.ToArray()).ToArray();
        if (transpose) { z = Enumerable.Range(0, x.Length).Select(i => z.Select(row => row[i]).ToArray()).ToArray(); (x, y) = (y, x); }
        if (flipX) { Array.Reverse(x); foreach (var row in z) Array.Reverse(row); }
        if (flipY) { Array.Reverse(y); Array.Reverse(z); }
        if (transpose || flipX || flipY) log.Add($"{grid.Id}: transpose={transpose}, reverse-X={flipX}, reverse-Y={flipY}; {reason}");
        return grid with { X = x, Y = y, Z = z, XAxis = transpose ? grid.YAxis : grid.XAxis, YAxis = transpose ? grid.XAxis : grid.YAxis };
    }
}

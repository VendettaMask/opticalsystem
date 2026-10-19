namespace OpticalSystemDesign.Core.Differentiation;

/// <summary>Forward automatic differentiation, with immutable values and tangent vectors.</summary>
public readonly struct DifferentialScalar
{
    private readonly double[]? _gradient;
    public double Value { get; }
    public int Dimension => _gradient?.Length ?? 0;
    public double Derivative(int index) => _gradient is null ? 0 : _gradient[index];
    public double[] Gradient() => _gradient?.ToArray() ?? [];
    public bool IsFinite => double.IsFinite(Value) && (_gradient is null || _gradient.All(double.IsFinite));

    private DifferentialScalar(double value, double[]? gradient) => (Value, _gradient) = (value, gradient);
    public static implicit operator DifferentialScalar(double value) => new(value, null);
    public static DifferentialScalar Variable(double value, int dimension, int index, double scale = 1)
    {
        if (dimension < 1 || index < 0 || index >= dimension || !double.IsFinite(value) || !double.IsFinite(scale))
            throw new ArgumentOutOfRangeException(nameof(index));
        var gradient = new double[dimension];
        gradient[index] = scale;
        return new(value, gradient);
    }

    internal static DifferentialScalar FromGradient(double value, double[] gradient) => new(value, gradient);
    internal DifferentialScalar WithValue(double value) => new(value, _gradient);

    private static DifferentialScalar Combine(double value, DifferentialScalar a, double da, DifferentialScalar b, double db)
    {
        if (a.Dimension != 0 && b.Dimension != 0 && a.Dimension != b.Dimension)
            throw new ArgumentException("Automatic differentiation dimensions differ.");
        var dimension = Math.Max(a.Dimension, b.Dimension);
        if (dimension == 0) return value;
        var gradient = new double[dimension];
        for (var index = 0; index < dimension; index++) gradient[index] = da * a.Derivative(index) + db * b.Derivative(index);
        return new(value, gradient);
    }

    public static DifferentialScalar operator +(DifferentialScalar a, DifferentialScalar b) => Combine(a.Value + b.Value, a, 1, b, 1);
    public static DifferentialScalar operator -(DifferentialScalar a, DifferentialScalar b) => Combine(a.Value - b.Value, a, 1, b, -1);
    public static DifferentialScalar operator -(DifferentialScalar a) => Combine(-a.Value, a, -1, 0, 0);
    public static DifferentialScalar operator *(DifferentialScalar a, DifferentialScalar b) => Combine(a.Value * b.Value, a, b.Value, b, a.Value);
    public static DifferentialScalar operator /(DifferentialScalar a, DifferentialScalar b) => Combine(a.Value / b.Value, a, 1 / b.Value, b, -a.Value / (b.Value * b.Value));
    public static DifferentialScalar Sqrt(DifferentialScalar a)
    {
        // A genuinely constant zero is differentiable. A zero carrying tangents is singular.
        if (a.Value == 0 && (a._gradient is null || a._gradient.All(value => value == 0))) return a;
        var root = Math.Sqrt(a.Value);
        return Combine(root, a, .5 / root, 0, 0);
    }
    public static DifferentialScalar Abs(DifferentialScalar a) => a.Value < 0 ? -a : a;
    public static DifferentialScalar PositivePart(DifferentialScalar a)
    {
        if (!a.IsFinite) throw new ArithmeticException("An unavailable automatic tangent cannot become an inactive zero penalty.");
        return a.Value > 0 ? a : 0;
    }
    public static DifferentialScalar Square(DifferentialScalar a) => a * a;
}

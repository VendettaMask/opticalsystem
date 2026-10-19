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
    public static DifferentialScalar operator /(DifferentialScalar a, DifferentialScalar b)
    {
        if (a.Dimension != 0 && b.Dimension != 0 && a.Dimension != b.Dimension)
            throw new ArgumentException("Automatic differentiation dimensions differ.");
        var value = a.Value / b.Value;
        var dimension = Math.Max(a.Dimension, b.Dimension);
        if (dimension == 0) return value;
        var squared = b.Value * b.Value;
        var da = 1 / b.Value;
        var db = -a.Value / squared;
        var ordinary = double.IsFinite(squared) && squared != 0 && !double.IsSubnormal(squared)
            && double.IsFinite(da) && double.IsFinite(db) && (a.Value == 0 || db != 0);
        var gradient = new double[dimension];
        for (var index = 0; index < dimension; index++)
        {
            var first = a.Derivative(index);
            var second = b.Derivative(index);
            var left = ordinary ? da * first : double.NaN;
            var right = ordinary ? db * second : double.NaN;
            var tangent = left + right;
            var lostRange = double.IsSubnormal(left) || double.IsSubnormal(right)
                || left == 0 && da != 0 && first != 0 || right == 0 && db != 0 && second != 0;
            gradient[index] = double.IsFinite(tangent) && !lostRange ? tangent
                : QuotientTangent(a.Value, b.Value, first, second);
        }
        return new(value, gradient);
    }

    private static double QuotientTangent(double a, double b, double da, double db)
    {
        if (!double.IsFinite(a) || !double.IsFinite(b) || b == 0 || !double.IsFinite(da) || !double.IsFinite(db))
            return da / b - (a / b) * (db / b);
        // Form (da*b - a*db)/b² in binary-normalized coordinates. Neither
        // product nor b² need be representable in the original units, and a/b
        // may itself have underflowed even though its tangent is finite.
        var eb = Math.ILogB(b);
        var mb = Math.ScaleB(b, -eb);
        var leftExponent = da == 0 ? int.MinValue : Math.ILogB(da) + eb;
        var rightExponent = a == 0 || db == 0 ? int.MinValue : Math.ILogB(a) + Math.ILogB(db);
        var exponent = Math.Max(leftExponent, rightExponent);
        if (exponent == int.MinValue) return 0;
        double left = 0, right = 0, leftError = 0, rightError = 0;
        if (leftExponent != int.MinValue)
        {
            var md = Math.ScaleB(da, -Math.ILogB(da));
            var product = md * mb;
            left = Math.ScaleB(product, leftExponent - exponent);
            leftError = Math.ScaleB(Math.FusedMultiplyAdd(md, mb, -product), leftExponent - exponent);
        }
        if (rightExponent != int.MinValue)
        {
            var ma = Math.ScaleB(a, -Math.ILogB(a));
            var md = Math.ScaleB(db, -Math.ILogB(db));
            var product = ma * md;
            right = Math.ScaleB(product, rightExponent - exponent);
            rightError = Math.ScaleB(Math.FusedMultiplyAdd(ma, md, -product), rightExponent - exponent);
        }
        return Math.ScaleB(((left - right) + (leftError - rightError)) / (mb * mb), exponent - 2 * eb);
    }
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

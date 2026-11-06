using OpticalSystemDesign.Core.Analysis;

namespace OpticalSystemDesign.Core.Tolerancing;

/// <summary>A local Standard Zernike realization of the ISO irregularity magnitude.</summary>
internal sealed record IsoSurfaceFigure((int N, int M)[] Orders, double[] Coefficients, double Piston, double[]? Envelope = null)
{
    private static readonly (int N, int M)[] TotalOrders = ZernikeFitEngine.StandardIndices(37)
        .Skip(4).Select(order => (order.N, order.M)).ToArray();
    private static readonly (int N, int M)[] RadialOrders = [(4, 0), (6, 0), (8, 0), (10, 0)];
    private static readonly (int N, int M)[][] AngularFamilies = TotalOrders
        .Where(order => order.M >= 0).GroupBy(order => order.M)
        .OrderBy(group => group.Key).Select(group => group.ToArray()).ToArray();

    internal static IsoSurfaceFigure Create(SurfaceFormMode mode, double magnitude, Random? random, bool nonRotating = false)
    {
        var orders = mode switch
        {
            SurfaceFormMode.IsoRotationalIrregularityPv => RadialOrders,
            SurfaceFormMode.IsoIrregularityPv when nonRotating => AngularFamilies[random?.Next(1, AngularFamilies.Length) ?? 2],
            SurfaceFormMode.IsoIrregularityPv => AngularFamilies[random?.Next(AngularFamilies.Length) ?? 2],
            _ => TotalOrders
        };
        var coefficients = new double[orders.Length];
        for (var i = 0; i < coefficients.Length; i++)
            coefficients[i] = random is null ? 1 : 2 * random.NextDouble() - 1;
        if (coefficients.All(value => value == 0)) coefficients[0] = 1;

        // D specifies the RMS after subtracting the aperture mean. The constant
        // added below places the sag datum at the vertex and leaves that RMS intact.
        var measure = mode == SurfaceFormMode.IsoIrregularityRms
            ? Math.Sqrt(coefficients.Sum(value => value * value))
            : PeakToValley(orders, coefficients);
        var piston = 0.0;
        for (var i = 0; i < orders.Length; i++)
        {
            var (n, m) = orders[i];
            var coefficient = coefficients[i] / measure * magnitude * ZernikeFitEngine.StandardNormalization(n, m);
            coefficients[i] = coefficient;
            if (m == 0) piston -= (n / 2 % 2 == 0 ? coefficient : -coefficient);
        }

        // B uses one angular frequency per realization. A common phase for its
        // cosine/sine terms keeps the continuous angular extrema analytic.
        if (mode == SurfaceFormMode.IsoIrregularityPv && orders[0].M > 0 && random is not null)
        {
            var (sine, cosine) = Math.SinCos(2 * Math.PI * random.NextDouble());
            var phasedOrders = new (int N, int M)[2 * orders.Length];
            var phasedCoefficients = new double[phasedOrders.Length];
            for (var i = 0; i < orders.Length; i++)
            {
                var (n, m) = orders[i];
                phasedOrders[2 * i] = (n, m);
                phasedOrders[2 * i + 1] = (n, -m);
                phasedCoefficients[2 * i] = cosine * coefficients[i];
                phasedCoefficients[2 * i + 1] = sine * coefficients[i];
            }
            return new(phasedOrders, phasedCoefficients, piston, nonRotating ? RadialPolynomial(orders, coefficients, normalized: true) : null);
        }
        return new(orders, coefficients, piston, nonRotating ? RadialPolynomial(orders, coefficients, normalized: true) : null);
    }

    internal static IsoSurfaceFigure CreateJoint(double total, double rotational, Random random)
    {
        if (!(total > 0) || rotational < 0 || total < rotational)
            throw new ArgumentOutOfRangeException(nameof(rotational));
        // Solve in units of the total P-V to keep the calibration scale independent.
        var symmetric = Create(SurfaceFormMode.IsoRotationalIrregularityPv, rotational / total, random);
        var asymmetric = Create(SurfaceFormMode.IsoIrregularityPv, 1, random, nonRotating: true);
        var c = RadialPolynomial(symmetric.Orders, symmetric.Coefficients, normalized: true);
        var b = asymmetric.Envelope!;
        var angularOrder = asymmetric.Orders[0].M;
        var degree = Math.Max(2 * (c.Length - 1), 2 * (b.Length - 1) + angularOrder);
        var positive = new double[degree + 1];
        var negative = new double[degree + 1];
        for (var i = 0; i < c.Length; i++)
            positive[2 * i] = negative[2 * i] = c[i];

        // At each radius the angular extrema are the two branches C ± r^m Q(r²).
        // The combined P-V grows monotonically with the non-rotational amplitude.
        var lower = 0.0;
        var upper = rotational == total ? 0.0 : 1.0;
        for (var iteration = 0; iteration < 42 && upper > lower; iteration++)
        {
            var scale = (lower + upper) / 2;
            for (var i = 0; i < b.Length; i++)
            {
                var power = 2 * i + angularOrder;
                var offset = c.Length > power / 2 && power % 2 == 0 ? c[power / 2] : 0;
                positive[power] = offset + scale * b[i];
                negative[power] = offset - scale * b[i];
            }
            var plus = PolynomialRange(positive);
            var minus = PolynomialRange(negative);
            var pv = Math.Max(plus.Maximum, minus.Maximum) - Math.Min(plus.Minimum, minus.Minimum);
            if (pv <= 1) lower = scale;
            else upper = scale;
        }

        return new([.. symmetric.Orders, .. asymmetric.Orders],
            [.. symmetric.Coefficients.Select(value => value * total),
             .. asymmetric.Coefficients.Select(value => value * (lower * total))],
            symmetric.Piston * total);
    }

    private static (double Minimum, double Maximum) PolynomialRange(double[] polynomial)
    {
        var derivative = new double[polynomial.Length - 1];
        for (var i = 0; i < derivative.Length; i++) derivative[i] = (i + 1) * polynomial[i + 1];

        var minimum = Math.Min(polynomial[0], Evaluate(polynomial, 1));
        var maximum = Math.Max(polynomial[0], Evaluate(polynomial, 1));
        foreach (var r in RootCandidates(derivative))
        {
            var value = Evaluate(polynomial, r);
            minimum = Math.Min(minimum, value);
            maximum = Math.Max(maximum, value);
        }
        return (minimum, maximum);
    }

    private static double PeakToValley((int N, int M)[] orders, double[] coefficients) =>
        RadialPeakToValley(RadialPolynomial(orders, coefficients, normalized: false), orders[0].M);

    private static double[] RadialPolynomial((int N, int M)[] orders, double[] coefficients, bool normalized)
    {
        var m = orders[0].M;
        var polynomial = new double[(orders[^1].N - m) / 2 + 1];
        for (var i = 0; i < orders.Length; i++)
        {
            var n = orders[i].N;
            var radialOrder = (n - m) / 2;
            var scale = normalized ? coefficients[i] : coefficients[i] * ZernikeFitEngine.StandardNormalization(n, m);
            // R_n^m(r) = r^m Q(r²). The monomial coefficients of Q are small
            // integers for the ISO term limits used here.
            for (var k = 0; k <= radialOrder; k++)
            {
                var degree = radialOrder - k;
                var multiplier = Factorial(n - k)
                    / (Factorial(k) * Factorial((n + m) / 2 - k) * Factorial(degree));
                polynomial[degree] += (k % 2 == 0 ? scale : -scale) * multiplier;
            }
        }
        return polynomial;
    }

    internal static double RadialPeakToValley(double[] polynomial, int angularOrder)
    {
        // The interior extrema of t^(m/2) Q(t) are the roots of
        // t Q'(t) + (m/2) Q(t). For m = 0 the root at t = 0 is already an endpoint.
        var derivative = new double[polynomial.Length];
        for (var power = 0; power < polynomial.Length; power++)
            derivative[power] = (power + angularOrder / 2.0) * polynomial[power];

        var minimum = double.PositiveInfinity;
        var maximum = double.NegativeInfinity;
        foreach (var t in RootCandidates(derivative).Prepend(0).Append(1))
        {
            var value = Evaluate(polynomial, t);
            if (angularOrder != 0) value *= Math.Pow(t, angularOrder / 2.0);
            minimum = Math.Min(minimum, value);
            maximum = Math.Max(maximum, value);
        }
        return angularOrder == 0 ? maximum - minimum : 2 * Math.Max(-minimum, maximum);
    }

    private static IReadOnlyList<double> RootCandidates(double[] polynomial)
    {
        var degree = polynomial.Length - 1;
        while (degree > 0 && polynomial[degree] == 0) degree--;
        if (degree == 0) return [];
        if (degree == 1)
        {
            var root = -polynomial[0] / polynomial[1];
            return root is > 0 and < 1 ? [root] : [];
        }

        var derivative = new double[degree];
        for (var i = 1; i <= degree; i++) derivative[i - 1] = i * polynomial[i];

        var candidates = new List<double>();
        var lower = 0.0;
        var lowerValue = Evaluate(polynomial, lower);
        foreach (var upper in RootCandidates(derivative).Append(1))
        {
            var upperValue = Evaluate(polynomial, upper);
            if (lowerValue != 0 && upperValue != 0 && Math.Sign(lowerValue) != Math.Sign(upperValue))
            {
                var left = lower;
                var right = upper;
                for (var iteration = 0; iteration < 54; iteration++)
                {
                    var middle = (left + right) / 2;
                    var value = Evaluate(polynomial, middle);
                    if (value == 0)
                    {
                        left = right = middle;
                        break;
                    }
                    if (Math.Sign(value) == Math.Sign(lowerValue)) left = middle;
                    else right = middle;
                }
                var root = (left + right) / 2;
                if (root > lower && root < upper) candidates.Add(root);
            }
            // Retain critical points as well, including even-multiplicity roots.
            // Extra evaluation points are inside the interval and cannot enlarge the true range.
            if (upper < 1) candidates.Add(upper);
            lower = upper;
            lowerValue = upperValue;
        }
        return candidates;
    }

    private static double Evaluate(double[] polynomial, double t)
    {
        var value = 0.0;
        for (var i = polynomial.Length - 1; i >= 0; i--)
            value = Math.FusedMultiplyAdd(value, t, polynomial[i]);
        return value;
    }

    private static int Factorial(int n)
    {
        var value = 1;
        for (var i = 2; i <= n; i++) value *= i;
        return value;
    }
}

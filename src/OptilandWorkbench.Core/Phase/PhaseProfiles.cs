namespace OptilandWorkbench.Core.Phase;

public interface IPhaseProfile
{
    string Kind { get; }

    double Efficiency { get; }

    double Phase(double x, double y, double wavelengthNanometers);

    (double Dx, double Dy) Gradient(double x, double y, double wavelengthNanometers);

    double ParaxialGradient(double y, double wavelengthNanometers);

    IPhaseProfile Clone();
}

public sealed class ConstantPhaseProfile : IPhaseProfile
{
    public ConstantPhaseProfile(double phase = 0)
    {
        PhaseValue = phase;
    }

    public string Kind => "constant";

    public double Efficiency => 1;

    public double PhaseValue { get; }

    public double Phase(double x, double y, double wavelengthNanometers) => PhaseValue;

    public (double Dx, double Dy) Gradient(double x, double y, double wavelengthNanometers) => (0, 0);

    public double ParaxialGradient(double y, double wavelengthNanometers) => 0;

    public IPhaseProfile Clone() => new ConstantPhaseProfile(PhaseValue);
}

public sealed class LinearGratingPhaseProfile : IPhaseProfile
{
    public LinearGratingPhaseProfile(
        double period,
        double angle = 0,
        int order = 1,
        double efficiency = 1)
    {
        if (!double.IsFinite(period) || period <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(period), "Period must be positive and finite.");
        }

        if (!double.IsFinite(efficiency) || efficiency < 0 || efficiency > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(efficiency), "Efficiency must be finite and in [0, 1].");
        }

        Period = period;
        Angle = angle;
        Order = order;
        Efficiency = efficiency;
    }

    public string Kind => "linear_grating";

    public double Period { get; }

    public double Angle { get; }

    public int Order { get; }

    public double Efficiency { get; }

    public double Phase(double x, double y, double wavelengthNanometers)
    {
        var (dx, dy) = WaveVector();
        return (dx * x) + (dy * y);
    }

    public (double Dx, double Dy) Gradient(double x, double y, double wavelengthNanometers)
    {
        return WaveVector();
    }

    public double ParaxialGradient(double y, double wavelengthNanometers)
    {
        return WaveVector().Dy;
    }

    public IPhaseProfile Clone() => new LinearGratingPhaseProfile(Period, Angle, Order, Efficiency);

    private (double Dx, double Dy) WaveVector()
    {
        var magnitude = Order * 2 * Math.PI / Period;
        return (magnitude * Math.Cos(Angle), magnitude * Math.Sin(Angle));
    }
}

public sealed class RadialPhaseProfile : IPhaseProfile
{
    public RadialPhaseProfile(IEnumerable<double> coefficients)
    {
        ArgumentNullException.ThrowIfNull(coefficients);
        Coefficients = Array.AsReadOnly(coefficients.ToArray());
    }

    public string Kind => "radial";

    public double Efficiency => 1;

    public IReadOnlyList<double> Coefficients { get; }

    public double Phase(double x, double y, double wavelengthNanometers)
    {
        var radiusSquared = (x * x) + (y * y);
        var value = 0.0;
        for (var index = 0; index < Coefficients.Count; index++)
        {
            value += Coefficients[index] * Math.Pow(radiusSquared, index + 1);
        }

        return value;
    }

    public (double Dx, double Dy) Gradient(double x, double y, double wavelengthNanometers)
    {
        var radiusSquared = (x * x) + (y * y);
        var radialFactor = 0.0;
        for (var index = 0; index < Coefficients.Count; index++)
        {
            var power = index + 1;
            radialFactor += 2 * power * Coefficients[index] * Math.Pow(radiusSquared, power - 1);
        }

        return (radialFactor * x, radialFactor * y);
    }

    public double ParaxialGradient(double y, double wavelengthNanometers)
    {
        return Gradient(0, y, wavelengthNanometers).Dy;
    }

    public IPhaseProfile Clone() => new RadialPhaseProfile(Coefficients);
}

public sealed class GridPhaseProfile : IPhaseProfile
{
    private readonly double[,] _phaseGrid;

    public GridPhaseProfile(
        IEnumerable<double> xCoordinates,
        IEnumerable<double> yCoordinates,
        double[,] phaseGrid)
    {
        ArgumentNullException.ThrowIfNull(xCoordinates);
        ArgumentNullException.ThrowIfNull(yCoordinates);
        ArgumentNullException.ThrowIfNull(phaseGrid);
        XCoordinates = Array.AsReadOnly(xCoordinates.ToArray());
        YCoordinates = Array.AsReadOnly(yCoordinates.ToArray());
        if (XCoordinates.Count < 4 || YCoordinates.Count < 4)
        {
            throw new ArgumentException("Grid phase profiles require at least four coordinates per axis.");
        }

        ValidateIncreasing(XCoordinates, nameof(xCoordinates));
        ValidateIncreasing(YCoordinates, nameof(yCoordinates));
        if (phaseGrid.GetLength(0) != YCoordinates.Count || phaseGrid.GetLength(1) != XCoordinates.Count)
        {
            throw new ArgumentException("Phase grid shape must be [yCoordinates, xCoordinates].", nameof(phaseGrid));
        }

        _phaseGrid = (double[,])phaseGrid.Clone();
    }

    public string Kind => "grid";

    public double Efficiency => 1;

    public IReadOnlyList<double> XCoordinates { get; }

    public IReadOnlyList<double> YCoordinates { get; }

    public double[,] PhaseGrid => (double[,])_phaseGrid.Clone();

    public double Phase(double x, double y, double wavelengthNanometers)
    {
        var values = EvaluateRows(x, derivative: false);
        return new NotAKnotCubicSpline(YCoordinates, values).Evaluate(y);
    }

    public (double Dx, double Dy) Gradient(double x, double y, double wavelengthNanometers)
    {
        var xDerivatives = EvaluateRows(x, derivative: true);
        var dx = new NotAKnotCubicSpline(YCoordinates, xDerivatives).Evaluate(y);
        var rowValues = EvaluateRows(x, derivative: false);
        var dy = new NotAKnotCubicSpline(YCoordinates, rowValues).Derivative(y);
        return (dx, dy);
    }

    public double ParaxialGradient(double y, double wavelengthNanometers)
    {
        return Gradient(0, y, wavelengthNanometers).Dy;
    }

    public IPhaseProfile Clone() => new GridPhaseProfile(XCoordinates, YCoordinates, _phaseGrid);

    private double[] EvaluateRows(double x, bool derivative)
    {
        var output = new double[YCoordinates.Count];
        var row = new double[XCoordinates.Count];
        for (var yIndex = 0; yIndex < YCoordinates.Count; yIndex++)
        {
            for (var xIndex = 0; xIndex < XCoordinates.Count; xIndex++)
            {
                row[xIndex] = _phaseGrid[yIndex, xIndex];
            }

            var spline = new NotAKnotCubicSpline(XCoordinates, row);
            output[yIndex] = derivative ? spline.Derivative(x) : spline.Evaluate(x);
        }

        return output;
    }

    private static void ValidateIncreasing(IReadOnlyList<double> values, string parameterName)
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (!double.IsFinite(values[index]) || (index > 0 && values[index] <= values[index - 1]))
            {
                throw new ArgumentException("Grid coordinates must be finite and strictly increasing.", parameterName);
            }
        }
    }
}

public sealed class PolynomialPhaseProfile : IPhaseProfile
{
    public PolynomialPhaseProfile(IReadOnlyDictionary<(int X, int Y), double> coefficients)
    {
        ArgumentNullException.ThrowIfNull(coefficients);
        Coefficients =
            new System.Collections.ObjectModel.ReadOnlyDictionary<(int X, int Y), double>(
                new Dictionary<(int X, int Y), double>(coefficients));
    }

    public string Kind => "polynomial_phase";

    public double Efficiency => 1;

    public IReadOnlyDictionary<(int X, int Y), double> Coefficients { get; }

    public double Phase(double x, double y, double wavelengthNanometers)
    {
        return Coefficients.Sum(term => term.Value * Math.Pow(x, term.Key.X) * Math.Pow(y, term.Key.Y));
    }

    public (double Dx, double Dy) Gradient(double x, double y, double wavelengthNanometers)
    {
        var dx = 0.0;
        var dy = 0.0;
        foreach (var term in Coefficients)
        {
            if (term.Key.X > 0)
            {
                dx += term.Value * term.Key.X * Math.Pow(x, term.Key.X - 1) * Math.Pow(y, term.Key.Y);
            }

            if (term.Key.Y > 0)
            {
                dy += term.Value * term.Key.Y * Math.Pow(x, term.Key.X) * Math.Pow(y, term.Key.Y - 1);
            }
        }

        return (dx, dy);
    }

    public double ParaxialGradient(double y, double wavelengthNanometers)
    {
        return Gradient(0, y, wavelengthNanometers).Dy;
    }

    public IPhaseProfile Clone() => new PolynomialPhaseProfile(Coefficients);
}

internal sealed class NotAKnotCubicSpline
{
    private readonly double[] _x;
    private readonly double[] _values;
    private readonly double[] _secondDerivatives;

    public NotAKnotCubicSpline(IReadOnlyList<double> x, IReadOnlyList<double> values)
    {
        if (x.Count != values.Count || x.Count < 4)
        {
            throw new ArgumentException("Not-a-knot cubic splines require matching arrays with at least four values.");
        }

        _x = x.ToArray();
        _values = values.ToArray();
        _secondDerivatives = SolveSecondDerivatives();
    }

    public double Evaluate(double x)
    {
        var index = Interval(x);
        var width = _x[index + 1] - _x[index];
        var left = _x[index + 1] - x;
        var right = x - _x[index];
        return (_secondDerivatives[index] * left * left * left / (6 * width))
            + (_secondDerivatives[index + 1] * right * right * right / (6 * width))
            + ((_values[index] - (_secondDerivatives[index] * width * width / 6)) * left / width)
            + ((_values[index + 1] - (_secondDerivatives[index + 1] * width * width / 6)) * right / width);
    }

    public double Derivative(double x)
    {
        var index = Interval(x);
        var width = _x[index + 1] - _x[index];
        var left = _x[index + 1] - x;
        var right = x - _x[index];
        return (-_secondDerivatives[index] * left * left / (2 * width))
            + (_secondDerivatives[index + 1] * right * right / (2 * width))
            + ((_values[index + 1] - _values[index]) / width)
            - (width * (_secondDerivatives[index + 1] - _secondDerivatives[index]) / 6);
    }

    private double[] SolveSecondDerivatives()
    {
        var count = _x.Length;
        var matrix = new double[count, count];
        var rightHandSide = new double[count];
        var firstWidth = _x[1] - _x[0];
        var secondWidth = _x[2] - _x[1];
        matrix[0, 0] = -secondWidth;
        matrix[0, 1] = firstWidth + secondWidth;
        matrix[0, 2] = -firstWidth;

        for (var index = 1; index < count - 1; index++)
        {
            var previousWidth = _x[index] - _x[index - 1];
            var nextWidth = _x[index + 1] - _x[index];
            matrix[index, index - 1] = previousWidth;
            matrix[index, index] = 2 * (previousWidth + nextWidth);
            matrix[index, index + 1] = nextWidth;
            rightHandSide[index] = 6 * (((_values[index + 1] - _values[index]) / nextWidth)
                - ((_values[index] - _values[index - 1]) / previousWidth));
        }

        var penultimateWidth = _x[count - 2] - _x[count - 3];
        var lastWidth = _x[count - 1] - _x[count - 2];
        matrix[count - 1, count - 3] = -lastWidth;
        matrix[count - 1, count - 2] = penultimateWidth + lastWidth;
        matrix[count - 1, count - 1] = -penultimateWidth;
        return Solve(matrix, rightHandSide);
    }

    private int Interval(double x)
    {
        if (x <= _x[0])
        {
            return 0;
        }

        if (x >= _x[^1])
        {
            return _x.Length - 2;
        }

        var index = Array.BinarySearch(_x, x);
        return index >= 0 ? Math.Min(index, _x.Length - 2) : ~index - 1;
    }

    private static double[] Solve(double[,] matrix, double[] rightHandSide)
    {
        var count = rightHandSide.Length;
        for (var pivot = 0; pivot < count; pivot++)
        {
            var best = pivot;
            for (var row = pivot + 1; row < count; row++)
            {
                if (Math.Abs(matrix[row, pivot]) > Math.Abs(matrix[best, pivot]))
                {
                    best = row;
                }
            }

            if (Math.Abs(matrix[best, pivot]) < 1e-15)
            {
                throw new InvalidOperationException("Phase grid spline system is singular.");
            }

            if (best != pivot)
            {
                for (var column = pivot; column < count; column++)
                {
                    (matrix[pivot, column], matrix[best, column]) = (matrix[best, column], matrix[pivot, column]);
                }

                (rightHandSide[pivot], rightHandSide[best]) = (rightHandSide[best], rightHandSide[pivot]);
            }

            var scale = matrix[pivot, pivot];
            for (var column = pivot; column < count; column++)
            {
                matrix[pivot, column] /= scale;
            }

            rightHandSide[pivot] /= scale;
            for (var row = 0; row < count; row++)
            {
                if (row == pivot)
                {
                    continue;
                }

                var factor = matrix[row, pivot];
                for (var column = pivot; column < count; column++)
                {
                    matrix[row, column] -= factor * matrix[pivot, column];
                }

                rightHandSide[row] -= factor * rightHandSide[pivot];
            }
        }

        return rightHandSide;
    }
}

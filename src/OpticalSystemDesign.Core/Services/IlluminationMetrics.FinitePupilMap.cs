using OpticalSystemDesign.Core.Analysis;

namespace OpticalSystemDesign.Core.Services;

public static partial class IlluminationMetrics
{
    // The finite native-grid profile maps image cosines to pupil coordinates
    // with a fixed total-degree-six polynomial. Coefficients come exclusively
    // from shared-Core coarse rays, never native values or per-file constants.
    // The general UniformImageCosine mode retains its tightly converged inverse.
    private sealed class FinitePupilMap
    {
        private const int Degree = 6;
        private const int TermCount = (Degree + 1) * (Degree + 2) / 2;
        // This is the observed finite-map rank rule, not an error tolerance or
        // an adjustable gain. Centering/scaling the columns changes the rule.
        private const double RelativeSingularCutoff = 1e-7;
        private readonly double[] _coefficientX;
        private readonly double[] _coefficientY;

        internal FinitePupilMap(IReadOnlyList<Node> nodes)
        {
            if (nodes.Count < TermCount) throw Unavailable("the finite pupil-coordinate map has too few traceable coarse rays");
            var design = new double[nodes.Count, TermCount];
            var targetX = new double[nodes.Count];
            var targetY = new double[nodes.Count];
            Span<double> features = stackalloc double[TermCount];
            for (var row = 0; row < nodes.Count; row++)
            {
                ComputationCancellation.ThrowIfCancellationRequested();
                var node = nodes[row];
                FillFeatures(node.L, node.M, features);
                for (var column = 0; column < TermCount; column++) design[row, column] = features[column];
                targetX[row] = node.X;
                targetY[row] = node.Y;
            }
            try
            {
                var factor = JacobiSvd.Factor(design, RelativeSingularCutoff);
                _coefficientX = JacobiSvd.Solve(factor, targetX);
                _coefficientY = JacobiSvd.Solve(factor, targetY);
            }
            catch (InvalidOperationException exception)
            {
                throw Unavailable("the finite pupil-coordinate map cannot be uniquely resolved: " + exception.Message);
            }
        }

        internal (double X, double Y, double Determinant) Evaluate(double l, double m)
        {
            Span<double> powersL = stackalloc double[Degree + 1];
            Span<double> powersM = stackalloc double[Degree + 1];
            FillPowers(l, powersL);
            FillPowers(m, powersM);
            double x = 0, y = 0, xu = 0, xv = 0, yu = 0, yv = 0;
            var index = 0;
            for (var a = 0; a <= Degree; a++)
                for (var b = 0; b <= Degree - a; b++)
                {
                    var value = powersL[a] * powersM[b];
                    x += _coefficientX[index] * value;
                    y += _coefficientY[index] * value;
                    var u = a == 0 ? 0 : a * powersL[a - 1] * powersM[b];
                    var v = b == 0 ? 0 : b * powersL[a] * powersM[b - 1];
                    xu += _coefficientX[index] * u;
                    xv += _coefficientX[index] * v;
                    yu += _coefficientY[index] * u;
                    yv += _coefficientY[index] * v;
                    index++;
                }
            if (!double.IsFinite(x + y)) throw Unavailable("the finite pupil-coordinate map is not finite");
            return (x, y, xu * yv - xv * yu);
        }

        private static void FillFeatures(double l, double m, Span<double> features)
        {
            Span<double> powersL = stackalloc double[Degree + 1];
            Span<double> powersM = stackalloc double[Degree + 1];
            FillPowers(l, powersL);
            FillPowers(m, powersM);
            var index = 0;
            for (var a = 0; a <= Degree; a++)
                for (var b = 0; b <= Degree - a; b++) features[index++] = powersL[a] * powersM[b];
        }

        private static void FillPowers(double value, Span<double> powers)
        {
            powers[0] = 1;
            for (var index = 1; index <= Degree; index++) powers[index] = powers[index - 1] * value;
        }
    }
}

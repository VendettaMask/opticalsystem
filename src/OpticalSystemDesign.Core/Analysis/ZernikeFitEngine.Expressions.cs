using System.Globalization;

namespace OpticalSystemDesign.Core.Analysis;

public static partial class ZernikeFitEngine
{
    /// <summary>Describes the same radial recurrence and normalization used by the fitter; it does not refit a wavefront.</summary>
    internal static string PolynomialExpression(int n, int m, bool standard, bool annular, double obscuration)
    {
        var absoluteM = Math.Abs(m);
        var coefficients = new double[n + 1];
        var variable = "ρ";
        var prefix = "";
        if (annular)
        {
            TrimAnnularCache();
            var recurrence = AnnularRadialCache.GetOrAdd((n, absoluteM, BitConverter.DoubleToInt64Bits(obscuration)),
                _ => BuildAnnularRecurrence(n, absoluteM, obscuration));
            var previous = new double[(n - absoluteM) / 2 + 1];
            var current = new double[previous.Length];
            current[0] = 1 / recurrence.InitialNorm;
            for (var k = 0; k < recurrence.Alpha.Length; k++)
            {
                var next = new double[current.Length];
                for (var power = 0; power < current.Length; power++)
                {
                    next[power] -= recurrence.Alpha[k] * current[power] + recurrence.Beta[k] * previous[power];
                    if (power > 0) next[power] += current[power - 1];
                    next[power] /= recurrence.Beta[k + 1];
                }
                previous = current; current = next;
            }
            coefficients = current; variable = "u";
            prefix = absoluteM == 0 ? "" : Power("ρ", absoluteM) + " · ";
        }
        else
        {
            for (var k = 0; k <= (n - absoluteM) / 2; k++)
                coefficients[n - 2 * k] = Math.Pow(-1, k) * Factorial(n - k)
                    / (Factorial(k) * Factorial((n + absoluteM) / 2 - k) * Factorial((n - absoluteM) / 2 - k));
            var normalization = m == 0 ? n + 1 : 2 * (n + 1);
            if (standard && normalization != 1)
            {
                var root = Math.Sqrt(normalization);
                prefix = (root == Math.Truncate(root) ? root.ToString("0", CultureInfo.InvariantCulture) : $"√{normalization}") + " · ";
            }
        }
        var terms = new List<string>();
        for (var power = coefficients.Length - 1; power >= 0; power--)
        {
            var value = coefficients[power];
            // Annular recurrences contain roundoff residues in analytically zero coefficients.
            // Omit residues below the same 12-significant-digit display precision, without altering the fitted basis or coefficients.
            if (value == 0 || annular && Math.Abs(value) < coefficients.Max(Math.Abs) * 1e-12) continue;
            var magnitude = Math.Abs(value);
            var coefficient = power > 0 && magnitude == 1 ? "" : magnitude.ToString(annular ? "G12" : "0", CultureInfo.InvariantCulture);
            var term = coefficient + (power > 0 ? Power(variable, power) : "");
            terms.Add((terms.Count == 0 ? value < 0 ? "−" : "" : value < 0 ? " − " : " + ") + term);
        }
        var radial = string.Concat(terms);
        if (terms.Count > 1) radial = "(" + radial + ")";
        var angular = m == 0 ? "" : " · " + (m > 0 ? "cos" : "sin") + "(" + (absoluteM == 1 ? "" : absoluteM.ToString(CultureInfo.InvariantCulture)) + "θ)";
        return prefix + radial + angular;
    }

    private static string Power(string variable, int power) => power == 1 ? variable : variable + "^" + power.ToString(CultureInfo.InvariantCulture);
}

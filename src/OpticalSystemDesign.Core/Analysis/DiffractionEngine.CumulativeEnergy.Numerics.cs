using System.Numerics;

namespace OpticalSystemDesign.Core.Analysis;

public static partial class DiffractionEngine
{
    internal static double CumulativeEnergyRootMeanSquare(IReadOnlyList<double> values)
    {
        // Just four marginal NAs determine an entire FFT grid. Retain their
        // exact binary squares when rounding the RMS: an error of one ULP can
        // add or remove all four circular-pupil boundary nodes.
        if (values.Count != 4 || values.Any(value => !double.IsFinite(value) || value < 0))
            throw new ArgumentException("Four finite nonnegative marginal numerical apertures are required.", nameof(values));
        var scale = values.Max();
        if (scale == 0) return 0;
        var components = values.Select(Binary).ToArray();
        var exponent = components.Min(component => 2 * component.Exponent);
        var sum = components.Aggregate(BigInteger.Zero, (total, component) => total
            + (component.Mantissa * component.Mantissa << (2 * component.Exponent - exponent)));
        var root = scale * Math.Sqrt(values.Average(value => Math.Pow(value / scale, 2)));
        while (AboveMidpoint(root, Math.BitIncrement(root), above: true)) root = Math.BitIncrement(root);
        while (root > 0 && AboveMidpoint(Math.BitDecrement(root), root, above: false)) root = Math.BitDecrement(root);
        return root;

        bool AboveMidpoint(double lower, double upper, bool above)
        {
            var a = Binary(lower);
            var b = Binary(upper);
            var e = Math.Min(a.Exponent, b.Exponent);
            var midpoint = (a.Mantissa << (a.Exponent - e)) + (b.Mantissa << (b.Exponent - e));
            var midpointExponent = 2 * e - 2;
            var right = values.Count * midpoint * midpoint;
            var comparison = exponent >= midpointExponent
                ? (sum << (exponent - midpointExponent)).CompareTo(right)
                : sum.CompareTo(right << (midpointExponent - exponent));
            var current = above ? lower : upper;
            return (above ? comparison > 0 : comparison < 0)
                || comparison == 0 && (BitConverter.DoubleToUInt64Bits(current) & 1) != 0;
        }

        static (BigInteger Mantissa, int Exponent) Binary(double value)
        {
            var bits = BitConverter.DoubleToUInt64Bits(value);
            var biasedExponent = (int)((bits >> 52) & 0x7ff);
            return (new BigInteger((bits & 0xfffffffffffff) | (biasedExponent == 0 ? 0 : 1UL << 52)),
                biasedExponent == 0 ? -1074 : biasedExponent - 1075);
        }
    }
}

using System.Globalization;

namespace OptilandWorkbench.Application.Formatting;

public sealed record NumericDisplayOptions(
    int DecimalPlaces = 6,
    int UpperScientificExponent = 6,
    int LowerScientificExponent = -4)
{
    public NumericDisplayOptions Normalize()
    {
        var decimalPlaces = Math.Clamp(DecimalPlaces, 0, 15);
        var upper = Math.Clamp(UpperScientificExponent, 1, 15);
        var lower = Math.Clamp(LowerScientificExponent, -15, -1);
        return lower < upper
            ? new NumericDisplayOptions(decimalPlaces, upper, lower)
            : new NumericDisplayOptions(decimalPlaces, Math.Max(1, lower + 1), lower);
    }
}

public static class NumericDisplayFormatter
{
    private static NumericDisplayOptions _current = new();

    public static NumericDisplayOptions Current => Volatile.Read(ref _current);

    public static void Configure(NumericDisplayOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Volatile.Write(ref _current, options.Normalize());
    }

    public static string Format(double value, IFormatProvider? provider = null)
    {
        return Format(value, Current, provider);
    }

    public static string Format(
        double value,
        NumericDisplayOptions options,
        IFormatProvider? provider = null)
    {
        if (double.IsNaN(value))
        {
            return "NaN";
        }

        if (double.IsPositiveInfinity(value))
        {
            return "∞";
        }

        if (double.IsNegativeInfinity(value))
        {
            return "-∞";
        }

        var normalized = options.Normalize();
        var exponent = value == 0
            ? 0
            : (int)Math.Floor(Math.Log10(Math.Abs(value)));
        var scientific = value != 0
            && (exponent >= normalized.UpperScientificExponent
                || exponent <= normalized.LowerScientificExponent);
        var decimals = normalized.DecimalPlaces == 0
            ? string.Empty
            : $".{new string('#', normalized.DecimalPlaces)}";
        var format = scientific
            ? $"0{decimals}E+0"
            : $"0{decimals}";
        return value.ToString(format, provider ?? CultureInfo.CurrentCulture);
    }
}

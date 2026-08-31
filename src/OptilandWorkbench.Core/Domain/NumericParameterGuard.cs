namespace OptilandWorkbench.Core.Domain;

internal static class NumericParameterGuard
{
    public static double RequireFinite(double value, string parameterName)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Optical numeric parameters must be finite.");
        }

        return value;
    }

    public static double RequireNotNaN(double value, string parameterName)
    {
        if (double.IsNaN(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Optical numeric parameters cannot be NaN.");
        }

        return value;
    }

    public static double RequireFiniteOrPositiveInfinity(double value, string parameterName)
    {
        if (double.IsFinite(value) || double.IsPositiveInfinity(value))
        {
            return value;
        }

        throw new ArgumentOutOfRangeException(
            parameterName,
            "Optical thickness must be finite, except positive infinity for an infinite object conjugate.");
    }

    public static double ClampMinimumFinite(double value, double minimum, string parameterName)
    {
        RequireFinite(value, parameterName);
        return Math.Max(minimum, value);
    }

    public static double ClampNonNegativeFinite(double value, string parameterName)
    {
        RequireFinite(value, parameterName);
        return Math.Max(0, value);
    }
}

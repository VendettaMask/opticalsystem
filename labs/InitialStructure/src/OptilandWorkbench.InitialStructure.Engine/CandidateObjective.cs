using OptilandWorkbench.InitialStructure.Contracts;

namespace OptilandWorkbench.InitialStructure.Engine;

internal static class CandidateObjective
{
    public static double Evaluate(
        CandidateParameterization parameterization,
        InitialStructureSpecification specification,
        IReadOnlyList<double> vector,
        int rayDensity,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var optic = parameterization.CreateOptic(vector);
            var evaluation = FirstOrderSeedGenerator.EvaluateOptic(
                optic,
                specification,
                rayDensity);
            var violations = FirstOrderSeedGenerator.EvaluateConstraints(
                optic,
                specification,
                evaluation);
            return Score(specification, evaluation, violations);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or ArgumentException
            or ArithmeticException
            or KeyNotFoundException)
        {
            return 1e12;
        }
    }

    public static double Score(
        InitialStructureSpecification specification,
        EvaluationVector evaluation,
        IReadOnlyList<ConstraintViolation> violations)
    {
        static double RelativeSquared(double? actual, double target) =>
            actual is { } value && double.IsFinite(value)
                ? Math.Pow((value - target) / target, 2)
                : 100;
        static double LimitSquared(double? actual, double limit) =>
            actual is { } value && double.IsFinite(value)
                ? Math.Pow(value / limit, 2)
                : 100;

        var score = 8 * RelativeSquared(
            evaluation.EffectiveFocalLengthMillimeters,
            specification.EffectiveFocalLengthMillimeters);
        score += 4 * RelativeSquared(evaluation.FNumber, specification.FNumber);
        score += 20 * Math.Pow(1 - Math.Clamp(evaluation.ValidRayFraction, 0, 1), 2);
        score += LimitSquared(
            evaluation.RmsSpotRadiusMillimeters,
            specification.MaximumRmsSpotRadiusMillimeters);
        score += 0.25 * LimitSquared(
            evaluation.MaximumSpotRadiusMillimeters,
            specification.MaximumSpotRadiusMillimeters);
        score += 100 * violations.Count(item => item.Severity == ConstraintSeverity.Hard);
        score += 10 * violations.Count(item => item.Severity == ConstraintSeverity.Warning);
        return double.IsFinite(score) ? score : 1e12;
    }
}

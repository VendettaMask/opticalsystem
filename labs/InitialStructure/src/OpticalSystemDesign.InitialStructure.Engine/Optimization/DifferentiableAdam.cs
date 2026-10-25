using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.InitialStructure.Contracts;

namespace OpticalSystemDesign.InitialStructure.Engine.Optimization;

/// <summary>Adam on exact caller-supplied automatic tangents; all state is explicit and resumable.</summary>
internal static class DifferentiableAdam
{
    public static AutoLensAdamState Initialize(IReadOnlyList<double> coordinates) => new(0, coordinates.ToArray(), new double[coordinates.Count], new double[coordinates.Count]);

    public static AutoLensAdamState Propose(AutoLensAdamState state, DifferentialScalar objective, IReadOnlyList<double> learningRates)
    {
        Validate(state);
        var dimension = state.Coordinates.Count;
        if (!objective.IsFinite || objective.Dimension != dimension || learningRates.Count != dimension
            || learningRates.Any(rate => !double.IsFinite(rate) || rate < 0))
            throw new ArithmeticException("Adam requires a complete finite automatic gradient and learning-rate vector.");
        var iteration = checked(state.Iteration + 1);
        var momentIteration = checked(state.MomentIteration + 1);
        var first = new double[dimension];
        var second = new double[dimension];
        var point = new double[dimension];
        for (var index = 0; index < dimension; index++)
        {
            var gradient = objective.Derivative(index);
            first[index] = .9 * state.FirstMoment[index] + .1 * gradient;
            second[index] = .999 * state.SecondMoment[index] + .001 * gradient * gradient;
            point[index] = state.Coordinates[index] - learningRates[index] * (first[index] / (1 - Math.Pow(.9, momentIteration)))
                / (Math.Sqrt(second[index] / (1 - Math.Pow(.999, momentIteration))) + 1e-8);
        }
        var result = new AutoLensAdamState(iteration, point, first, second, momentIteration);
        Validate(result);
        return result;
    }

    public static double CosineWarmRestart(int iteration, int plannedIterations)
    {
        if (iteration < 0 || plannedIterations < 1) throw new ArgumentOutOfRangeException(nameof(iteration));
        var period = Math.Max(1, plannedIterations / 4);
        return .5 * (1 + Math.Cos(Math.PI * (iteration % period) / period));
    }

    public static double ReferenceSchedule(int iteration, int plannedIterations, bool curriculum)
    {
        if (curriculum) return CosineWarmRestart(iteration, plannedIterations);
        // LambdaLR applies lambda(0) before the first optimizer.step().
        if (iteration < 100) return iteration / 100.0;
        var progress = (double)(iteration - 100) / Math.Max(1, plannedIterations - 100);
        return .1 + .9 * .5 * (1 + Math.Cos(Math.PI * progress));
    }

    public static AutoLensAdamState ReferenceStep(AutoLensAdamState state, DifferentialScalar objective,
        IReadOnlyList<double> learningRates)
        => ReferenceStep(state, Enumerable.Range(0, state.Coordinates.Count).Select(objective.Derivative).ToArray(), learningRates);

    public static AutoLensAdamState ReferenceStep(AutoLensAdamState state, IReadOnlyList<double> derivatives,
        IReadOnlyList<double> learningRates)
    {
        if (derivatives.Count != state.Coordinates.Count) throw new ArgumentException("Incomplete Adam gradient.");
        // torch.nan_to_num_(grad, nan=0, posinf=0, neginf=0), before ordinary Adam.
        var gradient = Enumerable.Range(0, state.Coordinates.Count).Select(index =>
            double.IsFinite(derivatives[index]) ? derivatives[index] : 0).ToArray();
        DifferentialScalar sanitized = 0;
        for (var index = 0; index < gradient.Length; index++) sanitized += DifferentialScalar.Variable(0, gradient.Length, index) * gradient[index];
        return Propose(state, sanitized, learningRates);
    }

    public static void Validate(AutoLensAdamState state)
    {
        if (state.Iteration is < 0 or > 100000 || state.MomentIteration < 0 || state.MomentIteration > state.Iteration
            || state.Coordinates is not { Count: > 0 and <= 512 }
            || state.FirstMoment?.Count != state.Coordinates.Count || state.SecondMoment?.Count != state.Coordinates.Count
            || state.Coordinates.Any(value => !double.IsFinite(value)) || state.FirstMoment.Any(value => !double.IsFinite(value))
            || state.SecondMoment.Any(value => !double.IsFinite(value) || value < 0)
            || state.MomentIteration == 0 && (state.FirstMoment.Any(value => value != 0) || state.SecondMoment.Any(value => value != 0)))
            throw new InvalidDataException("Invalid differentiable Adam state.");
    }
}

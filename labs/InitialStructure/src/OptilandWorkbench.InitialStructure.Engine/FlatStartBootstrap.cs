using System.Diagnostics;
using OptilandWorkbench.InitialStructure.Contracts;

namespace OptilandWorkbench.InitialStructure.Engine;

/// <summary>Strict zero-curvature startup. This does not perform full-field acceptance.</summary>
public sealed class FlatStartBootstrap
{
    public const double EntrancePupilFraction = 0.3;

    public FlatStartBootstrapResult Solve(InitialStructureSpecification specification, int elementCount,
        CancellationToken cancellationToken = default, FlatStartFamily? family = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var problem = new FlatStartProblem(specification, elementCount, EntrancePupilFraction, family);
        var stopwatch = Stopwatch.StartNew();
        var budget = specification.Budget.MaximumEvaluations;
        var count = 0;
        var vector = problem.InitialVector();
        var evaluation = Evaluate(vector);
        var steps = new List<FlatStartStep> { new(count, "exact-flat-root", problem.Root, evaluation) };
        var diagnostics = new List<SearchDiagnostic>();
        var damping = 1e-3;
        var stalled = 0;
        var state = FlatStartBootstrapState.BudgetExhausted;

        // Always reserve one fresh, denser evaluation; Jacobian probes count as evaluations.
        while (count + 2 * vector.Length + 2 <= budget && !problem.IsFocused(evaluation))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (stopwatch.Elapsed >= specification.Budget.TimeLimit)
            {
                diagnostics.Add(new("bootstrap.time-limit", "The startup time limit was reached."));
                state = FlatStartBootstrapState.TimeLimit;
                break;
            }
            var jacobian = new double[evaluation.Residuals.Count, vector.Length];
            var derivativeValid = true;
            for (var column = 0; column < vector.Length; column++)
            {
                var delta = 1e-5 * Math.Max(1, Math.Abs(vector[column]));
                var plus = (double[])vector.Clone();
                var minus = (double[])vector.Clone();
                plus[column] = problem.Bound(column, vector[column] + delta);
                minus[column] = problem.Bound(column, vector[column] - delta);
                var upper = Evaluate(plus);
                var lower = Evaluate(minus);
                var span = plus[column] - minus[column];
                if (span == 0) continue;
                if (upper.Violations.Count > 0 || lower.Violations.Count > 0
                    || upper.ValidRayFraction != 1 || lower.ValidRayFraction != 1)
                {
                    derivativeValid = false;
                    break;
                }
                for (var row = 0; row < evaluation.Residuals.Count; row++)
                    jacobian[row, column] = (upper.Residuals[row] - lower.Residuals[row]) / span;
            }
            if (!derivativeValid)
            {
                diagnostics.Add(new("bootstrap.derivative-domain", "A finite-difference probe left the valid optical domain."));
                state = FlatStartBootstrapState.Stalled;
                break;
            }
            var direction = DampedStep(jacobian, evaluation.Residuals, damping);
            var largest = direction.Select(Math.Abs).DefaultIfEmpty().Max();
            var stepScale = largest > 0.5 ? 0.5 / largest : 1;
            var trial = vector.Select((value, index) => problem.Bound(index, value + stepScale * direction[index])).ToArray();
            var proposed = Evaluate(trial);
            if (proposed.Violations.Count == 0 && proposed.ValidRayFraction == 1 && proposed.Merit < evaluation.Merit)
            {
                vector = trial;
                evaluation = proposed;
                damping = Math.Max(1e-9, damping / 3);
                stalled = 0;
                steps.Add(new(count, steps.Count == 1 ? "first-curvature-update" : "residual-vector-step",
                    problem.CreateOptic(vector).ToSnapshot(), evaluation));
            }
            else
            {
                damping = Math.Min(1e9, damping * 10);
                if (++stalled >= 12)
                {
                    state = FlatStartBootstrapState.Stalled;
                    diagnostics.Add(new("bootstrap.stalled", "No valid improving step was found in 12 attempts."));
                    break;
                }
            }
        }
        if (count < budget)
        {
            var dense = Evaluate(vector, dense: true);
            steps.Add(new(count, "independent-dense-startup-validation", problem.CreateOptic(vector).ToSnapshot(), dense));
            if (problem.IsFocused(dense)) state = FlatStartBootstrapState.Focused;
        }
        diagnostics.Add(new("bootstrap.scope", "Axial primary-wavelength startup at 30% target pupil; full-field and full-aperture acceptance have not been performed."));
        return new FlatStartBootstrapResult
        {
            Algorithm = family is null ? new("strict-flat-bootstrap", "2", "Managed CPU", true)
                : new("strict-flat-family-bootstrap", "1", "Managed CPU", true),
            Specification = specification,
            SpecificationFingerprint = ContentFingerprint.Compute(specification),
            State = state,
            EntrancePupilFraction = EntrancePupilFraction,
            EvaluationCount = count,
            TracedRayCount = problem.TracedRayCount,
            Steps = steps,
            Diagnostics = diagnostics
        };

        FlatStartEvaluation Evaluate(double[] values, bool dense = false)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (count >= budget) throw new InvalidOperationException("The startup evaluation budget was exceeded.");
            count++;
            return problem.Evaluate(problem.CreateOptic(values), dense, cancellationToken);
        }
    }

    internal static double[] DampedStep(double[,] jacobian, IReadOnlyList<double> residuals, double damping)
    {
        var dimension = jacobian.GetLength(1);
        var matrix = new double[dimension, dimension];
        var target = new double[dimension];
        for (var i = 0; i < dimension; i++)
        {
            for (var row = 0; row < residuals.Count; row++)
            {
                target[i] -= jacobian[row, i] * residuals[row];
                for (var j = 0; j < dimension; j++) matrix[i, j] += jacobian[row, i] * jacobian[row, j];
            }
            matrix[i, i] += damping * Math.Max(matrix[i, i], 1e-8);
        }
        // Positive damping makes this small normal system positive definite.
        var lower = new double[dimension, dimension];
        for (var i = 0; i < dimension; i++)
            for (var j = 0; j <= i; j++)
            {
                var sum = matrix[i, j];
                for (var k = 0; k < j; k++) sum -= lower[i, k] * lower[j, k];
                if (i == j && (!double.IsFinite(sum) || sum <= 0)) throw new ArithmeticException("Startup normal system is not positive definite.");
                lower[i, j] = i == j ? Math.Sqrt(sum) : sum / lower[j, j];
            }
        var result = new double[dimension];
        for (var i = 0; i < dimension; i++)
        {
            var value = target[i];
            for (var j = 0; j < i; j++) value -= lower[i, j] * result[j];
            result[i] = value / lower[i, i];
        }
        for (var i = dimension - 1; i >= 0; i--)
        {
            var value = result[i];
            for (var j = i + 1; j < dimension; j++) value -= lower[j, i] * result[j];
            result[i] = value / lower[i, i];
        }
        return result;
    }
}

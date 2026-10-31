using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine.Optimization;

namespace OpticalSystemDesign.InitialStructure.Engine;

/// <summary>Bounded residual/model arithmetic. Core supplies every value and derivative;
/// physical acceptance, aperture sizes and manufacturing limits are never changed.</summary>
internal sealed partial class TopologyGateCompensation
{
    // Numerical inset for training only; independent physical limits are unchanged.
    private const double QualityTrainingInset = 1e-4;
    private readonly int _version;
    private readonly int[] _hard;
    private readonly int[] _clearance;
    private readonly int[] _quality;
    private readonly int[] _image;

    internal TopologyGateCompensation(IReadOnlyList<string> ids, int version = 1)
    {
        _version = version;
        _hard = Enumerable.Range(0, ids.Count).Where(i => i < 2 || ids[i].StartsWith("geometry:", StringComparison.Ordinal)).ToArray();
        _clearance = Enumerable.Range(0, ids.Count).Where(i => ids[i].EndsWith(":clearance", StringComparison.Ordinal)).ToArray();
        _quality = Enumerable.Range(0, ids.Count).Where(i => ids[i].EndsWith(":rms", StringComparison.Ordinal)
            || ids[i].EndsWith(":maximum", StringComparison.Ordinal) || ids[i].EndsWith(":distortion", StringComparison.Ordinal)).ToArray();
        _image = Enumerable.Range(0, ids.Count).Where(i => ids[i].EndsWith(":x", StringComparison.Ordinal)
            || ids[i].EndsWith(":y", StringComparison.Ordinal)).ToArray();
    }

    private static double Positive(double value) => Math.Max(0, value);
    private static Dictionary<int, TopologyConstraintTangent> Tangents(TopologyObjectiveEvaluation value) =>
        value.ConstraintTangents?.ToDictionary(item => item.Row)
        ?? throw new ArithmeticException("Gate compensation requires complete Core constraint tangents.");


    internal (double Hard, double Clearance, double Quality) Merit(TopologyObjectiveEvaluation value)
    {
        var signed = Tangents(value);
        return (_hard.Sum(row => Math.Pow(row < 2 ? Positive(Math.Abs(signed[row].Value) - 1) : Positive(signed[row].Value), 2)),
            _clearance.Sum(row => Math.Pow(Positive(signed[row].Value), 2)),
            _quality.Sum(row => Math.Pow(_version == 2 ? Positive(signed[row].Value + QualityTrainingInset) : value.Residuals[row], 2))
                + (_version == 2 ? 0 : _image.Sum(row => value.Residuals[row] * value.Residuals[row])));
    }

    internal bool Converged(TopologyObjectiveEvaluation value)
    {
        var signed = Tangents(value);
        return _hard.All(row => row < 2 ? Math.Abs(signed[row].Value) <= 1 : signed[row].Value <= 0)
            && _quality.All(row => signed[row].Value <= (_version == 2 ? -QualityTrainingInset / 2 : 0))
            && _clearance.All(row => signed[row].Value <= (_version == 2 ? 1e-10 : 0))
            && (_version != 2 || !NeedsInwardPolish(value));
    }

    internal bool Accept(TopologyObjectiveEvaluation before, TopologyObjectiveEvaluation after)
    {
        if (!after.Available) return false;
        var a = Tangents(before); var b = Tangents(after);
        // Preserve each already satisfied physical gate, rather than trading away
        // focus/manufacturing feasibility for a smaller aggregate spot objective.
        if (_hard.Any(row => row < 2 ? Positive(Math.Abs(b[row].Value) - 1) > Math.Max(Positive(Math.Abs(a[row].Value) - 1), 1e-10)
            : Positive(b[row].Value) > Math.Max(Positive(a[row].Value), 1e-10))) return false;
        var first = Merit(before); var next = Merit(after);
        if (_version == 2)
        {
            if (NeedsInwardPolish(before))
                return _quality.All(row => b[row].Value <= 0) && next.Clearance < first.Clearance;
            if (first.Clearance > 1e-16)
                // Use the same failed-gate objective as the constrained solve.
                // A separate monotone-clearance filter can reject every useful
                // joint step even though that objective is descending.
                return next.Clearance + next.Quality < first.Clearance + first.Quality;
            if (next.Clearance > 1e-16 || _quality.Any(row => a[row].Value <= 0 && b[row].Value > 1e-10)) return false;
            return next.Quality < first.Quality;
        }
        if (first.Hard > 1e-16) return next.Hard < first.Hard && next.Clearance <= Math.Max(first.Clearance, 1e-16);
        return first.Clearance > 1e-16 ? next.Clearance < first.Clearance
            : next.Clearance <= 1e-16 && next.Quality < first.Quality;
    }

    internal (double[] Coordinates, int Rank) Propose(TopologyObjectiveEvaluation value, IReadOnlyList<double> current,
        BoundTopologyParameters binding, double radiusFraction, CancellationToken token)
    {
        if (_version == 2) return ProposeConstrained(value, current, binding, radiusFraction, token);
        var signed = Tangents(value);
        var merit = Merit(value);
        var restoring = merit.Hard > 1e-16 || merit.Clearance > 1e-16;
        var rows = _hard.Concat(_clearance).Concat(restoring ? [] : _quality.Concat(_image)).ToArray();
        var residuals = new double[rows.Length];
        var jacobian = new double[rows.Length, current.Count];
        for (var i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            // Retain a centered focus/pupil model even inside the admissible band.
            // Otherwise an unconstrained clearance step consumes that band before
            // projection and loses most of its useful tangential direction.
            var weight = row < 2 ? restoring ? 100 : 10 : 1;
            residuals[i] = row < 2 ? weight * signed[row].Value : value.Residuals[row];
            for (var column = 0; column < current.Count; column++)
                jacobian[i, column] = row < 2 ? weight * signed[row].Derivatives[column] : value.Jacobian[row][column];
        }
        var model = new RegularizedLeastSquaresModel(jacobian, residuals);
        var radius = (restoring ? 1 : .25) * radiusFraction;
        var preferred = model.Step(radius, out _);
        var constraints = new List<(double Offset, double[] Normal)>();
        foreach (var row in _hard)
        {
            var item = signed[row];
            if (row < 2)
            {
                var band = Math.Max(.95, Math.Abs(item.Value));
                constraints.Add((item.Value - band, item.Derivatives.ToArray()));
                constraints.Add((-item.Value - band, item.Derivatives.Select(v => -v).ToArray()));
            }
            else constraints.Add((Math.Min(item.Value, 0), item.Derivatives.ToArray()));
        }
        if (!restoring)
            foreach (var row in _clearance)
                constraints.Add((Math.Min(signed[row].Value, 0), signed[row].Derivatives.ToArray()));
        for (var column = 0; column < current.Count; column++)
        {
            var positive = new double[current.Count]; positive[column] = 1;
            var negative = new double[current.Count]; negative[column] = -1;
            constraints.Add((-Math.Min(radius, binding.Slots[column].UpperCoordinate - current[column]), positive));
            constraints.Add((-Math.Min(radius, current[column] - binding.Slots[column].LowerCoordinate), negative));
        }
        // Recovery trades ray deficits rather than requiring every infeasible row
        // to improve simultaneously. Image compensation starts only after clearance
        // is feasible and preserves it; dense original checks still decide success.
        var projected = LinearFeasibilityProjection.Solve(constraints, current.Count, 512, token, preferred, 1e-8);
        if (projected is null) return (current.ToArray(), model.Rank);
        return (current.Select((coordinate, column) => Math.Clamp(coordinate + projected[column],
            binding.Slots[column].LowerCoordinate, binding.Slots[column].UpperCoordinate)).ToArray(), model.Rank);
    }
}

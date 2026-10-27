using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine.Optimization;

namespace OpticalSystemDesign.InitialStructure.Engine;

/// <summary>Residual/model arithmetic only. All optical values and tangents are supplied by Core through the fixed objective.</summary>
internal sealed class TopologyTransmissionCompensation
{
    private readonly int[] _constraints;
    private readonly int[] _clearance;
    private readonly int[] _other;
    private readonly HashSet<int> _rms;
    private readonly bool _jointRestoration;

    internal TopologyTransmissionCompensation(IReadOnlyList<string> ids, int version = 3)
    {
        _jointRestoration = version == 4;
        _constraints = Enumerable.Range(0, ids.Count).Where(row => !ids[row].EndsWith(":x", StringComparison.Ordinal)
            && !ids[row].EndsWith(":y", StringComparison.Ordinal)).ToArray();
        _clearance = _constraints.Where(row => ids[row].EndsWith(":clearance", StringComparison.Ordinal)).ToArray();
        _other = _constraints.Except(_clearance).ToArray();
        _rms = _constraints.Where(row => ids[row].EndsWith(":rms", StringComparison.Ordinal)).ToHashSet();
    }

    internal (double Clearance, double Other) Merit(TopologyObjectiveEvaluation value)
    {
        var clearance = _clearance.Sum(row => value.Residuals[row] * value.Residuals[row]);
        var raw = value.ConstraintTangents?.ToDictionary(item => item.Row);
        var other = _other.Sum(row =>
        {
            // The power/inverse-F anchors are signed target offsets in units of the specified tolerance.
            // Keep a small interior guard; independent physical acceptance retains the exact original limits.
            var residual = row < 2 ? Math.Max(0, Math.Abs(value.Residuals[row]) - .95)
                : _rms.Contains(row) && raw is not null ? Math.Max(0, raw[row].Value + .02) : value.Residuals[row];
            return residual * residual;
        });
        return (clearance, other);
    }

    internal bool Feasible(TopologyObjectiveEvaluation value)
    {
        var merit = Merit(value);
        return merit.Clearance <= 1e-18 && merit.Other <= 1e-18;
    }

    internal bool ValidTangents(TopologyObjectiveEvaluation value, int dimension) => value.ConstraintTangents is { } signed
        && signed.Select(item => item.Row).SequenceEqual(_constraints)
        && signed.All(item => double.IsFinite(item.Value) && item.Derivatives.Length == dimension && item.Derivatives.All(double.IsFinite));

    internal bool Accept(TopologyObjectiveEvaluation before, TopologyObjectiveEvaluation after)
    {
        if (!after.Available) return false;
        if (_jointRestoration && _constraints.Any(row => Violation(after, row) > Math.Max(Violation(before, row), 1e-8))) return false;
        var a = Merit(before); var b = Merit(after);
        return a.Other > 1e-16
            ? b.Other < a.Other && b.Clearance <= Math.Max(a.Clearance, 1e-18)
            : b.Other <= 1e-16 && b.Clearance < a.Clearance;
    }

    private double Violation(TopologyObjectiveEvaluation value, int row)
        => row < 2 ? Math.Max(0, Math.Abs(value.Residuals[row]) - .95)
            : _rms.Contains(row) && value.ConstraintTangents is { } signed
                ? Math.Max(0, signed.Single(item => item.Row == row).Value + .02) : value.Residuals[row];

    internal int[] Select(TopologyObjectiveEvaluation value, int count)
    {
        var dimension = value.Gradient.Length;
        if (count >= dimension) return Enumerable.Range(0, dimension).ToArray();
        var active = _clearance.Where(row => value.Residuals[row] > 1e-10).ToArray();
        var first = Enumerable.Range(0, dimension).OrderByDescending(column => active.Sum(row =>
            value.Jacobian[row][column] * value.Jacobian[row][column])).ThenBy(column => column).First();
        if (active.Length == 0) first = Enumerable.Range(0, dimension).OrderByDescending(column => Math.Abs(value.Gradient[column])).ThenBy(column => column).First();
        var selected = new List<int> { first };
        var raw = value.ConstraintTangents?.ToDictionary(item => item.Row);
        var vectors = Enumerable.Range(0, dimension).Select(column => _constraints.Select(row => raw is null
            ? value.Jacobian[row][column] : raw[row].Derivatives[column]).ToArray()).ToArray();
        var basis = new List<double[]>();
        while (selected.Count < count)
        {
            var pivot = vectors[selected[^1]].ToArray();
            foreach (var previous in basis) SubtractProjection(pivot, previous);
            var norm = Math.Sqrt(pivot.Sum(item => item * item));
            if (norm > 1e-20) basis.Add(pivot.Select(item => item / norm).ToArray());
            var next = Enumerable.Range(0, dimension).Where(column => !selected.Contains(column)).Select(column =>
            {
                var remainder = vectors[column].ToArray();
                foreach (var previous in basis) SubtractProjection(remainder, previous);
                return (Column: column, Score: remainder.Sum(item => item * item));
            }).OrderByDescending(item => item.Score).ThenBy(item => item.Column).First();
            selected.Add(next.Column);
        }
        return selected.ToArray();
    }

    internal (double[] Coordinates, int Rank) Propose(TopologyObjectiveEvaluation value, IReadOnlyList<double> current,
        BoundTopologyParameters binding, IReadOnlyList<int> active, double stepFraction, CancellationToken cancellationToken = default)
    {
        const double radius = 1;
        if (value.ConstraintTangents is not { } tangents) throw new ArithmeticException("Signed constraint tangents are required for transmission compensation.");
        foreach (var fraction in new[] { 1.05, .75, .5, .25, .1 })
        {
            if (_jointRestoration) cancellationToken.ThrowIfCancellationRequested();
            var constraints = new List<(double Offset, double[] Normal)>();
            foreach (var item in tangents)
            {
                var normal = active.Select(column => item.Derivatives[column]).ToArray();
                if (item.Row < 2)
                {
                    constraints.Add((item.Value - .9, normal));
                    constraints.Add((-item.Value - .9, normal.Select(entry => -entry).ToArray()));
                }
                else
                {
                    var offset = item.Value + (_rms.Contains(item.Row) ? .02 : 0);
                    if ((_jointRestoration || _clearance.Contains(item.Row)) && offset > 0) offset *= fraction;
                    else if (offset < 0) offset *= .9; // Stay inside satisfied constraints in the linear prediction.
                    constraints.Add((offset, normal));
                }
            }
            for (var column = 0; column < active.Count; column++)
            {
                var index = active[column];
                var positive = new double[active.Count]; positive[column] = 1;
                var negative = new double[active.Count]; negative[column] = -1;
                constraints.Add((-Math.Min(radius, binding.Slots[index].UpperCoordinate - current[index]), positive));
                constraints.Add((-Math.Min(radius, current[index] - binding.Slots[index].LowerCoordinate), negative));
            }
            if (LinearFeasibilityProjection.Solve(constraints, active.Count, _jointRestoration ? 4096 : 400,
                _jointRestoration ? cancellationToken : default) is not { } projected) continue;
            var candidate = current.ToArray();
            for (var index = 0; index < active.Count; index++) candidate[active[index]] = Math.Clamp(candidate[active[index]] + stepFraction * projected[index],
                binding.Slots[active[index]].LowerCoordinate, binding.Slots[active[index]].UpperCoordinate);
            var matrix = new double[tangents.Length, active.Count];
            for (var row = 0; row < tangents.Length; row++)
                for (var column = 0; column < active.Count; column++) matrix[row, column] = tangents[row].Derivatives[active[column]];
            return (candidate, new RegularizedLeastSquaresModel(matrix, new double[tangents.Length]).Rank);
        }
        // An infeasible linearization can still make partial progress; the nonlinear gate filter remains authoritative.
        var jacobian = new double[_constraints.Length, active.Count];
        var residuals = new double[_constraints.Length];
        for (var row = 0; row < _constraints.Length; row++)
        {
            var original = _constraints[row];
            residuals[row] = value.Residuals[original] * (_clearance.Contains(original) ? 1.05 : 1);
            for (var column = 0; column < active.Count; column++) jacobian[row, column] = value.Jacobian[original][active[column]];
        }
        var model = new RegularizedLeastSquaresModel(jacobian, residuals);
        var step = model.Step(radius, out _);
        var coordinates = current.ToArray();
        for (var index = 0; index < active.Count; index++)
        {
            var slot = active[index];
            coordinates[slot] = Math.Clamp(coordinates[slot] + stepFraction * step[index], binding.Slots[slot].LowerCoordinate, binding.Slots[slot].UpperCoordinate);
        }
        if (coordinates.Any(item => !double.IsFinite(item))) throw new ArithmeticException("Transmission model produced a non-finite bounded step.");
        return (coordinates, model.Rank);
    }

    private static void SubtractProjection(double[] vector, double[] unit)
    {
        var dot = vector.Zip(unit, (a, b) => a * b).Sum();
        for (var index = 0; index < vector.Length; index++) vector[index] -= dot * unit[index];
    }
}

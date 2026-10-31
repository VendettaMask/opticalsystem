using OpticalSystemDesign.Core.Optimization;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine.Optimization;

namespace OpticalSystemDesign.InitialStructure.Engine;

internal sealed partial class TopologyGateCompensation
{
    private const double InwardRadius = 1e-3;
    private const double InwardTarget = 1e-4;

    private bool NeedsInwardPolish(TopologyObjectiveEvaluation value)
    {
        if (_version != 2 || Merit(value).Hard != 0) return false;
        var signed = Tangents(value);
        return _quality.All(row => signed[row].Value <= 0)
            && _clearance.All(row => signed[row].Value <= InwardTarget)
            && _clearance.Any(row => MovingDeficit(signed[row]));
    }

    private static bool MovingDeficit(TopologyConstraintTangent item) => item.Value > 1e-12
        && item.Derivatives.Sum(Math.Abs) * InwardRadius > 64 * (Math.BitIncrement(1.0) - 1);

    private (double[] Coordinates, int Rank) ProposeInwardPolish(TopologyObjectiveEvaluation value,
        IReadOnlyList<double> current, BoundTopologyParameters binding, CancellationToken token)
    {
        using var scope = ComputationCancellation.Push(token);
        var signed = Tangents(value); var rows = _clearance.Where(row => MovingDeficit(signed[row])).ToArray();
        var jacobian = new double[rows.Length, current.Count]; var residuals = new double[rows.Length];
        for (var i = 0; i < rows.Length; i++)
        {
            // A small inward parameter target avoids the LS solver's zero-step
            // scale at a still-clipped rim. Fixed-stop aiming roundoff is excluded.
            residuals[i] = signed[rows[i]].Value + InwardTarget;
            for (var j = 0; j < current.Count; j++) jacobian[i, j] = signed[rows[i]].Derivatives[j];
        }
        var constraints = new List<(double[] Normal, double Upper)>();
        foreach (var row in _hard.Concat(_quality).Concat(_clearance))
        {
            var item = signed[row];
            if (row < 2)
            {
                constraints.Add((item.Derivatives.ToArray(), Math.Max(0, 1 - item.Value)));
                constraints.Add((item.Derivatives.Select(v => -v).ToArray(), Math.Max(0, 1 + item.Value)));
            }
            else if (item.Value <= 0) constraints.Add((item.Derivatives.ToArray(), -item.Value));
        }
        var epsilon = Math.BitIncrement(1.0) - 1;
        constraints.RemoveAll(c => c.Normal.Sum(Math.Abs) * InwardRadius <= 64 * epsilon * Math.Max(1, c.Upper));
        for (var j = 0; j < current.Count; j++)
        {
            var positive = new double[current.Count]; positive[j] = 1; var negative = new double[current.Count]; negative[j] = -1;
            constraints.Add((positive, Math.Max(0, Math.Min(InwardRadius, binding.Slots[j].UpperCoordinate - current[j]))));
            constraints.Add((negative, Math.Max(0, Math.Min(InwardRadius, current[j] - binding.Slots[j].LowerCoordinate))));
        }
        var normals = new double[constraints.Count, current.Count]; var upper = new double[constraints.Count];
        for (var i = 0; i < constraints.Count; i++)
        {
            upper[i] = constraints[i].Upper;
            for (var j = 0; j < current.Count; j++) normals[i, j] = constraints[i].Normal[j];
        }
        InequalityStepResult solved;
        try { solved = InequalityLeastSquaresStep.Solve(jacobian, residuals, normals, upper, .001); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        { throw new ArithmeticException("Core inward inequality step: " + error.Message, error); }
        if (constraints.Any(c => c.Normal.Zip(solved.Step, (a, b) => a * b).Sum() > c.Upper + 1e-8)) return (current.ToArray(), 0);
        return (current.Select((v, j) => Math.Clamp(v + solved.Step[j], binding.Slots[j].LowerCoordinate,
            binding.Slots[j].UpperCoordinate)).ToArray(), new RegularizedLeastSquaresModel(jacobian, residuals).Rank);
    }
}

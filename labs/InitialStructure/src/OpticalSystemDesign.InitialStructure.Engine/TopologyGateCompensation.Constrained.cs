using OpticalSystemDesign.Core.Optimization;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine.Optimization;

namespace OpticalSystemDesign.InitialStructure.Engine;

internal sealed partial class TopologyGateCompensation
{
    internal bool NeedsHardCorrection(TopologyObjectiveEvaluation before, TopologyObjectiveEvaluation after)
    {
        if (_version < 3) return _version == 2 && after.Available && Merit(before).Hard <= 1e-16 && Merit(after).Hard > 1e-16;
        if (!after.Available) return false;
        var a = Tangents(before); var b = Tangents(after);
        return _hard.Any(row => row < 2
            ? Positive(Math.Abs(b[row].Value) - 1) > Math.Max(Positive(Math.Abs(a[row].Value) - 1), 1e-10)
            : Positive(b[row].Value) > Math.Max(Positive(a[row].Value), 1e-10));
    }

    // The signed physical tangent supplies an inward training target, including
    // tiny real clipping. A fixed-stop aiming tangent has no shape dependence.
    private static bool MovingClearance(TopologyConstraintTangent row) =>
        row.Derivatives.Sum(Math.Abs) * InwardRadius > 64 * (Math.BitIncrement(1.0) - 1);

    private static double JointClearance(TopologyConstraintTangent row) =>
        MovingClearance(row) ? row.Value + InwardTarget : Math.Max(0, row.Value);

    private static double JointGeometry(TopologyConstraintTangent row) =>
        MovingClearance(row) ? row.Value + GeometryTrainingInset : 0;

    internal double[] CorrectHard(TopologyObjectiveEvaluation trial, IReadOnlyList<double> coordinates,
        BoundTopologyParameters binding, double radiusFraction, CancellationToken token)
    {
        var signed = Tangents(trial);
        var constraints = new List<(double Offset, double[] Normal)>();
        foreach (var row in _hard)
        {
            var item = signed[row];
            if (row < 2)
            {
                constraints.Add((item.Value - .95, item.Derivatives.ToArray()));
                constraints.Add((-item.Value - .95, item.Derivatives.Select(v => -v).ToArray()));
            }
            else constraints.Add((item.Value + (item.Value > 0 ? 1e-8 : 0), item.Derivatives.ToArray()));
        }
        var radius = .25 * radiusFraction;
        for (var j = 0; j < coordinates.Count; j++)
        {
            var positive = new double[coordinates.Count]; positive[j] = 1;
            var negative = new double[coordinates.Count]; negative[j] = -1;
            constraints.Add((-Math.Min(radius, binding.Slots[j].UpperCoordinate - coordinates[j]), positive));
            constraints.Add((-Math.Min(radius, coordinates[j] - binding.Slots[j].LowerCoordinate), negative));
        }
        var correction = LinearFeasibilityProjection.Solve(constraints, coordinates.Count, 512, token, originalTolerance: 1e-10);
        return correction is null ? coordinates.ToArray() : coordinates.Select((value, j) => Math.Clamp(value + correction[j],
            binding.Slots[j].LowerCoordinate, binding.Slots[j].UpperCoordinate)).ToArray();
    }

    private (double[] Coordinates, int Rank) ProposeConstrained(TopologyObjectiveEvaluation value,
        IReadOnlyList<double> current, BoundTopologyParameters binding, double radiusFraction, CancellationToken token)
    {
        if (NeedsInwardPolish(value)) return ProposeInwardPolish(value, current, binding, token);
        using var cancellation = ComputationCancellation.Push(token);
        var signed = Tangents(value); var merit = Merit(value);
        var radius = .5 * radiusFraction;
        // Repair the actual failed gates first. Minimising average spot size can
        // be stationary while a worst-field RMS/maximum gate remains violated.
        var rows = _clearance.Concat(_quality).Concat(_version >= 4 ? _hard.Where(row => row >= 2) : []).ToArray();
        var imageRows = _version >= 3 ? _image : [];
        var residuals = new double[rows.Length + imageRows.Length]; var jacobian = new double[residuals.Length, current.Count];
        var clearanceRows = _clearance.ToHashSet();
        for (var i = 0; i < rows.Length; i++)
        {
            const double scale = 1;
            var geometry = _version >= 4 && _hard.Contains(rows[i]);
            var quality = !geometry && !clearanceRows.Contains(rows[i]);
            var shifted = geometry ? JointGeometry(signed[rows[i]]) : quality ? signed[rows[i]].Value + QualityInset
                : _version >= 3 ? JointClearance(signed[rows[i]]) : value.Residuals[rows[i]];
            residuals[i] = Positive(shifted) * scale;
            for (var j = 0; j < current.Count; j++) jacobian[i, j] = quality || _version >= 3
                ? shifted > 0 ? signed[rows[i]].Derivatives[j] * scale : 0
                : value.Jacobian[rows[i]][j] * scale;
        }
        // Rank-one worst-gate models otherwise choose unstable minimum-norm
        // shape steps. Core image-coordinate tangents supply Gauss-Newton
        // curvature with zero target displacement; the accepted merit remains
        // the original failed gates, not average image size.
        for (var i = 0; i < imageRows.Length; i++)
            for (var j = 0; j < current.Count; j++)
                jacobian[rows.Length + i, j] = .1 * value.Jacobian[imageRows[i]][j];
        var constraints = new List<(double[] Normal, double Upper)>();
        foreach (var row in _hard.Concat(_version < 3 && merit.Clearance > 1e-16 ? [] : _clearance.Concat(_quality).Where(row => signed[row].Value <= 0)))
        {
            var item = signed[row];
            if (row < 2)
            {
                var band = Math.Max(.95, Math.Abs(item.Value));
                constraints.Add((item.Derivatives.ToArray(), Math.Max(0, band - item.Value)));
                constraints.Add((item.Derivatives.Select(v => -v).ToArray(), Math.Max(0, band + item.Value)));
            }
            else constraints.Add((item.Derivatives.ToArray(), Math.Max(0, -item.Value)));
        }
        // An aimed rim at the fixed stop has constant clearance. Its AD tangent
        // can contain roundoff (~1e-16); unit-normalising that noise invents an
        // active constraint and freezes unrelated shape variables. Omit only
        // models whose entire trust-box variation is at floating-point roundoff.
        // Every actual Core gate is still checked after the step and at final validation.
        var epsilon = Math.BitIncrement(1.0) - 1;
        constraints.RemoveAll(c => c.Normal.Sum(Math.Abs) * radius <= 64 * epsilon * Math.Max(1, Math.Abs(c.Upper)));
        for (var j = 0; j < current.Count; j++)
        {
            var positive = new double[current.Count]; positive[j] = 1;
            var negative = new double[current.Count]; negative[j] = -1;
            constraints.Add((positive, Math.Max(0, Math.Min(radius, binding.Slots[j].UpperCoordinate - current[j]))));
            constraints.Add((negative, Math.Max(0, Math.Min(radius, current[j] - binding.Slots[j].LowerCoordinate))));
        }
        var normals = new double[constraints.Count, current.Count]; var upper = new double[constraints.Count];
        for (var i = 0; i < constraints.Count; i++)
        {
            upper[i] = constraints[i].Upper;
            for (var j = 0; j < current.Count; j++) normals[i, j] = constraints[i].Normal[j];
        }
        InequalityStepResult solved;
        SearchPerformanceMeasurement.ObserveModel(jacobian, residuals, normals, upper, .01);
        try { using var timing = SearchPerformanceMeasurement.Measure("gate.solve"); solved = InequalityLeastSquaresStep.Solve(jacobian, residuals, normals, upper, .01); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            // Return a charged fixed-topology result with the actual model failure,
            // rather than dropping the phase's function/gradient counters upstream.
            throw new ArithmeticException("Core inequality step: " + error.Message, error);
        }
        // Verify the original, unnormalised inequalities, including inexact solves.
        // They are model constraints only; real Core values still decide acceptance.
        if (constraints.Any(c => c.Normal.Zip(solved.Step, (a, b) => a * b).Sum() > c.Upper + 1e-8))
            return (current.ToArray(), 0);
        int rank;
        if (ReferenceEquals(value, _rankSource)) rank = _retainedRank;
        else
        {
            using (SearchPerformanceMeasurement.Measure("gate.rank")) rank = new RegularizedLeastSquaresModel(jacobian, residuals).Rank;
            // Only rate/trust-box bounds change on a retry; the model rows and
            // their exact QR rank belong to the same immutable accepted evaluation.
            _rankSource = value; _retainedRank = rank;
        }
        return (current.Select((coordinate, j) => Math.Clamp(coordinate + solved.Step[j],
            binding.Slots[j].LowerCoordinate, binding.Slots[j].UpperCoordinate)).ToArray(),
            rank);
    }
}

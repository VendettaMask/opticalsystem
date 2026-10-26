using System.Collections.Immutable;
using System.Diagnostics;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine.Optimization;

namespace OpticalSystemDesign.InitialStructure.Engine;

/// <summary>Restore parent A and focal-scale C about the edited child's q0. All optical
/// quantities and geometry probes come from Core; the trust-region objective is laboratory policy.</summary>
public static class ParaxialRestorer
{
    private sealed record Sample(CompiledTopology Source, ParaxialTransferMatrix Matrix, double[] Coordinates, double[] Residuals);
    private sealed class BudgetReached : Exception { }

    public static ParaxialRecoveryResult Run(CompiledTopology parent, CompiledTopology child,
        IEnumerable<TopologyVariableRequest> variables, TopologyTargets targets,
        ParaxialRecoveryOptions? options = null, CancellationToken cancellationToken = default,
        Action<ParaxialRecoveryStep>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(child);
        ArgumentNullException.ThrowIfNull(variables);
        options ??= new();
        var timer = Stopwatch.StartNew();
        using var deadline = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var token = linked.Token;
        var requests = variables.Take(65).ToImmutableArray();
        var diagnostics = new List<string>();
        var steps = new List<ParaxialRecoveryStep>();
        var trials = new List<ParaxialRecoveryTrial>();
        var termination = ParaxialRecoveryTermination.Unsupported;
        var fingerprint = "unavailable";
        var functions = 0; var differences = 0; var matrices = 0; var builds = 0;
        long probes = 0;
        BoundTopologyParameters? binding = null;
        ImmutableArray<TopologyGeometryRow> rows = [];
        double[] q0 = [], endpoint = [];
        ParaxialTransferMatrix? parentMatrix = null, initial = null, final = null;
        var current = child;
        Sample? last = null;
        var solverDifference = false;
        int? rank = null;
        try
        {
            token.ThrowIfCancellationRequested();
            ValidateOptions(options);
            TopologyEvaluationPlan.Validate(targets, new());
            deadline.CancelAfter(options.TimeLimit);
            if (requests.Length > 64) throw new ArgumentException("Recovery supports at most 64 explicit variables.");
            if (ReferenceIdentity(parent) != ReferenceIdentity(child))
                throw new ArgumentException("Parent and child must share the fixed mechanical frame, stop, image, spectrum, fields and environment.");
            if (requests.Any(request => request.SurfaceId == child.Topology.Image.Id
                || request.Kind is not (OpticalDesignParameterKind.Curvature or OpticalDesignParameterKind.AxialPosition)))
                throw new ArgumentException("First-order recovery fixes image/reference/stop and releases only permitted lens curvatures or axial positions.");
            fingerprint = ContentFingerprint.Compute(new
            {
                Identity = "autonomous-paraxial-recovery-v1",
                Parent = parent.BindingFingerprint,
                Child = child.BindingFingerprint,
                requests,
                targets,
                options,
                Reference = "fixed-reference-to-image/y-mm,u-dy-dz/A,f-target*C/child-q0"
            });
            if (requests.Length > 0)
            {
                binding = TopologyParameterBinder.Bind(child, requests);
                q0 = binding.InitialCoordinates.ToArray();
                endpoint = q0.ToArray();
            }
            rows = TopologyEvaluationPlan.Geometry(child, targets, options.ProfileRadialSamples);
            Charge();
            parentMatrix = Matrix(parent);
            var first = EvaluateCore(q0, false);
            if (!first.IsValid) termination = ParaxialRecoveryTermination.GeometryBlocked;
            else
            {
                initial = final = last!.Matrix;
                if (Recovered(final)) termination = ParaxialRecoveryTermination.Recovered;
                else if (binding is null || binding.Slots.All(slot => slot.LowerCoordinate == slot.UpperCoordinate))
                    termination = ParaxialRecoveryTermination.NoFreeVariables;
                else
                {
                    rank = Rank(first.Residuals);
                    if (rank is null) termination = ParaxialRecoveryTermination.GeometryBlocked;
                    else if (rank < 2) termination = ParaxialRecoveryTermination.RankDeficient;
                    else
                    {
                        var solved = BoundedTrustRegionLeastSquares.Solve(q0,
                            binding.Slots.Select(slot => slot.LowerCoordinate).ToArray(),
                            binding.Slots.Select(slot => slot.UpperCoordinate).ToArray(), Evaluate,
                            new()
                            {
                                MaximumEvaluations = Math.Max(1, options.MaximumFunctionEvaluations - functions),
                                InitialRadius = 1,
                                MaximumRadius = 100,
                                RelativeDifferenceStep = options.RelativeDifferenceStep,
                                StepMethod = LeastSquaresStepMethod.Regularized,
                                GradientTolerance = 1e-12,
                                StepTolerance = 1e-12
                            },
                            token, () => Recovered(final!) || steps.Count >= options.MaximumAcceptedSteps,
                            (point, evaluation) =>
                            {
                                if (last is null || !point.SequenceEqual(last.Coordinates)) throw new InvalidOperationException("Accepted recovery point does not match its Core evaluation.");
                                current = last.Source; final = last.Matrix; endpoint = point.ToArray();
                                var displacement = endpoint.Select((value, index) => value - q0[index]).Aggregate(0.0, double.Hypot);
                                steps.Add(new(steps.Count + 1, endpoint.ToImmutableArray(), AError(final), CError(final), displacement));
                                progress?.Invoke(steps[^1]);
                            }, difference => solverDifference = difference, () => builds++,
                            trial => trials.Add(new(functions, trial.Accepted, trial.BasePoint.ToImmutableArray(), trial.TrialPoint.ToImmutableArray(),
                                trial.Radius, trial.NextRadius, trial.PredictedReduction, trial.ActualReduction, trial.ReductionRatio)));
                        diagnostics.Add("Trust-region termination: " + solved.Termination);
                        termination = Recovered(final!) ? ParaxialRecoveryTermination.Recovered
                            : solved.Termination == LeastSquaresTermination.EvaluationLimit || functions >= options.MaximumFunctionEvaluations
                                ? ParaxialRecoveryTermination.BudgetExhausted : ParaxialRecoveryTermination.NoParaxialRecovery;
                    }
                }
            }
        }
        catch (BudgetReached) { termination = ParaxialRecoveryTermination.BudgetExhausted; }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            termination = cancellationToken.IsCancellationRequested ? ParaxialRecoveryTermination.Cancelled : ParaxialRecoveryTermination.TimeLimit;
        }
        catch (Exception error) when (TopologyEvaluationPlan.DomainError(error))
        {
            termination = ParaxialRecoveryTermination.Unsupported;
            diagnostics.Add(error.Message);
        }
        return new()
        {
            ProtocolFingerprint = fingerprint,
            Topology = current.Topology,
            Termination = termination,
            Work = new(functions, differences, matrices, builds, probes, timer.Elapsed),
            Variables = requests,
            ChildInitialCoordinates = q0.ToImmutableArray(),
            EndpointCoordinates = endpoint.ToImmutableArray(),
            ParentMatrix = parentMatrix,
            InitialMatrix = initial,
            EndpointMatrix = final,
            InitialAError = initial is null || parentMatrix is null ? null : AError(initial),
            InitialFocalCError = initial is null || parentMatrix is null ? null : CError(initial),
            EndpointAError = final is null || parentMatrix is null ? null : AError(final),
            EndpointFocalCError = final is null || parentMatrix is null ? null : CError(final),
            InitialJacobianRank = rank,
            Steps = steps.ToImmutableArray(),
            Trials = trials.ToImmutableArray(),
            Diagnostics = diagnostics.Distinct().ToImmutableArray()
        };

        double AError(ParaxialTransferMatrix matrix) => matrix.A - parentMatrix!.A;
        double CError(ParaxialTransferMatrix matrix) => targets.EffectiveFocalLengthMillimeters * (matrix.C - parentMatrix!.C);
        bool Recovered(ParaxialTransferMatrix matrix) => Math.Abs(AError(matrix)) <= options.ATolerance && Math.Abs(CError(matrix)) <= options.FocalCTolerance;
        void Charge()
        {
            if (timer.Elapsed >= options.TimeLimit) deadline.Cancel();
            token.ThrowIfCancellationRequested();
            if (functions >= options.MaximumFunctionEvaluations) throw new BudgetReached();
            functions++;
        }
        ParaxialTransferMatrix Matrix(CompiledTopology value)
        {
            matrices++;
            return value.CreateOptic().Paraxial.GetTransferMatrix(value.SurfaceIndices[value.Topology.Reference.Id], value.SurfaceIndices[value.Topology.Image.Id]);
        }
        LeastSquaresEvaluation Evaluate(IReadOnlyList<double> coordinates, CancellationToken ignored)
            => EvaluateCore(coordinates, solverDifference);
        LeastSquaresEvaluation EvaluateCore(IReadOnlyList<double> coordinates, bool difference)
        {
            Charge();
            if (difference) differences++;
            var start = probes;
            var matrixStart = matrices;
            try
            {
                var value = binding is null ? child : TopologyParameterBinder.Apply(child, binding, coordinates);
                var optic = value.CreateOptic();
                foreach (var row in rows)
                {
                    token.ThrowIfCancellationRequested(); probes++;
                    var measured = TopologyEvaluationPlan.FormalMeasure(optic, row);
                    if (!double.IsFinite(measured) || (row.Upper ? measured > row.Limit : measured < row.Limit))
                        throw new ArithmeticException("Recovery geometry is blocked at " + row.Id);
                }
                var matrix = Matrix(value);
                var residuals = new double[2 + coordinates.Count];
                residuals[0] = AError(matrix) / options.AScale;
                residuals[1] = CError(matrix) / options.FocalCScale;
                for (var index = 0; index < coordinates.Count; index++)
                    residuals[index + 2] = Math.Sqrt(options.MinimumChangeWeight) * (coordinates[index] - q0[index]);
                last = new(value, matrix, coordinates.ToArray(), residuals);
                return new(residuals, true, probes - start + matrices - matrixStart);
            }
            catch (Exception error) when (TopologyEvaluationPlan.DomainError(error))
            {
                diagnostics.Add(error.Message);
                return new([], false, probes - start + matrices - matrixStart);
            }
        }
        int? Rank(IReadOnlyList<double> baseline)
        {
            var a = new List<double>(); var c = new List<double>();
            for (var slot = 0; slot < binding!.Slots.Length; slot++)
            {
                var bound = binding.Slots[slot];
                if (bound.LowerCoordinate == bound.UpperCoordinate) continue;
                var step = options.RelativeDifferenceStep * Math.Max(1, Math.Abs(q0[slot]));
                LeastSquaresEvaluation? plus = null, minus = null;
                double hp = 0, hm = 0;
                for (var attempt = 0; attempt < 4; attempt++, step *= .25)
                {
                    var up = q0.ToArray(); var down = q0.ToArray();
                    up[slot] = Math.Min(bound.UpperCoordinate, q0[slot] + step);
                    down[slot] = Math.Max(bound.LowerCoordinate, q0[slot] - step);
                    hp = up[slot] - q0[slot]; hm = q0[slot] - down[slot];
                    if (hp > 0) plus = EvaluateCore(up, true);
                    if (hm > 0) minus = EvaluateCore(down, true);
                    if (plus?.IsValid == true || minus?.IsValid == true) break;
                }
                if (plus?.IsValid != true && minus?.IsValid != true) return null;
                a.Add(Slope(0)); c.Add(Slope(1));
                double Slope(int row) => plus?.IsValid == true && minus?.IsValid == true
                    ? (plus.Residuals[row] - minus.Residuals[row]) / (hp + hm)
                    : plus?.IsValid == true ? (plus.Residuals[row] - baseline[row]) / hp
                    : (baseline[row] - minus!.Residuals[row]) / hm;
            }
            builds++;
            var an = a.Aggregate(0.0, double.Hypot); var cn = c.Aggregate(0.0, double.Hypot);
            if (!double.IsFinite(an) || !double.IsFinite(cn)) throw new ArithmeticException("Recovery Jacobian is not finite.");
            if (an == 0 || cn == 0) return an + cn > 0 ? 1 : 0;
            var projection = a.Select((value, index) => value / an * (c[index] / cn)).Sum();
            var perpendicular = a.Select((value, index) => c[index] / cn - projection * value / an).Aggregate(0.0, double.Hypot);
            return perpendicular > options.RelativeRankTolerance ? 2 : 1;
        }
    }

    private static string ReferenceIdentity(CompiledTopology source) => ContentFingerprint.Compute(new
    {
        source.Topology.Reference,
        source.Topology.Stop,
        source.Topology.Image,
        source.Topology.Fields,
        source.Topology.Wavelengths,
        source.Topology.Conjugate,
        source.Topology.GlassCatalogs,
        source.Topology.RayAimingEnabled,
        source.Topology.MatchRefractiveIndexData,
        source.Topology.TemperatureCelsius,
        source.Topology.PressureAtmospheres,
        source.Constraints
    });

    internal static void ValidateOptions(ParaxialRecoveryOptions options)
    {
        double[] positive = [options.AScale, options.FocalCScale, options.ATolerance, options.FocalCTolerance,
            options.RelativeDifferenceStep, options.RelativeRankTolerance];
        if (options.MaximumAcceptedSteps is < 1 or > 1000 || options.MaximumFunctionEvaluations is < 1 or > 100_000
            || options.TimeLimit <= TimeSpan.Zero || options.TimeLimit > TimeSpan.FromHours(1)
            || positive.Any(value => !double.IsFinite(value) || value <= 0)
            || !double.IsFinite(options.MinimumChangeWeight) || options.MinimumChangeWeight <= 0
            || options.ProfileRadialSamples is < 4 or > 64)
            throw new ArgumentException("Invalid bounded first-order recovery protocol.");
    }
}

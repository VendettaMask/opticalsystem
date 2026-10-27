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
    private sealed record GeometryPlane(double Offset, double[] Normal);
    private sealed record Sample(CompiledTopology Source, ParaxialTransferMatrix Matrix, double[] Coordinates, double[] Residuals,
        double[,]? Jacobian = null, double? InverseFNumber = null, GeometryPlane[]? Geometry = null);
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
        Sample? accepted = null;
        var solverDifference = false;
        int? rank = null;
        var restorePupil = options.RestorePupilMagnification == true;
        var constrainedGeometry = options.UseGeometryConstraintTangents == true;
        var projections = 0;
        var projectionSolves = 0;
        var opticalRows = restorePupil ? 3 : 2;
        var pupils = 0;
        double? parentFNumber = null, initialInverseF = null, finalInverseF = null;
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
                Identity = constrainedGeometry ? "autonomous-paraxial-recovery-geometry-v1" : restorePupil ? "autonomous-paraxial-recovery-pupil-v4" : options.UseAutomaticMatrixDerivatives == true ? "autonomous-paraxial-recovery-ad-v2" : "autonomous-paraxial-recovery-v1",
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
            if (restorePupil)
            {
                pupils++; parentFNumber = parent.CreateOptic().Paraxial.EstimateFNumber();
                if (!(parentFNumber > 0) || !double.IsFinite(parentFNumber.Value)) throw new ArithmeticException("Parent F number is unavailable for fixed-stop recovery.");
            }
            var first = EvaluateCore(q0, false);
            if (!first.IsValid) termination = ParaxialRecoveryTermination.GeometryBlocked;
            else
            {
                initial = final = last!.Matrix;
                accepted = last;
                initialInverseF = finalInverseF = last.InverseFNumber;
                if (Recovered(final)) termination = ParaxialRecoveryTermination.Recovered;
                else if (binding is null || binding.Slots.All(slot => slot.LowerCoordinate == slot.UpperCoordinate))
                    termination = ParaxialRecoveryTermination.NoFreeVariables;
                else
                {
                    rank = Rank(first.Residuals);
                    var consistentRedundancy = restorePupil && rank == 2
                        && OpticalRank(CachedJacobian(q0), first.Residuals) == rank;
                    if (consistentRedundancy) diagnostics.Add("A redundant pupil constraint is locally consistent with the optical Jacobian; continuing with rank 2.");
                    if (rank is null) termination = ParaxialRecoveryTermination.GeometryBlocked;
                    else if (rank < opticalRows && !consistentRedundancy) termination = ParaxialRecoveryTermination.RankDeficient;
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
                                accepted = last;
                                finalInverseF = last.InverseFNumber;
                                var displacement = endpoint.Select((value, index) => value - q0[index]).Aggregate(0.0, double.Hypot);
                                steps.Add(new(steps.Count + 1, endpoint.ToImmutableArray(), AError(final), CError(final), displacement));
                                progress?.Invoke(steps[^1]);
                            }, difference => solverDifference = difference, () => { if (options.UseAutomaticMatrixDerivatives != true) builds++; },
                            trial => trials.Add(new(functions, trial.Accepted, trial.BasePoint.ToImmutableArray(), trial.TrialPoint.ToImmutableArray(),
                                trial.Radius, trial.NextRadius, trial.PredictedReduction, trial.ActualReduction, trial.ReductionRatio)),
                            analyticJacobian: options.UseAutomaticMatrixDerivatives == true ? CachedJacobian : null,
                            trialProjector: constrainedGeometry ? ProjectGeometry : null);
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
        if (constrainedGeometry)
        {
            diagnostics.Add("Geometry-aware trial projections: " + projections);
            diagnostics.Add("Geometry linear projection solves: " + projectionSolves);
        }
        return new()
        {
            ProtocolFingerprint = fingerprint,
            Topology = current.Topology,
            Termination = termination,
            Work = new(functions, differences, matrices, builds, probes, timer.Elapsed) { PupilEvaluations = restorePupil ? pupils : null },
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
            InitialPupilError = initialInverseF is { } firstInverse ? firstInverse * parentFNumber - 1 : null,
            EndpointPupilError = finalInverseF is { } lastInverse ? lastInverse * parentFNumber - 1 : null,
            InitialJacobianRank = rank,
            Steps = steps.ToImmutableArray(),
            Trials = trials.ToImmutableArray(),
            Diagnostics = diagnostics.Distinct().ToImmutableArray()
        };

        double AError(ParaxialTransferMatrix matrix) => matrix.A - parentMatrix!.A;
        double CError(ParaxialTransferMatrix matrix) => targets.EffectiveFocalLengthMillimeters * (matrix.C - parentMatrix!.C);
        bool Recovered(ParaxialTransferMatrix matrix) => Math.Abs(AError(matrix)) <= options.ATolerance && Math.Abs(CError(matrix)) <= options.FocalCTolerance
            && (!restorePupil || finalInverseF is { } inverse && Math.Abs(inverse * parentFNumber!.Value - 1) <= options.FocalCTolerance);
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
                var planes = constrainedGeometry ? new List<GeometryPlane>() : null;
                foreach (var row in rows)
                {
                    token.ThrowIfCancellationRequested(); probes++;
                    var measured = TopologyEvaluationPlan.FormalMeasure(optic, row);
                    if (!double.IsFinite(measured) || (row.Upper ? measured > row.Limit : measured < row.Limit))
                        throw new ArithmeticException("Recovery geometry is blocked at " + row.Id);
                    if (planes is not null)
                    {
                        probes++;
                        var tangent = TopologyEvaluationPlan.Measure(optic, binding?.CoreParameters ?? [], row);
                        if (Math.Abs(tangent.Value - measured) > 1e-9 * Math.Max(1, Math.Abs(measured)))
                            throw new ArithmeticException("Core geometry tangent disagrees with formal geometry at " + row.Id);
                        var sign = row.Upper ? 1 : -1;
                        planes.Add(new(sign * (measured - row.Limit), Enumerable.Range(0, coordinates.Count).Select(column => sign * tangent.Derivative(column)).ToArray()));
                    }
                }
                ParaxialTransferMatrix matrix;
                DifferentiableParaxialTransferMatrix? automatic = null;
                if (options.UseAutomaticMatrixDerivatives == true)
                {
                    matrices++; builds++;
                    if (restorePupil) pupils++;
                    automatic = DifferentiableParaxialMetrics.TransferMatrix(optic, binding?.CoreParameters ?? [],
                        value.SurfaceIndices[value.Topology.Reference.Id], value.SurfaceIndices[value.Topology.Image.Id], includeInverseFNumber: restorePupil);
                    matrix = automatic.Nominal;
                }
                else matrix = Matrix(value);
                var residuals = new double[opticalRows + coordinates.Count];
                residuals[0] = AError(matrix) / options.AScale;
                residuals[1] = CError(matrix) / options.FocalCScale;
                if (restorePupil) residuals[2] = (automatic!.InverseFNumber!.Value.Value * parentFNumber!.Value - 1) / options.FocalCScale;
                for (var index = 0; index < coordinates.Count; index++)
                    residuals[index + opticalRows] = Math.Sqrt(options.MinimumChangeWeight) * (coordinates[index] - q0[index]);
                double[,]? jacobian = null;
                if (automatic is not null)
                {
                    jacobian = new double[residuals.Length, coordinates.Count];
                    for (var column = 0; column < coordinates.Count; column++)
                    {
                        jacobian[0, column] = automatic.A.Derivative(column) / options.AScale;
                        jacobian[1, column] = targets.EffectiveFocalLengthMillimeters * automatic.C.Derivative(column) / options.FocalCScale;
                        if (restorePupil) jacobian[2, column] = automatic.InverseFNumber!.Value.Derivative(column) * parentFNumber!.Value / options.FocalCScale;
                        jacobian[column + opticalRows, column] = Math.Sqrt(options.MinimumChangeWeight);
                    }
                }
                last = new(value, matrix, coordinates.ToArray(), residuals, jacobian, automatic?.InverseFNumber?.Value, planes?.ToArray());
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
            if (options.UseAutomaticMatrixDerivatives == true)
            {
                var cached = CachedJacobian(q0);
                if (restorePupil) return OpticalRank(cached);
                for (var column = 0; column < binding!.Slots.Length; column++)
                    if (binding.Slots[column].LowerCoordinate != binding.Slots[column].UpperCoordinate)
                    { a.Add(cached[0, column]); c.Add(cached[1, column]); }
                return RowRank(a, c);
            }
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
            return RowRank(a, c);
        }
        double[,] CachedJacobian(IReadOnlyList<double> coordinates)
            => last?.Jacobian is { } jacobian && coordinates.SequenceEqual(last.Coordinates)
                ? jacobian : throw new InvalidOperationException("Recovery derivatives must belong to the current Core matrix evaluation.");
        double[]? ProjectGeometry(IReadOnlyList<double> point, IReadOnlyList<double> proposed, double radius)
        {
            if (accepted?.Geometry is not { } geometry || !point.SequenceEqual(accepted.Coordinates))
                throw new InvalidOperationException("Geometry projection must use the accepted Core geometry evaluation.");
            token.ThrowIfCancellationRequested(); projections++;
            var step = proposed.Select((value, index) => value - point[index]).ToArray();
            var planes = geometry.Select(row => (Offset: row.Offset + (row.Normal.Any(value => value != 0) ? Math.Min(1e-6, radius * radius * 1e-3) : 0), row.Normal)).ToList();
            for (var column = 0; column < point.Count; column++)
            {
                var positive = new double[point.Count]; positive[column] = 1;
                var negative = new double[point.Count]; negative[column] = -1;
                planes.Add((-Math.Min(radius, binding!.Slots[column].UpperCoordinate - point[column]), positive));
                planes.Add((-Math.Min(radius, point[column] - binding.Slots[column].LowerCoordinate), negative));
            }
            foreach (var fraction in new[] { 1, .5, .25, .1, .025 })
            {
                var joint = planes.ToList();
                for (var row = 0; row < opticalRows; row++)
                {
                    var normal = Enumerable.Range(0, point.Count).Select(column => accepted.Jacobian![row, column]).ToArray();
                    var tolerance = .25 * (row == 0 ? options.ATolerance / options.AScale : options.FocalCTolerance / options.FocalCScale);
                    joint.Add((fraction * accepted.Residuals[row] - tolerance, normal));
                    joint.Add((-fraction * accepted.Residuals[row] - tolerance, normal.Select(value => -value).ToArray()));
                }
                projectionSolves++;
                var constrained = LinearFeasibilityProjection.Solve(joint, point.Count, 4096, token, step);
                if (constrained is not null) return constrained.Select((value, index) => point[index] + value).ToArray();
            }
            projectionSolves++;
            var projected = LinearFeasibilityProjection.Solve(planes, point.Count, 4096, token, step);
            return projected?.Select((value, index) => point[index] + value).ToArray();
        }
        int OpticalRank(double[,] jacobian, IReadOnlyList<double>? residuals = null)
        {
            var columns = Enumerable.Range(0, binding!.Slots.Length)
                .Where(column => binding.Slots[column].LowerCoordinate != binding.Slots[column].UpperCoordinate).ToArray();
            var basis = new List<double[]>();
            for (var row = 0; row < opticalRows; row++)
            {
                var values = columns.Select(column => jacobian[row, column]);
                var vector = (residuals is null ? values : values.Append(residuals[row])).ToArray();
                var size = vector.Aggregate(0.0, double.Hypot);
                if (!double.IsFinite(size)) throw new ArithmeticException("Recovery Jacobian is not finite.");
                if (size == 0) continue;
                for (var index = 0; index < vector.Length; index++) vector[index] /= size;
                // Reorthogonalize the small row basis before classifying an almost-dependent constraint.
                for (var pass = 0; pass < 2; pass++)
                    foreach (var previous in basis)
                    {
                        var projection = vector.Zip(previous, (x, y) => x * y).Sum();
                        for (var index = 0; index < vector.Length; index++) vector[index] -= projection * previous[index];
                    }
                var remainder = vector.Aggregate(0.0, double.Hypot);
                if (remainder > options.RelativeRankTolerance) basis.Add(vector.Select(item => item / remainder).ToArray());
            }
            return basis.Count;
        }
        int RowRank(IReadOnlyList<double> a, IReadOnlyList<double> c)
        {
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
            || options.RestorePupilMagnification == true && options.UseAutomaticMatrixDerivatives != true
            || options.UseGeometryConstraintTangents == true && options.UseAutomaticMatrixDerivatives != true
            || options.TimeLimit <= TimeSpan.Zero || options.TimeLimit > TimeSpan.FromHours(1)
            || positive.Any(value => !double.IsFinite(value) || value <= 0)
            || !double.IsFinite(options.MinimumChangeWeight) || options.MinimumChangeWeight <= 0
            || options.ProfileRadialSamples is < 4 or > 64)
            throw new ArgumentException("Invalid bounded first-order recovery protocol.");
    }
}

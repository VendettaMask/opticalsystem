using System.Collections.Immutable;
using System.Diagnostics;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine.Optimization;
using static OpticalSystemDesign.InitialStructure.Engine.TopologyValidation;

namespace OpticalSystemDesign.InitialStructure.Engine;

/// <summary>Bounded fixed-topology compensation with opt-in constraint restoration. No UpstreamE58 state is reused.</summary>
public static class FixedTopologyOptimizer
{
    public static FixedTopologyResult Run(CompiledTopology source, BoundTopologyParameters binding, TopologyTargets targets,
        FixedTopologyOptions? options = null, CancellationToken cancellationToken = default, Action<FixedTopologyStep>? progress = null,
        FixedTopologyContinuation? continuation = null)
    {
        options ??= new();
        Require(options.MaximumUpdates is >= 0 and <= 400 && options.MaximumFunctionEvaluations is >= 1 and <= 10000
            && options.MaximumGradientEvaluations is >= 0 and <= 10000 && options.TimeLimit > TimeSpan.Zero && options.TimeLimit <= TimeSpan.FromHours(1)
            && double.IsFinite(options.LearningRate) && options.LearningRate is > 0 and <= 1 && Enum.IsDefined(options.ComputeMode),
            TopologyFailureCode.InvalidInput, "Invalid fixed-topology stage budget or learning rate.");
        var transmissionFirst = options.TransmissionFirst == true;
        var needsJacobian = transmissionFirst || options.ActiveParameterLimit is not null;
        Require((options.ActiveParameterLimit is null or >= 1 and <= 512) && (!needsJacobian || options.Sampling.IncludeBoundaryClearance == true),
            TopologyFailureCode.InvalidInput, "Transmission compensation/selection requires explicit boundary sampling and a bounded active parameter count.");
        Require(!needsJacobian || options.TransmissionPolicyVersion is 3 or 4 or 5 or 9 or 11 or 12, TopologyFailureCode.IdentityMismatch,
            "An explicit transmission policy version 3, 4, 5, 9, 11 or 12 is required. Unversioned prototype records are read-only; create a fresh versioned plan.");
        var timer = Stopwatch.StartNew();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var elapsedBefore = continuation?.Work.Elapsed ?? TimeSpan.Zero;
        deadline.CancelAfter(options.TimeLimit > elapsedBefore ? options.TimeLimit - elapsedBefore : TimeSpan.Zero);
        var token = deadline.Token;
        var steps = continuation?.Steps.ToList() ?? new List<FixedTopologyStep>();
        var diagnostics = new HashSet<string>(StringComparer.Ordinal);
        var previous = continuation?.Work;
        var functions = previous?.FunctionEvaluations ?? 0; var gradients = previous?.GradientEvaluations ?? 0;
        var completedGradients = previous?.CompletedGradientEvaluations ?? 0; var validations = previous?.ValidationEvaluations ?? 0;
        var updates = continuation?.AcceptedUpdates ?? 0;
        long rays = previous?.TracedRays ?? 0, probes = previous?.GeometryProbes ?? 0;
        long gpuRays = previous?.GpuDerivativeRays ?? 0, gpuBatches = previous?.GpuBatches ?? 0;
        var unknownGpuWork = previous is not null && previous.GpuDerivativeRays is null;
        var current = source;
        TopologyObjectiveEvaluation? initial = continuation?.InitialEvaluation, endpoint = continuation?.EndpointEvaluation;
        TopologyAssessment? assessment = null;
        var termination = FixedTopologyTermination.UpdateLimit;
        FixedTopologyObjective? objective = null;
        var state = continuation?.Adam ?? DifferentiableAdam.Initialize(binding.InitialCoordinates);
        var rate = continuation?.LearningRate ?? options.LearningRate;
        var phaseFingerprint = ContentFingerprint.Compute(new { Identity = needsJacobian ? "fixed-topology-transmission-v" + options.TransmissionPolicyVersion : "fixed-topology-continuation-v1", source.BindingFingerprint, binding.Fingerprint, targets, options });
        if (continuation is not null)
        {
            DifferentiableAdam.Validate(state);
            Require(continuation.PhaseFingerprint == phaseFingerprint && state.Coordinates.Count == binding.Slots.Length
                && double.IsFinite(rate) && rate > 0 && rate <= options.LearningRate
                && functions >= 0 && functions <= options.MaximumFunctionEvaluations && gradients >= 0 && gradients <= options.MaximumGradientEvaluations
                && validations == 0 && rays >= 0 && probes >= 0 && elapsedBefore >= TimeSpan.Zero
                && steps.Count <= options.MaximumUpdates && updates == steps.Count(step => step.Accepted)
                && steps.Select(step => step.Attempt).SequenceEqual(Enumerable.Range(1, steps.Count)),
                TopologyFailureCode.IdentityMismatch, "Continuation identity, optimizer state or consumed work does not match this phase.");
            current = TopologyParameterBinder.Apply(source, binding, state.Coordinates);
        }
        try { objective = new(source, binding, targets, options.Sampling); }
        catch (Exception error) when (TopologyEvaluationPlan.DomainError(error))
        {
            termination = FixedTopologyTermination.Unsupported;
            diagnostics.Add(error.Message);
        }
        var projectionEvents = options.TransmissionPolicyVersion == 12 ? new List<TopologyProjectionAttempt>() : null;
        var transmission = objective is null ? null : new TopologyTransmissionCompensation(objective.ResidualIds,
            options.TransmissionPolicyVersion ?? 3, projectionEvents is null ? null : projectionEvents.Add);
        if (continuation is not null && endpoint is not null)
            Require(endpoint.Available && endpoint.LayoutFingerprint == objective?.Fingerprint
                && endpoint.OpticFingerprint == current.OpticFingerprint && endpoint.Gradient.Length == binding.Slots.Length
                && endpoint.Gradient.All(double.IsFinite)
                && (!needsJacobian || endpoint.Jacobian.Length == objective.ResidualIds.Length && endpoint.Jacobian.All(row => row.Length == binding.Slots.Length && row.All(double.IsFinite))
                    && transmission!.ValidTangents(endpoint, binding.Slots.Length)
                    && steps.All(step => step.ActiveParameters is { Length: > 0 } selected
                        && selected.Length <= Math.Min(options.ActiveParameterLimit ?? binding.Slots.Length, binding.Slots.Length)
                        && selected.Distinct().Count() == selected.Length && selected.All(index => index >= 0 && index < binding.Slots.Length)
                        && (step.Transmission is not null) == transmissionFirst)),
                TopologyFailureCode.IdentityMismatch, "Continuation residuals do not match the accepted point and layout.");
        if (objective is not null && !StopRequested())
        {
            if (endpoint is null && !CanDifferentiate()) termination = FixedTopologyTermination.BudgetExhausted;
            else
            {
                if (endpoint is null)
                {
                    var first = Evaluate(state.Coordinates);
                    if (first.Available) initial = endpoint = first;
                    else if (SequentialTraceMeasurement.CpuBudgetReached) termination = FixedTopologyTermination.BudgetExhausted;
                    else if (!StopRequested()) termination = FixedTopologyTermination.DomainFailure;
                }
                if (endpoint is null) { /* An interrupted initial evaluation is charged but is not an accepted point. */ }
                else
                {
                    current = TopologyParameterBinder.Apply(source, binding, state.Coordinates);
                    for (var attempt = steps.Count + 1; attempt <= options.MaximumUpdates; attempt++)
                    {
                        if (StopRequested()) break;
                        if (!CanDifferentiate()) { termination = FixedTopologyTermination.BudgetExhausted; break; }
                        if (transmissionFirst ? transmission!.Feasible(endpoint) : endpoint.Gradient.All(value => Math.Abs(value) < 1e-10))
                        { termination = FixedTopologyTermination.Converged; break; }
                        var active = options.ActiveParameterLimit is { } limit ? transmission!.Select(endpoint, limit) : Enumerable.Range(0, binding.Slots.Length).ToArray();
                        AutoLensAdamState proposed;
                        double[] coordinates;
                        var modelRank = 0;
                        if (transmissionFirst)
                        {
                            projectionEvents?.Clear();
                            try { (coordinates, modelRank) = transmission!.Propose(endpoint, state.Coordinates, binding, active, rate / options.LearningRate, token); }
                            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                            catch (ArithmeticException error) { diagnostics.Add(error.Message); termination = FixedTopologyTermination.Stalled; break; }
                            proposed = DifferentiableAdam.Initialize(coordinates) with { Iteration = state.Iteration + 1 };
                        }
                        else
                        {
                            DifferentialScalar tangent = 0;
                            for (var index = 0; index < binding.Slots.Length; index++)
                                tangent += DifferentialScalar.Variable(0, binding.Slots.Length, index) * (active.Contains(index) ? endpoint.Gradient[index] : 0);
                            proposed = DifferentiableAdam.Propose(state, tangent, Enumerable.Repeat(rate, binding.Slots.Length).ToArray());
                            coordinates = proposed.Coordinates.Select((value, index) => active.Contains(index) ? Math.Clamp(value,
                                binding.Slots[index].LowerCoordinate, binding.Slots[index].UpperCoordinate) : state.Coordinates[index]).ToArray();
                            if (options.ActiveParameterLimit is not null)
                                proposed = proposed with
                                {
                                    FirstMoment = proposed.FirstMoment.Select((value, index) => active.Contains(index) ? value : 0).ToArray(),
                                    SecondMoment = proposed.SecondMoment.Select((value, index) => active.Contains(index) ? value : 0).ToArray()
                                };
                        }
                        if (projectionEvents is not null)
                            foreach (var item in projectionEvents)
                                diagnostics.Add($"Projection attempt {attempt}: decrease={item.Decrease:R}, dimension={item.Dimension}, primary={item.PrimaryFeasible}, fallbackAttempted={item.FallbackAttempted}, fallbackFeasible={item.FallbackFeasible}.");
                        if (coordinates.SequenceEqual(state.Coordinates)) { termination = FixedTopologyTermination.Stalled; break; }
                        var trial = Evaluate(coordinates);
                        if (SequentialTraceMeasurement.CpuBudgetReached) { termination = FixedTopologyTermination.BudgetExhausted; break; }
                        // Cancellation is not a rejected numerical update: preserve moments and rate.
                        if (StopRequested()) break;
                        var beforeMerit = transmissionFirst ? transmission!.Merit(endpoint) : default;
                        var afterMerit = transmissionFirst && trial.Available ? transmission!.Merit(trial) : (double.PositiveInfinity, double.PositiveInfinity);
                        var accepted = transmissionFirst ? transmission!.Accept(endpoint, trial)
                            : trial.Available && (options.RequireDescent != true || trial.Objective <= endpoint.Objective);
                        if (accepted)
                        {
                            current = TopologyParameterBinder.Apply(source, binding, coordinates);
                            state = proposed with { Coordinates = coordinates };
                            endpoint = trial;
                            updates++;
                            if (transmissionFirst) rate = Math.Min(options.LearningRate, rate * 2);
                        }
                        else
                        {
                            // Reject this branch of the continuous step, retain its charged work, discard its moments.
                            state = DifferentiableAdam.Initialize(state.Coordinates);
                            rate *= .5;
                        }
                        var step = new FixedTopologyStep(attempt, accepted, trial.Objective,
                            trial.Available && !accepted ? transmissionFirst ? "Transmission/gate filter rejected the proposal." : "Non-descending proposal rejected under the fixed phase objective." : trial.Failure)
                        {
                            ActiveParameters = needsJacobian ? active.ToImmutableArray() : null,
                            Transmission = transmissionFirst ? new(beforeMerit.Clearance, afterMerit.Item1, beforeMerit.Other, afterMerit.Item2, modelRank) : null
                        };
                        if (projectionEvents is { Count: > 0 } && projectionEvents[^1].FallbackFeasible)
                            diagnostics.Add($"Fallback candidate attempt {attempt}: accepted={accepted}.");
                        steps.Add(step); progress?.Invoke(step);
                        if (!trial.Available && rate < 1e-10) { termination = FixedTopologyTermination.DomainFailure; break; }
                    }
                }
            }
            // The last function slot is reserved for a fresh, independent physical endpoint assessment.
            if (options.PerformFinalAssessment && !SequentialTraceMeasurement.CpuBudgetReached && !StopRequested() && functions < options.MaximumFunctionEvaluations)
            {
                functions++; validations++;
                assessment = TopologyValidator.Validate(current, targets, token);
                rays += assessment.TracedRayCount; probes += assessment.GeometryProbes;
                if (!assessment.Completed && !token.IsCancellationRequested)
                {
                    termination = FixedTopologyTermination.DomainFailure;
                    diagnostics.Add("Final physical assessment is unavailable: " + assessment.Failure);
                }
            }
        }
        if (StopRequested())
            termination = cancellationToken.IsCancellationRequested ? FixedTopologyTermination.Cancelled : FixedTopologyTermination.TimeLimit;
        return new()
        {
            ObjectiveFingerprint = objective?.Fingerprint ?? "unavailable",
            Topology = current.Topology,
            Constraints = source.Constraints,
            Targets = targets,
            Variables = binding.Slots.Select(slot => slot.Request).ToImmutableArray(),
            Termination = termination,
            AcceptedUpdates = updates,
            Steps = steps.ToImmutableArray(),
            InitialEvaluation = initial,
            EndpointEvaluation = endpoint,
            FinalAssessment = assessment,
            Work = Work(),
            Diagnostics = diagnostics.ToImmutableArray(),
            Continuation = validations == 0 ? new()
            {
                PhaseFingerprint = phaseFingerprint,
                Adam = state,
                LearningRate = rate,
                Work = Work(),
                Steps = steps.ToImmutableArray(),
                AcceptedUpdates = updates,
                InitialEvaluation = initial,
                EndpointEvaluation = endpoint
            } : null
        };

        FixedTopologyWork Work() => new(functions, gradients, validations, rays, probes, unknownGpuWork ? null : gpuRays,
            unknownGpuWork ? null : gpuBatches, elapsedBefore + timer.Elapsed, completedGradients);
        bool CanDifferentiate() => functions < options.MaximumFunctionEvaluations - (options.PerformFinalAssessment ? 1 : 0)
            && gradients < options.MaximumGradientEvaluations && !SequentialTraceMeasurement.CpuBudgetReached;
        bool StopRequested()
        {
            if (!token.IsCancellationRequested && elapsedBefore + timer.Elapsed >= options.TimeLimit) deadline.Cancel();
            return token.IsCancellationRequested;
        }
        TopologyObjectiveEvaluation Evaluate(IReadOnlyList<double> coordinates)
        {
            functions++; gradients++;
            var value = objective!.Evaluate(coordinates, options.ComputeMode, includeJacobian: needsJacobian, cancellationToken: token, includeConstraintTangents: needsJacobian);
            rays += value.TracedRayCount; probes += value.GeometryProbes;
            if (value.Available) completedGradients++;
            gpuRays += value.Execution?.GpuRays ?? 0; gpuBatches += value.Execution?.GpuBatches ?? 0;
            if (options.ComputeMode != DesignComputeMode.Cpu && value.Execution is null && value.TracedRayCount > 0)
            {
                unknownGpuWork = true;
                diagnostics.Add("An interrupted derivative request has no complete GPU counters. Its request and measured traces remain charged; GPU totals are unavailable, not zero.");
            }
            if (value.Execution?.FallbackReason is { } fallback) diagnostics.Add(fallback);
            if (value.PropagationExecution?.FallbackReason is { } propagationFallback) diagnostics.Add(propagationFallback);
            if (value.Failure is { } failure) diagnostics.Add(failure);
            return value;
        }
    }
}

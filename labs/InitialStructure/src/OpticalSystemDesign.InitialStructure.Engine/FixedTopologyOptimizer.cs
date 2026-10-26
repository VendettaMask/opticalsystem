using System.Collections.Immutable;
using System.Diagnostics;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine.Optimization;
using static OpticalSystemDesign.InitialStructure.Engine.TopologyValidation;

namespace OpticalSystemDesign.InitialStructure.Engine;

/// <summary>Bounded fixed-topology Adam. No fixed-family indexing or UpstreamE58 state is reused.</summary>
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
        var phaseFingerprint = ContentFingerprint.Compute(new { Identity = "fixed-topology-continuation-v1", source.BindingFingerprint, binding.Fingerprint, targets, options });
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
        if (continuation is not null && endpoint is not null)
            Require(endpoint.Available && endpoint.LayoutFingerprint == objective?.Fingerprint
                && endpoint.OpticFingerprint == current.OpticFingerprint && endpoint.Gradient.Length == binding.Slots.Length
                && endpoint.Gradient.All(double.IsFinite), TopologyFailureCode.IdentityMismatch, "Continuation residuals do not match the accepted point and layout.");
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
                        if (endpoint.Gradient.All(value => Math.Abs(value) < 1e-10)) { termination = FixedTopologyTermination.Converged; break; }
                        DifferentialScalar tangent = 0;
                        for (var index = 0; index < binding.Slots.Length; index++)
                            tangent += DifferentialScalar.Variable(0, binding.Slots.Length, index) * endpoint.Gradient[index];
                        var proposed = DifferentiableAdam.Propose(state, tangent, Enumerable.Repeat(rate, binding.Slots.Length).ToArray());
                        var coordinates = proposed.Coordinates.Select((value, index) => Math.Clamp(value,
                            binding.Slots[index].LowerCoordinate, binding.Slots[index].UpperCoordinate)).ToArray();
                        if (coordinates.SequenceEqual(state.Coordinates)) { termination = FixedTopologyTermination.Stalled; break; }
                        var trial = Evaluate(coordinates);
                        if (SequentialTraceMeasurement.CpuBudgetReached) { termination = FixedTopologyTermination.BudgetExhausted; break; }
                        // Cancellation is not a rejected numerical update: preserve moments and rate.
                        if (StopRequested()) break;
                        if (trial.Available)
                        {
                            current = TopologyParameterBinder.Apply(source, binding, coordinates);
                            state = proposed with { Coordinates = coordinates };
                            endpoint = trial;
                            updates++;
                        }
                        else
                        {
                            // Reject this branch of the continuous step, retain its charged work, discard its moments.
                            state = DifferentiableAdam.Initialize(state.Coordinates);
                            rate *= .5;
                        }
                        var step = new FixedTopologyStep(attempt, trial.Available, trial.Objective, trial.Failure);
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
            var value = objective!.Evaluate(coordinates, options.ComputeMode, cancellationToken: token);
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

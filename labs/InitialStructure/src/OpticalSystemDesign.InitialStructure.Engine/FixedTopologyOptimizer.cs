using System.Collections.Immutable;
using System.Diagnostics;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine.Optimization;
using static OpticalSystemDesign.InitialStructure.Engine.TopologyValidation;

namespace OpticalSystemDesign.InitialStructure.Engine;

/// <summary>Bounded fixed-topology Adam. No fixed-family indexing or UpstreamE58 state is reused.</summary>
public static class FixedTopologyOptimizer
{
    public static FixedTopologyResult Run(CompiledTopology source, BoundTopologyParameters binding, TopologyTargets targets,
        FixedTopologyOptions? options = null, CancellationToken cancellationToken = default, Action<FixedTopologyStep>? progress = null)
    {
        options ??= new();
        Require(options.MaximumUpdates is >= 0 and <= 400 && options.MaximumFunctionEvaluations is >= 1 and <= 10000
            && options.MaximumGradientEvaluations is >= 0 and <= 10000 && options.TimeLimit > TimeSpan.Zero && options.TimeLimit <= TimeSpan.FromHours(1)
            && double.IsFinite(options.LearningRate) && options.LearningRate is > 0 and <= 1 && Enum.IsDefined(options.ComputeMode),
            TopologyFailureCode.InvalidInput, "Invalid fixed-topology stage budget or learning rate.");
        var timer = Stopwatch.StartNew();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(options.TimeLimit);
        var token = deadline.Token;
        var steps = new List<FixedTopologyStep>();
        var diagnostics = new HashSet<string>(StringComparer.Ordinal);
        var functions = 0; var gradients = 0; var completedGradients = 0; var validations = 0; var updates = 0;
        long rays = 0, probes = 0, gpuRays = 0, gpuBatches = 0;
        var unknownGpuWork = false;
        var current = source;
        TopologyObjectiveEvaluation? initial = null, endpoint = null;
        TopologyAssessment? assessment = null;
        var termination = FixedTopologyTermination.UpdateLimit;
        FixedTopologyObjective? objective = null;
        try { objective = new(source, binding, targets, options.Sampling); }
        catch (Exception error) when (TopologyEvaluationPlan.DomainError(error))
        {
            termination = FixedTopologyTermination.Unsupported;
            diagnostics.Add(error.Message);
        }
        if (objective is not null && !StopRequested())
        {
            if (!CanDifferentiate()) termination = FixedTopologyTermination.BudgetExhausted;
            else
            {
                var state = DifferentiableAdam.Initialize(binding.InitialCoordinates);
                initial = endpoint = Evaluate(state.Coordinates);
                if (!initial.Available) termination = FixedTopologyTermination.DomainFailure;
                else
                {
                    current = TopologyParameterBinder.Apply(source, binding, state.Coordinates);
                    var rate = options.LearningRate;
                    for (var attempt = 1; attempt <= options.MaximumUpdates; attempt++)
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
            if (!StopRequested() && functions < options.MaximumFunctionEvaluations)
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
            Work = new(functions, gradients, validations, rays, probes, unknownGpuWork ? null : gpuRays, unknownGpuWork ? null : gpuBatches, timer.Elapsed, completedGradients),
            Diagnostics = diagnostics.ToImmutableArray()
        };

        bool CanDifferentiate() => functions < options.MaximumFunctionEvaluations - 1 && gradients < options.MaximumGradientEvaluations;
        bool StopRequested()
        {
            if (!token.IsCancellationRequested && timer.Elapsed >= options.TimeLimit) deadline.Cancel();
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

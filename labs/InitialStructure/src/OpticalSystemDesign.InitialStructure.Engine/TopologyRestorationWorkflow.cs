using System.Collections.Immutable;
using System.Diagnostics;
using OpticalSystemDesign.InitialStructure.Contracts;

namespace OpticalSystemDesign.InitialStructure.Engine;

/// <summary>One explicit structural branch: real edit, fresh recovery, fresh local/full Adam,
/// and independent physical acceptance. This is not the M4 candidate search scheduler.</summary>
public static class TopologyRestorationWorkflow
{
    public static TopologyRestorationResult Run(CompiledTopology parent, TopologyEditResult edit,
        IEnumerable<TopologyVariableRequest> recoveryVariables, IEnumerable<TopologyVariableRequest> localVariables,
        IEnumerable<TopologyVariableRequest> fullVariables, TopologyTargets targets,
        TopologyRestorationOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(edit);
        options ??= new();
        var recoveryRequests = recoveryVariables.Take(65).ToImmutableArray();
        var localRequests = localVariables.Take(513).ToImmutableArray();
        var fullRequests = fullVariables.Take(513).ToImmutableArray();
        var timer = Stopwatch.StartNew();
        using var deadline = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var token = linked.Token;
        var termination = TopologyRestorationTermination.EditRejected;
        var diagnostics = new List<string>();
        var fingerprint = "unavailable";
        CompiledTopology? current = null;
        ParaxialRecoveryResult? recovery = null;
        FixedTopologyResult? local = null, full = null;
        TopologyAssessment? assessment = null;
        var functions = 0; var gradients = 0; var validations = 0; var matrices = 0;
        long rays = 0, probes = 0;
        try
        {
            token.ThrowIfCancellationRequested();
            if (options.MaximumFunctionEvaluations is < 1 or > 100_000 || options.MaximumGradientEvaluations is < 1 or > 100_000
                || options.TimeLimit <= TimeSpan.Zero || options.TimeLimit > TimeSpan.FromHours(24))
                throw new ArgumentException("Invalid structural-branch budget.");
            deadline.CancelAfter(options.TimeLimit);
            if (!edit.Succeeded) diagnostics.Add(edit.Failure?.Message ?? "The structural edit is unavailable.");
            else
            {
                if (edit.Change!.ParentFingerprint != parent.TopologyFingerprint
                    || edit.Change.ChildFingerprint != ContentFingerprint.Compute(edit.Topology!))
                    throw new ArgumentException("Structural change identity does not match parent and edited child.");
                ValidateEdit(parent.Topology, edit.Topology!, edit.Change);
                fingerprint = ContentFingerprint.Compute(new
                {
                    Identity = "autonomous-structural-restoration-v1",
                    Parent = parent.BindingFingerprint,
                    edit.Change,
                    recoveryRequests,
                    localRequests,
                    fullRequests,
                    targets,
                    options
                });
                current = TopologyCompiler.Compile(edit.Topology!, parent.Constraints);
                Execute();
                // A failure or budget boundary still gets a fresh physical endpoint assessment when its reserved slot is available.
                if (assessment is null && functions < options.MaximumFunctionEvaluations && !token.IsCancellationRequested)
                {
                    functions++; validations++;
                    assessment = TopologyValidator.Validate(current, targets, token);
                    rays += assessment.TracedRayCount; probes += assessment.GeometryProbes;
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { SetCancellation(); }
        catch (Exception error) when (TopologyEvaluationPlan.DomainError(error))
        {
            termination = TopologyRestorationTermination.Unsupported;
            diagnostics.Add(error.Message);
        }
        if (token.IsCancellationRequested) SetCancellation();
        return new()
        {
            ProtocolFingerprint = fingerprint,
            Parent = parent.Topology,
            EditedChild = edit.Topology,
            Endpoint = current?.Topology ?? parent.Topology,
            Change = edit.Change,
            Termination = termination,
            Constraints = parent.Constraints,
            Targets = targets,
            Options = options,
            RecoveryVariables = recoveryRequests,
            LocalVariables = localRequests,
            FullVariables = fullRequests,
            Recovery = recovery,
            LocalCompensation = local,
            FullOptimization = full,
            FinalAssessment = assessment,
            Work = new(functions, gradients, validations, matrices, rays, probes, timer.Elapsed),
            Diagnostics = diagnostics.Distinct().ToImmutableArray()
        };

        void SetCancellation() => termination = cancellationToken.IsCancellationRequested
            ? TopologyRestorationTermination.Cancelled : TopologyRestorationTermination.TimeLimit;
        TimeSpan Remaining(TimeSpan cap)
        {
            var remaining = options.TimeLimit - timer.Elapsed;
            if (remaining <= TimeSpan.Zero) { deadline.Cancel(); token.ThrowIfCancellationRequested(); }
            return remaining < cap ? remaining : cap;
        }
        void Execute()
        {
            if (options.MaximumFunctionEvaluations <= 1) { termination = TopologyRestorationTermination.BudgetExhausted; return; }
            recovery = ParaxialRestorer.Run(parent, current!, recoveryRequests, targets, options.Recovery with
            {
                MaximumFunctionEvaluations = Math.Min(options.Recovery.MaximumFunctionEvaluations, options.MaximumFunctionEvaluations - 1),
                TimeLimit = Remaining(options.Recovery.TimeLimit)
            }, token);
            functions += recovery.Work.FunctionEvaluations; matrices += recovery.Work.MatrixEvaluations; probes += recovery.Work.GeometryProbes;
            current = TopologyCompiler.Compile(recovery.Topology, parent.Constraints);
            if (recovery.Termination != ParaxialRecoveryTermination.Recovered)
            {
                termination = recovery.Termination switch
                {
                    ParaxialRecoveryTermination.Cancelled => TopologyRestorationTermination.Cancelled,
                    ParaxialRecoveryTermination.TimeLimit => TopologyRestorationTermination.TimeLimit,
                    ParaxialRecoveryTermination.BudgetExhausted => TopologyRestorationTermination.BudgetExhausted,
                    _ => TopologyRestorationTermination.RecoveryFailed
                };
                diagnostics.Add("Recovery: " + recovery.Termination);
                return;
            }
            local = Compensate(options.LocalCompensation, localRequests, reserve: 1);
            if (local is null || !CanContinue(local)) return;
            full = Compensate(options.FullOptimization, fullRequests, reserve: 0);
            if (full is null) return;
            assessment = full.FinalAssessment;
            if (!CanContinue(full)) return;
            termination = assessment?.Completed == true ? TopologyRestorationTermination.Completed : TopologyRestorationTermination.CompensationFailed;
        }
        FixedTopologyResult? Compensate(FixedTopologyOptions stage, ImmutableArray<TopologyVariableRequest> requests, int reserve)
        {
            token.ThrowIfCancellationRequested();
            var available = options.MaximumFunctionEvaluations - functions - reserve;
            var derivativeBudget = options.MaximumGradientEvaluations - gradients;
            if (available < 2 || derivativeBudget < 1) { termination = TopologyRestorationTermination.BudgetExhausted; return null; }
            // A structural or phase change always binds anew and creates fresh optimizer moments.
            var binding = TopologyParameterBinder.Bind(current!, requests);
            var result = FixedTopologyOptimizer.Run(current!, binding, targets, stage with
            {
                MaximumFunctionEvaluations = Math.Min(stage.MaximumFunctionEvaluations, available),
                MaximumGradientEvaluations = Math.Min(stage.MaximumGradientEvaluations, derivativeBudget),
                TimeLimit = Remaining(stage.TimeLimit)
            }, token);
            functions += result.Work.FunctionEvaluations; gradients += result.Work.GradientEvaluations;
            validations += result.Work.ValidationEvaluations; rays += result.Work.TracedRays; probes += result.Work.GeometryProbes;
            current = TopologyCompiler.Compile(result.Topology, parent.Constraints);
            return result;
        }
        bool CanContinue(FixedTopologyResult result)
        {
            if (result.Termination is FixedTopologyTermination.UpdateLimit or FixedTopologyTermination.Converged or FixedTopologyTermination.Stalled) return true;
            termination = result.Termination switch
            {
                FixedTopologyTermination.Cancelled => TopologyRestorationTermination.Cancelled,
                FixedTopologyTermination.TimeLimit => TopologyRestorationTermination.TimeLimit,
                FixedTopologyTermination.BudgetExhausted => TopologyRestorationTermination.BudgetExhausted,
                _ => TopologyRestorationTermination.CompensationFailed
            };
            diagnostics.Add("Compensation: " + result.Termination);
            return false;
        }
    }

    private static void ValidateEdit(DesignTopology parent, DesignTopology child, TopologyChange change)
    {
        if (ContentFingerprint.Compute(parent with { Elements = child.Elements }) != ContentFingerprint.Compute(child))
            throw new ArgumentException("A structural edit cannot change the reference frame, stop, image or task metadata.");
        var retained = child.Elements.Where(element => parent.Elements.Any(original => original.Id == element.Id)).ToArray();
        foreach (var element in retained)
            if (ContentFingerprint.Compute(element) != ContentFingerprint.Compute(parent.Elements.Single(original => original.Id == element.Id)))
                throw new ArgumentException("A structural edit cannot modify a retained lens before the explicit compensation stages.");
        var added = child.Elements.Where(element => !parent.Elements.Any(original => original.Id == element.Id)).ToArray();
        var removed = parent.Elements.Where(element => !child.Elements.Any(updated => updated.Id == element.Id)).ToArray();
        if (change.CreatedElementIds.IsDefault || change.RemovedElementIds.IsDefault
            || !change.CreatedElementIds.SequenceEqual(added.Select(element => element.Id))
            || !change.RemovedElementIds.SequenceEqual(removed.Select(element => element.Id))
            || removed.Any(element => element.Protected)
            || change.Operation switch
            {
                TopologyOperationKind.Insert => added.Length != 1 || removed.Length != 0,
                TopologyOperationKind.Delete => added.Length != 0 || removed.Length != 1,
                _ => true
            })
            throw new ArgumentException("The edit must represent exactly one permitted insertion or deletion with matching stable IDs.");
    }
}

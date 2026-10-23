using System.Diagnostics;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;

namespace OpticalSystemDesign.InitialStructure.Engine;

/// <summary>Deterministic continuation of one exact-flat family using the formal optical engine.</summary>
public sealed class FlatStartDesignService
{
    public FlatStartDesignResult Solve(InitialStructureSpecification specification, int elementCount,
        CancellationToken cancellationToken = default, FlatStartFamily? family = null, bool screenOnly = false,
        IReadOnlyList<string>? allowedGlassNames = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SpecificationValidator.Validate(specification);
        var stopwatch = Stopwatch.StartNew();
        using var compute = specification.FlatStart!.OptimizationMethod == FlatStartOptimizationMethod.AutoLensManagedV1
            ? new SequentialGpuSession(specification.FlatStart.AutoLens?.ComputeMode ?? Core.Differentiation.DesignComputeMode.Cpu,cancellationToken) : null;
        var budget = specification.Budget.MaximumEvaluations;
        var progressive = specification.FlatStart!.SamplingPolicy == FlatStartSamplingPolicy.UniformAreaGaussianV1;
        var bootstrap = new FlatStartBootstrap().Solve(specification with
        {
            Budget = specification.Budget with { MaximumEvaluations = Math.Max(1, Math.Min(800, progressive ? budget / 3 : budget - 1)) }
        }, elementCount, cancellationToken, family);
        if (specification.FlatStart.OptimizationMethod == FlatStartOptimizationMethod.AutoLensManagedV1)
            return AutoLensManagedDesign.Run(specification, elementCount, bootstrap, stopwatch, family, null, null,
                allowedGlassNames ?? [specification.InitialGlass], cancellationToken, screenOnly: screenOnly);
        return Run(specification, elementCount, bootstrap, stopwatch, family, null, null, cancellationToken, false, screenOnly);
    }

    internal FlatStartDesignResult Continue(InitialStructureSpecification specification, FlatStartFamily family,
        FlatStartBootstrapResult rootProof, OpticSnapshot start, string parentCandidateId,
        CancellationToken cancellationToken = default, bool improveBeyondTargets = false, bool screenOnly = false,
        IReadOnlyList<string>? allowedGlassNames = null)
    {
        SpecificationValidator.Validate(specification);
        if (specification.FlatStart!.OptimizationMethod == FlatStartOptimizationMethod.AutoLensManagedV1)
            return AutoLensManagedDesign.Run(specification, family.ElementCount, rootProof, Stopwatch.StartNew(), family, start, parentCandidateId,
                allowedGlassNames ?? [specification.InitialGlass], cancellationToken, screenOnly: screenOnly);
        return Run(specification, family.ElementCount, rootProof, Stopwatch.StartNew(), family, start, parentCandidateId, cancellationToken, improveBeyondTargets, screenOnly);
    }

    public FlatStartDesignResult ResumeAutoLens(InitialStructureSpecification specification, FlatStartBootstrapResult rootProof,
        AutoLensManagedState state, IReadOnlyList<string> allowedGlassNames, FlatStartFamily? family = null,
        CancellationToken cancellationToken = default)
    {
        SpecificationValidator.Validate(specification);
        if (specification.FlatStart!.OptimizationMethod != FlatStartOptimizationMethod.AutoLensManagedV1)
            throw new ArgumentException("This state requires the native automatic differentiation strategy.", nameof(specification));
        AutoLensManagedStateValidation.Validate(state);
        if (rootProof.Steps is not { Count: > 0 }) throw new InvalidDataException("A saved strict-flat root proof is required.");
        var root = rootProof.Steps[0].Optic;
        OpticSnapshotValidator.Validate(root);
        if (root.Surfaces.Count < 4 || (root.Surfaces.Count - 2) % 2 != 0 || root.Surfaces.Count != state.Optic.Surfaces.Count
            || root.Surfaces.Any(surface => surface.Radius != 0 || surface.Components?.GeometryKind is not (null or "plane" or "standard")))
            throw new InvalidDataException("The saved ancestor is not an exact-flat design of the same topology.");
        var elements = rootProof.Steps[0].Optic.Surfaces.Count / 2 - 1;
        return AutoLensManagedDesign.Run(specification, elements, rootProof, Stopwatch.StartNew(), family, state.Optic, state.ParentCandidateId,
            allowedGlassNames, cancellationToken, state);
    }

    private static FlatStartDesignResult Run(InitialStructureSpecification specification, int elementCount,
        FlatStartBootstrapResult bootstrap, Stopwatch stopwatch, FlatStartFamily? family,
        OpticSnapshot? restart, string? parentCandidateId, CancellationToken cancellationToken, bool improveBeyondTargets, bool screenOnly = false)
    {
        var budget = specification.Budget.MaximumEvaluations;
        var progressive = specification.FlatStart!.SamplingPolicy == FlatStartSamplingPolicy.UniformAreaGaussianV1;
        var count = restart is null ? bootstrap.EvaluationCount : 0;
        var autoLensCpu = specification.FlatStart.OptimizationMethod == FlatStartOptimizationMethod.AutoLensCpuExperimentV1;
        var startingOptic = restart ?? bootstrap.Steps[^1].Optic;
        var problem = new FlatStartDesignProblem(specification, elementCount, startingOptic, family);
        var vector = problem.Vector(startingOptic);
        var steps = new List<DesignStep>();
        var diagnostics = new List<SearchDiagnostic>
        {
            new("design.scope", family is null
                ? "Fixed element count, initial catalog glass and front stop; clear apertures follow the specified diameter policy; no material or topology search."
                : "Explicit catalog material allocation and stop surface; all geometry and optical results use formal Core."),
            new("design.engine", "Formal Core sampled spot analysis, common polychromatic centroid, geometric pupil weights and real aperture clipping.")
        };
        if (autoLensCpu) diagnostics.Add(new("design.autolens-cpu-experiment-v1",
            "AutoLens-inspired CPU adaptation: cosine aperture 0.5 to 1, full field/all wavelengths, finite-difference bounded Adam, then native full-target trust-region polish. Fixed real catalog glasses; no autograd, CUDA, continuous material search or upstream numerical equivalence."));
        var canContinue = restart is not null || bootstrap.State is FlatStartBootstrapState.Focused or FlatStartBootstrapState.Initialized;
        var state = canContinue
            ? FlatStartDesignState.TargetsNotMet : FlatStartDesignState.StartupFailed;
        var refinedAtFullTarget = false;
        double[]? incumbent = null;
        DesignEvaluation? incumbentEvaluation = null;
        if (restart is not null && !screenOnly && CanEvaluate(true))
        {
            // A full-field candidate must not be degraded by replaying reduced monochromatic stages.
            var evaluation = Evaluate(vector, FlatStartDesignProblem.FullStage, dense: true);
            RetainFullTarget(evaluation);
            // A finite geometry violation belongs in the joint constrained objective.
            // Only missing optical data requires restarting the reduced-field path;
            // final manufacturing and optical acceptance remain separate hard gates.
            if (evaluation.HasContinuousSearchResiduals)
            {
                steps.Add(new(count, "full-target-refinement-entry", FlatStartDesignProblem.FullStage,
                    evaluation.EvaluatedOptic!, evaluation));
                Optimize(FlatStartDesignProblem.FullStage, evaluation, budget - 1, dense: true);
                refinedAtFullTarget = true;
            }
        }
        if (canContinue && !refinedAtFullTarget && progressive)
        {
            DesignStage? established = null;
            double[]? establishedVector = null;
            var stages = autoLensCpu ? AutoLensCpuCurriculum.Stages : FlatStartContinuation.ProgressiveStages;
            for (var index = 0; index < stages.Count && CanEvaluate(true); index++)
            {
                var stage = stages[index];
                var window = FlatStartContinuation.ProgressiveWindow(budget - count - 1, problem.Dimension, index, stages.Count);
                if (window <= 1) continue;
                var stageEnd = Math.Min(budget - 1, count + window);
                var dense = stage == FlatStartDesignProblem.FullStage;
                var evaluation = Evaluate(vector, stage, dense);
                if (dense) RetainFullTarget(evaluation);
                steps.Add(new(count, autoLensCpu ? "autolens-cpu-stage-entry" : "progressive-stage-entry", stage, evaluation.EvaluatedOptic!, evaluation));
                var local = Optimize(stage, evaluation, stageEnd, dense);
                evaluation = local?.Evaluation ?? evaluation;
                var failedStage = stage;
                // A missing real-ray bundle has no optical Jacobian. Approach the failed
                // milestone from the last traceable stage instead of advancing through it.
                for (var backoff = 0; !evaluation.HasContinuousSearchResiduals
                    && established is not null && backoff < 8 && count + 2 < stageEnd && CanEvaluate(true); backoff++)
                {
                    vector = establishedVector!.ToArray();
                    var bridge = new DesignStage((established.PupilFraction + failedStage.PupilFraction) / 2,
                        (established.FieldFraction + failedStage.FieldFraction) / 2, failedStage.AllWavelengths);
                    var bridged = Evaluate(vector, bridge);
                    steps.Add(new(count, "progressive-bridge-entry", bridge, bridged.EvaluatedOptic!, bridged));
                    diagnostics.Add(new("design.progressive-backoff", $"Missing optical data at {failedStage}; trying {bridge}."));
                    if (!bridged.HasContinuousSearchResiduals)
                    {
                        failedStage = bridge;
                        continue;
                    }
                    // Keep a window for retrying the original milestone. Intermediate
                    // targets are only a path to that milestone, not stopping criteria.
                    var bridgeEnd = count + Math.Max(0, (stageEnd - count - 1) / 2);
                    Optimize(bridge, bridged, bridgeEnd, refinePastTargets: true);
                    established = bridge;
                    establishedVector = vector.ToArray();
                    if (!CanEvaluate(true)) break;
                    evaluation = Evaluate(vector, stage, dense);
                    if (dense) RetainFullTarget(evaluation);
                    steps.Add(new(count, "progressive-stage-retry", stage, evaluation.EvaluatedOptic!, evaluation));
                    local = Optimize(stage, evaluation, stageEnd, dense);
                    evaluation = local?.Evaluation ?? evaluation;
                    failedStage = stage;
                }
                if (!evaluation.HasContinuousSearchResiduals)
                {
                    diagnostics.Add(new("design.progressive-blocked", $"No complete optical bundle at {stage}; full-target validation remains mandatory."));
                    break;
                }
                established = stage;
                establishedVector = vector.ToArray();
            }
        }
        if (canContinue && !refinedAtFullTarget && !progressive)
        {
            var continuation = new FlatStartContinuation();
            while (CanEvaluate(true) && !continuation.Exhausted)
            {
                var stage = continuation.Proposed;
                var before = vector.ToArray();
                var evaluation = Evaluate(vector, stage);
                var initialViolation = FlatStartContinuation.Violation(evaluation);
                var initialRank = FlatStartContinuation.FeasibilityRank(evaluation);
                steps.Add(new(count, "stage-entry", stage, evaluation.EvaluatedOptic!, evaluation));
                // A bounded local window supplies measured progress before allocating
                // another window. There is no equal split across predetermined fields.
                var window = 8 * (2 * problem.Dimension + 1);
                var local = Optimize(stage, evaluation, Math.Min(budget - 1, count + window));
                evaluation = local?.Evaluation ?? evaluation;
                if (evaluation.MeetsTargets)
                {
                    continuation.Accept();
                    if (stage == FlatStartDesignProblem.FullStage) break;
                    continue;
                }
                var rank = FlatStartContinuation.FeasibilityRank(evaluation);
                var progressed = rank > initialRank || rank == initialRank
                    && FlatStartContinuation.Violation(evaluation) < initialViolation * (1 - 1e-6);
                if (progressed && local?.Termination == Optimization.LeastSquaresTermination.EvaluationLimit)
                    continue;
                if (!progressed) vector = before;
                continuation.Backoff();
                diagnostics.Add(new("design.stage-backoff", $"Reduced pupil/field after {local?.Termination}; next {continuation.Proposed}."));
            }
        }

        DesignEvaluation? validation = null;
        CandidateSnapshot? candidate = null;
        if (!screenOnly && !refinedAtFullTarget && canContinue && count + 2 * problem.Dimension + 3 < budget && CanEvaluate(true))
        {
            // Coarse sampling can pass while the independent denser field/pupil grid still fails.
            var evaluation = Evaluate(vector, FlatStartDesignProblem.FullStage, dense: true);
            RetainFullTarget(evaluation);
            if (evaluation.HasContinuousSearchResiduals)
            {
                steps.Add(new(count, "dense-target-repair-entry", FlatStartDesignProblem.FullStage,
                    evaluation.EvaluatedOptic!, evaluation));
                Optimize(FlatStartDesignProblem.FullStage, evaluation, budget - 1, dense: true);
            }
        }
        if (incumbent is not null) vector = incumbent;
        if (CanEvaluate(reserveFinal: false))
        {
            // A new Optic is constructed; full target and frozen denser samples never inherit a reduced stage.
            validation = Evaluate(vector, FlatStartDesignProblem.FullStage, dense: true, independentValidation: progressive);
            var optic = validation.EvaluatedOptic!;
            steps.Add(new(count, "full-target-dense-validation", FlatStartDesignProblem.FullStage, optic, validation));
            var fingerprint = ContentFingerprint.Compute(optic);
            candidate = new()
            {
                CandidateId = "flat-design-" + fingerprint[..24],
                OpticFingerprint = fingerprint,
                FlatRootOptic = bootstrap.Steps[0].Optic,
                Optic = optic,
                Status = validation.MeetsTargets ? CandidateStatus.LabAccepted
                    : validation.IsFeasible ? CandidateStatus.TraceValid : CandidateStatus.Rejected,
                Lineage = new()
                {
                    RootFingerprint = ContentFingerprint.Compute(bootstrap.Steps[0].Optic),
                    Operation = FlatStartAlgorithm.Name(family is null ? $"strict-flat-design-v{FlatStartAlgorithm.Version}"
                        : $"strict-flat-family-design-v{FlatStartAlgorithm.Version}", specification),
                    ParentCandidateId = parentCandidateId,
                    Generation = bootstrap.Steps.Count + steps.Count - 1,
                    ElementCount = elementCount,
                    StopVariant = family?.StopSurfaceIndex ?? 0,
                    SeedIndex = family?.SeedIndex ?? 0,
                    InitialForm = family?.BinaryStart?.Signs
                },
                Evaluation = new()
                {
                    FlatStartObjective = validation.Objective,
                    EffectiveFocalLengthMillimeters = validation.EffectiveFocalLengthMillimeters,
                    FNumber = validation.FNumber,
                    RmsSpotRadiusMillimeters = validation.Fields.Max(field => field.RmsRadiusMillimeters),
                    MaximumSpotRadiusMillimeters = validation.Fields.Max(field => field.MaximumRadiusMillimeters),
                    EvaluatedRayCount = validation.Fields.Sum(field => field.AttemptedRays + field.CheckAttemptedRays),
                    ValidRayCount = validation.Fields.Sum(field => field.ValidRays + field.CheckValidRays),
                    ValidRayFraction = (double)validation.Fields.Sum(field => field.ValidRays + field.CheckValidRays)
                        / validation.Fields.Sum(field => field.AttemptedRays + field.CheckAttemptedRays)
                },
                Violations = validation.Violations
            };
            if (validation.MeetsTargets) state = FlatStartDesignState.Accepted;
        }
        if (state != FlatStartDesignState.Accepted)
        {
            if (stopwatch.Elapsed >= specification.Budget.TimeLimit) state = FlatStartDesignState.TimeLimit;
            else if (count >= budget) state = FlatStartDesignState.BudgetExhausted;
        }
        if (validation is null) diagnostics.Add(new("design.validation-not-run", "Budget or time ended before full-target validation; no accepted candidate is published."));
        return new()
        {
            Algorithm = new(FlatStartAlgorithm.Name(family is null ? "strict-flat-design" : "strict-flat-family-design", specification),
                FlatStartAlgorithm.Version, "Managed CPU", true),
            Specification = specification,
            SpecificationFingerprint = ContentFingerprint.Compute(specification),
            State = state,
            Bootstrap = bootstrap,
            EvaluationCount = count,
            TracedRayCount = (restart is null ? bootstrap.TracedRayCount : 0) + problem.TracedRayCount,
            Steps = steps,
            FinalValidation = validation,
            Candidate = candidate,
            Diagnostics = diagnostics
        };

        bool CanEvaluate(bool reserveFinal)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return count < budget - (reserveFinal ? 1 : 0) && stopwatch.Elapsed < specification.Budget.TimeLimit;
        }

        DesignEvaluation Evaluate(double[] values, DesignStage stage, bool dense = false, bool independentValidation = false)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (count >= budget) throw new InvalidOperationException("Design evaluation budget exceeded.");
            count++;
            return problem.Evaluate(problem.CreateOptic(values, stage), stage, dense, cancellationToken, independentValidation);
        }

        void RetainFullTarget(DesignEvaluation evaluation)
        {
            // Never restore a failed full-field probe over a later traceable bridge.
            if (!evaluation.HasContinuousSearchResiduals) return;
            if (incumbentEvaluation is not null)
            {
                var rank = evaluation.MeetsTargets ? 0 : evaluation.IsFeasible ? 1 : 2;
                var previousRank = incumbentEvaluation.MeetsTargets ? 0 : incumbentEvaluation.IsFeasible ? 1 : 2;
                if (rank > previousRank || rank == previousRank
                    && FlatStartCandidateArchive.Score(evaluation.Objective) >= FlatStartCandidateArchive.Score(incumbentEvaluation.Objective)) return;
            }
            incumbent = vector.ToArray();
            incumbentEvaluation = evaluation;
        }

        OpticalLocalResult? Optimize(DesignStage stage, DesignEvaluation evaluation, int stageEnd, bool dense = false,
            bool refinePastTargets = false)
        {
            if (stageEnd <= count || !CanEvaluate(true)) return null;
            if (autoLensCpu && !dense)
            {
                var adam = AutoLensCpuCurriculum.Solve(problem.SolverCoordinates, vector, evaluation, stageEnd - count,
                    values => Evaluate(values, stage), () => !CanEvaluate(true), cancellationToken,
                    (values, accepted) =>
                    {
                        vector = values;
                        steps.Add(new(count, "autolens-cpu-adam-step", stage, accepted.EvaluatedOptic!, accepted));
                    },
                    statistics => diagnostics.Add(new("design.autolens-cpu-adam",
                        $"{statistics.Termination}; {statistics.EvaluationCount} evaluations, {statistics.DifferenceEvaluationCount} difference probes, {statistics.AcceptedSteps} accepted steps, {statistics.WorkUnits} rays.")),
                    () => problem.TracedRayCount);
                vector = adam.Variables;
                return adam;
            }
            var result = FlatStartLocalSolver.Solve(problem.SolverCoordinates, vector, evaluation, stageEnd - count,
                values => Evaluate(values, stage, dense), improveBeyondTargets || refinePastTargets,
                () => !CanEvaluate(true), cancellationToken,
                (values, accepted, phase) =>
                {
                    vector = values;
                    if (dense) RetainFullTarget(accepted);
                    steps.Add(new(count, "curvature-thickness-step", stage, accepted.EvaluatedOptic!, accepted));
                },
                (phase, statistics) => diagnostics.Add(new("design.local-solver",
                    $"{phase}: {statistics.Termination}; {statistics.EvaluationCount} evaluations, {statistics.WorkUnits} rays, {statistics.JacobianBuildCount} Jacobians, {statistics.SecantUpdateCount} secant updates, {statistics.FactorizationCount} QR factorizations.")),
                () => problem.TracedRayCount);
            vector = result.Variables;
            return result;
        }
    }
}

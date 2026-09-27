using System.Diagnostics;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.InitialStructure.Contracts;

namespace OptilandWorkbench.InitialStructure.Engine;

/// <summary>Deterministic continuation of one exact-flat family using the formal optical engine.</summary>
public sealed class FlatStartDesignService
{
    public FlatStartDesignResult Solve(InitialStructureSpecification specification, int elementCount,
        CancellationToken cancellationToken = default, FlatStartFamily? family = null, bool screenOnly = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SpecificationValidator.Validate(specification);
        var stopwatch = Stopwatch.StartNew();
        var budget = specification.Budget.MaximumEvaluations;
        var progressive = specification.FlatStart!.SamplingPolicy == FlatStartSamplingPolicy.UniformAreaGaussianV1;
        var bootstrap = new FlatStartBootstrap().Solve(specification with
        {
            Budget = specification.Budget with { MaximumEvaluations = Math.Max(1, Math.Min(800, progressive ? budget / 3 : budget - 1)) }
        }, elementCount, cancellationToken, family);
        return Run(specification, elementCount, bootstrap, stopwatch, family, null, null, cancellationToken, false, screenOnly);
    }

    internal FlatStartDesignResult Continue(InitialStructureSpecification specification, FlatStartFamily family,
        FlatStartBootstrapResult rootProof, OpticSnapshot start, string parentCandidateId,
        CancellationToken cancellationToken = default, bool improveBeyondTargets = false, bool screenOnly = false)
    {
        SpecificationValidator.Validate(specification);
        return Run(specification, family.ElementCount, rootProof, Stopwatch.StartNew(), family, start, parentCandidateId, cancellationToken, improveBeyondTargets, screenOnly);
    }

    private static FlatStartDesignResult Run(InitialStructureSpecification specification, int elementCount,
        FlatStartBootstrapResult bootstrap, Stopwatch stopwatch, FlatStartFamily? family,
        OpticSnapshot? restart, string? parentCandidateId, CancellationToken cancellationToken, bool improveBeyondTargets, bool screenOnly = false)
    {
        var budget = specification.Budget.MaximumEvaluations;
        var progressive = specification.FlatStart!.SamplingPolicy == FlatStartSamplingPolicy.UniformAreaGaussianV1;
        var count = restart is null ? bootstrap.EvaluationCount : 0;
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
            for (var index = 0; index < FlatStartContinuation.ProgressiveStages.Count && CanEvaluate(true); index++)
            {
                var stage = FlatStartContinuation.ProgressiveStages[index];
                var window = FlatStartContinuation.ProgressiveWindow(budget - count - 1, problem.Dimension, index);
                if (window <= 1) continue;
                var stageEnd = Math.Min(budget - 1, count + window);
                var dense = stage == FlatStartDesignProblem.FullStage;
                var evaluation = Evaluate(vector, stage, dense);
                if (dense) RetainFullTarget(evaluation);
                steps.Add(new(count, "progressive-stage-entry", stage, evaluation.EvaluatedOptic!, evaluation));
                Optimize(stage, evaluation, stageEnd, dense);
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
                    Operation = family is null ? $"strict-flat-design-v{FlatStartAlgorithm.Version}" : $"strict-flat-family-design-v{FlatStartAlgorithm.Version}",
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
            Algorithm = family is null ? new("strict-flat-design", FlatStartAlgorithm.Version, "Managed CPU", true)
                : new("strict-flat-family-design", FlatStartAlgorithm.Version, "Managed CPU", true),
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

        OpticalLocalResult? Optimize(DesignStage stage, DesignEvaluation evaluation, int stageEnd, bool dense = false)
        {
            if (stageEnd <= count || !CanEvaluate(true)) return null;
            var result = FlatStartLocalSolver.Solve(problem.SolverCoordinates, vector, evaluation, stageEnd - count,
                values => Evaluate(values, stage, dense), improveBeyondTargets,
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

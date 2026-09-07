using System.Diagnostics;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.InitialStructure.Contracts;

namespace OptilandWorkbench.InitialStructure.Engine;

/// <summary>Deterministic continuation of one exact-flat family using the formal optical engine.</summary>
public sealed class FlatStartDesignService
{
    public FlatStartDesignResult Solve(InitialStructureSpecification specification, int elementCount,
        CancellationToken cancellationToken = default, FlatStartFamily? family = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SpecificationValidator.Validate(specification);
        var stopwatch = Stopwatch.StartNew();
        var budget = specification.Budget.MaximumEvaluations;
        var bootstrap = new FlatStartBootstrap().Solve(specification with
        {
            Budget = specification.Budget with { MaximumEvaluations = Math.Max(1, Math.Min(800, budget - 1)) }
        }, elementCount, cancellationToken, family);
        return Run(specification, elementCount, bootstrap, stopwatch, family, null, null, cancellationToken);
    }

    internal FlatStartDesignResult Continue(InitialStructureSpecification specification, FlatStartFamily family,
        FlatStartBootstrapResult rootProof, OpticSnapshot start, string parentCandidateId,
        CancellationToken cancellationToken = default)
    {
        SpecificationValidator.Validate(specification);
        return Run(specification, family.ElementCount, rootProof, Stopwatch.StartNew(), family, start, parentCandidateId, cancellationToken);
    }

    private static FlatStartDesignResult Run(InitialStructureSpecification specification, int elementCount,
        FlatStartBootstrapResult bootstrap, Stopwatch stopwatch, FlatStartFamily? family,
        OpticSnapshot? restart, string? parentCandidateId, CancellationToken cancellationToken)
    {
        var budget = specification.Budget.MaximumEvaluations;
        var count = restart is null ? bootstrap.EvaluationCount : 0;
        var startingOptic = restart ?? bootstrap.Steps[^1].Optic;
        var problem = new FlatStartDesignProblem(specification, elementCount, startingOptic, family);
        var vector = problem.Vector(startingOptic);
        var steps = new List<DesignStep>();
        var diagnostics = new List<SearchDiagnostic>
        {
            new("design.scope", family is null
                ? "Fixed element count, initial catalog glass, front stop and frozen clear apertures; no material or topology search."
                : "Explicit catalog material allocation and stop surface; all geometry and optical results use formal Core."),
            new("design.engine", "Formal Core sampled spot analysis, common polychromatic centroid, geometric pupil weights and real aperture clipping.")
        };
        var canContinue = restart is not null || bootstrap.State == FlatStartBootstrapState.Focused;
        var state = canContinue
            ? FlatStartDesignState.TargetsNotMet : FlatStartDesignState.StartupFailed;
        DesignStage currentStage = new(.3, 0, false);
        DesignStage[] targets = [new(.6, .25, false), new(1, .5, false), new(1, 1, false), new(1, 1, true)];
        if (canContinue)
        {
            for (var targetIndex = 0; targetIndex < targets.Length && CanEvaluate(reserveFinal: true); targetIndex++)
            {
                var target = targets[targetIndex];
                if (!specification.Wavelengths.Any(wave => !wave.IsPrimary && wave.Weight > 0) && targetIndex == 3) break;
                var stageEnd = Math.Min(budget - 1, count + Math.Max(2 * problem.Dimension + 2,
                    (budget - count - 1) / (targets.Length - targetIndex)));
                var nextStage = target;
                var reached = false;
                for (var subdivisions = 0; subdivisions < 6 && count < stageEnd && CanEvaluate(true); subdivisions++)
                {
                    var evaluation = Evaluate(vector, nextStage);
                    if (!evaluation.IsFeasible)
                    {
                        if (nextStage.AllWavelengths != currentStage.AllWavelengths) break;
                        nextStage = new((currentStage.PupilFraction + nextStage.PupilFraction) / 2,
                            (currentStage.FieldFraction + nextStage.FieldFraction) / 2, nextStage.AllWavelengths);
                        continue;
                    }
                    steps.Add(new(count, "stage-entry", nextStage, problem.CreateOptic(vector, nextStage).ToSnapshot(), evaluation));
                    Optimize(nextStage, evaluation, stageEnd);
                    currentStage = nextStage;
                    if (nextStage == target) { reached = true; break; }
                    nextStage = target;
                }
                if (!reached)
                {
                    diagnostics.Add(new("design.continuation-incomplete",
                        $"Could not complete pupil {target.PupilFraction:P0}, field {target.FieldFraction:P0}, all wavelengths {target.AllWavelengths}; final validation still uses the full request."));
                    break;
                }
            }
        }

        DesignEvaluation? validation = null;
        CandidateSnapshot? candidate = null;
        if (CanEvaluate(reserveFinal: false))
        {
            // A new Optic is constructed; full target and frozen denser samples never inherit a reduced stage.
            validation = Evaluate(vector, FlatStartDesignProblem.FullStage, dense: true);
            var optic = problem.CreateOptic(vector, FlatStartDesignProblem.FullStage).ToSnapshot();
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
                    Operation = family is null ? "strict-flat-design-v1" : "strict-flat-family-design-v1",
                    ParentCandidateId = parentCandidateId,
                    Generation = bootstrap.Steps.Count + steps.Count - 1,
                    ElementCount = elementCount,
                    StopVariant = family?.StopSurfaceIndex ?? 0,
                    SeedIndex = family?.SeedIndex ?? 0
                },
                Evaluation = new()
                {
                    EffectiveFocalLengthMillimeters = validation.EffectiveFocalLengthMillimeters,
                    FNumber = validation.FNumber,
                    RmsSpotRadiusMillimeters = validation.Fields.Max(field => field.RmsRadiusMillimeters),
                    MaximumSpotRadiusMillimeters = validation.Fields.Max(field => field.MaximumRadiusMillimeters),
                    EvaluatedRayCount = validation.Fields.Sum(field => field.AttemptedRays),
                    ValidRayCount = validation.Fields.Sum(field => field.ValidRays),
                    ValidRayFraction = (double)validation.Fields.Sum(field => field.ValidRays) / validation.Fields.Sum(field => field.AttemptedRays)
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
            Algorithm = family is null ? new("strict-flat-design", "1", "Managed CPU", true)
                : new("strict-flat-family-design", "1", "Managed CPU", true),
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

        DesignEvaluation Evaluate(double[] values, DesignStage stage, bool dense = false)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (count >= budget) throw new InvalidOperationException("Design evaluation budget exceeded.");
            count++;
            return problem.Evaluate(problem.CreateOptic(values, stage), stage, dense, cancellationToken);
        }

        void Optimize(DesignStage stage, DesignEvaluation evaluation, int stageEnd)
        {
            var damping = 1e-3;
            var stalled = 0;
            while (count + 2 * problem.Dimension + 1 <= stageEnd && CanEvaluate(true) && !evaluation.MeetsTargets)
            {
                var jacobian = new double[evaluation.Residuals.Count, problem.Dimension];
                for (var column = 0; column < problem.Dimension; column++)
                {
                    if (!CanEvaluate(true)) return;
                    var delta = 1e-5 * Math.Max(1, Math.Abs(vector[column]));
                    var plus = vector.ToArray();
                    var minus = vector.ToArray();
                    plus[column] += delta;
                    minus[column] -= delta;
                    plus = problem.Project(plus);
                    minus = problem.Project(minus);
                    var upper = Evaluate(plus, stage);
                    if (!CanEvaluate(true)) return;
                    var lower = Evaluate(minus, stage);
                    // One-sided derivatives at active geometry bounds; never differentiate synthetic penalties.
                    if (!upper.IsFeasible) { upper = evaluation; plus = vector; }
                    if (!lower.IsFeasible) { lower = evaluation; minus = vector; }
                    var span = plus[column] - minus[column];
                    if (Math.Abs(span) < 1e-15) continue;
                    for (var row = 0; row < evaluation.Residuals.Count; row++)
                        jacobian[row, column] = (upper.Residuals[row] - lower.Residuals[row]) / span;
                }
                var direction = FlatStartBootstrap.DampedStep(jacobian, evaluation.Residuals, damping);
                var largest = direction.Select(Math.Abs).Max();
                var scale = largest > .2 ? .2 / largest : 1;
                var accepted = false;
                for (var attempt = 0; attempt < 8 && count < stageEnd && CanEvaluate(true); attempt++, scale /= 2)
                {
                    var trial = problem.Project(vector.Select((value, index) => value + scale * direction[index]).ToArray());
                    var proposed = Evaluate(trial, stage);
                    if (!proposed.IsFeasible || proposed.Merit >= evaluation.Merit - 1e-12) continue;
                    vector = trial;
                    evaluation = proposed;
                    steps.Add(new(count, "curvature-thickness-step", stage, problem.CreateOptic(vector, stage).ToSnapshot(), evaluation));
                    accepted = true;
                    break;
                }
                if (accepted) { damping = Math.Max(1e-9, damping / 3); stalled = 0; }
                else { damping = Math.Min(1e9, damping * 10); if (++stalled >= 8) break; }
            }
        }
    }
}

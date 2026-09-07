using System.Diagnostics;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.InitialStructure.Contracts;

namespace OptilandWorkbench.InitialStructure.Engine;

/// <summary>Family proposals, accounting and provenance only; optical evaluation belongs to formal Core.</summary>
public sealed class FlatStartSearchService
{
    public static FlatStartPreflight Preflight(InitialStructureSpecification specification, FlatStartSearchOptions options)
    {
        FlatStartSearchPlanning.Validate(specification, options);
        var materials = FlatStartSearchPlanning.Materials(specification, options);
        return new(materials.Names, materials.Diagnostics);
    }

    public static IReadOnlyList<CandidateSnapshot> SelectCandidates(FlatStartSearchCheckpoint checkpoint) =>
        FlatStartCandidateArchive.Select(checkpoint.Specification, checkpoint.Options,
            checkpoint.Origin is { } origin ? checkpoint.Trials.Prepend(origin.Source) : checkpoint.Trials);

    public static FlatStartSearchCheckpoint CreateRefinementCheckpoint(FlatStartSearchCheckpoint source,
        string candidateId, int additionalEvaluations, TimeSpan additionalTime)
    {
        var materials = FlatStartSearchPlanning.Materials(source.Specification, source.Options);
        var roots = source.Origin is null ? FlatStartSearchPlanning.Roots(source.Specification, materials.Names) : [];
        ValidateResume(source, source.Specification, source.Options, materials, roots, RootQuota(source.Specification, source.Options, roots.Count));
        if (source.Trials.Any(trial => trial.State == FamilyTrialState.Reserved))
            throw new InvalidOperationException("Finish or restore the active batch before creating a refinement run.");
        var selected = source.Trials.FirstOrDefault(trial => trial.Candidate?.CandidateId == candidateId)
            ?? (source.Origin?.Source.Candidate?.CandidateId == candidateId ? source.Origin.Source : null)
            ?? throw new ArgumentException("The selected candidate does not belong to this run.", nameof(candidateId));
        var ancestor = selected;
        // A selected origin may itself come from a previous run; its supplied proof is already complete.
        if (ReferenceEquals(selected, source.Origin?.Source)) ancestor = source.Origin!.AsParent();
        else while (ancestor.ParentTrialId is { } parentId) ancestor = Parent(source, parentId);
        var specification = source.Specification with
        {
            Budget = source.Specification.Budget with
            { MaximumEvaluations = additionalEvaluations, TimeLimit = additionalTime }
        };
        FlatStartSearchPlanning.Validate(specification, source.Options);
        return new()
        {
            Algorithm = new("strict-flat-selected-refinement", "1", "Managed CPU", true),
            RunId = "flat-refine-" + Guid.NewGuid().ToString("N"),
            Specification = specification,
            Options = source.Options,
            SpecificationFingerprint = ContentFingerprint.Compute(specification),
            OptionsFingerprint = source.OptionsFingerprint,
            MaterialFingerprint = materials.Fingerprint,
            UsableGlassNames = materials.Names,
            Origin = new(source.RunId, source.Specification, materials.Fingerprint, source.ChargedEvaluations, selected, ancestor.BootstrapProof!),
            Diagnostics = [new("refinement.origin", "A separate budget refines the selected candidate; source targets and history are preserved.")]
        };
    }

    private static int RootQuota(InitialStructureSpecification specification, FlatStartSearchOptions options, int rootCount) =>
        rootCount == 0 ? 0 : Math.Min(options.MaximumEvaluationsPerTrial,
            Math.Min(specification.Budget.MaximumEvaluations / rootCount,
                Math.Max(2, (int)(.55 * specification.Budget.MaximumEvaluations / rootCount))));

    public async Task<FlatStartSearchResult> RunAsync(InitialStructureSpecification specification,
        FlatStartSearchOptions? options = null, CancellationToken cancellationToken = default,
        FlatStartSearchCheckpoint? checkpoint = null,
        Func<FlatStartSearchCheckpoint, CancellationToken, ValueTask>? checkpointSink = null)
    {
        options ??= new();
        FlatStartSearchPlanning.Validate(specification, options);
        var materials = FlatStartSearchPlanning.Materials(specification, options);
        var roots = checkpoint?.Origin is null ? FlatStartSearchPlanning.Roots(specification, materials.Names) : [];
        var quota = RootQuota(specification, options, roots.Count);
        var current = checkpoint ?? new FlatStartSearchCheckpoint
        {
            RunId = "flat-search-" + Guid.NewGuid().ToString("N"),
            Specification = specification,
            Options = options,
            SpecificationFingerprint = ContentFingerprint.Compute(specification),
            OptionsFingerprint = ContentFingerprint.Compute(options),
            MaterialFingerprint = materials.Fingerprint,
            UsableGlassNames = materials.Names,
            RootPlan = roots,
            RootEvaluationQuota = quota,
            Diagnostics = materials.Diagnostics
        };
        if (checkpoint is not null)
        {
            ValidateResume(current, specification, options, materials, roots, quota);
            if (current.Trials.Any(trial => trial.State == FamilyTrialState.Reserved))
                current = current with
                {
                    Trials = current.Trials.Select(trial => trial.State == FamilyTrialState.Reserved
                        ? trial with
                        {
                            State = FamilyTrialState.Interrupted,
                            Diagnostics = [new("search.interrupted-quota", "Completion is unknown; the entire reserved evaluation quota remains charged.")]
                        }
                        : trial).ToArray(),
                    ElapsedComputeTicks = checked(current.ElapsedComputeTicks + current.ReservedComputeTicks),
                    ReservedComputeTicks = 0,
                    Diagnostics = current.Diagnostics.Append(new("search.interrupted-time", "The interrupted batch's reserved wall time remains charged.")).ToArray()
                };
        }
        var previousTicks = current.ElapsedComputeTicks;
        var clock = Stopwatch.StartNew();
        current = current with { State = FlatStartSearchState.Running };
        while (materials.Names.Count > 0 && RemainingEvaluations() > 0
            && RemainingTime() > TimeSpan.Zero && current.Trials.Count < FlatStartSearchPlanning.MaximumTrials
            && !cancellationToken.IsCancellationRequested)
        {
            var isRootBatch = current.NextRootIndex < roots.Count;
            var count = isRootBatch ? Math.Min(Math.Min(specification.Budget.MaximumParallelism, 4), roots.Count - current.NextRootIndex) : 1;
            count = Math.Min(count, FlatStartSearchPlanning.MaximumTrials - current.Trials.Count);
            var work = new List<TrialWork>();
            var remaining = RemainingEvaluations();
            for (var offset = 0; offset < count && remaining > 0; offset++)
            {
                var allocation = Math.Min(remaining, isRootBatch ? quota : options.MaximumEvaluationsPerTrial);
                var index = current.Trials.Count + offset;
                var family = isRootBatch ? roots[current.NextRootIndex + offset] : null;
                var proposal = family is not null ? new TrialWork(family, "flat-root", null, null, null)
                    : Propose(specification, materials.Names, current);
                work.Add(proposal with
                {
                    Trial = new()
                    {
                        Index = index,
                        TrialId = $"trial-{index:D4}",
                        Family = proposal.Family,
                        Operation = proposal.Operation,
                        ParentTrialId = proposal.Parent?.TrialId,
                        State = FamilyTrialState.Reserved,
                        AllocatedEvaluations = allocation,
                        ChargedEvaluations = allocation
                    }
                });
                remaining -= allocation;
            }
            var timeLimit = RemainingTime();
            // A cancel completes this bounded batch. A process loss charges its reserved upper bound.
            timeLimit = TimeSpan.FromTicks(Math.Min(timeLimit.Ticks, Math.Max(TimeSpan.TicksPerSecond,
                (long)(specification.Budget.TimeLimit.Ticks * (2.0 * work.Max(item => item.Trial.AllocatedEvaluations)
                    / specification.Budget.MaximumEvaluations)))));
            current = current with
            {
                Trials = current.Trials.Concat(work.Select(item => item.Trial)).ToArray(),
                NextRootIndex = current.NextRootIndex + (isRootBatch ? work.Count : 0),
                NextRefinementIndex = current.NextRefinementIndex + (isRootBatch ? 0 : work.Count),
                ElapsedComputeTicks = previousTicks + clock.Elapsed.Ticks,
                ReservedComputeTicks = timeLimit.Ticks
            };
            await Save(); // Persist reservations before dispatching any evaluations.
            var completed = await Task.WhenAll(work.Select(item => Task.Run(() => Execute(specification, item, timeLimit))));
            // Task.WhenAll preserves proposal order, independent of worker completion order.
            var updated = current.Trials.ToArray();
            foreach (var trial in completed) updated[trial.Index] = trial;
            current = current with { Trials = updated, ReservedComputeTicks = 0, ElapsedComputeTicks = previousTicks + clock.Elapsed.Ticks };
            await Save();
        }
        current = current with
        {
            ElapsedComputeTicks = previousTicks + clock.Elapsed.Ticks,
            State = materials.Names.Count == 0 ? FlatStartSearchState.NoUsableGlass
                : cancellationToken.IsCancellationRequested ? FlatStartSearchState.Cancelled
                : RemainingTime() <= TimeSpan.Zero ? FlatStartSearchState.TimeLimit
                : RemainingEvaluations() == 0 ? FlatStartSearchState.BudgetExhausted : FlatStartSearchState.Completed
        };
        if (current.Origin is null && (current.NextRootIndex < roots.Count || roots.Select(root => root.ElementCount).Distinct().Count()
            < specification.MaximumElementCount - specification.MinimumElementCount + 1))
            current = current with
            {
                Diagnostics = current.Diagnostics.Append(new("search.incomplete-coverage",
                "The available seed, evaluation or time budget did not cover every requested initial family/element count.")).ToArray()
            };
        if (current.Trials.Count == FlatStartSearchPlanning.MaximumTrials)
            current = current with { Diagnostics = current.Diagnostics.Append(new("search.trial-limit", "The bounded trial archive is full; remaining evaluation budget is unused.")).ToArray() };
        await Save();
        return new(current, SelectCandidates(current));

        int RemainingEvaluations() => specification.Budget.MaximumEvaluations - current.ChargedEvaluations;
        TimeSpan RemainingTime() => specification.Budget.TimeLimit - TimeSpan.FromTicks(previousTicks + clock.Elapsed.Ticks);
        async ValueTask Save()
        {
            if (checkpointSink is not null) await checkpointSink(current, CancellationToken.None);
        }
    }

    private static FamilyTrial Execute(InitialStructureSpecification specification, TrialWork work, TimeSpan timeLimit)
    {
        var trial = work.Trial;
        try
        {
            var request = specification with
            {
                Budget = specification.Budget with
                { MaximumEvaluations = trial.AllocatedEvaluations, MaximumParallelism = 1, TimeLimit = timeLimit }
            };
            var service = new FlatStartDesignService();
            var result = work.Start is null ? service.Solve(request, work.Family.ElementCount, family: work.Family)
                : service.Continue(request, work.Family, work.RootProof!, work.Start, work.Parent!.Candidate!.CandidateId,
                    improveBeyondTargets: work.Operation == "refine-selected");
            var candidate = result.Candidate is { } value ? value with
            {
                CandidateId = (work.Operation == "refine-selected" ? "selected-" + ContentFingerprint.Compute(work.Parent!.Candidate!.CandidateId)[..8] + "-" : "")
                    + trial.TrialId + "-" + value.OpticFingerprint[..16],
                Lineage = value.Lineage with
                {
                    Operation = trial.Operation,
                    Generation = work.Parent is null ? 0 : work.Parent.Candidate!.Lineage.Generation + 1
                }
            } : null;
            return trial with
            {
                State = FamilyTrialState.Completed,
                CompletedEvaluations = result.EvaluationCount,
                ChargedEvaluations = result.EvaluationCount,
                TracedRealRayCount = result.TracedRayCount,
                DesignState = result.State,
                BootstrapProof = work.Parent is null ? result.Bootstrap : null,
                FlatRoot = result.Bootstrap.Steps[0].Optic,
                RefinementStartOptic = work.Start,
                FirstCurvatureUpdate = work.Parent is null ? result.Bootstrap.Steps.FirstOrDefault(step => step.Optic.Surfaces.Any(surface => surface.Radius != 0)) : null,
                FirstRefinementStep = Compact(result.Steps.FirstOrDefault(step => step.Operation == "curvature-thickness-step")),
                Stages = result.Steps.Where(step => step.Operation != "curvature-thickness-step").Select(step => Compact(step)!).ToArray(),
                Candidate = candidate,
                FinalValidation = result.FinalValidation is { } validation ? validation with { Residuals = [] } : null,
                Diagnostics = result.Diagnostics
            };
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or ArithmeticException or KeyNotFoundException or InvalidDataException)
        {
            return trial with
            {
                State = FamilyTrialState.Failed,
                Diagnostics = [new("search.trial-failed", $"{exception.GetType().Name}: {exception.Message} The reserved quota remains charged because completion is unknown.")]
            };
        }
    }

    private static DesignStep? Compact(DesignStep? step) => step is null ? null : step with { Evaluation = step.Evaluation with { Residuals = [] } };

    private static TrialWork Propose(InitialStructureSpecification specification, IReadOnlyList<string> glasses, FlatStartSearchCheckpoint checkpoint)
    {
        var ordinal = checkpoint.NextRefinementIndex;
        if (checkpoint.Origin is { } origin)
        {
            var chosen = checkpoint.Trials.Prepend(origin.AsParent()).Where(trial => trial.Candidate is not null)
                .OrderBy(trial => FlatStartCandidateArchive.Rank(trial.Candidate!.Status))
                .ThenBy(trial => FlatStartCandidateArchive.Score(specification, trial.Candidate!)).First();
            return new(chosen.Family, "refine-selected", chosen, origin.RootProof, chosen.Candidate!.Optic);
        }
        var parents = checkpoint.Trials.Where(trial => trial.Candidate?.Evaluation.EffectiveFocalLengthMillimeters > 0)
            .OrderBy(trial => FlatStartCandidateArchive.Rank(trial.Candidate!.Status))
            .ThenBy(trial => FlatStartCandidateArchive.Score(specification, trial.Candidate!)).ThenBy(trial => trial.Index)
            .DistinctBy(trial => FlatStartCandidateArchive.FamilyKey(trial.Candidate!)).ToArray();
        if (parents.Length == 0)
            return new(FlatStartSearchPlanning.Family(specification, glasses, checkpoint.RootPlan.Count + ordinal), "flat-root-retry", null, null, null);
        var parent = parents[ordinal / 5 % parents.Length];
        var ancestor = parent;
        while (ancestor.ParentTrialId is { } parentId) ancestor = Parent(checkpoint, parentId);
        var optic = Optic.FromSnapshot(parent.Candidate!.Optic);
        var n = parent.Family.ElementCount;
        var selectedGlass = parent.Family.GlassNames.ToArray();
        var random = new DeterministicRandom(unchecked(specification.Budget.RandomSeed + 15485863L * (ordinal + 1)));
        var operation = ordinal % 5;
        if (glasses.Count == 1 && operation is 1 or 2) operation = 4;
        if (operation is 1 or 2)
        {
            var start = random.NextInt32(n);
            for (var offset = 0; offset < (operation == 1 ? 1 : Math.Min(2, n)); offset++)
            {
                var element = (start + offset) % n;
                var surface = optic.SurfaceGroup.Items[2 * element + 1];
                var choices = glasses.Where(name => !name.Equals(selectedGlass[element], StringComparison.OrdinalIgnoreCase)).ToArray();
                selectedGlass[element] = choices[random.NextInt32(choices.Length)];
                surface.MaterialAfter = optic.Materials.Resolve(selectedGlass[element]);
            }
        }
        if (operation == 3)
        {
            var stop = 1 + parent.Family.StopSurfaceIndex % (2 * n);
            for (var index = 1; index <= 2 * n; index++) optic.SurfaceGroup.Items[index].IsStop = index == stop;
        }
        optic.SurfaceGroup.Renumber();
        var family = parent.Family with
        {
            GlassNames = selectedGlass,
            StopSurfaceIndex = optic.SurfaceGroup.Items.ToList().FindIndex(surface => surface.IsStop),
            SeedIndex = checkpoint.RootPlan.Count + ordinal
        };
        var snapshot = optic.ToSnapshot();
        if (operation == 4)
        {
            var problem = new FlatStartDesignProblem(specification, n, snapshot, family);
            var vector = problem.Vector(snapshot);
            for (var index = 0; index < vector.Length; index++) vector[index] += (random.NextUnitDouble() - .5) * (index < 2 * n ? .1 : .04);
            snapshot = problem.CreateOptic(problem.Project(vector), FlatStartDesignProblem.FullStage).ToSnapshot();
        }
        string[] names = ["refine", "glass-swap", "glass-pair-swap", "stop-surface-change", "parameter-perturbation"];
        return new(family, names[operation], parent, ancestor.BootstrapProof!, snapshot);
    }

    private static void ValidateResume(FlatStartSearchCheckpoint checkpoint, InitialStructureSpecification specification,
        FlatStartSearchOptions options, SearchMaterialSet materials, IReadOnlyList<FlatStartFamily> roots, int quota)
    {
        FlatStartCheckpointValidation.Validate(checkpoint);
        if (checkpoint.Origin is { } origin)
        {
            var source = origin.Source.Candidate!;
            var rootHash = ContentFingerprint.Compute(origin.RootProof.Steps[0].Optic);
            if (ContentFingerprint.Compute(specification with { Budget = origin.Specification.Budget }) != ContentFingerprint.Compute(origin.Specification)
                || origin.MaterialFingerprint != materials.Fingerprint || source.OpticFingerprint != ContentFingerprint.Compute(source.Optic)
                || source.Lineage.RootFingerprint != rootHash || ContentFingerprint.Compute(source.FlatRootOptic) != rootHash)
                throw new InvalidDataException("Refinement must retain source targets, catalog materials and original flat-root provenance.");
            FlatStartFamilySupport.Validate(specification, origin.Source.Family);
        }
        if (checkpoint.SpecificationFingerprint != ContentFingerprint.Compute(specification)
            || checkpoint.SpecificationFingerprint != ContentFingerprint.Compute(checkpoint.Specification)
            || checkpoint.OptionsFingerprint != ContentFingerprint.Compute(options)
            || checkpoint.OptionsFingerprint != ContentFingerprint.Compute(checkpoint.Options)
            || checkpoint.MaterialFingerprint != materials.Fingerprint
            || ContentFingerprint.Compute(checkpoint.UsableGlassNames) != ContentFingerprint.Compute(materials.Names)
            || ContentFingerprint.Compute(checkpoint.RootPlan) != ContentFingerprint.Compute(roots)
            || checkpoint.RootEvaluationQuota != quota)
            throw new InvalidDataException("The saved specification, search options, root plan or catalog materials differ from this run.");
        for (var index = 0; index < checkpoint.Trials.Count; index++)
        {
            var trial = checkpoint.Trials[index];
            FlatStartFamilySupport.Validate(specification, trial.Family);
            if (index < checkpoint.NextRootIndex && (trial.ParentTrialId is not null || trial.Operation != "flat-root"
                || ContentFingerprint.Compute(trial.Family) != ContentFingerprint.Compute(roots[index])))
                throw new InvalidDataException("An initial trial differs from the saved flat-root plan.");
            if (trial.Candidate is { } candidate)
            {
                var rootHash = ContentFingerprint.Compute(trial.FlatRoot!);
                if (candidate.OpticFingerprint != ContentFingerprint.Compute(candidate.Optic)
                    || candidate.Lineage.RootFingerprint != rootHash || ContentFingerprint.Compute(candidate.FlatRootOptic) != rootHash
                    || candidate.Lineage.ElementCount != trial.Family.ElementCount || candidate.Lineage.StopVariant != trial.Family.StopSurfaceIndex)
                    throw new InvalidDataException("Candidate optical content or flat-root provenance was changed.");
                if (trial.ParentTrialId is { } parentId && Parent(checkpoint, parentId).Candidate!.Lineage.RootFingerprint != rootHash)
                    throw new InvalidDataException("A refinement lost its original flat-root provenance.");
            }
            if (trial.BootstrapProof is { } proof && ContentFingerprint.Compute(proof.Steps[0].Optic) != ContentFingerprint.Compute(trial.FlatRoot!))
                throw new InvalidDataException("The bootstrap and recorded root disagree.");
        }
    }

    private static FamilyTrial Parent(FlatStartSearchCheckpoint checkpoint, string id) =>
        id == FlatStartRefinementOrigin.ParentReference && checkpoint.Origin is { } origin ? origin.AsParent()
            : checkpoint.Trials.First(trial => trial.TrialId == id);

    private sealed record TrialWork(FlatStartFamily Family, string Operation, FamilyTrial? Parent,
        FlatStartBootstrapResult? RootProof, OpticSnapshot? Start)
    {
        public FamilyTrial Trial { get; init; } = new();
    }
}

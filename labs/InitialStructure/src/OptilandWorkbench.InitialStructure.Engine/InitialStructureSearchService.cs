using System.Collections.Concurrent;
using OptilandWorkbench.InitialStructure.Contracts;

namespace OptilandWorkbench.InitialStructure.Engine;

public sealed record SearchProgress(int Completed, int Total, string Stage, string? CandidateId = null);

public sealed class InitialStructureSearchService
{
    private static readonly AlgorithmIdentity Algorithm = new(
        "flat-to-usable-hybrid",
        "2",
        "Managed CPU",
        true);

    private readonly FirstOrderSeedGenerator _seedGenerator;
    private readonly HybridCandidateRefiner _refiner;
    private readonly TimeProvider _timeProvider;

    public InitialStructureSearchService(
        FirstOrderSeedGenerator? seedGenerator = null,
        TimeProvider? timeProvider = null)
    {
        _seedGenerator = seedGenerator ?? new FirstOrderSeedGenerator();
        _refiner = new HybridCandidateRefiner();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<SearchRunManifest> RunAsync(
        InitialStructureSpecification specification,
        IProgress<SearchProgress>? progress = null,
        CancellationToken cancellationToken = default,
        SearchCheckpoint? checkpoint = null,
        Func<SearchCheckpoint, CancellationToken, ValueTask>? checkpointSink = null)
    {
        SpecificationValidator.Validate(specification);
        var specificationFingerprint = ContentFingerprint.Compute(specification);
        ValidateCheckpoint(checkpoint, specificationFingerprint, specification);
        var createdUtc = checkpoint?.CreatedUtc ?? _timeProvider.GetUtcNow();
        var runId = checkpoint?.RunId
            ?? $"run-{createdUtc:yyyyMMdd-HHmmssfff}-{specificationFingerprint[..10]}-{Guid.NewGuid():N}";
        var candidates = new ConcurrentBag<CandidateSnapshot>(checkpoint?.SeedCandidates ?? []);
        var diagnostics = new ConcurrentBag<SearchDiagnostic>(checkpoint?.Diagnostics ?? []);
        var allWorkItems = CreateWorkItems(specification).ToArray();
        var completedSeedIndices = new ConcurrentDictionary<int, byte>(
            (checkpoint?.CompletedInitialSeedIndices ?? [])
                .Select(index => new KeyValuePair<int, byte>(index, 0)));
        var workItems = allWorkItems
            .Where(item => !completedSeedIndices.ContainsKey(item.SeedIndex))
            .ToArray();
        if (allWorkItems.Length < specification.Budget.InitialSeedCount
            && !diagnostics.Any(item => item.Code == "run.evaluation-limit"))
        {
            diagnostics.Add(new SearchDiagnostic(
                "run.evaluation-limit",
                $"MaximumEvaluations limited the initial seed run to {allWorkItems.Length} candidates."));
        }
        var completed = completedSeedIndices.Count;
        using var checkpointGate = new SemaphoreSlim(1, 1);
        using var timeLimit = new CancellationTokenSource(specification.Budget.TimeLimit);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeLimit.Token);
        var state = SearchRunState.Completed;

        try
        {
            await Parallel.ForEachAsync(
                workItems,
                new ParallelOptions
                {
                    CancellationToken = linked.Token,
                    MaxDegreeOfParallelism = specification.Budget.MaximumParallelism
                },
                async (workItem, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        var candidate = _seedGenerator.Create(
                            specification,
                            workItem.ElementCount,
                            workItem.SeedIndex);
                        candidates.Add(candidate);
                        if (candidate.Status == CandidateStatus.Rejected)
                        {
                            diagnostics.Add(new SearchDiagnostic(
                                "candidate.rejected",
                                string.Join(" ", candidate.Violations.Select(item => item.Message)),
                                candidate.CandidateId));
                        }

                        var current = Interlocked.Increment(ref completed);
                        progress?.Report(new SearchProgress(
                            current,
                            allWorkItems.Length,
                            "First-order expansion",
                            candidate.CandidateId));
                    }
                    catch (Exception exception) when (
                        exception is InvalidOperationException
                        or ArgumentException
                        or ArithmeticException
                        or KeyNotFoundException)
                    {
                        diagnostics.Add(new SearchDiagnostic(
                            "candidate.exception",
                            $"Seed {workItem.SeedIndex}: {exception.Message}"));
                        var current = Interlocked.Increment(ref completed);
                        progress?.Report(new SearchProgress(
                            current,
                            allWorkItems.Length,
                            "Candidate failed"));
                    }

                    completedSeedIndices.TryAdd(workItem.SeedIndex, 0);
                    await PersistCheckpointAsync("initial-seeds", token);
                });

            await PersistCheckpointAsync("refinement-ready", linked.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            state = SearchRunState.Cancelled;
            diagnostics.Add(new SearchDiagnostic(
                "run.time-limit",
                $"The run reached its {specification.Budget.TimeLimit} time limit."));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var orderedSeeds = candidates
            .OrderBy(candidate => candidate.Lineage.ElementCount)
            .ThenBy(candidate => candidate.Lineage.StopVariant)
            .ThenBy(candidate => candidate.Lineage.SeedIndex)
            .ToArray();
        var refinementEvaluations = 0;
        if (state == SearchRunState.Completed)
        {
            try
            {
                var remainingEvaluations = Math.Max(
                    0,
                    specification.Budget.MaximumEvaluations - allWorkItems.Length);
                var refinement = _refiner.Refine(
                    specification,
                    orderedSeeds,
                    remainingEvaluations,
                    progress,
                    linked.Token);
                refinementEvaluations = refinement.EvaluationCount;
                foreach (var candidate in refinement.Candidates)
                {
                    candidates.Add(candidate);
                }
                foreach (var diagnostic in refinement.Diagnostics)
                {
                    diagnostics.Add(diagnostic);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                state = SearchRunState.Cancelled;
                diagnostics.Add(new SearchDiagnostic(
                    "run.time-limit",
                    $"The run reached its {specification.Budget.TimeLimit} time limit during refinement."));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        diagnostics.Add(new SearchDiagnostic(
            "run.evaluation-count",
            $"Consumed {allWorkItems.Length + refinementEvaluations} of {specification.Budget.MaximumEvaluations} evaluations."));
        var uniqueCandidates = candidates
            .GroupBy(candidate => candidate.OpticFingerprint, StringComparer.Ordinal)
            .Select(group => group
                .OrderByDescending(candidate => candidate.Lineage.Generation)
                .ThenBy(candidate => candidate.CandidateId, StringComparer.Ordinal)
                .First())
            .ToArray();
        var orderedCandidates = CandidateDiversityOrdering.Order(specification, uniqueCandidates);
        return new SearchRunManifest
        {
            RunId = runId,
            CreatedUtc = createdUtc,
            CompletedUtc = _timeProvider.GetUtcNow(),
            State = state,
            Specification = specification,
            SpecificationFingerprint = specificationFingerprint,
            Algorithm = Algorithm,
            Candidates = orderedCandidates,
            Diagnostics = diagnostics
                .OrderBy(diagnostic => diagnostic.CandidateId, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
                .ToArray()
        };

        async ValueTask PersistCheckpointAsync(string stage, CancellationToken token)
        {
            if (checkpointSink is null)
            {
                return;
            }

            await checkpointGate.WaitAsync(token);
            try
            {
                var snapshot = new SearchCheckpoint
                {
                    RunId = runId,
                    CreatedUtc = createdUtc,
                    UpdatedUtc = _timeProvider.GetUtcNow(),
                    Stage = stage,
                    Specification = specification,
                    SpecificationFingerprint = specificationFingerprint,
                    Algorithm = Algorithm,
                    CompletedInitialSeedIndices = completedSeedIndices.Keys
                        .Order()
                        .ToArray(),
                    SeedCandidates = candidates
                        .Where(candidate =>
                            candidate.Lineage.Generation == 1
                            && completedSeedIndices.ContainsKey(candidate.Lineage.SeedIndex))
                        .OrderBy(candidate => candidate.Lineage.ElementCount)
                        .ThenBy(candidate => candidate.Lineage.StopVariant)
                        .ThenBy(candidate => candidate.Lineage.SeedIndex)
                        .ToArray(),
                    Diagnostics = diagnostics
                        .OrderBy(diagnostic => diagnostic.CandidateId, StringComparer.Ordinal)
                        .ThenBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
                        .ToArray()
                };
                await checkpointSink(snapshot, token);
            }
            finally
            {
                checkpointGate.Release();
            }
        }
    }

    private static void ValidateCheckpoint(
        SearchCheckpoint? checkpoint,
        string specificationFingerprint,
        InitialStructureSpecification specification)
    {
        if (checkpoint is null)
        {
            return;
        }

        if (checkpoint.SchemaVersion != SearchCheckpoint.CurrentSchemaVersion
            || checkpoint.Specification is null
            || checkpoint.Algorithm is null
            || checkpoint.CompletedInitialSeedIndices is null
            || checkpoint.SeedCandidates is null
            || checkpoint.Diagnostics is null
            || !StringComparer.Ordinal.Equals(
                checkpoint.SpecificationFingerprint,
                specificationFingerprint)
            || !StringComparer.Ordinal.Equals(
                ContentFingerprint.Compute(checkpoint.Specification),
                specificationFingerprint))
        {
            throw new InvalidDataException(
                "The checkpoint schema or specification fingerprint does not match the requested search.");
        }

        if (!StringComparer.Ordinal.Equals(checkpoint.Algorithm.Name, Algorithm.Name)
            || !StringComparer.Ordinal.Equals(checkpoint.Algorithm.Version, Algorithm.Version))
        {
            throw new InvalidDataException(
                "The checkpoint was created by an incompatible search algorithm version.");
        }

        var maximumInitialSeedIndex = Math.Min(
            specification.Budget.InitialSeedCount,
            specification.Budget.MaximumEvaluations);
        var completed = checkpoint.CompletedInitialSeedIndices.ToHashSet();
        if (completed.Count != checkpoint.CompletedInitialSeedIndices.Count
            || completed.Any(index => index < 0 || index >= maximumInitialSeedIndex)
            || checkpoint.SeedCandidates.Any(candidate =>
                candidate.Lineage.Generation != 1
                || !completed.Contains(candidate.Lineage.SeedIndex)))
        {
            throw new InvalidDataException("The checkpoint contains invalid or duplicate seed progress.");
        }
    }

    private static IEnumerable<SearchWorkItem> CreateWorkItems(
        InitialStructureSpecification specification)
    {
        var elementRange = specification.MaximumElementCount
            - specification.MinimumElementCount
            + 1;
        var evaluationLimit = Math.Min(
            specification.Budget.InitialSeedCount,
            specification.Budget.MaximumEvaluations);
        for (var seedIndex = 0; seedIndex < evaluationLimit; seedIndex++)
        {
            yield return new SearchWorkItem(
                specification.MinimumElementCount + (seedIndex % elementRange),
                seedIndex);
        }
    }

    private sealed record SearchWorkItem(int ElementCount, int SeedIndex);
}

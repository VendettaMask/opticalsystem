using System.Collections.Concurrent;
using OptilandWorkbench.InitialStructure.Contracts;

namespace OptilandWorkbench.InitialStructure.Engine;

public sealed record SearchProgress(int Completed, int Total, string Stage, string? CandidateId = null);

public sealed class InitialStructureSearchService
{
    private static readonly AlgorithmIdentity Algorithm = new(
        "paraxial-expansion",
        "1",
        "Managed CPU",
        true);

    private readonly FirstOrderSeedGenerator _seedGenerator;
    private readonly TimeProvider _timeProvider;

    public InitialStructureSearchService(
        FirstOrderSeedGenerator? seedGenerator = null,
        TimeProvider? timeProvider = null)
    {
        _seedGenerator = seedGenerator ?? new FirstOrderSeedGenerator();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<SearchRunManifest> RunAsync(
        InitialStructureSpecification specification,
        IProgress<SearchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        SpecificationValidator.Validate(specification);
        var specificationFingerprint = ContentFingerprint.Compute(specification);
        var createdUtc = _timeProvider.GetUtcNow();
        var runId = $"run-{createdUtc:yyyyMMdd-HHmmssfff}-{specificationFingerprint[..10]}";
        var candidates = new ConcurrentBag<CandidateSnapshot>();
        var diagnostics = new ConcurrentBag<SearchDiagnostic>();
        var workItems = CreateWorkItems(specification).ToArray();
        var completed = 0;
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
                (workItem, token) =>
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
                            workItems.Length,
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
                            workItems.Length,
                            "Candidate failed"));
                    }

                    return ValueTask.CompletedTask;
                });
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            state = SearchRunState.Cancelled;
            diagnostics.Add(new SearchDiagnostic(
                "run.time-limit",
                $"The run reached its {specification.Budget.TimeLimit} time limit."));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var orderedCandidates = candidates
            .OrderBy(candidate => candidate.Lineage.ElementCount)
            .ThenBy(candidate => candidate.Lineage.SeedIndex)
            .ToArray();
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
    }

    private static IEnumerable<SearchWorkItem> CreateWorkItems(
        InitialStructureSpecification specification)
    {
        var elementRange = specification.MaximumElementCount
            - specification.MinimumElementCount
            + 1;
        for (var seedIndex = 0; seedIndex < specification.Budget.InitialSeedCount; seedIndex++)
        {
            yield return new SearchWorkItem(
                specification.MinimumElementCount + (seedIndex % elementRange),
                seedIndex);
        }
    }

    private sealed record SearchWorkItem(int ElementCount, int SeedIndex);
}

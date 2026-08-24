using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.InitialStructure.Contracts;
using OptilandWorkbench.InitialStructure.Engine;
using OptilandWorkbench.InitialStructure.Persistence;

namespace OptilandWorkbench.InitialStructure.Tests;

public sealed class InitialStructureLabTests
{
    [Fact]
    public void FlatRootIsAnExactIndependentPlaneParallelSystem()
    {
        var specification = Specification(seedCount: 3);
        var factory = new FlatRootFactory();

        var first = factory.Create(specification, elementCount: 3, stopVariant: 1);
        var second = factory.Create(specification, elementCount: 3, stopVariant: 1);

        Assert.Equal(8, first.Surfaces.Count);
        Assert.All(first.Surfaces, surface => Assert.Equal(0, surface.Radius));
        Assert.True(double.IsPositiveInfinity(first.Surfaces[0].Thickness));
        Assert.Equal(3, first.Surfaces.Count(surface => surface.Material == specification.InitialGlass));
        Assert.Single(first.Surfaces, surface => surface.IsStop);
        Assert.Equal(ContentFingerprint.Compute(first), ContentFingerprint.Compute(second));

        first.Surfaces[1] = first.Surfaces[1] with { Radius = 42 };
        Assert.Equal(0, second.Surfaces[1].Radius);
        OpticSnapshotValidator.Validate(second);
        Assert.Equal(
            ContentFingerprint.Compute(second),
            ContentFingerprint.Compute(Optic.FromSnapshot(second).ToSnapshot()));
    }

    [Fact]
    public void FirstOrderSeedPreservesFlatRootLineageAndBendsSurfaces()
    {
        var specification = Specification(seedCount: 3);
        var generator = new FirstOrderSeedGenerator();

        var candidate = generator.Create(specification, elementCount: 3, seedIndex: 0);

        Assert.Equal(CandidateStatus.Refinable, candidate.Status);
        Assert.Equal("paraxial-power-expansion", candidate.Lineage.Operation);
        Assert.Equal(
            ContentFingerprint.Compute(new FlatRootFactory().Create(specification, 3, 0)),
            candidate.Lineage.RootFingerprint);
        Assert.All(candidate.FlatRootOptic.Surfaces, surface => Assert.Equal(0, surface.Radius));
        Assert.Equal(
            candidate.Lineage.RootFingerprint,
            ContentFingerprint.Compute(candidate.FlatRootOptic));
        Assert.Contains(candidate.Optic.Surfaces, surface => Math.Abs(surface.Radius) > 1e-9);
        Assert.NotNull(candidate.Evaluation.EffectiveFocalLengthMillimeters);
        Assert.Equal(15, candidate.Evaluation.EvaluatedRayCount);
        Assert.True(candidate.Evaluation.ValidRayCount > 0);
        OpticSnapshotValidator.Validate(candidate.Optic);
    }

    [Fact]
    public async Task SearchIsDeterministicForTheSameSpecificationAndSeed()
    {
        var specification = Specification(seedCount: 6) with
        {
            MinimumElementCount = 3,
            MaximumElementCount = 4
        };
        var service = new InitialStructureSearchService();

        var first = await service.RunAsync(specification);
        var second = await service.RunAsync(specification);

        Assert.Equal(SearchRunState.Completed, first.State);
        Assert.Equal(6, first.Candidates.Count);
        Assert.Equal(
            first.Candidates.Select(candidate => candidate.CandidateId),
            second.Candidates.Select(candidate => candidate.CandidateId));
        Assert.Equal(
            first.Candidates.Select(candidate => candidate.OpticFingerprint),
            second.Candidates.Select(candidate => candidate.OpticFingerprint));
        Assert.All(first.Candidates, candidate => Assert.NotEqual(CandidateStatus.LabAccepted, candidate.Status));
    }

    [Fact]
    public async Task RunDirectoryStoreRoundTripsInfinityAndCandidateLineage()
    {
        var manifest = await new InitialStructureSearchService().RunAsync(Specification(seedCount: 3));
        var root = Path.Combine(Path.GetTempPath(), "initial-structure-lab-tests", Guid.NewGuid().ToString("N"));

        try
        {
            var store = new RunDirectoryStore();
            var path = await store.SaveAsync(manifest, root);
            var restored = await store.LoadAsync(path);

            Assert.Equal(manifest.RunId, restored.RunId);
            Assert.Equal(manifest.SpecificationFingerprint, restored.SpecificationFingerprint);
            Assert.Equal(
                manifest.Candidates.Select(candidate => candidate.Lineage.RootFingerprint),
                restored.Candidates.Select(candidate => candidate.Lineage.RootFingerprint));
            Assert.All(
                restored.Candidates,
                candidate => Assert.True(double.IsPositiveInfinity(candidate.Optic.Surfaces[0].Thickness)));
            Assert.True(File.Exists(Path.Combine(
                Path.GetDirectoryName(path)!,
                "candidates",
                manifest.Candidates[0].CandidateId + ".json")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SearchHonorsCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new InitialStructureSearchService().RunAsync(
                Specification(seedCount: 6),
                cancellationToken: cancellation.Token));
    }

    [Fact]
    public void SpecificationRejectsAnImpossibleStructuralTrack()
    {
        var specification = Specification(seedCount: 3) with
        {
            MaximumElementCount = 8,
            MaximumTrackLengthMillimeters = 10
        };

        var exception = Assert.Throws<InitialStructureSpecificationException>(() =>
            SpecificationValidator.Validate(specification));

        Assert.Contains(
            exception.Errors,
            error => error.Contains("minimum structural track", StringComparison.Ordinal));
    }

    [Fact]
    public void FormalProductProjectsDoNotReferenceTheLab()
    {
        var repositoryRoot = FindRepositoryRoot();
        var formalSolution = File.ReadAllText(Path.Combine(repositoryRoot, "OptilandWorkbench.slnx"));
        Assert.DoesNotContain("InitialStructure", formalSolution, StringComparison.Ordinal);

        var formalProjects = new[]
        {
            "src/OptilandWorkbench.Core/OptilandWorkbench.Core.csproj",
            "src/OptilandWorkbench.Application/OptilandWorkbench.Application.csproj",
            "src/OptilandWorkbench.App/OptilandWorkbench.App.csproj"
        };
        foreach (var project in formalProjects)
        {
            var contents = File.ReadAllText(Path.Combine(repositoryRoot, project));
            Assert.DoesNotContain("InitialStructure", contents, StringComparison.Ordinal);
        }
    }

    private static InitialStructureSpecification Specification(int seedCount)
    {
        return new InitialStructureSpecification
        {
            Name = "Test flat-to-start",
            Budget = new SearchBudget
            {
                InitialSeedCount = seedCount,
                MaximumEvaluations = seedCount * 20,
                MaximumParallelism = 2,
                RandomSeed = 20260824,
                TimeLimit = TimeSpan.FromMinutes(1)
            }
        };
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}

using System.Text.Json;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.InitialStructure.Contracts;
using OptilandWorkbench.InitialStructure.Engine;
using OptilandWorkbench.InitialStructure.Persistence;

namespace OptilandWorkbench.InitialStructure.Tests;

public sealed class InitialStructureLabTests
{
    [Fact]
    public void FrozenBenchmarkSpecificationsAreValidAndUnique()
    {
        var specifications = LoadFrozenBenchmarkSpecifications();
        Assert.True(specifications.Count >= 10);

        var fingerprints = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (_, specification) in specifications)
        {
            SpecificationValidator.Validate(specification);
            Assert.True(fingerprints.Add(ContentFingerprint.Compute(specification)));
        }
    }

    [Fact]
    public async Task FrozenBenchmarkSetMeetsTheL3AcceptedFamilyGate()
    {
        var minimumFamilies = LoadFrozenBenchmarkMinimums();
        var results = new List<string>();
        var passingSpecifications = 0;
        foreach (var (name, specification) in LoadFrozenBenchmarkSpecifications())
        {
            var manifest = await new InitialStructureSearchService().RunAsync(specification);
            var acceptedFamilies = manifest.Candidates
                .Where(candidate => candidate.Status == CandidateStatus.LabAccepted)
                .Select(candidate => (
                    candidate.Lineage.ElementCount,
                    candidate.Lineage.StopVariant))
                .Distinct()
                .Count();
            Assert.True(
                minimumFamilies.TryGetValue(name, out var minimum)
                && acceptedFamilies >= minimum,
                $"{name} produced {acceptedFamilies} accepted families; frozen minimum is {minimum}.");
            if (acceptedFamilies >= 3)
            {
                passingSpecifications++;
            }

            results.Add($"{name}: {acceptedFamilies} accepted families");
        }

        var required = (int)Math.Ceiling(results.Count * 0.8);
        Console.WriteLine(string.Join(Environment.NewLine, results));
        Assert.True(
            passingSpecifications >= required,
            $"L3 requires {required}/{results.Count} passing specifications; "
            + $"observed {passingSpecifications}. {string.Join("; ", results)}");
    }

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
        Assert.Equal(45, candidate.Evaluation.EvaluatedRayCount);
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
        Assert.True(first.Candidates.Count >= 6);
        Assert.Equal(
            first.Candidates.Select(candidate => candidate.CandidateId),
            second.Candidates.Select(candidate => candidate.CandidateId));
        Assert.Equal(
            first.Candidates.Select(candidate => candidate.OpticFingerprint),
            second.Candidates.Select(candidate => candidate.OpticFingerprint));
        Assert.NotEqual(first.RunId, second.RunId);
        Assert.Contains(first.Candidates, candidate => candidate.Lineage.Generation == 2);
        Assert.Contains(first.Candidates, candidate =>
            candidate.Lineage.Operation.Contains("dense-validation", StringComparison.Ordinal));
        Assert.Contains(first.Diagnostics, diagnostic =>
            diagnostic.Code == "run.evaluation-count"
            && diagnostic.Message.Contains(
                $"of {specification.Budget.MaximumEvaluations}",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task HybridSearchCanPublishOnlyDenselyValidatedLabAcceptedCandidates()
    {
        var baseSpecification = Specification(seedCount: 3);
        var specification = baseSpecification with
        {
            MaximumRmsSpotRadiusMillimeters = 10,
            MaximumSpotRadiusMillimeters = 20,
            Budget = baseSpecification.Budget with { MaximumEvaluations = 180 }
        };

        var manifest = await new InitialStructureSearchService().RunAsync(specification);

        var accepted = Assert.Single(
            manifest.Candidates.Where(candidate => candidate.Status == CandidateStatus.LabAccepted),
            candidate => candidate.Lineage.StopVariant == 0);
        Assert.Equal(2, accepted.Lineage.Generation);
        Assert.NotNull(accepted.Lineage.ParentCandidateId);
        Assert.Contains("dense-validation", accepted.Lineage.Operation, StringComparison.Ordinal);
        Assert.DoesNotContain(
            manifest.Candidates.Where(candidate => candidate.Lineage.Generation == 1),
            candidate => candidate.Status == CandidateStatus.LabAccepted);
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
    public async Task RunDirectoryStorePublishesImmutableCompleteRuns()
    {
        var manifest = await new InitialStructureSearchService().RunAsync(Specification(seedCount: 3));
        var root = Path.Combine(Path.GetTempPath(), "initial-structure-lab-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new RunDirectoryStore();
            var path = await store.SaveAsync(manifest, root);

            await Assert.ThrowsAsync<IOException>(() => store.SaveAsync(manifest, root));

            var restored = await store.LoadAsync(path);
            Assert.Equal(manifest.RunId, restored.RunId);
            Assert.DoesNotContain(
                Directory.GetFileSystemEntries(root),
                entry => Path.GetFileName(entry).StartsWith(".", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunDirectoryStoreRejectsTamperedOrCaseCollidingCandidateFiles()
    {
        var manifest = await new InitialStructureSearchService().RunAsync(Specification(seedCount: 3));
        var root = Path.Combine(Path.GetTempPath(), "initial-structure-lab-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new RunDirectoryStore();
            var path = await store.SaveAsync(manifest, root);
            var candidatePath = Path.Combine(
                Path.GetDirectoryName(path)!,
                "candidates",
                manifest.Candidates[0].CandidateId + ".json");
            await File.WriteAllTextAsync(candidatePath, "{}");
            await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync(path));

            var first = manifest.Candidates[0] with { CandidateId = "Candidate-A" };
            var second = manifest.Candidates[1] with { CandidateId = "candidate-a" };
            var collision = manifest with
            {
                RunId = manifest.RunId + "-collision",
                Candidates = new[] { first, second }
            };
            await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(collision, root));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CanceledRunDirectorySaveLeavesNoPartialRun()
    {
        var manifest = await new InitialStructureSearchService().RunAsync(Specification(seedCount: 3));
        var root = Path.Combine(Path.GetTempPath(), "initial-structure-lab-tests", Guid.NewGuid().ToString("N"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                new RunDirectoryStore().SaveAsync(manifest, root, cancellation.Token));

            Assert.True(Directory.Exists(root));
            Assert.Empty(Directory.GetFileSystemEntries(root));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
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
    public async Task SeedCheckpointResumeMatchesAnUninterruptedDeterministicRun()
    {
        var baseSpecification = Specification(seedCount: 6);
        var specification = baseSpecification with
        {
            Budget = baseSpecification.Budget with { MaximumParallelism = 1 }
        };
        using var cancellation = new CancellationTokenSource();
        SearchCheckpoint? captured = null;
        var interruptedService = new InitialStructureSearchService();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            interruptedService.RunAsync(
                specification,
                cancellationToken: cancellation.Token,
                checkpointSink: (checkpoint, _) =>
                {
                    captured = checkpoint;
                    if (checkpoint.CompletedInitialSeedIndices.Count >= 2)
                    {
                        cancellation.Cancel();
                    }

                    return ValueTask.CompletedTask;
                }));

        Assert.NotNull(captured);
        Assert.Equal(2, captured.CompletedInitialSeedIndices.Count);
        var resumed = await new InitialStructureSearchService().RunAsync(
            specification,
            checkpoint: captured);
        var uninterrupted = await new InitialStructureSearchService().RunAsync(specification);

        Assert.Equal(captured.RunId, resumed.RunId);
        Assert.Equal(
            uninterrupted.Candidates.Select(candidate => candidate.CandidateId),
            resumed.Candidates.Select(candidate => candidate.CandidateId));
        Assert.Equal(
            uninterrupted.Candidates.Select(candidate => candidate.OpticFingerprint),
            resumed.Candidates.Select(candidate => candidate.OpticFingerprint));
    }

    [Fact]
    public async Task CheckpointStoreRoundTripsAndRejectsCandidateProgressMismatch()
    {
        SearchCheckpoint? captured = null;
        var specification = Specification(seedCount: 3) with
        {
            Budget = Specification(seedCount: 3).Budget with { MaximumParallelism = 1 }
        };
        await new InitialStructureSearchService().RunAsync(
            specification,
            checkpointSink: (checkpoint, _) =>
            {
                captured = checkpoint;
                return ValueTask.CompletedTask;
            });
        Assert.NotNull(captured);
        var root = Path.Combine(
            Path.GetTempPath(),
            "initial-structure-checkpoint-tests",
            Guid.NewGuid().ToString("N"));

        try
        {
            var store = new SearchCheckpointStore();
            await store.SaveAsync(captured, root);
            var restored = await store.LoadLatestAsync(root);
            Assert.NotNull(restored);
            Assert.Equal(captured.RunId, restored.RunId);
            Assert.Equal(captured.CompletedInitialSeedIndices, restored.CompletedInitialSeedIndices);

            var invalid = captured with
            {
                CompletedInitialSeedIndices = captured.CompletedInitialSeedIndices
                    .Where(index => index != captured.SeedCandidates[0].Lineage.SeedIndex)
                    .ToArray()
            };
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                store.SaveAsync(invalid, root).AsTask());

            store.Delete(root, captured.RunId);
            Assert.Null(await store.LoadLatestAsync(root));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CandidateExportProducesAReopenableIdenticalStarOptProject()
    {
        var manifest = await new InitialStructureSearchService().RunAsync(Specification(seedCount: 3));
        var candidate = manifest.Candidates.First(item => item.Status == CandidateStatus.LabAccepted);
        var root = Path.Combine(
            Path.GetTempPath(),
            "initial-structure-export-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var path = await new CandidateExportService().ExportStarOptAsync(
                candidate,
                Path.Combine(root, "candidate"));
            var restored = await StarOptProjectStore.LoadAsync(path);

            Assert.EndsWith(StarOptProjectStore.Extension, path, StringComparison.OrdinalIgnoreCase);
            Assert.Single(restored.Configurations);
            Assert.Equal(
                candidate.OpticFingerprint,
                ContentFingerprint.Compute(restored.Configurations[0].ToSnapshot()));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CandidateExportCancellationPreservesExistingTarget()
    {
        var manifest = await new InitialStructureSearchService().RunAsync(Specification(seedCount: 3));
        var candidate = manifest.Candidates.First(item => item.Status == CandidateStatus.LabAccepted);
        var root = Path.Combine(
            Path.GetTempPath(),
            "initial-structure-export-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var target = Path.Combine(root, "candidate.staropt");
        await File.WriteAllTextAsync(target, "existing project content");

        try
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                new CandidateExportService().ExportStarOptAsync(candidate, target, cancellation.Token));

            Assert.Equal("existing project content", await File.ReadAllTextAsync(target));
            Assert.Empty(Directory.EnumerateFiles(root, ".*.tmp"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void StrictImageQualityLimitsPreventRefinableClassification()
    {
        var specification = Specification(seedCount: 3) with
        {
            MaximumRmsSpotRadiusMillimeters = 1e-9,
            MaximumSpotRadiusMillimeters = 1e-9
        };

        var candidate = new FirstOrderSeedGenerator().Create(specification, 3, 0);

        Assert.NotEqual(CandidateStatus.Refinable, candidate.Status);
        Assert.Contains(candidate.Violations, violation =>
            violation.Code is "image.rms-spot-radius" or "image.maximum-spot-radius");
    }

    [Fact]
    public async Task MaximumEvaluationsCapsRequestedInitialSeeds()
    {
        var specification = Specification(seedCount: 6) with
        {
            Budget = Specification(seedCount: 6).Budget with { MaximumEvaluations = 2 }
        };

        var manifest = await new InitialStructureSearchService().RunAsync(specification);

        Assert.Equal(2, manifest.Candidates.Count);
        Assert.Contains(manifest.Diagnostics, diagnostic => diagnostic.Code == "run.evaluation-limit");
    }

    [Fact]
    public async Task RefinementCanUseAnExactRemainingBudgetWithoutSkippingValidation()
    {
        var baseSpecification = Specification(seedCount: 3);
        var specification = baseSpecification with
        {
            Budget = baseSpecification.Budget with { MaximumEvaluations = 7 }
        };

        var manifest = await new InitialStructureSearchService().RunAsync(specification);

        Assert.Contains(manifest.Candidates, candidate => candidate.Lineage.Generation == 2);
        Assert.Contains(manifest.Diagnostics, diagnostic =>
            diagnostic.Code == "run.evaluation-count"
            && diagnostic.Message.Contains("Consumed 7 of 7", StringComparison.Ordinal));
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
    public void SpecificationRejectsUnboundedSearchResources()
    {
        var specification = Specification(seedCount: 3) with
        {
            EffectiveFocalLengthMillimeters = double.MaxValue,
            FNumber = double.Epsilon,
            MaximumFieldAngleDegrees = InitialStructureLimits.MaximumFieldAngleDegrees + 1,
            Budget = new SearchBudget
            {
                InitialSeedCount = InitialStructureLimits.MaximumInitialSeedCount + 1,
                MaximumEvaluations = InitialStructureLimits.MaximumEvaluations + 1,
                MaximumParallelism = InitialStructureLimits.MaximumParallelism + 1,
                TimeLimit = InitialStructureLimits.MaximumTimeLimit + TimeSpan.FromSeconds(1)
            }
        };

        var exception = Assert.Throws<InitialStructureSpecificationException>(() =>
            SpecificationValidator.Validate(specification));

        Assert.Contains(exception.Errors, error => error.Contains("initial seed count", StringComparison.Ordinal));
        Assert.Contains(exception.Errors, error => error.Contains("maximum evaluations", StringComparison.Ordinal));
        Assert.Contains(exception.Errors, error => error.Contains("maximum parallelism", StringComparison.Ordinal));
        Assert.Contains(exception.Errors, error => error.Contains("time limit", StringComparison.Ordinal));
        Assert.Contains(exception.Errors, error => error.Contains("maximum field angle", StringComparison.Ordinal));
        Assert.Contains(exception.Errors, error => error.Contains("unsupported aperture size", StringComparison.Ordinal));
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

    [Fact]
    public void LabAppSourceKeepsTheL4ResponsiveAccessibleWorkflow()
    {
        var repositoryRoot = FindRepositoryRoot();
        var mainWindow = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "labs",
            "InitialStructure",
            "src",
            "OptilandWorkbench.InitialStructure.App",
            "MainWindow.cs"));
        var preview = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "labs",
            "InitialStructure",
            "src",
            "OptilandWorkbench.InitialStructure.App",
            "CandidatePreviewControl.cs"));

        Assert.Contains("width < 820", mainWindow, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.NameProperty", mainWindow, StringComparison.Ordinal);
        Assert.Contains("SpecificationValidator.Validate", mainWindow, StringComparison.Ordinal);
        Assert.Contains("SearchCheckpointStore", mainWindow, StringComparison.Ordinal);
        Assert.Contains("SetComparison", mainWindow, StringComparison.Ordinal);
        Assert.Contains("CandidateExportService", mainWindow, StringComparison.Ordinal);
        Assert.Contains("TryGetLocalPath", mainWindow, StringComparison.Ordinal);
        Assert.Contains("surface.Radius", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("TraceGeneric", preview, StringComparison.Ordinal);
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

    private static IReadOnlyList<(string Name, InitialStructureSpecification Specification)>
        LoadFrozenBenchmarkSpecifications()
    {
        var repositoryRoot = FindRepositoryRoot();
        return Directory.GetFiles(
                Path.Combine(repositoryRoot, "labs", "InitialStructure", "benchmarks", "specs"),
                "*.json")
            .Order(StringComparer.Ordinal)
            .Select(path => (
                Path.GetFileNameWithoutExtension(path),
                JsonSerializer.Deserialize<InitialStructureSpecification>(File.ReadAllText(path))
                    ?? throw new InvalidDataException($"Benchmark '{path}' deserialized to null.")))
            .ToArray();
    }

    private static IReadOnlyDictionary<string, int> LoadFrozenBenchmarkMinimums()
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "labs",
            "InitialStructure",
            "benchmarks",
            "accepted-baselines",
            "flat-to-usable-hybrid-v1.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement
            .GetProperty("Results")
            .EnumerateArray()
            .ToDictionary(
                item => item.GetProperty("Specification").GetString()
                    ?? throw new InvalidDataException("A benchmark result has no specification name."),
                item => item.GetProperty("MinimumAcceptedFamilies").GetInt32(),
                StringComparer.Ordinal);
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

using System.Collections.Immutable;
using System.Text.Json;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed class HappyLensWorkflowTests
{
    [Fact]
    public async Task ExplicitPerFaceGenerationAperturesPreserveThePhysicalTaskAndRoundTrip()
    {
        var plan = ParaxialSeedSearchTests.Plan();
        var descriptor = Descriptor(StructureGenerationMethod.Delano) with
        {
            Elements = plan.Seeds[0].Elements.Select(element =>
            new StructureElementDescriptor
            {
                Materials = [element.Material],
                FrontAperture = new(element.Front.ClearSemiDiameterMillimeters, element.Front.MechanicalSemiDiameterMillimeters),
                BackAperture = new(element.Back.ClearSemiDiameterMillimeters, element.Back.MechanicalSemiDiameterMillimeters)
            }).ToImmutableArray()
        };
        var random = new DeterministicRandom(42);
        using var rays = SequentialTraceMeasurement.Begin();
        var generated = StructureSeedGenerator.Create(plan, descriptor, ref random, () => { }, default);
        Assert.Equal(plan.Seeds[0].Elements.Select(element => (element.Front.ClearSemiDiameterMillimeters, element.Front.MechanicalSemiDiameterMillimeters,
            element.Back.ClearSemiDiameterMillimeters, element.Back.MechanicalSemiDiameterMillimeters)), generated.Elements.Select(element =>
            (element.Front.ClearSemiDiameterMillimeters, element.Front.MechanicalSemiDiameterMillimeters, element.Back.ClearSemiDiameterMillimeters, element.Back.MechanicalSemiDiameterMillimeters)));
        Assert.Equal(0, rays.RayCount);
        Assert.Throws<ArgumentException>(() => ParaxialSeedSearch.ValidateOptions(new()
        {
            Generation = descriptor with
            { Elements = descriptor.Elements.SetItem(0, descriptor.Elements[0] with { FrontAperture = new(5, 4) }) }
        }));
        var path = Path.Combine(Path.GetTempPath(), "per-face-" + Guid.NewGuid().ToString("N") + ".topology-plan.json");
        try
        {
            var configured = plan with { Options = plan.Options with { SeedPreparation = new() { Generation = descriptor } } };
            await AutonomousDesignFiles.SavePlanAsync(configured, path);
            Assert.Equal(ContentFingerprint.Compute(configured), ContentFingerprint.Compute(await AutonomousDesignFiles.LoadPlanAsync(path)));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task PopulationTrialsAlternateWithActualLocalAndFullStagesAndResumeWithoutReplayingWork()
    {
        var plan = PopulationPlan();
        using var stop = new CancellationTokenSource();
        var directory = Path.Combine(Path.GetTempPath(), "population-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "search.json");
        try
        {
            var store = new AutonomousSearchCheckpointStore();
            var partial = AutonomousSearchService.Run(AutonomousSearchService.Create("population-resume", plan), state =>
            {
                store.SaveAsync(state, path, state.Revision - 1).AsTask().GetAwaiter().GetResult();
                if (state.Events.Any(item => item.Operation == "population-difference" && item.Phase == AutonomousSearchPhase.Local)
                    && state.Reservation is null) stop.Cancel();
            }, stop.Token);
            Assert.Equal(AutonomousSearchStatus.Cancelled, partial.Status);
            var loaded = await store.LoadAsync(path);
            Assert.Equal(partial.RandomState, loaded.RandomState);
            Assert.Equal(partial.Consumed, loaded.Consumed);
            var resumed = AutonomousSearchService.Run(loaded);
            var control = AutonomousSearchService.Run(AutonomousSearchService.Create("population-control", plan));
            Assert.Equal(control.RandomState, resumed.RandomState);
            Assert.Equal(control.Consumed with { Elapsed = TimeSpan.Zero }, resumed.Consumed with { Elapsed = TimeSpan.Zero });
            Assert.Equal(control.Candidates.Select(candidate => ContentFingerprint.Compute(candidate.Topology)),
                resumed.Candidates.Select(candidate => ContentFingerprint.Compute(candidate.Topology)));
            Assert.Contains(resumed.Events, item => item.Operation == "population-difference" && item.Phase == AutonomousSearchPhase.Local);
            Assert.Contains(resumed.Events, item => item.Operation == "population-difference" && item.Phase == AutonomousSearchPhase.Full);
            Assert.Contains(resumed.Events, item => item.Operation == "population-difference" && item.Phase == AutonomousSearchPhase.Validation
                && item.Outcome is "Accepted" or "UnmetTargets");
            Assert.Contains(resumed.Candidates, candidate => candidate.ParentId is not null);
            Assert.Equal(resumed.Consumed.TracedRays, resumed.Events.Sum(item => item.Work.TracedRays));
            Assert.InRange(resumed.Consumed.TracedRays, 1, plan.Options.MaximumTracedRays);
            Assert.True(resumed.Candidates.Min(candidate => candidate.Score) <= resumed.Candidates.Take(4).Min(candidate => candidate.Score));
            AutonomousSearchService.Validate(resumed);
            Assert.Throws<ArgumentException>(() => AutonomousSearchService.Create("incompatible", plan with
            { Options = plan.Options with { QualifiedReductionOnly = true } }));
            if (Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_HAPPYLENS_EVIDENCE") is { Length: > 0 } evidence)
            {
                Directory.CreateDirectory(evidence);
                await File.WriteAllTextAsync(Path.Combine(evidence, "population-resumed.json"), JsonSerializer.Serialize(resumed));
            }
        }
        finally { Directory.Delete(directory, true); }
    }

    private static AutonomousSearchPlan PopulationPlan()
    {
        var plan = ParaxialSeedSearchTests.Plan();
        var source = plan.Seeds[0];
        var stage = new FixedTopologyOptions
        {
            ComputeMode = Core.Differentiation.DesignComputeMode.Cpu,
            PerformFinalAssessment = false,
            RequireDescent = true,
            MaximumUpdates = 1,
            MaximumFunctionEvaluations = 8,
            MaximumGradientEvaluations = 2,
            Sampling = new() { PupilRings = 1, PupilArms = 8, ProfileRadialSamples = 8 }
        };
        return plan with
        {
            Seeds = Enumerable.Range(0, 4).Select(i => source with
            {
                Elements = source.Elements.Select(element => element with
                {
                    Front = element.Front with
                    { Profile = element.Front.Profile with { CurvaturePerMillimeter = element.Front.Profile.CurvaturePerMillimeter * (1 + .02 * i) } }
                }).ToImmutableArray()
            }).ToImmutableArray(),
            Targets = plan.Targets with { MaximumRmsRadiusMillimeters = 1e-9 },
            Options = plan.Options with
            {
                PopulationExploration = new() { RebirthProbability = 0 },
                CandidatesPerElementCount = 4,
                PoolDiversity = new(),
                AxialCompensation = null,
                Expansion = null,
                FixedTopologyOnly = true,
                CheckStageFeasibility = null,
                MaximumTrials = 2,
                MaximumFunctionEvaluations = 1000,
                MaximumGradientEvaluations = 100,
                MaximumTracedRays = 2_000_000,
                MaximumStageTracedRays = 100_000,
                FinalValidationRayReserve = 100_000,
                TimeLimit = TimeSpan.FromMinutes(2),
                Local = stage,
                Full = stage
            }
        };
    }

    [Fact]
    public void PopulationModeRetainsExplicitMaterialBranchesAndSmallPoolsUseAuditedRebirth()
    {
        var plan = PopulationPlan();
        // Deliberately loose scheduling fixture only; the measured comparison keeps product targets.
        var branchPlan = plan with
        {
            Targets = plan.Targets with
            {
                FocalLengthRelativeTolerance = 1,
                FNumberRelativeTolerance = 1,
                MaximumRmsRadiusMillimeters = 5,
                MaximumSpotRadiusMillimeters = 10,
                MaximumDistortionFraction = 1
            },
            Options = plan.Options with
            {
                FixedTopologyOnly = null,
                Expansion = new()
                {
                    EnableDeletion = false,
                    EnableInsertion = false,
                    EnableSplit = false,
                    EnableMerge = false,
                    EnableProfile = false,
                    EnableMaterial = true,
                    MaterialAlternatives = ["N-BK7", "N-F2"]
                }
            }
        };
        var result = AutonomousSearchService.Run(AutonomousSearchService.Create("population-discrete", branchPlan));
        Assert.Contains(result.Events, item => item.Operation == "material");
        Assert.Contains(result.Events, item => item.Operation == "population-difference");
        var small = plan with { Seeds = [plan.Seeds[0]], Options = plan.Options with { MaximumTrials = 1 } };
        var rebirth = AutonomousSearchService.Run(AutonomousSearchService.Create("population-small", small));
        Assert.Contains(rebirth.Events, item => item.Operation == "population-rebirth");
        Assert.InRange(rebirth.Consumed.TracedRays, 1, small.Options.MaximumTracedRays);
    }

    [Theory]
    [InlineData(StructureGenerationMethod.RandomPhysical)]
    [InlineData(StructureGenerationMethod.Delano)]
    public void DescriptorGenerationDoesNotDependOnTemplateLensShapesAndNeverCertifiesImageQuality(StructureGenerationMethod method)
    {
        var plan = ParaxialSeedSearchTests.Plan();
        var generation = Descriptor(method);
        var other = plan with
        {
            Seeds = [plan.Seeds[0] with { Elements = plan.Seeds[0].Elements.Select(element => element with
        { Front = element.Front with { Profile = element.Front.Profile with { CurvaturePerMillimeter = .001 } } }).ToImmutableArray() }]
        };
        var a = new DeterministicRandom(123); var b = new DeterministicRandom(123); var work = 0;
        using var trace = SequentialTraceMeasurement.Begin();
        var first = StructureSeedGenerator.Create(plan, generation, ref a, () => work++, default);
        var second = StructureSeedGenerator.Create(other, generation, ref b, () => { }, default);
        Assert.Equal(ContentFingerprint.Compute(first), ContentFingerprint.Compute(second));
        Assert.Equal(plan.Seeds[0].Stop, first.Stop); Assert.Equal(plan.Seeds[0].Image, first.Image);
        Assert.Equal(plan.Seeds[0].Fields, first.Fields); Assert.Equal(3, first.Elements.Length);
        Assert.Equal(0, trace.RayCount);
        Assert.Equal(method == StructureGenerationMethod.Delano ? generation.AnnealingProposals + 2 : 0, work);
        var third = StructureSeedGenerator.Create(plan, generation, ref a, () => { }, default);
        Assert.NotEqual(ContentFingerprint.Compute(first), ContentFingerprint.Compute(third));
    }

    [Theory]
    [InlineData(StructureGenerationMethod.RandomPhysical)]
    [InlineData(StructureGenerationMethod.Delano)]
    public async Task ActualCorePreparationHasFiniteAccountingAndRetainedSeedsPassOriginalFirstOrderAndGeometry(StructureGenerationMethod method)
    {
        var plan = ParaxialSeedSearchTests.Plan();
        var options = new ParaxialSeedSearchOptions
        {
            Generation = Descriptor(method),
            MaximumStarts = 4,
            RetainedSeeds = 4,
            MaximumFunctionEvaluations = 400,
            MaximumEvaluationsPerStart = 100,
            TimeLimit = TimeSpan.FromSeconds(30)
        };
        var result = ParaxialSeedSearch.Run(plan, options);
        Assert.Equal("initial-structure-descriptor-seeds-v1", result.Algorithm);
        Assert.Equal(result.Work.FunctionEvaluations, result.Attempts.Sum(attempt => attempt.FunctionEvaluations));
        Assert.InRange(result.Work.FunctionEvaluations, 1, 400); Assert.Equal(0, result.Work.TracedRays);
        Assert.All(result.Attempts, attempt => Assert.InRange(attempt.FunctionEvaluations, 0, 100));
        Assert.NotEmpty(result.Seeds);
        foreach (var seed in result.Seeds)
        {
            var source = TopologyCompiler.Compile(seed, plan.Constraints); var optic = source.CreateOptic();
            Assert.InRange(Math.Abs(optic.Paraxial.EstimateEffectiveFocalLength() / plan.Targets.EffectiveFocalLengthMillimeters - 1), 0, plan.Targets.FocalLengthRelativeTolerance);
            Assert.InRange(Math.Abs(optic.Paraxial.EstimateFNumber() / plan.Targets.FNumber - 1), 0, plan.Targets.FNumberRelativeTolerance);
            Assert.InRange(Math.Abs(optic.Paraxial.GetTransferMatrix(source.SurfaceIndices[seed.Reference.Id], source.SurfaceIndices[seed.Image.Id]).A), 0, options.FocusATolerance);
            Assert.All(TopologyEvaluationPlan.Geometry(source, plan.Targets, 64), row =>
            {
                var value = TopologyEvaluationPlan.FormalMeasure(optic, row);
                Assert.True(double.IsFinite(value) && (row.Upper ? value <= row.Limit : value >= row.Limit));
            });
        }
        if (Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_HAPPYLENS_EVIDENCE") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            await ParaxialSeedSearchFiles.SaveAsync(result, Path.Combine(directory, method + ".seed-search.json"));
            await AutonomousDesignFiles.SavePlanAsync(plan with
            {
                Seeds = result.Seeds,
                Options = plan.Options with
                { SeedPreparation = options, PopulationExploration = new(), PoolDiversity = new() }
            }, Path.Combine(directory, method + ".topology-plan.json"));
        }
    }

    [Fact]
    public void UnsupportedGroupsUnknownVersionsAndHardCapsCannotBypassOriginalConstraints()
    {
        var plan = ParaxialSeedSearchTests.Plan(); var descriptor = Descriptor(StructureGenerationMethod.Delano);
        Assert.Throws<ArgumentException>(() => ParaxialSeedSearch.ValidateOptions(new()
        {
            Generation = descriptor with
            { Elements = descriptor.Elements.SetItem(0, descriptor.Elements[0] with { Kind = "D" }) }
        }));
        Assert.Throws<ArgumentException>(() => AutonomousSearchService.Create("bad-version", plan with
        {
            Options = plan.Options with
            { PopulationExploration = new() { Version = 2 } }
        }));
        var options = new ParaxialSeedSearchOptions
        {
            Generation = descriptor,
            MaximumStarts = 2,
            MaximumFunctionEvaluations = 6,
            MaximumEvaluationsPerStart = 3
        };
        var result = ParaxialSeedSearch.Run(plan, options);
        Assert.InRange(result.Work.FunctionEvaluations, 1, 6);
        Assert.All(result.Attempts, attempt => Assert.InRange(attempt.FunctionEvaluations, 0, 3));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Equal(0, ParaxialSeedSearch.Run(plan, options, cancelled.Token).Work.FunctionEvaluations);
        var random = new DeterministicRandom(42);
        Assert.Throws<ArgumentException>(() => StructureSeedGenerator.Create(plan with
        {
            Seeds = [plan.Seeds[0] with
            { Elements = plan.Seeds[0].Elements.SetItem(0, plan.Seeds[0].Elements[0] with { Protected = true }) }]
        }, descriptor, ref random, () => { }, default));
    }

    [Fact]
    public void DescriptorCanPlaceAllElementsBeforeTheFixedStopWithoutReducingDeclaredBackClearance()
    {
        var plan = ParaxialSeedSearchTests.Plan();
        plan = plan with { Seeds = [plan.Seeds[0] with { Stop = new("stop", 45, 2) }] };
        var descriptor = Descriptor(StructureGenerationMethod.RandomPhysical) with { StopAfterElement = 3 };
        var random = new DeterministicRandom(42);
        var value = StructureSeedGenerator.Create(plan, descriptor, ref random, () => { }, default);
        Assert.All(value.Elements, element => Assert.True(element.BackZMillimeters < value.Stop.ZMillimeters));
        Assert.True(value.Image.ZMillimeters - value.Elements[^1].BackZMillimeters >= descriptor.MinimumBackFocusMillimeters);
    }

    internal static StructureGenerationOptions Descriptor(StructureGenerationMethod method) => new()
    {
        Method = method,
        Elements = [new() { Materials = ["N-BK7"] }, new() { Materials = ["N-F2"] }, new() { Materials = ["N-BK7"] }],
        ClearSemiDiameterMillimeters = 5,
        MinimumThicknessMillimeters = 2,
        MaximumThicknessMillimeters = 3,
        MinimumBackFocusMillimeters = 10,
        AnnealingProposals = 8
    };
}

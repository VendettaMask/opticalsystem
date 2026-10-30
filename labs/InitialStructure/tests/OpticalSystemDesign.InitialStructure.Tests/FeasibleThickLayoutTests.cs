using System.Collections.Immutable;
using System.Text.Json;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed class FeasibleThickLayoutTests
{
    [Fact]
    public void StopPartitionSearchRejectsImpossibleSidesWithoutMovingTheStop()
    {
        // A front stop at the reference plane has room for all lenses behind
        // it, and provably no positive-thickness lenses in front of it.
        var original = AutonomousDesignExamples.ThreeElement();
        var plan = original with
        {
            Seeds = [original.Seeds[0] with { Stop = new("stop", 0, 3.125), Image = new("image", 65) }],
            Targets = original.Targets with { EffectiveFocalLengthMillimeters = 50, FNumber = 8,
                MinimumEdgeThicknessMillimeters = .1, MinimumEdgeAirGapMillimeters = .05, MaximumOpticalTrackMillimeters = 65 },
            Constraints = original.Constraints with { MinimumCenterThicknessMillimeters = .1,
                MinimumAirGapMillimeters = .05, MinimumBackFocusMillimeters = 15, MaximumMechanicalTrackMillimeters = 65 }
        };
        var partitions = ImmutableArray.Create(0, 1, 3);
        var descriptor = new StructureGenerationOptions { Method = StructureGenerationMethod.Delano, MaterializationVersion = 3,
            Elements = [new() { Materials = ["N-BK7"] }, new() { Materials = ["N-F2"] }, new() { Materials = ["N-BK7"] }],
            ClearSemiDiameterMillimeters = 5, MinimumThicknessMillimeters = .5, MaximumThicknessMillimeters = 2,
            MinimumBackFocusMillimeters = 15, AnnealingProposals = 24,
            FeasibleLayouts = new() { MaximumLayouts = partitions.Length, StopAfterElementCandidates = partitions } };
        var random = new DeterministicRandom(42); var work = new StructureGenerationWork();
        var topology = StructureSeedGenerator.Create(plan, descriptor, ref random, () => { }, default, work: work);
        Assert.Equal(partitions.Length, work.AttemptedLayouts);
        Assert.Equal(1, work.FeasibleLayouts);
        Assert.Equal(0, work.SelectedStopAfterElement);
        Assert.Equal(plan.Seeds[0].Stop, topology.Stop);
        Assert.Equal(work.SelectedStopAfterElement, topology.Elements.Count(e => e.BackZMillimeters < topology.Stop.ZMillimeters));
        AssertFeasible(plan, topology);
    }

    [Fact]
    public async Task NativeThickLayoutsPassGeometryBeforeRestorationAndPreserveFixedEnvelope()
    {
        var (plan, options) = await DelanoMaterializationTests.Wide();
        var descriptor = options.Generation! with { MaterializationVersion = 3, FeasibleLayouts = new() };
        var random = new DeterministicRandom(42);
        var work = new StructureGenerationWork(); var functions = 0;
        using var rays = SequentialTraceMeasurement.Begin();
        var topology = StructureSeedGenerator.Create(plan, descriptor, ref random, () => functions++, default, work: work);
        Assert.Equal(3, work.AttemptedLayouts); Assert.InRange(work.FeasibleLayouts, 1, 3);
        Assert.InRange(functions, 1, descriptor.AnnealingProposals + 3 * descriptor.FeasibleLayouts.MaximumLayouts);
        Assert.True(work.MinimumClearanceMarginMillimeters >= 0);
        Assert.Equal(plan.Seeds[0].Reference, topology.Reference); Assert.Equal(plan.Seeds[0].Stop, topology.Stop);
        Assert.Equal(plan.Seeds[0].Image, topology.Image); Assert.Equal(plan.Seeds[0].Fields, topology.Fields);
        Assert.Equal(plan.Seeds[0].Wavelengths, topology.Wavelengths);
        AssertFeasible(plan, topology);
        Assert.Equal(0, rays.RayCount);
    }

    [Theory]
    [InlineData(3, 8.0, 5.0, 42)]
    [InlineData(3, 8.0, 12.5, 42)]
    [InlineData(8, 8.0, 5.0, 42)]
    [InlineData(8, 8.0, 12.5, 42)]
    [InlineData(8, 8.0, 12.5, 73)]
    [InlineData(8, 4.0, 12.5, 42)]
    public async Task SeveralSpecificationsUseBoundedNativePreparationWithoutRelaxingAcceptance(int count, double fNumber, double halfField, long seed)
    {
        var source = AutonomousDesignExamples.ThreeElement();
        var plan = source with
        {
            Seeds = [source.Seeds[0] with { Stop = source.Seeds[0].Stop with { ClearSemiDiameterMillimeters = 50 / (2 * fNumber) },
                Image = source.Seeds[0].Image with { ZMillimeters = 65 }, Fields = [new("axis", 0, 0), new("edge", 0, halfField)] }],
            Targets = source.Targets with { EffectiveFocalLengthMillimeters = 50, FNumber = fNumber,
                MinimumEdgeThicknessMillimeters = .1, MinimumEdgeAirGapMillimeters = .05, MaximumOpticalTrackMillimeters = 65 },
            Constraints = source.Constraints with { MinimumElementCount = 2, MaximumElementCount = 8,
                MinimumCenterThicknessMillimeters = .1, MinimumAirGapMillimeters = .05,
                MinimumBackFocusMillimeters = 15, MaximumMechanicalTrackMillimeters = 65 },
            Options = source.Options with { Seed = seed, AxialCompensation = null }
        };
        var options = new ParaxialSeedSearchOptions
        {
            FixedStopRecoveryVersion = 2, MaximumStarts = 3, RetainedSeeds = 3,
            MaximumFunctionEvaluations = 180, MaximumEvaluationsPerStart = 60, TimeLimit = TimeSpan.FromSeconds(30),
            Generation = new() { MaterializationVersion = 3, Method = StructureGenerationMethod.Delano,
                FeasibleLayouts = new() { MaximumLayouts = 3 },
                Elements = Enumerable.Range(0, count).Select(i => new StructureElementDescriptor
                { Materials = [i % 2 == 0 ? "N-BK7" : "N-F2"] }).ToImmutableArray(),
                ClearSemiDiameterMillimeters = Math.Max(5, 50 / (2 * fNumber)),
                MinimumThicknessMillimeters = .5, MaximumThicknessMillimeters = 2,
                MinimumBackFocusMillimeters = 15, AnnealingProposals = 24 }
        };
        var fingerprint = ContentFingerprint.Compute(plan);
        var result = ParaxialSeedSearch.Run(plan, options);
        Assert.Equal("initial-structure-feasible-thick-seeds-v3", result.Algorithm);
        Assert.Equal(fingerprint, ContentFingerprint.Compute(plan));
        Assert.InRange(result.Work.FunctionEvaluations, 1, 180);
        Assert.Equal(result.Work.FunctionEvaluations, result.Attempts.Sum(a => a.FunctionEvaluations));
        Assert.All(result.Attempts, attempt => Assert.InRange(attempt.FunctionEvaluations, 0, 60));
        Assert.All(result.Attempts, attempt => Assert.NotNull(attempt.Construction));
        Assert.Equal(0, result.Work.TracedRays);
        foreach (var topology in result.Seeds) AssertFeasible(plan, topology);
        if (count == 3 && halfField == 5) Assert.NotEmpty(result.Seeds);
        await DelanoMaterializationTests.Evidence($"feasible-{count}-{fNumber}-{halfField}-{seed}.json", result);
        if (count == 8 && fNumber == 8 && halfField == 12.5 && seed == 42 && !result.Seeds.IsEmpty)
        {
            // Independent physical inspection has its own bounded ledger. It is
            // never credited as work or certification of the first-order search.
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var physicalBudget = SequentialTraceMeasurement.BeginCpuBudget(500_000);
            using var cpu = new SequentialGpuSession(DesignComputeMode.Cpu, deadline.Token);
            var assessment = TopologyValidator.Validate(TopologyCompiler.Compile(result.Seeds[0], plan.Constraints), plan.Targets, deadline.Token);
            Assert.Equal(plan.Targets, assessment.Targets);
            await DelanoMaterializationTests.Evidence("user-spec-independent-physical-inspection.json", new
            {
                Scope = "Separate post-preparation CPU physical validation; not optimization or a changed acceptance protocol",
                Assessment = assessment, AdditionalTracedRays = physicalBudget.RayCount
            });
        }
    }

    [Fact]
    public async Task GeometryFailureBudgetCancellationAndInvalidVersionsCannotReturnUnqualifiedLayouts()
    {
        var (plan, previous) = await DelanoMaterializationTests.Wide();
        var generation = previous.Generation! with { MaterializationVersion = 3, FeasibleLayouts = new() };
        var options = previous with { Generation = generation };
        var exhausted = ParaxialSeedSearch.Run(plan, options with { MaximumFunctionEvaluations = 3, MaximumEvaluationsPerStart = 3 });
        Assert.Empty(exhausted.Seeds); Assert.InRange(exhausted.Work.FunctionEvaluations, 1, 3);
        Assert.Equal(exhausted.Work.FunctionEvaluations, exhausted.Attempts.Sum(a => a.FunctionEvaluations));
        using var cancelled = new CancellationTokenSource();
        var random = new DeterministicRandom(42); var calls = 0;
        Assert.Throws<OperationCanceledException>(() => StructureSeedGenerator.Create(plan, generation, ref random, () =>
        { if (++calls == 3) cancelled.Cancel(); }, cancelled.Token));
        foreach (var invalid in new[]
        {
            generation with { FeasibleLayouts = null },
            generation with { FeasibleLayouts = new() { MaximumLayouts = 9 } },
            generation with { FeasibleLayouts = new() { StopAfterElementCandidates = [-1] } },
            generation with { FeasibleLayouts = new() { StopAfterElementCandidates = [0, 0] } },
            generation with { MaterializationVersion = 2 },
            generation with { MaterializationVersion = 4 }
        }) Assert.Throws<ArgumentException>(() => ParaxialSeedSearch.ValidateOptions(options with { Generation = invalid }));
        // An impossible clearance remains a rejected preparation, not a relaxed target.
        var impossible = ParaxialSeedSearch.Run(plan with { Targets = plan.Targets with { MinimumEdgeThicknessMillimeters = 100 } }, options);
        Assert.Empty(impossible.Seeds); Assert.All(impossible.Attempts, a => Assert.Null(a.Endpoint));
        var legacy = JsonSerializer.Serialize(previous);
        Assert.DoesNotContain("FeasibleLayouts", legacy);
        Assert.DoesNotContain("Construction", JsonSerializer.Serialize(new ParaxialSeedAttempt(0, "fixture", "historical", 0, null, null)));
    }

    private static void AssertFeasible(AutonomousSearchPlan plan, DesignTopology topology)
    {
        var compiled = TopologyCompiler.Compile(topology, plan.Constraints); var optic = compiled.CreateOptic();
        Assert.InRange(Math.Abs(optic.Paraxial.EstimateEffectiveFocalLength() / plan.Targets.EffectiveFocalLengthMillimeters - 1), 0, plan.Targets.FocalLengthRelativeTolerance);
        Assert.InRange(Math.Abs(optic.Paraxial.EstimateFNumber() / plan.Targets.FNumber - 1), 0, plan.Targets.FNumberRelativeTolerance);
        Assert.All(TopologyEvaluationPlan.Geometry(compiled, plan.Targets, 64), row =>
        {
            var value = TopologyEvaluationPlan.FormalMeasure(optic, row);
            Assert.True(double.IsFinite(value) && (row.Upper ? value <= row.Limit : value >= row.Limit), row.Id);
        });
    }
}

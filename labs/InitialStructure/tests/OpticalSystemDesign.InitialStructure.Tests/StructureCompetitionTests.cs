using System.Collections.Immutable;
using System.Text.Json;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed class StructureCompetitionTests
{
    internal static (AutonomousSearchPlan Plan, ParaxialSeedSearchOptions Options) Specification(int count = 3, double halfField = 5, double fNumber = 8)
    {
        var source = AutonomousDesignExamples.ThreeElement();
        var plan = source with
        {
            Seeds = [source.Seeds[0] with { Stop = new("stop", 0, 50 / (2 * fNumber)), Image = new("image", 65),
                Fields = [new("axis", 0, 0), new("edge", 0, halfField)] }],
            Targets = source.Targets with
            {
                EffectiveFocalLengthMillimeters = 50,
                FNumber = fNumber,
                MinimumEdgeAirGapMillimeters = .05,
                MaximumOpticalTrackMillimeters = 65
            },
            Constraints = source.Constraints with
            {
                MinimumElementCount = 2,
                MaximumElementCount = 8,
                MinimumCenterThicknessMillimeters = .1,
                MinimumAirGapMillimeters = .05,
                MinimumBackFocusMillimeters = 15,
                MaximumMechanicalTrackMillimeters = 65
            },
            Options = source.Options with { AxialCompensation = new() { MaximumDisplacementMillimeters = .1 } }
        };
        return (plan, new()
        {
            MaximumStarts = 4,
            RetainedSeeds = 2,
            MaximumFunctionEvaluations = 200,
            MaximumEvaluationsPerStart = 50,
            MaximumTracedRays = 500000,
            TimeLimit = TimeSpan.FromSeconds(110),
            Competition = new() { UpdatesPerStage = 2, MaximumStageTimePerCandidate = TimeSpan.FromSeconds(2) },
            Generation = new()
            {
                MaterializationVersion = 3,
                Method = StructureGenerationMethod.Delano,
                FeasibleLayouts = new() { StopAfterElementCandidates = [0, count / 2, count] },
                Elements = Enumerable.Range(0, count).Select(i => new StructureElementDescriptor
                { Materials = [i % 2 == 0 ? "N-BK7" : "N-F2"] }).ToImmutableArray(),
                ClearSemiDiameterMillimeters = 5,
                MinimumThicknessMillimeters = .5,
                MaximumThicknessMillimeters = 2,
                MinimumBackFocusMillimeters = 15,
                AnnealingProposals = 12
            }
        });
    }

    [Fact]
    public void StagesRestoreOriginalSpecificationAndRejectPartialFinalSpecifications()
    {
        var (plan, options) = Specification(); var fingerprint = ContentFingerprint.Compute(plan);
        var stage = StructureCompetitionSearch.StageSpecification(plan.Seeds[0], plan.Targets, options.Competition!.Stages[0]);
        Assert.Equal(2.5, stage.Topology.Fields[1].YAngleDegrees);
        Assert.Equal(plan.Seeds[0].Stop.ClearSemiDiameterMillimeters * .7, stage.Topology.Stop.ClearSemiDiameterMillimeters);
        Assert.Equal(plan.Targets.FNumber / .7, stage.Targets.FNumber);
        Assert.Equal(plan.Seeds[0].Elements, stage.Topology.Elements);
        Assert.Equal(plan.Targets with { FNumber = stage.Targets.FNumber }, stage.Targets);
        var final = StructureCompetitionSearch.StageSpecification(plan.Seeds[0], plan.Targets, options.Competition.Stages[^1]);
        Assert.Equal(ContentFingerprint.Compute(plan.Seeds[0]), ContentFingerprint.Compute(final.Topology));
        Assert.Equal(plan.Targets, final.Targets); Assert.Equal(fingerprint, ContentFingerprint.Compute(plan));
        foreach (var invalid in new[]
        {
            options.Competition with { Stages = [new("Partial", .5, .5)] },
            options.Competition with { Stages = [new("First", .8, .8), new("Backwards", .5, 1), new("Original", 1, 1)] },
            options.Competition with { Families = [StructureRayPathFamily.Balanced, StructureRayPathFamily.Balanced] },
            options.Competition with { MaximumStageTimePerCandidate = TimeSpan.Zero },
            options.Competition with { FinalValidationTimeReserveFraction = .99 },
            options.Competition with { FinalValidationRayReserve = options.MaximumTracedRays + 1 }
        }) Assert.Throws<ArgumentException>(() => ParaxialSeedSearch.ValidateOptions(options with { Competition = invalid }));
        Assert.Throws<ArgumentException>(() => ParaxialSeedSearch.ValidateOptions(options with { ImageScreening = new() }));
        Assert.DoesNotContain("Competition", JsonSerializer.Serialize(new ParaxialSeedSearchOptions()));
    }

    [Theory]
    [InlineData(3, 5.0, 8.0)]
    [InlineData(8, 12.5, 8.0)]
    [InlineData(8, 12.5, 4.0)]
    public async Task NativeCandidatesCompeteInEqualRoundsAndOnlyOriginalValidationQualifies(int count, double halfField, double fNumber)
    {
        var (plan, options) = Specification(count, halfField, fNumber);
        var original = ContentFingerprint.Compute(plan);
        var result = ParaxialSeedSearch.Run(plan, options); var competition = result.Competition!;
        await DelanoMaterializationTests.Evidence($"competition-{count}-F{fNumber}-full{2 * halfField}.json", result);
        if (Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_MATERIALIZATION_EVIDENCE") is { } directory)
        {
            var path = Path.Combine(directory, $"competition-{count}-F{fNumber}-full{2 * halfField}.seed-search.json");
            await ParaxialSeedSearchFiles.SaveAsync(result, path);
            Assert.InRange(new FileInfo(path).Length, 1, InitialStructureLimits.MaximumManifestBytes);
            using var saved = JsonDocument.Parse(await File.ReadAllBytesAsync(path));
            Assert.Equal(ContentFingerprint.Compute(result), saved.RootElement.GetProperty("sha256").GetString());
        }
        Assert.Equal("initial-structure-competition-v1", result.Algorithm);
        Assert.Equal(original, ContentFingerprint.Compute(plan));
        Assert.Equal(4, competition.Candidates.Length);
        Assert.Equal(4, competition.Candidates.Select(c => c.Family).Distinct().Count());
        var constructed = result.Attempts.Where(a => a.Endpoint is not null).ToArray();
        Assert.True(constructed.Length >= 2);
        Assert.Equal(constructed.Length, constructed.Select(a => ContentFingerprint.Compute(a.Endpoint!)).Distinct().Count());
        Assert.NotEmpty(competition.Rounds);
        Assert.Equal(constructed.Length, competition.Rounds.Count(r => r.StageIndex == 0));
        foreach (var group in competition.Rounds.GroupBy(r => r.StageIndex))
        {
            Assert.All(group, r => Assert.Equal(group.First().Allocation, r.Allocation));
            Assert.All(group, r => Assert.Equal(options.Competition!.Stages[r.StageIndex], r.Stage));
            Assert.All(group, r => Assert.True(r.Work.FunctionEvaluations <= options.Competition!.UpdatesPerStage + 2));
            Assert.All(group, r => Assert.InRange(r.Work.TracedRays, 0, r.Allocation.MaximumTracedRays));
            Assert.All(group, r => Assert.InRange(r.Work.GradientEvaluations, 0, r.Allocation.MaximumGradientEvaluations));
            Assert.All(group, r => Assert.Equal(plan.Targets.FNumber / r.Stage.PupilFraction, r.Optimization!.Targets.FNumber));
            if (group.Key < options.Competition!.Stages.Length - 1 && group.Count() > 2)
                Assert.InRange(group.Count(r => r.Selected), 2, group.Count());
        }
        Assert.InRange(result.Work.FunctionEvaluations, 1, options.MaximumFunctionEvaluations);
        Assert.InRange(result.Work.GradientEvaluations, 1, options.Competition!.MaximumGradientEvaluations);
        Assert.InRange(result.Work.TracedRays, 1, options.MaximumTracedRays);
        Assert.Equal(result.Work.FunctionEvaluations, competition.Candidates.Sum(c => c.Work.FunctionEvaluations));
        Assert.Equal(result.Work.TracedRays, competition.Candidates.Sum(c => c.Work.TracedRays));
        Assert.Equal(result.Work.GradientEvaluations, competition.Candidates.Sum(c => c.Work.GradientEvaluations));
        Assert.Equal(result.Work.GeometryProbes, competition.Candidates.Sum(c => c.Work.GeometryProbes));
        Assert.All(competition.Candidates, c => Assert.InRange(c.Work.FunctionEvaluations, 0, options.MaximumEvaluationsPerStart));
        var assessed = competition.Candidates.Where(c => c.OriginalAssessment is not null).ToArray();
        Assert.NotEmpty(assessed);
        Assert.All(assessed, c =>
        {
            var a = c.OriginalAssessment!;
            Assert.Equal(plan.Targets, a.Targets); Assert.Equal(plan.Constraints, a.StructuralConstraints);
            var compiled = TopologyCompiler.Compile(c.Topology!, plan.Constraints);
            Assert.Equal(TopologyValidator.GetProtocolFingerprint(compiled, plan.Targets), a.ProtocolFingerprint);
            Assert.Equal(compiled.OpticFingerprint, a.OpticFingerprint);
            Assert.Equal(a.Completed && a.MeetsTargets, competition.QualifiedIds.Contains(c.Id));
            Assert.Equal(plan.Seeds[0].Stop, c.Topology!.Stop); Assert.Equal(plan.Seeds[0].Fields, c.Topology.Fields);
            var initial = constructed.Single(x => x.Index == int.Parse(c.Id.AsSpan(10))).Endpoint!;
            foreach (var (element, anchor) in c.Topology.Elements.Zip(initial.Elements))
            {
                Assert.InRange(Math.Abs(element.FrontZMillimeters - anchor.FrontZMillimeters), 0, .10000001);
                Assert.InRange(Math.Abs(element.BackZMillimeters - anchor.BackZMillimeters), 0, .10000001);
            }
        });
        Assert.All(competition.SelectedIds, id => Assert.True(competition.Candidates.Single(c => c.Id == id).OriginalAssessment!.Completed));
        Assert.Equal(result.Seeds.Length, competition.SelectedIds.Length);
    }

    [Fact]
    public async Task CancellationAndSmallBudgetsRetainWorkWithoutQualifyingIntermediateResults()
    {
        var (plan, options) = Specification();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var beforeDispatch = ParaxialSeedSearch.Run(plan, options, cancelled.Token);
        Assert.Equal(ParaxialSeedTermination.Cancelled, beforeDispatch.Termination);
        Assert.Empty(beforeDispatch.Competition!.Candidates);
        Assert.Equal(0, beforeDispatch.Work.FunctionEvaluations); Assert.Equal(0, beforeDispatch.Work.TracedRays);
        using var constructionCancellation = new CancellationTokenSource();
        var stopped = ParaxialSeedSearch.Run(plan, options, constructionCancellation.Token,
            (start, _) => { if (start == 2) constructionCancellation.Cancel(); });
        Assert.Equal(ParaxialSeedTermination.Cancelled, stopped.Termination);
        Assert.Equal(2, stopped.Competition!.Candidates.Length); Assert.Empty(stopped.Seeds); Assert.Empty(stopped.Competition.QualifiedIds);
        Assert.Equal(stopped.Work.FunctionEvaluations, stopped.Competition.Candidates.Sum(c => c.Work.FunctionEvaluations));
        using var duringStage = new CancellationTokenSource(); var phaseCallbacks = 0;
        var partial = ParaxialSeedSearch.Run(plan, options, duringStage.Token, (start, _) =>
        { if (start == options.MaximumStarts && ++phaseCallbacks == 2) duringStage.Cancel(); });
        Assert.Equal(ParaxialSeedTermination.Cancelled, partial.Termination);
        Assert.Single(partial.Competition!.Rounds); Assert.Empty(partial.Competition.QualifiedIds);
        Assert.Equal(partial.Work.FunctionEvaluations, partial.Competition.Candidates.Sum(c => c.Work.FunctionEvaluations));
        Assert.Equal(partial.Work.TracedRays, partial.Competition.Candidates.Sum(c => c.Work.TracedRays));
        var tiny = ParaxialSeedSearch.Run(plan, options with { MaximumFunctionEvaluations = 3, MaximumEvaluationsPerStart = 3 });
        Assert.Equal(ParaxialSeedTermination.BudgetExhausted, tiny.Termination);
        Assert.Empty(tiny.Seeds); Assert.Empty(tiny.Competition!.QualifiedIds);
        Assert.InRange(tiny.Work.FunctionEvaluations, 0, 3);
        var incomplete = ParaxialSeedSearch.Run(plan, options with
        {
            MaximumStarts = 2,
            MaximumTracedRays = 5,
            Competition = options.Competition! with { FinalValidationRayReserve = 5 }
        });
        Assert.Empty(incomplete.Seeds); Assert.Empty(incomplete.Competition!.QualifiedIds);
        Assert.InRange(incomplete.Work.TracedRays, 0, 5);
        Assert.True(incomplete.Work.ValidationEvaluations > 0);
        Assert.Equal(incomplete.Work.TracedRays, incomplete.Competition.Candidates.Sum(c => c.Work.TracedRays));
        await DelanoMaterializationTests.Evidence("competition-incomplete.json", incomplete);
    }

    [Fact]
    public void PermittedPartitionsAreExploredBeforeRepeatingTheSameFamilyPartitionPair()
    {
        var (plan, options) = Specification();
        plan = plan with { Seeds = [plan.Seeds[0] with { Stop = plan.Seeds[0].Stop with { ZMillimeters = 16 } }] };
        var result = ParaxialSeedSearch.Run(plan, options with
        {
            MaximumTracedRays = 5,
            Competition = options.Competition! with { FinalValidationRayReserve = 5 }
        });
        Assert.Equal(new[] { 0, 1, 3, 0 }, result.Competition!.Candidates.Select(c => c.StopAfterElement));
        Assert.All(result.Competition.Candidates.Where(c => c.Topology is not null),
            c => Assert.Equal(plan.Seeds[0].Stop, c.Topology!.Stop));
        Assert.Empty(result.Competition.QualifiedIds);
    }

    [Fact]
    public void AllPathFamiliesUseTheSameBoundedNativeLayoutAndPreserveReproducibility()
    {
        var (plan, options) = Specification(); var fingerprints = new HashSet<string>();
        foreach (var family in options.Competition!.Families)
        {
            var a = new DeterministicRandom(42); var b = new DeterministicRandom(42);
            var descriptor = options.Generation! with { AnnealingProposals = 0 };
            var shape = StructureCompetitionSearch.InitialShape(family, 3);
            var first = ThickStructureSeedGenerator.Create(plan, descriptor, ref a, () => { }, new(), default, true, shape);
            var second = ThickStructureSeedGenerator.Create(plan, descriptor, ref b, () => { }, new(), default, true, shape);
            Assert.Equal(ContentFingerprint.Compute(first), ContentFingerprint.Compute(second));
            fingerprints.Add(ContentFingerprint.Compute(first));
            var optic = TopologyCompiler.Compile(first, plan.Constraints).CreateOptic();
            Assert.InRange(Math.Abs(optic.Paraxial.EstimateEffectiveFocalLength() / 50 - 1), 0, 1e-8);
            Assert.InRange(Math.Abs(optic.Paraxial.EstimateFNumber() / 8 - 1), 0, 1e-8);
        }
        Assert.Equal(4, fingerprints.Count);
    }
}

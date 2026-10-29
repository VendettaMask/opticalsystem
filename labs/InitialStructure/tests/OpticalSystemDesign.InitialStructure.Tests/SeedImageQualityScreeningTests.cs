using System.Collections.Immutable;
using System.Text.Json;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed class SeedImageQualityScreeningTests
{
    [Fact]
    public async Task SparseScreenCompletesWideFieldsWithinTheOriginalTotalPreparationQuota()
    {
        var (plan, previous) = await DelanoMaterializationTests.Wide();
        var options = previous with { Generation = previous.Generation! with { MaterializationVersion = 2 },
            ImageScreening = new() { BoundaryCheckArms = 8, PupilRings = 1, PupilArms = 4 } };
        var result = ParaxialSeedSearch.Run(plan, options);
        if (Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_IMAGE_SCREEN_EVIDENCE") is { } directory)
            await ParaxialSeedSearchFiles.SaveAsync(result, Path.Combine(directory, "development-preparation.seed-search.json"));
        Assert.Equal(4, result.ImageScreens!.Value.Length);
        Assert.All(result.ImageScreens.Value, screen =>
        {
            Assert.Equal("Screened", screen.Outcome); Assert.Equal(3, screen.CompletedFields);
            Assert.InRange(screen.TracedRays, 1, 2500); Assert.Equal(1, screen.MinimumValidFraction);
        });
        Assert.InRange(result.Work.TracedRays, 1, 10000); Assert.Single(result.Seeds);
        Assert.Equal(result.ImageScreens.Value.Min(screen => screen.QualityScore), result.ImageScreens.Value.Single(screen =>
            screen.TopologyFingerprint == TopologyDiversitySelection.PrescriptionIdentity(result.Seeds[0])).QualityScore);
    }

    [Fact]
    public void ScreenMatchesSharedCorePolychromaticMetricsAndCountsAllActualRays()
    {
        var plan = ParaxialSeedSearchTests.Plan();
        var source = TopologyCompiler.Compile(plan.Seeds[0], plan.Constraints);
        var options = new SeedImageScreeningOptions { MaximumTracedRaysPerCandidate = 100000 };
        using var outer = SequentialTraceMeasurement.Begin();
        var result = SeedImageQualityScreening.Evaluate(source, plan.Targets, options, default);
        Assert.Equal(result.TracedRays, outer.RayCount);
        Assert.True(result.TracedRays > 0);
        Assert.Equal("Screened", result.Outcome);
        var optic = source.CreateOptic();
        var pupils = ApertureSampler.GenerateGaussianQuadrature(options.PupilRings, options.PupilArms).ToArray();
        var actual = SpotMetricEvaluator.EvaluatePupilSamplesWithChecks(optic, 0, 0, pupils, TopologyEvaluationPlan.Checks(),
            includeSurfaceTransmission: false);
        Assert.Equal(actual.Integration.Metrics!.RmsSpotRadius, result.WorstRmsMillimeters);
        Assert.Equal(actual.MaximumRadius, result.WorstMaximumMillimeters);
        Assert.Equal(3, actual.Integration.Wavelengths.Count);
        Assert.Equal(Math.Max(result.WorstRmsMillimeters!.Value / plan.Targets.MaximumRmsRadiusMillimeters,
            result.WorstMaximumMillimeters!.Value / plan.Targets.MaximumSpotRadiusMillimeters), result.QualityScore);
        Assert.Equal(1, result.MinimumValidFraction);
    }

    [Fact]
    public void RealSpotQualityWinsOverSmallerFirstOrderResidual()
    {
        var plan = ParaxialSeedSearchTests.Plan();
        var first = plan.Seeds[0]; var element = first.Elements[0];
        var second = first with { Elements = first.Elements.SetItem(0, element with
        {
            Front = element.Front with { Profile = element.Front.Profile with
                { CurvaturePerMillimeter = element.Front.Profile.CurvaturePerMillimeter * .85 } }
        }) };
        var options = new SeedImageScreeningOptions { MaximumTracedRaysPerCandidate = 100000 };
        var assessed = new[] { first, second }.Select(seed => (Topology: seed,
            Result: SeedImageQualityScreening.Evaluate(TopologyCompiler.Compile(seed, plan.Constraints), plan.Targets, options, default))).ToArray();
        Assert.All(assessed, value => Assert.Equal("Screened", value.Result.Outcome));
        var ordered = assessed.OrderBy(value => value.Result.QualityScore).ToArray();
        Assert.True(ordered[0].Result.QualityScore < ordered[1].Result.QualityScore);
        var selected = SeedImageQualityScreening.Select([(ordered[1].Topology, 0), (ordered[0].Topology, 1)],
            assessed.Select(value => value.Result).ToArray(), 1, options, plan.Targets);
        Assert.Equal(TopologyDiversitySelection.PrescriptionIdentity(ordered[0].Topology),
            TopologyDiversitySelection.PrescriptionIdentity(Assert.Single(selected)));
    }

    [Fact]
    public void WorstFieldAndEveryWavelengthUseCoreResultsAndClippingRejectsTheRank()
    {
        var plan = ParaxialSeedSearchTests.Plan();
        var topology = plan.Seeds[0] with { Fields = [new("Axis", 0, 0), new("Edge", 0, 1), new("Ignored", 0, 20, 0)] };
        var source = TopologyCompiler.Compile(topology, plan.Constraints);
        var options = new SeedImageScreeningOptions { FieldsPerDirection = 3, BoundaryCheckArms = 8, MaximumTracedRaysPerCandidate = 100000 };
        var result = SeedImageQualityScreening.Evaluate(source, plan.Targets, options, default);
        Assert.Equal("Screened", result.Outcome); Assert.Equal(3, result.CompletedFields);
        var optic = source.CreateOptic();
        var pupils = ApertureSampler.GenerateGaussianQuadrature(options.PupilRings, options.PupilArms).ToArray();
        var actual = TopologyEvaluationPlan.Fields(source, options.FieldsPerDirection).Select(field =>
            SpotMetricEvaluator.EvaluatePupilSamplesWithChecks(optic, field.X, field.Y, pupils, TopologyEvaluationPlan.Checks(options.BoundaryCheckArms ?? 32),
                includeSurfaceTransmission: false)).ToArray();
        Assert.Equal(actual.Max(field => field.Integration.Metrics!.RmsSpotRadius), result.WorstRmsMillimeters);
        Assert.Equal(actual.Max(field => field.MaximumRadius), result.WorstMaximumMillimeters);
        Assert.All(actual, field => Assert.Equal(3, field.Integration.Wavelengths.Count));
        var element = topology.Elements[0];
        var clipped = topology with { Elements = topology.Elements.SetItem(0, element with
            { Front = element.Front with { ClearSemiDiameterMillimeters = .1 } }) };
        var rejected = SeedImageQualityScreening.Evaluate(TopologyCompiler.Compile(clipped, plan.Constraints), plan.Targets, options, default);
        Assert.NotEqual("Screened", rejected.Outcome); Assert.Null(rejected.QualityScore);
    }

    [Fact]
    public void IncompleteTraceQuotaAndCancellationNeverProduceAValidRank()
    {
        var plan = ParaxialSeedSearchTests.Plan(); var source = TopologyCompiler.Compile(plan.Seeds[0], plan.Constraints);
        using (var outer = SequentialTraceMeasurement.BeginCpuBudget(5))
        {
            var exhausted = SeedImageQualityScreening.Evaluate(source, plan.Targets, new() { MaximumTracedRaysPerCandidate = 1000 }, default);
            Assert.Equal("RayBudgetExhausted", exhausted.Outcome);
            Assert.Null(exhausted.QualityScore);
            Assert.InRange(exhausted.TracedRays, 0, 5);
            Assert.Equal(outer.RayCount, exhausted.TracedRays);
            Assert.Empty(SeedImageQualityScreening.Select([(source.Topology, 0)], [exhausted], 1, new(), plan.Targets));
        }
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var stopped = SeedImageQualityScreening.Evaluate(source, plan.Targets, new(), cancellation.Token);
        Assert.Equal("Cancelled", stopped.Outcome); Assert.Equal(0, stopped.TracedRays); Assert.Null(stopped.QualityScore);
    }

    [Fact]
    public void CandidateQuotaDoesNotConsumeTheNextCandidatesAllowance()
    {
        var plan = ParaxialSeedSearchTests.Plan(); var source = TopologyCompiler.Compile(plan.Seeds[0], plan.Constraints);
        using var parent = SequentialTraceMeasurement.BeginCpuBudget(100000);
        var incomplete = SeedImageQualityScreening.Evaluate(source, plan.Targets, new() { MaximumTracedRaysPerCandidate = 5 }, default);
        Assert.Equal("RayBudgetExhausted", incomplete.Outcome);
        Assert.False(parent.LimitReached);
        var complete = SeedImageQualityScreening.Evaluate(source, plan.Targets, new() { MaximumTracedRaysPerCandidate = 20000 }, default);
        Assert.Equal("Screened", complete.Outcome);
        Assert.Equal(incomplete.TracedRays + complete.TracedRays, parent.RayCount);
    }

    [Fact]
    public void DiversityCannotPromoteAnIncompleteOrFarWorseImage()
    {
        var plan = ParaxialSeedSearchTests.Plan(); var first = plan.Seeds[0]; var element = first.Elements[0];
        var candidates = new[] { 1d, 1.05, 1.1, 1.2 }.Select(scale => first with
        { Elements = first.Elements.SetItem(0, element with { FrontZMillimeters = element.FrontZMillimeters * scale - scale }) }).ToArray();
        var scores = new[] { 1d, 1.1, 4, .1 };
        var screens = candidates.Select((seed, index) => new SeedImageScreeningResult(
            TopologyDiversitySelection.PrescriptionIdentity(seed), "Screened", index == 3 ? 1 : 3, 3,
            .01, .02, 1, true, scores[index], 100)).ToArray();
        var selected = SeedImageQualityScreening.Select(candidates.Select(seed => (seed, 0d)).ToArray(), screens, 2, new(), plan.Targets);
        Assert.Equal(candidates.Take(2), selected);
    }

    [Fact]
    public void WholePreparationChargesScreeningInsideItsOriginalBudgetAndKeepsLegacyIdentity()
    {
        var plan = ParaxialSeedSearchTests.Plan(); var originalFingerprint = ContentFingerprint.Compute(plan);
        var options = new ParaxialSeedSearchOptions { MaximumStarts = 3, RetainedSeeds = 2,
            MaximumFunctionEvaluations = 120, MaximumTracedRays = 100000, TimeLimit = TimeSpan.FromSeconds(30),
            ImageScreening = new() { MaximumTracedRaysPerCandidate = 20000 } };
        using var measured = SequentialTraceMeasurement.Begin();
        var result = ParaxialSeedSearch.Run(plan, options);
        Assert.NotEmpty(result.ImageScreens!.Value);
        Assert.NotEmpty(result.Seeds);
        Assert.Equal(result.Work.TracedRays, measured.RayCount);
        Assert.Equal(result.Attempts.Sum(attempt => attempt.FunctionEvaluations) + result.ImageScreens.Value.Length, result.Work.FunctionEvaluations);
        Assert.InRange(result.Work.TracedRays, 1, options.MaximumTracedRays);
        Assert.InRange(result.Work.FunctionEvaluations, 1, options.MaximumFunctionEvaluations);
        Assert.Equal("initial-structure-real-ray-ranked-seeds-v1", result.Algorithm);
        Assert.Equal(originalFingerprint, ContentFingerprint.Compute(plan));
        var restored = JsonSerializer.Deserialize<ParaxialSeedSearchResult>(JsonSerializer.Serialize(result));
        Assert.Equal(ContentFingerprint.Compute(result), ContentFingerprint.Compute(restored));
        var legacy = ParaxialSeedSearch.Run(plan, options with { ImageScreening = null });
        Assert.Null(legacy.ImageScreens); Assert.Equal(0, legacy.Work.TracedRays);
        Assert.Equal("initial-structure-paraxial-seeds-v1", legacy.Algorithm);
        Assert.DoesNotContain("ImageScreening", JsonSerializer.Serialize(options with { ImageScreening = null }));
        Assert.NotEqual(result.ProtocolFingerprint, legacy.ProtocolFingerprint);
    }

    [Fact]
    public void InvalidVersionIsRejectedBeforeAnyOpticalWork()
    {
        using var measured = SequentialTraceMeasurement.Begin();
        Assert.Throws<ArgumentException>(() => ParaxialSeedSearch.Run(ParaxialSeedSearchTests.Plan(),
            new() { ImageScreening = new() { Version = 2 } }));
        Assert.Equal(0, measured.RayCount);
    }
}

using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed class StructureEvolutionTests
{
    internal static (AutonomousSearchPlan Plan, ParaxialSeedSearchOptions Options) Specification()
    {
        var (plan, options) = StructureCompetitionTests.Specification(); var random = new DeterministicRandom(42);
        var seed = ThickStructureSeedGenerator.Create(plan, options.Generation!, ref random, () => { }, new(), CancellationToken.None,
            requireFeasibleGeometry: true, StructureCompetitionSearch.InitialShape(StructureRayPathFamily.Balanced, 3));
        plan = plan with
        {
            Seeds = [seed],
            Targets = plan.Targets with
            {
                MaximumRmsRadiusMillimeters = 1,
                MaximumSpotRadiusMillimeters = 2,
                MaximumDistortionFraction = 1
            }
        };
        options = options with
        {
            MaximumFunctionEvaluations = 400,
            MaximumEvaluationsPerStart = 160,
            Evolution = new() { MaximumBranches = 7, CompensationUpdatesPerBranch = 1 },
            Competition = options.Competition! with { SearchStrategy = new() { QuickEvaluation = new() }, UpdatesPerStage = 1 }
        };
        return (plan, options);
    }

    [Theory]
    [InlineData(.01)]
    [InlineData(-.01)]
    public void CoreInversePowerUsesActualThickMatrixAndPreservesOtherSurfaces(double power)
    {
        var (plan, _) = Specification(); var source = TopologyCompiler.Compile(plan.Seeds[0], plan.Constraints);
        var before = source.OpticFingerprint; var map = source.Elements[0]; var optic = source.CreateOptic(); var calls = 0;
        var assigned = ElementPowerAssignment.Assign(optic, map.FrontIndex, map.BackIndex, power, .08, charge: () => calls++);
        Assert.True(assigned.Succeeded, assigned.Outcome); Assert.Equal(3, calls); Assert.Equal(3, assigned.MatrixRequests);
        var changed = TopologyCompiler.Update(source, assigned.Snapshot!); var lens = changed.Topology.Elements[0];
        var n = optic.Materials.Resolve(lens.Material).RefractiveIndex(587.6);
        var front = lens.Front.Profile.CurvaturePerMillimeter; var back = lens.Back.Profile.CurvaturePerMillimeter;
        var analytical = (n - 1) * (front - back) + (n - 1) * (n - 1) * lens.ThicknessMillimeters * front * back / n;
        Assert.Equal(power, analytical, 11); Assert.Equal(power, assigned.ActualPowerPerMillimeter!.Value, 11);
        Assert.Equal(source.Topology.Elements[1], changed.Topology.Elements[1]); Assert.Equal(source.Topology.Elements[2], changed.Topology.Elements[2]);
        Assert.Equal(before, source.OpticFingerprint); Assert.Equal(source.Topology.Stop, changed.Topology.Stop);
    }

    [Fact]
    public void InversePowerRejectsBoundsAndHonorsCancellation()
    {
        var (plan, _) = Specification(); var source = TopologyCompiler.Compile(plan.Seeds[0], plan.Constraints); var map = source.Elements[0];
        var result = ElementPowerAssignment.Assign(source.CreateOptic(), map.FrontIndex, map.BackIndex, 100, .01);
        Assert.False(result.Succeeded); Assert.Null(result.Snapshot); Assert.Contains("Bounds", result.Outcome);
        Assert.Equal(2, result.MatrixRequests);
        using var stop = new CancellationTokenSource(); stop.Cancel();
        Assert.Throws<OperationCanceledException>(() => ElementPowerAssignment.Assign(source.CreateOptic(), map.FrontIndex, map.BackIndex, .01, .08, stop.Token));
    }

    [Theory]
    [InlineData(StructureEvolutionMode.DirectCompensation)]
    [InlineData(StructureEvolutionMode.RecoveryAndCompensation)]
    [InlineData(StructureEvolutionMode.RecoveryAndCompetition)]
    public async Task EditsUseFreshBindingsKeepOriginalParentAndChargeEveryStage(StructureEvolutionMode mode)
    {
        var (plan, options) = Specification(); options = options with { Evolution = options.Evolution! with { Mode = mode } };
        var original = ContentFingerprint.Compute(plan); var result = ParaxialSeedSearch.Run(plan, options); var e = result.Evolution!;
        Assert.Equal(original, ContentFingerprint.Compute(plan)); Assert.Equal("initial-structure-evolution-v1", result.Algorithm);
        Assert.Equal(7, e.Trials.Length); Assert.Equal(6, e.Trials.Where(t => t.Operation != "optimize").Select(t => t.Operation).Distinct().Count());
        Assert.Contains(e.QualifiedArchive, q => q.Id == "parent-001" && q.ParentId is null);
        Assert.Equal(ContentFingerprint.Compute(plan.Seeds[0]), ContentFingerprint.Compute(e.QualifiedArchive.Single(q => q.ParentId is null).Topology));
        Assert.Equal(result.Work.FunctionEvaluations, e.Parents.Sum(p => p.Work.FunctionEvaluations) + e.Trials.Sum(t => t.Work.FunctionEvaluations));
        Assert.Equal(result.Work.TracedRays, e.Parents.Sum(p => p.Work.TracedRays) + e.Trials.Sum(t => t.Work.TracedRays));
        Assert.Equal(result.Work.GradientEvaluations, e.Trials.Sum(t => t.Work.GradientEvaluations));
        Assert.InRange(result.Work.FunctionEvaluations, 1, options.MaximumFunctionEvaluations);
        Assert.InRange(result.Work.TracedRays, 1, options.MaximumTracedRays);
        foreach (var trial in e.Trials)
        {
            Assert.InRange(trial.Work.FunctionEvaluations, 0, options.MaximumEvaluationsPerStart);
            if (trial.EditedTopology is not { } edited) continue;
            Assert.Equal(plan.Seeds[0].Stop, edited.Stop); Assert.Equal(plan.Seeds[0].Image, edited.Image);
            Assert.Equal(plan.Seeds[0].Fields, edited.Fields); Assert.Equal(plan.Seeds[0].Wavelengths, edited.Wavelengths);
            var editedSource = TopologyCompiler.Compile(edited, plan.Constraints);
            Assert.Equal(editedSource.BindingFingerprint, trial.EditBindingFingerprint);
            if (trial.Compensation is not null)
            {
                var start = TopologyCompiler.Compile(trial.Recovery?.Topology ?? edited, plan.Constraints);
                Assert.Equal(start.BindingFingerprint, trial.CompensationBindingFingerprint);
                Assert.True(trial.AnchoredVariables.SequenceEqual(trial.Compensation.Variables));
                Assert.All(trial.AnchoredVariables, v => Assert.DoesNotContain(v.SurfaceId, new[] { edited.Stop.Id, edited.Reference.Id, edited.Image.Id }));
            }
            if (trial.OriginalAssessment is { } a)
            {
                Assert.Equal(plan.Targets, a.Targets); Assert.Equal(plan.Constraints, a.StructuralConstraints);
                Assert.Equal(TopologyCompiler.Compile(trial.Endpoint!, plan.Constraints).OpticFingerprint, a.OpticFingerprint);
            }
        }
        Assert.All(e.QualifiedArchive, q => { Assert.True(q.Assessment.Completed && q.Assessment.MeetsTargets); Assert.Equal(plan.Targets, q.Assessment.Targets); });
        foreach (var working in result.Seeds)
        {
            var fingerprint = ContentFingerprint.Compute(working);
            var assessment = e.QualifiedArchive.FirstOrDefault(q => ContentFingerprint.Compute(q.Topology) == fingerprint)?.Assessment
                ?? e.Trials.Single(t => t.Endpoint is not null && ContentFingerprint.Compute(t.Endpoint) == fingerprint && t.OriginalAssessment is not null).OriginalAssessment;
            Assert.True(StructureEvolutionSearch.IsWorkingSeedAssessment(assessment));
        }
        var file = Path.Combine(Path.GetTempPath(), "structure-evolution-" + Guid.NewGuid().ToString("N") + ".seed-search.json");
        try
        {
            await ParaxialSeedSearchFiles.SaveAsync(result, file); Assert.InRange(new FileInfo(file).Length, 1, InitialStructureLimits.MaximumManifestBytes);
            using var saved = JsonDocument.Parse(await File.ReadAllBytesAsync(file)); Assert.Equal(ContentFingerprint.Compute(result), saved.RootElement.GetProperty("sha256").GetString());
        }
        finally { if (File.Exists(file)) File.Delete(file); }
    }

    [Fact]
    public void PowerAllocationClosesRemainingTargetsOrReportsChargedFailureWithoutChangingFrozenPowers()
    {
        var (plan, options) = Specification(); var found = false; var outcomes = new List<string>();
        for (var seed = 42; seed <= 46; seed++)
        {
            var random = new DeterministicRandom(seed);
            var result = PowerAllocationSeedGenerator.Run("parent", plan.Seeds[0], plan, options,
                new() { ClosureFunctionsPerAttempt = 80 }, 100, TimeSpan.FromSeconds(4), ref random, CancellationToken.None);
            outcomes.Add($"{seed}:{result.Evidence.Outcome}:functions={result.Evidence.Work.FunctionEvaluations}");
            Assert.InRange(result.Evidence.Work.FunctionEvaluations, 1, 100);
            if (result.Topology is null) { Assert.NotEqual("AllocatedPowersAndOriginalTargetsClosed", result.Evidence.Outcome); continue; }
            found = true; Assert.True(result.Evidence.AllocatedPowersPreserved); Assert.NotEmpty(result.Evidence.AllocatedElementIds);
            Assert.All(result.Topology.Elements, e => Assert.False(e.Protected));
            var optic = TopologyCompiler.Compile(result.Topology, plan.Constraints).CreateOptic();
            Assert.InRange(Math.Abs(optic.Paraxial.EstimateEffectiveFocalLength() / plan.Targets.EffectiveFocalLengthMillimeters - 1), 0, plan.Targets.FocalLengthRelativeTolerance);
            Assert.InRange(Math.Abs(optic.Paraxial.EstimateFNumber() / plan.Targets.FNumber - 1), 0, plan.Targets.FNumberRelativeTolerance);
        }
        Assert.True(found, "At least one bounded fixture must exercise actual successful power allocation and target closure. " + string.Join("; ", outcomes));
    }

    [Fact]
    public void ParetoRetainsCountQualityTradeoffsAndDoesNotMutateQualifiedArchive()
    {
        var (plan, _) = Specification(); var source = TopologyCompiler.Compile(plan.Seeds[0], plan.Constraints);
        var assessment = TopologyValidator.Validate(source, plan.Targets);
        QualifiedEvolutionDesign Q(string id, QualifiedStructureComplexity complexity, double rms, double maximum) =>
            new(id, null, source.Topology, assessment, complexity, rms, maximum);
        var archive = ImmutableArray.Create(Q("parent", new(3, 0, 0), .04, .1), Q("small", new(2, 0, 0), .05, .11),
            Q("accurate", new(4, 1, 1), .03, .09), Q("dominated", new(3, 0, 0), .06, .13), Q("extra-terms", new(3, 0, 1), .04, .1));
        var frontier = StructureEvolutionSearch.Pareto(archive);
        Assert.Equal(new[] { "parent", "small", "accurate" }, frontier); Assert.Equal(5, archive.Length);
    }

    [Fact]
    public void OriginalPermissionsProtectedElementsAndTinyBudgetsRemainBinding()
    {
        var (plan, options) = Specification();
        plan = plan with
        {
            Options = plan.Options with
            {
                Expansion = plan.Options.Expansion! with
                { EnableMaterial = false, EnableProfile = false, EnableInsertion = false, EnableSplit = false, EnableMerge = false }
            }
        };
        var result = ParaxialSeedSearch.Run(plan, options); var e = result.Evolution!;
        Assert.All(e.Trials.Where(t => t.Operation is "insert" or "split" or "merge" or "material" or "profile"), t =>
            { Assert.StartsWith("EditRejected:", t.Outcome); Assert.Contains("permissions", t.Detail); Assert.Equal(0, t.Work.FunctionEvaluations); });
        var tiny = ParaxialSeedSearch.Run(plan, options with { MaximumTracedRays = 10 });
        Assert.Empty(tiny.Evolution!.Parents); Assert.Empty(tiny.Evolution.QualifiedArchive); Assert.Equal(0, tiny.Work.TracedRays);
        Assert.Equal(ParaxialSeedTermination.BudgetExhausted, tiny.Termination);
    }

    [Fact]
    public void CancellationAfterBranchRetainsParentAndSettlesAllWork()
    {
        var (plan, options) = Specification(); using var stop = new CancellationTokenSource();
        var result = ParaxialSeedSearch.Run(plan, options, stop.Token, (branches, _) => { if (branches == 1) stop.Cancel(); });
        Assert.Equal(ParaxialSeedTermination.Cancelled, result.Termination); Assert.Single(result.Evolution!.Trials);
        Assert.Contains(result.Evolution.QualifiedArchive, q => q.ParentId is null);
        Assert.Equal(result.Work.TracedRays, result.Evolution.Parents.Sum(p => p.Work.TracedRays) + result.Evolution.Trials.Sum(t => t.Work.TracedRays));
        Assert.Equal(result.Work.FunctionEvaluations, result.Evolution.Parents.Sum(p => p.Work.FunctionEvaluations) + result.Evolution.Trials.Sum(t => t.Work.FunctionEvaluations));
    }

    [Fact]
    public void NullOptionsPreserveLegacySerializationAndUnknownVersionsAreRejected()
    {
        Assert.DoesNotContain("Evolution", JsonSerializer.Serialize(new ParaxialSeedSearchOptions()));
        var (_, options) = Specification();
        foreach (var p in new[] { options.Evolution! with { Version = 2 }, options.Evolution! with { MaximumBranches = 0 },
            options.Evolution! with { Operations = [TopologyOperationKind.Delete, TopologyOperationKind.Delete] },
            options.Evolution! with { PowerAllocation = new() { Version = 2 } } })
            Assert.Throws<ArgumentException>(() => ParaxialSeedSearch.ValidateOptions(options with { Evolution = p }));
        Assert.Throws<ArgumentException>(() => ParaxialSeedSearch.ValidateOptions(options with { Evolution = null, MaximumTracedRays = 2000000 }));
    }

    [Fact]
    public void ProtectedLensAndItsOriginalAperturesAreNeverEditedOrReleased()
    {
        var (plan, options) = Specification(); var topology = plan.Seeds[0];
        var protectedLens = topology.Elements[^1] with { Protected = true };
        plan = plan with { Seeds = [topology with { Elements = topology.Elements.SetItem(topology.Elements.Length - 1, protectedLens) }] };
        var result = ParaxialSeedSearch.Run(plan, options);
        foreach (var t in result.Evolution!.Trials.Where(t => t.Endpoint is not null))
        {
            Assert.Equal(protectedLens, t.Endpoint!.Elements.Single(e => e.Id == protectedLens.Id));
            Assert.DoesNotContain(t.AnchoredVariables, v => v.SurfaceId == protectedLens.FrontSurfaceId || v.SurfaceId == protectedLens.BackSurfaceId);
        }
    }

    [Fact]
    public void FrozenLegacyPreparationEnvelopeStillHasItsOriginalContentHash()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var path = Path.Combine(directory.FullName, "validation/rechecks/2026-10-30/structure-competition-v1/acceptance/competition-3-F8-full10.seed-search.json");
        using var saved = JsonDocument.Parse(File.ReadAllBytes(path));
        var json = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };
        var result = saved.RootElement.GetProperty("result").Deserialize<ParaxialSeedSearchResult>(json)!;
        Assert.Null(result.Evolution); Assert.Null(result.Options.Evolution);
        Assert.Equal(saved.RootElement.GetProperty("sha256").GetString(), ContentFingerprint.Compute(result));
    }

    [Theory]
    [InlineData("focal-length")]
    [InlineData("f-number")]
    [InlineData("geometry:glass")]
    [InlineData("transition-element")]
    public void FailedFirstOrderOrGeometryCannotEscapeAsWorkingSeed(string code)
    {
        var (plan, _) = Specification(); var assessment = TopologyValidator.Validate(TopologyCompiler.Compile(plan.Seeds[0], plan.Constraints), plan.Targets);
        Assert.True(StructureEvolutionSearch.IsWorkingSeedAssessment(assessment));
        Assert.False(StructureEvolutionSearch.IsWorkingSeedAssessment(assessment with { MeetsTargets = false, Violations = [new(code, .1, .01)] }));
        Assert.False(StructureEvolutionSearch.IsWorkingSeedAssessment(assessment with { Completed = false }));
        Assert.True(StructureEvolutionSearch.IsWorkingSeedAssessment(assessment with { MeetsTargets = false, Violations = [new("field:0:rms", .1, .05)] }));
    }
}

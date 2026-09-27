using OptilandWorkbench.Core;
using OptilandWorkbench.InitialStructure.Contracts;
using OptilandWorkbench.InitialStructure.Engine;

namespace OptilandWorkbench.InitialStructure.Tests;

public sealed class DesignFormSearchTests
{
    [Fact]
    public void EveryBinaryFormIsVisitedBeforeRepeatingMaterialAndStopCycles()
    {
        var spec = FlatStartRefinementTests.Spec(10_000);
        spec = spec with { Budget = spec.Budget with { InitialSeedCount = 24 } };
        var roots = DesignFormSearch.Roots(spec, new(), ["N-BK7", "N-F2"]);
        Assert.Equal(24, roots.Count);
        for (var cycle = 0; cycle < 3; cycle++)
            Assert.Equal(8, roots.Skip(cycle * 8).Take(8).Select(root => root.BinaryStart!.Signs).Distinct().Count());
        Assert.Equal("+++", roots[0].BinaryStart!.Signs);
        Assert.Contains(roots, root => root.BinaryStart!.Signs == "---");
        Assert.Contains(roots.Skip(8), root => root.GlassNames.Contains("N-F2"));
        Assert.Equal(ContentFingerprint.Compute(roots), ContentFingerprint.Compute(DesignFormSearch.Roots(spec, new(), ["N-BK7", "N-F2"])));
        Assert.NotEqual(ContentFingerprint.Compute(roots), ContentFingerprint.Compute(DesignFormSearch.Roots(
            spec with { Budget = spec.Budget with { RandomSeed = 102 } }, new(), ["N-BK7", "N-F2"])));
    }

    [Fact]
    public void FilterAndExplicitStartsDefineTheActualRootPrescription()
    {
        var spec = FlatStartRefinementTests.Spec(120) with { Budget = FlatStartRefinementTests.Spec(120).Budget with { InitialSeedCount = 2 } };
        var options = new FlatStartSearchOptions
        {
            DesignSearch = new()
            {
                SignFilter = "+*-",
                StartingRadiusMillimeters = 400,
                StartingCenterThicknessMillimeters = 3,
                StartingAirGapMillimeters = 2,
                StopSurfaceIndex = 2,
                ExploreStopPositions = false
            }
        };
        var plan = DesignFormSearch.Preview(spec, options);
        Assert.Equal(2, plan.DistinctSignForms);
        var roots = DesignFormSearch.Roots(spec, options, ["N-BK7"]);
        Assert.Equal(new[] { "+--", "++-" }, roots.Select(root => root.BinaryStart!.Signs).Order().ToArray());
        foreach (var family in roots)
        {
            var bootstrap = new FlatStartBootstrap().Solve(spec, 3, family: family);
            Assert.Equal(FlatStartBootstrapState.Initialized, bootstrap.State);
            Assert.Equal(2, bootstrap.EvaluationCount);
            Assert.All(bootstrap.Steps[0].Optic.Surfaces, surface => Assert.Equal(0, surface.Radius));
            var optic = Optic.FromSnapshot(bootstrap.Steps[1].Optic);
            for (var element = 0; element < 3; element++)
            {
                var sign = family.BinaryStart!.Signs[element] == '+' ? 1 : -1;
                Assert.Equal(sign * 400, optic.SurfaceGroup.Items[2 * element + 1].Radius);
                Assert.Equal(-sign * 400, optic.SurfaceGroup.Items[2 * element + 2].Radius);
                Assert.Equal(3, optic.SurfaceGroup.Items[2 * element + 1].Thickness);
            }
            Assert.True(optic.SurfaceGroup.Items[2].IsStop);
            Assert.Equal(optic.Paraxial.EstimateOpticalPower(), bootstrap.Steps[1].Evaluation.OpticalPowerPerMillimeter);
        }
    }

    [Fact]
    public void PreviewReportsPartialCoverageAndFullModeHasARealDifferentBudget()
    {
        var spec = FlatStartRefinementTests.Spec(10000) with { Budget = FlatStartRefinementTests.Spec(10000).Budget with { InitialSeedCount = 3 } };
        var quick = DesignFormSearch.Preview(spec, new());
        var full = DesignFormSearch.Preview(spec, new() { DesignSearch = new() { Mode = DesignSearchMode.Full } });
        Assert.Equal(8, quick.DistinctSignForms);
        Assert.Equal(3, quick.PlannedSignForms);
        Assert.True(full.ScreeningEvaluationsPerRoot > quick.ScreeningEvaluationsPerRoot);
        var varied = spec with { MinimumElementCount = 1, MaximumElementCount = 3 };
        Assert.Equal(new[] { 1, 2, 3 }, DesignFormSearch.Roots(varied, new(), ["N-BK7"]).Select(root => root.ElementCount));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task UninitializedBudgetLimitedRootsNeverCountAsCompletedForms(int budget)
    {
        var result = await new FlatStartSearchService().RunAsync(FlatStartRefinementTests.Spec(budget));
        Assert.Equal(0, DesignFormSearch.CompletedSignForms(result.Checkpoint));
        Assert.Contains(result.Checkpoint.Diagnostics, item => item.Code == "search.binary-coverage");
    }

    [Theory]
    [InlineData("++")]
    [InlineData("+?+")]
    public void InvalidFormFiltersAreRejectedBeforeWork(string filter) => Assert.Throws<ArgumentException>(() =>
        DesignFormSearch.Preview(FlatStartRefinementTests.Spec(), new() { DesignSearch = new() { SignFilter = filter } }));

    [Fact]
    public void ImprovingChildrenCannotResetBranchPromotionAndStarveOtherForms()
    {
        var spec = FlatStartRefinementTests.Spec();
        var optic = new FlatRootFactory().Create(spec, 3);
        FamilyTrial Trial(string id, string? parent, string operation, string signs, double rms) => new()
        {
            TrialId = id,
            ParentTrialId = parent,
            Operation = operation,
            Family = new() { BinaryStart = new(signs, 500) },
            Candidate = new()
            {
                Optic = optic,
                OpticFingerprint = id,
                Status = CandidateStatus.TraceValid,
                Lineage = new() { InitialForm = signs, ElementCount = 3 },
                Evaluation = new() { EffectiveFocalLengthMillimeters = 50, ValidRayFraction = 1, RmsSpotRadiusMillimeters = rms }
            }
        };
        var a = Trial("a", null, "flat-root", "+++", .1);
        var b = Trial("b", null, "flat-root", "+-+", .2);
        var checkpoint = new FlatStartSearchCheckpoint { Specification = spec, Trials = [a, b], UsableGlassNames = ["N-BK7"] };
        Assert.Equal("a", DesignFormScheduler.Decide(checkpoint).Parent!.TrialId);
        var improved = Trial("a1", "a", "refine", "+++", .05);
        checkpoint = checkpoint with { Trials = [a, b, improved] };
        Assert.Equal("b", DesignFormScheduler.Decide(checkpoint).Parent!.TrialId);
        checkpoint = checkpoint with { Trials = [a, b, improved, Trial("b1", "b", "refine", "+-+", .15)] };
        var next = DesignFormScheduler.Decide(checkpoint);
        Assert.Equal("a1", next.Parent!.TrialId);
        Assert.Equal(4, next.Operation); // perturbation, not a second automatic promotion
    }
}

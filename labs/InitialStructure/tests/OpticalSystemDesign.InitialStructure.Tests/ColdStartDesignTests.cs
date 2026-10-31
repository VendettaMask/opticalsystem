using System.Collections.Immutable;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed class ColdStartDesignTests
{
    [Fact]
    public void ColdEnvelopeDiscardsEverySourceShapeButPreservesPhysicalTask()
    {
        var (plan, _) = StructureEvolutionTests.Specification(); var before = ContentFingerprint.Compute(plan);
        var cold = ColdStartDesignSearch.ColdPlan(plan);
        Assert.Equal(before, ContentFingerprint.Compute(plan));
        Assert.All(cold.Seeds[0].Elements.SelectMany(e => new[] { e.Front, e.Back }), face =>
        {
            Assert.Equal(0, face.Profile.CurvaturePerMillimeter); Assert.Equal(0, face.Profile.Conic); Assert.Empty(face.Profile.Terms);
        });
        Assert.Equal(plan.Seeds[0].Reference, cold.Seeds[0].Reference); Assert.Equal(plan.Seeds[0].Stop, cold.Seeds[0].Stop);
        Assert.Equal(plan.Seeds[0].Image, cold.Seeds[0].Image); Assert.Equal(plan.Targets, cold.Targets); Assert.Equal(plan.Constraints, cold.Constraints);
        Assert.Equal(plan.Seeds[0].Elements.Select(e => e.Front.ClearSemiDiameterMillimeters), cold.Seeds[0].Elements.Select(e => e.Front.ClearSemiDiameterMillimeters));
    }

    [Fact]
    public void OriginalStopConflictReturnsWithoutConstructionOrOpticalWork()
    {
        var (plan, options) = StructureEvolutionTests.Specification();
        var seed = plan.Seeds[0];
        plan = plan with { Seeds = [seed with { Stop = seed.Stop with { ZMillimeters = seed.Reference.ZMillimeters, ClearSemiDiameterMillimeters = 100 } }] };
        options = options with { Generation = options.Generation! with { StopAfterElement = 0 } };
        var result = ColdStartDesignSearch.Run(plan, options, new());
        Assert.False(result.Qualified); Assert.Equal("OriginalExternalStopApertureConflict", result.StopReason);
        Assert.Empty(result.Construction); Assert.Equal(0, result.Work.FunctionEvaluations); Assert.Equal(0, result.Work.TracedRays);
    }

    [Fact]
    public void ColdPolicyRejectsProtectedFixedReductionOnlyAndExpandedApertureInputs()
    {
        var (plan, options) = StructureEvolutionTests.Specification();
        options = options with { Generation = options.Generation! with { StopAfterElement = 0 } };
        Assert.Throws<ArgumentException>(() => ColdStartDesignSearch.Validate(plan with { Options = plan.Options with { FixedTopologyOnly = true } }, options, new()));
        Assert.Throws<ArgumentException>(() => ColdStartDesignSearch.Validate(plan with { Options = plan.Options with { QualifiedReductionOnly = true } }, options, new()));
        Assert.Throws<ArgumentException>(() => ColdStartDesignSearch.Validate(plan, options, new() { BackFocusSlackFractions = [1] }));
        Assert.Throws<ArgumentException>(() => ColdStartDesignSearch.Validate(plan, options with { Generation = options.Generation with { ClearSemiDiameterMillimeters = 100 } }, new()));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        var result = ColdStartDesignSearch.Run(plan, options, new(), cancelled.Token);
        Assert.Equal("Cancelled", result.StopReason); Assert.Equal(0, result.Work.TracedRays);
    }
}

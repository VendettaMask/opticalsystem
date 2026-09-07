using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.InitialStructure.Engine;

namespace OptilandWorkbench.InitialStructure.Tests;

public sealed class FlatStartApertureRecoveryTests
{
    [Fact]
    public void FeasibilityUsesTheSpecifiedPhysicalThroughputWithoutAHiddenHundredPercentGate()
    {
        var spec = FlatStartDesignTests.LoadSpecification("03-50mm-monochrome.json");
        var source = new FlatStartDesignService().Solve(spec, 3);
        var problem = new FlatStartDesignProblem(spec, 3, source.Candidate!.Optic);
        var optic = problem.CreateOptic(problem.Vector(source.Candidate.Optic), FlatStartDesignProblem.FullStage);
        var image = optic.SurfaceGroup.Items[^1];
        var intercepts = FlatStartDesignProblem.Pupils(true).Select(pupil =>
        {
            var ray = optic.SequentialRayTracer.TraceGenericFinalSample(0, 1, pupil.X, pupil.Y, .5876)!;
            var hit = image.CoordinateSystem.ToLocalPoint(ray.Position);
            return Math.Sqrt(hit.X * hit.X + hit.Y * hit.Y);
        }).OrderDescending().ToArray();
        image.PhysicalAperture = new CircularAperture((intercepts[0] + intercepts[1]) / 2);
        var allowed = problem.Evaluate(optic, FlatStartDesignProblem.FullStage, true, CancellationToken.None);
        Assert.Equal(96, allowed.Fields[^1].ValidRays);
        Assert.True(allowed.IsFeasible);
        Assert.True(allowed.MeetsTargets);
        var strictSpec = spec with { FlatStart = spec.FlatStart! with { MinimumValidRayFraction = 1 } };
        var strict = new FlatStartDesignProblem(strictSpec, 3, source.Candidate.Optic)
            .Evaluate(optic, FlatStartDesignProblem.FullStage, true, CancellationToken.None);
        Assert.False(strict.IsFeasible);
        Assert.False(strict.MeetsTargets);
        Assert.Equal(allowed.Fields[^1].ValidRays, strict.Fields[^1].ValidRays);
        Assert.Equal(allowed.Fields[^1].RmsRadiusMillimeters, strict.Fields[^1].RmsRadiusMillimeters);
    }

    [Fact]
    public void ClippedCandidateHasVaryingSearchResidualsButStillFailsPhysicalAcceptance()
    {
        var spec = FlatStartDesignTests.LoadSpecification("03-50mm-monochrome.json");
        var source = new FlatStartDesignService().Solve(spec, 3);
        var problem = new FlatStartDesignProblem(spec, 3, source.Candidate!.Optic);
        var vector = problem.Vector(source.Candidate.Optic);
        var optic = problem.CreateOptic(vector, FlatStartDesignProblem.FullStage);
        optic.SurfaceGroup.Items[1].PhysicalAperture = new CircularAperture(.1);
        var evaluation = problem.Evaluate(optic, FlatStartDesignProblem.FullStage, true, CancellationToken.None);
        Assert.False(evaluation.MeetsTargets);
        Assert.False(evaluation.IsFeasible);
        Assert.True(evaluation.HasContinuousSearchResiduals);
        Assert.Equal(2 * 6 * 97, problem.TracedRayCount);
        var direct = SpotMetricEvaluator.EvaluatePupilSamples(optic, 0, 1,
            FlatStartDesignProblem.Pupils(true).ToArray(), includeSurfaceTransmission: false);
        Assert.Equal(direct.RayCount - direct.VignettedRayCount, evaluation.Fields[^1].ValidRays);
        Assert.Equal(direct.Metrics?.RmsSpotRadius, evaluation.Fields[^1].RmsRadiusMillimeters);
        vector[0] += 1e-5;
        var changed = problem.CreateOptic(vector, FlatStartDesignProblem.FullStage);
        changed.SurfaceGroup.Items[1].PhysicalAperture = new CircularAperture(.1);
        var next = problem.Evaluate(changed, FlatStartDesignProblem.FullStage, true, CancellationToken.None);
        Assert.True(next.HasContinuousSearchResiduals);
        Assert.Equal(evaluation.Residuals.Count, next.Residuals.Count);
        Assert.Contains(evaluation.Residuals.Zip(next.Residuals), pair => Math.Abs(pair.First - pair.Second) > 1e-10);
        Assert.False(next.MeetsTargets);
    }
}

using System.Text.Json;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.InitialStructure.Contracts;
using OptilandWorkbench.InitialStructure.Engine;
using OptilandWorkbench.InitialStructure.Persistence;

namespace OptilandWorkbench.InitialStructure.Tests;

public sealed class AutomaticLensDiameterTests
{
    [Fact]
    public void HistoricalSpecificationsKeepTheirFixedDiameterMeaningAndFingerprint()
    {
        var settings = JsonSerializer.Deserialize<FlatStartSettings>("{}")!;
        Assert.False(settings.AutomaticLensDiameters);
        Assert.DoesNotContain("AutomaticLensDiameters", JsonSerializer.Serialize(settings));
        var changed = settings with { AutomaticLensDiameters = true };
        Assert.True(JsonSerializer.Deserialize<FlatStartSettings>(JsonSerializer.Serialize(changed))!.AutomaticLensDiameters);
        Assert.NotEqual(ContentFingerprint.Compute(settings), ContentFingerprint.Compute(changed));
    }

    [Fact]
    public void EveryLensTracksItsBeamAndCanShrinkWithoutChangingSystemAperture()
    {
        var spec = Specification();
        var template = new FlatStartProblem(spec, 3, .3).Root;
        var problem = new FlatStartDesignProblem(spec, 3, template);
        var optic = problem.CreateOptic(problem.Vector(template), FlatStartDesignProblem.FullStage);
        var full = problem.Evaluate(optic, FlatStartDesignProblem.FullStage, true, default);
        var sizes = Enumerable.Range(0, 3).Select(n => optic.SurfaceGroup.Items[2 * n + 1].SemiDiameter).ToArray();
        Assert.Equal(3, sizes.Distinct().Count());
        Assert.Equal(spec.EffectiveFocalLengthMillimeters / spec.FNumber, optic.Aperture.Value);
        Assert.Equal(ContentFingerprint.Compute(optic.ToSnapshot()), ContentFingerprint.Compute(full.EvaluatedOptic!));
        foreach (var n in Enumerable.Range(0, 3))
        {
            var front = optic.SurfaceGroup.Items[2 * n + 1];
            var back = optic.SurfaceGroup.Items[2 * n + 2];
            Assert.Equal(front.SemiDiameter, back.SemiDiameter);
            Assert.Equal(front.SemiDiameter, front.MechanicalSemiDiameter);
            Assert.Equal(front.SemiDiameter, Assert.IsType<CircularAperture>(front.PhysicalAperture).Radius);
        }
        optic.Aperture.Value *= .5;
        problem.Evaluate(optic, new(.5, 0, true), true, default);
        Assert.All(Enumerable.Range(0, 3), n => Assert.True(optic.SurfaceGroup.Items[2 * n + 1].SemiDiameter < sizes[n]));
        // Sizing rays, as well as physical validation rays, are charged to this evaluation's ray count.
        Assert.True(problem.TracedRayCount >= 2 * 6 * 97);
    }

    [Fact]
    public void GeometryIsRecheckedAtTheNewLensDiameter()
    {
        var spec = Specification() with { SemiDiameterMarginFactor = 2, FlatStart = new() { AutomaticLensDiameters = true, MinimumEdgeThicknessMillimeters = 1.9 } };
        var template = new FlatStartProblem(spec, 3, .3).Root;
        var problem = new FlatStartDesignProblem(spec, 3, template);
        var vector = problem.Vector(template);
        vector[0] = .5;
        vector[1] = -.5;
        var optic = problem.CreateOptic(vector, FlatStartDesignProblem.FullStage);
        var result = problem.Evaluate(optic, FlatStartDesignProblem.FullStage, true, default);
        Assert.False(result.MeetsTargets);
        Assert.Contains(result.Violations, violation => violation.Code == "geometry.edge-thickness");
        Assert.False(result.GeometryFeasible);
    }

    [Fact]
    public async Task SearchRecordsExportsAndRecalculatesTheActualSizedPrescription()
    {
        var spec = Specification() with { Budget = new() { MaximumEvaluations = 100, InitialSeedCount = 1, MaximumParallelism = 1 } };
        var family = new FlatStartFamily
        {
            GlassNames = ["N-BK7", "N-BK7", "N-BK7"],
            CenterThicknesses = [2, 2, 2],
            AirGaps = [1, 1],
            StopSurfaceIndex = 3,
            BinaryStart = new("+-+", 500)
        };
        var result = new FlatStartDesignService().Solve(spec, 3, family: family);
        var candidate = Assert.IsType<CandidateSnapshot>(result.Candidate);
        Assert.Equal(ContentFingerprint.Compute(result.FinalValidation!.EvaluatedOptic!), candidate.OpticFingerprint);
        Assert.All(result.Steps, step => Assert.Equal(ContentFingerprint.Compute(step.Optic), ContentFingerprint.Compute(step.Evaluation.EvaluatedOptic!)));
        Assert.All(result.Bootstrap.Steps, step => Assert.Equal(ContentFingerprint.Compute(step.Optic), ContentFingerprint.Compute(step.Evaluation.EvaluatedOptic!)));
        var optic = Optic.FromSnapshot(candidate.Optic);
        var before = ContentFingerprint.Compute(optic.ToSnapshot());
        var evaluation = new FlatStartDesignProblem(spec, 3, candidate.Optic, family).Evaluate(optic, FlatStartDesignProblem.FullStage, true, default);
        Assert.Equal(before, ContentFingerprint.Compute(optic.ToSnapshot()));
        Assert.Equal(ContentFingerprint.Compute(result.FinalValidation), ContentFingerprint.Compute(evaluation));
        foreach (var field in evaluation.Fields.Where(field => field.RmsRadiusMillimeters.HasValue))
        {
            var core = SpotMetricEvaluator.EvaluatePupilSamples(optic, 0, field.NormalizedFieldY,
                FlatStartDesignProblem.Pupils(true).ToArray(), includeSurfaceTransmission: false);
            Assert.Equal(core.Metrics!.RmsSpotRadius, field.RmsRadiusMillimeters);
        }
        var path = Path.Combine(Path.GetTempPath(), $"automatic-diameter-{Guid.NewGuid():N}.staropt");
        try
        {
            await new CandidateExportService().ExportStarOptAsync(candidate, path);
            var restored = await StarOptProjectStore.LoadAsync(path);
            Assert.Equal(candidate.OpticFingerprint, ContentFingerprint.Compute(restored.Configurations[0].ToSnapshot()));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static InitialStructureSpecification Specification() => new()
    {
        Name = "Automatic lens diameter mechanism",
        EffectiveFocalLengthMillimeters = 50,
        FNumber = 8,
        MaximumFieldAngleDegrees = 7,
        MinimumElementCount = 3,
        MaximumElementCount = 3,
        Wavelengths = [new() { Nanometers = 587.6, Weight = 1, IsPrimary = true }],
        FlatStart = new() { AutomaticLensDiameters = true }
    };
}

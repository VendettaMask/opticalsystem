using System.Text.Json;
using System.Text.Json.Serialization;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.InitialStructure.Contracts;
using OptilandWorkbench.InitialStructure.Engine;
using OptilandWorkbench.InitialStructure.Persistence;
using Xunit.Abstractions;

namespace OptilandWorkbench.InitialStructure.Tests;

public sealed class FlatStartDesignTests(ITestOutputHelper output)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    { WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };

    [Theory]
    [InlineData("03-50mm-monochrome.json")]
    [InlineData("04-75mm-visible.json")]
    public async Task FrozenFullTargetCasesAreValidatedPerFieldAndExportThroughFormalStore(string name)
    {
        var specification = LoadSpecification(name);
        var result = new FlatStartDesignService().Solve(specification, specification.MinimumElementCount);
        output.WriteLine($"{name}: {result.State}; {result.EvaluationCount} evaluations, {result.TracedRayCount} rays");
        foreach (var field in result.FinalValidation?.Fields ?? [])
            output.WriteLine($"field {field.NormalizedFieldY}: RMS {field.RmsRadiusMillimeters:R}, max {field.MaximumRadiusMillimeters:R}, rays {field.ValidRays}/{field.AttemptedRays}");
        SaveEvidence(name, result);
        Assert.Equal(FlatStartDesignState.Accepted, result.State);
        Assert.Equal(CandidateStatus.LabAccepted, result.Candidate!.Status);
        Assert.All(result.Bootstrap.Steps[0].Optic.Surfaces, surface => Assert.Equal(0, surface.Radius));
        Assert.Equal(new[] { 0, .25, .5, .7, .85, 1 }, result.FinalValidation!.Fields.Select(field => field.NormalizedFieldY));
        Assert.All(result.FinalValidation.Fields, field =>
        {
            Assert.Equal(97 * specification.Wavelengths.Count(wave => wave.Weight > 0), field.AttemptedRays);
            Assert.True(field.RmsRadiusMillimeters <= specification.MaximumRmsSpotRadiusMillimeters);
            Assert.True(field.MaximumRadiusMillimeters <= specification.MaximumSpotRadiusMillimeters);
        });
        Assert.Equal("full-target-dense-validation", result.Steps[^1].Operation);
        Assert.Equal(FlatStartDesignProblem.FullStage, result.Steps[^1].Stage);
        var path = Path.Combine(Path.GetTempPath(), "flat-design-" + Guid.NewGuid().ToString("N") + ".staropt");
        try
        {
            await new CandidateExportService().ExportStarOptAsync(result.Candidate, path);
            var loaded = await StarOptProjectStore.LoadAsync(path);
            var optic = loaded.Configurations[loaded.ActiveConfigurationIndex];
            Assert.Equal(ContentFingerprint.Compute(result.Candidate.Optic), ContentFingerprint.Compute(optic.ToSnapshot()));
            foreach (var field in result.FinalValidation.Fields)
            {
                var direct = SpotMetricEvaluator.EvaluatePupilSamples(optic, 0, field.NormalizedFieldY,
                    FlatStartDesignProblem.Pupils(true).ToArray(), includeSurfaceTransmission: false);
                Assert.Equal(field.RmsRadiusMillimeters!.Value, direct.Metrics!.RmsSpotRadius, 12);
                Assert.Equal(field.MaximumRadiusMillimeters!.Value, direct.Metrics.MaximumSpotRadius, 12);
                Assert.Equal(field.AttemptedRays - field.ValidRays, direct.VignettedRayCount);
            }
        }
        finally { File.Delete(path); }
        var restored = JsonSerializer.Deserialize<FlatStartDesignResult>(JsonSerializer.Serialize(result, JsonOptions), JsonOptions)!;
        Assert.Equal(ContentFingerprint.Compute(result), ContentFingerprint.Compute(restored));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(20)]
    [InlineData(100)]
    public void BudgetIncludesBootstrapProbesAndFinalValidation(int limit)
    {
        var specification = LoadSpecification("03-50mm-monochrome.json");
        specification = specification with { Budget = specification.Budget with { MaximumEvaluations = limit } };
        var result = new FlatStartDesignService().Solve(specification, 3);
        Assert.InRange(result.EvaluationCount, 1, limit);
        Assert.All(result.Steps, step => Assert.InRange(step.EvaluationNumber, result.Bootstrap.EvaluationCount + 1, limit));
        if (limit == 1)
        {
            Assert.Null(result.FinalValidation);
            Assert.Null(result.Candidate);
            Assert.Equal(FlatStartDesignState.BudgetExhausted, result.State);
        }
        else Assert.NotNull(result.FinalValidation);
    }

    [Theory]
    [InlineData("01-24mm-monochrome.json")]
    [InlineData("02-35mm-fixed-back.json")]
    [InlineData("05-100mm-fixed-back.json")]
    [InlineData("06-150mm-visible.json")]
    [InlineData("07-24mm-visible.json")]
    [InlineData("08-35mm-visible.json")]
    [InlineData("09-50mm-visible.json")]
    [InlineData("10-85mm-visible.json")]
    [InlineData("11-100mm-visible.json")]
    [InlineData("12-50mm-six-elements.json")]
    public void RemainingFrozenFamiliesReportSuccessOrExplicitFullTargetGaps(string name)
    {
        var specification = LoadSpecification(name);
        var result = new FlatStartDesignService().Solve(specification, specification.MinimumElementCount);
        SaveEvidence(name, result);
        output.WriteLine($"{name}: {result.State}, evaluations {result.EvaluationCount}, rays {result.TracedRayCount}");
        Assert.InRange(result.EvaluationCount, 1, specification.Budget.MaximumEvaluations);
        Assert.Equal(ContentFingerprint.Compute(specification), result.SpecificationFingerprint);
        if (result.FinalValidation is null)
        {
            Assert.Null(result.Candidate);
            Assert.Contains(result.State, new[] { FlatStartDesignState.TimeLimit, FlatStartDesignState.BudgetExhausted });
            return;
        }
        Assert.Equal(result.FinalValidation.MeetsTargets, result.Candidate!.Status == CandidateStatus.LabAccepted);
        Assert.Equal(6, result.FinalValidation.Fields.Count);
        Assert.Equal(FlatStartDesignProblem.FullStage, result.Steps[^1].Stage);
        Assert.All(result.Steps, step =>
        {
            Assert.Equal(specification.FlatStart!.FixedBackFocusMillimeters ?? step.Optic.Surfaces[^2].Thickness,
                step.Optic.Surfaces[^2].Thickness);
            Assert.All(step.Optic.Surfaces.Zip(result.Bootstrap.Steps[0].Optic.Surfaces), pair =>
                Assert.Equal(pair.Second.SemiDiameter, pair.First.SemiDiameter));
        });
        if (!result.FinalValidation.MeetsTargets) Assert.NotEmpty(result.FinalValidation.Violations);
    }

    [Fact]
    public void ThicknessAirAndAutomaticBackFocusAreBoundedVariables()
    {
        var specification = LoadSpecification("03-50mm-monochrome.json");
        var bootstrap = new FlatStartBootstrap().Solve(specification, 3);
        var problem = new FlatStartDesignProblem(specification, 3, bootstrap.Steps[^1].Optic);
        var vector = problem.Vector(bootstrap.Steps[^1].Optic);
        Assert.Equal(12, problem.Dimension);
        vector[6] += .02;
        vector[7] += .03;
        var optic = problem.CreateOptic(vector, FlatStartDesignProblem.FullStage);
        Assert.Equal(3, optic.SurfaceGroup.Items[1].Thickness, 10);
        Assert.Equal(2.5, optic.SurfaceGroup.Items[2].Thickness, 10);
        Array.Fill(vector, 1000, 6, 6);
        optic = problem.CreateOptic(vector, FlatStartDesignProblem.FullStage);
        Assert.Equal(specification.MaximumTrackLengthMillimeters, optic.SurfaceGroup.TotalTrack, 9);
        Assert.All(optic.SurfaceGroup.Items.Skip(1).Take(6), surface => Assert.True(surface.Thickness >= 1));
    }

    [Fact]
    public void FixedBackFocusIsExcludedFromDesignVariables()
    {
        var specification = LoadSpecification("02-35mm-fixed-back.json");
        var root = new FlatStartProblem(specification, 3, .3).Root;
        var problem = new FlatStartDesignProblem(specification, 3, root);
        Assert.Equal(11, problem.Dimension);
        var vector = problem.Vector(root);
        Array.Fill(vector, 100, 6, 5);
        var optic = problem.CreateOptic(vector, FlatStartDesignProblem.FullStage);
        Assert.Equal(25, optic.SurfaceGroup.Items[6].Thickness);
        Assert.False(optic.SurfaceGroup.Items[6].ThicknessVariable);
        Assert.InRange(optic.SurfaceGroup.TotalTrack, 0, specification.MaximumTrackLengthMillimeters + 1e-9);
    }

    [Fact]
    public void EdgeFieldFailureCannotBeHiddenByGoodAxialSpot()
    {
        var specification = LoadSpecification("03-50mm-monochrome.json");
        var bootstrap = new FlatStartBootstrap().Solve(specification, 3);
        specification = specification with { MaximumFieldAngleDegrees = 30 };
        var wideRoot = Optic.FromSnapshot(bootstrap.Steps[^1].Optic);
        wideRoot.Fields[^1].YAngleDegrees = 30;
        var problem = new FlatStartDesignProblem(specification, 3, wideRoot.ToSnapshot());
        var evaluation = problem.Evaluate(problem.CreateOptic(problem.Vector(wideRoot.ToSnapshot()), FlatStartDesignProblem.FullStage),
            FlatStartDesignProblem.FullStage, true, CancellationToken.None);
        Assert.True(evaluation.Fields[0].RmsRadiusMillimeters < specification.MaximumRmsSpotRadiusMillimeters);
        Assert.False(evaluation.MeetsTargets);
        Assert.Contains(evaluation.Violations, item => item.Code.StartsWith("field.5.", StringComparison.Ordinal));
    }

    [Fact]
    public void ImpossibleImageQualityIsReportedWithoutRelaxingTargetsAndReplayIsDeterministic()
    {
        var specification = LoadSpecification("03-50mm-monochrome.json");
        specification = specification with
        {
            MaximumRmsSpotRadiusMillimeters = 1e-7,
            MaximumSpotRadiusMillimeters = 2e-7,
            Budget = specification.Budget with { MaximumEvaluations = 140 }
        };
        var first = new FlatStartDesignService().Solve(specification, 3);
        var second = new FlatStartDesignService().Solve(specification, 3);
        Assert.NotEqual(FlatStartDesignState.Accepted, first.State);
        Assert.NotEqual(CandidateStatus.LabAccepted, first.Candidate!.Status);
        Assert.Contains(first.FinalValidation!.Violations, violation => violation.Code.EndsWith(".rms", StringComparison.Ordinal));
        Assert.Equal(ContentFingerprint.Compute(first), ContentFingerprint.Compute(second));
        Assert.Equal(1e-7, first.Specification.MaximumRmsSpotRadiusMillimeters);
    }

    [Fact]
    public void CancelledDesignDoesNotStartComputation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => new FlatStartDesignService().Solve(
            LoadSpecification("03-50mm-monochrome.json"), 3, cancellation.Token));
    }

    internal static InitialStructureSpecification LoadSpecification(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OptilandWorkbench.slnx"))) directory = directory.Parent;
        return JsonSerializer.Deserialize<InitialStructureSpecification>(File.ReadAllText(Path.Combine(directory!.FullName,
            "labs/InitialStructure/benchmarks/flat-start-v1/specs", name)))!;
    }

    private static void SaveEvidence(string name, FlatStartDesignResult result)
    {
        var directory = Environment.GetEnvironmentVariable("INITIAL_STRUCTURE_DESIGN_EVIDENCE");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name), JsonSerializer.Serialize(result, JsonOptions));
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;
using OptilandWorkbench.Core;
using OptilandWorkbench.InitialStructure.Contracts;
using OptilandWorkbench.InitialStructure.Engine;
using Xunit.Abstractions;

namespace OptilandWorkbench.InitialStructure.Tests;

public sealed class FlatStartBootstrapTests(ITestOutputHelper output)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };

    private static InitialStructureSpecification Specification(int count = 1) => new()
    {
        Name = $"P1 strict flat proof {count} elements",
        EffectiveFocalLengthMillimeters = 50,
        FNumber = 8,
        MaximumFieldAngleDegrees = 0,
        MinimumElementCount = count,
        MaximumElementCount = count,
        Wavelengths = [new() { Label = "d", Nanometers = 587.6, Weight = 1, IsPrimary = true }],
        FlatStart = new(),
        Budget = new() { InitialSeedCount = 1, MaximumEvaluations = 800, MaximumParallelism = 1 }
    };

    [Fact]
    public void FutureFullTargetSpecificationsAndProtocolAreFrozenSeparatelyFromBootstrap()
    {
        var directory = Path.Combine(RepositoryRoot(), "labs/InitialStructure/benchmarks/flat-start-v1");
        var paths = Directory.GetFiles(Path.Combine(directory, "specs"), "*.json");
        Assert.Equal(12, paths.Length);
        foreach (var path in paths)
        {
            var specification = JsonSerializer.Deserialize<InitialStructureSpecification>(File.ReadAllText(path))!;
            SpecificationValidator.Validate(specification);
            Assert.Equal(0.05, specification.MaximumRmsSpotRadiusMillimeters);
            Assert.Equal(0.15, specification.MaximumSpotRadiusMillimeters);
            Assert.Equal(0.02, specification.FlatStart!.EffectiveFocalLengthRelativeTolerance);
            Assert.Equal(0.98, specification.FlatStart.MinimumValidRayFraction);
        }
        using var protocol = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "protocol.json")));
        Assert.Equal(10, protocol.RootElement.GetProperty("RequiredSuccessfulSpecifications").GetInt32());
        Assert.Equal(5, protocol.RootElement.GetProperty("RandomSeeds").GetArrayLength());
    }

    [Fact]
    public void ExactFlatRootHasARealNonzeroPupilAndFiniteRealRayResiduals()
    {
        var problem = new FlatStartProblem(Specification(), 1, .3);
        var optic = problem.CreateOptic(problem.InitialVector());
        Assert.All(problem.Root.Surfaces, surface => Assert.Equal(0, surface.Radius));
        Assert.Equal(1.875, optic.Paraxial.EstimateEntrancePupilDiameter(), 12);
        var evaluation = problem.Evaluate(optic, false, CancellationToken.None);
        Assert.Equal(0, evaluation.OpticalPowerPerMillimeter);
        Assert.All(evaluation.Residuals, value => Assert.True(double.IsFinite(value)));
        Assert.Equal(1, evaluation.ValidRayFraction);
        Assert.Equal(problem.PupilRadius * Math.Sqrt(10.0 / 17), evaluation.RmsInterceptMillimeters, 10);
        Assert.Empty(evaluation.Violations);
    }

    [Fact]
    public void ParaxialPowerMatchesTheIndependentThickLensEquationAndDerivativeAtZero()
    {
        var problem = new FlatStartProblem(Specification(), 1, .3);
        var optic = problem.CreateOptic(problem.InitialVector());
        optic.SurfaceGroup.Items[1].Radius = 40;
        optic.SurfaceGroup.Items[2].Radius = -60;
        var n = optic.Materials.Resolve("N-BK7").RefractiveIndex(587.6);
        var expected = (n - 1) * (1.0 / 40 - 1.0 / -60 + (n - 1) * 2 / (n * 40 * -60));
        Assert.Equal(expected, problem.Evaluate(optic, false, CancellationToken.None).OpticalPowerPerMillimeter, 12);
        foreach (var h in new[] { 1e-4, 1e-5, 1e-6 })
        {
            var plus = problem.InitialVector();
            var minus = problem.InitialVector();
            plus[0] = h;
            minus[0] = -h;
            var upper = problem.Evaluate(problem.CreateOptic(plus), false, CancellationToken.None);
            var lower = problem.Evaluate(problem.CreateOptic(minus), false, CancellationToken.None);
            Assert.Equal((n - 1) / 50, (upper.OpticalPowerPerMillimeter - lower.OpticalPowerPerMillimeter) / (2 * h), 10);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void StrictFlatStartFocusesWithoutAnyPrebentSeedAndMatchesIndependentSnellTracing(int count)
    {
        var specification = Specification(count);
        var result = new FlatStartBootstrap().Solve(specification, count);
        var initial = result.Steps[0];
        var final = result.Steps[^1];
        output.WriteLine($"{count} elements: {result.State}, evaluations {result.EvaluationCount}, rays {result.TracedRayCount}, EFL {1 / final.Evaluation.OpticalPowerPerMillimeter:R} mm, RMS {final.Evaluation.RmsInterceptMillimeters:R} mm");
        Assert.Equal(FlatStartBootstrapState.Focused, result.State);
        Assert.Equal("exact-flat-root", initial.Operation);
        Assert.All(initial.Optic.Surfaces, surface => Assert.Equal(0, surface.Radius));
        Assert.Equal("first-curvature-update", result.Steps[1].Operation);
        Assert.True(result.Steps[1].Evaluation.Merit < initial.Evaluation.Merit);
        Assert.Contains(result.Steps[1].Optic.Surfaces, surface => surface.Radius != 0);
        Assert.Equal("independent-dense-startup-validation", final.Operation);
        Assert.InRange(result.EvaluationCount, 2, 800);
        Assert.Equal(1, final.Evaluation.ValidRayFraction);
        Assert.Empty(final.Evaluation.Violations);
        Assert.All(result.Steps, step => Assert.All(step.Evaluation.Residuals, value => Assert.True(double.IsFinite(value))));
        var optic = Optic.FromSnapshot(final.Optic);
        var pupilRadius = specification.EffectiveFocalLengthMillimeters / specification.FNumber * .3 / 2;
        foreach (var normalized in new[] { -.93, -.42, 0, .37, .88, 1 })
        {
            var expected = TraceSphericalMeridional(optic, normalized * pupilRadius, 587.6);
            var actual = optic.TraceGenericFinalSample(0, 0, 0, normalized, .5876);
            Assert.NotNull(actual);
            Assert.False(actual.Vignetted);
            Assert.Equal(expected, actual.Position.Y, 8);
        }
        var repeated = new FlatStartBootstrap().Solve(specification, count);
        Assert.Equal(result.EvaluationCount, repeated.EvaluationCount);
        Assert.Equal(ContentFingerprint.Compute(final.Optic), ContentFingerprint.Compute(repeated.Steps[^1].Optic));
        var roundTrip = JsonSerializer.Deserialize<FlatStartBootstrapResult>(JsonSerializer.Serialize(result, JsonOptions), JsonOptions)!;
        Assert.Equal(ContentFingerprint.Compute(result), ContentFingerprint.Compute(roundTrip));
        var evidence = Environment.GetEnvironmentVariable("INITIAL_STRUCTURE_BOOTSTRAP_EVIDENCE");
        if (!string.IsNullOrWhiteSpace(evidence))
        {
            Directory.CreateDirectory(evidence);
            File.WriteAllText(Path.Combine(evidence, $"p1-{count}-elements.json"), JsonSerializer.Serialize(result, JsonOptions));
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(20)]
    public void EveryProbeAndFinalValidationStaysInsideTheEvaluationBudget(int maximum)
    {
        var specification = Specification() with { Budget = Specification().Budget with { MaximumEvaluations = maximum } };
        var result = new FlatStartBootstrap().Solve(specification, 1);
        Assert.InRange(result.EvaluationCount, 1, maximum);
        if (maximum <= 2) Assert.Equal(FlatStartBootstrapState.BudgetExhausted, result.State);
        Assert.All(result.Steps, step => Assert.InRange(step.EvaluationNumber, 1, maximum));
    }

    [Fact]
    public void FixedBackFocusIsNeverMovedToImproveTheResidual()
    {
        var specification = Specification(2) with { FlatStart = new() { FixedBackFocusMillimeters = 35 } };
        var result = new FlatStartBootstrap().Solve(specification, 2);
        Assert.All(result.Steps, step => Assert.Equal(35, step.Optic.Surfaces[4].Thickness));
        Assert.All(result.Steps, step => Assert.True(double.IsFinite(step.Evaluation.Merit)));
    }

    [Fact]
    public void EdgeThicknessUsesSphericalSagAndNonconstructibleEdgesAreReported()
    {
        var problem = new FlatStartProblem(Specification(), 1, .3);
        var optic = problem.CreateOptic(problem.InitialVector());
        optic.SurfaceGroup.Items[1].Radius = 10;
        optic.SurfaceGroup.Items[2].Radius = -10;
        optic.SurfaceGroup.Items[1].SemiDiameter = 5;
        optic.SurfaceGroup.Items[2].SemiDiameter = 5;
        var edge = Assert.Single(problem.GeometryViolations(optic), item => item.Code == "geometry.edge-thickness");
        Assert.Equal(2 - 2 * (10 - Math.Sqrt(75)), edge.Actual!.Value, 10);
        optic.SurfaceGroup.Items[1].Radius = 4;
        Assert.Null(Assert.Single(problem.GeometryViolations(optic), item => item.Code == "geometry.edge-thickness").Actual);
    }

    [Fact]
    public void ClippingCannotProduceAnApparentlyImprovedValidCandidate()
    {
        var problem = new FlatStartProblem(Specification(), 1, .3);
        var optic = problem.CreateOptic(problem.InitialVector());
        optic.SurfaceGroup.Items[1].SemiDiameter = .1;
        var evaluation = problem.Evaluate(optic, false, CancellationToken.None);
        Assert.True(evaluation.ValidRayFraction < 1);
        Assert.False(problem.IsFocused(evaluation));
    }

    [Fact]
    public async Task MissingGlassAndUsingTheLegacySearchCannotSilentlySubstituteAnotherModel()
    {
        Assert.Throws<KeyNotFoundException>(() => new FlatStartBootstrap().Solve(Specification() with { InitialGlass = "MISSING_FLAT_START_GLASS" }, 1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new InitialStructureSearchService().RunAsync(Specification()));
        Assert.DoesNotContain("flatStart", JsonSerializer.Serialize(new InitialStructureSpecification(), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }

    [Fact]
    public void CancelledStartupDoesNotSpendTheBudget()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => new FlatStartBootstrap().Solve(Specification(), 1, cancellation.Token));
    }

    [Fact]
    public void CatalogWavelengthRangeIsEnforcedWithoutExtrapolation()
    {
        var material = Assert.IsType<OptilandWorkbench.Core.Materials.CatalogGlassMaterial>(new Optic().Materials.Resolve("N-BK7"));
        var specification = Specification() with { Wavelengths = [new() { Nanometers = material.MinimumWavelengthNanometers / 2, IsPrimary = true }] };
        var error = Assert.Throws<InvalidOperationException>(() => new FlatStartBootstrap().Solve(specification, 1));
        Assert.Contains("does not cover", error.Message);
    }

    [Fact]
    public void UnrepresentablePupilAndInconsistentPlateThicknessAreRejected()
    {
        Assert.Throws<InitialStructureSpecificationException>(() => new FlatStartBootstrap().Solve(Specification() with { EffectiveFocalLengthMillimeters = .0001 }, 1));
        Assert.Throws<InitialStructureSpecificationException>(() => new FlatStartBootstrap().Solve(Specification() with { FlatStart = new() { MinimumEdgeThicknessMillimeters = 3 } }, 1));
        Assert.Throws<InvalidOperationException>(() => new FirstOrderSeedGenerator().Create(Specification(), 1, 0));
    }

    [Fact]
    public void DampedVectorSolveHandlesCoupledAndRankDeficientResiduals()
    {
        var step = FlatStartBootstrap.DampedStep(new double[,] { { 1, 1 }, { 1, -1 } }, [-3, -1], 1e-8);
        Assert.Equal(2, step[0], 6);
        Assert.Equal(1, step[1], 6);
        var degenerate = FlatStartBootstrap.DampedStep(new double[,] { { 1, 1 } }, [-2], 1e-3);
        Assert.All(degenerate, value => Assert.True(double.IsFinite(value)));
        Assert.InRange(degenerate.Sum(), 1.99, 2.01);
    }

    // Independent 2D sphere intersection and vector Snell law. No Core geometry/tracing routines.
    private static double TraceSphericalMeridional(Optic optic, double height, double wavelength)
    {
        var z = -10.0;
        var y = height;
        var dz = 1.0;
        var dy = 0.0;
        var vertex = 0.0;
        var before = 1.0;
        foreach (var surface in optic.ToSnapshot().Surfaces.Skip(1))
        {
            var radius = surface.Radius;
            double distance;
            if (radius == 0) distance = (vertex - z) / dz;
            else
            {
                var center = vertex + radius;
                var b = (z - center) * dz + y * dy;
                var c = (z - center) * (z - center) + y * y - radius * radius;
                var root = Math.Sqrt(b * b - c);
                distance = new[] { -b - root, -b + root }.Where(t => t >= -1e-9 && (z + t * dz - center) * radius <= 0).Min();
            }
            z += distance * dz;
            y += distance * dy;
            var nz = radius == 0 ? 1 : (vertex + radius - z) / radius;
            var ny = radius == 0 ? 0 : -y / radius;
            var length = Math.Sqrt(nz * nz + ny * ny);
            nz /= length;
            ny /= length;
            var after = optic.Materials.Resolve(surface.Material).RefractiveIndex(wavelength);
            var cosine = dz * nz + dy * ny;
            var ratio = before / after;
            var transmittedCosine = Math.Sqrt(1 - ratio * ratio * (1 - cosine * cosine));
            dz = ratio * dz + (transmittedCosine - ratio * cosine) * nz;
            dy = ratio * dy + (transmittedCosine - ratio * cosine) * ny;
            before = after;
            vertex += surface.Thickness;
        }
        return y;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}

using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed class ParaxialSeedSearchTests
{
    [Fact]
    public void SharedCoreWholeSystemPowerAndDerivativeMatchNominalPerturbationsWithoutTracing()
    {
        var plan = Plan(); var source = TopologyCompiler.Compile(plan.Seeds[0], plan.Constraints);
        var binding = TopologyParameterBinder.Bind(source, ParaxialSeedSearch.Variables(source.Topology, plan.Options).Take(2));
        using var trace = SequentialTraceMeasurement.Begin();
        var matrix = DifferentiableParaxialMetrics.TransferMatrix(source.CreateOptic(), binding.CoreParameters,
            source.SurfaceIndices[source.Topology.Reference.Id], source.SurfaceIndices[source.Topology.Image.Id], includeInverseFNumber: true);
        Assert.Equal(source.CreateOptic().Paraxial.EstimateOpticalPower(), matrix.OpticalPowerPerMillimeter!.Value.Value);
        for (var column = 0; column < binding.Slots.Length; column++)
        {
            const double step = 1e-5;
            var plus = binding.InitialCoordinates.ToArray(); var minus = plus.ToArray(); plus[column] += step; minus[column] -= step;
            var difference = (TopologyParameterBinder.Apply(source, binding, plus).CreateOptic().Paraxial.EstimateOpticalPower()
                - TopologyParameterBinder.Apply(source, binding, minus).CreateOptic().Paraxial.EstimateOpticalPower()) / (2 * step);
            Assert.InRange(Math.Abs(difference - matrix.OpticalPowerPerMillimeter.Value.Derivative(column)), 0, 1e-9);
        }
        Assert.Equal(0, trace.RayCount);
    }

    [Fact]
    public async Task GeneratedSeedsFitActualCoreFirstOrderTargetsAndPreserveTheOriginalTaskAndPermissions()
    {
        var plan = Plan(); var fingerprint = ContentFingerprint.Compute(plan);
        var options = new ParaxialSeedSearchOptions { MaximumStarts = 3, RetainedSeeds = 3, MaximumFunctionEvaluations = 120, TimeLimit = TimeSpan.FromSeconds(30) };
        var result = ParaxialSeedSearch.Run(plan, options);
        Assert.NotEmpty(result.Seeds);
        Assert.InRange(result.Work.FunctionEvaluations, 1, options.MaximumFunctionEvaluations);
        Assert.Equal(result.Work.FunctionEvaluations, result.Attempts.Sum(attempt => attempt.FunctionEvaluations));
        Assert.All(result.Attempts, attempt => Assert.InRange(attempt.FunctionEvaluations, 0, options.MaximumEvaluationsPerStart));
        Assert.Equal(0, result.Work.TracedRays);
        foreach (var seed in result.Seeds)
        {
            Assert.Equal(plan.Seeds[0].Reference, seed.Reference); Assert.Equal(plan.Seeds[0].Stop, seed.Stop); Assert.Equal(plan.Seeds[0].Image, seed.Image);
            Assert.Equal(plan.Seeds[0].Fields, seed.Fields); Assert.Equal(plan.Seeds[0].Wavelengths, seed.Wavelengths);
            Assert.Equal(plan.Seeds[0].Elements.Select(element => element.Material), seed.Elements.Select(element => element.Material));
            Assert.Equal(plan.Seeds[0].Elements.Select(element => element.FrontZMillimeters), seed.Elements.Select(element => element.FrontZMillimeters));
            var compiled = TopologyCompiler.Compile(seed, plan.Constraints); var optic = compiled.CreateOptic();
            Assert.InRange(Math.Abs(optic.Paraxial.EstimateEffectiveFocalLength() / plan.Targets.EffectiveFocalLengthMillimeters - 1), 0, plan.Targets.FocalLengthRelativeTolerance);
            Assert.InRange(Math.Abs(optic.Paraxial.EstimateFNumber() / plan.Targets.FNumber - 1), 0, plan.Targets.FNumberRelativeTolerance);
            Assert.InRange(Math.Abs(optic.Paraxial.GetTransferMatrix(compiled.SurfaceIndices[seed.Reference.Id], compiled.SurfaceIndices[seed.Image.Id]).A), 0, options.FocusATolerance);
            Assert.All(TopologyEvaluationPlan.Geometry(compiled, plan.Targets, 64), row =>
            {
                var value = TopologyEvaluationPlan.FormalMeasure(optic, row);
                Assert.True(double.IsFinite(value) && (row.Upper ? value <= row.Limit : value >= row.Limit));
            });
        }
        Assert.Equal(fingerprint, ContentFingerprint.Compute(plan));
        var directory = Path.Combine(Path.GetTempPath(), "seed-evidence-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "preparation.seed-search.json");
            await ParaxialSeedSearchFiles.SaveAsync(result, path);
            using var json = JsonDocument.Parse(await File.ReadAllBytesAsync(path));
            var stored = json.RootElement.GetProperty("result").Deserialize<ParaxialSeedSearchResult>(Json);
            Assert.Equal(ContentFingerprint.Compute(result), ContentFingerprint.Compute(stored));
            Assert.Equal(ContentFingerprint.Compute(result), json.RootElement.GetProperty("sha256").GetString());
            if (Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SEED_SEARCH_EVIDENCE") is { Length: > 0 } evidence)
            {
                Directory.CreateDirectory(evidence); File.Copy(path, Path.Combine(evidence, "first-order-preparation.json"), true);
                await AutonomousDesignFiles.SavePlanAsync(plan with { Seeds = result.Seeds,
                    Options = plan.Options with { SeedPreparation = options, PoolDiversity = new() } },
                    Path.Combine(evidence, "prepared-first-order.topology-plan.json"));
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void PreparationIsDeterministicBoundedCancellableAndRejectsInvalidVersionsBeforeWork()
    {
        var plan = Plan(); var options = new ParaxialSeedSearchOptions { MaximumStarts = 3, MaximumFunctionEvaluations = 8, MaximumEvaluationsPerStart = 2 };
        var a = ParaxialSeedSearch.Run(plan, options); var b = ParaxialSeedSearch.Run(plan, options);
        Assert.Equal(ContentFingerprint.Compute(a.Seeds), ContentFingerprint.Compute(b.Seeds));
        Assert.Equal(a.Work.FunctionEvaluations, b.Work.FunctionEvaluations);
        Assert.Equal(a.Attempts.Select(attempt => attempt.Outcome), b.Attempts.Select(attempt => attempt.Outcome));
        Assert.InRange(a.Work.FunctionEvaluations, 1, 8); Assert.Equal(0, a.Work.TracedRays);
        var exhausted = ParaxialSeedSearch.Run(plan, options with { MaximumFunctionEvaluations = 3, MaximumEvaluationsPerStart = 3 });
        Assert.Equal(ParaxialSeedTermination.BudgetExhausted, exhausted.Termination); Assert.Equal(3, exhausted.Work.FunctionEvaluations);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        var stopped = ParaxialSeedSearch.Run(plan, options, cancelled.Token);
        Assert.Equal(ParaxialSeedTermination.Cancelled, stopped.Termination); Assert.Equal(0, stopped.Work.FunctionEvaluations);
        using var afterFirst = new CancellationTokenSource();
        var partial = ParaxialSeedSearch.Run(plan, new() { MaximumStarts = 3, MaximumFunctionEvaluations = 100 }, afterFirst.Token,
            (_, _) => afterFirst.Cancel());
        Assert.Equal(ParaxialSeedTermination.Cancelled, partial.Termination); Assert.Single(partial.Attempts); Assert.NotEmpty(partial.Seeds);
        using var trace = SequentialTraceMeasurement.Begin();
        Assert.Throws<ArgumentException>(() => ParaxialSeedSearch.Run(plan, options with { Version = 2 }));
        Assert.Throws<ArgumentException>(() => AutonomousSearchService.Create("invalid-diversity", plan with { Options = plan.Options with { PoolDiversity = new() { Version = 2 } } }));
        Assert.Throws<ArgumentException>(() => ParaxialSeedSearch.Run(plan with { Seeds = [plan.Seeds[0] with
            { Elements = plan.Seeds[0].Elements.SetItem(0, plan.Seeds[0].Elements[0] with { Transition = true }) }],
            Constraints = plan.Constraints with { AllowTransitionElements = true } }, options));
        Assert.Equal(0, trace.RayCount);
    }

    internal static AutonomousSearchPlan Plan()
    {
        var plan = AutonomousDesignExamples.ThreeElement();
        return plan with { Options = plan.Options with { AxialCompensation = null },
            Seeds = [plan.Seeds[0] with { Fields = [new("Axis", 0, 0)] }] };
    }
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals, Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
}

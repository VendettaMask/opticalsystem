using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed class DelanoMaterializationTests
{
    [Fact]
    public async Task ThickDelanoPreparesOriginalWideSeedsWithinTheSameCapsAndFixedPhysicalEnvelope()
    {
        var (plan, previous) = await Wide();
        var options = previous with { Generation = previous.Generation! with { MaterializationVersion = 2 } };
        var result = ParaxialSeedSearch.Run(plan, options);
        if (Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_MATERIALIZATION_EVIDENCE") is { } directory)
            await ParaxialSeedSearchFiles.SaveAsync(result, Path.Combine(directory, "development-thick-preparation.json"));
        Assert.NotEmpty(result.Seeds); Assert.Equal(0, result.Work.TracedRays);
        Assert.Equal(4, result.Attempts.Length); Assert.All(result.Attempts, a => Assert.Equal("FirstOrderFeasibleOnly", a.Outcome));
        Assert.InRange(result.Work.FunctionEvaluations, 1, 120);
        Assert.Equal(result.Work.FunctionEvaluations, result.Attempts.Sum(a => a.FunctionEvaluations));
        Assert.All(result.Attempts, a => Assert.InRange(a.FunctionEvaluations, 1, 30));
        Assert.True(result.Work.MatrixEvaluations > result.Work.FunctionEvaluations);
        foreach (var seed in result.Seeds)
        {
            Assert.Equal(plan.Seeds[0].Stop, seed.Stop); Assert.Equal(plan.Seeds[0].Image, seed.Image);
            Assert.Equal(plan.Seeds[0].Fields, seed.Fields); Assert.Equal(plan.Seeds[0].Wavelengths, seed.Wavelengths);
            Assert.Equal(plan.Seeds[0].Elements.Select(e => e.Material), seed.Elements.Select(e => e.Material));
            var source = TopologyCompiler.Compile(seed, plan.Constraints); var optic = source.CreateOptic();
            Assert.InRange(Math.Abs(optic.Paraxial.EstimateEffectiveFocalLength() / plan.Targets.EffectiveFocalLengthMillimeters - 1), 0, .02);
            Assert.InRange(Math.Abs(optic.Paraxial.EstimateFNumber() / plan.Targets.FNumber - 1), 0, .02);
            Assert.All(TopologyEvaluationPlan.Geometry(source, plan.Targets, 64), row =>
            {
                var value = TopologyEvaluationPlan.FormalMeasure(optic, row);
                Assert.True(double.IsFinite(value) && (row.Upper ? value <= row.Limit : value >= row.Limit));
            });
        }
    }

    [Fact]
    public async Task ThickMaterializationAlreadyMeetsTheFixedStopFocalAndImageRequirementsBeforeRestoration()
    {
        var (plan, options) = await Wide(); var random = new DeterministicRandom(plan.Options.Seed);
        var generation = options.Generation! with { MaterializationVersion = 2 }; var rows = new List<object>();
        using var rays = SequentialTraceMeasurement.Begin();
        for (var start = 0; start < 4; start++)
        {
            var work = new StructureGenerationWork(); var functions = 0;
            var seed = StructureSeedGenerator.Create(plan, generation, ref random, () => functions++, default, work: work);
            var source = TopologyCompiler.Compile(seed, plan.Constraints); var optic = source.CreateOptic();
            var focal = optic.Paraxial.EstimateEffectiveFocalLength(); var fNumber = optic.Paraxial.EstimateFNumber();
            var matrix = optic.Paraxial.GetTransferMatrix(source.SurfaceIndices[seed.Reference.Id], source.SurfaceIndices[seed.Image.Id]);
            var pupil = optic.Paraxial.EstimateEntrancePupilDiameter();
            Assert.InRange(Math.Abs(focal / plan.Targets.EffectiveFocalLengthMillimeters - 1), 0, 1e-10);
            Assert.InRange(Math.Abs(fNumber / plan.Targets.FNumber - 1), 0, 1e-10); Assert.InRange(Math.Abs(matrix.A), 0, 1e-10);
            Assert.InRange(functions, 1, 10);
            rows.Add(new
            {
                Index = start,
                Focal = focal,
                FNumber = fNumber,
                FocusA = matrix.A,
                EntrancePupilDiameter = pupil,
                Functions = functions,
                GenerationMatrixRequests = work.MatrixRequests,
                work.GeometryProbes,
                AdditionalDiagnosticRequests = 4,
                Seed = seed
            });
        }
        Assert.Equal(0, rays.RayCount);
        await Evidence("thick-before-restoration.json", new { Scope = "Four generated thick proposals before least-squares restoration; separate from the final comparison", TracedRays = rays.RayCount, Starts = rows });
    }

    [Fact]
    public async Task ThickGenerationIsIndependentOfSourceLensShapeAndChargesInterruptedConstruction()
    {
        var (plan, options) = await Wide(); var generation = options.Generation! with { MaterializationVersion = 2 };
        var changed = plan with
        {
            Seeds = [plan.Seeds[0] with { Elements = plan.Seeds[0].Elements.Select(e => e with
            { Front = e.Front with { Profile = e.Front.Profile with { CurvaturePerMillimeter = e.Front.Profile.CurvaturePerMillimeter * .5 } } }).ToImmutableArray() }]
        };
        var randomA = new DeterministicRandom(42); var randomB = new DeterministicRandom(42);
        using var rays = SequentialTraceMeasurement.Begin();
        var a = StructureSeedGenerator.Create(plan, generation, ref randomA, () => { }, default);
        var b = StructureSeedGenerator.Create(changed, generation, ref randomB, () => { }, default);
        Assert.Equal(ContentFingerprint.Compute(a), ContentFingerprint.Compute(b));
        var interrupted = ParaxialSeedSearch.Run(plan, options with
        {
            Generation = generation,
            MaximumFunctionEvaluations = 3,
            MaximumEvaluationsPerStart = 3
        });
        Assert.Equal(2, interrupted.Work.FunctionEvaluations); Assert.Empty(interrupted.Seeds);
        Assert.Equal(ParaxialSeedTermination.BudgetExhausted, interrupted.Termination);
        Assert.Equal(interrupted.Work.FunctionEvaluations, interrupted.Attempts.Sum(t => t.FunctionEvaluations));
        Assert.True(interrupted.Work.MatrixEvaluations >= 3); Assert.True(interrupted.Work.GeometryProbes > 0);
        Assert.Equal(0, rays.RayCount);
        Assert.Throws<ArgumentException>(() => ParaxialSeedSearch.ValidateOptions(options with { Generation = generation with { MaterializationVersion = 3 } }));
        Assert.Throws<ArgumentException>(() => ParaxialSeedSearch.ValidateOptions(options with { Generation = generation with { Method = StructureGenerationMethod.RandomPhysical } }));
    }

    [Fact]
    public async Task FrozenFailedStartsExposeSpacingAndFixedStopMismatchBeforeRestorationWithoutRays()
    {
        var (plan, options) = await Wide();
        using var frozen = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(Root(),
            "validation/rechecks/2026-10-29/bounded-initial-structure/runs/bounded-candidates/preparation.seed-search.json")));
        var old = frozen.RootElement.GetProperty("result").Deserialize<ParaxialSeedSearchResult>(Json)!;
        var random = new DeterministicRandom(plan.Options.Seed); var rows = new List<object>(); var requests = 0; long probes = 0;
        using var rays = SequentialTraceMeasurement.Begin();
        for (var start = 0; start < 4; start++)
        {
            StructureGenerationTrace? trace = null;
            var generated = StructureSeedGenerator.Create(plan, options.Generation!, ref random, () => { }, default, value => trace = value);
            Assert.Equal(old.Attempts[start].SourceFingerprint, ContentFingerprint.Compute(generated));
            var source = TopologyCompiler.Compile(generated, plan.Constraints); var optic = source.CreateOptic();
            var reference = source.SurfaceIndices[generated.Reference.Id];
            requests++; var full = optic.Paraxial.GetTransferMatrix(reference, source.SurfaceIndices[generated.Image.Id]);
            requests++; var stop = optic.Paraxial.GetTransferMatrix(reference, source.SurfaceIndices[generated.Stop.Id]);
            requests++; var focal = optic.Paraxial.EstimateEffectiveFocalLength();
            requests++; var fNumber = optic.Paraxial.EstimateFNumber();
            var elements = generated.Elements.Select(element =>
            {
                var frontIndex = source.SurfaceIndices[element.FrontSurfaceId];
                requests++; var m = optic.Paraxial.GetTransferMatrix(frontIndex,
                    source.SurfaceIndices[element.BackSurfaceId]);
                // Independent diagnostic factorization only; product calculations remain in Core.
                var power = -m.C;
                return new
                {
                    element.FrontZMillimeters,
                    element.ThicknessMillimeters,
                    element.BackZMillimeters,
                    Matrix = m,
                    element.Front.Profile.CurvaturePerMillimeter,
                    BackCurvature = element.Back.Profile.CurvaturePerMillimeter,
                    FrontPrincipalOffset = (1 - m.D) / power,
                    BackPrincipalOffset = (1 - m.A) / power
                };
            }).ToArray();
            var effectiveDistances = elements.Select((element, i) => i + 1 == elements.Length
                ? generated.Image.ZMillimeters - element.BackZMillimeters + element.BackPrincipalOffset
                : elements[i + 1].FrontZMillimeters - element.BackZMillimeters + element.BackPrincipalOffset + elements[i + 1].FrontPrincipalOffset).ToArray();
            for (var i = 0; i < elements.Length; i++) Assert.Equal(trace!.PowersPerMillimeter[i], -elements[i].Matrix.C, 11);
            Assert.True(effectiveDistances[^1] > trace!.DistancesMillimeters[^1]);
            var failedGeometry = TopologyEvaluationPlan.Geometry(source, plan.Targets, 64).Select(row =>
            {
                probes++; var value = TopologyEvaluationPlan.FormalMeasure(optic, row);
                return new
                {
                    row.Id,
                    row.Limit,
                    row.Upper,
                    Value = value,
                    Failed = !double.IsFinite(value) || (row.Upper ? value > row.Limit : value < row.Limit)
                };
            }).Where(row => row.Failed).ToArray();
            var incomingRadius = plan.Targets.EffectiveFocalLengthMillimeters / (2 * plan.Targets.FNumber);
            rows.Add(new
            {
                Index = start,
                Trace = trace,
                Focal = focal,
                FNumber = fNumber,
                FocusA = full.A,
                EntranceMarginalRadius = incomingRadius,
                MarginalAtFixedStop = incomingRadius * stop.A,
                ActualStopRadius = generated.Stop.ClearSemiDiameterMillimeters,
                Elements = elements,
                EffectiveDistances = effectiveDistances,
                FailedGeometry = failedGeometry,
                FrozenRestoredEndpoint = old.Attempts[start].Outcome
            });
        }
        Assert.Equal(0, rays.RayCount);
        await Evidence("legacy-materialization-diagnosis.json", new
        {
            AdditionalFirstOrderRequests = requests,
            GeometryProbes = probes,
            AdditionalTracedRays = rays.RayCount,
            Scope = "Deterministic read-only replay of four frozen proposals; no optimization",
            Starts = rows
        });
    }

    internal static async Task<(AutonomousSearchPlan Plan, ParaxialSeedSearchOptions Options)> Wide()
    {
        var plan = await AutonomousDesignFiles.LoadPlanAsync(Path.Combine(Root(),
            "validation/rechecks/2026-10-28/three-to-two-continuation/selected-runs/reduce-3-2-42-C-geometry/task.topology-plan.json"));
        using var file = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(Root(),
            "validation/rechecks/2026-10-29/bounded-initial-structure/protocol.json")));
        return (plan, file.RootElement.GetProperty("ImprovedPreparation").Deserialize<ParaxialSeedSearchOptions>(Json)!);
    }

    internal static async Task Evidence(string name, object value)
    {
        if (Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_MATERIALIZATION_EVIDENCE") is { } directory)
            await File.WriteAllTextAsync(Path.Combine(directory, name), JsonSerializer.Serialize(value, Json));
    }
    internal static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
        return directory!.FullName;
    }
    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter() }
    };
}

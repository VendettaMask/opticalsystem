using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.Optimization;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed class SpcPreparationTests
{
    private sealed class Stop : Exception;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals, Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };

    [Fact]
    public void BfgsAndHessianContinuationNeverReplayAnOpticalCallback()
    {
        static StationaryValue Value(double[] x) => new(Math.Pow(x[0] * x[0] - 1, 2) + x[1] * x[1], [4 * x[0] * (x[0] * x[0] - 1), 2 * x[1]]);
        var referenceCalls = new List<string>(); var resumedCalls = new List<string>();
        StationaryValue Reference(double[] x) { referenceCalls.Add(ContentFingerprint.Compute(x)); return Value(x); }
        StationaryValue Resumed(double[] x) { resumedCalls.Add(ContentFingerprint.Compute(x)); return Value(x); }
        var reference = StationaryPointSearch.Minimize(Reference, [.05, .1], [-2, -2], [2, 2], 100, 1e-8);
        StationaryContinuation? saved = null;
        Assert.Throws<Stop>(() => StationaryPointSearch.Minimize(Resumed, [.05, .1], [-2, -2], [2, 2], 100, 1e-8, checkpoint: s => { saved = s; throw new Stop(); }));
        var continuation = JsonSerializer.Deserialize<StationaryContinuation>(JsonSerializer.Serialize(saved, Json), Json)!;
        var endpoint = StationaryPointSearch.Minimize(Resumed, [.05, .1], [-2, -2], [2, 2], 100, 1e-8, continuation: continuation);
        Assert.Equal(referenceCalls, resumedCalls); Assert.Equal(ContentFingerprint.Compute(reference), ContentFingerprint.Compute(endpoint));
        referenceCalls.Clear(); resumedCalls.Clear();
        var curve = StationaryPointSearch.Curvature(Reference, [0, 0], [-2, -2], [2, 2], .001);
        StationaryCurvatureContinuation? column = null;
        Assert.Throws<Stop>(() => StationaryPointSearch.Curvature(Resumed, [0, 0], [-2, -2], [2, 2], .001, checkpoint: c => { column = c; throw new Stop(); }));
        var reopened = JsonSerializer.Deserialize<StationaryCurvatureContinuation>(JsonSerializer.Serialize(column, Json), Json)!;
        var complete = StationaryPointSearch.Curvature(Resumed, [0, 0], [-2, -2], [2, 2], .001, continuation: reopened);
        Assert.Equal(referenceCalls, resumedCalls); Assert.Equal(ContentFingerprint.Compute(curve), ContentFingerprint.Compute(complete));
    }

    [Fact]
    public async Task PreparationStopsAcrossParentHessianBothBranchesAndPhysicalUpdatesThenResumesExactly()
    {
        var plan = Plan(); var baseline = SeedPreparationSearch.Run(SeedPreparationSearch.Create("continuous", plan, true), _ => { });
        Assert.Equal(SeedPreparationStatus.Completed, baseline.Status); Assert.Equal(2, baseline.Result!.Spc!.QualifiedBranchIds.Length);
        var directory = Path.Combine(Path.GetTempPath(), "spc-resume-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "run.preparation.json"); var store = new SeedPreparationCheckpointStore();
        var state = SeedPreparationSearch.Create("resumed", plan, true) with { Revision = 1 };
        await store.SaveAsync(state, path, 0);
        var targets = new Queue<string>(["parent", "hessian", "minus", "minus-physical", "plus", "plus-physical"]);
        try
        {
            while (state.Stage != "Finished")
            {
                using var pause = new CancellationTokenSource(); var revision = state.Revision;
                state = SeedPreparationSearch.Run(state, next =>
                {
                    store.SaveAsync(next, path, revision).AsTask().GetAwaiter().GetResult(); revision = next.Revision;
                    if (targets.Count > 0 && At(next.Spc!, targets.Peek())) { targets.Dequeue(); pause.Cancel(); }
                }, pause.Token);
                if (state.Stage == "Finished") break;
                Assert.Equal(SeedPreparationStatus.Suspended, state.Status); Assert.Null(state.Pending);
                var captured = ContentFingerprint.Compute(state);
                using var reads = SequentialTraceMeasurement.Begin();
                store = new(); state = await store.LoadAsync(path); Assert.Equal(0, reads.RayCount);
                Assert.Equal(captured, ContentFingerprint.Compute(state));
            }
            Assert.Empty(targets); Assert.Equal(0, state.LostReservations);
            Assert.Equal(baseline.RandomState, state.RandomState);
            Assert.Equal(Comparable(baseline.Spc!), Comparable(state.Spc!));
            Assert.Equal(baseline.Consumed with { Elapsed = TimeSpan.Zero }, state.Consumed with { Elapsed = TimeSpan.Zero });
            Assert.Equal(ContentFingerprint.Compute(baseline.Result.Seeds), ContentFingerprint.Compute(state.Result!.Seeds));
            Assert.NotEmpty(state.Result.Seeds); Assert.All(state.Result.Seeds, seed => Assert.False(seed.HasTransitionElements));
            Assert.InRange(state.Consumed.FunctionEvaluations, 1, plan.Options.SeedPreparation!.MaximumFunctionEvaluations);
            Assert.InRange(state.Consumed.TracedRays, 1, plan.Options.SeedPreparation.MaximumTracedRays);
        }
        finally { Directory.Delete(directory, true); }
        static bool At(SpcPreparationState s, string target) => target switch
        {
            "parent" => s.Phase == SpcPreparationPhase.ParentOptimization && s.Optimizer?.Iterations >= 1,
            "hessian" => s.Phase == SpcPreparationPhase.SaddleCurvature && s.Curvature?.NextColumn == 1,
            "minus" => s.Sign == -1 && s.Phase == SpcPreparationPhase.BranchOptimization && s.BranchMinimum?.Iterations >= 1,
            "plus" => s.Sign == 1 && s.Phase == SpcPreparationPhase.BranchOptimization && s.BranchMinimum?.Iterations >= 1,
            "minus-physical" => s.Sign == -1 && s.Phase == SpcPreparationPhase.PhysicalOptimization && s.PhysicalContinuation?.Steps.Length >= 1,
            "plus-physical" => s.Sign == 1 && s.Phase == SpcPreparationPhase.PhysicalOptimization && s.PhysicalContinuation?.Steps.Length >= 1,
            _ => false
        };
    }

    [Fact]
    public void LostInFlightWorkIsChargedOnceAndCannotBePromotedByRestarting()
    {
        var state = SeedPreparationSearch.Create("crash", Plan()); SeedPreparationCheckpoint? inFlight = null;
        Assert.Throws<IOException>(() => SeedPreparationSearch.Run(state, next =>
        {
            if (next.Pending is { InFlight: true }) { inFlight = next; throw new IOException("Simulated process loss after durable reservation"); }
        }));
        Assert.NotNull(inFlight);
        var settled = SeedPreparationSearch.Run(inFlight, _ => { });
        Assert.Equal(1, settled.LostReservations); Assert.Equal(SeedPreparationStatus.Interrupted, settled.Status);
        Assert.Empty(settled.Result!.Seeds); Assert.Equal("UnsettledReservationCharged", settled.StopReason);
        var repeated = SeedPreparationSearch.Run(settled, _ => throw new Exception("Finished state must not persist again"));
        Assert.Equal(settled.Consumed, repeated.Consumed); Assert.Equal(1, repeated.LostReservations);
    }

    [Theory]
    [InlineData("f4")]
    [InlineData("insertion")]
    [InlineData("limit")]
    public void AutomaticSpcNeverRelaxesOriginalRestrictions(string conflict)
    {
        var plan = Plan();
        if (conflict == "f4") plan = plan with { Seeds = [plan.Seeds[0] with { Stop = plan.Seeds[0].Stop with { ZMillimeters = 0, ClearSemiDiameterMillimeters = 6.25 } }] };
        if (conflict == "insertion") plan = plan with { Options = plan.Options with { Expansion = plan.Options.Expansion! with { EnableInsertion = false } } };
        if (conflict == "limit") plan = plan with { Constraints = plan.Constraints with { MaximumElementCount = 1 } };
        using var rays = SequentialTraceMeasurement.Begin(); var hash = ContentFingerprint.Compute(plan);
        var done = SeedPreparationSearch.Run(SeedPreparationSearch.Create("guard", plan, true), _ => { });
        Assert.Equal(0, rays.RayCount); Assert.Equal(0, done.Consumed.FunctionEvaluations); Assert.Empty(done.Result!.Seeds);
        Assert.Equal(hash, ContentFingerprint.Compute(done.Plan));
        if (conflict == "f4") Assert.Equal("OriginalExternalStopApertureConflict", done.StopReason);
    }

    [Fact]
    public void InvalidStrategiesAndForeignKernelsAreRejectedWithoutTracing()
    {
        var plan = Plan();
        Assert.Throws<ArgumentException>(() => SeedPreparationSearch.Create("mixed", plan with { Options = plan.Options with
        { SeedPreparation = plan.Options.SeedPreparation! with { ColdStart = new() } } }));
        var state = SeedPreparationSearch.Create("foreign", plan) with { KernelFingerprint = new('0', 64) };
        SeedPreparationSearch.Validate(state); Assert.False(SeedPreparationSearch.CanExecute(state));
        var regular = SeedPreparationSearch.Create("regular", plan with { Options = plan.Options with { SeedPreparation = new() } });
        var wrongEnvironment = SeedPreparationSearch.Create("wrong-environment", plan) with { KernelFingerprint = regular.KernelFingerprint };
        Assert.False(SeedPreparationSearch.CanExecute(wrongEnvironment));
        using var rays = SequentialTraceMeasurement.Begin(); Assert.Throws<NotSupportedException>(() => SeedPreparationSearch.Run(state, _ => { })); Assert.Equal(0, rays.RayCount);
        Assert.DoesNotContain("\"spc\"", JsonSerializer.Serialize(new ParaxialSeedSearchOptions(), Json));
        Assert.DoesNotContain("\"spc\"", JsonSerializer.Serialize(SeedPreparationSearch.Create("old", SpcBranchTests.Plan() with
        { Options = SpcBranchTests.Plan().Options with { SeedPreparation = new() } }), Json));
    }

    internal static AutonomousSearchPlan Plan(long seed = 42)
    {
        var plan = SpcBranchTests.Plan(); return plan with { Options = plan.Options with { Seed = seed, SeedPreparation = SpcBranchTests.Budget() with { Spc = new() } } };
    }
    private static string Comparable(object value)
    {
        var node = JsonSerializer.SerializeToNode(value, Json)!; Strip(node); return node.ToJsonString();
        static void Strip(JsonNode n)
        {
            if (n is JsonObject o) foreach (var key in o.Select(p => p.Key).ToArray())
            { if (key is "elapsed") o.Remove(key); else if (o[key] is { } child) Strip(child); }
            else if (n is JsonArray a) foreach (var child in a) if (child is not null) Strip(child);
        }
    }
}

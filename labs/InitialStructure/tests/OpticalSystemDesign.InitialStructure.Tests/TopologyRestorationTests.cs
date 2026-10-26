using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using Xunit.Abstractions;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed class TopologyRestorationTests(ITestOutputHelper output)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
    private static readonly TopologyTrainingSampling Small = new() { PupilRings = 1, PupilArms = 8, ProfileRadialSamples = 8 };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ActualInsertionAndDeletionRecoverThenCompensateAndValidateWithProtectedImage(bool insert)
    {
        var parent = Source();
        var parentIdentity = parent.TopologyFingerprint;
        var edit = Edit(parent.Topology, insert);
        Assert.True(edit.Succeeded, edit.Failure?.Message);
        var child = TopologyCompiler.Compile(edit.Topology!, parent.Constraints);
        var recoveryVariables = Variables(child.Topology).Where(request => !request.SurfaceId.StartsWith("m3-added", StringComparison.Ordinal)).ToArray();
        var localVariables = recoveryVariables.Take(2).ToArray();
        var fullVariables = Variables(child.Topology).ToArray();
        var stage = new FixedTopologyOptions
        {
            MaximumUpdates = 2,
            MaximumFunctionEvaluations = 4,
            MaximumGradientEvaluations = 3,
            ComputeMode = DesignComputeMode.Cpu,
            Sampling = Small,
            LearningRate = .02
        };
        var options = new TopologyRestorationOptions
        {
            Recovery = new() { ProfileRadialSamples = 16 },
            LocalCompensation = stage,
            FullOptimization = stage with { MaximumUpdates = 5, MaximumFunctionEvaluations = 7, MaximumGradientEvaluations = 6 }
        };
        var result = TopologyRestorationWorkflow.Run(parent, edit, recoveryVariables, localVariables, fullVariables, new(), options);
        Evidence(insert ? "insert-3-to-4" : "delete-3-to-2", new
        {
            Result = result,
            ParentSnapshot = parent.Snapshot,
            EditedSnapshot = child.Snapshot,
            FinalSnapshot = TopologyCompiler.Compile(result.Endpoint, parent.Constraints).Snapshot
        });
        Assert.Equal(TopologyRestorationTermination.Completed, result.Termination);
        Assert.NotNull(result.Recovery);
        Assert.Equal(ParaxialRecoveryTermination.Recovered, result.Recovery.Termination);
        Assert.True(result.Recovery.Steps.Length > 0);
        Assert.InRange(Math.Abs(result.Recovery.EndpointAError!.Value), 0, options.Recovery.ATolerance);
        Assert.InRange(Math.Abs(result.Recovery.EndpointFocalCError!.Value), 0, options.Recovery.FocalCTolerance);
        Assert.True(result.LocalCompensation!.AcceptedUpdates > 0);
        Assert.True(result.FullOptimization!.AcceptedUpdates > 0);
        Assert.NotEqual(result.LocalCompensation.ObjectiveFingerprint, result.FullOptimization.ObjectiveFingerprint);
        Assert.True(result.FinalAssessment!.Completed, result.FinalAssessment.Failure);
        Assert.Equal(21, result.FinalAssessment.Fields.Length);
        Assert.All(result.FinalAssessment.Fields, field => Assert.Equal(3 * 320, field.AttemptedRays));
        Assert.Equal(insert ? 4 : 2, result.Endpoint.Elements.Length);
        Assert.Equal(parent.Topology.Image, result.Endpoint.Image);
        Assert.Equal(parent.Topology.Reference, result.Endpoint.Reference);
        Assert.Equal(parent.Topology.Stop, result.Endpoint.Stop);
        Assert.Equal(parentIdentity, ContentFingerprint.Compute(parent.Topology));
        Assert.Equal(result.Recovery.Work.FunctionEvaluations + result.LocalCompensation.Work.FunctionEvaluations + result.FullOptimization.Work.FunctionEvaluations,
            result.Work.FunctionEvaluations);
        Assert.InRange(result.Work.FunctionEvaluations, 1, options.MaximumFunctionEvaluations);
        Assert.InRange(result.Work.GradientEvaluations, 1, options.MaximumGradientEvaluations);
        Assert.Equal(2, result.Work.ValidationEvaluations);
        var restored = JsonSerializer.Deserialize<TopologyRestorationResult>(JsonSerializer.Serialize(result, Json), Json)!;
        var compiled = TopologyCompiler.Compile(restored.Endpoint, parent.Constraints);
        Assert.Equal(compiled.OpticFingerprint, result.FinalAssessment.OpticFingerprint);
        var optic = compiled.CreateOptic();
        var roundtrip = Optic.FromSnapshot(optic.ToSnapshot());
        Assert.Equal(optic.Paraxial.EstimateEffectiveFocalLength(), roundtrip.Paraxial.EstimateEffectiveFocalLength(), 11);
        var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_TOPOLOGY_M3_EVIDENCE")
            ?? Path.Combine(Path.GetTempPath(), "topology-m3-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var nativePath = Path.Combine(directory, insert ? "insert-endpoint.staropt" : "delete-endpoint.staropt");
        await StarOptProjectStore.SaveAsync(new([optic], 0), nativePath);
        var native = await StarOptProjectStore.LoadAsync(nativePath);
        Assert.Equal(ContentFingerprint.Compute(optic.ToSnapshot()), ContentFingerprint.Compute(native.Configurations[0].ToSnapshot()));
        output.WriteLine($"{result.Change!.Operation}: 3->{result.Endpoint.Elements.Length}; recovery={result.Recovery.Steps.Length}, final EFL={result.FinalAssessment.EffectiveFocalLengthMillimeters:R}, RMS={result.FinalAssessment.Fields.Max(field => field.RmsRadiusMillimeters):R}, meets={result.FinalAssessment.MeetsTargets}; functions={result.Work.FunctionEvaluations}, matrices={result.Work.MatrixEvaluations}, rays={result.Work.TracedRays}");
    }

    [Fact]
    public void RecoveryRecordsRankNoFreedomGeometryBudgetCancellationAndTimeoutSeparately()
    {
        var parent = Source();
        var child = TopologyCompiler.Compile(Edit(parent.Topology, false).Topology!);
        var requests = Variables(child.Topology).ToArray();
        var noFreedom = ParaxialRestorer.Run(parent, child, [], new());
        Assert.Equal(ParaxialRecoveryTermination.NoFreeVariables, noFreedom.Termination);
        var rank = ParaxialRestorer.Run(parent, child, requests.Take(1), new());
        Assert.Equal(ParaxialRecoveryTermination.RankDeficient, rank.Termination);
        Assert.Equal(1, rank.InitialJacobianRank);
        var budget = ParaxialRestorer.Run(parent, child, requests, new(), new() { MaximumFunctionEvaluations = 2 });
        Assert.Equal(ParaxialRecoveryTermination.BudgetExhausted, budget.Termination);
        Assert.Equal(2, budget.Work.FunctionEvaluations); Assert.Equal(0, budget.Work.DifferenceEvaluations);
        using var stop = new CancellationTokenSource(); stop.Cancel();
        var cancelled = ParaxialRestorer.Run(parent, child, requests, new(), cancellationToken: stop.Token);
        Assert.Equal(ParaxialRecoveryTermination.Cancelled, cancelled.Termination);
        Assert.Equal(0, cancelled.Work.FunctionEvaluations);
        using var midwayStop = new CancellationTokenSource();
        var interrupted = ParaxialRestorer.Run(parent, child, requests, new(), cancellationToken: midwayStop.Token,
            progress: step => midwayStop.Cancel());
        Assert.Equal(ParaxialRecoveryTermination.Cancelled, interrupted.Termination);
        Assert.Single(interrupted.Steps);
        Assert.True(interrupted.Work.DifferenceEvaluations > 0);
        Assert.True(interrupted.Trials.Any(trial => trial.Accepted));
        Assert.True(interrupted.Steps[0].Coordinates.SequenceEqual(interrupted.EndpointCoordinates));
        var timeout = ParaxialRestorer.Run(parent, child, requests, new(), new() { TimeLimit = TimeSpan.FromTicks(1) });
        Assert.Equal(ParaxialRecoveryTermination.TimeLimit, timeout.Termination);
        var bad = child.Topology;
        bad = bad with
        {
            Elements = bad.Elements.SetItem(0, bad.Elements[0] with
            {
                Front = bad.Elements[0].Front with
                { Profile = bad.Elements[0].Front.Profile with { CurvaturePerMillimeter = .3 } }
            })
        };
        var blocked = ParaxialRestorer.Run(parent, TopologyCompiler.Compile(bad),
            Variables(bad).Select(request => request with { LowerBound = -.5, UpperBound = .5 }), new());
        Assert.Equal(ParaxialRecoveryTermination.GeometryBlocked, blocked.Termination);
        Assert.True(blocked.Work.GeometryProbes > 0);
        Evidence("failure-contracts", new { noFreedom, rank, budget, cancelled, interrupted, timeout, blocked });
    }

    [Fact]
    public void ChildQ0HasItsOwnDimensionAndFirstLensDeletionKeepsTheMechanicalReference()
    {
        var parent = Source();
        var plate = Added() with { Front = Added().Front with { Profile = new() }, Back = Added().Back with { Profile = new() } };
        var inserted = TopologyOperators.Insert(parent.Topology, "stop", "lens-1:front", plate, .5);
        var child = TopologyCompiler.Compile(inserted.Topology!);
        var unchanged = ParaxialRestorer.Run(parent, child, Variables(child.Topology), new());
        Assert.Equal(ParaxialRecoveryTermination.Recovered, unchanged.Termination);
        Assert.Equal(8, unchanged.ChildInitialCoordinates.Length);
        Assert.True(unchanged.ChildInitialCoordinates.SequenceEqual(unchanged.EndpointCoordinates));
        Assert.Empty(unchanged.Steps);
        var deleted = TopologyOperators.Delete(parent.Topology, "lens-1");
        var withoutFirst = TopologyCompiler.Compile(deleted.Topology!);
        var matrix = withoutFirst.CreateOptic().Paraxial.GetTransferMatrix(withoutFirst.SurfaceIndices[withoutFirst.Topology.Reference.Id],
            withoutFirst.SurfaceIndices[withoutFirst.Topology.Image.Id]);
        Assert.Equal(0, matrix.InputZMillimeters);
        Assert.Equal(parent.Topology.Image.ZMillimeters, matrix.OutputZMillimeters);
        Assert.Equal(10, withoutFirst.Topology.Elements[0].FrontZMillimeters);
        var badImageVariable = ParaxialRestorer.Run(parent, child,
            [new("image", OpticalDesignParameterKind.AxialPosition, 30, 80)], new());
        Assert.Equal(ParaxialRecoveryTermination.Unsupported, badImageVariable.Termination);
    }

    [Fact]
    public void StructuralWorkflowReservesIndependentValidationAndPreCancellationUsesNoOpticalWork()
    {
        var model = Source().Topology with { Fields = [new("Axis", 0, 0)] };
        var parent = TopologyCompiler.Compile(model);
        var edit = Edit(model, true);
        var variables = Variables(edit.Topology!).ToArray();
        var result = TopologyRestorationWorkflow.Run(parent, edit, variables, variables, variables, new(),
            new() { MaximumFunctionEvaluations = 1 });
        Assert.Equal(TopologyRestorationTermination.BudgetExhausted, result.Termination);
        Assert.Equal(1, result.Work.FunctionEvaluations); Assert.Equal(0, result.Work.GradientEvaluations);
        Assert.Equal(1, result.Work.ValidationEvaluations); Assert.True(result.Work.TracedRays > 0);
        Assert.True(result.FinalAssessment!.Completed);
        using var stop = new CancellationTokenSource(); stop.Cancel();
        var cancelled = TopologyRestorationWorkflow.Run(parent, edit, variables, variables, variables, new(), cancellationToken: stop.Token);
        Assert.Equal(TopologyRestorationTermination.Cancelled, cancelled.Termination);
        Assert.Equal(0, cancelled.Work.FunctionEvaluations); Assert.Equal(0, cancelled.Work.TracedRays);
        var wrong = edit with { Change = edit.Change! with { ParentFingerprint = "other-parent" } };
        var rejected = TopologyRestorationWorkflow.Run(parent, wrong, variables, variables, variables, new());
        Assert.Equal(TopologyRestorationTermination.Unsupported, rejected.Termination);
        Assert.Equal(0, rejected.Work.FunctionEvaluations);
        var tampered = edit.Topology! with
        {
            Elements = edit.Topology.Elements.SetItem(0,
            edit.Topology.Elements[0] with { FrontZMillimeters = edit.Topology.Elements[0].FrontZMillimeters + .1 })
        };
        var forged = edit with { Topology = tampered, Change = edit.Change! with { ChildFingerprint = ContentFingerprint.Compute(tampered) } };
        var rejectedMutation = TopologyRestorationWorkflow.Run(parent, forged, variables, variables, variables, new());
        Assert.Equal(TopologyRestorationTermination.Unsupported, rejectedMutation.Termination);
        Assert.Equal(0, rejectedMutation.Work.FunctionEvaluations);
    }

    private static IEnumerable<TopologyVariableRequest> Variables(DesignTopology topology) => topology.Elements.Where(element => !element.Protected)
        .SelectMany(element => new[] { element.FrontSurfaceId, element.BackSurfaceId })
        .Select(id => new TopologyVariableRequest(id, OpticalDesignParameterKind.Curvature, -.08, .08, .01));
    private static LensElement Added() => new()
    {
        Id = "m3-added",
        FrontSurfaceId = "m3-added:front",
        BackSurfaceId = "m3-added:back",
        ThicknessMillimeters = 1,
        Material = "N-BK7",
        Front = new() { ClearSemiDiameterMillimeters = 7.5, Profile = new() { CurvaturePerMillimeter = .003 } },
        Back = new() { ClearSemiDiameterMillimeters = 7.5, Profile = new() { CurvaturePerMillimeter = -.003 } }
    };
    private static TopologyEditResult Edit(DesignTopology topology, bool insert) => insert
        ? TopologyOperators.Insert(topology, "lens-1:back", "lens-2:front", Added(), .5)
        : TopologyOperators.Delete(topology, "lens-2");
    private static CompiledTopology Source()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
        var path = Path.Combine(directory!.FullName, "labs/InitialStructure/benchmarks/autonomous-topology-v1/foundation-3.json");
        return TopologyCompiler.Compile(JsonSerializer.Deserialize<DesignTopology>(File.ReadAllText(path), Json)!);
    }
    private static void Evidence(string name, object value)
    {
        var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_TOPOLOGY_M3_EVIDENCE");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name + ".json"), JsonSerializer.Serialize(value, Json));
    }
}

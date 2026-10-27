using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed class ParaxialRecoveryRepairTests
{
    [Theory]
    [InlineData("element-001")]
    [InlineData("element-002")]
    [InlineData("element-003")]
    [InlineData("element-004")]
    public async Task CoreFixedPlaneAbcdDerivativesMatchAllRemainingCurvatureAndAxialDifferences(string removed)
    {
        var plan = await Plan(); var edited = TopologyOperators.Delete(plan.Seeds[0], removed, plan.Constraints).Topology!;
        var source = TopologyCompiler.Compile(edited, plan.Constraints); var binding = Bind(source, plan, true);
        var optic = source.CreateOptic(); var first = source.SurfaceIndices[edited.Reference.Id]; var last = source.SurfaceIndices[edited.Image.Id];
        using var measurement = SequentialTraceMeasurement.Begin();
        var actual = DifferentiableParaxialMetrics.TransferMatrix(optic, binding.CoreParameters, first, last, includeInverseFNumber: true);
        var errors = new List<object>();
        for (var column = 0; column < binding.Slots.Length; column++)
        {
            const double h = 1e-6;
            var plus = binding.InitialCoordinates.ToArray(); var minus = plus.ToArray(); plus[column] += h; minus[column] -= h;
            var up = TopologyParameterBinder.Apply(source, binding, plus).CreateOptic(); var down = TopologyParameterBinder.Apply(source, binding, minus).CreateOptic();
            var a = up.Paraxial.GetTransferMatrix(first, last); var b = down.Paraxial.GetTransferMatrix(first, last);
            double[] fd = [(a.A - b.A) / (2 * h), (a.B - b.B) / (2 * h), (a.C - b.C) / (2 * h), (a.D - b.D) / (2 * h), (1 / up.Paraxial.EstimateFNumber() - 1 / down.Paraxial.EstimateFNumber()) / (2 * h)];
            double[] ad = [actual.A.Derivative(column), actual.B.Derivative(column), actual.C.Derivative(column), actual.D.Derivative(column), actual.InverseFNumber!.Value.Derivative(column)];
            for (var row = 0; row < 5; row++) Assert.True(Math.Abs(fd[row] - ad[row]) < 2e-6 + 3e-4 * Math.Abs(fd[row]), $"{removed}, param{column}, row{row}: FD={fd[row]:R}, AD={ad[row]:R}");
            errors.Add(new { Parameter = binding.Slots[column].Request, FiniteDifferences = fd, Automatic = ad });
        }
        Assert.Equal(0, measurement.RayCount); Assert.Equal(actual.Nominal.A, actual.A.Value); Assert.Equal(actual.Nominal.C, actual.C.Value);
        Assert.Throws<ArgumentOutOfRangeException>(() => DifferentiableParaxialMetrics.TransferMatrix(optic, binding.CoreParameters, 0, last));
        var identity = DifferentiableParaxialMetrics.TransferMatrix(optic, [], first, first);
        Assert.Equal(1, identity.A.Value); Assert.Equal(0, identity.B.Value); Assert.Equal(0, identity.C.Value); Assert.Equal(1, identity.D.Value);
        Evidence("matrix-" + removed, errors);
    }

    [Fact]
    public async Task DependentPupilJacobianWithIncompatibleResidualStillRejectsRecovery()
    {
        var plan = await Plan(); var parent = TopologyCompiler.Compile(plan.Seeds[0], plan.Constraints);
        using var recorded = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(Root(), "validation/rechecks/2026-10-27/four-to-three-recovery/prepared/preparation.json")));
        var restored = recorded.RootElement.GetProperty("Deletions")[2].GetProperty("Recovery").GetProperty("Topology").Deserialize<DesignTopology>(
            new JsonSerializerOptions { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } })!;
        var child = TopologyCompiler.Compile(restored, plan.Constraints);
        var surfaces = restored.Elements.Where(element => element.FrontZMillimeters > restored.Stop.ZMillimeters)
            .SelectMany(element => new[] { element.FrontSurfaceId, element.BackSurfaceId }).ToHashSet();
        var variables = Bind(child, plan, true).Slots.Where(slot => surfaces.Contains(slot.Request.SurfaceId)).Take(2).Select(slot => slot.Request).ToArray();
        using var measurement = SequentialTraceMeasurement.Begin();
        var result = ParaxialRestorer.Run(parent, child, variables, plan.Targets, plan.Options.Recovery with
        { UseAutomaticMatrixDerivatives = true, RestorePupilMagnification = true, MaximumFunctionEvaluations = 2 });
        Assert.Equal(2, result.InitialJacobianRank); Assert.Equal(ParaxialRecoveryTermination.RankDeficient, result.Termination);
        Assert.Empty(result.Steps); Assert.Equal(2, result.Work.FunctionEvaluations); Assert.Equal(0, measurement.RayCount);
        Assert.True(Math.Abs(result.InitialPupilError!.Value) > .05);
        Evidence("incompatible-pupil-rank", result);
    }

    [Fact]
    public async Task MatchedAAndCDoNotFalselyCertifyUnrestoredFixedStopPupil()
    {
        var plan = await Plan(); var parent = TopologyCompiler.Compile(plan.Seeds[0], plan.Constraints);
        using var recorded = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(Root(), "validation/rechecks/2026-10-27/four-to-three-recovery/prepared/preparation.json")));
        var restored = recorded.RootElement.GetProperty("Deletions")[2].GetProperty("Recovery").GetProperty("Topology").Deserialize<DesignTopology>(
            new JsonSerializerOptions { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } })!;
        var child = TopologyCompiler.Compile(restored, plan.Constraints); var binding = Bind(child, plan, true);
        var options = plan.Options.Recovery with { UseAutomaticMatrixDerivatives = true, RestorePupilMagnification = true, MaximumFunctionEvaluations = 2 };
        using var measurement = SequentialTraceMeasurement.Begin();
        var checkedResult = ParaxialRestorer.Run(parent, child, binding.Slots.Select(slot => slot.Request), plan.Targets, options);
        Assert.NotEqual(ParaxialRecoveryTermination.Recovered, checkedResult.Termination);
        Assert.InRange(Math.Abs(checkedResult.InitialAError!.Value), 0, plan.Options.Recovery.ATolerance);
        Assert.InRange(Math.Abs(checkedResult.InitialFocalCError!.Value), 0, plan.Options.Recovery.FocalCTolerance);
        Assert.True(Math.Abs(checkedResult.InitialPupilError!.Value) > .05); Assert.True(checkedResult.Work.PupilEvaluations > 0);
        var unchanged = ParaxialRestorer.Run(parent, parent, [], plan.Targets, options);
        Assert.Equal(ParaxialRecoveryTermination.Recovered, unchanged.Termination); Assert.Equal(0, unchanged.EndpointPupilError);
        Assert.Equal(0, measurement.RayCount); Evidence("pupil-guard", new { checkedResult, unchanged });
    }

    [Fact]
    public async Task AutomaticRecoveryReplacesDifferenceProbesAndRetainsHardGeometryChecksAndLegacyIdentity()
    {
        var plan = await Plan(); var parent = TopologyCompiler.Compile(plan.Seeds[0], plan.Constraints);
        var child = TopologyCompiler.Compile(TopologyOperators.Delete(plan.Seeds[0], "element-003", plan.Constraints).Topology!, plan.Constraints);
        var binding = Bind(child, plan, false); var requests = binding.Slots.Select(slot => slot.Request).ToArray();
        using var measurement = SequentialTraceMeasurement.Begin();
        var old = ParaxialRestorer.Run(parent, child, requests, plan.Targets, plan.Options.Recovery);
        var automatic = ParaxialRestorer.Run(parent, child, requests, plan.Targets, plan.Options.Recovery with { UseAutomaticMatrixDerivatives = true });
        Assert.Equal(ParaxialRecoveryTermination.Recovered, old.Termination); Assert.Equal(ParaxialRecoveryTermination.Recovered, automatic.Termination);
        Assert.Equal(0, automatic.Work.DifferenceEvaluations); Assert.True(automatic.Work.JacobianBuilds > 0);
        Assert.True(automatic.Work.FunctionEvaluations < old.Work.FunctionEvaluations);
        Assert.InRange(Math.Abs(automatic.EndpointAError!.Value), 0, plan.Options.Recovery.ATolerance);
        Assert.InRange(Math.Abs(automatic.EndpointFocalCError!.Value), 0, plan.Options.Recovery.FocalCTolerance);
        using var recorded = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(Root(), "validation/rechecks/2026-10-27/four-to-three-recovery/prepared/preparation.json")));
        var expected = recorded.RootElement.GetProperty("Deletions")[2].GetProperty("Recovery");
        Assert.Equal(expected.GetProperty("ProtocolFingerprint").GetString(), old.ProtocolFingerprint);
        Assert.NotEqual(old.ProtocolFingerprint, automatic.ProtocolFingerprint);
        var blocked = ParaxialRestorer.Run(parent, child, requests, plan.Targets with { MinimumEdgeThicknessMillimeters = 1000 },
            plan.Options.Recovery with { UseAutomaticMatrixDerivatives = true });
        Assert.Equal(ParaxialRecoveryTermination.GeometryBlocked, blocked.Termination); Assert.Equal(0, blocked.Work.JacobianBuilds);
        Assert.Equal(0, measurement.RayCount); Evidence("recovery-probes", new { old, automatic, blocked });
    }

    [Fact]
    public async Task RecoveryBudgetGuardDefersOptionalCheckAndPersistsTheOriginalAxialWindow()
    {
        var original = await Plan(); var plan = original with
        {
            Options = original.Options with
            {
                ProtectPostRecoveryRayBudget = true,
                Recovery = original.Options.Recovery with { UseAutomaticMatrixDerivatives = true }
            }
        };
        var directory = Path.Combine(Path.GetTempPath(), "recovery-budget-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "run.autonomous.json"); var store = new AutonomousSearchCheckpointStore();
        try
        {
            using var stop = new CancellationTokenSource();
            var paused = AutonomousSearchService.Run(AutonomousSearchService.Create("recovery-budget", plan), saved =>
            {
                store.SaveAsync(saved, path, saved.Revision - 1).AsTask().GetAwaiter().GetResult();
                if (saved.Events.LastOrDefault() is { Outcome: "StageValidationDeferred", Phase: AutonomousSearchPhase.Feasibility }
                    && saved.Pending?.Phase == AutonomousSearchPhase.Local) stop.Cancel();
            }, stop.Token);
            Evidence("budget-observed", paused);
            Assert.Equal(AutonomousSearchStatus.Cancelled, paused.Status); Assert.Equal(AutonomousSearchPhase.Local, paused.Pending!.Phase);
            var deferred = paused.Events.Last(item => item.Outcome == "StageValidationDeferred"); Assert.Equal(0, deferred.Work.TracedRays); Assert.Equal(0, deferred.Work.FunctionEvaluations);
            Assert.Equal(paused.Events.Single(item => item.Operation == "seed").Work.TracedRays, paused.Consumed.TracedRays);
            var loaded = await store.LoadAsync(path); Assert.NotNull(loaded.Pending!.AxialAnchors);
            Assert.Equal(ContentFingerprint.Compute(paused), ContentFingerprint.Compute(loaded));
            var protectedAxes = plan.Seeds[0].Elements.SelectMany(element => new[] { element.FrontSurfaceId, element.BackSurfaceId }).ToHashSet();
            Assert.All(loaded.Pending.AxialAnchors!.Value, anchor => Assert.Contains(anchor.SurfaceId, protectedAxes));
            Assert.All(loaded.Events.Where(item => item.Recovery is not null).SelectMany(item => item.Recovery!.Variables), variable =>
                Assert.DoesNotContain(variable.SurfaceId, new[] { plan.Seeds[0].Reference.Id, plan.Seeds[0].Stop.Id, plan.Seeds[0].Image.Id }));
            var axialPlan = plan with { Options = plan.Options with { UseAxialVariablesInRecovery = true } };
            var parent = loaded.Candidates.Single(candidate => candidate.Id == "seed-0");
            var edited = TopologyOperators.Delete(parent.Topology, "element-003", plan.Constraints).Topology!;
            var prepared = AutonomousSearchService.Create("recovery-permission", axialPlan) with
            {
                SeedCursor = 1,
                Candidates = [parent],
                Pool = [parent.Id],
                Pending = new()
                { TrialId = "trial-permission", ParentId = parent.Id, Operation = "delete", Template = edited, Phase = AutonomousSearchPhase.Recovery }
            };
            using var noDispatch = new CancellationTokenSource(); using var trace = SequentialTraceMeasurement.Begin();
            var permission = AutonomousSearchService.Run(prepared, saved => { if (saved.Reservation?.Phase == AutonomousSearchPhase.Recovery) noDispatch.Cancel(); }, noDispatch.Token);
            Assert.Equal(0, trace.RayCount);
            Assert.Contains(permission.Pending!.Variables, variable => variable.Kind == OpticalDesignParameterKind.AxialPosition);
            Assert.DoesNotContain(permission.Pending.Variables, variable => variable.SurfaceId.StartsWith("element-003", StringComparison.Ordinal));
            Evidence("budget-defer", loaded);
        }
        finally { File.Delete(path); File.Delete(path + ".lock"); Directory.Delete(directory); }
    }

    private static BoundTopologyParameters Bind(CompiledTopology source, AutonomousSearchPlan plan, bool axial)
    {
        var requests = new List<TopologyVariableRequest>(); var bound = plan.Options.CurvatureBoundPerMillimeter;
        foreach (var element in source.Topology.Elements.Where(element => !element.Protected))
            foreach (var (id, face, z) in new[] { (element.FrontSurfaceId, element.Front, element.FrontZMillimeters), (element.BackSurfaceId, element.Back, element.BackZMillimeters) })
            {
                if (face.Profile.Kind != TopologyProfileKind.Plane) requests.Add(new(id, OpticalDesignParameterKind.Curvature, -bound, bound, .01));
                if (axial) requests.Add(new(id, OpticalDesignParameterKind.AxialPosition, Math.Max(source.Topology.Reference.ZMillimeters, z - .25), Math.Min(source.Topology.Image.ZMillimeters, z + .25), .1));
            }
        return TopologyParameterBinder.Bind(source, requests);
    }

    [Fact]
    public async Task ConicCompensationIsSeparateFromBranchesAndItsOpticalTangentsMatchDifferences()
    {
        var recorded = await new AutonomousSearchCheckpointStore().LoadAsync(Path.Combine(Root(), "validation/rechecks/2026-10-27/four-to-three-recovery/pupil-runs/four-to-three-42-C-pupil/run.autonomous.json"));
        var parent = recorded.Candidates.Single(candidate => candidate.Id == "seed-0");
        var child = recorded.Candidates.Where(candidate => candidate.Topology.Elements.Length == 3).OrderBy(candidate => candidate.Score).First().Topology;
        var plan = recorded.Plan with { Options = recorded.Plan.Options with { CompensateConic = true } };
        Assert.False(plan.Options.Expansion!.EnableProfile);
        var initial = AutonomousSearchService.Create("conic-permission", plan) with
        {
            SeedCursor = 1,
            Candidates = [parent],
            Pool = [parent.Id],
            Pending = new()
            { TrialId = "trial-permission", ParentId = parent.Id, Operation = "delete", Template = child, Phase = AutonomousSearchPhase.Full }
        };
        using var stop = new CancellationTokenSource();
        AutonomousSearchCheckpoint paused;
        using (var zero = SequentialTraceMeasurement.Begin())
        {
            paused = AutonomousSearchService.Run(initial, saved => { if (saved.Reservation?.Phase == AutonomousSearchPhase.Full) stop.Cancel(); }, stop.Token);
            Assert.Equal(0, zero.RayCount);
        }
        var variables = paused.Pending!.Variables; Assert.Equal(18, variables.Length);
        Assert.Equal(6, variables.Count(variable => variable.Kind == OpticalDesignParameterKind.Conic));
        var source = TopologyCompiler.Compile(child, plan.Constraints); var binding = TopologyParameterBinder.Bind(source, variables);
        var firstOrder = DifferentiableParaxialMetrics.TransferMatrix(source.CreateOptic(), binding.CoreParameters,
            source.SurfaceIndices[child.Reference.Id], source.SurfaceIndices[child.Image.Id], includeInverseFNumber: true);
        var objective = new FixedTopologyObjective(source, binding, plan.Targets, plan.Options.Full.Sampling);
        var actual = objective.Evaluate(binding.InitialCoordinates, includeJacobian: true, includeConstraintTangents: true); Assert.True(actual.Available, actual.Failure);
        var rows = actual.ConstraintTangents!.Value.Where(row => objective.ResidualIds[row.Row].EndsWith(":rms", StringComparison.Ordinal)
            || objective.ResidualIds[row.Row].EndsWith(":maximum", StringComparison.Ordinal)).ToArray();
        var results = new List<object>();
        for (var column = 0; column < variables.Length; column++)
        {
            if (variables[column].Kind != OpticalDesignParameterKind.Conic) continue;
            Assert.Equal(0, firstOrder.A.Derivative(column)); Assert.Equal(0, firstOrder.C.Derivative(column)); Assert.Equal(0, firstOrder.InverseFNumber!.Value.Derivative(column));
            const double h = 1e-5;
            var plus = binding.InitialCoordinates.ToArray(); var minus = plus.ToArray(); plus[column] += h; minus[column] -= h;
            var a = objective.Evaluate(plus, includeConstraintTangents: true); var b = objective.Evaluate(minus, includeConstraintTangents: true);
            Assert.True(a.Available, a.Failure); Assert.True(b.Available, b.Failure);
            var maximumError = 0.0;
            foreach (var row in rows)
            {
                var difference = (a.ConstraintTangents!.Value.Single(item => item.Row == row.Row).Value - b.ConstraintTangents!.Value.Single(item => item.Row == row.Row).Value) / (2 * h);
                var error = Math.Abs(difference - row.Derivatives[column]); maximumError = Math.Max(maximumError, error);
                Assert.True(error < 2e-5 + 3e-4 * Math.Abs(difference));
            }
            results.Add(new { Variable = variables[column], MaximumError = maximumError });
        }
        Assert.Contains(rows, row => variables.Select((variable, index) => (variable, index)).Any(item => item.variable.Kind == OpticalDesignParameterKind.Conic && Math.Abs(row.Derivatives[item.index]) > 1e-5));
        Evidence("conic-gradients", results);
    }
    private static async Task<AutonomousSearchPlan> Plan() => await AutonomousDesignFiles.LoadPlanAsync(Path.Combine(Root(), "validation/rechecks/2026-10-27/four-to-three-recovery/prepared/baseline.topology-plan.json"));
    private static string Root() { var directory = new DirectoryInfo(AppContext.BaseDirectory); while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent; return directory!.FullName; }
    private static void Evidence(string name, object value)
    { if (Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_RECOVERY_EVIDENCE") is not { } directory) return; Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, name + ".json"), JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals })); }
}

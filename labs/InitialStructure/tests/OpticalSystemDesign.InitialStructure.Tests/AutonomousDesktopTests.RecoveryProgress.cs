using System.Text.Json;
using Avalonia.Controls;
using OpticalSystemDesign.App.Laboratories.InitialStructure;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;
using OpticalSystemDesign.Tests;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed partial class AutonomousDesktopTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualRecoveryProgressCancelsAtItsSafeEndpointAndDesktopResumeDoesNotReplayTheTrial(bool automatic)
    {
        using var files = new OwnedFiles();
        var plan = Plan();
        plan = plan with
        {
            Targets = plan.Targets with
            {
                FocalLengthRelativeTolerance = 1,
                FNumberRelativeTolerance = 1,
                MaximumRmsRadiusMillimeters = 5,
                MaximumSpotRadiusMillimeters = 10,
                MaximumDistortionFraction = 1,
                MinimumValidRayFraction = .1
            },
            Options = plan.Options with
            {
                AxialCompensation = new(),
                UseAxialVariablesInRecovery = true,
                CompensateConic = true,
                CheckStageFeasibility = true,
                ProtectPostRecoveryRayBudget = true,
                Recovery = new()
                {
                    MaximumFunctionEvaluations = 80,
                    ProfileRadialSamples = 8,
                    UseAutomaticMatrixDerivatives = automatic,
                    RestorePupilMagnification = automatic ? true : null
                },
                Expansion = new()
                {
                    EnableInsertion = false,
                    EnableDeletion = true,
                    EnableGradualDeletion = false,
                    EnableSplit = false,
                    EnableMerge = false,
                    EnableMaterial = false,
                    EnableProfile = false
                }
            }
        };
        var callbacks = new List<(string Trial, ParaxialRecoveryStep Step)>();
        var session = new AutonomousDesktopSession(files.Root);
        await session.RunAsync(plan, false, _ => { }, recoveryProgress: (pending, step) =>
        {
            Assert.Equal(AutonomousSearchPhase.Recovery, pending.Phase);
            callbacks.Add((pending.TrialId, step));
            session.Cancel();
        });
        var stopped = session.Checkpoint!;
        var evidence = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_TOPOLOGY_M6_EVIDENCE");
        if (!string.IsNullOrWhiteSpace(evidence))
        {
            Directory.CreateDirectory(evidence);
            await File.WriteAllBytesAsync(System.IO.Path.Combine(evidence, "recovery-" + automatic + "-attempt-" + Guid.NewGuid().ToString("N") + ".autonomous.json"),
                await File.ReadAllBytesAsync(session.RecordPath!));
        }
        Assert.Equal(AutonomousSearchStatus.Cancelled, stopped.Status);
        Assert.Null(stopped.Pending); Assert.Null(stopped.Reservation); Assert.Equal(1, stopped.TrialCursor);
        var callback = Assert.Single(callbacks); Assert.Equal(1, callback.Step.AcceptedStep);
        var entry = Assert.Single(stopped.Events, item => item.Phase == AutonomousSearchPhase.Recovery);
        var recovery = entry.Recovery!;
        Assert.Equal(callback.Trial, entry.TrialId); Assert.Equal(ParaxialRecoveryTermination.Cancelled, recovery.Termination);
        Assert.Equal(callback.Step, Assert.Single(recovery.Steps)); Assert.Equal(0, entry.Work.TracedRays);
        Assert.Equal(ContentFingerprint.Compute(recovery.Topology), ContentFingerprint.Compute(entry.Endpoint!));
        Assert.Equal(callback.Step.Coordinates.ToArray(), recovery.EndpointCoordinates.ToArray());
        Assert.Contains(recovery.Variables, item => item.Kind == OpticalDesignParameterKind.AxialPosition);
        Assert.DoesNotContain(recovery.Variables, item => item.Kind == OpticalDesignParameterKind.Conic);
        Assert.True(recovery.Work.MatrixEvaluations > 0); Assert.True(recovery.Work.JacobianBuilds > 0);
        Assert.True(automatic ? recovery.Work.DifferenceEvaluations == 0 : recovery.Work.DifferenceEvaluations > 0);
        if (automatic) Assert.True(recovery.Work.PupilEvaluations > 0);
        if (automatic)
        {
            var edited = TopologyOperators.Delete(plan.Seeds[0], entry.Change!.RemovedElementIds.Single(), plan.Constraints);
            using var noPhysicalRays = SequentialTraceMeasurement.Begin();
            var completed = ParaxialRestorer.Run(TopologyCompiler.Compile(plan.Seeds[0], plan.Constraints),
                TopologyCompiler.Compile(edited.Topology!, plan.Constraints), recovery.Variables, plan.Targets, plan.Options.Recovery);
            if (!string.IsNullOrWhiteSpace(evidence))
                await File.WriteAllTextAsync(System.IO.Path.Combine(evidence, "front-stop-pupil-full-current.json"), JsonSerializer.Serialize(completed, Json));
            Assert.Equal(2, completed.InitialJacobianRank); Assert.Equal(ParaxialRecoveryTermination.NoParaxialRecovery, completed.Termination);
            Assert.NotEmpty(completed.Steps);
            Assert.Contains(completed.Diagnostics, item => item.Contains("Recovery geometry is blocked", StringComparison.Ordinal));
            var original = plan.Seeds[0]; var element = original.Elements[1];
            var perturbed = original with
            {
                Elements = original.Elements.SetItem(1, element with
                { Front = element.Front with { Profile = element.Front.Profile with { CurvaturePerMillimeter = element.Front.Profile.CurvaturePerMillimeter + 1e-4 } } })
            };
            var localRecovery = ParaxialRestorer.Run(TopologyCompiler.Compile(original, plan.Constraints),
                TopologyCompiler.Compile(perturbed, plan.Constraints), recovery.Variables, plan.Targets, plan.Options.Recovery);
            if (!string.IsNullOrWhiteSpace(evidence))
                await File.WriteAllTextAsync(System.IO.Path.Combine(evidence, "front-stop-pupil-perturbation.json"), JsonSerializer.Serialize(localRecovery, Json));
            Assert.Equal(2, localRecovery.InitialJacobianRank); Assert.Equal(ParaxialRecoveryTermination.Recovered, localRecovery.Termination);
            Assert.InRange(Math.Abs(localRecovery.EndpointAError!.Value), 0, plan.Options.Recovery.ATolerance);
            Assert.InRange(Math.Abs(localRecovery.EndpointFocalCError!.Value), 0, plan.Options.Recovery.FocalCTolerance);
            Assert.InRange(Math.Abs(localRecovery.EndpointPupilError!.Value), 0, plan.Options.Recovery.FocalCTolerance);
            Assert.Equal(0, noPhysicalRays.RayCount);
        }
        Assert.Single(stopped.AcceptedArchive); Assert.All(stopped.AcceptedArchive, item => Assert.Equal(3, item.Topology.Elements.Length));
        var path = session.RecordPath!;
        Assert.Equal(ContentFingerprint.Compute(stopped), ContentFingerprint.Compute(await new AutonomousSearchCheckpointStore().LoadAsync(path)));
        using var host = SafeHeadlessUnitTestSession.StartNew(typeof(FlatStartDesktopTests));
        await host.Dispatch(async () =>
        {
            var window = new AutonomousTopologyWindow(new() { InitialPlan = plan, OpenPicker = (_, _) => Task.FromResult<string?>(path) });
            try
            {
                window.Show(); window.UpdateLayout();
                using var noWork = SequentialTraceMeasurement.Begin();
                Click(window, "AutonomousOpenRecord"); await window.PendingOperation;
                Assert.True(Find<Button>(window, "AutonomousResume").IsEnabled);
                Click(window, "AutonomousResume"); await window.PendingOperation;
                var final = window.CurrentCheckpoint!;
                Assert.Equal(AutonomousSearchStatus.Completed, final.Status);
                Assert.Equal(stopped.Consumed with { Elapsed = TimeSpan.Zero }, final.Consumed with { Elapsed = TimeSpan.Zero });
                Assert.Equal(ContentFingerprint.Compute(stopped.Events), ContentFingerprint.Compute(final.Events));
                Assert.Equal(ContentFingerprint.Compute(stopped.AcceptedArchive), ContentFingerprint.Compute(final.AcceptedArchive));
                Assert.Equal(stopped.PlanFingerprint, final.PlanFingerprint); Assert.Equal(0, noWork.RayCount);
                Assert.Equal(ContentFingerprint.Compute(final), ContentFingerprint.Compute(await new AutonomousSearchCheckpointStore().LoadAsync(path)));
                await Task.Delay(20); window.UpdateLayout();
                Assert.Equal(UiLocalization.Text("自主任务结束，已保存达标结果"), Find<TextBlock>(window, "AutonomousStatus").Text);
                if (!string.IsNullOrWhiteSpace(evidence))
                {
                    await File.WriteAllTextAsync(System.IO.Path.Combine(evidence, "recovery-" + automatic + "-cancelled.json"), JsonSerializer.Serialize(stopped, Json));
                    await File.WriteAllBytesAsync(System.IO.Path.Combine(evidence, "recovery-" + automatic + "-resumed.autonomous.json"), await File.ReadAllBytesAsync(path));
                }
                return true;
            }
            finally { window.RequestSafeClose(); await window.PendingOperation; }
        }, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(1));
    }
}

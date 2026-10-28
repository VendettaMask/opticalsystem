using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using OpticalSystemDesign.App.Laboratories.InitialStructure;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;
using OpticalSystemDesign.Tests;

namespace OpticalSystemDesign.InitialStructure.Tests;

[Collection(LabDesktopCollection.Name)]
public sealed partial class AutonomousDesktopTests
{
    private static readonly JsonSerializerOptions Json = new() { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };

    [Fact]
    public async Task PlanChecksumReadOnlyLoadFiniteImportAndCoreExportAreVerified()
    {
        using var files = new OwnedFiles();
        var plan = Plan(); var path = files.Path("task.topology-plan.json");
        await AutonomousDesignFiles.SavePlanAsync(plan, path);
        using (var trace = SequentialTraceMeasurement.Begin())
        {
            Assert.Equal(ContentFingerprint.Compute(plan), ContentFingerprint.Compute(await AutonomousDesignFiles.LoadPlanAsync(path)));
            Assert.Equal(0, trace.RayCount);
        }
        var bytes = await File.ReadAllTextAsync(path);
        await File.WriteAllTextAsync(path, bytes.Replace("\"sha256\":\"", "\"sha256\":\"tampered"));
        await Assert.ThrowsAsync<InvalidDataException>(() => AutonomousDesignFiles.LoadPlanAsync(path));
        var completed = AutonomousSearchService.Run(AutonomousSearchService.Create("desktop-export", plan));
        var candidate = completed.Candidates.First();
        Assert.False(candidate.Assessment.MeetsTargets);
        var opticPath = await AutonomousDesignFiles.ExportAsync(candidate, plan.Constraints, files.Path("candidate.staropt"));
        var project = await StarOptProjectStore.LoadAsync(opticPath);
        Assert.Equal(candidate.Assessment.OpticFingerprint, ContentFingerprint.Compute(project.Configurations[0].ToSnapshot()));
        var imported = await AutonomousDesignFiles.ImportSeedAsync(opticPath, plan.Constraints);
        Assert.Equal(candidate.Topology.Elements.Length, imported.Elements.Length);
        var finite = candidate.Topology with { Conjugate = ObjectConjugateMode.Finite };
        await File.WriteAllTextAsync(files.Path("finite.topology.json"), JsonSerializer.Serialize(finite, Json));
        var exception = await Assert.ThrowsAsync<TopologyValidationException>(() => AutonomousDesignFiles.ImportSeedAsync(files.Path("finite.topology.json"), plan.Constraints));
        Assert.Equal(TopologyFailureCode.UnsupportedConjugate, exception.Code);
        await Assert.ThrowsAsync<InvalidDataException>(() => AutonomousDesignFiles.ExportAsync(candidate with { Topology = candidate.Topology with { Image = candidate.Topology.Image with { ZMillimeters = 56 } } }, plan.Constraints, opticPath));
        Assert.Equal(candidate.Assessment.OpticFingerprint, ContentFingerprint.Compute((await StarOptProjectStore.LoadAsync(opticPath)).Configurations[0].ToSnapshot()));
    }

    [Fact]
    public async Task DesktopLoadsWithoutRunningPreservesPlanIdentityLocalizesAndRejectsUnsupportedInput()
    {
        using var files = new OwnedFiles();
        var plan = Plan();
        await AutonomousDesignFiles.SavePlanAsync(plan, files.Path("task.topology-plan.json"));
        await File.WriteAllTextAsync(files.Path("seed.topology.json"), JsonSerializer.Serialize(plan.Seeds[0] with { Conjugate = ObjectConjugateMode.Finite }, Json));
        using var host = SafeHeadlessUnitTestSession.StartNew(typeof(FlatStartDesktopTests));
        await host.Dispatch(async () =>
        {
            var originalLanguage = UiLocalization.CurrentLanguage;
            var window = new AutonomousTopologyWindow(new()
            {
                RunDirectory = files.Root,
                InitialPlan = plan,
                OpenPicker = (_, kind) => Task.FromResult<string?>(files.Path(kind == "plan" ? "task.topology-plan.json" : "seed.topology.json")),
                SavePicker = (_, _) => Task.FromResult<string?>(files.Path("saved.topology-plan.json"))
            });
            try
            {
                window.Show(); window.UpdateLayout();
                using (var trace = SequentialTraceMeasurement.Begin())
                {
                    Click(window, "AutonomousOpenPlan"); await window.PendingOperation;
                    Click(window, "AutonomousCheck"); await window.PendingOperation;
                    Click(window, "AutonomousSavePlan"); await window.PendingOperation;
                    Assert.Equal(ContentFingerprint.Compute(plan), ContentFingerprint.Compute(await AutonomousDesignFiles.LoadPlanAsync(files.Path("saved.topology-plan.json"))));
                    Assert.Null(window.CurrentCheckpoint); Assert.Equal(0, trace.RayCount);
                }
                Click(window, "AutonomousImport"); await window.PendingOperation;
                Assert.NotEmpty(Find<TextBlock>(window, "AutonomousError").Text!);
                Assert.Null(window.CurrentCheckpoint);
                foreach (var language in new[] { UiLocalization.Chinese, UiLocalization.English, UiLocalization.Japanese })
                {
                    UiLocalization.Apply(language); window.UpdateLayout();
                    Assert.Equal(UiLocalization.Text("开始自主设计"), Find<Button>(window, "AutonomousRun").Content);
                    Assert.Contains("accent", Find<Button>(window, "AutonomousRun").Classes);
                    Assert.DoesNotContain("accent", Find<Button>(window, "AutonomousCancel").Classes);
                    Assert.NotNull(ToolTip.GetTip(Find<Button>(window, "AutonomousResume")));
                    Capture(window, "wide-" + language);
                }
                window.Width = 480; await Task.Delay(30); window.UpdateLayout();
                Click(window, "AutonomousCheck"); await window.PendingOperation;

                Assert.Empty(Find<TextBlock>(window, "AutonomousError").Text!);
                var input = Find<NumericUpDown>(window, "AutonomousSeed"); input.BringIntoView(); window.UpdateLayout();
                var point = input.TranslatePoint(new(input.Bounds.Width / 2, input.Bounds.Height / 2), window)!.Value;

                Assert.InRange(point.Y, 0, window.Bounds.Height);
                Capture(window, "narrow-ja-JP");
                // Invalid uncommitted text must not silently use the last numeric value.
                input.Text = "invalid";
                Click(window, "AutonomousCheck"); await window.PendingOperation;
                Assert.NotEmpty(Find<TextBlock>(window, "AutonomousError").Text!);
                return true;
            }
            finally { window.RequestSafeClose(); await window.PendingOperation; UiLocalization.Apply(originalLanguage); }
        }, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(2));
    }

    [Fact]
    public async Task DesktopStopsSavesLoadsResumesAndExportsActualUnqualifiedEndpoint()
    {
        using var files = new OwnedFiles();
        using var host = SafeHeadlessUnitTestSession.StartNew(typeof(FlatStartDesktopTests));
        await host.Dispatch(async () =>
        {
            string? record = null;
            var plan = Plan() with
            {
                Options = Plan().Options with
                {
                    MaximumTrials = 2,
                    Full = Plan().Options.Full with { MaximumUpdates = 100, MaximumFunctionEvaluations = 101, MaximumGradientEvaluations = 101 }
                }
            };
            var window = new AutonomousTopologyWindow(new()
            {
                RunDirectory = files.Root,
                InitialPlan = plan,
                OpenPicker = (_, _) => Task.FromResult(record),
                SavePicker = (_, kind) => Task.FromResult<string?>(files.Path(kind == "optic" ? "ui-candidate.staropt" : "copy.autonomous.json"))
            });
            try
            {
                window.Show(); Click(window, "AutonomousRun");
                await Until(() => window.CurrentCheckpoint?.Candidates.Length > 0);
                Assert.True(window.IsBusy);
                Click(window, "AutonomousCancel"); await window.PendingOperation;
                var stopped = window.CurrentCheckpoint!;
                Assert.Equal(AutonomousSearchStatus.Cancelled, stopped.Status);
                Assert.False(window.IsBusy);
                record = Find<TextBlock>(window, "AutonomousRecordPath").Text!;
                var stored = await new AutonomousSearchCheckpointStore().LoadAsync(record);
                Assert.Equal(ContentFingerprint.Compute(stopped), ContentFingerprint.Compute(stored));
                Click(window, "AutonomousCopy"); await window.PendingOperation;
                Assert.Equal(await File.ReadAllBytesAsync(record), await File.ReadAllBytesAsync(files.Path("copy.autonomous.json")));
                Click(window, "AutonomousCopy"); await window.PendingOperation;
                Assert.NotEmpty(Find<TextBlock>(window, "AutonomousError").Text!);
                Assert.Equal(await File.ReadAllBytesAsync(record), await File.ReadAllBytesAsync(files.Path("copy.autonomous.json")));
                Click(window, "AutonomousNew"); await window.PendingOperation;
                using (var trace = SequentialTraceMeasurement.Begin())
                {
                    Click(window, "AutonomousOpenRecord"); await window.PendingOperation;
                    Assert.False(window.IsBusy); Assert.Equal(0, trace.RayCount);
                    Assert.Equal(stopped.Revision, window.CurrentCheckpoint!.Revision);
                }
                Assert.False(Find<NumericUpDown>(window, "AutonomousFocal").IsEnabled);
                Assert.True(Find<Button>(window, "AutonomousResume").IsEnabled);
                Click(window, "AutonomousResume"); await window.PendingOperation;
                var final = window.CurrentCheckpoint!;
                Assert.True(final.Revision > stopped.Revision);
                Assert.True(final.Consumed.TracedRays >= stopped.Consumed.TracedRays);
                Assert.Equal(ContentFingerprint.Compute(stopped.Events), ContentFingerprint.Compute(final.Events.Take(stopped.Events.Length).ToImmutableArray()));
                Assert.Contains(final.Status, new[] { AutonomousSearchStatus.Completed, AutonomousSearchStatus.BudgetExhausted });
                Click(window, "AutonomousExport"); await window.PendingOperation;
                Assert.Empty(Find<TextBlock>(window, "AutonomousError").Text!);
                var selected = (TopologyCandidateRow)Find<DataGrid>(window, "AutonomousCandidates").SelectedItem!;
                var restored = await StarOptProjectStore.LoadAsync(files.Path("ui-candidate.staropt"));
                Assert.Equal(selected.Candidate.Assessment.OpticFingerprint, ContentFingerprint.Compute(restored.Configurations[0].ToSnapshot()));
                Assert.False(selected.Candidate.Assessment.MeetsTargets);
                Assert.Equal(UiLocalization.Text("未达标候选已导出"), Find<TextBlock>(window, "AutonomousStatus").Text);
                Capture(window, "endpoint-failed");
                return true;
            }
            finally { window.RequestSafeClose(); await window.PendingOperation; }
        }, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void ComparisonPoliciesKeepBackwardIdentityAndDistinctDispatch()
    {
        var plan = Plan();
        var serialized = JsonSerializer.Serialize(plan, Json);
        Assert.DoesNotContain("FixedTopologyOnly", serialized); Assert.DoesNotContain("SkipParaxialRecovery", serialized);
        var fixedOnly = AutonomousSearchService.Run(AutonomousSearchService.Create("m6-A", plan with { Options = plan.Options with { MaximumTrials = 3, FixedTopologyOnly = true } }));
        Assert.All(fixedOnly.Candidates, item => Assert.Equal(plan.Seeds[0].Elements.Length, item.Topology.Elements.Length));
        Assert.DoesNotContain(fixedOnly.Events, item => item.Change is not null || item.Recovery is not null);
        var skipped = AutonomousSearchService.Run(AutonomousSearchService.Create("m6-B", plan with { Options = plan.Options with { MaximumTrials = 4, SkipParaxialRecovery = true } }));
        Assert.Contains(skipped.Events, item => item.Outcome == "RecoverySkipped" && item.Work.MatrixEvaluations == 0);
        Assert.DoesNotContain(skipped.Events, item => item.Recovery is not null);
        Assert.Equal(0, skipped.Consumed.MatrixEvaluations);
        Assert.Throws<ArgumentException>(() => AutonomousSearchService.Create("invalid", plan with { Options = plan.Options with { SkipParaxialRecovery = true, Expansion = new() } }));
    }

    [Fact]
    public async Task LegacyEntryOpensReadOnlyChildWithoutChangingUpstreamDefaults()
    {
        using var files = new OwnedFiles();
        using var host = SafeHeadlessUnitTestSession.StartNew(typeof(FlatStartDesktopTests));
        await host.Dispatch(async () =>
        {
            var owner = new MainWindow(new LabDesktopSettings { RunDirectory = files.Root });
            try
            {
                owner.Show(); await owner.Ready;
                using var trace = SequentialTraceMeasurement.Begin();
                Click(owner, "OpenAutonomousTopology");
                var child = Assert.Single(owner.OwnedWindows.OfType<AutonomousTopologyWindow>());
                Assert.Null(child.CurrentCheckpoint); Assert.False(child.IsBusy);
                Assert.Equal(0, trace.RayCount);
                Assert.Equal(5m, Find<NumericUpDown>(owner, "FocalLength").Value);
                Assert.Equal(1.8m, Find<NumericUpDown>(owner, "FNumber").Value);
                Assert.Equal(UiLocalization.Text("AutoLens · C# 自动求导"), Find<TextBlock>(owner, "OptimizationMethod").Text);
                child.Close(); Assert.Empty(owner.OwnedWindows);
                return true;
            }
            finally { owner.Close(); await owner.PendingOperation; }
        }, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(2));
    }

    [Fact]
    public async Task ClosingDuringWorkWaitsForDurableCancellation()
    {
        using var files = new OwnedFiles();
        using var host = SafeHeadlessUnitTestSession.StartNew(typeof(FlatStartDesktopTests));
        await host.Dispatch(async () =>
        {
            var plan = Plan() with
            {
                Options = Plan().Options with
                {
                    MaximumTrials = 4,
                    Full = Plan().Options.Full with { MaximumUpdates = 100, MaximumFunctionEvaluations = 101, MaximumGradientEvaluations = 101 }
                }
            };
            var window = new AutonomousTopologyWindow(new() { InitialPlan = plan, RunDirectory = files.Root });
            window.Show(); Click(window, "AutonomousRun");
            await Until(() => window.CurrentCheckpoint?.Candidates.Length > 0);
            window.Close();
            await window.PendingOperation;
            Assert.False(window.IsVisible); Assert.False(window.IsBusy);
            var record = Assert.Single(Directory.GetFiles(files.Root, "*.autonomous.json"));
            var checkpoint = await new AutonomousSearchCheckpointStore().LoadAsync(record);
            Assert.Equal(AutonomousSearchStatus.Cancelled, checkpoint.Status);
            Assert.Equal(ContentFingerprint.Compute(window.CurrentCheckpoint!), ContentFingerprint.Compute(checkpoint));
            return true;
        }, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(2));
    }

    [Fact]
    public async Task FailedSaveKeepsWindowOpenAndShowsFailure()
    {
        using var files = new OwnedFiles();
        using var host = SafeHeadlessUnitTestSession.StartNew(typeof(FlatStartDesktopTests));
        await host.Dispatch(async () =>
        {
            var plan = Plan() with
            {
                Options = Plan().Options with
                {
                    MaximumTrials = 4,
                    Full = Plan().Options.Full with { MaximumUpdates = 100, MaximumFunctionEvaluations = 101, MaximumGradientEvaluations = 101 }
                }
            };
            var window = new AutonomousTopologyWindow(new() { InitialPlan = plan, RunDirectory = files.Root });
            try
            {
                window.Show(); Click(window, "AutonomousRun");
                await Until(() => window.CurrentCheckpoint?.Candidates.Length > 0);
                var record = Assert.Single(Directory.GetFiles(files.Root, "*.autonomous.json"));
                await Until(() => window.CurrentCheckpoint?.Pending?.Phase == AutonomousSearchPhase.Full);
                // The storage lease is held by this test only. A normal stop must surface the failed write.
                using (var lease = new FileStream(record + ".lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    window.Close(); await window.PendingOperation;
                    Assert.True(window.IsVisible); Assert.False(window.IsBusy);
                    Assert.NotEmpty(Find<TextBlock>(window, "AutonomousError").Text!);
                }
                Assert.NotNull(await new AutonomousSearchCheckpointStore().LoadAsync(record));
                return true;
            }
            finally { window.RequestSafeClose(); await window.PendingOperation; }
        }, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(2));
    }

    [Fact]
    public async Task PrecisionAndAxialEditorAreExplicitAndBudgetEditsPreserveImportedSampling()
    {
        using var files = new OwnedFiles();
        using var host = SafeHeadlessUnitTestSession.StartNew(typeof(FlatStartDesktopTests));
        await host.Dispatch(async () =>
        {
            var plan = Plan();
            plan = plan with
            {
                Options = plan.Options with
                {
                    AxialCompensation = null,
                    Local = plan.Options.Local with { Sampling = plan.Options.Local.Sampling with { PupilRings = 2 } },
                    Full = plan.Options.Full with { Sampling = plan.Options.Full.Sampling with { PupilRings = 2 } }
                }
            };
            var saved = 0;
            var window = new AutonomousTopologyWindow(new() { InitialPlan = plan, SavePicker = (_, _) => Task.FromResult<string?>(files.Path("sampling-" + saved++ + ".topology-plan.json")) });
            try
            {
                window.Show(); window.UpdateLayout();
                using var trace = SequentialTraceMeasurement.Begin();
                Assert.False(Find<CheckBox>(window, "AutonomousPrecision").IsChecked);
                Assert.False(Find<CheckBox>(window, "AutonomousAxial").IsChecked);
                Find<NumericUpDown>(window, "AutonomousTrials").Value = plan.Options.MaximumTrials + 1;
                Click(window, "AutonomousSavePlan"); await window.PendingOperation;
                var budgetOnly = await AutonomousDesignFiles.LoadPlanAsync(files.Path("sampling-0.topology-plan.json"));
                Assert.Equal(plan.Options.Full.Sampling, budgetOnly.Options.Full.Sampling); Assert.Null(budgetOnly.Options.AxialCompensation);
                Find<CheckBox>(window, "AutonomousPrecision").IsChecked = true;
                Find<CheckBox>(window, "AutonomousAxial").IsChecked = true;
                Find<NumericUpDown>(window, "AutonomousAxialWindow").Value = .2m;
                Click(window, "AutonomousSavePlan"); await window.PendingOperation;
                var upgraded = await AutonomousDesignFiles.LoadPlanAsync(files.Path("sampling-1.topology-plan.json"));
                Assert.Equal(4, upgraded.Options.Full.Sampling.PupilRings); Assert.Equal(16, upgraded.Options.Full.Sampling.PupilArms);
                Assert.Equal(.2, upgraded.Options.AxialCompensation!.MaximumDisplacementMillimeters);
                Assert.Equal(plan.Targets, upgraded.Targets); Assert.Equal(ContentFingerprint.Compute(plan.Seeds), ContentFingerprint.Compute(upgraded.Seeds));
                Assert.Null(window.CurrentCheckpoint); Assert.Equal(0, trace.RayCount);
                Assert.Equal(UiLocalization.Text("高精度训练采样"), Find<CheckBox>(window, "AutonomousPrecision").Content);
            }
            finally { window.RequestSafeClose(); await window.PendingOperation; }
            return true;
        }, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task QualifiedReductionControlPersistsPermissionsAndExplainsVerifiedCountInThreeLanguages()
    {
        using var files = new OwnedFiles();
        using var host = SafeHeadlessUnitTestSession.StartNew(typeof(FlatStartDesktopTests));
        await host.Dispatch(async () =>
        {
            var language = UiLocalization.CurrentLanguage;
            var plan = Plan();
            plan = plan with { Options = plan.Options with { Expansion = new() } };
            var saved = 0;
            var window = new AutonomousTopologyWindow(new() { InitialPlan = plan,
                SavePicker = (_, _) => Task.FromResult<string?>(files.Path("reduction-" + saved++ + ".topology-plan.json")) });
            try
            {
                window.Show(); window.UpdateLayout();
                using var trace = SequentialTraceMeasurement.Begin();
                Assert.False(Find<CheckBox>(window, "AutonomousReductionOnly").IsChecked);
                Find<CheckBox>(window, "AutonomousReductionOnly").IsChecked = true;
                foreach (var id in new[] { "AutonomousSplit", "AutonomousMerge", "AutonomousMaterial", "AutonomousProfile" })
                {
                    Assert.False(Find<CheckBox>(window, id).IsChecked);
                    Assert.False(Find<CheckBox>(window, id).IsEnabled);
                }
                Assert.True(Find<CheckBox>(window, "AutonomousStructure").IsChecked);
                Click(window, "AutonomousSavePlan"); await window.PendingOperation;
                var reduction = await AutonomousDesignFiles.LoadPlanAsync(files.Path("reduction-0.topology-plan.json"));
                Assert.True(reduction.Options.QualifiedReductionOnly);
                Assert.False(reduction.Options.Expansion!.EnableInsertion);
                Assert.True(reduction.Options.Expansion.EnableDeletion);
                Assert.Equal(plan.Targets, reduction.Targets);
                Assert.Equal(ContentFingerprint.Compute(plan.Seeds), ContentFingerprint.Compute(reduction.Seeds));
                Find<NumericUpDown>(window, "AutonomousTrials").Value = plan.Options.MaximumTrials + 1;
                Click(window, "AutonomousSavePlan"); await window.PendingOperation;
                var edited = await AutonomousDesignFiles.LoadPlanAsync(files.Path("reduction-1.topology-plan.json"));
                Assert.True(edited.Options.QualifiedReductionOnly);
                Assert.Equal(ContentFingerprint.Compute(reduction.Options.Expansion!), ContentFingerprint.Compute(edited.Options.Expansion!));
                var state = AutonomousSearchService.Create("reduction-status-ui", reduction);
                foreach (var selected in new[] { UiLocalization.Chinese, UiLocalization.English, UiLocalization.Japanese })
                {
                    UiLocalization.Apply(selected); window.UpdateLayout();
                    Assert.Equal(UiLocalization.Text("仅从达标处方减片"), Find<CheckBox>(window, "AutonomousReductionOnly").Content);
                    var text = AutonomousTopologyWindow.ReductionConclusionText(state);
                    Assert.Contains(UiLocalization.Text("最少已验证片数"), text);
                    Assert.Contains(UiLocalization.Text("不代表全局最少片数"), text);
                    Assert.Contains(UiLocalization.Text("尚无达标处方"), text);
                }
                Assert.Equal(0, trace.RayCount);
            }
            finally { window.RequestSafeClose(); await window.PendingOperation; UiLocalization.Apply(language); }
            return true;
        }, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30));
    }

    private static AutonomousSearchPlan Plan()
    {
        var source = AutonomousDesignExamples.ThreeElement();
        var stage = source.Options.Full with
        {
            TransmissionFirst = null,
            TransmissionPolicyVersion = null,
            ActiveParameterLimit = null,
            MaximumUpdates = 2,
            MaximumFunctionEvaluations = 3,
            MaximumGradientEvaluations = 3,
            Sampling = new() { FieldsPerDirection = 3, PupilRings = 1, PupilArms = 8, ProfileRadialSamples = 8 }
        };
        return source with
        {
            Seeds = [source.Seeds[0] with { Fields = [new("Axis", 0, 0)] }],
            Targets = source.Targets with { MaximumRmsRadiusMillimeters = 1e-9 },
            Options = source.Options with
            {
                AxialCompensation = null,
                MaximumTrials = 1,
                MaximumFunctionEvaluations = 800,
                MaximumGradientEvaluations = 240,
                TimeLimit = TimeSpan.FromSeconds(30),
                Expansion = null,
                Local = stage,
                Full = stage
            }
        };
    }
    private static T Find<T>(Window window, string name) where T : Control => Assert.Single(window.GetLogicalDescendants().OfType<T>().Distinct(), item => item.Name == name);
    private static void Click(Window window, string name)
    {
        var button = Find<Button>(window, name); Assert.True(button.IsEffectivelyEnabled, name);
        button.BringIntoView(); window.UpdateLayout();
        var point = button.TranslatePoint(new(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        Assert.InRange(point.X, 0, window.Bounds.Width);
        Assert.InRange(point.Y, 0, window.Bounds.Height);
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
    }
    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!condition()) { Assert.True(DateTime.UtcNow < deadline, "Desktop workflow timed out."); await Task.Delay(10); }
    }
    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_TOPOLOGY_M6_EVIDENCE");
        if (string.IsNullOrWhiteSpace(directory)) return;
        window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Directory.CreateDirectory(directory); using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
        frame.Save(System.IO.Path.Combine(directory, name + ".png"), PngBitmapEncoderOptions.Default);
    }
    private sealed class OwnedFiles : IDisposable
    {
        public string Root { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "autonomous-m6-" + Guid.NewGuid().ToString("N"));
        public OwnedFiles() => Directory.CreateDirectory(Root);
        public string Path(string name) => System.IO.Path.Combine(Root, name);
        public void Dispose() => Directory.Delete(Root, true);
    }
}

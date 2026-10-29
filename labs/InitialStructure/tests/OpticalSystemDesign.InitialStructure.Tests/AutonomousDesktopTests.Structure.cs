using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Laboratories.InitialStructure;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Visualization;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.Modules;
using OpticalSystemDesign.Tests;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed class AutonomousStructurePresentationTests
{
    [Fact]
    public async Task StructureDisplayUsesActualSharedCoreLayoutAndSeparateBoundedTraceAccounting()
    {
        var (plan, _) = await DelanoMaterializationTests.Wide(); var fingerprint = ContentFingerprint.Compute(plan);
        var display = InitialStructurePresentation.BuildTopologyScene(plan.Seeds[0], plan.Constraints);
        var source = TopologyCompiler.Compile(plan.Seeds[0], plan.Constraints);
        var expected = WorkbenchMapper.ToScene2Dto(new Layout2DBuilder(source.CreateOptic()).Build(
            options: new LayoutBuildOptions(RayCount: 7, LowerPupil: -1, UpperPupil: 1, DeleteVignetted: false)));
        Assert.Equal(ContentFingerprint.Compute(expected), ContentFingerprint.Compute(display.Scene));
        Assert.Equal(source.OpticFingerprint, display.OpticFingerprint);
        Assert.Equal(3, display.Scene.LensElements.Count); Assert.Equal(21, display.Scene.Rays.Count);
        Assert.Equal(3, display.Scene.Rays.Select(r => r.FieldIndex).Distinct().Count());
        Assert.InRange(display.DisplayTracedRays, 1, 50_000); Assert.Equal(fingerprint, ContentFingerprint.Compute(plan));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        using var rays = SequentialTraceMeasurement.Begin();
        Assert.Throws<OperationCanceledException>(() => InitialStructurePresentation.BuildTopologyScene(plan.Seeds[0], plan.Constraints, cancelled.Token));
        Assert.Equal(0, rays.RayCount);
    }
}

public sealed partial class AutonomousDesktopTests
{
    [Fact]
    public async Task StructureTabShowsActualSelectedCandidatesAndSupportsNavigationWithoutChangingTheSearchRecord()
    {
        using var files = new OwnedFiles(); using var host = SafeHeadlessUnitTestSession.StartNew(typeof(FlatStartDesktopTests));
        await host.Dispatch(async () =>
        {
            var language = UiLocalization.CurrentLanguage; UiLocalization.Apply(UiLocalization.Chinese);
            var record = Path.Combine(DelanoMaterializationTests.Root(), "validation/rechecks/2026-10-29/delano-thick-materialization/runs/thick-delano/run.autonomous.json");
            var window = new AutonomousTopologyWindow(new() { RunDirectory = files.Root, InitialPlan = Plan(), OpenPicker = (_, _) => Task.FromResult<string?>(record) });
            try
            {
                window.Show();
                using (var load = SequentialTraceMeasurement.Begin())
                { Click(window, "AutonomousOpenRecord"); await window.PendingOperation; Assert.Equal(0, load.RayCount); }
                var state = ContentFingerprint.Compute(window.CurrentCheckpoint!);
                var tabs = Find<TabControl>(window, "AutonomousTabs");
                var tab = Find<TabItem>(window, "AutonomousStructureTab"); tabs.SelectedItem = tab;
                await window.StructurePendingLoad; window.UpdateLayout();
                var plot = Find<TopologyStructurePlot>(window, "AutonomousStructurePlot");
                Assert.NotNull(plot.Data); var data = plot.Data;
                var selected = (TopologyCandidateRow)Find<DataGrid>(window, "AutonomousCandidates").SelectedItem!;
                Assert.Equal(selected.Candidate.Assessment.OpticFingerprint, data.OpticFingerprint);
                Assert.Equal(state, ContentFingerprint.Compute(window.CurrentCheckpoint!));
                using (var navigation = SequentialTraceMeasurement.Begin())
                {
                    plot.BringIntoView(); window.UpdateLayout();
                    var point = plot.TranslatePoint(new Point(plot.Bounds.Width / 2, plot.Bounds.Height / 2), window)!.Value;
                    window.MouseWheel(point, new Vector(0, 1)); Assert.True(plot.Zoom > 1);
                    window.MouseDown(point, MouseButton.Left); window.MouseMove(point + new Vector(30, 12)); window.MouseUp(point + new Vector(30, 12), MouseButton.Left);
                    Assert.NotEqual(default(Vector), plot.Pan);
                    Click(window, "AutonomousStructureFit"); Assert.Equal(1, plot.Zoom); Assert.Equal(default, plot.Pan);
                    Click(window, "AutonomousStructurePlus"); Assert.True(plot.Zoom > 1);
                    foreach (var locale in new[] { UiLocalization.Chinese, UiLocalization.English, UiLocalization.Japanese })
                    {
                        UiLocalization.Apply(locale); window.UpdateLayout();
                        Assert.Equal(UiLocalization.Text("镜头结构"), tab.Header);
                        Assert.Same(data, plot.Data); Assert.True(plot.Zoom > 1);
                    }
                    Assert.Equal(0, navigation.RayCount);
                }
                UiLocalization.Apply(UiLocalization.Chinese); Click(window, "AutonomousStructureFit");
                Capture(window, "structure-wide");
                tabs.SelectedIndex = 0;
                var list = Find<DataGrid>(window, "AutonomousCandidates");
                var failed = list.ItemsSource!.Cast<TopologyCandidateRow>().Single(r => r.Candidate.Id == "seed-1");
                using (var select = SequentialTraceMeasurement.Begin()) { list.SelectedItem = failed; Assert.Null(plot.Data); Assert.Equal(0, select.RayCount); }
                tabs.SelectedItem = tab; await window.StructurePendingLoad;
                Assert.Equal(failed.Candidate.Assessment.OpticFingerprint, plot.Data!.OpticFingerprint);
                Assert.Contains(UiLocalization.Text("未达标"), Find<TextBlock>(window, "AutonomousStructureTitle").Text!);
                window.Width = 480; window.UpdateLayout(); plot.BringIntoView(); await Task.Delay(20); window.UpdateLayout();
                var page = plot.GetVisualAncestors().OfType<ScrollViewer>().Last();
                var content = (Control)page.Content!;
                var position = plot.TranslatePoint(default, content)!.Value;
                page.Offset = new Vector(0, Math.Clamp(position.Y - 100, 0, Math.Max(0, page.Extent.Height - page.Viewport.Height)));
                window.UpdateLayout();
                Assert.InRange(plot.Bounds.Width, 100, 480); Assert.True(plot.Bounds.Height >= 260);
                Capture(window, "structure-narrow");
                window.Width = 1240; window.UpdateLayout();
                Click(window, "AutonomousStructureRefresh"); var pending = window.StructurePendingLoad;
                Click(window, "AutonomousNew"); await window.PendingOperation; await pending;
                Assert.Null(plot.Data); Assert.Null(window.CurrentCheckpoint);
            }
            finally { window.RequestSafeClose(); await window.PendingOperation; await window.StructurePendingLoad; UiLocalization.Apply(language); }
            return true;
        }, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(2));
    }

    [Fact]
    public async Task InvalidDraftClearsTheStructureWithoutTracingAndNewDraftDoesNotRestoreStaleScene()
    {
        using var files = new OwnedFiles(); using var host = SafeHeadlessUnitTestSession.StartNew(typeof(FlatStartDesktopTests));
        await host.Dispatch(async () =>
        {
            var window = new AutonomousTopologyWindow(new() { RunDirectory = files.Root, InitialPlan = Plan() });
            try
            {
                window.Show();
                Find<NumericUpDown>(window, "AutonomousFocal").Text = "invalid";
                using var rays = SequentialTraceMeasurement.Begin();
                var tabs = Find<TabControl>(window, "AutonomousTabs"); tabs.SelectedItem = Find<TabItem>(window, "AutonomousStructureTab");
                await window.StructurePendingLoad;
                Assert.Null(Find<TopologyStructurePlot>(window, "AutonomousStructurePlot").Data);
                Assert.Contains(UiLocalization.Text("结构显示未完成"), Find<TextBlock>(window, "AutonomousStructureHint").Text!);
                Assert.Equal(0, rays.RayCount);
                Click(window, "AutonomousNew"); await window.PendingOperation;
                Assert.Null(Find<TopologyStructurePlot>(window, "AutonomousStructurePlot").Data);
            }
            finally { window.RequestSafeClose(); await window.StructurePendingLoad; }
            return true;
        }, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(1));
    }
}

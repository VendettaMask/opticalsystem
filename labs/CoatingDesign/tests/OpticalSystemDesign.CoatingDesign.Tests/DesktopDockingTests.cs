using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using OpticalSystemDesign.App.Laboratories.CoatingDesign;
using OpticalSystemDesign.App.Services;

namespace OpticalSystemDesign.CoatingDesign.Tests;

public sealed partial class DesktopTests
{
    [Fact]
    public async Task FourCoatingPagesFloatCloseReopenAndRestoreWithoutRecalculatingOrLosingEdits()
    {
        using var host = new Host();
        await host.Run(async () =>
        {
            var window = new MainWindow { Width = 1500, Height = 980, ChooseUnsavedChanges = DiscardChanges };
            try
            {
                window.Show(); DockTick(window);
                Assert.Equal(2, window.GetVisualDescendants().OfType<DocumentDockControl>().Count());
                Assert.Equal(2, window.GetVisualDescendants().OfType<DocumentTabStripItem>().Count());
                Click(window, "示例：demo-edge"); await window.PendingOperation; DockTick(window);
                var workspace = window.DockWorkspace; var factory = workspace.DockFactory;
                var plot = workspace.PageContents.SelectMany(c => c.GetLogicalDescendants()).OfType<SpectrumPlot>().Single();
                var data = plot.DisplayData; var computations = plot.DisplayComputations; var fingerprint = window.CurrentExperiment.Fingerprint();
                plot.SetCurveVisible(CoatingQuantity.R, CoatingCurvePolarization.P, true); plot.ApplyRange(new(425, 725, 0, 100));
                Click(window, "堆栈几何分析"); await window.PendingOperation;
                var stack = Assert.IsType<CoatingStackAnalysisView>(factory.Pages[CoatingDockWorkspace.StackId].Context); stack.Plot.Select(7);
                Click(window, "查看镀膜结果"); await window.PendingOperation; DockTick(window);
                Assert.Equal(4, factory.AllDockables().OfType<IDocument>().Count());
                var contexts = factory.Pages.ToDictionary(p => p.Key, p => p.Value.Context);
                var plotPoint = plot.TranslatePoint(plot.PlotRect.Center, window)!.Value;
                window.MouseMove(plotPoint); window.MouseDown(plotPoint, MouseButton.Left); window.MouseUp(plotPoint, MouseButton.Left); DockTick(window);
                Click(window, "浮动当前镀膜面板"); await window.PendingOperation; DockTick(window);
                var floating = Assert.IsType<HostWindow>(TopLevel.GetTopLevel(plot)); DockTick(floating);
                Assert.NotSame(window, floating); Assert.Single(factory.Root!.Windows!);
                window.FontSize = 16; DockTick(floating); Assert.Equal(16, floating.FontSize);
                floating.Width = 860; floating.Height = 640; floating.Position = new PixelPoint(80, 80); DockTick(floating);
                Click(window, "保存镀膜布局"); await window.PendingOperation;
                Assert.True(File.Exists(CoatingDockLayoutStore.DefaultPath));
                Assert.DoesNotContain("ThicknessNm", File.ReadAllText(CoatingDockLayoutStore.DefaultPath));
                Click(window, "镀膜标签布局"); await window.PendingOperation; DockTick(window);
                Assert.Empty(factory.Root.Windows!); Assert.Single(factory.AllDockables().OfType<IDocumentDock>());
                Click(window, "恢复镀膜布局"); await window.PendingOperation; DockTick(window);
                Assert.Single(factory.Root.Windows!); floating = Assert.IsType<HostWindow>(TopLevel.GetTopLevel(plot)); DockTick(floating);
                Assert.Equal(860, floating.Width, 2); Assert.Equal(640, floating.Height, 2);
                Assert.Equal(new PixelPoint(80, 80), floating.Position);
                Assert.Same(data, plot.DisplayData); Assert.Equal(computations, plot.DisplayComputations);
                Assert.Equal(new CoatingPlotRange(425, 725, 0, 100), plot.Range); Assert.True(plot.CurveVisible(CoatingQuantity.R, CoatingCurvePolarization.P));
                Assert.Equal(7, stack.Plot.SelectedLayer); Assert.Equal(fingerprint, window.CurrentExperiment.Fingerprint());
                Assert.All(contexts, p => Assert.Same(p.Value, factory.Pages[p.Key].Context));
                floating.Close(); DockTick(window); Assert.False(workspace.IsPageOpen(CoatingDockWorkspace.SpectrumId));
                Click(window, "显示光学评价"); await window.PendingOperation; DockTick(window);
                Assert.Same(data, plot.DisplayData); Assert.True(workspace.IsPageOpen(CoatingDockWorkspace.SpectrumId));
                Click(window, "停靠全部镀膜面板"); await window.PendingOperation; DockTick(window);
                Assert.Equal(4, factory.AllDockables().OfType<IDocument>().Count());
                Assert.All(contexts, p => Assert.Same(p.Value, factory.Pages[p.Key].Context));
                Capture(window, "coating-four-pages-docked");
                Click(window, "重置镀膜布局"); await window.PendingOperation; DockTick(window);
                Assert.Equal(2, factory.AllDockables().OfType<IDocument>().Count()); Assert.Equal(7, stack.Plot.SelectedLayer);
                Click(window, "显示堆栈几何"); await window.PendingOperation; DockTick(window);
                Assert.Same(stack, factory.Pages[CoatingDockWorkspace.StackId].Context);
                UiLocalization.Apply(UiLocalization.English); DockTick(window);
                Assert.Equal("Stack Geometry", factory.Pages[CoatingDockWorkspace.StackId].Title);
                UiLocalization.Apply(UiLocalization.Chinese);
            }
            finally { UiLocalization.Apply(UiLocalization.Chinese); window.CloseAfterHostApproval(); }
        });
    }

    [Fact]
    public async Task SavedCoatingLayoutLoadsInANewWindowAndInvalidFilesKeepCurrentLayout()
    {
        using var host = new Host();
        await host.Run(async () =>
        {
            var window = new MainWindow { Width = 1400, Height = 900, ChooseUnsavedChanges = DiscardChanges };
            window.Show(); DockTick(window);
            Click(window, "堆栈几何分析"); await window.PendingOperation;
            Click(window, "查看镀膜结果"); await window.PendingOperation;
            Click(window, "镀膜上下布局"); await window.PendingOperation;
            Click(window, "保存镀膜布局"); await window.PendingOperation;
            window.CloseAfterHostApproval();
            var reopened = new MainWindow { Width = 1400, Height = 900, ChooseUnsavedChanges = DiscardChanges };
            try
            {
                reopened.Show(); await reopened.PendingOperation; DockTick(reopened);
                var workspace = reopened.DockWorkspace;
                Assert.Equal(4, workspace.DockFactory.AllDockables().OfType<IDocument>().Count());
                Assert.Equal(Dock.Model.Core.Orientation.Vertical,
                    Assert.IsType<ProportionalDock>(workspace.DockFactory.Root!.VisibleDockables!.Single()).Orientation);
                var originalRoot = workspace.Layout; var original = reopened.CurrentExperiment.Fingerprint();
                Assert.Throws<InvalidDataException>(() => workspace.RestoreLayout("{\"Version\":99}"));
                Assert.Same(originalRoot, workspace.Layout);
                var saved = CoatingDockLayoutStore.Parse(workspace.CaptureLayout());
                var unknown = saved with { Tree = saved.Tree.Replace(CoatingDockWorkspace.EditorId, "coating:unknown", StringComparison.Ordinal) };
                Assert.Throws<InvalidDataException>(() => workspace.RestoreLayout(JsonSerializer.Serialize(unknown)));
                Assert.Same(originalRoot, workspace.Layout); Assert.Equal(original, reopened.CurrentExperiment.Fingerprint());
                var badActive = saved with { ActivePage = "coating:unknown" };
                Assert.Throws<InvalidDataException>(() => workspace.RestoreLayout(JsonSerializer.Serialize(badActive)));
                Assert.Same(originalRoot, workspace.Layout);
                File.WriteAllText(CoatingDockLayoutStore.DefaultPath, "invalid-json");
                Click(reopened, "恢复镀膜布局"); await reopened.PendingOperation; DockTick(reopened);
                Assert.Same(originalRoot, workspace.Layout);
                Assert.True(WindowControls(reopened).OfType<Control>().Single(c => c.Name == "CoatingOperationStatus").IsVisible);
            }
            finally { reopened.CloseAfterHostApproval(); }
        });
    }

    [Fact]
    public async Task NativeTabReorderingAndSplitProportionsRoundTripWithTheActivePage()
    {
        using var host = new Host();
        await host.Run(async () =>
        {
            var window = new MainWindow { Width = 1500, Height = 980, ChooseUnsavedChanges = DiscardChanges };
            try
            {
                window.Show(); DockTick(window);
                Click(window, "堆栈几何分析"); await window.PendingOperation;
                Click(window, "查看镀膜结果"); await window.PendingOperation;
                Click(window, "镀膜标签布局"); await window.PendingOperation; DockTick(window);
                var workspace = window.DockWorkspace; var factory = workspace.DockFactory;
                var group = Assert.Single(factory.AllDockables().OfType<IDocumentDock>());
                var source = factory.Pages[CoatingDockWorkspace.ResultsId];
                var first = factory.Pages[CoatingDockWorkspace.EditorId];
                var sourceTab = window.GetVisualDescendants().OfType<DocumentTabStripItem>().Single(t => ReferenceEquals(t.DataContext, source));
                var firstTab = window.GetVisualDescendants().OfType<DocumentTabStripItem>().Single(t => ReferenceEquals(t.DataContext, first));
                var start = sourceTab.TranslatePoint(new Point(18, sourceTab.Bounds.Height / 2), window)!.Value;
                window.MouseMove(start); window.MouseDown(start, MouseButton.Left);
                window.MouseMove(start + new Vector(20, 35), RawInputModifiers.LeftMouseButton); DockTick(window);
                var target = firstTab.TranslatePoint(new Point(3, firstTab.Bounds.Height / 2), window)!.Value;
                window.MouseMove(target, RawInputModifiers.LeftMouseButton); DockTick(window);
                window.MouseMove(target, RawInputModifiers.LeftMouseButton); DockTick(window);
                window.MouseUp(target, MouseButton.Left); DockTick(window);
                Assert.Same(group, source.Owner); Assert.Same(source, group.VisibleDockables![0]);
                Click(window, "镀膜上下布局"); await window.PendingOperation; DockTick(window);
                var split = Assert.IsType<ProportionalDock>(factory.Root!.VisibleDockables!.Single());
                var groups = split.VisibleDockables!.OfType<IDocumentDock>().ToArray();
                groups[0].Proportion = .37; groups[1].Proportion = .63;
                Click(window, "显示堆栈几何"); await window.PendingOperation; DockTick(window);
                var order = groups[1].VisibleDockables!.Select(d => d.Id).ToArray();
                var context = factory.Pages[CoatingDockWorkspace.StackId].Context;
                Assert.Equal(.37, groups[0].Proportion, 10); Assert.Equal(.63, groups[1].Proportion, 10);
                var saved = workspace.CaptureLayout();
                if (Environment.GetEnvironmentVariable("COATING_UI_EVIDENCE") is { Length: > 0 } evidence)
                    File.WriteAllText(Path.Combine(evidence, "coating-layout-detail.json"), saved);
                workspace.ApplyLayout(CoatingPaneLayout.Columns); DockTick(window);
                workspace.RestoreLayout(saved); DockTick(window);
                split = Assert.IsType<ProportionalDock>(factory.Root.VisibleDockables!.Single());
                groups = split.VisibleDockables!.OfType<IDocumentDock>().ToArray();
                Assert.Equal(Dock.Model.Core.Orientation.Vertical, split.Orientation);
                Assert.Equal(.37, groups[0].Proportion, 10); Assert.Equal(.63, groups[1].Proportion, 10);
                Assert.Equal(order, groups[1].VisibleDockables!.Select(d => d.Id));
                Assert.Equal(CoatingDockWorkspace.StackId, groups[1].ActiveDockable!.Id);
                Assert.Same(context, factory.Pages[CoatingDockWorkspace.StackId].Context);
                Capture(window, "coating-restored-row-layout");
            }
            finally { window.CloseAfterHostApproval(); }
        });
    }

    [Fact]
    public void FloatingBoundsHandleRemovedScreensNegativeOriginsAndDisplayScaling()
    {
        var screens = new[] { new CoatingScreenArea(0, 0, 1920, 1080, 1), new(-2560, 0, 2560, 1440, 2) };
        var second = CoatingDockLayoutStore.ClampWindow(-2400, 100, 1000, 600, screens);
        Assert.Equal(new CoatingWindowRectangle(-2400, 100, 1000, 600), second);
        var restored = CoatingDockLayoutStore.ClampWindow(-2400, 100, 1000, 600, [screens[0]]);
        Assert.Equal(0, restored.X); Assert.InRange(restored.Y + restored.Height, 0, 1080);
        var invalid = CoatingDockLayoutStore.ClampWindow(double.NaN, double.PositiveInfinity, double.MaxValue, -1, [screens[1]]);
        Assert.InRange(invalid.X, -2560, 0); Assert.InRange(invalid.Width, 380, 1280);
        Assert.InRange(invalid.X + invalid.Width * 2, -2560, 0);
        Assert.InRange(invalid.Y + invalid.Height * 2, 0, 1440);
    }

    [Theory]
    [InlineData(DockOperation.Left)]
    [InlineData(DockOperation.Right)]
    [InlineData(DockOperation.Top)]
    [InlineData(DockOperation.Bottom)]
    [InlineData(DockOperation.Fill)]
    public async Task CoatingTabsUseNativeCompassAndRetainTheSameContents(DockOperation direction)
    {
        using var host = new Host();
        await host.Run(() =>
        {
            var window = new MainWindow { Width = 1500, Height = 980, ChooseUnsavedChanges = DiscardChanges };
            try
            {
                window.Show(); DockTick(window);
                var workspace = window.DockWorkspace; var factory = workspace.DockFactory;
                var source = factory.Pages[CoatingDockWorkspace.SpectrumId]; var target = factory.Pages[CoatingDockWorkspace.EditorId];
                var content = source.Context; var fingerprint = window.CurrentExperiment.Fingerprint();
                var compass = DragCoatingTab(window, source, target);
                Assert.Empty(window.GetVisualDescendants().OfType<GlobalDockTarget>());
                foreach (var part in new[] { "Left", "Right", "Top", "Bottom", "Center" }) Assert.True(DockPart(compass, part + "Selector").IsEffectivelyVisible);
                var operation = direction == DockOperation.Fill ? "Center" : direction.ToString();
                var point = HoverDock(window, DockPart(compass, operation + "Selector"));
                Assert.True(DockPart(compass, operation + "Indicator").Opacity > 0);
                Capture(window, "coating-dock-preview-" + operation);
                window.MouseUp(point, MouseButton.Left); DockTick(window);
                Assert.Same(content, source.Context); Assert.Equal(fingerprint, window.CurrentExperiment.Fingerprint());
                Assert.Equal(2, factory.AllDockables().OfType<IDocument>().Count());
                if (direction == DockOperation.Fill) Assert.Same(source.Owner, target.Owner);
                else Assert.NotSame(source.Owner, target.Owner);
                Assert.False(workspace.IsDraggingDock); Capture(window, "coating-docked-" + operation);
            }
            finally { window.CloseAfterHostApproval(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task CancellingADockDragAndCancellingMainCloseKeepTheFloatingWorkspace()
    {
        using var host = new Host();
        await host.Run(async () =>
        {
            var window = new MainWindow
            {
                Width = 1400,
                Height = 900,
                ChooseUnsavedChanges = _ => Task.FromResult(global::OpticalSystemDesign.App.UnsavedChangesChoice.Cancel)
            };
            try
            {
                window.Show(); DockTick(window); var workspace = window.DockWorkspace; var factory = workspace.DockFactory;
                var source = factory.Pages[CoatingDockWorkspace.SpectrumId]; var target = factory.Pages[CoatingDockWorkspace.EditorId];
                var owner = source.Owner;
                DragCoatingTab(window, source, target);
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                window.MouseUp(new Point(500, 400), MouseButton.Left); DockTick(window);
                Assert.Same(owner, source.Owner); Assert.False(workspace.IsDraggingDock); Assert.Empty(factory.Root!.Windows!);
                factory.FloatDockable(source); DockTick(window);
                var floating = Assert.IsType<HostWindow>(TopLevel.GetTopLevel((Control)source.Context!));
                Find<TextBox>(window, "实验名称").Text = "unsaved floating coating";
                Assert.False(await window.PrepareForHostCloseAsync());
                Assert.True(floating.IsVisible); Assert.True(workspace.IsPageOpen(source.Id)); Assert.True(window.HasUnsavedChanges);
                Capture(floating, "coating-floating-spectrum");
                window.CloseAfterHostApproval(); Assert.False(floating.IsVisible);
            }
            finally { window.CloseAfterHostApproval(); }
        });
    }

    [Fact]
    public async Task PointerTearOffAndCrossWindowRedockUseTheSameCoatingPage()
    {
        using var host = new Host();
        await host.Run(() =>
        {
            var window = new MainWindow { Width = 1500, Height = 980, ChooseUnsavedChanges = DiscardChanges };
            try
            {
                window.Show(); DockTick(window);
                var workspace = window.DockWorkspace; var factory = workspace.DockFactory;
                var source = factory.Pages[CoatingDockWorkspace.SpectrumId]; var target = factory.Pages[CoatingDockWorkspace.EditorId];
                var content = source.Context;
                var tab = window.GetVisualDescendants().OfType<DocumentTabStripItem>().Single(t => ReferenceEquals(t.DataContext, source));
                var start = tab.TranslatePoint(new Point(18, tab.Bounds.Height / 2), window)!.Value;
                window.MouseMove(start); window.MouseDown(start, MouseButton.Left);
                window.MouseMove(start + new Vector(20, 40), RawInputModifiers.LeftMouseButton); DockTick(window);
                var outside = new Point(-150, -100);
                window.MouseMove(outside, RawInputModifiers.LeftMouseButton); DockTick(window);
                window.MouseUp(outside, MouseButton.Left); DockTick(window);
                var floating = Assert.IsType<HostWindow>(TopLevel.GetTopLevel((Control)content!)); DockTick(floating);
                Assert.Single(factory.Root!.Windows!);
                var floatTab = floating.GetVisualDescendants().OfType<DocumentTabStripItem>().Single(t => ReferenceEquals(t.DataContext, source));
                var grip = floatTab.TranslatePoint(new Point(18, floatTab.Bounds.Height / 2), floating)!.Value;
                floating.MouseMove(grip); floating.MouseDown(grip, MouseButton.Left);
                floating.MouseMove(grip + new Vector(18, 40), RawInputModifiers.LeftMouseButton); DockTick(floating);
                var targetDock = window.GetVisualDescendants().OfType<DocumentDockControl>().Single(d => ReferenceEquals(d.DataContext, target.Owner));
                var targetPoint = targetDock.TranslatePoint(new Point(targetDock.Bounds.Width / 2, targetDock.Bounds.Height / 2), window)!.Value;
                var crossPoint = floating.PointToClient(window.PointToScreen(targetPoint));
                floating.MouseMove(crossPoint, RawInputModifiers.LeftMouseButton); DockTick(window); DockTick(floating);
                floating.MouseMove(crossPoint, RawInputModifiers.LeftMouseButton); DockTick(window);
                var compass = Assert.Single(window.GetVisualDescendants().OfType<DockTarget>());
                var selector = DockPart(compass, "CenterSelector");
                var drop = selector.TranslatePoint(new Point(selector.Bounds.Width / 2, selector.Bounds.Height / 2), window)!.Value;
                var localDrop = floating.PointToClient(window.PointToScreen(drop));
                floating.MouseMove(localDrop, RawInputModifiers.LeftMouseButton); DockTick(window); DockTick(floating);
                Capture(window, "coating-cross-window-redock-preview");
                floating.MouseUp(localDrop, MouseButton.Left); DockTick(window);
                Assert.Same(content, source.Context); Assert.Same(source.Owner, target.Owner);
                Assert.Empty(factory.Root.Windows!); Assert.Same(window, TopLevel.GetTopLevel((Control)content!));
            }
            finally { window.CloseAfterHostApproval(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task EveryPageCanFloatTogetherAndFloatingEditorKeepsSaveShortcuts()
    {
        using var host = new Host();
        var path = Path.Combine(Path.GetDirectoryName(CoatingDockLayoutStore.DefaultPath)!, "floating-editor.coating.json");
        await host.Run(async () =>
        {
            var window = new MainWindow(new LaboratoryFileDialogs(() => Task.FromResult<string?>(path),
                () => Task.FromResult<string?>(path), () => Task.FromResult<string?>(null)))
            { Width = 1500, Height = 980, ChooseUnsavedChanges = DiscardChanges };
            try
            {
                window.Show(); DockTick(window);
                Click(window, "堆栈几何分析"); await window.PendingOperation;
                Click(window, "查看镀膜结果"); await window.PendingOperation;
                var workspace = window.DockWorkspace; var factory = workspace.DockFactory;
                foreach (var id in new[] { CoatingDockWorkspace.EditorId, CoatingDockWorkspace.SpectrumId, CoatingDockWorkspace.StackId, CoatingDockWorkspace.ResultsId })
                { factory.FloatDockable(factory.Pages[id]); DockTick(window); }
                Assert.Equal(4, factory.Root!.Windows!.Count);
                var floating = Assert.IsType<HostWindow>(TopLevel.GetTopLevel((Control)factory.Pages[CoatingDockWorkspace.EditorId].Context!));
                Find<TextBox>(window, "实验名称").Text = "Saved from floating editor";
                floating.KeyPress(Key.S, RawInputModifiers.Control, PhysicalKey.S, "s");
                floating.KeyRelease(Key.S, RawInputModifiers.Control, PhysicalKey.S, "s");
                await window.PendingOperation;
                Assert.Equal("Saved from floating editor", (await OpticalSystemDesign.CoatingDesign.Engine.ExperimentStore.OpenAsync(path)).Name);
                var saved = workspace.CaptureLayout();
                workspace.ApplyLayout(CoatingPaneLayout.Columns); DockTick(window);
                workspace.RestoreLayout(saved); DockTick(window);
                Assert.Equal(4, factory.Root.Windows!.Count);
                Assert.Equal(4, factory.AllDockables().OfType<IDocument>().Count());
                Assert.NotEmpty(factory.Root.VisibleDockables!);
                workspace.ApplyLayout(CoatingPaneLayout.Columns); DockTick(window);
                foreach (var id in factory.Pages.Keys.ToArray()) workspace.Close(id);
                DockTick(window); Assert.Empty(factory.AllDockables().OfType<IDocument>());
                Click(window, "显示膜系编辑器"); await window.PendingOperation; DockTick(window);
                Assert.True(workspace.IsPageOpen(CoatingDockWorkspace.EditorId));
                Assert.Equal("Saved from floating editor", Find<TextBox>(window, "实验名称").Text);
            }
            finally { window.CloseAfterHostApproval(); }
        });
    }

    private static void DockTick(Window window)
    { Tick(window); window.UpdateLayout(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    private static DockTarget DragCoatingTab(Window window, Document source, Document target)
    {
        var tab = window.GetVisualDescendants().OfType<DocumentTabStripItem>().Single(t => ReferenceEquals(t.DataContext, source));
        var start = tab.TranslatePoint(new Point(18, tab.Bounds.Height / 2), window)!.Value;
        window.MouseMove(start); window.MouseDown(start, MouseButton.Left);
        window.MouseMove(start + new Vector(18, tab.Bounds.Height + 16), RawInputModifiers.LeftMouseButton); DockTick(window);
        HoverDock(window, window.GetVisualDescendants().OfType<DocumentDockControl>().Single(d => ReferenceEquals(d.DataContext, target.Owner)));
        return Assert.Single(window.GetVisualDescendants().OfType<DockTarget>());
    }
    private static Point HoverDock(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point, RawInputModifiers.LeftMouseButton); DockTick(window);
        window.MouseMove(point, RawInputModifiers.LeftMouseButton); DockTick(window); return point;
    }
    private static Control DockPart(DockTarget target, string name) => target.GetVisualDescendants().OfType<Control>().Single(c => c.Name == "PART_" + name);
}

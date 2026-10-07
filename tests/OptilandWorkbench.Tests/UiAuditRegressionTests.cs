using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Panels;
using OptilandWorkbench.App.Services;
using OptilandWorkbench.App.Theming;
using OptilandWorkbench.App.ViewModels;

namespace OptilandWorkbench.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class UiAuditRegressionTests
{
    private const string CaptureDirectory = "OPTILAND_UI_AUDIT_CAPTURE_DIR";

    public static AppBuilder BuildAvaloniaApp()
    {
        var capture = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(CaptureDirectory));
        var builder = AppBuilder.Configure<global::OptilandWorkbench.App.App>();
        if (capture) builder.UseSkia();
        return builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = !capture });
    }

    [Fact]
    public async Task UnlockingAnOldResultRequiresSyncAndFileSwitchClearsLockedContent()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(UiAuditRegressionTests));
        var previousDirectory = Environment.GetEnvironmentVariable("OPTILAND_SETTINGS_DIRECTORY");
        var settingsDirectory = Path.Combine(Path.GetTempPath(), "ui-audit-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("OPTILAND_SETTINGS_DIRECTORY", settingsDirectory);
        try
        {
            // Keep the asynchronous calculation inside a single headless dispatcher lifetime.
            await session.Dispatch(async () =>
            {
                ThemeApplicationService.Apply(global::Avalonia.Application.Current!, "Light");
                using var app = WorkbenchApplication.Create("tessar");
                using var panel = new AnalysisPanel(app.Analyses, app.Visualization, app.Documents, app.Events,
                    new AppSettings(), "First Order");
                panel.IsLocked = true;
                panel.IsLocked = false;
                Assert.Equal(OperationStatusKind.Idle, Status(panel).Kind);
                var run = panel.RunForGuiCaptureAsync();
                var window = new Window { Width = 1200, Height = 650, Content = panel };
                try
                {
                    window.Show();
                    Assert.True(await run.WaitAsync(TimeSpan.FromSeconds(30)));
                    Render(window);
                    Assert.Equal(OperationStatusKind.Synced, Status(panel).Kind);
                    var oldView = Field<AnalysisViewDto>(panel, "_view");
                    Assert.Equal("4.5", oldView.Rows.Single(row => row.Metric == "F 数").Value);
                    var host = Field<ContentControl>(panel, "_resultHost");
                    Assert.Equal(1, Assert.IsType<TabControl>(host.Content).SelectedIndex);
                    Capture(window, "first-order-default-data.png");
                    panel.IsLocked = true;
                    panel.IsLocked = false;
                    Assert.Equal(OperationStatusKind.Synced, Status(panel).Kind);
                    panel.IsLocked = true;
                    var settings = app.Prescription.GetSystemSettings();
                    Assert.Equal(4.5, settings.ApertureValue);
                    app.Prescription.UpdateSystemSettings(settings with { ApertureValue = 5 });
                    Render(window);
                    Assert.Equal(OperationStatusKind.Stale, Status(panel).Kind);
                    panel.IsLocked = false;
                    Assert.Equal(OperationStatusKind.Stale, Status(panel).Kind);
                    Assert.Same(oldView, Field<AnalysisViewDto>(panel, "_view"));
                    panel.IsLocked = true;
                    Assert.Equal(OperationStatusKind.Stale, Status(panel).Kind);
                    panel.IsLocked = false;
                    Assert.Contains(Status(panel).Children.OfType<TextBlock>(), text => text.Text == "结果已过期，请同步");
                    Capture(window, "unlocked-stale-result.png");
                    run = panel.RunForGuiCaptureAsync();
                    panel.IsLocked = true;
                    panel.IsLocked = false;
                    Assert.Equal(OperationStatusKind.Running, Status(panel).Kind);
                    Assert.True(await run.WaitAsync(TimeSpan.FromSeconds(30)));
                    Render(window);
                    Assert.Equal(OperationStatusKind.Synced, Status(panel).Kind);
                    Assert.Equal(app.Events.Revision, Field<long>(panel, "_displayedSourceRevision"));
                    Assert.Equal("5", Field<AnalysisViewDto>(panel, "_view").Rows.Single(row => row.Metric == "F 数").Value);
                    panel.IsLocked = true;
                    app.Documents.NewBlank();
                    Render(window);
                    Assert.Null(host.Content);
                    panel.IsLocked = false;
                    Assert.NotEqual(OperationStatusKind.Synced, Status(panel).Kind);
                }
                finally { window.Close(); }
                return true;
            }, CancellationToken.None);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPTILAND_SETTINGS_DIRECTORY", previousDirectory);
            if (Directory.Exists(settingsDirectory)) Directory.Delete(settingsDirectory, recursive: true);
        }
    }

    [Theory]
    [InlineData("RadiusSolveCell")]
    [InlineData("ThicknessSolveCell")]
    [InlineData("SemiDiameterSolveCell")]
    [InlineData("ThermalExpansionCell")]
    public async Task InvalidNumericInputRetainsErrorWithoutPublishingAnUpdate(string cellName)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(UiAuditRegressionTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(global::Avalonia.Application.Current!, "Light");
            using var app = WorkbenchApplication.Create("tessar");
            // Establish recalculated automatic apertures before checking undo equality.
            app.Prescription.UpdateSurface(app.Prescription.GetSurfaces()[1] with
            {
                SemiDiameterFixed = cellName == "SemiDiameterSolveCell"
            });
            using var panel = new LensEditorPanel(app.Prescription, app.Events, new SurfaceSelectionService());
            var window = new Window { Width = 2100, Height = 580, Content = panel };
            try
            {
                window.Show();
                Render(window);
                var grid = panel.GetVisualDescendants().OfType<DataGrid>().Single();
                TextBox Editor() => grid.GetVisualDescendants().OfType<DataGridRow>()
                    .Single(row => row.DataContext is SurfaceEditorRow { Number: 1 })
                    .GetVisualDescendants().OfType<Grid>().Single(cell => cell.Name == cellName)
                    .GetVisualDescendants().OfType<TextBox>().Single();
                var editor = Editor();
                Assert.False(editor.IsReadOnly);
                var editorHeight = editor.Bounds.Height;
                var originalText = editor.Text;
                var original = app.Prescription.GetSurfaces()[1];
                var revision = app.Events.Revision;
                var events = new List<WorkspaceChangedEventArgs>();
                app.Events.Changed += (_, args) => events.Add(args);
                editor.Focus();
                Press(window, Key.Tab);
                Render(window);
                Assert.Empty(events);
                Assert.Equal(original, app.Prescription.GetSurfaces()[1]);
                editor.Focus();
                editor.SelectAll();
                window.KeyTextInput("abc");
                Press(window, Key.Tab);
                Render(window);
                Assert.Equal("abc", editor.Text);
                Assert.True(DataValidationErrors.GetHasErrors(editor));
                Assert.Contains("请输入", Assert.IsType<string>(ToolTip.GetTip(editor)));
                Assert.Equal(ToolTip.GetTip(editor), AutomationProperties.GetHelpText(editor));
                var border = editor.GetVisualDescendants().OfType<Border>()
                    .Single(item => item.Name == "PART_BorderElement");
                Assert.Equal(Color.Parse("#AD3F3B"), Assert.IsAssignableFrom<ISolidColorBrush>(border.BorderBrush).Color);
                Assert.True(border.BorderThickness.Left > 0);
                Assert.Equal(editorHeight, editor.Bounds.Height);
                var presenter = editor.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.TextPresenter>().Single();
                Assert.Equal("abc", presenter.Text);
                Assert.True(presenter.Bounds.Height >= editor.FontSize);
                var top = presenter.TranslatePoint(default, editor)!.Value.Y;
                Assert.InRange(top, 0, editor.Bounds.Height - presenter.Bounds.Height);
                Assert.Equal(original, app.Prescription.GetSurfaces()[1]);
                Assert.Equal(revision, app.Events.Revision);
                Assert.Empty(events);
                Capture(window, cellName + "-invalid.png");

                editor.Focus();
                Press(window, Key.Escape);
                Render(window);
                Assert.Equal(originalText, editor.Text);
                Assert.False(DataValidationErrors.GetHasErrors(editor));
                Assert.Empty(events);

                editor.SelectAll();
                window.KeyTextInput("abc");
                Press(window, Key.Tab);
                Render(window);
                Assert.True(DataValidationErrors.GetHasErrors(editor));
                Assert.Empty(events);
                editor.Focus();
                editor.SelectAll();
                window.KeyTextInput("2.123456789");
                Press(window, Key.Tab);
                Render(window);
                var updated = app.Prescription.GetSurfaces()[1];
                Assert.Equal(2.123456789, cellName switch
                {
                    "RadiusSolveCell" => updated.Radius,
                    "ThicknessSolveCell" => updated.Thickness,
                    "ThermalExpansionCell" => updated.ThermalExpansionPpmPerC!.Value,
                    _ => updated.SemiDiameter
                }, precision: 12);
                Assert.Single(events);
                Assert.Equal(revision + 1, app.Events.Revision);
                Assert.False(DataValidationErrors.GetHasErrors(Editor()));
                Assert.Null(ToolTip.GetTip(Editor()));
                Assert.True(app.Documents.Undo());
                Assert.Equal(original, app.Prescription.GetSurfaces()[1]);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task HeadlessSessionWaitsForAsyncCallbacks()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(UiAuditRegressionTests));
        var completed = false;
        await session.Dispatch(async () =>
        {
            await Task.Delay(25);
            completed = true;
        }, CancellationToken.None);
        Assert.True(completed);
    }

    [Fact]
    public async Task HeadlessSessionPropagatesAsyncCallbackFailures()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(UiAuditRegressionTests));
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => session.Dispatch(async () =>
        {
            await Task.Yield();
            throw new InvalidOperationException("Dispatched callback failed.");
        }, CancellationToken.None));
        Assert.Equal("Dispatched callback failed.", failure.Message);
    }

    [Theory]
    [InlineData("First Order", 1)]
    [InlineData("Prescription Report", 1)]
    [InlineData("System Data Report", 1)]
    [InlineData("Classified Data Report", 1)]
    [InlineData("Spot Diagram", 0)]
    [InlineData("Matrix Spot Diagram", 0)]
    [InlineData("Wavefront Map", 0)]
    public async Task DefaultResultPageShowsActualContentRegardlessOfTitle(string key, int expectedIndex)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(UiAuditRegressionTests));
        await session.Dispatch(async () =>
        {
            using var app = WorkbenchApplication.Create("tessar");
            var result = await app.Analyses.RunAsync(new AnalysisRequestDto(Guid.NewGuid(), 1, key,
                app.Analyses.MergeSettings(key, null)));
            foreach (var title in new[] { result.View.Name, "任意本地化名称" })
            {
                var tabs = BuildResult(result.View with { Name = title }, app);
                Assert.Equal(expectedIndex, tabs.SelectedIndex);
                Assert.Equal(new[] { "绘图", "数据", "文本" }, tabs.Items.Cast<TabItem>().Select(tab => tab.Header));
            }
            var reportOnly = result.View with
            {
                Name = "文本报告",
                PresentationKind = AnalysisPresentationKind.Standard,
                Series = Array.Empty<AnalysisSeriesDto>(),
                PlotPanes = Array.Empty<AnalysisPlotPaneDto>(),
                Rows = Array.Empty<AnalysisRowDto>(),
                Table = null,
                ReportText = "报告正文"
            };
            Assert.Equal(2, BuildResult(reportOnly, app).SelectedIndex);
            return true;
        }, CancellationToken.None);
    }

    private static TabControl BuildResult(AnalysisViewDto view, WorkbenchApplication app) =>
        Assert.IsType<TabControl>(typeof(AnalysisPanel).GetMethod("BuildResultContent", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object?[] { view, app.Documents.GetSnapshot(), DateTimeOffset.Now, null }));

    private static T Field<T>(AnalysisPanel panel, string name) =>
        (T)typeof(AnalysisPanel).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(panel)!;

    private static OperationStatusBar Status(AnalysisPanel panel) => Field<OperationStatusBar>(panel, "_operationStatus");

    private static void Press(Window window, Key key)
    {
        window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
        window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
    }

    private static void Render(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }

    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable(CaptureDirectory);
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var bitmap = window.CaptureRenderedFrame();
        Assert.NotNull(bitmap);
        bitmap.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }
}

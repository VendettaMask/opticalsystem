using System.Collections;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Laboratories;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class AssemblyWorkspaceTests
{
    [Theory]
    [InlineData("Light", "zh-CN", 1440, 900, 18)]
    [InlineData("Dark", "en-US", 1260, 850, 13)]
    [InlineData("Light", "ja-JP", 760, 680, 18)]
    [InlineData("Light", "zh-CN", 1440, 960, 22)]
    public async Task CompactWorkspaceKeepsCommandsSplittersAndEveryParameterReachable(string theme, string language, int width, int height, double fontSize)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(LaboratoryPresentationTests));
        await session.Dispatch(async () =>
        {
            var previousLanguage = UiLocalization.CurrentLanguage;
            DisplayTypography.Configure(new AppSettings { Theme = theme, FontSize = fontSize });
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme); UiLocalization.Apply(language);
            using var app = WorkbenchApplication.Create();
            var window = new OpticalAssemblyWindow(app) { Width = width, Height = height };
            try
            {
                window.Show(); await window.CalculateAsync(); Tick(window);
                var revision = app.Events.Revision;
                var toolbar = Find<Control>(window, "LaboratoryHeader");
                Assert.InRange(toolbar.Bounds.Height, 25, Math.Max(50, fontSize * 2.8));
                foreach (var button in toolbar.GetVisualDescendants().OfType<Button>())
                {
                    var rectangle = Bounds(button, window);
                    Assert.InRange(rectangle.Right, 0, window.ClientSize.Width + .5);
                    Assert.True(rectangle.Bottom < window.ClientSize.Height);
                }
                var tabs = Find<TabControl>(window, "AssemblyResultTabs");
                Assert.Equal(width == 760, tabs.IsVisible);
                Assert.True(Find<Control>(window, "AssemblyEmptyTable").IsVisible);
                Assert.Null(window.Preview);
                Capture(window, $"empty-{theme}-{language}-{width}-{fontSize}");
                if (tabs.IsVisible) { tabs.SelectedIndex = 1; Tick(window); Assert.True(Find<Control>(window, "AssemblyEmptyPreview").IsEffectivelyVisible); Capture(window, "empty-narrow-image"); tabs.SelectedIndex = 0; }
                var scroll = Find<ScrollViewer>(window, "AssemblySettings");
                foreach (var expander in scroll.GetLogicalDescendants().OfType<Expander>()) expander.IsExpanded = true;
                Tick(window);
                foreach (var editor in scroll.GetVisualDescendants().OfType<Control>().Where(c => c is NumericUpDown or ComboBox).Where(c => c.IsEffectivelyVisible))
                {
                    editor.BringIntoView(); Tick(window);
                    var rectangle = Bounds(editor, window); var viewport = Bounds(scroll, window);
                    Assert.True(rectangle.Width > 50 && rectangle.Height > 20);
                    Assert.True(rectangle.Left >= viewport.Left - .5 && rectangle.Right <= viewport.Right + .5);
                    Assert.InRange(rectangle.Center.Y, viewport.Top - .5, viewport.Bottom + .5);
                }
                if (!tabs.IsVisible)
                {
                    var workspace = Find<Grid>(window, "AssemblyWorkspace"); var before = workspace.ColumnDefinitions[0].ActualWidth;
                    Drag(Find<GridSplitter>(window, "AssemblyParameterSplitter"), 24, window); Tick(window);
                    Assert.True(workspace.ColumnDefinitions[0].ActualWidth > before + 10);
                }
                Assert.Equal(revision, app.Events.Revision);
            }
            finally { window.Close(); UiLocalization.Apply(previousLanguage); DisplayTypography.Configure(new AppSettings()); }
        }, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(2));
    }

    [Theory] [InlineData("Light")] [InlineData("Dark")]
    public async Task RealConjugatesReticleAndSelectionSurviveTabsSplittersAndParameterInvalidation(string theme)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(LaboratoryPresentationTests));
        await session.Dispatch(async () =>
        {
            DisplayTypography.Configure(new AppSettings { Theme = theme, FontSize = 13 });
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme); UiLocalization.Apply("zh-CN");
            using var app = WorkbenchApplication.Create(); await app.Documents.OpenAsync(DoubleGaussSfrTests.StandardLens);
            var serialization = new JsonSerializerOptions { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
            var prescription = JsonSerializer.Serialize(app.Prescription.GetSurfaces(), serialization);
            var window = new OpticalAssemblyWindow(app) { Width = 1440, Height = 900 };
            try
            {
                window.Show(); await window.CalculateAsync(); Tick(window);
                var conjugates = Assert.IsType<OpticalAssemblyResult>(window.Result);
                var preview = Assert.IsType<AssemblyReticleResult>(window.Preview); Assert.True(preview.RecordedRays > 0);
                var table = Find<DataGrid>(window, "assembly-conjugates"); var selection = table.SelectedItem;
                var selectedRow = Assert.IsType<OpticalAssemblyRow>(selection!.GetType().GetProperty("Row")!.GetValue(selection));
                var direct = await app.OpticalAssembly.SimulateAsync(new(conjugates.Request, conjugates.SourceRevision, selectedRow.SurfaceNumber, selectedRow.Kind));
                Assert.Equal(direct.Pixels, preview.Pixels); Assert.Equal(direct.RecordedRays, preview.RecordedRays); Assert.Equal(direct.Magnification, preview.Magnification);
                Capture(window, $"computed-{theme}");
                DisplayTypography.Configure(new AppSettings { Theme = theme, FontSize = 22 }); DisplayTypography.ApplyRecursively(window, 13); Tick(window);
                Assert.Equal(22, table.FontSize); Assert.Same(preview, window.Preview); Assert.Same(conjugates, window.Result);
                Capture(window, $"computed-{theme}-large");
                DisplayTypography.Configure(new AppSettings { Theme = theme, FontSize = 13 }); DisplayTypography.ApplyRecursively(window, 22); Tick(window);
                var split = Find<Grid>(window, "AssemblyResultSplit"); var tableWidth = split.ColumnDefinitions[0].ActualWidth;
                Drag(Find<GridSplitter>(window, "AssemblyResultSplitter"), -32, window); Tick(window);
                Assert.True(split.ColumnDefinitions[0].ActualWidth < tableWidth - 10);
                window.Width = 760; window.Height = 680; Tick(window);
                var tabs = Find<TabControl>(window, "AssemblyResultTabs"); Assert.True(tabs.IsVisible);
                tabs.SelectedIndex = 1; Tick(window); Capture(window, $"computed-{theme}-narrow-image");
                Assert.Same(preview, window.Preview); Assert.Same(selection, table.SelectedItem);
                window.Width = 1440; window.Height = 900; Tick(window); Assert.False(tabs.IsVisible);
                Assert.Same(preview, window.Preview); Assert.Same(conjugates, window.Result);
                var sort = All(window).OfType<CheckBox>().Single(c => AutomationProperties.GetName(c) == "按找像位置排序");
                Click(sort, window); Tick(window); Assert.Same(preview, window.Preview);
                // Actual keyboard editing clears only the stale image, retaining conjugate results.
                var head = Find<NumericUpDown>(window, "assembly-head-focal"); head.BringIntoView(); Tick(window);
                var input = head.GetVisualDescendants().OfType<TextBox>().Single(); Click(input, window);
                window.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.A, "a"); window.KeyRelease(Key.A, RawInputModifiers.Control, PhysicalKey.A, "a");
                window.KeyTextInput("110"); window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); Tick(window);
                Assert.Equal(110, head.Value); Assert.Null(window.Preview); Assert.Same(conjugates, window.Result);
                Assert.True(Find<Control>(window, "AssemblyEmptyPreview").IsVisible);
                await window.SimulateAsync(); Assert.NotNull(window.Preview);
                var mode = Find<ComboBox>(window, "assembly-measurement-mode"); mode.SelectedIndex = 1; Tick(window);
                Assert.Same(conjugates, window.Result); Assert.Null(window.Preview);
                Assert.True(Find<NumericUpDown>(window, "assembly-pupil-diameter").IsEffectivelyVisible);
                Assert.False(head.IsEffectivelyVisible);
                app.Documents.NewBlank(); Tick(window);
                Assert.Null(window.Result); Assert.Null(window.Preview);
                Assert.True(Find<Control>(window, "AssemblyEmptyTable").IsVisible);
                // The UI actions never wrote back to the original lens.
                await app.Documents.OpenAsync(DoubleGaussSfrTests.StandardLens);
                Assert.Equal(prescription, JsonSerializer.Serialize(app.Prescription.GetSurfaces(), serialization));
            }
            finally { window.Close(); DisplayTypography.Configure(new AppSettings()); }
        }, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(3));
    }

    private static IEnumerable<Control> All(Window window) => window.GetVisualDescendants().OfType<Control>().Concat(window.GetLogicalDescendants().OfType<Control>()).Distinct();
    private static T Find<T>(Window window, string name) where T : Control => All(window).OfType<T>().Single(c => c.Name == name);
    private static Rect Bounds(Control control, Window window) => new Rect(control.Bounds.Size).TransformToAABB(control.TransformToVisual(window)!.Value);
    private static void Click(Control control, Window window)
    {
        var rectangle = Bounds(control, window); var point = rectangle.Center;
        Assert.InRange(point.X, 0, window.ClientSize.Width); Assert.InRange(point.Y, 0, window.ClientSize.Height);
        window.MouseMove(point); window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Tick(window);
    }
    private static void Drag(Control control, double delta, Window window)
    {
        var point = Bounds(control, window).Center;
        window.MouseMove(point); window.MouseDown(point, MouseButton.Left); window.MouseMove(point + new Vector(delta, 0)); window.MouseUp(point + new Vector(delta, 0), MouseButton.Left); Tick(window);
    }
    private static void Tick(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    private static void Capture(Window window, string name)
    {
        var root = Environment.GetEnvironmentVariable("OPTICAL_ASSEMBLY_WORKSPACE_CAPTURE_DIR"); if (string.IsNullOrWhiteSpace(root)) return;
        Directory.CreateDirectory(root); Tick(window); using var bitmap = window.CaptureRenderedFrame(); Assert.NotNull(bitmap); bitmap.Save(Path.Combine(root, name + ".png"), PngBitmapEncoderOptions.Default);
    }
}

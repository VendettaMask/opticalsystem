using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class MultiConfigurationMatrixTests
{
    public static AppBuilder BuildAvaloniaApp() => MultiConfigurationPanelTests.BuildAvaloniaApp();

    [Theory]
    [InlineData("Light", "zh-CN", 1000)]
    [InlineData("Dark", "en-US", 680)]
    [InlineData("Pixel", "ja-JP", 480)]
    [InlineData("Isekai", "en-US", 1000)]
    [InlineData("Lumen", "zh-CN", 680)]
    public async Task ZoomMatrixIsDefaultAndUsesRealReadOnlyAperturesAndEditableOperandReferences(string theme, string language, int width)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(MultiConfigurationMatrixTests));
        await session.Dispatch(async () =>
        {
            var previousLanguage = UiLocalization.CurrentLanguage;
            UiLocalization.Apply(language);
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            using var app = WorkbenchApplication.Create();
            await app.Documents.OpenAsync(ZoomPath());
            var revision = app.Events.Revision;
            Assert.Equal(new double[] { 5, 6.2, 7.8 }, app.MultiConfiguration.GetApertureValues());
            Assert.Equal(revision, app.Events.Revision);
            using var panel = new MultiConfigurationPanel(app.Prescription, app.MultiConfiguration, app.Events);
            var window = new Window { Content = panel, Width = width, Height = 760 };
            try
            {
                window.Show(); Tick(window);
                var pages = Find<TabControl>(panel, "MultiConfigurationPages");
                Assert.Equal(1, pages.SelectedIndex);
                var grid = Find<DataGrid>(panel, "MultiConfigurationOperands");
                Assert.Equal(3, grid.FrozenColumnCount);
                Assert.Equal(6, grid.Columns.Count);
                Assert.Equal(3, grid.ItemsSource!.Cast<object>().Count());
                Assert.Empty(app.MultiConfiguration.GetOperandRows());
                Assert.Equal(new[] { 8, 15 }, app.MultiConfiguration.GetThicknessDifferences().Select(row => row.SurfaceNumber));
                Assert.False(Find<Border>(panel, "MultiConfigurationOperandProperties").IsVisible);
                Assert.Contains(grid.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "APER");
                Assert.All(grid.GetVisualDescendants().OfType<DataGridColumnHeader>().Where(h => h.Content is not null), h => Assert.Equal(FontWeight.Bold, h.FontWeight));
                Assert.Equal(UiLocalization.Text("结构 1*"), ((TextBlock)grid.Columns[3].Header!).Text);
                foreach (var name in new[] { "ToggleMultiConfigurationProperties", "PreviousMatrixConfiguration", "NextMatrixConfiguration", "AddMatrixConfiguration" })
                {
                    var button = Find<Button>(panel, name);
                    var bounds = new Rect(button.TranslatePoint(default, window)!.Value, button.Bounds.Size);
                    Assert.True(bounds.X >= 0 && bounds.Right <= width && bounds.Bottom < 180, $"{name}: {bounds}");
                }
                Capture(window, $"matrix-{theme}-{language}-{width}-default.png");
                if (width >= 680)
                {
                    MouseClick(window, Find<Border>(panel, "MultiConfigurationCell-THIC-8-3"));
                    Assert.Equal(0, app.MultiConfiguration.GetNavigationState().ActiveIndex);
                    grid.CurrentColumn = grid.Columns[4]; Tick(window);
                    var header = (TextBlock)grid.Columns[5].Header!;
                    MouseClick(window, header); MouseClick(window, header);
                    Assert.Equal(2, app.MultiConfiguration.GetNavigationState().ActiveIndex);
                    app.MultiConfiguration.Activate(0); Tick(window);
                }
                MouseClick(window, Find<Button>(panel, "NextMatrixConfiguration"));
                Assert.Equal(1, app.MultiConfiguration.GetNavigationState().ActiveIndex);
                Assert.Equal(UiLocalization.Text("结构 2/3"), Find<TextBlock>(panel, "MultiConfigurationPosition").Text);
                Assert.Equal(UiLocalization.Text("结构 2*"), ((TextBlock)grid.Columns[4].Header!).Text);
                Assert.Equal(4.48, app.Prescription.GetSurfaces()[8].Thickness, 8);
                Assert.True(app.Documents.Undo()); Tick(window);
                Assert.Equal(0, app.MultiConfiguration.GetNavigationState().ActiveIndex);
                Assert.True(app.Documents.Redo()); Tick(window);
                MouseClick(window, Find<Button>(panel, "ToggleMultiConfigurationProperties"));
                var configuration = Input(panel, UiLocalization.Text("编辑配置号（从 1 开始）"));
                var value = Input(panel, UiLocalization.Text("多配置单元格值"));
                grid.SelectedIndex = 0; Tick(window);
                Assert.False(value.IsEnabled);
                Assert.False(string.IsNullOrEmpty(AutomationProperties.GetHelpText(value)));
                Assert.Equal(6.2, double.Parse(value.Text!, System.Globalization.CultureInfo.InvariantCulture), 8);
                grid.SelectedIndex = 1; Tick(window);
                Assert.True(value.IsEnabled);
                Assert.False(Find<Button>(panel, "SetMultiConfigurationVariable").IsEnabled);
                Assert.False(string.IsNullOrEmpty(AutomationProperties.GetHelpText(Find<Button>(panel, "SetMultiConfigurationVariable"))));
                Assert.Equal(4.48, double.Parse(value.Text!, System.Globalization.CultureInfo.InvariantCulture), 8);
                Assert.Null(AutomationProperties.GetHelpText(value));
                configuration.Text = "2"; Tick(window);
                value.Text = "4.75";
                Find<Button>(panel, "ApplyMultiConfigurationValue").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Tick(window);
                Assert.Equal(4.75, app.MultiConfiguration.GetThicknessDifferences()[0].Values[1]);
                Assert.Empty(app.MultiConfiguration.GetOperandRows());
                Assert.Equal(4.75, app.Prescription.GetSurfaces()[8].Thickness, 8);
                Assert.Equal(new double[] { 5, 6.2, 7.8 }, app.MultiConfiguration.GetApertureValues());
                Capture(window, $"matrix-{theme}-{language}-{width}-properties.png");
                revision = app.Events.Revision;
                value.Text = "bad input";
                Find<Button>(panel, "NextMatrixConfiguration").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Tick(window);
                Assert.Equal(revision, app.Events.Revision);
                Assert.Equal("bad input", value.Text);
                Assert.Equal(1, app.MultiConfiguration.GetNavigationState().ActiveIndex);
                Find<Button>(panel, "ReloadMultiConfigurationValue").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Tick(window);
                Assert.Equal("4.75", value.Text);
                UiLocalization.Apply(UiLocalization.Chinese); Tick(window);
                Assert.Equal("结构 2/3", Find<TextBlock>(panel, "MultiConfigurationPosition").Text);
                UiLocalization.Apply(language); Tick(window);
                Assert.Equal(UiLocalization.Text("结构 2/3"), Find<TextBlock>(panel, "MultiConfigurationPosition").Text);
                Assert.Equal(revision, app.Events.Revision);
                Input(panel, UiLocalization.Text("多配置行表面号")).Text = "8";
                Find<Button>(panel, "AddMultiConfigurationOperand").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Tick(window);
                Assert.Equal(8, Assert.Single(app.MultiConfiguration.GetOperandRows()).SurfaceNumber);
                Assert.Equal(3, grid.ItemsSource!.Cast<object>().Count());
                Assert.True(Find<Button>(panel, "SetMultiConfigurationVariable").IsEnabled);
                Assert.Null(AutomationProperties.GetHelpText(Find<Button>(panel, "SetMultiConfigurationVariable")));
                Find<Button>(panel, "SetMultiConfigurationVariable").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Tick(window);
                Assert.True(app.MultiConfiguration.GetOperandRows()[0].Variables[1]);
                Assert.True(app.Documents.Undo()); Tick(window);
                Assert.True(app.Documents.Undo()); Tick(window);
                Assert.Empty(app.MultiConfiguration.GetOperandRows());
                Assert.Equal(3, grid.ItemsSource!.Cast<object>().Count());
                app.Documents.NewBlank(); Tick(window);
                Assert.Equal(1, app.MultiConfiguration.GetNavigationState().Count);
                Assert.Equal(4, grid.Columns.Count);
            }
            finally { window.Close(); UiLocalization.Apply(previousLanguage); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task DifferenceMetadataIsNonMutatingAndThicknessUsesRevisionGuardAndProjectRoundTrip()
    {
        using var app = WorkbenchApplication.Create();
        await app.Documents.OpenAsync(ZoomPath());
        var revision = app.Events.Revision;
        var aperture = app.MultiConfiguration.GetApertureValues();
        var differences = app.MultiConfiguration.GetThicknessDifferences();
        Assert.Equal(new[] { 8, 15 }, differences.Select(row => row.SurfaceNumber));
        Assert.Equal(new[] { 9.48, 4.48, 2.0 }, differences[0].Values);
        Assert.Equal(revision, app.Events.Revision);
        Assert.Empty(app.MultiConfiguration.GetOperandRows());
        ((double[])aperture)[0] = 999;
        ((double[])differences[0].Values)[0] = 999;
        Assert.Equal(5, app.MultiConfiguration.GetApertureValues()[0]);
        Assert.Equal(9.48, app.MultiConfiguration.GetThicknessDifferences()[0].Values[0]);
        app.MultiConfiguration.SetThickness(1, 8, 4.75, revision);
        var changed = app.Events.Revision;
        Assert.Throws<InvalidOperationException>(() => app.MultiConfiguration.SetThickness(1, 8, 7, revision));
        Assert.Equal(changed, app.Events.Revision);
        Assert.Equal(4.75, app.MultiConfiguration.GetThicknessDifferences()[0].Values[1]);
        Assert.Equal(9.48, app.Prescription.GetSurfaces()[8].Thickness);
        var path = Path.Combine(Path.GetTempPath(), "matrix-roundtrip-" + Guid.NewGuid().ToString("N") + ".staropt");
        try
        {
            await app.Documents.SaveAsync(path);
            app.Documents.NewBlank();
            await app.Documents.OpenAsync(path);
            Assert.Equal(4.75, app.MultiConfiguration.GetThicknessDifferences()[0].Values[1]);
            Assert.Equal(new[] { 5, 6.2, 7.8 }, app.MultiConfiguration.GetApertureValues());
            Assert.Empty(app.MultiConfiguration.GetOperandRows());
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static TextBox Input(Control root, string name) => root.GetVisualDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == name);
    private static T Find<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void MouseClick(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point); window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Tick(window);
    }
    private static void Tick(Window window)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
    }
    private static string ZoomPath()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "OpticalSystemDesign.slnx"))) root = root.Parent;
        return Path.Combine(root!.FullName, "src/OpticalSystemDesign.App/Assets/SequentialExamples/Zoom systems/Zoom lens.zmx");
    }
    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_MCE_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        // A compact offscreen viewport keeps the default matrix preview useful
        // without capturing the user's screen or altering the window layout.
        var height = name.EndsWith("-default.png", StringComparison.Ordinal) ? 300 : (int)window.Height;
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Width, height), new Vector(96, 96));
        bitmap.Render(window); bitmap.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }
}

using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.App.Panels;
using OptilandWorkbench.App.Services;
using OptilandWorkbench.App.Theming;
using OptilandWorkbench.App.ViewModels;

namespace OptilandWorkbench.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class RadiusSolveUiTests
{
    private const string CaptureDirectory = "OPTILAND_RADIUS_SOLVE_CAPTURE_DIR";

    public static AppBuilder BuildAvaloniaApp()
    {
        var capture = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(CaptureDirectory));
        var builder = AppBuilder.Configure<global::OptilandWorkbench.App.App>();
        if (capture) builder.UseSkia();
        return builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = !capture });
    }

    [Fact]
    public Task RadiusMarkerOpensVariableByDefaultWithoutMutatingUntilConfirmed() => Run((app, panel, window) =>
    {
        var grid = Grid(panel);
        Assert.DoesNotContain(grid.Columns, column => Equals(column.Header, "R 变量"));
        Assert.Equal(string.Empty, Marker(panel, 2).Content);
        Assert.Equal(24, Marker(panel, 2).Bounds.Width);
        Assert.False(Marker(panel, 0).IsEnabled);
        Assert.False(Marker(panel, app.Prescription.GetSurfaces().Count - 1).IsEnabled);
        var revision = app.Events.Revision;
        Open(panel, window, 2);
        var content = Content(panel);
        var kind = Find<ComboBox>(content, "RadiusSolveKind");
        Assert.Equal(new[] { "固定", "变量", "拾取" }, kind.Items.Cast<string>());
        Assert.Equal("变量", kind.SelectedItem);
        Assert.False(Find<StackPanel>(content, "RadiusPickupFields").IsVisible);
        Assert.Equal(2, Assert.IsType<SurfaceEditorRow>(grid.SelectedItem).Number);
        Assert.Equal(revision, app.Events.Revision);
        Capture(window, "variable");
        Click(Find<Button>(content, "CancelRadiusSolve"));
        Render(window);
        Assert.Equal(revision, app.Events.Revision);
        Assert.Equal(string.Empty, Marker(panel, 2).Content);
        Open(panel, window, 2);
        Click(Find<Button>(Content(panel), "ApplyRadiusSolve"));
        Render(window);
        Assert.Equal(revision + 1, app.Events.Revision);
        Assert.Equal("V", Marker(panel, 2).Content);
        Assert.True(app.Prescription.GetSurfaces()[2].RadiusVariable);
    });

    [Fact]
    public Task PickupFieldsValidateAndReflectActualSolveAndCanReturnToFixed() => Run((app, panel, window) =>
    {
        var revision = app.Events.Revision;
        Open(panel, window, 2);
        var content = Content(panel);
        Find<ComboBox>(content, "RadiusSolveKind").SelectedIndex = 2;
        Render(window);
        Assert.True(Find<StackPanel>(content, "RadiusPickupFields").IsVisible);
        var source = Find<ComboBox>(content, "RadiusPickupSource");
        Assert.Equal(new[] { 0, 1 }, source.Items.Cast<int>());
        source.SelectedItem = 1;
        var scale = Find<TextBox>(content, "RadiusPickupScale");
        scale.Text = "invalid";
        Click(Find<Button>(content, "ApplyRadiusSolve"));
        Render(window);
        Assert.Equal(revision, app.Events.Revision);
        Assert.True(Find<TextBlock>(content, "RadiusSolveError").IsVisible);
        scale.Text = "0.5";
        Capture(window, "pickup");
        Click(Find<Button>(content, "ApplyRadiusSolve"));
        Render(window);
        Assert.Equal("P", Marker(panel, 2).Content);
        Assert.Equal(app.Prescription.GetSurfaces()[1].Radius * 2, app.Prescription.GetSurfaces()[2].Radius);
        Assert.True(Row(panel, 2).GetVisualDescendants().OfType<TextBox>().First().IsReadOnly);
        Open(panel, window, 2);
        content = Content(panel);
        Assert.Equal("拾取", Find<ComboBox>(content, "RadiusSolveKind").SelectedItem);
        Assert.Equal("0.5", Find<TextBox>(content, "RadiusPickupScale").Text);
        Find<ComboBox>(content, "RadiusSolveKind").SelectedIndex = 0;
        Click(Find<Button>(content, "ApplyRadiusSolve"));
        Render(window);
        Assert.Equal(string.Empty, Marker(panel, 2).Content);
        Assert.False(Row(panel, 2).GetVisualDescendants().OfType<TextBox>().First().IsReadOnly);
    });

    [Fact]
    public Task PendingNumericEditIsCommittedBeforeOpeningAndStalePopupCannotApply() => Run((app, panel, window) =>
    {
        var radius = Row(panel, 2).GetVisualDescendants().OfType<TextBox>().First();
        radius.Focus();
        radius.Text = "-120";
        Open(panel, window, 2);
        Assert.Equal(-120, app.Prescription.GetSurfaces()[2].Radius);
        var apply = Find<Button>(Content(panel), "ApplyRadiusSolve");
        app.Prescription.InsertSurface(1, after: false);
        var revision = app.Events.Revision;
        Click(apply); // A command already queued before refresh must also be rejected.
        Render(window);
        Assert.Equal(revision, app.Events.Revision);
        Assert.False(app.Prescription.GetSurfaces()[2].RadiusVariable);
        Assert.Null(Flyout(panel));
    });

    private static async Task Run(Action<WorkbenchApplication, LensEditorPanel, Window> test)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(RadiusSolveUiTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(global::Avalonia.Application.Current!, "Light");
            using var app = WorkbenchApplication.Create("cooke");
            using var panel = new LensEditorPanel(app.Prescription, app.Events, new SurfaceSelectionService());
            var window = new Window { Width = 1100, Height = 580, Content = panel };
            try
            {
                window.Show();
                Render(window);
                test(app, panel, window);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private static DataGrid Grid(Control panel) => panel.GetVisualDescendants().OfType<DataGrid>().Single();
    private static DataGridRow Row(Control panel, int number) => Grid(panel).GetVisualDescendants().OfType<DataGridRow>()
        .Single(row => row.DataContext is SurfaceEditorRow data && data.Number == number);
    private static Button Marker(Control panel, int number) => Find<Button>(Row(panel, number), "RadiusSolveButton");
    private static T Find<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>()
        .Single(control => control.Name == name);
    private static Flyout? Flyout(LensEditorPanel panel) => (Flyout?)typeof(LensEditorPanel)
        .GetField("_radiusSolveFlyout", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(panel);
    private static Control Content(LensEditorPanel panel) => Assert.IsAssignableFrom<Control>(Assert.IsType<Flyout>(Flyout(panel)).Content);
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Open(LensEditorPanel panel, Window window, int number)
    {
        var marker = Marker(panel, number);
        var point = marker.TranslatePoint(new Point(marker.Bounds.Width / 2, marker.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Render(window);
        Assert.NotNull(Flyout(panel));
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
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, $"radius-solve-{name}.png"), PngBitmapEncoderOptions.Default);
    }
}

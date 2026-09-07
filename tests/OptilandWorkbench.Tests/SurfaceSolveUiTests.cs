using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.App.Panels;
using OptilandWorkbench.App.Services;
using OptilandWorkbench.App.Theming;
using OptilandWorkbench.App.ViewModels;

namespace OptilandWorkbench.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class SurfaceSolveUiTests
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<global::OptilandWorkbench.App.App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());

    [Fact]
    public Task ThicknessUsesInlineVAndPMarkersWithoutASeparateVariableColumn() => Run((app, panel, window) =>
    {
        var grid = Grid(panel);
        Assert.DoesNotContain(grid.Columns, column => Equals(column.Header, "T 变量"));
        Assert.DoesNotContain(grid.Columns, column => Equals(column.Header, "固定"));
        Assert.Equal(string.Empty, Marker(panel, 2, "ThicknessSolveButton").Content);

        RightClick(window, Cell(panel, 2, "RadiusSolveCell"));
        var radiusContent = FlyoutContent(panel, "_radiusSolveFlyout");
        Assert.Equal("变量", Find<ComboBox>(radiusContent, "RadiusSolveKind").SelectedItem);
        Click(Find<Button>(radiusContent, "CancelRadiusSolve"));
        Layout(window);

        RightClick(window, Cell(panel, 2, "ThicknessSolveCell"));
        var content = FlyoutContent(panel, "_thicknessSolveFlyout");
        Assert.Equal("变量", Find<ComboBox>(content, "ThicknessSolveKind").SelectedItem);
        Click(Find<Button>(content, "ApplyThicknessSolveContent"));
        Layout(window);
        Assert.Equal("V", Marker(panel, 2, "ThicknessSolveButton").Content);

        RightClick(window, Cell(panel, 2, "ThicknessSolveCell"));
        content = FlyoutContent(panel, "_thicknessSolveFlyout");
        Find<ComboBox>(content, "ThicknessSolveKind").SelectedIndex = 2;
        Find<ComboBox>(content, "ThicknessPickupSource").SelectedItem = 1;
        Find<TextBox>(content, "ThicknessPickupScale").Text = "2";
        Find<TextBox>(content, "ThicknessPickupOffset").Text = "1";
        Click(Find<Button>(content, "ApplyThicknessSolveContent"));
        Layout(window);
        Assert.Equal("P", Marker(panel, 2, "ThicknessSolveButton").Content);
        Assert.Equal((app.Prescription.GetSurfaces()[1].Thickness * 2) + 1,
            app.Prescription.GetSurfaces()[2].Thickness, 12);
        Assert.True(Cell(panel, 2, "ThicknessSolveCell").GetVisualDescendants().OfType<TextBox>().Single().IsReadOnly);
    });

    [Fact]
    public Task SemiDiameterUsesBlankUAndPMarkersWithAutomaticAsTheDefault() => Run((app, panel, window) =>
    {
        var automatic = Marker(panel, 2, "SemiDiameterSolveButton");
        Assert.Equal(string.Empty, automatic.Content);
        Assert.True(Cell(panel, 2, "SemiDiameterSolveCell").GetVisualDescendants().OfType<TextBox>().Single().IsReadOnly);

        RightClick(window, Cell(panel, 2, "SemiDiameterSolveCell"));
        var content = FlyoutContent(panel, "_semiDiameterSolveFlyout");
        var kind = Find<ComboBox>(content, "SemiDiameterSolveKind");
        Assert.Equal(new[] { "自动", "固定", "拾取" }, kind.Items.Cast<string>());
        Assert.Equal("固定", kind.SelectedItem);
        Click(Find<Button>(content, "ApplySemiDiameterSolveContent"));
        Layout(window);
        Assert.Equal("U", Marker(panel, 2, "SemiDiameterSolveButton").Content);
        Assert.False(Cell(panel, 2, "SemiDiameterSolveCell").GetVisualDescendants().OfType<TextBox>().Single().IsReadOnly);

        RightClick(window, Cell(panel, 2, "SemiDiameterSolveCell"));
        content = FlyoutContent(panel, "_semiDiameterSolveFlyout");
        Find<ComboBox>(content, "SemiDiameterSolveKind").SelectedIndex = 2;
        Find<ComboBox>(content, "SemiDiameterPickupSource").SelectedItem = 1;
        Find<TextBox>(content, "SemiDiameterPickupScale").Text = "0.5";
        Click(Find<Button>(content, "ApplySemiDiameterSolveContent"));
        Layout(window);
        Assert.Equal("P", Marker(panel, 2, "SemiDiameterSolveButton").Content);

        RightClick(window, Cell(panel, 2, "SemiDiameterSolveCell"));
        content = FlyoutContent(panel, "_semiDiameterSolveFlyout");
        Find<ComboBox>(content, "SemiDiameterSolveKind").SelectedIndex = 0;
        Click(Find<Button>(content, "ApplySemiDiameterSolveContent"));
        Layout(window);
        Assert.Equal(string.Empty, Marker(panel, 2, "SemiDiameterSolveButton").Content);
        Assert.True(Cell(panel, 2, "SemiDiameterSolveCell").GetVisualDescendants().OfType<TextBox>().Single().IsReadOnly);
    });

    private static async Task Run(Action<WorkbenchApplication, LensEditorPanel, Window> test)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(SurfaceSolveUiTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(global::Avalonia.Application.Current!, "Light");
            using var app = WorkbenchApplication.Create("cooke");
            using var panel = new LensEditorPanel(app.Prescription, app.Events, new SurfaceSelectionService());
            var window = new Window { Width = 1200, Height = 580, Content = panel };
            try
            {
                window.Show();
                Layout(window);
                test(app, panel, window);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    private static DataGrid Grid(Control panel) => panel.GetVisualDescendants().OfType<DataGrid>().Single();

    private static DataGridRow Row(Control panel, int number) => Grid(panel).GetVisualDescendants().OfType<DataGridRow>()
        .Single(row => row.DataContext is SurfaceEditorRow data && data.Number == number);

    private static Control Cell(Control panel, int number, string name) =>
        Find<Control>(Row(panel, number), name);

    private static Button Marker(Control panel, int number, string name) =>
        Find<Button>(Row(panel, number), name);

    private static Control FlyoutContent(LensEditorPanel panel, string field) =>
        Assert.IsAssignableFrom<Control>(Assert.IsType<Flyout>(typeof(LensEditorPanel)
            .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(panel)).Content);

    private static T Find<T>(Control root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);

    private static void RightClick(Window window, Control target)
    {
        var point = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Right);
        window.MouseUp(point, MouseButton.Right);
        Layout(window);
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void Layout(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Panels;
using OptilandWorkbench.App.Services;
using OptilandWorkbench.App.Theming;
using OptilandWorkbench.App.ViewModels;

namespace OptilandWorkbench.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class SurfacePropertiesPanelTests
{
    private const string CaptureDirectory = "OPTILAND_SURFACE_PROPERTIES_CAPTURE_DIR";

    public static AppBuilder BuildAvaloniaApp()
    {
        var capture = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(CaptureDirectory));
        var builder = AppBuilder.Configure<global::OptilandWorkbench.App.App>();
        if (capture) builder.UseSkia();
        return builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = !capture });
    }

    [Theory]
    [InlineData(600)]
    [InlineData(1000)]
    public async Task CompactHeaderTracksSelectionAndNavigatesWithoutEditing(int width)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(SurfacePropertiesPanelTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(global::Avalonia.Application.Current!, "Light");
            using var application = WorkbenchApplication.Create("cooke");
            var selection = new SurfaceSelectionService();
            using var editor = new LensEditorPanel(application.Prescription, application.Events, selection);
            var window = new Window { Width = width, Height = 700, Content = editor };
            try
            {
                window.Show();
                Render(window);
                var grid = Find<DataGrid>(editor, "LensSurfaceGrid");
                var title = Find<TextBlock>(editor, "SurfacePropertiesTitle");
                var header = Find<Border>(editor, "SurfacePropertiesHeader");
                var items = Find<StackPanel>(editor, "SurfacePropertiesHeaderItems");
                var body = Find<Border>(editor, "SurfacePropertiesEditor");
                var toggle = Find<Button>(editor, "SurfacePropertiesToggle");
                var previous = Find<Button>(editor, "PreviousPropertySurface");
                var next = Find<Button>(editor, "NextPropertySurface");
                var revision = application.Events.Revision;
                Assert.False(body.IsVisible);
                Assert.Equal("表面 1 属性", title.Text);
                Assert.InRange(header.Bounds.Height, 28, 36);
                Assert.True(items.Bounds.Width < 240);
                Assert.Equal(4, items.Children.Count);
                foreach (var button in new[] { toggle, previous, next })
                {
                    Assert.Equal(28, button.Bounds.Width);
                    Assert.Equal(28, button.Bounds.Height);
                    Assert.Equal(new CornerRadius(14), button.CornerRadius);
                    Assert.IsType<LocalIcon>(button.Content);
                }
                MouseClick(window, previous);
                Assert.Equal("表面 0 属性", title.Text);
                Assert.Equal(0, selection.SelectedSurfaceNumber);
                Assert.False(previous.IsEnabled);
                Assert.True(next.IsEnabled);
                Capture(window, $"surface-properties-header-{width}.png");
                MouseClick(window, toggle);
                Assert.True(body.IsVisible);
                Assert.Equal("chevron-up", Assert.IsType<LocalIcon>(toggle.Content).IconName);
                MouseClick(window, next);
                Assert.Equal("表面 1 属性", title.Text);
                Assert.Equal(1, selection.SelectedSurfaceNumber);
                Assert.True(body.IsVisible);
                grid.SelectedItem = grid.ItemsSource!.Cast<SurfaceEditorRow>().Last();
                Render(window);
                var last = application.Prescription.GetSurfaces().Count - 1;
                Assert.Equal($"表面 {last} 属性", title.Text);
                Assert.Equal(last, selection.SelectedSurfaceNumber);
                Assert.False(next.IsEnabled);
                MouseClick(window, previous);
                Assert.Equal(last - 1, selection.SelectedSurfaceNumber);
                Assert.True(next.IsEnabled);
                MouseClick(window, toggle);
                Assert.False(body.IsVisible);
                Assert.Equal("chevron-down", Assert.IsType<LocalIcon>(toggle.Content).IconName);
                Assert.Equal(revision, application.Events.Revision);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task HeaderNavigationCommitsNumericEditButRejectsStaleQueuedNavigation()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(SurfacePropertiesPanelTests));
        await session.Dispatch(() =>
        {
            using var application = WorkbenchApplication.Create("cooke");
            using var editor = new LensEditorPanel(application.Prescription, application.Events, new SurfaceSelectionService());
            var window = new Window { Width = 1000, Height = 700, Content = editor };
            try
            {
                window.Show();
                Render(window);
                var grid = Find<DataGrid>(editor, "LensSurfaceGrid");
                var radius = grid.GetVisualDescendants().OfType<DataGridRow>()
                    .Single(row => row.DataContext is SurfaceEditorRow { Number: 1 })
                    .GetVisualDescendants().OfType<TextBox>().First();
                radius.Focus();
                radius.Text = "120";
                var next = Find<Button>(editor, "NextPropertySurface");
                MouseClick(window, next);
                Assert.Equal(120, application.Prescription.GetSurfaces()[1].Radius);
                Assert.Equal(2, Assert.IsType<SurfaceEditorRow>(grid.SelectedItem).Number);
                Assert.Equal("表面 2 属性", Find<TextBlock>(editor, "SurfacePropertiesTitle").Text);
                Click(next); // Queue navigation, then change the surface numbering before it runs.
                application.Prescription.InsertSurface(1, after: false);
                var revision = application.Events.Revision;
                Render(window);
                Assert.Equal(2, Assert.IsType<SurfaceEditorRow>(grid.SelectedItem).Number);
                Assert.Equal("表面 2 属性", Find<TextBlock>(editor, "SurfacePropertiesTitle").Text);
                Assert.Equal(revision, application.Events.Revision);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(600)]
    public async Task CategoriesUseBoundedPanelAndOnlyApplicableParameters(int width)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(SurfacePropertiesPanelTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(global::Avalonia.Application.Current!, "Light");
            using var application = WorkbenchApplication.Create("cooke");
            using var editor = new LensEditorPanel(application.Prescription, application.Events, new SurfaceSelectionService());
            var window = new Window { Width = width, Height = 700, Content = editor };
            try
            {
                window.Show();
                window.UpdateLayout();
                Click(Find<Button>(editor, "SurfacePropertiesToggle"));
                window.UpdateLayout();
                var body = Find<Grid>(editor, "SurfacePropertiesBody");
                var nav = Find<ListBox>(editor, "SurfacePropertyNavigation");
                Assert.Equal(new[] { "类型", "绘图", "孔径", "散射", "倾斜/偏心", "物理光学", "膜层", "导入", "复合", "偏振" },
                    nav.Items.Cast<ListBoxItem>().Select(item => item.Content));
                Assert.Equal(300, body.Bounds.Height);
                Assert.True(Find<DataGrid>(editor, "LensSurfaceGrid").Bounds.Height > 250);
                var grating = Find<StackPanel>(editor, "SurfaceGratingProperties");
                Assert.False(grating.IsVisible);
                Assert.False(Find<StackPanel>(editor, "SurfaceThinLensProperties").IsVisible);
                Assert.Equal(3, body.GetVisualDescendants().OfType<ComboBox>().Count(picker => !picker.IsEnabled));
                Assert.Equal(3, body.GetVisualDescendants().OfType<CheckBox>().Count(check => !check.IsEnabled));

                var geometry = Find<ComboBox>(editor, "SurfaceGeometry");
                geometry.SelectedItem = "平面光栅";
                window.UpdateLayout();
                Assert.True(grating.IsVisible);
                Click(Find<Button>(editor, "RevertSurfaceProperties"));
                window.UpdateLayout();
                Assert.False(grating.IsVisible);
                Assert.Equal("标准球面/圆锥", geometry.SelectedItem);

                foreach (var theme in new[] { "Light", "Dark", IsekaiTheme.SettingsValue, PixelTheme.SettingsValue })
                {
                    // Return to the type page: a detached picker has no ancestor styles.
                    nav.SelectedIndex = 0;
                    ThemeApplicationService.Apply(global::Avalonia.Application.Current!, theme);
                    window.UpdateLayout();
                    Assert.IsAssignableFrom<ISolidColorBrush>(geometry.Background);
                    for (var index = 0; index < 10; index++)
                    {
                        nav.SelectedIndex = index;
                        window.UpdateLayout();
                        Assert.Equal(300, body.Bounds.Height);
                        Assert.NotEmpty(body.GetVisualDescendants().OfType<TextBlock>());
                    }
                }
                ThemeApplicationService.Apply(global::Avalonia.Application.Current!, "Light");
                nav.SelectedIndex = 0;
                window.UpdateLayout();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                var directory = Environment.GetEnvironmentVariable(CaptureDirectory);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                    using var frame = window.CaptureRenderedFrame();
                    Assert.NotNull(frame);
                    frame.Save(Path.Combine(directory, $"surface-properties-{width}.png"), PngBitmapEncoderOptions.Default);
                }
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ApplyAndRevertUseSelectedSurfaceAndNavigationDoesNotCommitDrafts()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(SurfacePropertiesPanelTests));
        await session.Dispatch(() =>
        {
            using var application = WorkbenchApplication.Create("cooke");
            using var editor = new LensEditorPanel(application.Prescription, application.Events, new SurfaceSelectionService());
            var window = new Window { Width = 1000, Height = 700, Content = editor };
            try
            {
                window.Show();
                window.UpdateLayout();
                Click(Find<Button>(editor, "SurfacePropertiesToggle"));
                window.UpdateLayout();
                var grid = Find<DataGrid>(editor, "LensSurfaceGrid");
                var row = Assert.IsType<SurfaceEditorRow>(grid.SelectedItem);
                var nav = Find<ListBox>(editor, "SurfacePropertyNavigation");
                Find<CheckBox>(editor, "SurfaceIsStop").IsChecked = true;
                nav.SelectedIndex = 6;
                window.UpdateLayout();
                Find<TextBox>(editor, "SurfaceCoating").Text = "MgF2";
                nav.SelectedIndex = 2;
                window.UpdateLayout();
                Find<CheckBox>(editor, "SurfaceFixedSemiDiameter").IsChecked = true;
                Find<NumericUpDown>(editor, "SurfaceSemiDiameter").Value = 8.25m;
                Assert.Equal("None", application.Prescription.GetSurfaces()[row.Number].Coating);
                Click(Find<Button>(editor, "ApplySurfaceProperties"));
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var applied = application.Prescription.GetSurfaces()[row.Number];
                Assert.True(applied.IsStop);
                Assert.Equal("MgF2", applied.Coating);
                Assert.Equal(8.25, applied.SemiDiameter);
                Assert.Equal(row.Number, Assert.IsType<SurfaceEditorRow>(grid.SelectedItem).Number);

                nav.SelectedIndex = 6;
                window.UpdateLayout();
                Find<TextBox>(editor, "SurfaceCoating").Text = "unsaved draft";
                MouseClick(window, Find<Button>(editor, "NextPropertySurface"));
                Assert.Equal(2, Assert.IsType<SurfaceEditorRow>(grid.SelectedItem).Number);
                Assert.Equal("None", Find<TextBox>(editor, "SurfaceCoating").Text);
                Assert.Equal("MgF2", application.Prescription.GetSurfaces()[row.Number].Coating);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private static T Find<T>(Control root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void MouseClick(Window window, Button button)
    {
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Render(window);
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
        frame.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }
}

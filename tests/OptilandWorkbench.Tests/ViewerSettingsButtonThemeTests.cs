using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Panels;
using OptilandWorkbench.App.Services;
using OptilandWorkbench.App.Theming;

namespace OptilandWorkbench.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class ViewerSettingsButtonThemeTests
{
    [Fact]
    public async Task SharedSceneDrawingResourcesDoNotBelongToAnEarlierUiThread()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OptilandWorkbench.App.App));
        await session.Dispatch(() => _ = new OpticSceneControl(), CancellationToken.None);
        var resources = typeof(OpticSceneControl).GetFields(
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            .Select(field => field.GetValue(null)).Where(value => value is IPen or IBrush).ToArray();
        Assert.True(resources.Length > 20);
        await Task.Run(() =>
        {
            foreach (var resource in resources)
            {
                if (resource is IPen pen)
                {
                    Assert.True(double.IsFinite(pen.Thickness));
                    Assert.NotNull(pen.Brush);
                    Assert.InRange(pen.Brush!.Opacity, 0, 1);
                }
                if (resource is IBrush brush) Assert.InRange(brush.Opacity, 0, 1);
            }
        });
    }

    [Fact]
    public async Task RefreshButtonMatchesAdjacentSettingsButtonAndKeepsHoverFeedback()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OptilandWorkbench.App.App));
        await session.Dispatch(() =>
        {
            var app = Assert.IsType<global::OptilandWorkbench.App.App>(global::Avalonia.Application.Current);
            using var application = WorkbenchApplication.Create("cooke");
            using var viewer = new ViewerPanel(application.Visualization, application.Events,
                new SurfaceSelectionService(), SceneDimension.TwoDimensional)
            { IsLocked = true };
            var window = new Window { Width = 600, Height = 400, Content = viewer };
            try
            {
                window.Show();
                window.UpdateLayout();
                var refresh = viewer.GetVisualDescendants().OfType<Button>()
                    .Single(button => button.Content is LocalIcon { IconName: "refresh-cw" });
                var settings = Assert.IsType<Button>(Assert.IsType<StackPanel>(refresh.Parent).Children[0]);
                var presenter = refresh.GetVisualDescendants().OfType<ContentPresenter>()
                    .Single(control => control.Name == "PART_ContentPresenter");
                foreach (var theme in new[] { "Light", "Dark", IsekaiTheme.SettingsValue, PixelTheme.SettingsValue })
                {
                    ThemeApplicationService.Apply(app, theme);
                    window.MouseMove(new Point(590, 390));
                    window.UpdateLayout();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    var normal = Assert.IsAssignableFrom<ISolidColorBrush>(settings.Background).Color;
                    Assert.Equal(normal, Assert.IsAssignableFrom<ISolidColorBrush>(refresh.Background).Color);
                    Assert.Equal(normal, Assert.IsAssignableFrom<ISolidColorBrush>(presenter.Background).Color);
                    var bounds = refresh.Bounds;
                    var center = refresh.TranslatePoint(new Point(refresh.Bounds.Width / 2, refresh.Bounds.Height / 2), window);
                    Assert.NotNull(center);
                    window.MouseMove(center.Value);
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    Assert.True(refresh.IsPointerOver);
                    Assert.NotEqual(normal, Assert.IsAssignableFrom<ISolidColorBrush>(presenter.Background).Color);
                    Assert.Equal(bounds, refresh.Bounds);
                    window.MouseMove(new Point(590, 390));
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    Assert.Equal(normal, Assert.IsAssignableFrom<ISolidColorBrush>(presenter.Background).Color);
                }
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }
}

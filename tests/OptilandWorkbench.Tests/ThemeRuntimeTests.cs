using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.VisualTree;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Theming;

namespace OptilandWorkbench.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class ThemeRuntimeTests
{
    [Fact]
    public async Task RuntimeSwitchUpdatesPackageResourcesWithoutChangingLayoutBounds()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessTestApplication));
        await session.Dispatch(() =>
        {
            var application = Assert.IsType<HeadlessTestApplication>(global::Avalonia.Application.Current);
            var content = new Border
            {
                Width = 320,
                Height = 180,
                Child = new LocalIcon
                {
                    IconName = "save",
                    Width = 28,
                    Height = 28
                }
            };
            ThemeChrome.Apply(content, ThemeChromeRole.SettingsCard);
            var window = new Window
            {
                Width = 420,
                Height = 260,
                Content = ThemeChrome.WrapWithDecoration(content, ThemeChromeRole.Dialog)
            };

            try
            {
                window.Show();
                ThemeApplicationService.Apply(application, "Light");
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                var lightBounds = content.Bounds.Size;
                var lightCorner = content.CornerRadius;
                Assert.Equal(ThemeVariant.Light, content.ActualThemeVariant);
                Assert.Equal(StandardThemeIconPack.Instance.Id, ThemeIconResolver.PackId(content.ActualThemeVariant));

                ThemeApplicationService.Apply(application, IsekaiTheme.SettingsValue);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Assert.Equal(IsekaiTheme.Variant, content.ActualThemeVariant);
                Assert.Equal(IsekaiThemeIconPack.Instance.Id, ThemeIconResolver.PackId(content.ActualThemeVariant));
                Assert.NotEqual(lightCorner, content.CornerRadius);
                Assert.Equal(lightBounds, content.Bounds.Size);

                ThemeApplicationService.Apply(application, "Dark");
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Assert.Equal(ThemeVariant.Dark, content.ActualThemeVariant);
                Assert.Equal(StandardThemeIconPack.Instance.Id, ThemeIconResolver.PackId(content.ActualThemeVariant));
                Assert.Equal(lightBounds, content.Bounds.Size);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SystemSelectionRemainsAProxyForTheResolvedVisualTheme()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessTestApplication));
        await session.Dispatch(() =>
        {
            var application = Assert.IsType<HeadlessTestApplication>(global::Avalonia.Application.Current);
            var selection = ThemeApplicationService.Apply(application, "System");

            Assert.True(selection.FollowsSystem);
            Assert.Equal(ThemeVariant.Default, application.RequestedThemeVariant);
            Assert.Equal(
                ThemeRegistry.FromActualVariant(ThemeVariant.Dark),
                selection.ResolveVisual(ThemeVariant.Dark));
            Assert.Equal(
                ThemeRegistry.FromActualVariant(ThemeVariant.Light),
                selection.ResolveVisual(ThemeVariant.Light));
        }, CancellationToken.None);
    }

    [Fact]
    public async Task DialogDecorationDetachesScrollViewerBeforeReparenting()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessTestApplication));
        await session.Dispatch(() =>
        {
            var scrollViewer = new ScrollViewer
            {
                Content = new TextBlock { Text = "显示格式设置" }
            };
            var window = new Window
            {
                Width = 420,
                Height = 260,
                Content = scrollViewer
            };

            ThemeChrome.ApplyDialogDecoration(window);

            var layer = Assert.IsType<ThemeChromeLayer>(window.Content);
            Assert.Same(scrollViewer, layer.Children[0]);
            try
            {
                window.Show();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Assert.Contains(scrollViewer, window.GetVisualDescendants());
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ImportedGameIconCatalogRendersEverySvgPath()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessTestApplication));
        await session.Dispatch(() =>
        {
            var application = Assert.IsType<HeadlessTestApplication>(global::Avalonia.Application.Current);
            var icons = IsekaiThemeIconPack.Names
                .Select(iconName => new LocalIcon
                {
                    IconName = iconName,
                    Width = 24,
                    Height = 24,
                    Margin = new Thickness(2)
                })
                .ToArray();
            var panel = new WrapPanel { Width = 480 };
            foreach (var icon in icons)
            {
                panel.Children.Add(icon);
            }

            var window = new Window
            {
                Width = 520,
                Height = 220,
                Content = panel
            };

            try
            {
                ThemeApplicationService.Apply(application, IsekaiTheme.SettingsValue);
                window.Show();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();

                Assert.Equal(IsekaiThemeIconPack.Names.Count, icons.Length);
                Assert.All(icons, icon => Assert.Equal(IsekaiTheme.Variant, icon.ActualThemeVariant));
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }
}

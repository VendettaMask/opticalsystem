using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Services;
using OptilandWorkbench.App.Theming;

namespace OptilandWorkbench.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class ThemeRuntimeTests
{
    [Fact]
    public async Task PixelThemeReachesLiveFluentButtonAndTextBoxTemplates()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OptilandWorkbench.App.App));
        await session.Dispatch(() =>
        {
            var application = Assert.IsType<global::OptilandWorkbench.App.App>(global::Avalonia.Application.Current);
            ThemeApplicationService.Apply(application, PixelTheme.SettingsValue);
            var button = new Button { Content = "应用" };
            var input = new TextBox { Text = "10" };
            var window = new Window
            {
                Width = 280,
                Height = 140,
                Content = new StackPanel
                {
                    Margin = new Thickness(16),
                    Spacing = 10,
                    Children = { input, button }
                }
            };

            try
            {
                window.Show();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();

                Assert.Equal(PixelTheme.Variant, button.ActualThemeVariant);
                Assert.Equal(Color.FromRgb(221, 239, 242), Assert.IsAssignableFrom<ISolidColorBrush>(button.Background).Color);
                Assert.Equal(Color.FromRgb(23, 50, 77), Assert.IsAssignableFrom<ISolidColorBrush>(button.BorderBrush).Color);
                Assert.Equal(Color.FromRgb(255, 249, 220), Assert.IsAssignableFrom<ISolidColorBrush>(input.Background).Color);
                Assert.Equal(new CornerRadius(0), button.CornerRadius);
                Assert.Equal(new CornerRadius(0), input.CornerRadius);
                var presenter = button.GetVisualDescendants()
                    .OfType<ContentPresenter>()
                    .Single(control => control.Name == "PART_ContentPresenter");
                Assert.NotEqual(default, presenter.BoxShadow);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RuntimeTypographyRefreshRescalesExistingLocalFontSizesExactlyOnce()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(HeadlessTestApplication));
        await session.Dispatch(() =>
        {
            var defaults = new AppSettings { FontSize = AppSettings.DefaultFontSize };
            var enlarged = new AppSettings { FontSize = AppSettings.DefaultFontSize * 2 };
            DisplayTypography.Configure(defaults);
            var title = new TextBlock
            {
                Text = "Section",
                FontSize = DisplayTypography.SectionTitle
            };
            var body = new TextBlock
            {
                Text = "Body",
                FontSize = DisplayTypography.Body
            };
            var root = new UserControl
            {
                Content = new StackPanel { Children = { title, body } }
            };

            try
            {
                DisplayTypography.Configure(enlarged);
                DisplayTypography.ApplyRecursively(root, defaults.FontSize);
                DisplayTypography.ApplyRecursively(root, defaults.FontSize);

                Assert.Equal(32, title.FontSize, precision: 12);
                Assert.Equal(26, body.FontSize, precision: 12);
                Assert.Equal(26, root.FontSize, precision: 12);
            }
            finally
            {
                DisplayTypography.Configure(defaults);
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RuntimeSwitchUpdatesPackageResourcesWithoutChangingLayoutBounds()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(HeadlessTestApplication));
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

                ThemeApplicationService.Apply(application, PixelTheme.SettingsValue);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Assert.Equal(PixelTheme.Variant, content.ActualThemeVariant);
                Assert.Equal(PixelThemeIconPack.Instance.Id, ThemeIconResolver.PackId(content.ActualThemeVariant));
                Assert.Equal(new CornerRadius(0), content.CornerRadius);
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
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(HeadlessTestApplication));
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
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(HeadlessTestApplication));
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
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(HeadlessTestApplication));
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

    [Fact]
    public async Task IsekaiDecorationReusesRenderResourcesDuringResizeFrames()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(HeadlessTestApplication));
        await session.Dispatch(() =>
        {
            var application = Assert.IsType<HeadlessTestApplication>(global::Avalonia.Application.Current);
            ThemeApplicationService.Apply(application, IsekaiTheme.SettingsValue);
            var overlay = new ThemeChromeOverlay
            {
                Role = ThemeChromeRole.Ribbon,
                Width = 733,
                Height = 48
            };
            var window = new Window
            {
                Width = 733,
                Height = 48,
                Content = overlay
            };

            try
            {
                window.Show();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var bitmap = new RenderTargetBitmap(new PixelSize(960, 64));
                bitmap.Render(overlay);
                var afterFirstRender = IsekaiThemeDecorationRenderer.Instance.BladeGeometryBuildCount;

                for (var frame = 0; frame < 12; frame++)
                {
                    bitmap.Render(overlay);
                }

                Assert.Equal(
                    afterFirstRender,
                    IsekaiThemeDecorationRenderer.Instance.BladeGeometryBuildCount);

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                const int frames = 48;
                for (var frame = 0; frame < frames; frame++)
                {
                    var width = 733 + ((frame % 4) * 47);
                    window.Width = width;
                    overlay.Width = width;
                    overlay.Measure(new Size(width, 48));
                    overlay.Arrange(new Rect(0, 0, width, 48));
                    if (frame == frames / 2)
                    {
                        ThemeApplicationService.Apply(application, "Light");
                        ThemeApplicationService.Apply(application, IsekaiTheme.SettingsValue);
                    }

                    bitmap.Render(overlay);
                }

                var allocatedPerFrame =
                    (GC.GetAllocatedBytesForCurrentThread() - allocatedBefore) / frames;

                Assert.True(
                    allocatedPerFrame < 96_000,
                    $"Isekai decoration render allocated {allocatedPerFrame:N0} bytes per frame.");
                var cachedResourceCount = typeof(IsekaiThemeDecorationRenderer)
                    .GetFields(BindingFlags.NonPublic | BindingFlags.Static)
                    .Count(field =>
                        typeof(IImmutableBrush).IsAssignableFrom(field.FieldType)
                        || typeof(IPen).IsAssignableFrom(field.FieldType));
                Assert.True(cachedResourceCount >= 30);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }
}

using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using OpticalSystemDesign.App;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Formatting;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class ReferenceWorkbenchLayoutTests
{
    public static AppBuilder BuildAvaloniaApp()
    {
        var capture = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_WORKBENCH_CAPTURE_DIR"));
        var builder = AppBuilder.Configure<global::OpticalSystemDesign.App.App>();
        if (capture) builder.UseSkia();
        return builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = !capture }).UseHarfBuzz();
    }

    [Theory]
    [InlineData("Light", 1280, 13)]
    [InlineData("Light", 1024, 18)]
    [InlineData("Light", 640, 13)]
    [InlineData("Dark", 1280, 13)]
    [InlineData("Pixel", 1280, 13)]
    [InlineData("Isekai", 1280, 13)]
    public async Task RealWorkspaceKeepsReferenceChromeWithoutReplacingProductData(string theme, double width, double fontSize)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(ReferenceWorkbenchLayoutTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            var settings = new AppSettings { FontSize = fontSize, Theme = theme, LeftPaneWidth = 256 };
            DisplayTypography.Configure(settings);
            using var application = WorkbenchApplication.Create();
            using var panels = new PanelManager(application, settings,
                new WorkspaceSessionStore(Path.Combine(Path.GetTempPath(), "reference-workbench", Guid.NewGuid().ToString("N"))));
            var snapshot = application.Documents.GetSnapshot();
            var commands = new[]
            {
                Button("file-plus", "新建"), Button("folder-open", "打开"), Button("save", "保存"),
                Button("download", "导入", true), Button("upload", "导出", true), Button("aperture", "示例", true),
                Button("undo", "撤销"), Button("redo", "重做")
            };
            MainWindow.UpdateHistoryButtons(commands[6], commands[7], snapshot.CanUndo, snapshot.CanRedo);
            var groups = new[]
            {
                Group("文件", true, commands[..3]), Group("交换与示例", true, commands[3..6]), Group("编辑", false, commands[6..])
            };
            var page = (Control)Invoke("BuildRibbonPage", new object[] { groups });
            var names = new[] { "文件", "设置", "视图", "分析", "优化", "公差", "加工与图纸", "数据库", "实验室", "窗口", "帮助" };
            var tabs = new TabControl
            {
                SelectedIndex = 0,
                ItemsSource = names.Select(name => (TabItem)Invoke("RibbonTab", name, name == "文件" ? page : new Border())).ToArray()
            };
            var ribbon = MainWindow.BuildRibbonFrame(tabs);
            var status = MainWindow.BuildStatusBarFrame(
                new TextBlock { Text = snapshot.Status },
                new TextBlock { Text = $"EFFL: {Invoke("FormatMetric", snapshot.EffectiveFocalLength)}" },
                new TextBlock { Text = $"F/#: {Invoke("FormatMetric", snapshot.FNumber)}" },
                new TextBlock { Text = $"APER: {NumericDisplayFormatter.Format(snapshot.ApertureValue)}" },
                new TextBlock { Text = $"TOTR: {Invoke("FormatMetric", snapshot.TotalTrack)}" });
            var shell = MainWindow.BuildShellLayout(new ContentControl { Content = ribbon }, panels.WorkspaceControl, status);
            var actions = new ActionManager();
            actions.Register("show-lens-editor", "镜头数据", "设置", () => panels.Show(WorkspacePanelId.LensEditor));
            var window = new Window { Title = snapshot.Name + " - 顺序模式 - Optical System Design", Width = width, Height = 820 };
            window.Content = MainWindowTitleBar.Wrap(window, shell, new CommandSearchControl(actions));
            DisplayTypography.Apply(window);
            window.Show();
            try
            {
                Tick(window);
                panels.ApplyLayout(new WorkspaceLayoutState(256));
                Tick(window);
                var sidebar = Assert.Single(window.GetVisualDescendants().OfType<SystemPropertiesPanel>());
                var editor = Assert.Single(window.GetVisualDescendants().OfType<LensEditorPanel>());
                var sections = Assert.IsType<StackPanel>(Assert.IsType<ScrollViewer>(sidebar.Content).Content);
                var sidebarScroll = Assert.IsType<ScrollViewer>(sidebar.Content);
                Assert.Equal(Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden, sidebarScroll.VerticalScrollBarVisibility);
                Assert.False(sidebarScroll.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ScrollBar>()
                    .Single(bar => bar.Name == "PART_VerticalScrollBar"
                        && bar.GetVisualAncestors().OfType<ScrollViewer>().First() == sidebarScroll).IsVisible);
                var sidebarViewport = sidebarScroll.GetVisualDescendants().OfType<ScrollContentPresenter>().First();
                foreach (var input in sidebar.GetVisualDescendants().OfType<ComboBox>().Where(input => input.IsEffectivelyVisible))
                    Assert.True(input.TranslatePoint(new Point(input.Bounds.Width, 0), sidebarViewport)!.Value.X <= sidebarViewport.Bounds.Width + 1);
                Assert.InRange(sidebar.Bounds.Width, 236, 280);
                Assert.Equal(7, sections.Children.Count);
                var grid = Assert.Single(editor.GetVisualDescendants().OfType<DataGrid>());
                Assert.Equal(snapshot.SurfaceCount, grid.ItemsSource!.Cast<object>().Count());
                Assert.True(grid.Columns.Count >= 12); // Keep columns omitted from the illustrative mockup too.
                Assert.Equal(2, panels.Factory.Descriptors.Count);
                Assert.Equal(WorkspaceDockFactory.LensDocumentId, panels.Factory.PrimaryDocumentDock!.ActiveDockable!.Id);
                var viewportTop = panels.WorkspaceControl.TranslatePoint(default, window)!.Value.Y;
                var ribbonBottom = ribbon.TranslatePoint(new Point(0, ribbon.Bounds.Height), window)!.Value.Y;
                Assert.Equal(8, viewportTop - ribbonBottom);
                Assert.True(status.Bounds.Bottom <= shell.Bounds.Height);
                if (theme == "Light")
                {
                    var chrome = Assert.Single(window.GetVisualDescendants().OfType<ToolChromeControl>());
                    var toolHeader = Assert.Single(chrome.GetVisualDescendants().OfType<Border>(),
                        border => border.TemplatedParent == chrome && border.Name == "PART_Border");
                    Assert.Equal(new CornerRadius(12, 12, 0, 0), toolHeader.CornerRadius);
                    Assert.True(toolHeader.ClipToBounds);
                    Assert.Equal(new Thickness(5, 0, 5, 6), Assert.IsType<Grid>(panels.WorkspaceControl.Parent).Margin);
                    Assert.Equal(new Thickness(6, 4, 6, 12), sections.Margin);
                    Assert.Equal(6, sections.Spacing);
                    Assert.All(sections.Children.Cast<Border>(), card => Assert.Equal(new CornerRadius(6), card.CornerRadius));
                    Assert.Same(BlueThemeTokens.StatusBarGradient, status.Background);
                    Assert.Same(BlueThemeTokens.RibbonPageGradient, ((ScrollViewer)page).Background);
                    var selected = Assert.Single(window.GetVisualDescendants().OfType<DocumentTabStripItem>(), tab => tab.IsSelected);
                    Assert.Equal(BlueThemeTokens.Surface, Assert.IsAssignableFrom<ISolidColorBrush>(selected.Background).Color);
                    var body = Assert.Single(selected.GetVisualDescendants().OfType<Border>(), border => border.Name == "PART_TabBody");
                    Assert.Equal(new Thickness(0, 0, 2, 0), body.BorderThickness);
                    Assert.Equal(BlueThemeTokens.Accent, Assert.IsAssignableFrom<ISolidColorBrush>(body.BorderBrush).Color);
                }
                ThemeIconResolver.TryResolve(sidebar.ActualThemeVariant, "circle-question-mark", out var fallback);
                foreach (var icon in sidebar.GetVisualDescendants().OfType<LocalIcon>().Where(icon => icon.Classes.Contains("system-section-icon")))
                {
                    Assert.True(ThemeIconResolver.TryResolve(sidebar.ActualThemeVariant, icon.IconName, out var resolved));
                    Assert.NotSame(fallback, resolved);
                }
                Capture(window, $"workbench-{theme}-{width:0}-{fontSize:0}");
                if (theme == "Light")
                {
                    var document = Assert.Single(window.GetVisualDescendants().OfType<DocumentControl>());
                    document.IsActive = true;
                    var strip = Assert.Single(document.GetVisualDescendants().OfType<DocumentTabStrip>());
                    strip.IsActive = true;
                    Tick(window);
                    var stripeHost = Assert.Single(document.GetVisualDescendants().OfType<Panel>(),
                        panel => panel.TemplatedParent == document && panel.Name == "PART_DocumentSeperatorHost");
                    Assert.Equal(0, stripeHost.MaxHeight);
                    Assert.Equal(0, stripeHost.Bounds.Height);
                    Assert.True(stripeHost.ClipToBounds);
                    var frame = Assert.Single(document.GetVisualDescendants().OfType<Border>(),
                        border => border.TemplatedParent == document && border.Name == "PART_Border");
                    Assert.Equal(1, frame.BorderThickness.Top);
                    Assert.Equal(BlueThemeTokens.Divider, Assert.IsAssignableFrom<ISolidColorBrush>(frame.BorderBrush).Color);
                    Capture(window, $"workbench-{theme}-{width:0}-{fontSize:0}-active");
                }
            }
            finally { window.Close(); DisplayTypography.Configure(new AppSettings()); }
        }, CancellationToken.None);
    }

    private static Button Button(string icon, string label, bool dropdown = false)
    {
        var button = dropdown ? new DropDownButton() : new Button();
        button.Content = Invoke(dropdown ? "RibbonDropDownCommandContent" : "RibbonCommandContent", icon, label);
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.VerticalContentAlignment = VerticalAlignment.Center;
        button.Classes.Add("ribbon-command");
        if (dropdown) button.Classes.Add("ribbon-dropdown");
        Invoke("BindRibbonCommandLayout", button);
        Invoke("BindRibbonCommandChrome", button);
        Invoke("AttachRibbonCommandHover", button, button.Content);
        return button;
    }

    private static Control Group(string title, bool divider, Button[] buttons) =>
        (Control)Invoke("RibbonGroup", title, divider, buttons.Cast<Control>().ToArray());

    private static object Invoke(string name, params object?[] arguments) =>
        typeof(MainWindow).GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(method => method.Name == name && method.GetParameters().Length == arguments.Length)
            .Invoke(null, arguments)!;

    private static void Tick(Window window)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }

    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_WORKBENCH_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Width * 2, (int)window.Height * 2), new Vector(192, 192));
        bitmap.Render(window);
        bitmap.Save(Path.Combine(directory, name + ".png"), PngBitmapEncoderOptions.Default);
    }
}

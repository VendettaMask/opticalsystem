using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Services;
using Path = System.IO.Path;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class UserGuidePanelTests
{
    [Fact]
    public async Task GuideIsSearchableLocalizedAndCoversEveryRegisteredCommand()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OpticalSystemDesign.App.App));
        await session.Dispatch(() =>
        {
            UiLocalization.InstallGlobalHooks();
            UiLocalization.Apply(UiLocalization.Chinese);
            var actions = new ActionManager();
            actions.Register("new", "新建空白系统", "文件", () => { });
            actions.Register("future-command", "未来功能", "工具", () => { });
            foreach (var analysis in WorkbenchAnalysisCatalog.AllRibbonCommands)
            {
                actions.Register(analysis.Id, analysis.Name, "分析", () => { });
            }

            var panel = new UserGuidePanel(actions.Actions);
            var window = new Window { Width = 1080, Height = 720, Content = panel };
            try
            {
                window.Show();
                Tick(window);
                Assert.Equal(actions.Actions.Count, panel.CoveredCommandIds.Count);
                Assert.All(actions.Actions, action => Assert.Contains(action.Id, panel.CoveredCommandIds));
                Assert.True(panel.ChapterCount >= 20);
                Assert.Equal(panel.ChapterCount, panel.IllustratedChapterCount);
                Assert.Equal(27, panel.CalculationChapterCount);
                Assert.Equal(9, panel.TutorialChapterCount);
                Assert.Equal("推荐工作流", Named<TextBlock>(panel, "UserGuideTitle").Text);
                Assert.False(Named<Expander>(panel, "UserGuideWorkflowDiagram").IsExpanded);
                ExpandDiagram(panel, window);
                var firstFigure = Named<Border>(panel, "UserGuideIllustration");
                Assert.Contains("推荐顺序", AutomationProperties.GetName(firstFigure));
                Assert.True(firstFigure.GetVisualDescendants().OfType<Shape>().Count() >= 5);

                Named<TextBox>(panel, "UserGuideSearch").Text = "界面与工作模式";
                Tick(window);
                var screenshot = Named<Image>(panel, "UserGuideScreenshot");
                Assert.NotNull(screenshot.Source);
                Assert.Equal("主工作台真实界面截图", AutomationProperties.GetName(screenshot));

                Named<ComboBox>(panel, "UserGuideKindFilter").SelectedIndex = 0;
                Named<TextBox>(panel, "UserGuideSearch").Text = "future-command";
                Tick(window);
                Assert.Equal("工具", Named<TextBlock>(panel, "UserGuideTitle").Text);
                Assert.Contains("future-command", VisibleText(panel));

                UiLocalization.Apply(UiLocalization.English);
                Tick(window);
                Assert.Equal("Tools", Named<TextBlock>(panel, "UserGuideTitle").Text);
                Assert.Equal(
                    "Search the guide…",
                    Named<TextBox>(panel, "UserGuideSearch").PlaceholderText);
                Assert.Contains("Run future command", VisibleText(panel));
                ExpandDiagram(panel, window);
                Assert.Contains("find every feature", AutomationProperties.GetName(Named<Border>(panel, "UserGuideIllustration")));

                UiLocalization.Apply(UiLocalization.Japanese);
                Tick(window);
                Assert.Equal("ツール", Named<TextBlock>(panel, "UserGuideTitle").Text);
                Assert.Contains("future command", VisibleText(panel));
                ExpandDiagram(panel, window);
                Assert.Contains("全機能", AutomationProperties.GetName(Named<Border>(panel, "UserGuideIllustration")));

                Named<TextBox>(panel, "UserGuideSearch").Text = "停靠预览";
                Tick(window);
                Assert.Contains("灰色枠", VisibleText(panel));
                UiLocalization.Apply(UiLocalization.English);
                Tick(window);
                Assert.Contains("translucent gray frame", VisibleText(panel));
                UiLocalization.Apply(UiLocalization.Chinese);
                Tick(window);
                Assert.Contains("半透明灰框", VisibleText(panel));
            }
            finally
            {
                UiLocalization.Apply(UiLocalization.Chinese);
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CalculationFilterNavigationAndEmptySearchStayConsistent()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OpticalSystemDesign.App.App));
        await session.Dispatch(() =>
        {
            UiLocalization.Apply(UiLocalization.Chinese);
            var panel = new UserGuidePanel([]);
            var window = new Window { Width = 1180, Height = 800, Content = panel };
            try
            {
                window.Show(); Tick(window);
                Named<ComboBox>(panel, "UserGuideKindFilter").SelectedIndex = 2; Tick(window);
                Assert.Contains("27 /", Named<TextBlock>(panel, "UserGuideResultCount").Text);
                Assert.Equal("计算总览：从处方到结果", Named<TextBlock>(panel, "UserGuideTitle").Text);
                Click(Named<Button>(panel, "UserGuideNext")); Tick(window);
                Assert.Equal("单位、坐标与材料色散", Named<TextBlock>(panel, "UserGuideTitle").Text);
                Named<TextBox>(panel, "UserGuideSearch").Text = "calculation-paraxial"; Tick(window);
                Assert.Contains(panel.GetVisualDescendants().OfType<UserGuideMathView>(), f => f.FormulaId == "paraxial");
                Assert.Contains(@"\frac{dy}{dz}", UserGuideMath.All["paraxial"].Latex);
                UiLocalization.Apply(UiLocalization.English); Tick(window);
                Assert.Equal("First-order optics and focal length", Named<TextBlock>(panel, "UserGuideTitle").Text);
                Named<TextBox>(panel, "UserGuideSearch").Text = "no-such-guide-chapter"; Tick(window);
                Assert.Equal("No matching chapters", Named<TextBlock>(panel, "UserGuideTitle").Text);
                Assert.Empty(Named<StackPanel>(panel, "UserGuideSectionLinks").Children);
                Assert.False(Named<Button>(panel, "UserGuideNext").IsEnabled);
                Assert.False(Named<Button>(panel, "UserGuidePrevious").IsEnabled);
                Named<TextBox>(panel, "UserGuideSearch").Text = "Zernike Standard";
                Named<ComboBox>(panel, "UserGuideKindFilter").SelectedIndex = 3; Tick(window);
                var calculationLink = panel.GetVisualDescendants().OfType<Button>().First(b =>
                    b.Name == "UserGuideCalculationLink" && b.Content is TextBlock text && text.Text?.Contains("Zernike coefficients") == true);
                Click(calculationLink); Tick(window);
                Assert.Equal("How Zernike coefficients are fitted", Named<TextBlock>(panel, "UserGuideTitle").Text);
                Assert.True(Named<Button>(panel, "UserGuideExportPdf").IsVisible);
            }
            finally { window.Close(); UiLocalization.Apply(UiLocalization.Chinese); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(UiLocalization.Chinese, "Light", 1440)]
    [InlineData(UiLocalization.English, "Light", 1200)]
    [InlineData(UiLocalization.Japanese, "Light", 1200)]
    [InlineData(UiLocalization.Chinese, "Dark", 1200)]
    [InlineData(UiLocalization.Chinese, "Light", 520)]
    public async Task ReaderReflowsWithoutShrinkingTextAndScreenshotsCanEnlarge(string language, string theme, int width)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OpticalSystemDesign.App.App));
        await session.Dispatch(() =>
        {
            UiLocalization.Apply(language);
            OpticalSystemDesign.App.Theming.ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            var panel = new UserGuidePanel([]);
            var window = new Window { Width = width, Height = 860, Content = panel };
            try
            {
                window.Show(); Tick(window);
                Named<TextBox>(panel, "UserGuideSearch").Text = "tutorial-first-system"; Tick(window);
                Named<TextBox>(panel, "UserGuideSearch").Text = ""; Tick(window);
                var scroller = Named<ScrollViewer>(panel, "UserGuideReaderScroll");
                Assert.True(scroller.IsVisible);
                Assert.Equal(30, Named<TextBlock>(panel, "UserGuideTitle").FontSize);
                Assert.InRange(Named<StackPanel>(panel, "UserGuideReadingPage").Bounds.Width, 200, UserGuidePanel.ReadingWidth);
                Assert.True(scroller.Extent.Width <= scroller.Viewport.Width + 1);
                if (width >= 760)
                {
                    var tree = Named<TreeView>(panel, "UserGuideTree");
                    var viewport = tree.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ScrollContentPresenter>().Single();
                    foreach (var label in tree.GetVisualDescendants().OfType<TreeViewItem>().Select(item => item.Header).OfType<TextBlock>())
                    {
                        var right = label.TranslatePoint(new Point(label.Bounds.Width, 0), viewport)!.Value.X;
                        Assert.True(right <= viewport.Bounds.Width + 1, $"Clipped chapter: {label.Text}");
                    }
                }
                if (width < 760)
                {
                    Click(Named<Button>(panel, "UserGuideNavigationToggle")); Tick(window);
                    Assert.False(scroller.IsVisible);
                    Click(Named<Button>(panel, "UserGuideNavigationToggle")); Tick(window);
                    Assert.True(scroller.IsVisible);
                }
                var captureDirectory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_GUIDE_READER_CAPTURE_DIR");
                if (!string.IsNullOrWhiteSpace(captureDirectory))
                {
                    Directory.CreateDirectory(captureDirectory);
                    using var image = new RenderTargetBitmap(new Avalonia.PixelSize(width, 860), new Avalonia.Vector(96, 96));
                    image.Render(window);
                    using var output = File.Create(Path.Combine(captureDirectory, $"guide-{language}-{theme}-{width}.png"));
                    image.Save(output, new PngBitmapEncoderOptions());
                }
                var enlarge = panel.GetVisualDescendants().OfType<Button>().First(b => b.Name == "UserGuideEnlargeScreenshot");
                Click(enlarge); Tick(window);
                var dialog = Assert.Single(window.OwnedWindows);
                Assert.Equal("UserGuideScreenshotWindow", dialog.Name);
                Click(Named<Button>(dialog, "UserGuideScreenshotActualSize")); Tick(dialog);
                var fullImage = dialog.GetVisualDescendants().OfType<Image>().Single();
                Assert.Equal(1440, fullImage.Width);
                Click(Named<Button>(dialog, "UserGuideScreenshotFit")); Tick(dialog);
                Assert.True(double.IsNaN(fullImage.Width));
                dialog.Close(); Tick(window);
                if (!string.IsNullOrWhiteSpace(captureDirectory) && width == 1440 && language == UiLocalization.Chinese)
                {
                    Named<TextBox>(panel, "UserGuideSearch").Text = "材料库、镜头库与内置模块"; Tick(window);
                    Assert.Equal("材料库、镜头库与内置模块", Named<TextBlock>(panel, "UserGuideTitle").Text);
                    Named<TextBox>(panel, "UserGuideSearch").Text = ""; Tick(window);
                    Assert.False(Named<Expander>(panel, "UserGuideWorkflowDiagram").IsExpanded);
                    SavePage("guide-resources.png");
                    Named<TextBox>(panel, "UserGuideSearch").Text = "";
                    Named<ComboBox>(panel, "UserGuideKindFilter").SelectedIndex = 2;
                    Named<TextBox>(panel, "UserGuideSearch").Text = "calculation-paraxial"; Tick(window);
                    Named<TextBox>(panel, "UserGuideSearch").Text = ""; Tick(window);
                    SavePage("guide-calculation.png");
                }

                void SavePage(string file)
                {
                    using var image = new RenderTargetBitmap(new Avalonia.PixelSize(width, 860), new Avalonia.Vector(96, 96));
                    image.Render(window);
                    using var output = File.Create(Path.Combine(captureDirectory!, file));
                    image.Save(output, new PngBitmapEncoderOptions());
                }
            }
            finally { window.Close(); UiLocalization.Apply(UiLocalization.Chinese); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ChapterContentsMoveWithTheViewportAndKeepWorking()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OpticalSystemDesign.App.App));
        await session.Dispatch(() =>
        {
            UiLocalization.Apply(UiLocalization.Chinese);
            var panel = new UserGuidePanel([]);
            var window = new Window { Width = 1440, Height = 680, Content = panel };
            try
            {
                window.Show(); Tick(window);
                Named<ComboBox>(panel, "UserGuideKindFilter").SelectedIndex = 2;
                Named<TextBox>(panel, "UserGuideSearch").Text = "calculation-paraxial"; Tick(window);
                Named<TextBox>(panel, "UserGuideSearch").Text = ""; Tick(window);
                var title = Named<TextBlock>(panel, "UserGuideTitle").Text;
                Assert.True(Named<Border>(panel, "UserGuidePageContents").IsVisible);
                Assert.False(Named<Expander>(panel, "UserGuideInlineContents").IsVisible);
                var formula = panel.GetVisualDescendants().OfType<UserGuideMathView>().First(f => f.FormulaId == "paraxial");
                var formulaScroller = (ScrollViewer)formula.Parent!;
                var center = formula.TranslatePoint(new Avalonia.Point(formula.Bounds.Width / 2, 0), formulaScroller)!.Value.X;
                Assert.InRange(Math.Abs(center - formulaScroller.Viewport.Width / 2), 0, 2);
                var links = Named<StackPanel>(panel, "UserGuideSectionLinks");
                var scroller = Named<ScrollViewer>(panel, "UserGuideReaderScroll");
                Click(links.Children.OfType<Button>().Last()); Tick(window);
                Assert.True(scroller.Offset.Y > 100);

                window.Width = 1000; Tick(window);
                Assert.False(Named<Border>(panel, "UserGuidePageContents").IsVisible);
                var inline = Named<Expander>(panel, "UserGuideInlineContents");
                Assert.True(inline.IsVisible);
                scroller.Offset = default;
                inline.IsExpanded = true; Tick(window);
                Click(links.Children.OfType<Button>().Last()); Tick(window);
                Assert.False(inline.IsExpanded);
                Assert.True(scroller.Offset.Y > 100);

                window.Width = 360; Tick(window);
                Assert.True(scroller.IsVisible);
                Assert.Equal(title, Named<TextBlock>(panel, "UserGuideTitle").Text);
                Assert.Equal(30, Named<TextBlock>(panel, "UserGuideTitle").FontSize);
                Assert.True(scroller.Extent.Width <= scroller.Viewport.Width + 1);
                Assert.Contains(panel.GetVisualDescendants().OfType<ScrollViewer>(), s =>
                    s.Name == "UserGuideFormulaScroll" && s.Extent.Width > s.Viewport.Width);

                window.Width = 1440; Tick(window);
                Assert.Equal(title, Named<TextBlock>(panel, "UserGuideTitle").Text);
                Assert.True(Named<Border>(panel, "UserGuidePageContents").IsVisible);
                Assert.True(Named<Border>(panel, "UserGuideNavigation").IsVisible);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task EveryLocalizedScreenshotIsEmbeddedAndDecodes()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OpticalSystemDesign.App.App));
        await session.Dispatch(() =>
        {
            foreach (var language in new[] { UiLocalization.Chinese, UiLocalization.English, UiLocalization.Japanese })
                foreach (var entry in UserGuideScreenshots.All)
                {
                    using var stream = AssetLoader.Open(UserGuideScreenshots.Resource(entry.Key, language));
                    using var bitmap = new Bitmap(stream);
                    Assert.True(bitmap.PixelSize.Width >= 600, entry.Key);
                    Assert.True(bitmap.PixelSize.Height >= 400, entry.Key);
                }
        }, CancellationToken.None);
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void ExpandDiagram(Control panel, Window window)
    {
        Named<Expander>(panel, "UserGuideWorkflowDiagram").IsExpanded = true;
        Tick(window);
    }

    [Fact]
    public void UserGuideIsAPersistableWorkspaceDocumentType()
    {
        Assert.Equal("user-guide", WorkspaceDocumentTypes.UserGuide);
        Assert.True(WorkspaceDocumentTypes.IsKnown(WorkspaceDocumentTypes.UserGuide));
    }

    private static string VisibleText(Control root) => string.Join(
        "\n",
        root.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text));

    private static T Named<T>(Control root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);

    private static void Tick(Window window)
    {
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }
}

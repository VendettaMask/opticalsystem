using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Automation;
using Avalonia.Headless;
using Avalonia.VisualTree;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;

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
                Assert.Equal("推荐工作流", Named<TextBlock>(panel, "UserGuideTitle").Text);
                var firstFigure = Named<Border>(panel, "UserGuideIllustration");
                Assert.Contains("推荐顺序", AutomationProperties.GetName(firstFigure));
                Assert.True(firstFigure.GetVisualDescendants().OfType<Shape>().Count() >= 5);

                Named<TextBox>(panel, "UserGuideSearch").Text = "界面与工作模式";
                Tick(window);
                var screenshot = Named<Image>(panel, "UserGuideScreenshot");
                Assert.NotNull(screenshot.Source);
                Assert.Equal("主工作台真实界面截图", AutomationProperties.GetName(screenshot));

                Named<TextBox>(panel, "UserGuideSearch").Text = "future-command";
                Tick(window);
                Assert.Equal("工具", Named<TextBlock>(panel, "UserGuideTitle").Text);
                Assert.Contains("future-command", VisibleText(panel));

                UiLocalization.Apply(UiLocalization.English);
                Tick(window);
                Assert.Equal("Tools", Named<TextBlock>(panel, "UserGuideTitle").Text);
                Assert.Equal(
                    "Search chapters, commands, analyses, or keywords",
                    Named<TextBox>(panel, "UserGuideSearch").PlaceholderText);
                Assert.Contains("Run future command", VisibleText(panel));
                Assert.Contains("find every feature", AutomationProperties.GetName(Named<Border>(panel, "UserGuideIllustration")));

                UiLocalization.Apply(UiLocalization.Japanese);
                Tick(window);
                Assert.Equal("ツール", Named<TextBlock>(panel, "UserGuideTitle").Text);
                Assert.Contains("future command", VisibleText(panel));
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

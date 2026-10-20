using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class SystemPropertiesLocalizationTests
{
    [Theory]
    [InlineData("en-US", "Delete Field", "Delete Wavelength", "X Phase / °")]
    [InlineData("ja-JP", "この視野を削除", "この波長を削除", "X 位相 / °")]
    public async Task ExpandedRealEditorsTranslateTheirButtonsDescriptionsAndCanonicalDataStaysUnchanged(
        string language, string deleteField, string deleteWavelength, string phase)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OpticalSystemDesign.App.App));
        await session.Dispatch(() =>
        {
            UiLocalization.InstallGlobalHooks();
            UiLocalization.Apply(UiLocalization.Chinese);
            using var application = WorkbenchApplication.Create();
            using var panel = new SystemPropertiesPanel(application.Prescription, application.Materials, application.Events);
            var revision = application.Events.Revision;
            var window = new Window { Width = 280, Height = 900, Content = panel };
            try
            {
                window.Show(); Tick(window);
                foreach (var section in ((StackPanel)((ScrollViewer)panel.Content!).Content!).Children.Cast<Border>())
                {
                    var body = (StackPanel)section.Child!;
                    if (!body.Children[1].IsVisible)
                        ((Button)body.Children[0]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                Tick(window);
                foreach (var button in panel.GetVisualDescendants().OfType<Button>().Where(button =>
                             button.Classes.Contains("system-property-card-header")
                             && !button.Classes.Contains("system-section-header")).ToArray())
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Tick(window);
                UiLocalization.Apply(language); Tick(window);
                var displayed = Text(panel);
                Assert.Contains(deleteField, displayed);
                Assert.Contains(deleteWavelength, displayed);
                Assert.Contains(phase, displayed);
                foreach (var source in new[] { "删除此视场", "删除此波长", "X 相位 / °", "Y 相位 / °",
                             "CODA 始终使用所填 Jones 输入；RRET 使用参考轴。照度偏振计算遵循非偏振开关。",
                             "当前列表按从上到下的顺序解析未指定厂商的玻璃名称；至少保留一个目录。",
                             "当前仅保存环境参数，暂不启用温度补偿计算。" })
                {
                    Assert.DoesNotContain(source, displayed);
                    Assert.Contains(UiLocalization.Text(source), displayed);
                }
                Assert.Equal(revision, application.Events.Revision);
                UiLocalization.Apply(UiLocalization.Chinese); Tick(window);
                Assert.Contains("删除此视场", Text(panel));
                Assert.Contains("删除此波长", Text(panel));
                Assert.Equal(revision, application.Events.Revision);
            }
            finally { window.Close(); UiLocalization.Apply(UiLocalization.Chinese); }
        }, CancellationToken.None);
    }

    private static string[] Text(Control panel) => panel.GetVisualDescendants().OfType<TextBlock>()
        .Where(block => block.IsEffectivelyVisible).Select(block => block.Text ?? string.Empty).ToArray();

    private static void Tick(Window window)
    {
        window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); window.UpdateLayout();
    }
}

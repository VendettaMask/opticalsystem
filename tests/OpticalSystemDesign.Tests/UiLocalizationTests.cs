using Avalonia.Controls;
using Avalonia.Headless;
using OpticalSystemDesign.App.Services;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class UiLocalizationTests
{
    [Fact]
    public void SettingsDefaultToChineseAndNormalizeUnsupportedLanguages()
    {
        var settings = new AppSettings();
        Assert.Equal(UiLocalization.Chinese, settings.Language);

        settings.Language = "fr-FR";
        settings.NormalizeDisplaySettings();
        Assert.Equal(UiLocalization.Chinese, settings.Language);

        settings.Language = UiLocalization.Japanese;
        settings.NormalizeDisplaySettings();
        Assert.Equal(UiLocalization.Japanese, settings.Language);
    }

    [Fact]
    public async Task LanguageChangeUpdatesLoadedDynamicAndLaterControlsWithoutChangingUserInput()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OpticalSystemDesign.App.App));
        await session.Dispatch(() =>
        {
            UiLocalization.InstallGlobalHooks();
            UiLocalization.Apply(UiLocalization.Chinese);
            var label = new TextBlock { Text = "保存" };
            var button = new Button { Content = "取消" };
            var input = new TextBox { Text = "用户镜头名称", PlaceholderText = "搜索" };
            var apertureType = new ComboBox
            {
                ItemsSource = new[] { "入瞳直径", "像方 F 数", "物方数值孔径", "按光阑面尺寸浮动" },
                SelectedIndex = 0
            };
            var wavelengthTitle = new TextBlock { Text = "波长 1" };
            var wavelengthSummary = new TextBlock { Text = "0.588 µm · 权重 1 · 主波长" };
            var surfaceTitle = new TextBlock { Text = "表面 1 属性" };
            var window = new Window
            {
                Title = "操作失败",
                Content = new StackPanel
                {
                    Children = { label, button, input, apertureType, wavelengthTitle, wavelengthSummary, surfaceTitle }
                }
            };

            try
            {
                window.Show();
                window.UpdateLayout();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                UiLocalization.Apply(UiLocalization.English);

                Assert.Equal("Save", label.Text);
                Assert.Equal("Cancel", button.Content);
                Assert.Equal("Search", input.PlaceholderText);
                Assert.Equal("用户镜头名称", input.Text);
                Assert.Equal("Operation Failed", window.Title);
                Assert.Equal("入瞳直径", apertureType.SelectedItem);
                Assert.Equal("Entrance Pupil Diameter", UiLocalization.Text("入瞳直径"));
                Assert.Equal("Image-Space F/#", UiLocalization.Text("像方 F 数"));
                Assert.Equal("Object-Space Numerical Aperture", UiLocalization.Text("物方数值孔径"));
                Assert.Equal("Float by Stop Surface Size", UiLocalization.Text("按光阑面尺寸浮动"));
                Assert.Equal("Wavelength 1", wavelengthTitle.Text);
                Assert.Equal("0.588 µm · Weight 1 · Primary Wavelength", wavelengthSummary.Text);
                Assert.Equal("Surface 1 Properties", surfaceTitle.Text);
                Assert.Equal("Surface Type", UiLocalization.Text("表面类型"));
                Assert.Equal("Radius of Curvature", UiLocalization.Text("曲率半径"));
                Assert.Equal("Clear Semi-Diameter", UiLocalization.Text("净口径"));
                Assert.Equal("Object", UiLocalization.Text("物面"));
                Assert.Equal("Image", UiLocalization.Text("像面"));
                Assert.Equal("Standard", UiLocalization.Text("标准面"));
                Assert.Equal("Infinity", UiLocalization.Text("无限"));

                label.Text = "刷新";
                Assert.Equal("Refresh", label.Text);

                var later = new TextBlock { Text = "删除" };
                ((StackPanel)window.Content!).Children.Add(later);
                window.UpdateLayout();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Assert.Equal("Delete", later.Text);

                UiLocalization.Apply(UiLocalization.Japanese);
                Assert.Equal("更新", label.Text);
                Assert.Equal("キャンセル", button.Content);
                Assert.Equal("検索", input.PlaceholderText);
                Assert.Equal("用户镜头名称", input.Text);
                Assert.Equal("操作に失敗しました", window.Title);
                Assert.Equal("入瞳直径", apertureType.SelectedItem);
                Assert.Equal("入射瞳径", UiLocalization.Text("入瞳直径"));
                Assert.Equal("像側 F ナンバー", UiLocalization.Text("像方 F 数"));
                Assert.Equal("物体側開口数", UiLocalization.Text("物方数值孔径"));
                Assert.Equal("絞り面サイズに連動", UiLocalization.Text("按光阑面尺寸浮动"));
                Assert.Equal("波長 1", wavelengthTitle.Text);
                Assert.Equal("0.588 µm · 重み 1 · 主波長", wavelengthSummary.Text);
                Assert.Equal("面 1 プロパティ", surfaceTitle.Text);
                Assert.Equal("面タイプ", UiLocalization.Text("表面类型"));
                Assert.Equal("曲率半径", UiLocalization.Text("曲率半径"));
                Assert.Equal("有効半径", UiLocalization.Text("净口径"));
                Assert.Equal("物体面", UiLocalization.Text("物面"));
                Assert.Equal("像面", UiLocalization.Text("像面"));
                Assert.Equal("標準面", UiLocalization.Text("标准面"));
                Assert.Equal("無限", UiLocalization.Text("无限"));
            }
            finally
            {
                UiLocalization.Apply(UiLocalization.Chinese);
                window.Close();
            }
        }, CancellationToken.None);
    }
}

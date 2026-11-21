using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class MaterialLibraryConsistencyTests
{
    public static AppBuilder BuildAvaloniaApp() => DiagramViewportFitTests.BuildAvaloniaApp();

    [Theory]
    [InlineData("Light", "zh-CN", 1400, 960, 1)]
    [InlineData("Dark", "ja-JP", 1100, 960, 1.5)]
    [InlineData("Isekai", "en-US", 900, 1100, 2)]
    public async Task DataReviewStatusAndReportFollowLanguageWithoutChangingCatalogValues(string theme, string language, int width, int height, double scale)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(MaterialLibraryConsistencyTests));
        await session.Dispatch(() =>
        {
            DisplayTypography.Configure(new() { Theme = theme });
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            UiLocalization.Apply(language);
            using var app = WorkbenchApplication.Create("cooke");
            var before = app.Documents.GetRecoveryState(); var revision = app.Events.Revision;
            var panel = new MaterialLibraryPanel(app.Materials);
            var window = new Window { Width = width, Height = height, Content = panel };
            try
            {
                window.Show(); window.SetRenderScaling(scale); Tick(window); UiLocalization.Attach(window);
                Field<ComboBox>(panel, "_catalog").SelectedItem = "HIKARI";
                Field<ListBox>(panel, "_glassList").SelectedItem = "7054"; Tick(window);
                var glass = app.Materials.GetGlasses().Single(g => g.Manufacturer == "HIKARI" && g.Name == "7054");
                Assert.True(glass.RequiresThermalDataReview);
                var review = Field<TextBlock>(panel, "_dataQuality");
                Assert.Contains("dn/dT=", review.Text);
                Assert.Equal(UiLocalization.Text(glass.Status), Field<TextBox>(panel, "_state").Text);
                foreach (var label in new[] { "最小波长：", "最大波长：" })
                    Assert.Contains(panel.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == UiLocalization.Text(label));
                foreach (var key in new[] { "最小波长", "最大波长" })
                    Assert.EndsWith("µm", Field<Dictionary<string, TextBox>>(panel, "_properties")[key].Text);
                if (language == UiLocalization.English)
                    Assert.DoesNotMatch(@"[\u4e00-\u9fff]", review.Text!);
                var sourceComment = "原始备注（不翻译） / source text unchanged";
                var report = MaterialLibraryPanel.BuildGlassReport(glass with { Comment = sourceComment }, language: language);
                Assert.Contains(sourceComment, report);
                Assert.Contains(UiLocalization.Text("有效波长", language), report);
                Assert.Contains("µm", report);
                Assert.Contains(UiLocalization.Text(MaterialLibraryPanel.DataQualityText(glass), language), report);
                Assert.Contains(glass.ThermalCoefficients[0].ToString(System.Globalization.CultureInfo.InvariantCulture), report);
                Capture(window, $"material-review-{theme}.png");
                if (theme == "Light")
                {
                    UiLocalization.Apply(UiLocalization.English); Tick(window);
                    Assert.Equal(UiLocalization.Text(glass.Status), Field<TextBox>(panel, "_state").Text);
                    Assert.DoesNotMatch(@"[\u4e00-\u9fff]", review.Text!);
                    UiLocalization.Apply(UiLocalization.Japanese); Tick(window);
                    Assert.Equal(UiLocalization.Text(glass.Status), Field<TextBox>(panel, "_state").Text);
                    UiLocalization.Apply(UiLocalization.Chinese); Tick(window);
                    Assert.Equal(glass.Status, Field<TextBox>(panel, "_state").Text);
                }
                Assert.Equal(glass.ThermalCoefficients, app.Materials.GetGlasses().Single(g => g.Manufacturer == "HIKARI" && g.Name == "7054").ThermalCoefficients);
                Assert.Equal(before, app.Documents.GetRecoveryState()); Assert.Equal(revision, app.Events.Revision);
            }
            finally { window.Close(); UiLocalization.Apply(UiLocalization.Chinese); DisplayTypography.Configure(new()); }
        }, CancellationToken.None);
    }

    private static T Field<T>(object source, string name) => (T)source.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(source)!;
    private static void Tick(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("OPTICAL_REMAINING_CAPTURE_DIR");
        if (directory is null) return;
        Directory.CreateDirectory(directory); using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }
}

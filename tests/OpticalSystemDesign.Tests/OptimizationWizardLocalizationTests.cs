using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class OptimizationWizardLocalizationTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<global::OpticalSystemDesign.App.App>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseHarfBuzz();

    [Theory]
    [InlineData(13, 1.5)]
    [InlineData(18, 2)]
    public async Task IsekaiWizardLabelsChoicesSummaryAndDisabledReasonsUseEnglishAndAlmendra(double size, double scaling)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(OptimizationWizardLocalizationTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Isekai");
            DisplayTypography.Configure(new AppSettings { Theme = "Isekai", FontFamily = "Arial", FontSize = size });
            using var app = WorkbenchApplication.Create("cooke");
            var original = app.Documents.GetRecoveryState();
            var window = new OptimizationWizardWindow(app.Prescription, app.Optimization) { Width = 1040, Height = 860 };
            try
            {
                window.Show(); window.SetRenderScaling(scaling); Tick(window);
                Assert.Equal("Optimization Wizard", window.Title);
                Assert.Equal(ProductFonts.Isekai, window.FontFamily);
                var title = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Optimization Wizard and Operands");
                Assert.All(title.TextLayout.TextLines.SelectMany(l => l.TextRuns).OfType<ShapedTextRun>(),
                    run => Assert.Equal("Almendra", run.GlyphRun.GlyphTypeface.FamilyName));
                AssertEnglish(window);
                Capture(window, $"wizard-{size}.png");
                var quality = Field<ComboBox>(window, "_quality");
                var reference = Field<ComboBox>(window, "_reference");
                var sampling = Field<ComboBox>(window, "_sampling");
                var preview = Field<TextBlock>(window, "_preview");
                foreach (var mode in new[] { 0, 1, 2, 3 })
                {
                    quality.SelectedIndex = mode; Tick(window);
                    AssertEnglish(window);
                    Assert.Contains("Current selection:", preview.Text);
                    for (var method = 0; method < 2; method++)
                    {
                        sampling.SelectedIndex = method; Tick(window); AssertEnglish(window);
                        Assert.Equal(method == 0, Field<NumericUpDown>(window, "_arms").IsEnabled);
                        if (method == 1) Assert.Contains("Gaussian quadrature", Assert.IsType<string>(ToolTip.GetTip(Field<NumericUpDown>(window, "_arms"))));
                    }
                }
                quality.SelectedIndex = 0;
                foreach (var index in new[] { 0, 1, 2 })
                {
                    reference.SelectedIndex = index; Tick(window); AssertEnglish(window);
                    Assert.Contains(index == 0 ? "OPDX" : index == 1 ? "OPDM" : "OPDC", preview.Text);
                }
                quality.SelectedIndex = 1; Field<NumericUpDown>(window, "_spatialFrequency").Value = 0; Tick(window);
                Assert.False(Field<Button>(window, "_generate").IsEnabled);
                Assert.Contains("Generation requires", preview.Text);
                AssertEnglish(window);
                Field<NumericUpDown>(window, "_spatialFrequency").Value = 30; Tick(window);
                Assert.True(Field<Button>(window, "_generate").IsEnabled);
                Assert.DoesNotContain("Generation requires", preview.Text);
                Assert.Equal(original, app.Documents.GetRecoveryState());
            }
            finally { window.Close(); ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light"); UiLocalization.Apply(UiLocalization.Chinese); DisplayTypography.Configure(new AppSettings()); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task LanguageChangesRefreshOpenWizardAndGenerateTheSameOperands()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(OptimizationWizardLocalizationTests));
        await session.Dispatch(() =>
        {
            var application = Avalonia.Application.Current!;
            ThemeApplicationService.Apply(application, "Light"); DisplayTypography.Configure(new AppSettings());
            using var app = WorkbenchApplication.Create("cooke");
            var window = new OptimizationWizardWindow(app.Prescription, app.Optimization);
            try
            {
                UiLocalization.Apply(UiLocalization.Chinese); window.Show(); Tick(window);
                var quality = Field<ComboBox>(window, "_quality");
                var preview = Field<TextBlock>(window, "_preview");
                Assert.Contains("当前组合：点列图", preview.Text);
                var selected = quality.SelectedItem;
                UiLocalization.Apply(UiLocalization.English); Tick(window); AssertEnglish(window);
                Assert.Contains("Current selection: Spot Diagram", preview.Text);
                Assert.Equal(selected, quality.SelectedItem);
                UiLocalization.Apply(UiLocalization.Japanese); Tick(window);
                Assert.Contains("現在の組み合わせ：", preview.Text);
                UiLocalization.Apply(UiLocalization.Chinese); Tick(window);
                Assert.Contains("当前组合：点列图", preview.Text);
            }
            finally { window.Close(); }

            MeritOperandRowDto[] Generate(string theme, string language)
            {
                ThemeApplicationService.Apply(application, theme); UiLocalization.Apply(language);
                using var model = WorkbenchApplication.Create("cooke");
                var dialog = new OptimizationWizardWindow(model.Prescription, model.Optimization);
                try
                {
                    dialog.Show(); Tick(dialog);
                    Field<ComboBox>(dialog, "_quality").SelectedIndex = 2;
                    Field<ComboBox>(dialog, "_sampling").SelectedIndex = 1;
                    Field<NumericUpDown>(dialog, "_rings").Value = 4;
                    Field<NumericUpDown>(dialog, "_arms").Value = 8;
                    Field<NumericUpDown>(dialog, "_obscuration").Value = .2m;
                    Field<NumericUpDown>(dialog, "_weightScale").Value = 2;
                    Field<CheckBox>(dialog, "_allWavelengths").IsChecked = false;
                    typeof(OptimizationWizardWindow).GetMethod("Generate", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dialog, null);
                    return model.Optimization.GetMeritFunction().ToArray();
                }
                finally { dialog.Close(); }
            }
            try
            {
                var chinese = Generate("Light", UiLocalization.Chinese);
                var english = Generate("Isekai", UiLocalization.English);
                Assert.NotEmpty(english);
                Assert.Equal(chinese, english);
                Assert.Contains(english, row => row.Type == "TRCX" && row.PupilRings == 4 && row.PupilObscuration == .2);
            }
            finally { ThemeApplicationService.Apply(application, "Light"); UiLocalization.Apply(UiLocalization.Chinese); DisplayTypography.Configure(new AppSettings()); }
        }, CancellationToken.None);
    }

    private static void AssertEnglish(Window window)
    {
        var labels = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).ToArray();
        Assert.NotEmpty(labels);
        Assert.DoesNotContain(labels, t => t.Text is { } value && value.Any(c => c is >= '\u4e00' and <= '\u9fff'));
    }
    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
    private static void Tick(Window window)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
    }
    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_WIZARD_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory); using var image = window.CaptureRenderedFrame(); Assert.NotNull(image);
        image.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }
}

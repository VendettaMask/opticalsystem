using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core.Domain;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class LensScaleWindowTests
{
    public static AppBuilder BuildAvaloniaApp() => DiagramViewportFitTests.BuildAvaloniaApp();

    [Theory]
    [InlineData("Light", "zh-CN", 530, 13)]
    [InlineData("Dark", "en-US", 530, 13)]
    [InlineData("Lumen", "ja-JP", 530, 13)]
    [InlineData("Isekai", "en-US", 530, 13)]
    [InlineData("Pixel", "zh-CN", 530, 13)]
    [InlineData("Light", "ja-JP", 430, 18)]
    public async Task NativeDialogHasFactorRangeUnitControlsAndSafeValidation(string theme, string language, int width, int fontSize)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(LensScaleWindowTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            UiLocalization.Apply(language); DisplayTypography.Configure(new() { Theme = theme, FontSize = fontSize });
            using var app = WorkbenchApplication.Create("cooke");
            var before = app.Documents.GetSnapshot();
            var window = new LensScaleWindow(app.Prescription) { Width = width, Height = fontSize == 18 ? 700 : 480 };
            try
            {
                window.Show(); Tick(window);
                var factor = Find<TextBox>(window, "LensScaleFactor");
                var first = Find<ComboBox>(window, "LensScaleFirstSurface");
                var last = Find<ComboBox>(window, "LensScaleLastSurface");
                var unit = Find<ComboBox>(window, "LensScaleTargetUnit");
                var apply = Find<Button>(window, "LensScaleConfirm");
                Assert.Equal("1", factor.Text); Assert.True(factor.IsEnabled); Assert.False(unit.IsEnabled);
                Assert.Equal(0, first.SelectedIndex); Assert.Equal(before.SurfaceCount - 1, last.SelectedIndex);
                factor.Text = "0"; apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Tick(window);
                Assert.True(Find<TextBlock>(window, "LensScaleError").IsVisible);
                Assert.Equal(before, app.Documents.GetSnapshot()); Assert.True(window.IsVisible);
                factor.Text = "1";
                Find<RadioButton>(window, "LensScaleUnitMode").IsChecked = true; Tick(window);
                Assert.False(factor.IsEnabled); Assert.False(first.IsEnabled); Assert.False(last.IsEnabled); Assert.True(unit.IsEnabled);
                unit.SelectedItem = LensLengthUnit.Inch;
                var footerPoint = apply.TranslatePoint(new Point(0, apply.Bounds.Height), window)!.Value;
                Assert.InRange(footerPoint.Y, 0, window.Bounds.Height);
                var capture = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_LENS_SCALE_CAPTURE_DIR");
                if (!string.IsNullOrWhiteSpace(capture))
                {
                    Directory.CreateDirectory(capture);
                    using var image = window.CaptureRenderedFrame(); Assert.NotNull(image);
                    image.Save(Path.Combine(capture, $"lens-scale-{theme}-{width}.png"), PngBitmapEncoderOptions.Default);
                }
                apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.False(window.IsVisible);
                Assert.Equal(LensLengthUnit.Inch, app.Prescription.GetLensScaleSettings().Unit);
                Assert.Equal(before.EffectiveFocalLength, app.Documents.GetSnapshot().EffectiveFocalLength);
                Assert.True(app.Documents.Undo()); Assert.Equal(LensLengthUnit.Millimeter, app.Prescription.GetLensScaleSettings().Unit);
            }
            finally { window.Close(); DisplayTypography.Configure(new()); UiLocalization.Apply("zh-CN"); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task FactorApplyAndCancelFollowSingleUndoBoundary()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(LensScaleWindowTests));
        await session.Dispatch(() =>
        {
            using var app = WorkbenchApplication.Create("cooke");
            var before = app.Prescription.GetSurfaces()[1].Radius;
            var dialog = new LensScaleWindow(app.Prescription); dialog.Show(); Tick(dialog);
            Find<TextBox>(dialog, "LensScaleFactor").Text = "2";
            Find<Button>(dialog, "LensScaleConfirm").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(dialog.IsVisible); Assert.Equal(before * 2, app.Prescription.GetSurfaces()[1].Radius);
            Assert.True(app.Documents.Undo()); Assert.Equal(before, app.Prescription.GetSurfaces()[1].Radius);
            var cancel = new LensScaleWindow(app.Prescription); cancel.Show(); Tick(cancel);
            Find<TextBox>(cancel, "LensScaleFactor").Text = "3";
            Find<Button>(cancel, "LensScaleCancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(before, app.Prescription.GetSurfaces()[1].Radius); Assert.False(cancel.IsVisible);
        }, CancellationToken.None);
    }

    private static T Find<T>(Window window, string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);

    private static void Tick(Window window)
    {
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
    }
}

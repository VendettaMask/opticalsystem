using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Laboratories;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Services;
using CoatingWindow = OpticalSystemDesign.App.Laboratories.CoatingDesign.MainWindow;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class LaboratoryPresentationTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<global::OpticalSystemDesign.App.App>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseHarfBuzz();

    [Theory]
    [InlineData("coating", "Light", "zh-CN", 1200, 900, 13)]
    [InlineData("coating", "Dark", "en-US", 1100, 800, 13)]
    [InlineData("coating", "Light", "ja-JP", 820, 680, 18)]
    [InlineData("assembly", "Light", "zh-CN", 1260, 850, 13)]
    [InlineData("assembly", "Dark", "en-US", 1100, 760, 13)]
    [InlineData("assembly", "Light", "ja-JP", 760, 680, 18)]
    [InlineData("camera", "Light", "zh-CN", 1280, 900, 13)]
    [InlineData("camera", "Dark", "en-US", 1000, 760, 13)]
    [InlineData("camera", "Light", "ja-JP", 760, 680, 18)]
    public async Task LaboratoryCardsKeepCommandsAndEditorsReachableAcrossLanguagesAndSizes(
        string module, string theme, string language, int width, int height, double fontSize)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(LaboratoryPresentationTests));
        await session.Dispatch(() =>
        {
            var previousLanguage = UiLocalization.CurrentLanguage;
            DisplayTypography.Configure(new AppSettings { Theme = theme, FontSize = fontSize });
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme); UiLocalization.Apply(language);
            using var app = WorkbenchApplication.Create();
            Window window = module switch
            {
                "coating" => new CoatingWindow(),
                "assembly" => new OpticalAssemblyWindow(app),
                _ => new CameraImagingWindow(app)
            };
            try
            {
                window.Width = width; window.Height = height; window.Show(); Tick(window);
                var revision = app.Events.Revision;
                var fingerprint = (window as CoatingWindow)?.CurrentExperiment.Fingerprint();
                Assert.True(window.TryFindResource(ThemeResourceBindings.Workspace, window.ActualThemeVariant, out var workspace));
                Assert.Same(workspace, window.Background);
                var header = window.GetVisualDescendants().OfType<Control>().Single(c => c.Name == "LaboratoryHeader");
                var commandButtons = header.GetVisualDescendants().OfType<Button>().ToArray();
                Assert.NotEmpty(commandButtons);
                foreach (var button in commandButtons)
                {
                    var bounds = InWindow(button);
                    Assert.InRange(bounds.Left, 0, window.ClientSize.Width);
                    Assert.InRange(bounds.Right, 0, window.ClientSize.Width + .1);
                    Assert.InRange(bounds.Bottom, 0, window.ClientSize.Height);
                }
                Capture(window, $"{module}-{language}-{theme}-{width}-overview");
                var scroll = module == "coating"
                    ? Assert.IsType<ScrollViewer>(window.GetVisualDescendants().OfType<Border>().Single(c => c.Name == "CoatingSidebar").Child)
                    : window.GetVisualDescendants().OfType<ScrollViewer>().Single(c => c.Name == (module == "camera" ? "CameraSettings" : "AssemblySettings"));
                foreach (var disclosure in scroll.GetLogicalDescendants().OfType<Expander>().ToArray()) disclosure.IsExpanded = true;
                Tick(window);
                var editors = scroll.GetVisualDescendants().OfType<Control>()
                    .Where(c => c is TextBox or NumericUpDown or ComboBox or AutoCompleteBox)
                    .Where(c => c.IsEffectivelyVisible)
                    .Where(c => !c.GetVisualAncestors().TakeWhile(parent => parent != scroll)
                        .Any(parent => parent is TextBox or NumericUpDown or ComboBox or AutoCompleteBox)).ToArray();
                Assert.NotEmpty(editors);
                foreach (var editor in editors)
                {
                    editor.BringIntoView(); Tick(window);
                    var bounds = InWindow(editor); var viewport = InWindow(scroll);
                    Assert.True(bounds.Width > 45 && bounds.Height > 20, $"{module}: {editor.Name}: {bounds}");
                    Assert.True(bounds.Left >= viewport.Left - .5 && bounds.Right <= viewport.Right + .5,
                        $"{module}: clipped editor {editor.Name}: {bounds}; viewport={viewport}");
                    Assert.InRange(bounds.Center.Y, viewport.Top - .5, viewport.Bottom + .5);
                }
                var lastValues = editors.Select(Value).ToArray();
                UiLocalization.Apply(language == UiLocalization.English ? UiLocalization.Chinese : UiLocalization.English);
                ThemeApplicationService.Apply(Avalonia.Application.Current!, theme == "Light" ? "Dark" : "Light"); Tick(window);
                Assert.Equal(lastValues, editors.Select(Value));
                Assert.Equal(revision, app.Events.Revision);
                Assert.Equal(fingerprint, (window as CoatingWindow)?.CurrentExperiment.Fingerprint());
                UiLocalization.Apply(language); ThemeApplicationService.Apply(Avalonia.Application.Current!, theme); Tick(window);
                Capture(window, $"{module}-{language}-{theme}-{width}-scrolled");
                Rect InWindow(Control control) => new Rect(control.Bounds.Size).TransformToAABB(control.TransformToVisual(window)!.Value);
            }
            finally
            {
                if (window is CoatingWindow coating) coating.CloseAfterHostApproval(); else window.Close();
                UiLocalization.Apply(previousLanguage); DisplayTypography.Configure(new AppSettings());
            }
        }, CancellationToken.None);
    }

    private static object? Value(Control control) => control switch
    {
        NumericUpDown number => number.Value,
        TextBox text => text.Text,
        ComboBox choice => choice.SelectedIndex,
        AutoCompleteBox text => text.Text,
        _ => null
    };

    private static void Tick(Window window)
    { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }

    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("OPTICAL_LAB_PRESENTATION_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var bitmap = window.CaptureRenderedFrame(); Assert.NotNull(bitmap);
        bitmap.Save(Path.Combine(directory, name + ".png"), PngBitmapEncoderOptions.Default);
    }
}

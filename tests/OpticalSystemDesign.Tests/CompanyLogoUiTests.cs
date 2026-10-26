using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class CompanyLogoUiTests
{
    [Theory]
    [InlineData("Light", 13, UiLocalization.Chinese)]
    [InlineData("Dark", 13, UiLocalization.English)]
    [InlineData("Light", 18, UiLocalization.Japanese)]
    public async Task BrandSurfacesShareVectorArtworkAndRetainOrdinaryPublisherText(string theme, double fontSize, string language)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(OpticalSystemDesign.App.App));
        await session.Dispatch(() =>
        {
            var previousLanguage = UiLocalization.CurrentLanguage;
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            UiLocalization.Apply(language);
            DisplayTypography.Configure(new AppSettings { FontSize = fontSize });
            using var app = WorkbenchApplication.Create("cooke");
            using var portraitStream = AssetLoader.Open(new Uri("avares://OpticalSystemDesign.App/Assets/Author.jpg"));
            using var portrait = new Bitmap(portraitStream);
            var splash = new SplashWindow();
            var about = MainWindow.CreateAboutDialog(portrait);
            var view = new AnalysisViewDto("Brand test", [], "", [], new(), [], 1);
            var footer = Assert.IsAssignableFrom<Control>(typeof(AnalysisPanel)
                .GetMethod("BuildAnalysisTitleBlock", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [view, app.Documents.GetSnapshot(), DateTimeOffset.Now]));
            var analysis = new Window { Width = 940, Height = 220, Content = footer };
            using var panel = new OpticalDrawingPanel(app.Prescription, app.Materials, app.Events, app.Visualization);
            var drawing = new Window { Width = 1160, Height = 720, Content = panel };
            var windows = new[] { splash, about, analysis, drawing };
            try
            {
                foreach (var window in windows)
                {
                    window.Show();
                    Tick(window);
                    var logo = Assert.Single(window.GetVisualDescendants().OfType<CompanyLogoControl>());
                    Assert.IsType<DrawingImage>(logo.Source);
                    Assert.True(logo.IsEffectivelyVisible);
                    Assert.True(logo.Bounds.Width > 100 && logo.Bounds.Height > 10);
                    var point = logo.TranslatePoint(default, window)!.Value;
                    Assert.InRange(point.X, 0, window.ClientSize.Width - logo.Bounds.Width);
                    Assert.InRange(point.Y, 0, window.ClientSize.Height - logo.Bounds.Height);
                    Capture(window, $"{logo.Name}-{theme}-{fontSize:0}-{language}.png");
                }
                var publisher = Assert.Single(about.GetVisualDescendants().OfType<TextBlock>(), text => text.Name == "AboutPublisher");
                Assert.Equal(UiLocalization.Text("S.T.A.R. Labs 出品"), publisher.Text);
                var themedLogo = Assert.Single(analysis.GetVisualDescendants().OfType<CompanyLogoControl>());
                var before = Assert.IsType<DrawingImage>(themedLogo.Source);
                ThemeApplicationService.Apply(Avalonia.Application.Current!, theme == "Light" ? "Dark" : "Light");
                Tick(analysis);
                Assert.NotSame(before, themedLogo.Source);
                Assert.IsType<DrawingImage>(themedLogo.Source);
            }
            finally
            {
                foreach (var window in windows) window.Close();
                UiLocalization.Apply(previousLanguage);
                DisplayTypography.Configure(new AppSettings());
            }
        }, CancellationToken.None);
    }

    private static void Tick(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }

    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_BRAND_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Width * 2, (int)window.Height * 2), new Vector(192, 192));
        bitmap.Render(window);
        bitmap.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }
}

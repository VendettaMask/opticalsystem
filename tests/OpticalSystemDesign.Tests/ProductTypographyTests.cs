using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class ProductTypographyTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<global::OpticalSystemDesign.App.App>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseHarfBuzz();

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    [InlineData("Isekai")]
    [InlineData("Lumen")]
    public async Task MixedTextUsesBundledLatinChineseAndNumericFaces(string theme)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(ProductTypographyTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            DisplayTypography.Configure(new AppSettings());
            var label = new TextBlock { Text = "Optical 镜头系统 ±50 μm" };
            var numeric = new TextBlock { Text = "0123456789 -1.234E+05 镜头", Classes = { ProductFonts.NumericClass } };
            var window = new Window { Width = 700, Height = 200, Content = new StackPanel { Children = { label, numeric } } };
            DisplayTypography.Apply(window);
            try
            {
                window.Show(); Tick(window);
                var uiFamilies = Faces(label);
                Assert.Contains(uiFamilies, family => family.StartsWith("DM Sans", StringComparison.Ordinal));
                Assert.Contains("Source Han Sans SC", uiFamilies);
                Assert.All(uiFamilies, family => Assert.True(family.StartsWith("DM Sans", StringComparison.Ordinal) || family == "Source Han Sans SC", family));
                Assert.Contains("JetBrains Mono", Faces(numeric));
                Assert.Contains("Source Han Sans SC", Faces(numeric));
                var face = new Typeface(ProductFonts.Numeric).GlyphTypeface;
                var widths = "0123456789".Select(c =>
                {
                    Assert.True(face.CharacterToGlyphMap.TryGetGlyph(c, out var glyph));
                    return new FormattedText(c.ToString(), System.Globalization.CultureInfo.InvariantCulture,
                        Avalonia.Media.FlowDirection.LeftToRight, new Typeface(ProductFonts.Numeric), 13, Brushes.Black).Width;
                }).ToArray();
                Assert.All(widths, width => Assert.Equal(widths[0], width));
            }
            finally { window.Close(); DisplayTypography.Configure(new AppSettings()); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task BundledChineseHeadingsAndNumericEmphasisUseRealBoldFaces()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(ProductTypographyTests));
        await session.Dispatch(() =>
        {
            foreach (var (family, expected) in new[]
            {
                (new FontFamily(ProductFonts.SelectableFamilies[0]), "Source Han Sans SC"),
                (new FontFamily(ProductFonts.SelectableFamilies[2]), "JetBrains Mono")
            })
            {
                var face = new Typeface(family, weight: FontWeight.Bold).GlyphTypeface;
                Assert.Equal(expected, face.FamilyName);
                Assert.Equal(FontWeight.Bold, face.Weight);
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ExistingNumericControlsFollowThemeAndExplicitFontChanges()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(ProductTypographyTests));
        await session.Dispatch(() =>
        {
            var app = Avalonia.Application.Current!;
            ThemeApplicationService.Apply(app, "Light");
            DisplayTypography.Configure(new AppSettings());
            var numeric = new TextBlock { Text = "123.456", Classes = { ProductFonts.NumericClass } };
            var input = new NumericUpDown { Value = 123.456m };
            var window = new Window { Width = 400, Height = 240, Content = new StackPanel { Children = { numeric, input } } };
            try
            {
                window.Show(); DisplayTypography.Apply(window); Tick(window);
                Assert.Equal(ProductFonts.Numeric, numeric.FontFamily);
                Assert.Equal(ProductFonts.Numeric, input.FontFamily);
                ThemeApplicationService.Apply(app, "Pixel");
                DisplayTypography.Apply(window); Tick(window);
                Assert.Equal(ThemeRegistry.FromSettings("Pixel").UiFontFamily, numeric.FontFamily);
                Assert.Equal(window.FontFamily, input.FontFamily);
                Assert.All(Faces(numeric), family => Assert.Equal("Fusion Pixel 10px Mono zh_hans", family));
                var custom = ProductFonts.SelectableFamilies[0];
                DisplayTypography.Configure(new AppSettings { FontFamily = custom, FontSize = 18 });
                DisplayTypography.ApplyRecursively(window, 13); Tick(window);
                Assert.Equal(new FontFamily(custom), window.FontFamily);
                Assert.Equal(window.FontFamily, numeric.FontFamily);
                Assert.Equal(window.FontFamily, input.FontFamily);
                Assert.Equal(18, window.FontSize);
                ThemeApplicationService.Apply(app, "Dark");
                DisplayTypography.Configure(new AppSettings());
                DisplayTypography.ApplyRecursively(window, 18); Tick(window);
                Assert.Equal(ProductFonts.Interface, window.FontFamily);
                Assert.Equal(ProductFonts.Numeric, numeric.FontFamily);
                Assert.Equal(13, window.FontSize);
            }
            finally { window.Close(); DisplayTypography.Configure(new AppSettings()); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task PixelRegularAndBoldStayInThePixelFileForLatinAndChinese()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(ProductTypographyTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Pixel");
            DisplayTypography.Configure(new AppSettings());
            var normal = new TextBlock { Text = "Object Image 新建 系统选项 0123456789" };
            var bold = new TextBlock { Text = normal.Text, FontWeight = FontWeight.Bold };
            var window = new Window { Width = 800, Height = 220, Content = new StackPanel { Children = { normal, bold } } };
            DisplayTypography.Apply(window);
            try
            {
                window.Show(); Tick(window);
                Assert.All(Faces(normal).Concat(Faces(bold)), family => Assert.Equal("Fusion Pixel 10px Mono zh_hans", family));
            }
            finally { window.Close(); DisplayTypography.Configure(new AppSettings()); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task LensNumericCellsAndEditorsUseNumericRoleWithoutChangingTheDocument()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(ProductTypographyTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
            DisplayTypography.Configure(new AppSettings());
            using var app = WorkbenchApplication.Create();
            var revision = app.Events.Revision;
            using var editor = new LensEditorPanel(app.Prescription, app.Events, new SurfaceSelectionService());
            var window = new Window { Width = 1500, Height = 600, Content = editor };
            DisplayTypography.Apply(window);
            try
            {
                window.Show(); Tick(window);
                var inputs = editor.GetVisualDescendants().OfType<TextBox>().Where(t => t.Classes.Contains(ProductFonts.NumericClass)).ToArray();
                Assert.NotEmpty(inputs);
                Assert.All(inputs, input => Assert.Equal(ProductFonts.Numeric, input.FontFamily));
                var grid = editor.GetVisualDescendants().OfType<DataGrid>().Single();
                var conic = grid.Columns.Single(c => c.Header is TextBlock { Text: "圆锥系数" });
                var conicText = Assert.IsType<TextBlock>(conic.GetCellContent(grid.ItemsSource!.Cast<object>().First()));
                Assert.Equal(ProductFonts.Numeric, conicText.GetVisualAncestors()
                    .OfType<DataGridCell>().First().FontFamily);
                Assert.Contains("JetBrains Mono", Faces(conicText));
                var labelColumn = grid.Columns.Single(c => Equals(c.Header, "标注"));
                var label = Assert.IsType<TextBlock>(labelColumn.GetCellContent(grid.ItemsSource!.Cast<object>().First()));
                Assert.All(Faces(label), family => Assert.StartsWith("DM Sans", family));
                Assert.Equal(revision, app.Events.Revision);
            }
            finally { window.Close(); DisplayTypography.Configure(new AppSettings()); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task DisplaySettingsPreservesCustomFontAndPreviewsSelectedThemeDefault()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(ProductTypographyTests));
        await session.Dispatch(() =>
        {
            const string custom = "Unavailable user font";
            DisplayTypography.Configure(new AppSettings());
            var window = new DisplaySettingsWindow(new AppSettings { FontFamily = custom });
            try
            {
                window.Show(); Tick(window);
                Assert.Equal(ProductFonts.Interface, window.FontFamily);
                Assert.Equal(custom, Field<ComboBox>(window, "_fontFamily").SelectedItem);
                Assert.Contains(ProductFonts.SelectableFamilies[0], Field<ComboBox>(window, "_fontFamily").Items.Cast<string>());
                typeof(DisplaySettingsWindow).GetMethod("ResetControls", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
                Assert.Equal(ProductFonts.Numeric, Field<TextBlock>(window, "_numericPreview").FontFamily);
                Field<ComboBox>(window, "_theme").SelectedItem = ThemeRegistry.FromSettings("Pixel");
                Tick(window);
                Assert.Equal(ThemeRegistry.FromSettings("Pixel").UiFontFamily, Field<TextBlock>(window, "_preview").FontFamily);
                Assert.Equal(ThemeRegistry.FromSettings("Pixel").UiFontFamily, Field<TextBlock>(window, "_numericPreview").FontFamily);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private static string[] Faces(TextBlock text) => text.TextLayout.TextLines.SelectMany(line => line.TextRuns)
        .OfType<ShapedTextRun>().Select(run => run.GlyphRun.GlyphTypeface.FamilyName).Distinct().ToArray();
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static void Tick(Window window)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
    }
}

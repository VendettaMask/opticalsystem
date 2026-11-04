using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Polygon = Avalonia.Controls.Shapes.Polygon;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class IsekaiTypographyPolicyTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<global::OpticalSystemDesign.App.App>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseHarfBuzz();

    [Fact]
    public async Task ExistingControlsAndDrawingTypefaceUseRealAlmendraFacesDespiteCustomPreferences()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(IsekaiTypographyPolicyTests));
        await session.Dispatch(() =>
        {
            var app = Avalonia.Application.Current!;
            var settings = new AppSettings { FontFamily = ProductFonts.SelectableFamilies[2], Language = UiLocalization.Japanese };
            DisplayTypography.Configure(settings);
            UiLocalization.Apply(settings.Language);
            var label = new TextBlock { Text = "Optical System: Sword & Magic" };
            var number = new TextBlock { Text = "0123456789 -1.234E+05", Classes = { ProductFonts.NumericClass } };
            var input = new NumericUpDown { Value = 123.456m };
            var userName = new TextBox { Text = "用户镜头名称" };
            var faces = new[]
            {
                new TextBlock { Text = "Almendra Regular" },
                new TextBlock { Text = "Almendra Bold", FontWeight = FontWeight.Bold },
                new TextBlock { Text = "Almendra Italic", FontStyle = FontStyle.Italic },
                new TextBlock { Text = "Almendra Bold Italic", FontWeight = FontWeight.Bold, FontStyle = FontStyle.Italic }
            };
            var content = new StackPanel { Children = { label, number, input, userName } };
            foreach (var face in faces) content.Children.Add(face);
            var window = new Window { Width = 720, Height = 400, Content = content };
            try
            {
                window.Show(); DisplayTypography.Apply(window); Tick(window);
                Assert.Equal(new FontFamily(settings.FontFamily), number.FontFamily);
                ThemeApplicationService.Apply(app, "Isekai");
                DisplayTypography.Apply(window); Tick(window);
                Assert.Equal(UiLocalization.English, UiLocalization.CurrentLanguage);
                foreach (var text in faces.Prepend(number).Prepend(label))
                {
                    Assert.Equal(ProductFonts.Isekai, text.FontFamily);
                    Assert.NotEmpty(GlyphFaces(text));
                    Assert.All(GlyphFaces(text), glyph =>
                    {
                        Assert.Equal("Almendra", glyph.FamilyName);
                        Assert.Equal(text.FontWeight, glyph.Weight);
                        Assert.Equal(text.FontStyle, glyph.Style);
                        Assert.Equal(FontSimulations.None, glyph.FontSimulations);
                    });
                }
                Assert.Equal(ProductFonts.Isekai, input.FontFamily);
                Assert.Equal(ProductFonts.Isekai, DisplayTypography.Typeface().FontFamily);
                Assert.Equal(ProductFonts.Isekai, DisplayTypography.ResolveFontFamily("Arial", "Isekai", numeric: true));
                DisplayTypography.Configure(new AppSettings { Theme = "Isekai", FontFamily = "Arial" });
                Tick(window);
                Assert.Equal(ProductFonts.Isekai, number.FontFamily);
                UiLocalization.Apply(UiLocalization.Chinese);
                Assert.Equal(UiLocalization.English, UiLocalization.CurrentLanguage);
                Assert.Equal("用户镜头名称", userName.Text);
                Capture(window, "almendra-real-faces.png");

                DisplayTypography.Configure(settings);
                foreach (var theme in new[] { "Light", "Isekai", "Pixel", "Isekai", "Dark", "Lumen" })
                {
                    ThemeApplicationService.Apply(app, theme); DisplayTypography.Apply(window); Tick(window);
                    Assert.Equal(theme == "Isekai" ? ProductFonts.Isekai : new FontFamily(settings.FontFamily), number.FontFamily);
                    Assert.Equal(theme == "Isekai" ? UiLocalization.English : UiLocalization.Japanese, UiLocalization.CurrentLanguage);
                }
                Assert.Equal(UiLocalization.Japanese, settings.Language);
                Assert.Equal(ProductFonts.SelectableFamilies[2], settings.FontFamily);
            }
            finally { window.Close(); ThemeApplicationService.Apply(app, "Light"); UiLocalization.Apply(UiLocalization.Chinese); DisplayTypography.Configure(new AppSettings()); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SettingsLockShowsAlmendraAndRestoresThePriorFontAfterChangingTheme()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(IsekaiTypographyPolicyTests));
        await session.Dispatch(() =>
        {
            var settings = new AppSettings { Theme = "Isekai", FontFamily = "Unavailable user font" };
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Isekai");
            var window = new DisplaySettingsWindow(settings);
            try
            {
                window.Show(); Tick(window);
                var family = Field<ComboBox>(window, "_fontFamily");
                var theme = Field<ComboBox>(window, "_theme");
                Assert.False(family.IsEnabled);
                Assert.Contains(ThemeRegistry.LockedSettingClass, family.Classes);
                AssertNeutral(family.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "Background").Background);
                AssertNeutral(family.GetVisualDescendants().OfType<TextBlock>().First(t => t.IsEffectivelyVisible && t.Text == "Almendra").Foreground);
                Assert.True(Field<ComboBox>(window, "_fontShape").IsEnabled);
                Assert.Equal(ProductFonts.AlmendraFamily, family.SelectedItem);
                Assert.True(Field<TextBlock>(window, "_fontLockHint").IsVisible);
                Assert.Equal(settings.FontFamily, Invoke<string>(window, "SelectedFontFamily"));
                Assert.Equal(ProductFonts.Isekai, Field<TextBlock>(window, "_preview").FontFamily);
                Assert.Equal(ProductFonts.Isekai, Field<TextBlock>(window, "_numericPreview").FontFamily);
                Capture(window, "almendra-locked-settings.png");
                theme.SelectedItem = ThemeRegistry.FromSettings("Light"); Tick(window);
                Assert.True(family.IsEnabled);
                Assert.DoesNotContain(ThemeRegistry.LockedSettingClass, family.Classes);
                Assert.Equal(settings.FontFamily, family.SelectedItem);
                family.SelectedItem = ProductFonts.SelectableFamilies[0];
                theme.SelectedItem = ThemeRegistry.FromSettings("Isekai"); Tick(window);
                Assert.False(family.IsEnabled);
                Assert.Equal(ProductFonts.SelectableFamilies[0], Invoke<string>(window, "SelectedFontFamily"));
                // A stale/programmatic selection cannot affect the locked preview.
                family.SelectedItem = ProductFonts.SelectableFamilies[2]; Tick(window);
                Assert.Equal(ProductFonts.Isekai, Field<TextBlock>(window, "_preview").FontFamily);
                theme.SelectedItem = ThemeRegistry.FromSettings("Dark"); Tick(window);
                Assert.Equal(ProductFonts.SelectableFamilies[0], family.SelectedItem);
            }
            finally { window.Close(); ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light"); UiLocalization.Apply(UiLocalization.Chinese); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(UiLocalization.Chinese, 13, 1.5)]
    [InlineData(UiLocalization.Japanese, 18, 2)]
    public async Task SavedIsekaiStartsInEnglishAndGuardsTheActualRibbonLanguageCommand(string preferredLanguage, double fontSize, double scaling)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(IsekaiTypographyPolicyTests));
        await session.Dispatch(async () =>
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(new AppSettings
            {
                Theme = "Isekai", Language = preferredLanguage, FontFamily = "Arial", FontSize = fontSize,
                WindowWidth = 1600, WindowHeight = 1000
            }))!;
            using var workbench = WorkbenchApplication.Create("cooke");
            var original = workbench.Documents.GetRecoveryState();
            var store = new WorkspaceSessionStore(Path.Combine(Path.GetTempPath(), "isekai-font-policy", Guid.NewGuid().ToString("N")));
            var window = new MainWindow(settings, workbench, store);
            var ready = false; window.StartupCompleted += (_, _) => ready = true;
            try
            {
                window.Show(); window.SetRenderScaling(scaling);
                for (var n = 0; n < 250 && !ready; n++) { await Task.Delay(10); Tick(window); }
                Assert.True(ready);
                Assert.Equal(UiLocalization.English, UiLocalization.CurrentLanguage);
                Assert.Equal(ProductFonts.Isekai, window.FontFamily);
                var surfaceHeader = window.GetVisualDescendants().OfType<TextBlock>()
                    .First(t => t.IsEffectivelyVisible && t.Text == "Surface 1 Properties");
                Assert.Equal(ProductFonts.Isekai, surfaceHeader.FontFamily);
                Assert.All(GlyphFaces(surfaceHeader), glyph => Assert.Equal("Almendra", glyph.FamilyName));
                var visibleLabels = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).ToArray();
                var typeLabel = visibleLabels.FirstOrDefault(t => t.Text == UiLocalization.Text("标准面"));
                Assert.True(typeLabel is not null, "Expected surface type: " + UiLocalization.Text("标准面") + "; visible labels: " + string.Join(" | ", visibleLabels.Select(t => t.Text)));
                Assert.NotNull(typeLabel);
                Assert.Equal(ProductFonts.Isekai, typeLabel.FontFamily);
                Assert.All(GlyphFaces(typeLabel), glyph => Assert.Equal("Almendra", glyph.FamilyName));
                var ribbon = window.GetVisualDescendants().OfType<TabControl>().Single(c => c.Classes.Contains("ribbon-tabs"));
                ribbon.SelectedItem = ribbon.Items.Cast<TabItem>().Single(t => AutomationProperties.GetName(t) == UiLocalization.Text("设置"));
                Tick(window);
                var language = Field<DropDownButton>(window, "_languageMenuButton");
                Assert.False(language.IsEnabled);
                Assert.Contains(ThemeRegistry.LockedSettingClass, language.Classes);
                AssertNeutral(language.GetVisualDescendants().OfType<ContentPresenter>().Single(p => p.Name == "PART_ContentPresenter").Background);
                AssertNeutral(language.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Language").Foreground);
                AssertNeutral(Assert.IsType<Grid>(language.Content).Children.OfType<Polygon>().Single().Fill);
                var languageIcon = Assert.IsType<Grid>(language.Content).Children.OfType<LocalIcon>().Single();
                Assert.NotNull(languageIcon.DisabledColor);
                Assert.Equal(languageIcon.DisabledColor.Value.R, languageIcon.DisabledColor.Value.G);
                Assert.Equal(languageIcon.DisabledColor.Value.G, languageIcon.DisabledColor.Value.B);
                var choices = Assert.IsType<MenuFlyout>(language.Flyout).Items.OfType<MenuItem>().ToArray();
                Assert.All(choices, item => Assert.False(item.IsEnabled));
                Assert.Equal("language-en-US", AutomationProperties.GetAutomationId(Assert.Single(choices, i => i.IsChecked)));
                typeof(MainWindow).GetMethod("ChangeLanguage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [UiLocalization.Chinese]);
                Assert.Equal(UiLocalization.English, UiLocalization.CurrentLanguage);
                Assert.Equal(preferredLanguage, settings.Language);
                Assert.Equal("Arial", settings.FontFamily);
                Assert.Equal(original, workbench.Documents.GetRecoveryState());
                Capture(window, $"almendra-workbench-{fontSize}.png");
                if (fontSize == 13)
                {
                    Field<PanelManager>(window, "_panels").ShowAnalysis("Huygens MTF"); Tick(window);
                    var analysis = window.GetVisualDescendants().OfType<AnalysisPanel>().Single(p => p.IsEffectivelyVisible);
                    Assert.True(await analysis.RunForGuiCaptureAsync().WaitAsync(TimeSpan.FromSeconds(40)));
                    Tick(window);
                    Assert.Equal(ProductFonts.Isekai, analysis.FontFamily);
                    Assert.Equal("Copy Text", UiLocalization.Text("复制文本"));
                    Assert.Equal("Method: Huygens-Fresnel", UiLocalization.Text("方法: Huygens-Fresnel"));
                    Assert.Equal("See Data for 8 additional items", UiLocalization.Text("其余 8 项见“数据”页"));
                    Assert.DoesNotContain(analysis.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible),
                        t => t.Text is { } text && text.Any(c => c is >= '\u4e00' and <= '\u9fff'));
                    Assert.Equal(original, workbench.Documents.GetRecoveryState());
                    Capture(window, "almendra-analysis.png");
                }

                settings.Theme = "Light";
                typeof(MainWindow).GetMethod("ApplyTheme", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [false]);
                DisplayTypography.Apply(window); Tick(window);
                Assert.True(language.IsEnabled);
                Assert.DoesNotContain(ThemeRegistry.LockedSettingClass, language.Classes);
                Assert.Null(languageIcon.DisabledColor);
                Assert.Equal(preferredLanguage, UiLocalization.CurrentLanguage);
                Assert.Equal(new FontFamily("Arial"), window.FontFamily);
                Assert.All(choices, item => Assert.True(item.IsEnabled));
                Assert.Equal(original, workbench.Documents.GetRecoveryState());
            }
            finally { window.Close(); ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light"); UiLocalization.Apply(UiLocalization.Chinese); DisplayTypography.Configure(new AppSettings()); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public async Task SequentialExamplesFlyoutsUseAlmendraInBothPopupLevels(double scaling)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(IsekaiTypographyPolicyTests));
        await session.Dispatch(() =>
        {
            var app = Avalonia.Application.Current!;
            ThemeApplicationService.Apply(app, "Isekai");
            DisplayTypography.Configure(new AppSettings { FontFamily = "Arial" });
            var opened = 0;
            var button = MainWindow.CreateSequentialExamplesButton(_ => { opened++; return Task.CompletedTask; });
            button.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
            button.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
            var flyout = Assert.IsType<MenuFlyout>(button.Flyout);
            var categoryName = SequentialExampleCatalog.Groups.Single(g => g.Name == "Objectives").DisplayName;
            var category = flyout.Items.OfType<MenuItem>().Single(i => (string?)i.Header == categoryName);
            var example = category.Items.OfType<MenuItem>().Single(i => AutomationProperties.GetName(i) == "Double Gauss 28 degree field");
            var window = new Window { Width = 1400, Height = 1000, Content = button };
            try
            {
                window.Show(); window.SetRenderScaling(scaling); DisplayTypography.Apply(window); Tick(window);
                var baselines = new Dictionary<string, string>();
                foreach (var theme in new[] { "Light", "Dark", "Lumen", "Pixel" })
                {
                    Show(theme);
                    baselines[theme] = FontSnapshot();
                    flyout.Hide(); Tick(window);
                }
                Show("Isekai");
                var firstRoot = TopLevel.GetTopLevel(category)!;
                var secondRoot = TopLevel.GetTopLevel(example)!;
                Capture(window, $"examples-menus-{scaling}.png");
                if (!ReferenceEquals(window, firstRoot)) Capture(firstRoot, $"examples-categories-{scaling}.png");
                if (!ReferenceEquals(window, secondRoot) && !ReferenceEquals(firstRoot, secondRoot))
                    Capture(secondRoot, $"examples-objectives-{scaling}.png");
                foreach (var item in new[] { category, example })
                {
                    var labels = Labels(item); Assert.NotEmpty(labels);
                    foreach (var label in labels)
                    {
                        Assert.Equal(ProductFonts.Isekai, label.FontFamily);
                        var glyphs = GlyphFaces(label); Assert.NotEmpty(glyphs);
                        Assert.All(glyphs, glyph => Assert.Equal("Almendra", glyph.FamilyName));
                    }
                }
                // Existing open popup surfaces must lose the Isekai override on a theme switch.
                foreach (var theme in new[] { "Light", "Dark", "Lumen", "Pixel" })
                {
                    ThemeApplicationService.Apply(app, theme); DisplayTypography.Apply(window); Tick(window);
                    Assert.Equal(baselines[theme], FontSnapshot());
                }
                Assert.Equal(0, opened);

                void Show(string theme)
                {
                    ThemeApplicationService.Apply(app, theme); DisplayTypography.Apply(window);
                    flyout.ShowAt(button); Tick(window);
                    category.IsSubMenuOpen = true; Tick(window);
                    TopLevel.GetTopLevel(category)!.SetRenderScaling(scaling);
                    TopLevel.GetTopLevel(example)!.SetRenderScaling(scaling);
                    Tick(window);
                }
                string FontSnapshot() => string.Join("|", new[] { category, example }.SelectMany(Labels)
                    .Select(label => $"{label.Text}:{label.FontFamily}:{string.Join(',', GlyphFaces(label).Select(face => face.FamilyName))}"));
            }
            finally
            {
                flyout.Hide(); window.Close(); ThemeApplicationService.Apply(app, "Light");
                DisplayTypography.Configure(new AppSettings()); UiLocalization.Apply(UiLocalization.Chinese);
            }
            static TextBlock[] Labels(MenuItem item) => item.GetVisualDescendants().OfType<TextBlock>()
                .Where(t => t.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(t.Text)).ToArray();
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("flyout")]
    [InlineData("context")]
    [InlineData("tooltip")]
    [InlineData("combo")]
    public async Task OpenPopupSurfacesUseAlmendraAndRestoreTheirPreviousFonts(string kind)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(IsekaiTypographyPolicyTests));
        await session.Dispatch(() =>
        {
            const string text = "Popup Typography";
            var app = Avalonia.Application.Current!;
            ThemeApplicationService.Apply(app, "Light");
            DisplayTypography.Configure(new AppSettings { FontFamily = "Arial" });
            var button = new Button { Content = "Open" };
            var combo = new ComboBox { ItemsSource = new[] { text, "Other" }, SelectedIndex = 0 };
            var flyout = new Flyout { Content = new TextBlock { Text = text } };
            var context = new ContextMenu { Items = { new MenuItem { Header = text } } };
            var window = new Window { Width = 600, Height = 400,
                Content = new StackPanel { Children = { button, combo } } };
            try
            {
                window.Show(); DisplayTypography.Apply(window); Tick(window);
                switch (kind)
                {
                    case "flyout": flyout.ShowAt(button); break;
                    case "context": context.Open(button); break;
                    case "tooltip": ToolTip.SetTip(button, text); ToolTip.SetIsOpen(button, true); break;
                    case "combo": combo.IsDropDownOpen = true; break;
                }
                Tick(window);
                var labels = Labels(); Assert.NotEmpty(labels);
                var labelCount = labels.Length;
                var baseline = Snapshot();
                ThemeApplicationService.Apply(app, "Isekai"); DisplayTypography.Apply(window); Tick(window);
                // Fluent can recreate ComboBox template text on a variant change.
                labels = Labels(); Assert.Equal(labelCount, labels.Length);
                foreach (var label in labels)
                {
                    Assert.Equal(ProductFonts.Isekai, label.FontFamily);
                    var glyphs = GlyphFaces(label); Assert.NotEmpty(glyphs);
                    Assert.All(glyphs, glyph => Assert.Equal("Almendra", glyph.FamilyName));
                }
                Capture(window, $"popup-{kind}.png");
                ThemeApplicationService.Apply(app, "Light"); DisplayTypography.Apply(window); Tick(window);
                Assert.Equal(labelCount, Labels().Length); Assert.Equal(baseline, Snapshot());
                Assert.Equal(text, combo.SelectedItem);
                Assert.Equal(kind == "combo", combo.IsDropDownOpen);

                TextBlock[] Labels() => window.GetVisualDescendants().OfType<TextBlock>()
                    .Where(t => t.IsEffectivelyVisible && t.Text == text).ToArray();
                string Snapshot() => string.Join("|", Labels().Select(label =>
                    $"{label.FontFamily}:{string.Join(',', GlyphFaces(label).Select(face => face.FamilyName))}"));
            }
            finally
            {
                flyout.Hide(); context.Close(); ToolTip.SetIsOpen(button, false); combo.IsDropDownOpen = false;
                window.Close(); ThemeApplicationService.Apply(app, "Light");
                DisplayTypography.Configure(new AppSettings()); UiLocalization.Apply(UiLocalization.Chinese);
            }
        }, CancellationToken.None);
    }

    private static GlyphTypeface[] GlyphFaces(TextBlock text) => text.TextLayout.TextLines.SelectMany(line => line.TextRuns)
        .OfType<ShapedTextRun>().Select(run => run.GlyphRun.GlyphTypeface).Distinct().ToArray();
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static T Invoke<T>(object target, string name) => (T)target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, null)!;
    private static void AssertNeutral(IBrush? brush)
    {
        var color = Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;
        Assert.Equal(color.R, color.G);
        Assert.Equal(color.G, color.B);
        Assert.InRange(color.A, (byte)100, (byte)255);
        Assert.InRange(color.R, (byte)40, (byte)200);
    }
    private static void Tick(Window window)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
    }
    private static void Capture(TopLevel window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_ALMENDRA_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var image = window.CaptureRenderedFrame(); Assert.NotNull(image);
        image.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }
}

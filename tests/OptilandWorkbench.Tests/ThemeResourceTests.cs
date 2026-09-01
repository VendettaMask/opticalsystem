using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Services;
using OptilandWorkbench.App.Theming;

namespace OptilandWorkbench.Tests;

public sealed class ThemeResourceTests
{
    [Fact]
    public void ThemeRegistryOwnsSelectionResourcesIconsAndChrome()
    {
        Assert.Equal(
            new[] { "Light", "Dark", "Isekai", "Pixel", "System" },
            ThemeRegistry.SelectableThemes.Select(theme => theme.SettingsValue));
        Assert.Equal(4, ThemeRegistry.ConcreteThemes.Count);
        Assert.Equal(ThemeVariant.Light, ThemeRegistry.FromSettings("Light").RequestedVariant);
        Assert.Equal(ThemeVariant.Dark, ThemeRegistry.FromSettings("Dark").RequestedVariant);
        Assert.Equal(IsekaiTheme.Variant, ThemeRegistry.FromSettings("Isekai").RequestedVariant);
        Assert.Equal(PixelTheme.Variant, ThemeRegistry.FromSettings("Pixel").RequestedVariant);
        Assert.True(ThemeRegistry.FromSettings("System").FollowsSystem);
        Assert.Equal("Light", ThemeRegistry.NormalizeSettingsValue("unknown"));

        foreach (var theme in ThemeRegistry.ConcreteThemes)
        {
            var resources = theme.BuildResources();
            AssertAllThemeBrushes(resources);
            AssertChromeResources(resources);
        }
    }

    [Fact]
    public void CustomThemesDefineAnalysisAndSceneRenderingResources()
    {
        var lightResources = ThemePalette.Light.ToResourceDictionary();
        var resources = ThemePalette.DarkOpticStudio.ToResourceDictionary();
        var isekaiResources = ThemeRegistry.FromSettings(IsekaiTheme.SettingsValue).BuildResources();
        var pixelResources = ThemeRegistry.FromSettings(PixelTheme.SettingsValue).BuildResources();
        var settings = new AppSettings { Theme = IsekaiTheme.SettingsValue };
        settings.NormalizeDisplaySettings();

        Assert.Equal(IsekaiTheme.SettingsValue, settings.Theme);
        AssertAllThemeBrushes(lightResources);
        AssertAllThemeBrushes(resources);
        AssertAllThemeBrushes(isekaiResources);
        AssertAllThemeBrushes(pixelResources);

        AssertBrush(resources, ThemeResourceBindings.TextPrimary);
        AssertBrush(resources, ThemeResourceBindings.TextSecondary);
        AssertBrush(resources, ThemeResourceBindings.TextMuted);
        AssertBrush(resources, ThemeResourceBindings.TextDisabled);
        AssertBrush(resources, ThemeResourceBindings.TextAccent);
        AssertBrush(resources, ThemeResourceBindings.TextOnAccent);
        AssertBrush(resources, ThemeResourceBindings.TextWarning);
        AssertBrush(resources, ThemeResourceBindings.TextError);
        AssertBrush(resources, ThemeResourceBindings.TextSuccess);
        AssertBrush(resources, ThemeResourceBindings.WarningSurface);
        AssertBrush(resources, ThemeResourceBindings.ErrorSurface);
        AssertBrush(resources, ThemeResourceBindings.SuccessSurface);
        AssertBrush(resources, ThemeResourceBindings.SettingsSurface);
        AssertBrush(resources, ThemeResourceBindings.SettingsOverlaySurface);
        AssertBrush(resources, ThemeResourceBindings.PlotBackground);
        AssertBrush(resources, ThemeResourceBindings.PlotText);
        AssertBrush(resources, ThemeResourceBindings.PlotGrid);
        AssertBrush(resources, ThemeResourceBindings.SceneBackground);
        AssertBrush(resources, ThemeResourceBindings.SceneLensFill);
        AssertBrush(resources, ThemeResourceBindings.SceneSurface);
        AssertBrush(resources, ThemeResourceBindings.SceneOrientationFill);
        AssertBrush(isekaiResources, ThemeResourceBindings.TextPrimary);
        AssertBrush(isekaiResources, ThemeResourceBindings.PlotBackground);
        AssertBrush(isekaiResources, ThemeResourceBindings.SceneBackground);
        AssertBrush(isekaiResources, "AccentFillColorDefaultBrush");
        AssertBrush(pixelResources, ThemeResourceBindings.TextPrimary);
        AssertBrush(pixelResources, ThemeResourceBindings.PlotBackground);
        AssertBrush(pixelResources, ThemeResourceBindings.SceneBackground);
        AssertBrush(pixelResources, "AccentFillColorDefaultBrush");
        var applicationResources = new ResourceDictionary
        {
            ["AccentFillColorDefaultBrush"] = new SolidColorBrush(Color.FromRgb(0, 122, 255))
        };
        IsekaiTheme.ApplyAccentResources(applicationResources);
        Assert.Equal(
            Color.FromRgb(202, 148, 50),
            ColorOf(applicationResources, "AccentFillColorDefaultBrush"));
        Assert.True(ThemeRegistry.IsDarkVisual(IsekaiTheme.Variant));
        Assert.False(ThemeRegistry.IsDarkVisual(PixelTheme.Variant));
        Assert.False(ThemeRegistry.IsDarkVisual(ThemeVariant.Light));
        Assert.Equal(
            Color.FromRgb(255, 255, 255),
            ColorOf(lightResources, ThemeResourceBindings.SettingsSurface));
        Assert.Equal((byte)255, ColorOf(lightResources, ThemeResourceBindings.SettingsSurface).A);
        Assert.True(ColorOf(lightResources, ThemeResourceBindings.SettingsOverlaySurface).A < 255);
        Assert.True(ColorOf(resources, ThemeResourceBindings.SettingsOverlaySurface).A < 255);
        Assert.True(ColorOf(isekaiResources, ThemeResourceBindings.SettingsOverlaySurface).A < 255);
        Assert.True(ColorOf(pixelResources, ThemeResourceBindings.SettingsOverlaySurface).A < 255);
    }

    [Fact]
    public void DarkThemeKeepsRenderingPaletteDarkerThanLightTheme()
    {
        var light = ThemePalette.Light.ToResourceDictionary();
        var dark = ThemePalette.DarkOpticStudio.ToResourceDictionary();
        var isekai = ThemeRegistry.FromSettings(IsekaiTheme.SettingsValue).BuildResources();
        var pixel = ThemeRegistry.FromSettings(PixelTheme.SettingsValue).BuildResources();

        Assert.True(Luminance(ColorOf(dark, ThemeResourceBindings.PlotBackground))
            < Luminance(ColorOf(light, ThemeResourceBindings.PlotBackground)));
        Assert.True(Luminance(ColorOf(dark, ThemeResourceBindings.SceneBackground))
            < Luminance(ColorOf(light, ThemeResourceBindings.SceneBackground)));
        Assert.True(Luminance(ColorOf(dark, ThemeResourceBindings.PlotText))
            > Luminance(ColorOf(dark, ThemeResourceBindings.PlotBackground)));
        Assert.True(Luminance(ColorOf(dark, ThemeResourceBindings.TextPrimary))
            > Luminance(ColorOf(dark, ThemeResourceBindings.TextSecondary)));
        Assert.True(Luminance(ColorOf(dark, ThemeResourceBindings.TextSecondary))
            > Luminance(ColorOf(dark, ThemeResourceBindings.TextMuted)));
        Assert.True(Luminance(ColorOf(dark, ThemeResourceBindings.TextMuted))
            > Luminance(ColorOf(dark, ThemeResourceBindings.TextDisabled)));
        Assert.True(Luminance(ColorOf(dark, ThemeResourceBindings.TextPrimary))
            > Luminance(ColorOf(dark, ThemeResourceBindings.Surface)));
        Assert.True(Luminance(ColorOf(isekai, ThemeResourceBindings.PlotText))
            > Luminance(ColorOf(isekai, ThemeResourceBindings.PlotBackground)));
        Assert.True(Luminance(ColorOf(isekai, ThemeResourceBindings.TextAccent))
            > Luminance(ColorOf(isekai, ThemeResourceBindings.Surface)));
        Assert.True(Luminance(ColorOf(isekai, ThemeResourceBindings.TextWarning))
            > Luminance(ColorOf(isekai, ThemeResourceBindings.WarningSurface)));
        Assert.True(Luminance(ColorOf(isekai, ThemeResourceBindings.TextError))
            > Luminance(ColorOf(isekai, ThemeResourceBindings.ErrorSurface)));
        Assert.True(Luminance(ColorOf(isekai, ThemeResourceBindings.TextSuccess))
            > Luminance(ColorOf(isekai, ThemeResourceBindings.SuccessSurface)));
        AssertReadable(light, ThemeResourceBindings.AnalysisRealRayRowForeground, ThemeResourceBindings.AnalysisRealRayRowBackground);
        AssertReadable(light, ThemeResourceBindings.AnalysisParaxialRayRowForeground, ThemeResourceBindings.AnalysisParaxialRayRowBackground);
        AssertReadable(dark, ThemeResourceBindings.AnalysisRealRayRowForeground, ThemeResourceBindings.AnalysisRealRayRowBackground);
        AssertReadable(dark, ThemeResourceBindings.AnalysisParaxialRayRowForeground, ThemeResourceBindings.AnalysisParaxialRayRowBackground);
        AssertReadable(isekai, ThemeResourceBindings.AnalysisRealRayRowForeground, ThemeResourceBindings.AnalysisRealRayRowBackground);
        AssertReadable(isekai, ThemeResourceBindings.AnalysisParaxialRayRowForeground, ThemeResourceBindings.AnalysisParaxialRayRowBackground);
        AssertReadable(pixel, ThemeResourceBindings.TextPrimary, ThemeResourceBindings.Surface);
        AssertReadable(pixel, ThemeResourceBindings.TextSecondary, ThemeResourceBindings.Surface);
        AssertReadable(pixel, ThemeResourceBindings.TextMuted, ThemeResourceBindings.Surface);
        AssertReadable(pixel, ThemeResourceBindings.TextAccent, ThemeResourceBindings.Surface);
        AssertReadable(pixel, ThemeResourceBindings.TextWarning, ThemeResourceBindings.WarningSurface);
        AssertReadable(pixel, ThemeResourceBindings.TextError, ThemeResourceBindings.ErrorSurface);
        AssertReadable(pixel, ThemeResourceBindings.TextSuccess, ThemeResourceBindings.SuccessSurface);
        AssertReadable(pixel, ThemeResourceBindings.AnalysisRealRayRowForeground, ThemeResourceBindings.AnalysisRealRayRowBackground);
        AssertReadable(pixel, ThemeResourceBindings.AnalysisParaxialRayRowForeground, ThemeResourceBindings.AnalysisParaxialRayRowBackground);
        AssertReadable(pixel, ThemeResourceBindings.TextOnAccent, "AccentFillColorDefaultBrush");

    }

    [Fact]
    public void StandardThemesKeepExistingIconGeometryAndChromeMetrics()
    {
        Assert.True(ThemeIconResolver.TryResolve(ThemeVariant.Light, "save", out var light));
        Assert.True(ThemeIconResolver.TryResolve(ThemeVariant.Dark, "save", out var dark));
        Assert.Equal(StandardThemeIconPack.Instance.Id, ThemeIconResolver.PackId(ThemeVariant.Light));
        Assert.Equal(StandardThemeIconPack.Instance.Id, ThemeIconResolver.PackId(ThemeVariant.Dark));
        Assert.Same(light, dark);
        Assert.Empty(light.AccentPrimitives);
        Assert.Equal(Matrix.Identity, light.ContentTransform);
        Assert.Equal(PenLineCap.Round, light.LineCap);

        foreach (var settingsValue in new[] { "Light", "Dark" })
        {
            var profile = ThemeRegistry.FromSettings(settingsValue).Chrome;
            Assert.Equal(new Thickness(1), profile[ThemeChromeRole.SettingsCard].BorderThickness);
            Assert.Equal(new CornerRadius(8), profile[ThemeChromeRole.SettingsCard].CornerRadius);
            Assert.Equal(new Thickness(1), profile[ThemeChromeRole.ControlFrame].BorderThickness);
            Assert.Equal(new CornerRadius(5), profile[ThemeChromeRole.ControlFrame].CornerRadius);
            Assert.Equal(new Thickness(0, 0, 0, 1), profile[ThemeChromeRole.Ribbon].BorderThickness);
        }
    }

    [Fact]
    public void IsekaiThemeUsesImportedGameIconsPackAndChrome()
    {
        Assert.Equal(IsekaiThemeIconPack.Instance.Id, ThemeIconResolver.PackId(IsekaiTheme.Variant));
        Assert.True(ThemeIconResolver.TryResolve(ThemeVariant.Light, "save", out var standard));
        Assert.True(ThemeIconResolver.TryResolve(IsekaiTheme.Variant, "save", out var isekai));
        Assert.NotSame(standard, isekai);
        Assert.Empty(isekai.AccentPrimitives);
        Assert.NotEqual(Matrix.Identity, isekai.ContentTransform);
        Assert.All(isekai.Primitives, primitive => Assert.IsType<FilledPathPrimitive>(primitive));
        Assert.Equal(PenLineCap.Square, isekai.LineCap);

        Assert.True(IsekaiThemeIconPack.Names.Count >= 80);
        Assert.All(IsekaiThemeIconPack.Names, iconName =>
        {
            Assert.True(
                ThemeIconResolver.TryResolve(IsekaiTheme.Variant, iconName, out var imported),
                $"异世界图标包无法解析语义图标 '{iconName}'。");
            Assert.True(StandardThemeIconPack.Instance.TryResolve(iconName, out var standardIcon));
            Assert.NotSame(standardIcon, imported);
            Assert.NotEmpty(imported.Primitives);
            Assert.Empty(imported.AccentPrimitives);
            Assert.All(imported.Primitives, primitive => Assert.IsType<FilledPathPrimitive>(primitive));
            Assert.True(IsekaiThemeIconPack.TryGetAttribution(iconName, out var attribution));
            Assert.False(string.IsNullOrWhiteSpace(attribution.Author));
            Assert.EndsWith(".svg", attribution.Source, StringComparison.Ordinal);
        });

        var profile = ThemeRegistry.FromSettings(IsekaiTheme.SettingsValue).Chrome;
        Assert.Equal(new Thickness(1), profile[ThemeChromeRole.SettingsCard].BorderThickness);
        Assert.Equal(new CornerRadius(3), profile[ThemeChromeRole.SettingsCard].CornerRadius);
        Assert.NotEqual(
            ThemeRegistry.FromSettings("Dark").Chrome[ThemeChromeRole.Ribbon],
            profile[ThemeChromeRole.Ribbon]);
    }

    [Fact]
    public void IsekaiChromeKeepsStructuralThicknessLayoutCompatible()
    {
        var standard = ThemeRegistry.FromSettings("Light").Chrome;
        var isekai = ThemeRegistry.FromSettings(IsekaiTheme.SettingsValue).Chrome;

        foreach (var role in Enum.GetValues<ThemeChromeRole>())
        {
            Assert.Equal(
                standard[role].BorderThickness,
                isekai[role].BorderThickness);
        }
    }

    [Fact]
    public void PixelThemeUsesColoredRasterIconsAndSquaredChrome()
    {
        Assert.Equal(PixelThemeIconPack.Instance.Id, ThemeIconResolver.PackId(PixelTheme.Variant));
        Assert.True(ThemeIconResolver.TryResolve(ThemeVariant.Light, "save", out var standard));
        Assert.True(ThemeIconResolver.TryResolve(PixelTheme.Variant, "save", out var pixel));
        Assert.NotSame(standard, pixel);
        Assert.Empty(pixel.Primitives);
        Assert.Null(standard.Image);
        Assert.NotNull(pixel.Image);
        Assert.Equal(Matrix.Identity, pixel.ContentTransform);
        Assert.Equal(PenLineCap.Square, pixel.LineCap);
        Assert.Equal(PenLineJoin.Miter, pixel.LineJoin);
        Assert.Equal(1, pixel.StrokeWidthScale, precision: 12);
        Assert.True(pixel.UseAliasedEdges);
        Assert.False(standard.UseAliasedEdges);
        Assert.Equal(87, PixelThemeIconPack.Names.Count);
        Assert.True(PixelThemeIconPack.TryGetSource("save", out var saveSource));
        Assert.Equal("diskette.png", saveSource);
        Assert.All(LocalIconLibrary.Names, iconName =>
            Assert.True(PixelThemeIconPack.Instance.TryResolve(iconName, out _)));
        Assert.True(PixelThemeIconPack.Instance.TryResolve("not-a-real-icon", out var fallback));
        Assert.True(PixelThemeIconPack.Instance.TryResolve("circle-question-mark", out var question));
        Assert.Same(question, fallback);

        var standardChrome = ThemeRegistry.FromSettings("Light").Chrome;
        var pixelChrome = ThemeRegistry.FromSettings(PixelTheme.SettingsValue).Chrome;
        foreach (var role in Enum.GetValues<ThemeChromeRole>())
        {
            Assert.Equal(standardChrome[role].BorderThickness, pixelChrome[role].BorderThickness);
            Assert.Equal(new CornerRadius(0), pixelChrome[role].CornerRadius);
        }

        Assert.NotEqual(default, pixelChrome[ThemeChromeRole.SettingsCard].BoxShadow);
        Assert.NotEqual(default, pixelChrome[ThemeChromeRole.ControlFrame].BoxShadow);
    }

    [Fact]
    public void PixelThemeOverridesFluentControlsWithEightBitColorRoles()
    {
        var resources = ThemeRegistry.FromSettings(PixelTheme.SettingsValue).BuildResources();

        Assert.Equal(Color.FromRgb(221, 239, 242), ColorOf(resources, "ButtonBackground"));
        Assert.Equal(Color.FromRgb(23, 50, 77), ColorOf(resources, "ButtonBorderBrush"));
        Assert.Equal(Color.FromRgb(255, 249, 220), ColorOf(resources, "TextControlBackground"));
        Assert.Equal(Color.FromRgb(48, 139, 78), ColorOf(resources, "CheckBoxCheckBackgroundFillChecked"));
        Assert.Equal(Color.FromRgb(59, 131, 189), ColorOf(resources, "TabItemHeaderBackgroundUnselected"));
        Assert.Equal(Color.FromRgb(239, 91, 91), ColorOf(resources, "TabItemHeaderBackgroundSelected"));
        Assert.Equal(Color.FromRgb(247, 201, 72), ColorOf(resources, ThemeResourceBindings.SelectionBackground));
        Assert.Equal(Color.FromRgb(247, 201, 72), ColorOf(resources, "DataGridRowSelectedBackgroundBrush"));
        Assert.Equal(1d, Assert.IsType<double>(resources["DataGridRowSelectedBackgroundOpacity"]));
        Assert.Equal(Color.FromRgb(23, 50, 77), ColorOf(resources, ThemeResourceBindings.SelectionForeground));
        Assert.Equal(Color.FromRgb(59, 131, 189), ColorOf(resources, ThemeResourceBindings.SectionHeaderBackground));
        Assert.Equal(Colors.Transparent, ColorOf(resources, ThemeResourceBindings.RibbonCommandBackground));
        Assert.Equal(Color.FromRgb(221, 239, 242), ColorOf(resources, ThemeResourceBindings.RibbonGroupBackground));
        Assert.Equal(96d, Assert.IsType<double>(resources[ThemeLayoutResources.RibbonMinHeight]));
        Assert.Equal(48d, Assert.IsType<double>(resources[ThemeLayoutResources.RibbonCommandMinWidth]));
        Assert.Equal(48d, Assert.IsType<double>(resources[ThemeLayoutResources.RibbonCommandWidth]));
        Assert.Equal(50d, Assert.IsType<double>(resources[ThemeLayoutResources.RibbonCommandMinHeight]));
        Assert.Equal(22d, Assert.IsType<double>(resources[ThemeLayoutResources.RibbonCommandIconSize]));
        Assert.Equal(32d, Assert.IsType<double>(resources[ThemeLayoutResources.RibbonTabHeight]));
        Assert.Equal(0d, Assert.IsType<double>(resources[ThemeLayoutResources.RibbonGroupCaptionHeight]));
        Assert.Equal(0d, Assert.IsType<double>(resources[ThemeLayoutResources.RibbonGroupCaptionMaxHeight]));
        Assert.Equal(new Thickness(0), Assert.IsType<Thickness>(resources[ThemeLayoutResources.RibbonCommandBorderThickness]));
        Assert.Equal(32d, Assert.IsType<double>(resources["TabItemMinHeight"]));

        var lightResources = ThemeRegistry.FromSettings("Light").BuildResources();
        Assert.Equal(126d, Assert.IsType<double>(lightResources[ThemeLayoutResources.RibbonMinHeight]));
        Assert.Equal(78d, Assert.IsType<double>(lightResources[ThemeLayoutResources.RibbonCommandMinWidth]));
        Assert.True(double.IsNaN(Assert.IsType<double>(lightResources[ThemeLayoutResources.RibbonCommandWidth])));
        Assert.Equal(66d, Assert.IsType<double>(lightResources[ThemeLayoutResources.RibbonCommandMinHeight]));
        Assert.Equal(26d, Assert.IsType<double>(lightResources[ThemeLayoutResources.RibbonCommandIconSize]));
        Assert.Equal(new Thickness(1), Assert.IsType<Thickness>(lightResources[ThemeLayoutResources.RibbonCommandBorderThickness]));
        Assert.True(double.IsNaN(Assert.IsType<double>(lightResources[ThemeLayoutResources.RibbonTabHeight])));
        Assert.Equal(double.PositiveInfinity, Assert.IsType<double>(lightResources[ThemeLayoutResources.RibbonGroupCaptionMaxHeight]));
        Assert.Equal(new CornerRadius(0), Assert.IsType<CornerRadius>(resources["ControlCornerRadius"]));
        var definition = ThemeRegistry.FromSettings(PixelTheme.SettingsValue);
        Assert.Contains("Fusion Pixel 10px Mono zh_hans", definition.UiFontFamily.Name, StringComparison.Ordinal);
        Assert.Equal(TextRenderingMode.Alias, definition.TextRenderingMode);
    }

    [Fact]
    public void ThemeIconPacksOwnImportedAssetsAndFallbackPolicy()
    {
        Assert.True(IsekaiThemeIconPack.Instance.TryResolve("save", out var imported));
        Assert.Empty(imported.AccentPrimitives);
        Assert.NotEqual(Matrix.Identity, imported.ContentTransform);
        Assert.True(IsekaiThemeIconPack.TryGetAttribution("save", out var attribution));
        Assert.Equal("delapouite", attribution.Author);
        Assert.Equal("delapouite/save.svg", attribution.Source);

        Assert.True(IsekaiThemeIconPack.Instance.TryResolve("accessibility", out var unmapped));

        Assert.True(IsekaiThemeIconPack.Instance.TryResolve("not-a-real-icon", out var fallback));
        Assert.True(IsekaiThemeIconPack.Instance.TryResolve("circle-question-mark", out var question));
        Assert.Same(question, fallback);
        Assert.Same(question, unmapped);
    }

    [Fact]
    public void IsekaiIconMappingsKeepReviewedOperationsSemanticallyDistinct()
    {
        var expectedSources = new Dictionary<string, string>
        {
            ["grid-2x2"] = "skoll/divided-square.svg",
            ["list-tree"] = "lorc/checkbox-tree.svg",
            ["picture-in-picture-2"] = "delapouite/window.svg",
            ["search"] = "lorc/magnifying-glass.svg",
            ["zoom-in"] = "delapouite/expand.svg",
            ["zoom-out"] = "delapouite/contract.svg",
            ["file-plus"] = "delapouite/scroll-quill.svg",
            ["type"] = "lorc/quill-ink.svg",
            ["package-search"] = "delapouite/archive-research.svg",
            ["scan-search"] = "lorc/radar-sweep.svg"
        };

        foreach (var (iconName, expectedSource) in expectedSources)
        {
            Assert.True(IsekaiThemeIconPack.TryGetAttribution(iconName, out var attribution));
            Assert.Equal(expectedSource, attribution.Source);
        }

        AssertDistinctSources("grid-2x2", "list-tree", "picture-in-picture-2");
        AssertDistinctSources("search", "zoom-in", "zoom-out");
        AssertDistinctSources("file-plus", "type");
        AssertDistinctSources("package-search", "scan-search");

        static void AssertDistinctSources(params string[] iconNames)
        {
            var sources = iconNames.Select(iconName =>
            {
                Assert.True(IsekaiThemeIconPack.TryGetAttribution(iconName, out var attribution));
                return attribution.Source;
            });
            Assert.Equal(iconNames.Length, sources.Distinct(StringComparer.Ordinal).Count());
        }
    }

    [Fact]
    public void ThemeChromeOverlayIsInputTransparentAndDoesNotRequestLayoutSpace()
    {
        var chrome = new ThemeChromeOverlay { Role = ThemeChromeRole.Ribbon };
        chrome.Measure(new Size(1200, 140));

        Assert.False(chrome.IsHitTestVisible);
        Assert.True(chrome.ClipToBounds);
        Assert.Equal(default, chrome.DesiredSize);
    }

    [Fact]
    public void AThemePackageCanBeBuiltWithoutAddingResolverBranches()
    {
        var package = new ThemeDefinition(
            "TestPackage",
            "测试主题包",
            new ThemeVariant("TestPackage", ThemeVariant.Light),
            ThemePalette.Light,
            TestThemeIconPack.Instance,
            FontFamily.Default,
            TextRenderingMode.Unspecified,
            ThemeChromeProfile.CreateStandard(ThemePalette.Light.Border),
            NoThemeDecorationRenderer.Instance,
            StandardTheme.ApplyAccentResources,
            IsDarkLike: false);

        var resources = package.BuildResources();

        Assert.Equal(TestThemeIconPack.Instance, package.IconPack);
        AssertAllThemeBrushes(resources);
        AssertChromeResources(resources);
        Assert.True(package.IconPack.TryResolve("save", out _));
    }

    private static void AssertBrush(ResourceDictionary resources, string key)
    {
        Assert.True(resources.ContainsKey(key));
        Assert.IsAssignableFrom<IBrush>(resources[key]);
    }

    private static void AssertAllThemeBrushes(ResourceDictionary resources)
    {
        foreach (var field in typeof(ThemeResourceBindings).GetFields(
                     System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
        {
            if (!field.IsLiteral || field.FieldType != typeof(string))
            {
                continue;
            }

            AssertBrush(resources, Assert.IsType<string>(field.GetRawConstantValue()));
        }
    }

    private static void AssertChromeResources(ResourceDictionary resources)
    {
        foreach (var role in Enum.GetValues<ThemeChromeRole>())
        {
            Assert.IsAssignableFrom<IBrush>(resources[ThemeChromeResources.BorderBrush(role)]);
            Assert.IsType<Thickness>(resources[ThemeChromeResources.BorderThickness(role)]);
            Assert.IsType<CornerRadius>(resources[ThemeChromeResources.CornerRadius(role)]);
            Assert.IsType<BoxShadows>(resources[ThemeChromeResources.BoxShadow(role)]);
        }
    }

    private static Color ColorOf(ResourceDictionary resources, string key)
    {
        var brush = Assert.IsAssignableFrom<ISolidColorBrush>(resources[key]);
        return brush.Color;
    }

    private static double Luminance(Color color) =>
        (0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B);

    private static void AssertReadable(ResourceDictionary resources, string foregroundKey, string backgroundKey)
    {
        Assert.True(
            ContrastRatio(ColorOf(resources, foregroundKey), ColorOf(resources, backgroundKey)) >= 4.5,
            $"{foregroundKey} must be readable on {backgroundKey}");
    }

    private static double ContrastRatio(Color foreground, Color background)
    {
        var first = RelativeLuminance(foreground) + 0.05;
        var second = RelativeLuminance(background) + 0.05;
        return Math.Max(first, second) / Math.Min(first, second);
    }

    private static double RelativeLuminance(Color color)
    {
        static double Linear(byte channel)
        {
            var value = channel / 255.0;
            return value <= 0.03928
                ? value / 12.92
                : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Linear(color.R)) + (0.7152 * Linear(color.G)) + (0.0722 * Linear(color.B));
    }

    private sealed class TestThemeIconPack : IThemeIconPack
    {
        public static TestThemeIconPack Instance { get; } = new();

        public string Id => "TestThemeIcons";

        public bool TryResolve(string iconName, out IconDefinition definition) =>
            StandardThemeIconPack.Instance.TryResolve(iconName, out definition!);
    }
}

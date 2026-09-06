using System.Reflection;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.App;
using OptilandWorkbench.Core;

namespace OptilandWorkbench.Tests;

public sealed class LayeringArchitectureTests
{
    [Fact]
    public void AppAssemblyDoesNotReferenceCore()
    {
        var references = typeof(MainWindow).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(references, reference =>
            reference.Name == typeof(Optic).Assembly.GetName().Name);
    }

    [Fact]
    public void CoreAndApplicationDoNotReferenceUiFrameworks()
    {
        AssertNoUiReferences(typeof(Optic).Assembly);
        AssertNoUiReferences(typeof(IWorkbenchApplication).Assembly);
    }

    [Fact]
    public void ApplicationContractsDoNotExposeCoreTypes()
    {
        var contractTypes = typeof(IWorkbenchApplication).Assembly.ExportedTypes
            .Where(type => type.Namespace == typeof(IWorkbenchApplication).Namespace)
            .ToArray();

        foreach (var contractType in contractTypes)
        {
            AssertNotCoreType(contractType);
            foreach (var memberType in PublicMemberTypes(contractType))
            {
                AssertNotCoreType(memberType);
            }
        }
    }

    [Fact]
    public void WorkbenchApplicationIsOnlyACompositionRoot()
    {
        var interfaces = typeof(WorkbenchApplication).GetInterfaces();

        Assert.Contains(typeof(IWorkbenchApplication), interfaces);
        Assert.DoesNotContain(typeof(IOpticalDocumentService), interfaces);
        Assert.DoesNotContain(typeof(IPrescriptionService), interfaces);
        Assert.DoesNotContain(typeof(IAnalysisService), interfaces);
        Assert.DoesNotContain(typeof(IVisualizationService), interfaces);
        Assert.DoesNotContain(typeof(IOptimizationService), interfaces);
        Assert.DoesNotContain(typeof(ITolerancingService), interfaces);
        Assert.DoesNotContain(typeof(IMultiConfigurationService), interfaces);
        Assert.DoesNotContain(typeof(IMaterialCatalogService), interfaces);
        Assert.DoesNotContain(typeof(IWorkspaceEventStream), interfaces);
    }

    [Fact]
    public void MainApplicationProjectDoesNotCompileTheLegacyNamespace()
    {
        var applicationRoot = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "OptilandWorkbench.Application");
        var legacyFiles = Directory.EnumerateFiles(applicationRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains(
                "namespace OptilandWorkbench.Application.Legacy",
                StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(legacyFiles);
    }

    [Fact]
    public void RuntimeDoesNotDiscoverOrParseAUserZemaxStockCatalog()
    {
        var sourceRoot = Path.Combine(FindRepositoryRoot(), "src");
        var source = string.Join(
            '\n',
            Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
                .Select(File.ReadAllText));

        Assert.DoesNotContain("ZemaxStockCatalogReader", source, StringComparison.Ordinal);
        Assert.DoesNotContain("zemaxStockCatalogDirectory", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"Stockcat\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopLaunchersRestorePackagesBeforeCleaningBuildOutputs()
    {
        var repositoryRoot = FindRepositoryRoot();
        var windowsLauncher = File.ReadAllText(Path.Combine(repositoryRoot, "Run-Optiland.cmd"));
        var unixLauncher = File.ReadAllText(Path.Combine(repositoryRoot, "Run-Optiland.command"));

        Assert.True(
            windowsLauncher.IndexOf("dotnet restore", StringComparison.Ordinal) <
            windowsLauncher.IndexOf("dotnet clean", StringComparison.Ordinal));
        Assert.Contains("dotnet build \"%PROJECT%\" --no-restore", windowsLauncher, StringComparison.Ordinal);
        Assert.True(
            unixLauncher.IndexOf("restore \"$PROJECT\"", StringComparison.Ordinal) <
            unixLauncher.IndexOf("clean \"$PROJECT\"", StringComparison.Ordinal));
        Assert.Contains("build \"$PROJECT\" --no-restore", unixLauncher, StringComparison.Ordinal);
    }

    [Fact]
    public void GlobalNumericInputStyleUsesTheAcceptedArrowFreeChrome()
    {
        var appSource = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "OptilandWorkbench.App",
            "App.cs"));

        Assert.Contains(
            "new Setter(NumericUpDown.ShowButtonSpinnerProperty, false)",
            appSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionServicesDoNotReferenceLegacyNamespace()
    {
        var servicesRoot = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "OptilandWorkbench.Application",
            "Services");
        var actual = Directory.EnumerateFiles(servicesRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains(
                "using OptilandWorkbench.Application.Legacy;",
                StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(servicesRoot, path).Replace('\\', '/'))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Empty(actual);
    }

    [Fact]
    public void ProductionServicesUseTheCanonicalRuntimeVocabulary()
    {
        var servicesRoot = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "OptilandWorkbench.Application",
            "Services");

        var legacyVocabulary = Directory.EnumerateFiles(servicesRoot, "*.cs", SearchOption.AllDirectories)
            .SelectMany(path => File.ReadLines(path)
                .Select((line, index) => (Path: path, Line: line, LineNumber: index + 1)))
            .Where(item => System.Text.RegularExpressions.Regex.IsMatch(
                item.Line,
                @"\bOpticalWorkspaceModel\b|\bConnector\."))
            .Select(item => $"{Path.GetRelativePath(servicesRoot, item.Path).Replace('\\', '/')}:{item.LineNumber}: {item.Line.Trim()}")
            .ToArray();

        Assert.True(
            legacyVocabulary.Length == 0,
            "Production services must use WorkbenchRuntime directly:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, legacyVocabulary));
    }

    [Fact]
    public void AppUiFontSizesUseTypographyTokens()
    {
        var appRoot = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "OptilandWorkbench.App");
        var literalFontSizes = Directory.EnumerateFiles(appRoot, "*.cs", SearchOption.AllDirectories)
            .SelectMany(path => File.ReadLines(path)
                .Select((line, index) => (Path: path, Line: line, LineNumber: index + 1)))
            .Where(item => System.Text.RegularExpressions.Regex.IsMatch(
                item.Line,
                @"\bFontSize\s*=\s*\d"))
            .Select(item => $"{Path.GetRelativePath(appRoot, item.Path).Replace('\\', '/')}:{item.LineNumber}: {item.Line.Trim()}")
            .ToArray();

        Assert.True(
            literalFontSizes.Length == 0,
            "Use DisplayTypography named tokens instead of literal UI FontSize values:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, literalFontSizes));
    }

    [Fact]
    public void AppUiCardsUseSharedChromeTokens()
    {
        var appRoot = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "OptilandWorkbench.App");
        var allowedShadowFiles = new HashSet<string>(StringComparer.Ordinal)
        {
            "Controls/SettingsPanelChrome.cs",
            "Theming/ThemeChrome.cs"
        };
        var scatteredShadows = Directory.EnumerateFiles(appRoot, "*.cs", SearchOption.AllDirectories)
            .SelectMany(path => File.ReadLines(path)
                .Select((line, index) => (Path: path, Line: line, LineNumber: index + 1)))
            .Where(item => item.Line.Contains("BoxShadows.Parse", StringComparison.Ordinal))
            .Where(item => !allowedShadowFiles.Contains(Path.GetRelativePath(appRoot, item.Path).Replace('\\', '/')))
            .Select(item => $"{Path.GetRelativePath(appRoot, item.Path).Replace('\\', '/')}:{item.LineNumber}: {item.Line.Trim()}")
            .ToArray();
        var splitCardRadii = Directory.EnumerateFiles(appRoot, "*.cs", SearchOption.AllDirectories)
            .SelectMany(path => File.ReadLines(path)
                .Select((line, index) => (Path: path, Line: line, LineNumber: index + 1)))
            .Where(item => System.Text.RegularExpressions.Regex.IsMatch(
                item.Line,
                @"new\s+(?:Avalonia\.)?CornerRadius\((6|7|9)\)"))
            .Select(item => $"{Path.GetRelativePath(appRoot, item.Path).Replace('\\', '/')}:{item.LineNumber}: {item.Line.Trim()}")
            .ToArray();

        Assert.True(
            scatteredShadows.Length == 0,
            "Card shadows must come from theme Chrome profiles instead of local strings:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, scatteredShadows));
        Assert.True(
            splitCardRadii.Length == 0,
            "Card and control radii must come from theme Chrome roles:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, splitCardRadii));
    }

    [Fact]
    public void ThemeSpecificUiCodeStaysInsideRegisteredThemePackages()
    {
        var appRoot = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "OptilandWorkbench.App");
        var violations = Directory.EnumerateFiles(appRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(appRoot, path)
                .Replace('\\', '/')
                .StartsWith("Theming/", StringComparison.Ordinal))
            .SelectMany(path => File.ReadLines(path)
                .Select((line, index) => (Path: path, Line: line, LineNumber: index + 1)))
            .Where(item => item.Line.Contains("IsekaiTheme", StringComparison.Ordinal)
                || item.Line.Contains("\"Isekai\"", StringComparison.Ordinal))
            .Select(item => $"{Path.GetRelativePath(appRoot, item.Path).Replace('\\', '/')}:{item.LineNumber}: {item.Line.Trim()}")
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Theme-specific decisions belong in registered theme packages, not business UI:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));

        var settingsSource = File.ReadAllText(Path.Combine(appRoot, "DisplaySettingsWindow.cs"));
        var workspaceSource = File.ReadAllText(Path.Combine(appRoot, "Shell", "MainWindow.Workspace.cs"));
        var resolverSource = File.ReadAllText(Path.Combine(appRoot, "Controls", "LocalIcon.cs"));
        Assert.Contains("ThemeRegistry.SelectableThemes", settingsSource);
        Assert.Contains("ApplyTheme(_settings.Theme)", workspaceSource);
        Assert.DoesNotContain("Isekai", resolverSource, StringComparison.Ordinal);

        var globalThemeMutations = Directory.EnumerateFiles(appRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !string.Equals(
                Path.GetRelativePath(appRoot, path).Replace('\\', '/'),
                "Theming/ThemeApplicationService.cs",
                StringComparison.Ordinal))
            .SelectMany(path => File.ReadLines(path)
                .Select((line, index) => (Path: path, Line: line, LineNumber: index + 1)))
            .Where(item => item.Line.Contains(
                "Application.Current.RequestedThemeVariant",
                StringComparison.Ordinal))
            .Select(item => $"{Path.GetRelativePath(appRoot, item.Path).Replace('\\', '/')}:{item.LineNumber}: {item.Line.Trim()}")
            .ToArray();
        Assert.True(
            globalThemeMutations.Length == 0,
            "Global runtime theme changes must use ThemeApplicationService:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, globalThemeMutations));
    }

    [Fact]
    public void OrdinaryAppUiColorsUseThemeResources()
    {
        var appRoot = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "OptilandWorkbench.App");
        var engineeringOrThemeFiles = new HashSet<string>(StringComparer.Ordinal)
        {
            "Controls/AnalysisPlotControl.cs",
            "Controls/DielectricGlassMaterial.cs",
            "Controls/FoucaultPlotControl.cs",
            "Controls/FullFieldAberrationControl.cs",
            "Controls/LocalIcon.cs",
            "Controls/OpticSceneControl.cs",
            "Controls/SeidelDiagramControl.cs",
            "Controls/SpectralColorMap.cs",
            "Controls/WavefrontSurfaceControl.cs",
            "Panels/Analysis/AnalysisPanel.Plots.cs",
            "Panels/Analysis/AnalysisSemanticColors.cs",
            "Panels/MeritOperandRowPalette.cs",
            "Panels/OptimizationPanel.cs",
            "SplashWindow.cs"
        };
        var themeOwnedPrefixes = new[]
        {
            "Theming/",
            "Manufacturing/"
        };
        var directColorPattern = new System.Text.RegularExpressions.Regex(
            @"\bColor\.From(?:Rgb|Argb)\s*\(|\bBrushes\.(?!Transparent\b)|\bColors\.(?!Transparent\b)",
            System.Text.RegularExpressions.RegexOptions.Compiled);
        var violations = Directory.EnumerateFiles(appRoot, "*.cs", SearchOption.AllDirectories)
            .Select(path => (Path: path, RelativePath: Path.GetRelativePath(appRoot, path).Replace('\\', '/')))
            .Where(item => !themeOwnedPrefixes.Any(prefix =>
                item.RelativePath.StartsWith(prefix, StringComparison.Ordinal)))
            .Where(item => !engineeringOrThemeFiles.Contains(item.RelativePath))
            .SelectMany(item => File.ReadLines(item.Path)
                .Select((line, index) => (item.RelativePath, Line: line, LineNumber: index + 1)))
            .Where(item => directColorPattern.IsMatch(item.Line))
            .Select(item => $"{item.RelativePath}:{item.LineNumber}: {item.Line.Trim()}")
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Ordinary App UI colors must use ThemeResourceBindings or theme-derived brushes. "
            + "Only engineering semantics such as drawings, wavelength colors, ray colors, "
            + "analysis color maps and Zemax row palettes may use fixed colors:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void AppUiTableDensityUsesSharedTokens()
    {
        var appRoot = Path.Combine(FindRepositoryRoot(), "src", "OptilandWorkbench.App");
        var literalTableHeights = Directory.EnumerateFiles(appRoot, "*.cs", SearchOption.AllDirectories)
            .SelectMany(path => File.ReadLines(path)
                .Select((line, index) => (Path: path, Line: line, LineNumber: index + 1)))
            .Where(item => System.Text.RegularExpressions.Regex.IsMatch(
                item.Line,
                @"\b(?:RowHeight|ColumnHeaderHeight)\s*=\s*\d"))
            .Select(item => $"{Path.GetRelativePath(appRoot, item.Path).Replace('\\', '/')}:{item.LineNumber}: {item.Line.Trim()}")
            .ToArray();

        Assert.True(
            literalTableHeights.Length == 0,
            "Use UiDensity table tokens instead of literal row/header heights:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, literalTableHeights));
    }

    [Fact]
    public void DockDocumentsDoNotRestoreLargeFixedMinimumWidths()
    {
        var appRoot = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "OptilandWorkbench.App");
        var responsiveDocuments = new[]
        {
            "Panels/Analysis/AnalysisPanel.Parameters.cs",
            "Panels/CommercialLensCatalogPanel.cs",
            "Panels/MaterialAnalysisPanel.cs",
            "Panels/MaterialDatabasePanels.cs",
            "Panels/ViewerPanel.cs"
        };
        var violations = responsiveDocuments
            .SelectMany(relativePath => File.ReadLines(Path.Combine(
                    appRoot,
                    relativePath.Replace('/', Path.DirectorySeparatorChar)))
                .Select((line, index) => (RelativePath: relativePath, Line: line, LineNumber: index + 1)))
            .SelectMany(item => System.Text.RegularExpressions.Regex.Matches(
                    item.Line,
                    @"\bMinWidth\s*=\s*(?<width>\d+)")
                .Select(match => (item.RelativePath, item.Line, item.LineNumber, Width: int.Parse(match.Groups["width"].Value))))
            .Where(item => item.Width >= 500)
            .Select(item => $"{item.RelativePath}:{item.LineNumber}: {item.Line.Trim()}")
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Dock documents must reflow instead of imposing a minimum width of 500px or more:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));

        var mainWindow = File.ReadAllText(Path.Combine(appRoot, "MainWindow.cs"));
        Assert.Contains("MinWidth = 640;", mainWindow);
        Assert.Contains("Math.Clamp(_settings.WindowWidth, 640, 4096)", mainWindow);
    }

    [Fact]
    public void LongRunningPanelsUseSharedOperationStatusBar()
    {
        var appRoot = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "OptilandWorkbench.App");
        var statusBarSource = File.ReadAllText(Path.Combine(appRoot, "Controls", "OperationStatusBar.cs"));

        Assert.Contains("ProgressBar", statusBarSource);
        Assert.Contains("TimeSpan.FromMilliseconds(500)", statusBarSource);
        Assert.Contains("TimeSpan.FromSeconds(2)", statusBarSource);
        Assert.Contains("Content = \"取消\"", statusBarSource);

        var requiredPanels = new[]
        {
            "Panels/AnalysisPanel.cs",
            "Panels/OptimizationPanel.cs",
            "Panels/TolerancingPanel.cs"
        };
        foreach (var relativePath in requiredPanels)
        {
            var source = File.ReadAllText(Path.Combine(appRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            Assert.Contains("OperationStatusBar", source);
            Assert.Contains("_operationStatus.Start(", source);
            Assert.Contains("Cancel()", source);
            Assert.Contains("_operationStatus.MarkFailed(", source);
        }

        var scatteredProgressBars = Directory.EnumerateFiles(appRoot, "*.cs", SearchOption.AllDirectories)
            .SelectMany(path => File.ReadLines(path)
                .Select((line, index) => (Path: path, Line: line, LineNumber: index + 1)))
            .Where(item => item.Line.Contains("ProgressBar", StringComparison.Ordinal))
            .Where(item =>
            {
                var relative = Path.GetRelativePath(appRoot, item.Path).Replace('\\', '/');
                return relative is not "Controls/OperationStatusBar.cs" and not "SplashWindow.cs";
            })
            .Select(item => $"{Path.GetRelativePath(appRoot, item.Path).Replace('\\', '/')}:{item.LineNumber}: {item.Line.Trim()}")
            .ToArray();
        Assert.True(
            scatteredProgressBars.Length == 0,
            "Use OperationStatusBar for long-running App UI progress instead of local ProgressBar instances:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, scatteredProgressBars));
    }

    private static void AssertNoUiReferences(Assembly assembly)
    {
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference =>
            reference.Name?.StartsWith("Avalonia", StringComparison.Ordinal) == true
            || reference.Name?.StartsWith("Dock.", StringComparison.Ordinal) == true);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OptilandWorkbench.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate the repository root.");
    }

    private static IEnumerable<Type> PublicMemberTypes(Type contractType)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
        foreach (var property in contractType.GetProperties(flags))
        {
            yield return property.PropertyType;
        }

        foreach (var eventInfo in contractType.GetEvents(flags))
        {
            if (eventInfo.EventHandlerType is not null)
            {
                yield return eventInfo.EventHandlerType;
            }
        }

        foreach (var method in contractType.GetMethods(flags))
        {
            yield return method.ReturnType;
            foreach (var parameter in method.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }

        foreach (var constructor in contractType.GetConstructors(flags))
        {
            foreach (var parameter in constructor.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }
    }

    private static void AssertNotCoreType(Type type)
    {
        foreach (var exposedType in Flatten(type))
        {
            Assert.False(
                exposedType.Namespace?.StartsWith("OptilandWorkbench.Core", StringComparison.Ordinal) == true,
                $"Application contract exposes Core type {exposedType.FullName}.");
        }
    }

    private static IEnumerable<Type> Flatten(Type type)
    {
        if (type.IsByRef || type.IsArray || type.IsPointer)
        {
            yield return type.GetElementType()!;
            yield break;
        }

        yield return type;
        foreach (var argument in type.GetGenericArguments())
        {
            foreach (var nested in Flatten(argument))
            {
                yield return nested;
            }
        }
    }
}

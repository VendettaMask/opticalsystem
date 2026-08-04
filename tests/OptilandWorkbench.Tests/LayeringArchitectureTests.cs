using System.Reflection;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Legacy;
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
    public void LegacyConnectorAddsNoMembersButStillInheritsTheLegacyRuntime()
    {
        Assert.Equal(typeof(OpticalWorkspaceModel), typeof(OptilandConnector).BaseType);

        var declaredMethods = typeof(OptilandConnector).GetMethods(
            BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Instance
            | BindingFlags.Static
            | BindingFlags.DeclaredOnly);
        Assert.Empty(declaredMethods);
    }

    [Fact]
    public void ProductionServicesCannotAddNewLegacyDependencies()
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "AnalysisService.cs",
            "Mapping/WorkbenchMapper.cs",
            "MaterialCatalogService.Analysis.cs",
            "MaterialCatalogService.cs",
            "MultiConfigurationService.cs",
            "OpticalDocumentService.cs",
            "OpticContext.cs",
            "OptimizationService.cs",
            "OptimizationService.Run.cs",
            "PrescriptionService.cs",
            "TolerancingService.cs",
            "VisualizationService.cs",
            "WorkbenchServiceBase.cs",
            "WorkspaceCoordinator.cs"
        };
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

        Assert.True(
            actual.IsSubsetOf(allowed),
            $"New production Legacy dependencies: {string.Join(", ", actual.Except(allowed))}");
    }

    [Fact]
    public void ProductionServicesCannotIncreaseLegacyRuntimeCalls()
    {
        var maximumReferences = new Dictionary<string, (int Model, int Connector)>(StringComparer.Ordinal)
        {
            ["AnalysisService.cs"] = (3, 5),
            ["CadExportService.cs"] = (0, 1),
            ["MaterialCatalogService.cs"] = (1, 0),
            ["MultiConfigurationService.cs"] = (0, 4),
            ["OpticalDocumentService.cs"] = (2, 8),
            ["OpticContext.cs"] = (3, 0),
            ["OptimizationService.cs"] = (0, 11),
            ["OptimizationService.Run.cs"] = (2, 10),
            ["PrescriptionService.cs"] = (0, 44),
            ["TolerancingService.cs"] = (1, 4),
            ["VisualizationService.cs"] = (0, 4),
            ["WorkbenchServiceBase.cs"] = (1, 1),
            ["WorkspaceCoordinator.cs"] = (1, 10)
        };
        var servicesRoot = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "OptilandWorkbench.Application",
            "Services");

        foreach (var path in Directory.EnumerateFiles(servicesRoot, "*.cs", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(path);
            var modelReferences = System.Text.RegularExpressions.Regex.Matches(
                source,
                @"\bOpticalWorkspaceModel\b").Count;
            var connectorCalls = System.Text.RegularExpressions.Regex.Matches(
                source,
                @"\bConnector\.").Count;
            if (modelReferences == 0 && connectorCalls == 0)
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(servicesRoot, path).Replace('\\', '/');
            Assert.True(
                maximumReferences.TryGetValue(relativePath, out var maximum),
                $"New production Legacy runtime dependency: {relativePath}");
            Assert.True(
                modelReferences <= maximum.Model,
                $"{relativePath} increased OpticalWorkspaceModel references from {maximum.Model} to {modelReferences}.");
            Assert.True(
                connectorCalls <= maximum.Connector,
                $"{relativePath} increased Connector calls from {maximum.Connector} to {connectorCalls}.");
        }
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
            "Shell/MainWindow.Shell.cs"
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
            "Use SettingsPanelChrome.CardShadow instead of local card shadow strings:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, scatteredShadows));
        Assert.True(
            splitCardRadii.Length == 0,
            "Use SettingsPanelChrome.CardCornerRadius or ControlCornerRadius instead of split card/control radius values:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, splitCardRadii));
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

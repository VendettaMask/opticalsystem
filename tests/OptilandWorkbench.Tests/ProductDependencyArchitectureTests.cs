using System.Text.RegularExpressions;
using System.Xml.Linq;
using OptilandWorkbench.App;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Backend;

namespace OptilandWorkbench.Tests;

public sealed class ProductDependencyArchitectureTests
{
    private static readonly Regex ForbiddenDependency = new(
        @"\b(?:python\w*|ironpython|cpython|py\.exe)\b|(?:Process\.Start|ProcessStartInfo)[^\r\n]*['""]py['""]|Py\.GIL|tools[/\\]python-reference|validation[/\\]history|tools[/\\]zemax_parity|\btests[/\\]|OptilandConnector|OptilandWorkbench\.Compatibility",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [Fact]
    public void ProductSourcesAndBuildInputsHaveNoPythonOrValidationDependencies()
    {
        var root = RepositoryRoot();
        var inputs = SourceFiles(Path.Combine(root, "src"))
            .Concat(Directory.EnumerateFiles(root).Where(path =>
                Path.GetExtension(path) is ".props" or ".targets" or ".cmd" or ".command"))
            .Concat(SourceFiles(Path.Combine(root, "packaging")))
            .Where(path => Path.GetExtension(path) is ".cs" or ".csproj" or ".props" or ".targets"
                or ".json" or ".cmd" or ".command" or ".ps1" or ".sh");
        var violations = inputs.SelectMany(path => File.ReadLines(path)
                .Select((line, index) => (Path: path, Line: index + 1, Text: line)))
            .Where(item => ForbiddenDependency.IsMatch(RemoveRejectionOnlySuffixes(item.Path, item.Text)))
            .Select(item => $"{Path.GetRelativePath(root, item.Path)}:{item.Line}: {item.Text}")
            .ToArray();
        Assert.True(violations.Length == 0, string.Join(Environment.NewLine, violations));

        Assert.DoesNotContain(SourceFiles(Path.Combine(root, "src")), path =>
            Path.GetExtension(path).Equals(".py", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ProductProjectReferencesStayInsideSrcAndAssembliesExcludeRetiredAdapters()
    {
        var src = Path.Combine(RepositoryRoot(), "src");
        foreach (var project in SourceFiles(src).Where(path => Path.GetExtension(path) == ".csproj"))
        {
            var document = XDocument.Load(project);
            foreach (var reference in document.Descendants("ProjectReference"))
            {
                var target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project)!,
                    reference.Attribute("Include")!.Value.Replace('\\', Path.DirectorySeparatorChar)));
                Assert.StartsWith(src + Path.DirectorySeparatorChar, target, StringComparison.OrdinalIgnoreCase);
                Assert.True(File.Exists(target), target);
            }
        }

        foreach (var assembly in new[] { typeof(Optic).Assembly, typeof(IWorkbenchApplication).Assembly, typeof(MainWindow).Assembly })
        {
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference => ForbiddenDependency.IsMatch(reference.Name!));
            Assert.DoesNotContain(assembly.ExportedTypes, type => ForbiddenDependency.IsMatch(type.FullName!));
            Assert.DoesNotContain(assembly.GetManifestResourceNames(), resource => ForbiddenDependency.IsMatch(resource));
        }

        Assert.True(typeof(INumericBackend).IsAssignableFrom(typeof(ManagedCpuBackend)));
        Assert.True(typeof(IBatchedNumericBackend).IsAssignableFrom(typeof(ManagedCpuBackend)));
        Assert.IsType<ManagedCpuBackend>(new NumericBackendProvider().Current);
    }

    [Theory]
    [InlineData("Process.Start(\"python\", \"worker.py\")")]
    [InlineData("new ProcessStartInfo(\"py\", \"worker.py\")")]
    [InlineData("new ProcessStartInfo(\"python3.exe\")")]
    [InlineData("Python.Runtime.PythonEngine.Initialize()")]
    [InlineData("PackageReference Include=\"pythonnet\"")]
    [InlineData("Content Include=\"../../tools/python-reference/input.json\"")]
    [InlineData("File.ReadAllText(\"../../validation/history/reference.json\")")]
    public void DependencyGuardDetectsProhibitedRuntimeAndAssetReferences(string source)
    {
        Assert.Matches(ForbiddenDependency, source);
    }

    private static string RemoveRejectionOnlySuffixes(string path, string text) =>
        Path.GetFileName(path) == "WorkbenchRuntime.cs"
            ? text.Replace("\".optiland-python.json\"", "\"\"", StringComparison.Ordinal)
                .Replace("\".python-optiland.json\"", "\"\"", StringComparison.Ordinal)
            : text;

    private static IEnumerable<string> SourceFiles(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .Where(path => !Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(part => part is "bin" or "obj"));

    internal static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OptilandWorkbench.slnx")))
            {
                return directory.FullName;
            }
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}

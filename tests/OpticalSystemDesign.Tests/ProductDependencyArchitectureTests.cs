using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using OpticalSystemDesign.App;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Backend;

namespace OpticalSystemDesign.Tests;

public sealed class ProductDependencyArchitectureTests
{
    private static readonly Regex ForbiddenDependency = new(
        @"\b(?:python\w*|ironpython|cpython|py\.exe)\b|(?:Process\.Start|ProcessStartInfo)[^\r\n]*['""]py['""]|Py\.GIL|tools[/\\]python-reference|validation[/\\]history|tools[/\\]zemax_parity|\btests[/\\]|OpticalSystemDesignConnector|OpticalSystemDesign\.Compatibility",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex InteropReference = new(
        @"\b(?:Python\.Runtime|PythonEngine|IronPython|CPython)\b|\bPy\.GIL\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [Fact]
    public void ProductSourcesAndBuildInputsHaveNoPythonOrValidationDependencies()
    {
        var root = RepositoryRoot();
        var inputs = ProductSourceRoots(root).SelectMany(SourceFiles)
            .Concat(Directory.EnumerateFiles(root).Where(path =>
                Path.GetExtension(path) is ".props" or ".targets" or ".cmd" or ".command"))
            .Concat(SourceFiles(Path.Combine(root, "packaging")))
            .Where(path => Path.GetExtension(path) is ".cs" or ".csproj" or ".props" or ".targets"
                or ".json" or ".cmd" or ".command" or ".ps1" or ".sh");
        var violations = inputs.SelectMany(path =>
            {
                var source = File.ReadAllText(path);
                var lines = source.Split('\n');
                return ForbiddenSourceLines(source, Path.GetExtension(path)).Select(line =>
                    $"{Path.GetRelativePath(root, path)}:{line + 1}: {lines[line].TrimEnd('\r')}");
            })
            .ToArray();
        Assert.True(violations.Length == 0, string.Join(Environment.NewLine, violations));

        Assert.DoesNotContain(ProductSourceRoots(root).SelectMany(SourceFiles), path =>
            Path.GetExtension(path).Equals(".py", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ProductProjectReferencesStayInsideProductAndAssembliesExcludeRetiredAdapters()
    {
        var roots = ProductSourceRoots(RepositoryRoot()).ToArray();
        foreach (var project in roots.SelectMany(SourceFiles).Where(path => Path.GetExtension(path) == ".csproj"))
        {
            var document = XDocument.Load(project);
            foreach (var reference in document.Descendants("ProjectReference"))
            {
                var target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project)!,
                    reference.Attribute("Include")!.Value.Replace('\\', Path.DirectorySeparatorChar)));
                Assert.Contains(roots, root => target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
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
    [InlineData(".cs", "Process.Start(\"python\", \"worker.py\");")]
    [InlineData(".cs", "new ProcessStartInfo(\"py\", \"worker.py\");")]
    [InlineData(".cs", "new ProcessStartInfo(\"python3.exe\");")]
    [InlineData(".cs", "new ProcessStartInfo { FileName = \"py\" };")]
    [InlineData(".cs", "start.FileName = \"py\";")]
    [InlineData(".cs", "Python.Runtime.PythonEngine.Initialize();")]
    [InlineData(".cs", "using Interop = Python . Runtime;")]
    [InlineData(".cs", "Py . GIL();")]
    [InlineData(".cs", "Process.Start(\"\"\"python\"\"\");")]
    [InlineData(".cs", "Process.Start($\"python{version}.exe\");")]
    [InlineData(".cs", "#if UNUSED\nProcess.Start(\"python\");\n#endif")]
    [InlineData(".cs", "#if OUTER\n#if INNER\nProcess.Start(\"python\");\n#endif\n#endif")]
    [InlineData(".csproj", "<PackageReference Include=\"pythonnet\" />")]
    [InlineData(".props", "<Content Include=\"../../tools/python-reference/input.json\" />")]
    [InlineData(".cs", "File.ReadAllText(\"../../validation/history/reference.json\");")]
    [InlineData(".cs", "File.ReadAllText(@\"..\u005c..\u005cvalidation\u005chistory\u005creference.json\");")]
    [InlineData(".ps1", "& python3 worker.py")]
    [InlineData(".json", "{\"runtime\":\"python\"}")]
    public void DependencyGuardDetectsProhibitedRuntimeAndAssetReferences(string extension, string source)
    {
        Assert.NotEmpty(ForbiddenSourceLines(source, extension));
    }

    [Theory]
    [InlineData("// Keep source table order on ties, as Python min() does.")]
    [InlineData("/// <summary>Python random, NumPy and torch input stream conventions.</summary>\nclass Stream {}")]
    [InlineData("/* Process.Start(\"python\"); Python.Runtime.PythonEngine.Initialize(); */")]
    [InlineData("class Stream { bool Seed(bool python = false) => !python; }")]
    [InlineData("class Stream { void Next() { python.Next(); } }")]
    [InlineData("var url = \"https://example.test/trace\"; // Python source description")]
    [InlineData("var text = \"quoted \\\" // preserved\"; /* Python.Runtime */")]
    [InlineData("DoubleParameter(\"Py\", \"Py\", \"0\", -1, 1, 0.01);")]
    [InlineData("var coordinates = new[] { \"Px\", \"Py\" };")]
    public void DependencyGuardAllowsNativeIdentifiersAndSourceProvenanceComments(string source) =>
        Assert.Empty(ForbiddenSourceLines(source, ".cs"));

    private static IEnumerable<int> ForbiddenSourceLines(string source, string extension)
    {
        if (extension != ".cs") return source.Split('\n').Select((line, index) => (line, index))
            .Where(item => ForbiddenDependency.IsMatch(item.line)).Select(item => item.index);

        var violations = new SortedSet<int>();
        var pending = new Stack<(string Source, int Line)>();
        var visited = new HashSet<(string Source, int Line)>();
        pending.Push((source, 0));
        while (pending.TryPop(out var input))
        {
            if (!visited.Add(input)) continue;
            var tree = CSharpSyntaxTree.ParseText(input.Source);
            var root = tree.GetRoot();
            void Add(SyntaxNodeOrToken item) => violations.Add(input.Line + tree.GetLineSpan(item.Span).StartLinePosition.Line);
            foreach (var token in root.DescendantTokens())
            {
                if (token.Kind() is not (SyntaxKind.StringLiteralToken or SyntaxKind.SingleLineRawStringLiteralToken
                    or SyntaxKind.MultiLineRawStringLiteralToken or SyntaxKind.Utf8StringLiteralToken
                    or SyntaxKind.Utf8SingleLineRawStringLiteralToken or SyntaxKind.Utf8MultiLineRawStringLiteralToken
                    or SyntaxKind.InterpolatedStringTextToken)) continue;
                if (ForbiddenDependency.IsMatch(token.ValueText) || IsLauncherLiteral(token)) Add(token);
            }
            foreach (var node in root.DescendantNodes().Where(node => node is UsingDirectiveSyntax
                or QualifiedNameSyntax or AliasQualifiedNameSyntax or MemberAccessExpressionSyntax))
            {
                var text = string.Concat(node.DescendantTokens().Select(token => token.Text));
                if (node is UsingDirectiveSyntax or QualifiedNameSyntax or AliasQualifiedNameSyntax)
                {
                    if (ForbiddenDependency.IsMatch(text)) Add(node);
                }
                else if (node is MemberAccessExpressionSyntax && InteropReference.IsMatch(text)) Add(node);
            }
            // Inspect inactive branches too; a platform-specific dependency must not hide behind #if.
            foreach (var trivia in root.DescendantTrivia().Where(item => item.IsKind(SyntaxKind.DisabledTextTrivia)))
                pending.Push((trivia.ToFullString(), input.Line + tree.GetLineSpan(trivia.Span).StartLinePosition.Line));
        }
        return violations;
    }

    private static bool IsLauncherLiteral(SyntaxToken token)
    {
        if (!token.ValueText.Equals("py", StringComparison.OrdinalIgnoreCase)) return false;
        // "Py" is also a pupil coordinate. A launcher name needs executable context.
        foreach (var node in token.Parent!.Ancestors())
        {
            var target = node switch
            {
                ObjectCreationExpressionSyntax creation => creation.Type,
                InvocationExpressionSyntax invocation => invocation.Expression,
                AssignmentExpressionSyntax assignment => assignment.Left,
                _ => null
            };
            if (target is null) continue;
            var name = string.Concat(target.DescendantTokens().Select(item => item.Text));
            if (name is "ProcessStartInfo" or "Process.Start" or "FileName" ||
                name.EndsWith(".ProcessStartInfo", StringComparison.Ordinal) ||
                name.EndsWith(".Process.Start", StringComparison.Ordinal) ||
                name.EndsWith(".FileName", StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private static IEnumerable<string> SourceFiles(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .Where(path => !Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(part => part is "bin" or "obj"));

    private static IEnumerable<string> ProductSourceRoots(string root) =>
        new[] { Path.Combine(root, "src"), Path.Combine(root, "labs", "InitialStructure", "src"),
            Path.Combine(root, "labs", "CoatingDesign", "src") };

    internal static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpticalSystemDesign.slnx")))
            {
                return directory.FullName;
            }
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}

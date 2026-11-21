using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

var sourceRoot = Path.GetFullPath(args[0]);
var paths = Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
    .Where(p => !p.Split(Path.DirectorySeparatorChar).Any(s => s is "bin" or "obj")).ToArray();
var trees = paths.Select(p => CSharpSyntaxTree.ParseText(File.ReadAllText(p), path: p))
    .Append(CSharpSyntaxTree.ParseText("global using System; global using System.Collections.Generic; global using System.IO; global using System.Linq; global using System.Threading; global using System.Threading.Tasks;", path: "audit-global-usings.cs")).ToArray();
var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
    .Select(p => MetadataReference.CreateFromFile(p));
var compilation = CSharpCompilation.Create("AimingPolicyAudit", trees, references,
    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
var calls = new List<object>();
var omittedDisabledCalls = 0;
foreach (var tree in trees)
{
    var relative = Path.GetRelativePath(sourceRoot, tree.FilePath).Replace('\\', '/');
    if (!relative.StartsWith("Analysis/", StringComparison.Ordinal)
        && !(args.Contains("include-helpers") && (relative.StartsWith("Services/", StringComparison.Ordinal)
            || relative.StartsWith("Visualization/", StringComparison.Ordinal)))) continue;
    var model = compilation.GetSemanticModel(tree);
    foreach (var node in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
    {
        if (model.GetOperation(node) is not IInvocationOperation invocation) continue;
        var method = invocation.TargetMethod;
        var aiming = invocation.Arguments.Where(a => a.Parameter?.Name is "aimAtStop" or "useRayAiming").ToArray();
        if (aiming.Length == 0 && (method.ContainingType.Name != "RayGenerator"
            || !method.Name.Contains("Generate", StringComparison.Ordinal))) continue;
        var caller = model.GetEnclosingSymbol(node.SpanStart)?.ToDisplayString();
        var policies = aiming.Select(a => new
        {
            parameter = a.Parameter!.Name, omitted = a.IsImplicit,
            value = a.IsImplicit ? a.Parameter.HasExplicitDefaultValue ? a.Parameter.ExplicitDefaultValue?.ToString() ?? "null" : "?"
                : a.Value.Syntax.ToString().ReplaceLineEndings(" "),
            defaultValue = a.Parameter.HasExplicitDefaultValue ? a.Parameter.ExplicitDefaultValue?.ToString() ?? "null" : "required"
        }).ToArray();
        var line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        calls.Add(new { file = relative, line, caller, target = method.ToDisplayString(), policies });
        if (policies.Any(a => a.omitted && a.value == "False")) omittedDisabledCalls++;
        if (policies.Any(a => a.omitted && a.value == "False") || aiming.Length == 0)
            Console.WriteLine($"{relative}:{line} {caller} -> {method.ContainingType.Name}.{method.Name}: " +
                string.Join(", ", policies.Select(p => $"{p.parameter}={p.value} omitted={p.omitted}")));
    }
}
File.WriteAllText(args[1], JsonSerializer.Serialize(calls, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Recorded {calls.Count} aiming-sensitive analysis calls across {paths.Length} Core source files; omitted disabled policies: {omittedDisabledCalls}.");
if (args.Length > 2 && args[2] == "check" && (omittedDisabledCalls != 0 || calls.Count < 100))
    Environment.ExitCode = 1;

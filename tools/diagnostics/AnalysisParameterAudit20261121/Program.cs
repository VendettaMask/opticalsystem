using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

var root = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
var sourceRoot = Path.Combine(root, "src/OpticalSystemDesign.Application/Runtime");
var parameters = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(sourceRoot, "WorkbenchRuntime.Analysis.Parameters.cs"))).GetRoot();
var analysisTree = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(sourceRoot, "WorkbenchRuntime.Analysis.cs"))).GetRoot();
var factory = analysisTree.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.Text == "CreateAnalysis");
var wrapper = analysisTree.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.Text == "BuildAnalysisData");
var wrapperAnalyses = wrapper.DescendantNodes().OfType<ConstantPatternSyntax>().Select(p => Literal(p.Expression))
    .Concat(wrapper.DescendantNodes().OfType<BinaryExpressionSyntax>().Where(b => b.Left.ToString() == "canonicalName").Select(b => Literal(b.Right)))
    .OfType<string>().ToHashSet();
var wrapperKeys = wrapper.DescendantNodes().OfType<InvocationExpressionSyntax>()
    .Where(i => i.Expression.ToString() is "settings.TryGetValue" or "settings.GetValueOrDefault" or "SettingInt")
    .Select(i => Literal(i.ArgumentList.Arguments.FirstOrDefault()?.Expression)).OfType<string>().ToHashSet();
var declaration = parameters.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.Text == "GetAnalysisParameters");
var choices = declaration.DescendantNodes().OfType<SwitchExpressionSyntax>().First();
var dispatch = factory.DescendantNodes().OfType<SwitchExpressionSyntax>().Single(s => s.GoverningExpression.ToString() == "name");
var helpers = factory.DescendantNodes().OfType<LocalFunctionStatementSyntax>().ToDictionary(f => f.Identifier.Text);
var common = factory.DescendantNodes().OfType<InvocationExpressionSyntax>()
    .Where(i => !i.Ancestors().Any(p => p is LocalFunctionStatementSyntax || ReferenceEquals(p, dispatch)))
    .SelectMany(i => Keys(i, new())).ToHashSet();
var rows = choices.Arms.SelectMany(arm => Names(arm.Pattern).Select(name =>
{
    var keys = arm.Expression.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>()
        .Where(i => i.Expression.ToString().EndsWith("Parameter", StringComparison.Ordinal))
        .Select(i => Literal(i.ArgumentList.Arguments.FirstOrDefault()?.Expression)).OfType<string>().Distinct().ToArray();
    var target = dispatch.Arms.FirstOrDefault(a => Names(a.Pattern).Contains(name));
    var consumed = target is null ? new HashSet<string>() : target.Expression.DescendantNodesAndSelf()
        .OfType<InvocationExpressionSyntax>().SelectMany(i => Keys(i, new())).Concat(common).ToHashSet();
    var throughWrapper = wrapperAnalyses.Contains(name) ? keys.Where(wrapperKeys.Contains).ToArray() : [];
    return new { Analysis = name, Declared = keys, Consumed = consumed.Order().ToArray(), WrapperConsumed = throughWrapper, Unread = keys.Except(consumed).Except(throughWrapper).ToArray() };
})).ToArray();
var json = JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true });
if (args.Length > 1) File.WriteAllText(args[1], json); else Console.WriteLine(json);
Console.Error.WriteLine($"Analyses: {rows.Length}; declared keys: {rows.Sum(r => r.Declared.Length)}; unread candidates: {rows.Sum(r => r.Unread.Length)}");
foreach (var row in rows.Where(r => r.Unread.Length > 0)) Console.Error.WriteLine(row.Analysis + ": " + string.Join(", ", row.Unread));
Environment.ExitCode = rows.Any(r => r.Unread.Length > 0) ? 1 : 0;

IEnumerable<string> Keys(InvocationExpressionSyntax call, HashSet<string> visited)
{
    var name = call.Expression.ToString();
    if (name is "Int" or "Double" or "Bool" or "Text" or "LeadingInt" or "SignedLeadingInt")
    {
        if (Literal(call.ArgumentList.Arguments.FirstOrDefault()?.Expression) is { } key) yield return key;
    }
    if (helpers.TryGetValue(name, out var helper) && visited.Add(name))
        foreach (var nested in helper.DescendantNodes().OfType<InvocationExpressionSyntax>())
            foreach (var key in Keys(nested, visited)) yield return key;
}
static string? Literal(ExpressionSyntax? expression) => expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression) ? literal.Token.ValueText : null;
static IEnumerable<string> Names(PatternSyntax pattern) => pattern.DescendantNodesAndSelf().OfType<ConstantPatternSyntax>().Select(p => Literal(p.Expression)).OfType<string>();

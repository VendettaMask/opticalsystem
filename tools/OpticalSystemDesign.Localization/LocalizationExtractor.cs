using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OpticalSystemDesign.Localization;

public sealed record UiOccurrence(string Source, string Path, int Line, bool Template);
public sealed record TranslationEntry(string Source, string Chinese, string English, string Japanese);

/// <summary>Syntax-only extraction: never executes product code or reads user documents.</summary>
public static class LocalizationExtractor
{
    private static readonly HashSet<string> Properties = ["Text", "Content", "Header", "Title", "PlaceholderText", "Watermark"];
    private static readonly Dictionary<string, int[]> Calls = new(StringComparer.Ordinal)
    {
        ["Text"] = [0], ["Format"] = [0], ["SetTip"] = [1], ["SetName"] = [1], ["SetHelpText"] = [1],
        ["RibbonButton"] = [2], ["RibbonTab"] = [0], ["RibbonGroup"] = [0], ["Register"] = [1, 2]
    };

    public static IReadOnlyList<UiOccurrence> Extract(string source, string path)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        var calls = Calls.ToDictionary(x => x.Key, x => x.Value.ToHashSet(), StringComparer.Ordinal);
        // Infer local label helpers from parameter flow, rather than treating every string as UI.
        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>().ToArray();
        for (var round = 0; round < 8; round++)
        {
            var changed = false;
            foreach (var method in methods)
            {
                foreach (var expression in Sinks(method, calls))
                {
                    if (expression is not IdentifierNameSyntax parameter) continue;
                    var index = method.ParameterList.Parameters.Select((p, i) => (Parameter: p, Index: i))
                        .Where(x => x.Parameter.Identifier.ValueText == parameter.Identifier.ValueText
                            && x.Parameter.Type?.ToString() is "string" or "string?"
                            && x.Parameter.Identifier.ValueText is not ("id" or "actionId" or "iconName" or "key"))
                        .Select(x => x.Index).DefaultIfEmpty(-1).First();
                    if (index < 0) continue;
                    if (!calls.TryGetValue(method.Identifier.ValueText, out var indices))
                        calls[method.Identifier.ValueText] = indices = [];
                    changed |= indices.Add(index);
                }
            }
            if (!changed) break;
        }
        return Sinks(root, calls).SelectMany(Choices).DistinctBy(x => x.Span).Select(expression =>
        {
            var value = UiLiteral(expression);
            return value is null ? null : new UiOccurrence(value, path,
                expression.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                expression.DescendantNodesAndSelf().OfType<InterpolatedStringExpressionSyntax>().Any()
                    || (expression is BinaryExpressionSyntax && value.Contains('{')));
        }).OfType<UiOccurrence>().Where(x => IsLanguageText(x.Source)).Distinct().ToArray();
    }

    private static IEnumerable<ExpressionSyntax> Choices(ExpressionSyntax expression) => expression switch
    {
        ConditionalExpressionSyntax choice => Choices(choice.WhenTrue).Concat(Choices(choice.WhenFalse)),
        SwitchExpressionSyntax choice => choice.Arms.SelectMany(arm => Choices(arm.Expression)),
        _ => [expression]
    };

    private static string? UiLiteral(ExpressionSyntax expression)
    {
        if (Literal(expression) is { } literal) return literal;
        if (expression is not BinaryExpressionSyntax binary || !binary.IsKind(SyntaxKind.AddExpression)) return null;
        var index = 0;
        string Parts(ExpressionSyntax part)
        {
            if (part is BinaryExpressionSyntax sum && sum.IsKind(SyntaxKind.AddExpression)) return Parts(sum.Left) + Parts(sum.Right);
            if (part is InterpolatedStringExpressionSyntax interpolation)
                return string.Concat(interpolation.Contents.Select(item => item is InterpolatedStringTextSyntax text ? text.TextToken.ValueText : "{" + index++ + "}"));
            if (part is LiteralExpressionSyntax token && token.IsKind(SyntaxKind.StringLiteralExpression)) return token.Token.ValueText;
            return "{" + index++ + "}";
        }
        return Parts(expression);
    }

    public static bool IsLanguageText(string text) => text.Any(c => c is >= '\u4e00' and <= '\u9fff')
        || (text.Length > 1 && text.Count(char.IsAsciiLetter) > 1);

    private static IEnumerable<ExpressionSyntax> Sinks(SyntaxNode root, Dictionary<string, HashSet<int>> calls)
    {
        foreach (var assignment in root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            var name = assignment.Left is MemberAccessExpressionSyntax member ? member.Name.Identifier.ValueText : assignment.Left.ToString();
            if (Properties.Contains(name)) yield return assignment.Right;
            if (name == "ItemsSource")
            {
                IEnumerable<ExpressionSyntax> items = assignment.Right switch
                {
                    ArrayCreationExpressionSyntax array => array.Initializer?.Expressions ?? [],
                    ImplicitArrayCreationExpressionSyntax array => array.Initializer.Expressions,
                    CollectionExpressionSyntax collection => collection.Elements.OfType<ExpressionElementSyntax>().Select(x => x.Expression),
                    _ => []
                };
                foreach (var item in items) yield return item;
            }
        }
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var name = invocation.Expression is MemberAccessExpressionSyntax member ? member.Name.Identifier.ValueText : invocation.Expression.ToString();
            if (name == "Add" && invocation.Expression.ToString().EndsWith(".Items.Add", StringComparison.Ordinal)
                && invocation.ArgumentList.Arguments.Count == 1) yield return invocation.ArgumentList.Arguments[0].Expression;
            if (!calls.TryGetValue(name, out var indices)) continue;
            if (name is "Text" or "Format" && !invocation.Expression.ToString().StartsWith("UiLocalization.", StringComparison.Ordinal)) continue;
            foreach (var index in indices)
                if (index < invocation.ArgumentList.Arguments.Count) yield return invocation.ArgumentList.Arguments[index].Expression;
        }
    }

    public static string? Literal(ExpressionSyntax expression, IReadOnlyDictionary<string, string>? constants = null)
    {
        var index = 0;
        return LiteralPart(expression, constants, ref index);
    }

    private static string? LiteralPart(ExpressionSyntax expression, IReadOnlyDictionary<string, string>? constants, ref int index)
    {
        if (expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression)) return literal.Token.ValueText;
        if (expression is BinaryExpressionSyntax binary && binary.IsKind(SyntaxKind.AddExpression)
            && LiteralPart(binary.Left, constants, ref index) is { } left && LiteralPart(binary.Right, constants, ref index) is { } right) return left + right;
        if (constants is not null && constants.TryGetValue(expression.ToString().Split('.').Last(), out var constant)) return constant;
        if (expression is not InterpolatedStringExpressionSyntax interpolation) return null;
        var result = new System.Text.StringBuilder();
        foreach (var part in interpolation.Contents)
            result.Append(part is InterpolatedStringTextSyntax text ? text.TextToken.ValueText : "{" + index++ + "}");
        return result.ToString();
    }

    public static IReadOnlyList<TranslationEntry> ReadSeeds(IEnumerable<(string Path, string Text)> files)
    {
        var parsed = files.Select(x => (x.Path, Root: CSharpSyntaxTree.ParseText(x.Text).GetRoot())).ToArray();
        var constants = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (_, root) in parsed)
            foreach (var field in root.DescendantNodes().OfType<FieldDeclarationSyntax>().Where(x => x.Modifiers.Any(SyntaxKind.ConstKeyword)))
                foreach (var variable in field.Declaration.Variables)
                    if (variable.Initializer is { } initializer && Literal(initializer.Value, constants) is { } value)
                        constants[variable.Identifier.ValueText] = value;
        var result = new List<TranslationEntry>();
        var methods = parsed.Where(x => Path.GetFileName(x.Path).StartsWith("UiLocalization", StringComparison.Ordinal))
            .SelectMany(x => x.Root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            .Where(x => x.Identifier.ValueText == "BuildTexts" || x.Identifier.ValueText.StartsWith("Add", StringComparison.Ordinal))
            .ToDictionary(x => x.Identifier.ValueText, StringComparer.Ordinal);
        var review = parsed.Where(x => Path.GetFileName(x.Path) == "DesignReviewLabels.cs")
            .SelectMany(x => x.Root.DescendantNodes().OfType<TupleExpressionSyntax>()).Where(x => x.Arguments.Count == 3)
            .Select(tuple => tuple.Arguments.Select(x => Literal(x.Expression, constants)).ToArray())
            .Where(values => values.All(x => x is not null)).Select(values => new TranslationEntry(values[0]!, values[0]!, values[1]!, values[2]!)).ToArray();
        void Visit(MethodDeclarationSyntax method)
        {
                foreach (var call in method.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    if (methods.TryGetValue(call.Expression.ToString(), out var nested)) { Visit(nested); continue; }
                    if (call.Expression.ToString() is not ("Add" or "add") || call.ArgumentList.Arguments.Count != 3) continue;
                    var values = call.ArgumentList.Arguments.Select(x => Literal(x.Expression, constants)).ToArray();
                    if (values.All(x => x is not null)) result.Add(new(values[0]!, values[0]!, values[1]!, values[2]!));
                    else if (call.ToString().Contains("item.Zh", StringComparison.Ordinal)) result.AddRange(review);
                    else throw new InvalidDataException($"Unresolved translation seed: {call}");
                }
        }
        if (methods.TryGetValue("BuildTexts", out var entryPoint)) Visit(entryPoint);
        return result;
    }
}

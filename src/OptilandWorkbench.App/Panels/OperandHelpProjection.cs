using OptilandWorkbench.Application.Contracts;

namespace OptilandWorkbench.App.Panels;

internal enum OperandHelpSupportFilter
{
    All,
    Executable,
    CompatibilityOnly
}

internal enum OperandHelpLevel
{
    Category,
    Family,
    Operand
}

internal sealed record OperandHelpNode(
    string Key,
    string Title,
    string Path,
    OperandHelpLevel Level,
    IReadOnlyList<OperandHelpNode> Children,
    IReadOnlyList<MeritOperandTypeDto> Operands,
    MeritOperandTypeDto? Operand = null,
    string Summary = "");

internal static class OperandHelpProjection
{
    internal static IReadOnlyList<OperandHelpNode> BuildTree(IReadOnlyList<MeritOperandTypeDto> operands)
    {
        var families = operands.GroupBy(operand => OperandHelpTaxonomy.FamilyFor(operand.Code))
            .OrderBy(group => OperandHelpTaxonomy.Families.ToList().FindIndex(family => family.Id == group.Key.Id));
        return families.GroupBy(group => group.Key.Category).Select(category =>
        {
            var children = category.Select(group =>
            {
                var family = group.Key;
                var path = $"{family.Category} › {family.Name}";
                var items = group.OrderBy(operand => operand.Code, StringComparer.Ordinal).ToArray();
                var leaves = items.Select(operand => new OperandHelpNode(
                    operand.Code, $"{operand.Code} · {OperandHelpTaxonomy.DisplayName(operand)}",
                    path, OperandHelpLevel.Operand, [], [operand], operand)).ToArray();
                return new OperandHelpNode(family.Id, family.Name, path, OperandHelpLevel.Family,
                    leaves, items, Summary: family.Summary);
            }).ToArray();
            return new OperandHelpNode($"category/{category.Key}", category.Key, category.Key,
                OperandHelpLevel.Category, children, children.SelectMany(child => child.Operands).ToArray());
        }).ToArray();
    }

    internal static IReadOnlyList<MeritOperandTypeDto> Filter(
        IEnumerable<MeritOperandTypeDto> source,
        string? query,
        OperandHelpSupportFilter supportFilter)
    {
        ArgumentNullException.ThrowIfNull(source);
        var terms = (query ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return source
            .Where(operand => supportFilter switch
            {
                OperandHelpSupportFilter.Executable => !operand.CompatibilityOnly,
                OperandHelpSupportFilter.CompatibilityOnly => operand.CompatibilityOnly,
                _ => true
            })
            .Where(operand => terms.All(term => SearchText(operand)
                .Contains(term, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(operand => operand.Code, StringComparer.Ordinal)
            .ToArray();
    }

    private static string SearchText(MeritOperandTypeDto operand)
    {
        var parameters = operand.Parameters is null
            ? string.Empty
            : string.Join(' ', operand.Parameters.Select(parameter =>
                $"{parameter.Slot} {parameter.DisplayName} {parameter.ValueKind} {parameter.Unit}"));
        return string.Join(' ',
            OperandHelpTaxonomy.FamilyFor(operand.Code).Category,
            OperandHelpTaxonomy.FamilyFor(operand.Code).Name,
            OperandHelpTaxonomy.DisplayName(operand),
            operand.Code,
            operand.DisplayName,
            operand.Description,
            operand.Category,
            operand.Calculation,
            parameters);
    }
}

using OptilandWorkbench.Application.Contracts;

namespace OptilandWorkbench.App.Panels;

internal enum OperandHelpSupportFilter
{
    All,
    Executable,
    CompatibilityOnly
}

internal static class OperandHelpProjection
{
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
            operand.Code,
            operand.DisplayName,
            operand.Description,
            operand.Category,
            operand.Calculation,
            parameters);
    }
}

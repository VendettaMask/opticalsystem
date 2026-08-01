namespace OptilandWorkbench.App.Services;

internal sealed record GuiAnalysisCaptureRequest(
    string SourcePath,
    string SettingsManifestPath,
    string OutputDirectory,
    int StartIndex,
    int EndIndex)
{
    private const string ModeFlag = "--capture-analysis-gui";

    public static GuiAnalysisCaptureRequest? Parse(IEnumerable<string> arguments)
    {
        var values = arguments.ToArray();
        if (!values.Contains(ModeFlag, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        var source = RequiredValue(values, "--source=");
        var settings = RequiredValue(values, "--settings-manifest=");
        var output = RequiredValue(values, "--output=");
        var start = OptionalInteger(values, "--start=", 1);
        var end = OptionalInteger(values, "--end=", int.MaxValue);
        if (start < 1 || end < start)
        {
            throw new ArgumentException("GUI capture requires 1 <= start <= end.");
        }

        return new GuiAnalysisCaptureRequest(
            Path.GetFullPath(source),
            Path.GetFullPath(settings),
            Path.GetFullPath(output),
            start,
            end);
    }

    private static string RequiredValue(IReadOnlyList<string> arguments, string prefix)
    {
        var value = arguments.FirstOrDefault(argument =>
            argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (value is null || string.IsNullOrWhiteSpace(value[prefix.Length..]))
        {
            throw new ArgumentException($"GUI capture requires {prefix}<path>.");
        }

        return value[prefix.Length..];
    }

    private static int OptionalInteger(
        IReadOnlyList<string> arguments,
        string prefix,
        int fallback)
    {
        var value = arguments.FirstOrDefault(argument =>
            argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (value is null)
        {
            return fallback;
        }

        if (!int.TryParse(
                value[prefix.Length..],
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsed))
        {
            throw new ArgumentException($"GUI capture requires an integer for {prefix}.");
        }

        return parsed;
    }
}

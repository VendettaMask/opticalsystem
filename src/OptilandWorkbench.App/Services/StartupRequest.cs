namespace OptilandWorkbench.App.Services;

internal sealed record StartupRequest(string? Sample, string? DocumentPath)
{
    public static StartupRequest Parse(IEnumerable<string> arguments)
    {
        string? sample = null;
        string? documentPath = null;

        foreach (var argument in arguments)
        {
            if (argument.StartsWith("--sample=", StringComparison.OrdinalIgnoreCase))
            {
                sample = argument.Split('=', 2)[1];
                continue;
            }

            if (documentPath is null
                && !argument.StartsWith('-')
                && string.Equals(
                    Path.GetExtension(argument),
                    ".staropt",
                    StringComparison.OrdinalIgnoreCase))
            {
                documentPath = Path.GetFullPath(argument);
            }
        }

        return new StartupRequest(sample, documentPath);
    }
}

using System.Globalization;

namespace OptilandWorkbench.ZemaxComparison;

public sealed record ComparisonOptions
{
    public string Input { get; init; } = "";
    public string? Output { get; init; }
    public string Config { get; init; } = Path.Combine(AppContext.BaseDirectory, "comparison-settings.json");
    public string Configuration { get; init; } = "1";
    public string? ZemaxVersion { get; init; }
    public string? ZosApiPath { get; init; }
    public string FailOn { get; init; } = "difference";
    public string ReportLanguage { get; init; } = "zh-CN";
    public int Timeout { get; init; } = 120;
    public bool Overwrite { get; init; }
    public bool ListAnalyses { get; init; }
    public bool CaptureScreenshots { get; init; }
    public bool KeepRaw { get; init; }
    public List<string> Analyses { get; init; } = [];

    public static ComparisonOptions Parse(string[] args)
    {
        var o = new ComparisonOptions();
        var all = false;
        for (var i = 0; i < args.Length; i++)
        {
            string Value() => ++i < args.Length && !args[i].StartsWith("--", StringComparison.Ordinal)
                ? args[i] : throw new ArgumentException($"Missing value for {args[i - 1]}");
            o = args[i] switch
            {
                "--input" => o with { Input = Value() },
                "--output" => o with { Output = Value() },
                "--config" => o with { Config = Value() },
                "--configuration" => o with { Configuration = Value() },
                "--zemax-version" => o with { ZemaxVersion = Value() },
                "--zos-api-path" => o with { ZosApiPath = Value() },
                "--fail-on" => o with { FailOn = Value() },
                "--report-language" => o with { ReportLanguage = Value() },
                "--timeout" => o with { Timeout = ParseTimeout(Value()) },
                "--overwrite" => o with { Overwrite = true },
                "--list-analyses" => o with { ListAnalyses = true },
                "--capture-screenshots" => o with { CaptureScreenshots = true },
                "--keep-raw" => o with { KeepRaw = true },
                "--analysis" => AddAnalysis(o, Value()),
                "--all" => All(o),
                _ => throw new ArgumentException($"Unknown argument: {args[i]}")
            };
        }
        if (!o.ListAnalyses && string.IsNullOrWhiteSpace(o.Input)) throw new ArgumentException("--input is required");
        if (all && o.Analyses.Count != 0) throw new ArgumentException("--all and --analysis are mutually exclusive");
        if (o.Timeout <= 0 || o.Timeout > 86400) throw new ArgumentException("--timeout must be 1..86400 seconds");
        if (o.Configuration != "all" && (!int.TryParse(o.Configuration, out var n) || n < 1))
            throw new ArgumentException("--configuration must be a positive one-based index or all");
        if (o.FailOn is not ("none" or "error" or "difference")) throw new ArgumentException("Invalid --fail-on");
        if (o.ReportLanguage is not ("zh-CN" or "en-US")) throw new ArgumentException("Invalid --report-language");
        return o;
        ComparisonOptions All(ComparisonOptions v) { all = true; return v; }
    }

    private static ComparisonOptions AddAnalysis(ComparisonOptions o, string key)
    {
        if (!o.Analyses.Contains(key, StringComparer.Ordinal)) o.Analyses.Add(key);
        return o;
    }
    private static int ParseTimeout(string value) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
        ? number : throw new ArgumentException("--timeout must be an integer in 1..86400 seconds");
}

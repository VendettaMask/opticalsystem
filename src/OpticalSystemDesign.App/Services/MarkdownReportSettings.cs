namespace OpticalSystemDesign.App.Services;

public enum DesignReportFormat { Markdown, Word, Pdf, Html }

public sealed class MarkdownReportSettings
{
    public static readonly string[] CommonAnalyses =
        ["Spot Diagram", "Ray Fan", "MTF", "Field Curvature and Distortion"];

    public List<string> AnalysisKeys { get; set; } = [.. CommonAnalyses];
    public Dictionary<string, Dictionary<string, string>> AnalysisParameters { get; set; } = new();
    public bool Include2DLayout { get; set; } = true;
    public bool Include3DLayout { get; set; } = true;
    public bool AllConfigurations { get; set; }
    public DesignReportFormat Format { get; set; } = DesignReportFormat.Markdown;

    public MarkdownReportSettings Copy() => new()
    {
        AnalysisKeys = [.. AnalysisKeys ?? []],
        AnalysisParameters = (AnalysisParameters ?? new()).Where(pair => pair.Value is not null).ToDictionary(pair => pair.Key,
            pair => new Dictionary<string, string>(pair.Value)),
        Include2DLayout = Include2DLayout,
        Include3DLayout = Include3DLayout,
        AllConfigurations = AllConfigurations,
        Format = Format
    };
}

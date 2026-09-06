namespace OptilandWorkbench.ZemaxComparison.Configuration;

public sealed record AnalysisConfiguration
{
    public int Field { get; init; } = 1;
    public int Wavelength { get; init; } = 1;
    public int PupilSampling { get; init; } = 64;
    public int ImageSampling { get; init; } = 64;
    public int RayCount { get; init; } = 20;
    public double ImageDeltaMicrometers { get; init; } = 0.25;
    public double MaximumFrequency { get; init; } = 50;
    public Dictionary<string, Tolerances> Quantities { get; init; } = [];
    public Dictionary<string, string> WorkbenchSettings { get; init; } = [];
}
public sealed record ComparisonConfiguration
{
    public string Version { get; init; } = "1";
    public string ZemaxVersion { get; init; } = "2026 R1";
    public Dictionary<string, AnalysisConfiguration> Analyses { get; init; } = [];
    public static ComparisonConfiguration Load(string path) => System.Text.Json.JsonSerializer.Deserialize<ComparisonConfiguration>(File.ReadAllText(path),
        new System.Text.Json.JsonSerializerOptions(JsonFiles.Options) { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow })
        ?? throw new ArgumentException("Invalid comparison configuration");
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Version) || string.IsNullOrWhiteSpace(ZemaxVersion)) throw new ArgumentException("Missing configuration version");
        if (Analyses is null) throw new ArgumentException("Analyses cannot be null");
        foreach (var (key, a) in Analyses)
        {
            var entry = AnalysisComparisonRegistry.Get(key);
            if (a is null || a.Quantities is null || a.WorkbenchSettings is null)
                throw new ArgumentException($"Null analysis settings: {key}");
            if (a.Field < 1 || a.Wavelength < 1 || a.RayCount < 1 || a.RayCount > 500
                || a.PupilSampling is not (32 or 64 or 128 or 256) || a.ImageSampling is not (32 or 64 or 128 or 256)
                || !double.IsFinite(a.ImageDeltaMicrometers) || a.ImageDeltaMicrometers <= 0
                || !double.IsFinite(a.MaximumFrequency) || a.MaximumFrequency <= 0)
                throw new ArgumentException($"Invalid explicit settings: {key}");
            if (entry.ZemaxSettingsMapper == "spot" && a.RayCount > 32)
                throw new ArgumentException("Spot RayCount is the hexapolar ring density and must be <= 32 on both sides");
            if (entry.ZemaxSettingsMapper != "unimplemented" && a.WorkbenchSettings.Count != 0)
                throw new ArgumentException($"{key}: comparable adapters accept canonical typed settings only; unmatched Workbench-only overrides would invalidate alignment");
            foreach (var (quantity, t) in a.Quantities)
            {
                if (t is null || new[] { t.Absolute, t.Relative, t.Nrmse, t.CloseNrmse, t.MinimumCoverage }.Any(v => !double.IsFinite(v))
                    || t.Absolute < 0 || t.Relative < 0 || t.Nrmse < 0 || t.CloseNrmse < t.Nrmse
                    || t.MinimumCoverage <= 0 || t.MinimumCoverage > 1)
                    throw new ArgumentException($"Invalid tolerance {key}/{quantity}");
            }
        }
        foreach (var e in AnalysisComparisonRegistry.Entries.Where(e => e.Support is SupportStatus.Comparable or SupportStatus.PartiallyComparable))
            if (!Analyses.TryGetValue(e.CanonicalAnalysisKey, out var a)
                || !a.Quantities.Keys.Order(StringComparer.Ordinal).SequenceEqual(e.DefaultTolerances.Keys.Order(StringComparer.Ordinal)))
                throw new ArgumentException($"Explicit per-quantity tolerances required: {e.CanonicalAnalysisKey}");
    }
}

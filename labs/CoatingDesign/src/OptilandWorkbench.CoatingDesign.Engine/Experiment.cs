using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Serialization;

namespace OptilandWorkbench.CoatingDesign.Engine;

public enum DesignKind { Antireflection, HighReflector, NarrowBand }
public sealed record SpectralBand(double MinimumNm, double MaximumNm);
public sealed record DesignTarget(
    DesignKind Kind = DesignKind.Antireflection, double CenterNm = 550, double MinimumNm = 500,
    double MaximumNm = 600, double Reflectance = 0.02, int MaximumLayers = 8,
    double AngleDegrees = 0, ThinFilmPolarization Polarization = ThinFilmPolarization.Unpolarized,
    double FwhmNm = 15, double PeakTransmittance = 0.75, double BlockingOd = 1,
    SpectralBand[]? StopBands = null, double CenterToleranceNm = 0.5, double FwhmToleranceNm = 1);
public sealed record CalculationSettings(int PreviewIntervals = 400, int OptimizationIntervals = 120,
    int MaximumIterations = 80, string Optimizer = "Damped Least Squares",
    double MinimumThicknessNm = 2, double MaximumThicknessNm = 2000,
    double TolerancePercent = 1, int ToleranceTrials = 20, int RandomSeed = 1234);
public sealed record FilmLayer(string MaterialId, double ThicknessNm, bool Variable = true);

public sealed record MaterialSnapshot(string Id, string Name, string Source, double MinimumNm, double MaximumNm,
    ComponentSnapshot Model)
{
    public IMaterial Resolve(MaterialRegistry registry)
    {
        if (Model is null || Model.Numbers is null || Model.Text is null
            || Model.Kind is not ("air" or "constant" or "cauchy" or "sellmeier" or "catalog_glass" or "polynomial_dispersion")
            || Model.Numbers.Values.Any(v => !double.IsFinite(v)))
            throw new InvalidDataException($"材料 {Name} 缺少完整的可重算快照，不能用名称替代。");
        IMaterial material;
        try { material = ComponentSnapshotFactory.ToMaterial(Model, Name, registry); }
        catch (Exception error) when (error is ArgumentException or KeyNotFoundException or JsonException)
        { throw new InvalidDataException($"材料 {Name} 的快照字段缺失或无效。", error); }
        // The shared document reader has compatibility defaults. Laboratory snapshots must be complete:
        // reject any missing coefficient instead of silently accepting a reconstructed default value.
        var canonical = ComponentSnapshotFactory.FromMaterial(material);
        if (canonical.Kind != Model.Kind || canonical.Numbers.Count != Model.Numbers.Count
            || canonical.Numbers.Any(p => !Model.Numbers.TryGetValue(p.Key, out var value) || value != p.Value)
            || canonical.Text.Any(p => !Model.Text.TryGetValue(p.Key, out var value) || value != p.Value))
            throw new InvalidDataException($"材料 {Name} 的快照不完整，不能使用默认值补齐。");
        if (material is CatalogGlassMaterial c)
        {
            CheckTable(c.RefractiveIndexWavelengthsNanometers, c.RefractiveIndices);
            CheckTable(c.ExtinctionWavelengthsNanometers, c.ExtinctionCoefficients);
        }
        return material;
    }

    private static void CheckTable(IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        if (x.Count != y.Count || x.Where((v, i) => v <= 0 || (i > 0 && v <= x[i - 1])).Any())
            throw new InvalidDataException("材料表格必须按波长严格递增，且 n/k 数据长度必须一致。");
    }
}

public sealed record RequirementCheck(string Name, double? Actual, string Requirement, bool Passed);
public sealed record RunRecord(string Algorithm, string AlgorithmVersion, string StopReason, long Evaluations,
    int Iterations, int? RandomSeed, string SolverVersion = CoherentThinFilmSolver.Version);
public sealed record DesignResult(string Fingerprint, ThinFilmSample[] Spectrum, ThinFilmBandMetrics Metrics,
    RequirementCheck[] Checks, bool SamplingConverged, int VerificationSamples, RunRecord Run,
    double Merit, string[] Warnings)
{
    public bool Passed => SamplingConverged && Checks.All(x => x.Passed);
}
public sealed record Candidate(string Name, FilmLayer[] Layers, double Merit, bool Passed);
public sealed record Experiment(int SchemaVersion, Guid Id, string Name, DesignTarget Target,
    CalculationSettings Settings, string IncidentId, string SubstrateId, string LowId, string HighId,
    MaterialSnapshot[] Materials, FilmLayer[] Layers, DesignResult? Result = null, Candidate[]? Candidates = null,
    ToleranceResult? Tolerance = null)
{
    public const int CurrentSchema = 1;
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter() }
    };

    public Experiment Snapshot() => JsonSerializer.Deserialize<Experiment>(JsonSerializer.Serialize(this, JsonOptions), JsonOptions)!;
    public string Fingerprint() => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
        new { Target, Settings, IncidentId, SubstrateId, LowId, HighId, Materials, Layers }, JsonOptions)));

    public void Validate()
    {
        if (SchemaVersion != CurrentSchema) throw new InvalidDataException($"不支持实验格式版本 {SchemaVersion}。");
        if (Id == Guid.Empty || string.IsNullOrWhiteSpace(Name) || Target is null || Settings is null || Materials is null || Layers is null)
            throw new InvalidDataException("实验缺少名称、参数或材料快照。");
        var t = Target; var s = Settings;
        static bool Finite(params double[] values) => values.All(double.IsFinite);
        if (!Enum.IsDefined(t.Kind) || !Enum.IsDefined(t.Polarization) || !Finite(t.CenterNm, t.MinimumNm, t.MaximumNm, t.Reflectance,
            t.AngleDegrees, t.FwhmNm, t.PeakTransmittance, t.BlockingOd, t.CenterToleranceNm, t.FwhmToleranceNm)
            || t.MinimumNm <= 0 || t.MaximumNm <= t.MinimumNm || t.CenterNm < t.MinimumNm || t.CenterNm > t.MaximumNm
            || t.AngleDegrees < 0 || t.AngleDegrees >= 90 || t.Reflectance < 0 || t.Reflectance > 1
            || t.PeakTransmittance <= 0 || t.PeakTransmittance > 1 || t.FwhmNm <= 0 || t.BlockingOd < 0 || t.BlockingOd > 12
            || t.MaximumLayers is < 1 or > 200 || t.CenterToleranceNm <= 0 || t.FwhmToleranceNm <= 0)
            throw new ArgumentException("请检查目标：波长范围和带宽须为正；角度须小于 90°；R/T 为 0–100%；层数 1–200；OD 为 0–12。");
        if (t.Kind == DesignKind.NarrowBand && (t.StopBands is not { Length: > 0 and <= 8 }
            || t.CenterNm - t.FwhmNm / 2 <= t.MinimumNm || t.CenterNm + t.FwhmNm / 2 >= t.MaximumNm))
            throw new ArgumentException("窄带设计需要截止波段，计算范围须包含通带两侧。");
        foreach (var band in t.StopBands ?? [])
            if (!Finite(band.MinimumNm, band.MaximumNm) || band.MinimumNm < t.MinimumNm || band.MaximumNm > t.MaximumNm
                || band.MaximumNm <= band.MinimumNm || (band.MinimumNm < t.CenterNm + t.FwhmNm / 2 && band.MaximumNm > t.CenterNm - t.FwhmNm / 2))
                throw new ArgumentException("截止波段须在计算范围内，且不能与目标半高通带重叠。");
        if (s.PreviewIntervals is < 40 or > 10000 || s.OptimizationIntervals is < 20 or > 2000
            || s.MaximumIterations is < 1 or > 1000 || s.ToleranceTrials is < 1 or > 200
            || !Finite(s.MinimumThicknessNm, s.MaximumThicknessNm, s.TolerancePercent)
            || s.MinimumThicknessNm <= 0 || s.MaximumThicknessNm < s.MinimumThicknessNm || s.TolerancePercent < 0 || s.TolerancePercent > 20
            || s.Optimizer is not ("Damped Least Squares" or "Nelder-Mead" or "Coordinate Pattern Search"))
            throw new ArgumentException("高级设置无效：请检查采样、迭代次数、算法和厚度上下限。");
        if (Materials.Length is < 1 or > 500 || Materials.Select(m => m.Id).Distinct().Count() != Materials.Length)
            throw new InvalidDataException("材料快照为空、重复或超过数量上限。");
        var ids = Materials.Select(m => m.Id).ToHashSet();
        if (new[] { IncidentId, SubstrateId, LowId, HighId }.Any(id => !ids.Contains(id)) || Layers.Any(l => !ids.Contains(l.MaterialId)))
            throw new InvalidDataException("膜层或基板引用了缺失的材料。");
        if (Layers.Length > 200 || Layers.Any(l => !double.IsFinite(l.ThicknessNm) || l.ThicknessNm <= 0 || l.ThicknessNm > 100000))
            throw new ArgumentException("膜层厚度必须大于零且不超过 100000 nm，膜层不能超过 200 层。");
        var registry = new MaterialRegistry();
        foreach (var m in Materials)
        {
            if (!Finite(m.MinimumNm, m.MaximumNm) || m.MinimumNm <= 0 || m.MaximumNm <= m.MinimumNm)
                throw new InvalidDataException($"材料 {m.Name} 的有效波段无效。");
            m.Resolve(registry);
        }
    }
}

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Serialization;

namespace OpticalSystemDesign.CoatingDesign.Engine;

public enum DesignKind { Antireflection, HighReflector, NarrowBand }
public sealed record StructureOptions(int Cavities = 1)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? PrototypeFamilyVersion { get; init; }
}
public sealed record SearchOptions(int Starts = 6, double JitterPercent = 12, bool VaryStructure = true);
public enum ToleranceDistribution { Uniform, Normal }
public enum ThicknessCorrelation { Independent, SameMaterial, Common }
public sealed record ToleranceOptions(ToleranceDistribution Distribution = ToleranceDistribution.Uniform,
    ThicknessCorrelation Correlation = ThicknessCorrelation.Independent, double AngleDegrees = 0);
public sealed record SpectralBand(double MinimumNm, double MaximumNm);
public sealed record DesignTarget(
    DesignKind Kind = DesignKind.Antireflection, double CenterNm = 550, double MinimumNm = 500,
    double MaximumNm = 600, double Reflectance = 0.02, int MaximumLayers = 8,
    double AngleDegrees = 0, ThinFilmPolarization Polarization = ThinFilmPolarization.Unpolarized,
    double FwhmNm = 15, double PeakTransmittance = 0.75, double BlockingOd = 1,
    SpectralBand[]? StopBands = null, double CenterToleranceNm = 0.5, double FwhmToleranceNm = 1,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] StructureOptions? Structure = null);
public sealed record CalculationSettings(int PreviewIntervals = 400, int OptimizationIntervals = 120,
    int MaximumIterations = 80, string Optimizer = "Damped Least Squares",
    double MinimumThicknessNm = 2, double MaximumThicknessNm = 2000,
    double TolerancePercent = 1, int ToleranceTrials = 20, int RandomSeed = 1234,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SearchOptions? Search = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ToleranceOptions? Tolerancing = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? DesignProtocolVersion { get; init; }
}
public sealed record FilmLayer(string MaterialId, double ThicknessNm, bool Variable = true);
public sealed record CoatingStackGeometry(string ExitMediumId, double SubstrateThicknessMm = 1, bool IncludeBackSurface = true)
{
    public int Version { get; init; } = 1;
}
public enum CoatingDesignSurface { Front, Back, Both }
public enum CoatingEvaluationSurface { Front, Back, WholePlate }
public sealed record DuplexCoatingOptions(CoatingDesignSurface Optimize = CoatingDesignSurface.Front,
    CoatingEvaluationSurface Evaluate = CoatingEvaluationSurface.WholePlate, bool Symmetric = false)
{
    public int Version { get; init; } = 1;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? OptimizationVersion { get; init; }
}

public sealed record MaterialSnapshot(string Id, string Name, string Source, double MinimumNm, double MaximumNm,
    ComponentSnapshot Model)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool HoldTabulatedEndpoints { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplayColor { get; init; }
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
        if (HoldTabulatedEndpoints && (material is not CatalogGlassMaterial table
            || table.Formula != CatalogGlassMaterial.PchipTableFormula))
            throw new InvalidDataException("端点延用必须由显式 PCHIP 材料模型声明，不能用于替代未知色散。");
        return material;
    }

    public string? RangeNotice(double minimumNm, double maximumNm) => HoldTabulatedEndpoints
        && (minimumNm < MinimumNm || maximumNm > MaximumNm)
        ? $"{Name}：原数据 {MinimumNm:G7}–{MaximumNm:G7} nm，域外按参考模型延用端点（非实测）" : null;

    private static void CheckTable(IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        if (x.Count != y.Count || x.Where((v, i) => v <= 0 || (i > 0 && v <= x[i - 1])).Any())
            throw new InvalidDataException("材料表格必须按波长严格递增，且 n/k 数据长度必须一致。");
    }
}

public sealed record RequirementCheck(string Name, double? Actual, string Requirement, bool Passed);
public sealed record TargetRepairTrial(int Step, FilmLayer[] Layers, ThinFilmBandMetrics Metrics, double[] Violations, bool SamplingConverged, bool Retained)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? GoalIndex { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Stage { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double[]? ViolationCeilings { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? RestorationViolation { get; init; }
}
public sealed record TargetDirectionTrial(string Direction, FilmLayer[] Layers, ThinFilmBandMetrics Metrics, double Merit,
    bool Passed, int Iterations, long Evaluations, string StopReason, bool Selected);
public sealed record TargetRepairWindow(int PreviousLimit, int NewLimit, int GoalIndex, double BestMerit, double[] ViolationCeilings, string Reason);
public sealed record PassbandQualityBounds(double MinimumPeakTransmittance, double MaximumWidthErrorNm,
    double[] MinimumOpticalDensities, double[] ViolationCeilings);
public sealed record PassbandQualityTrial(int Step, FilmLayer[] Layers, ThinFilmBandMetrics Metrics,
    double[] OpticalDensities, bool SamplingConverged, bool Retained, PassbandQualityBounds Bounds);
public sealed record PassbandCorrectionAttempt(int Step, int Attempt, double Fraction, double? GuidedExcess,
    double? Merit, double BaselineMerit, bool Corrected);
public sealed record PassbandDirectionTrial(string Direction, FilmLayer[] StartLayers, FilmLayer[] Layers,
    ThinFilmBandMetrics Metrics, double[] OpticalDensities, bool SamplingConverged, bool Passed,
    int Allowance, int Iterations, long Evaluations, bool Selected, PassbandQualityTrial[] Trials,
    PassbandCorrectionAttempt[]? CorrectionAttempts);
public sealed record ReflectanceMetricWindow(int IterationOffset, int Allowance, FilmLayer[] StartLayers, FilmLayer[] CandidateLayers,
    double BaselineLoss, double CandidateLoss, bool SamplingConverged, bool Accepted, double DampingStart, double DampingEnd);
public sealed record SearchSeedValidation(FilmLayer[] Layers, ThinFilmBandMetrics Metrics, double[] OpticalDensities,
    bool SamplingConverged, bool Passed, double[] Violations)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NarrowBandPrototype? Prototype { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FilmLayer[]? InitialLayers { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RunRecord? RefinementRun { get; init; }
}
public sealed record RunRecord(string Algorithm, string AlgorithmVersion, string StopReason, long Evaluations,
    int Iterations, int? RandomSeed, string SolverVersion = CoherentThinFilmSolver.Version)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DuplexOptimizationRecord? Duplex { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FilmLayer[]? QualityStartLayers { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double[]? TargetViolationCeilings { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TargetEnvelopeSampling { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TargetProtectionPhase { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FilmLayer[]? TargetRepairStartLayers { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double[]? TargetRepairViolationCeilings { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TargetRepairGoalIndex { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TargetRepairTrial[]? TargetRepairTrials { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TargetDirectionTrial[]? TargetDirectionTrials { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TargetRepairBaselineDirection { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TargetRepairStopReason { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FilmLayer[]? TargetRepairContinuationStartLayers { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double[]? TargetRepairContinuationCeilings { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TargetParetoStopReason { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TargetRepairWindow[]? TargetRepairWindows { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TargetRefinementIterationLimit { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FilmLayer[]? ReflectanceMetricStartLayers { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FilmLayer[]? ReflectanceMetricResultLayers { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ReflectanceMetricAccepted { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ReflectanceMetricWindow[]? ReflectanceMetricWindows { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ReflectanceMetricIterationLimit { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReflectanceMetricStopReason { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FilmLayer[]? PassbandQualityStartLayers { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PassbandQualityTrial[]? PassbandQualityTrials { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? PassbandQualityIterationLimit { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PassbandQualityStopReason { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FilmLayer[]? PassbandCorrectionStartLayers { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? PassbandCorrectionIterationLimit { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PassbandCorrectionStopReason { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PassbandCorrectionAttempt[]? PassbandCorrectionAttempts { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PassbandDirectionTrial[]? PassbandDirectionTrials { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PassbandQualityBounds? PassbandDirectionSelectionBounds { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SeedRankingProtocol { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SearchSeedValidation[]? SearchSeedValidations { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DesignProtocol { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ScreeningRun[]? Screening { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? TemplateEvaluations { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NarrowBandCompetitionRecord? NarrowBandCompetition { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GeneralCoatingWork? GeneralWork { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public StructureFeedbackRecord? StructureFeedback { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public StructureFeedbackRecord[]? StructureFeedbackWindows { get; init; }
}
public sealed record ScreeningRun(int Number, FilmLayer[] InitialLayers, FilmLayer[] Layers, double Merit, RunRecord Run)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NarrowBandPrototype? OriginPrototype { get; init; }
}
public sealed record DesignResult(string Fingerprint, ThinFilmSample[] Spectrum, ThinFilmBandMetrics Metrics,
    RequirementCheck[] Checks, bool SamplingConverged, int VerificationSamples, RunRecord Run,
    double Merit, string[] Warnings)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SpectrumMetricsVersion { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SamplingProtocol { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ExtremaRefinementEvaluations { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ExtremaRefinementComplete { get; init; }
    public bool Passed => SamplingConverged && Checks.All(x => x.Passed);
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MaterialSuitabilityReport? MaterialSuitability { get; init; }
}
public sealed record Candidate(string Name, FilmLayer[] Layers, double Merit, bool Passed,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RunRecord? Run = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FilmLayer[]? BackLayers { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NarrowBandPrototype? OriginPrototype { get; init; }
}
public sealed record Experiment(int SchemaVersion, Guid Id, string Name, DesignTarget Target,
    CalculationSettings Settings, string IncidentId, string SubstrateId, string LowId, string HighId,
    MaterialSnapshot[] Materials, FilmLayer[] Layers, DesignResult? Result = null, Candidate[]? Candidates = null,
    ToleranceResult? Tolerance = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ReferenceExampleInfo? ReferenceExample { get; init; }
    // Display-system geometry is separate from the established front-surface design acceptance.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CoatingStackGeometry? StackGeometry { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FilmLayer[]? BackLayers { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DuplexCoatingOptions? Duplex { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? ReferenceWavelengthNm { get; init; }
    [JsonIgnore]
    public double EffectiveReferenceWavelengthNm => ReferenceWavelengthNm ?? Target.CenterNm;
    [JsonIgnore]
    public FilmLayer[] EffectiveBackLayers => Duplex?.Symmetric == true ? Layers : BackLayers ?? [];
    public const int CurrentSchema = 1;
    public const int DuplexSchema = 2;
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter() }
    };

    public Experiment Snapshot() => JsonSerializer.Deserialize<Experiment>(JsonSerializer.Serialize(this, JsonOptions), JsonOptions)!;
    public string Fingerprint() => Fingerprint(JsonOptions);

    internal bool MatchesFingerprint(string fingerprint) => fingerprint == Fingerprint()
        || fingerprint == Fingerprint(new JsonSerializerOptions(JsonOptions) { NewLine = "\r\n" });

    private string Fingerprint(JsonSerializerOptions options) => Convert.ToHexString(SHA256.HashData(Duplex is null
        ? JsonSerializer.SerializeToUtf8Bytes(new { Target, Settings, IncidentId, SubstrateId, LowId, HighId, Materials, Layers }, options)
        : JsonSerializer.SerializeToUtf8Bytes(new { Target, Settings, IncidentId, SubstrateId, LowId, HighId, Materials, Layers, BackLayers, Duplex, StackGeometry }, options)));

    public void Validate()
    {
        if (SchemaVersion is not (CurrentSchema or DuplexSchema)) throw new InvalidDataException($"不支持实验格式版本 {SchemaVersion}。");
        if (Duplex is null ? BackLayers is not null || SchemaVersion == DuplexSchema
            : SchemaVersion != DuplexSchema || Duplex.Version != 1 || Duplex.OptimizationVersion is not (null or 2) || StackGeometry is null
                || !Enum.IsDefined(Duplex.Optimize) || !Enum.IsDefined(Duplex.Evaluate)
                || Duplex.Optimize == CoatingDesignSurface.Both && Duplex.Evaluate != CoatingEvaluationSurface.WholePlate
                || Duplex.Optimize == CoatingDesignSurface.Front && Duplex.Evaluate == CoatingEvaluationSurface.Back
                || Duplex.Optimize == CoatingDesignSurface.Back && Duplex.Evaluate == CoatingEvaluationSurface.Front
                || Duplex.Symmetric && (Duplex.Optimize != CoatingDesignSurface.Both || BackLayers is not null))
            throw new InvalidDataException("双面设计版本、优化/评价对象或对称链接无效；旧单面实验保持原验收语义。");
        if (Id == Guid.Empty || string.IsNullOrWhiteSpace(Name) || Target is null || Settings is null || Materials is null || Layers is null)
            throw new InvalidDataException("实验缺少名称、参数或材料快照。");
        if (ReferenceWavelengthNm is { } reference && (!double.IsFinite(reference) || reference <= 0))
            throw new InvalidDataException("参考波长λ₀必须为正有限数值。");
        if (Materials.Any(m => m is null) || Layers.Any(l => l is null)
            || Target.StopBands?.Any(b => b is null) == true)
            throw new InvalidDataException("材料、膜层或截止波段不能包含空元素。");
        var t = Target; var s = Settings;
        if (s.DesignProtocolVersion is not (null or 2 or 3 or 4)
            || s.DesignProtocolVersion == 3 && t.Kind != DesignKind.NarrowBand)
            throw new ArgumentException("不支持的镀膜设计协议版本；协议 3 仅用于窄带结构竞争。");
        if (t.Structure?.PrototypeFamilyVersion is not (null or 2)
            || t.Structure?.PrototypeFamilyVersion == 2 && s.DesignProtocolVersion != 2)
            throw new ArgumentException("扩展镜层与腔阶提议需要显式的结构版本 2 和设计协议 2。");
        var search = s.Search ?? new(); var tolerance = s.Tolerancing ?? new();
        if ((t.Structure?.Cavities ?? 1) is < 1 or > 5 || search.Starts is < 1 or > 32
            || !double.IsFinite(search.JitterPercent) || search.JitterPercent is < 0 or > 50
            || !Enum.IsDefined(tolerance.Distribution) || !Enum.IsDefined(tolerance.Correlation)
            || !double.IsFinite(tolerance.AngleDegrees) || tolerance.AngleDegrees is < 0 or > 10)
            throw new ArgumentException("腔数须为 1–5，搜索起点须为 1–32，搜索扰动为 0–50%，角度公差为 0–10°。");
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
        if (StackGeometry is { } stack && (stack.Version != 1 || !ids.Contains(stack.ExitMediumId)
            || !double.IsFinite(stack.SubstrateThicknessMm) || stack.SubstrateThicknessMm <= 0
            || !double.IsFinite(stack.SubstrateThicknessMm * 1e6)))
            throw new InvalidDataException("堆栈出射介质缺失，或基板厚度不是正有限数值。");
        if (new[] { IncidentId, SubstrateId, LowId, HighId }.Any(id => !ids.Contains(id)) || Layers.Any(l => !ids.Contains(l.MaterialId)))
            throw new InvalidDataException("膜层或基板引用了缺失的材料。");
        if (Layers.Length > 200 || Layers.Any(l => !double.IsFinite(l.ThicknessNm) || l.ThicknessNm <= 0 || l.ThicknessNm > 100000))
            throw new ArgumentException("膜层厚度必须大于零且不超过 100000 nm，膜层不能超过 200 层。");
        if (BackLayers is { } rear && (rear.Length > 200 || rear.Any(l => l is null || !ids.Contains(l.MaterialId)
            || !double.IsFinite(l.ThicknessNm) || l.ThicknessNm <= 0 || l.ThicknessNm > 100000)))
            throw new ArgumentException("背面膜层引用、厚度或数量无效；每面最多200层。");
        var registry = new MaterialRegistry();
        foreach (var m in Materials)
        {
            if (!Finite(m.MinimumNm, m.MaximumNm) || m.MinimumNm <= 0 || m.MaximumNm <= m.MinimumNm)
                throw new InvalidDataException($"材料 {m.Name} 的有效波段无效。");
            m.Resolve(registry);
        }
    }
}

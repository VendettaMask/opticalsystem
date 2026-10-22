namespace OpticalSystemDesign.InitialStructure.Contracts;

/// <summary>Published targets, not imported upstream prescriptions or certified solutions.</summary>
public sealed record AutoLensExamplePreset(
    string Id, string Name, int OfficialExample, string Notes,
    InitialStructureSpecification Specification, FlatStartSearchOptions Options,
    IReadOnlyList<long> ValidationSeeds, double? SensorDiagonalMillimeters = null);

public static class AutoLensOfficialExamples
{
    public const string UpstreamCommit = "e58db32f3a0b07f25861b5839a45970f77b5c947";
    public const string SourceUrl = "https://github.com/AI4Optics/AutoLens/tree/" + UpstreamCommit + "#lens-design-examples";
    public const string ProtocolVersion = "autolens-official-targets-cpu-v1";
    public const int EvaluationsPerRun = 4000;
    public static readonly TimeSpan TimePerRun = TimeSpan.FromMinutes(1);

    // Fresh graphs prevent an editor or protocol consumer from mutating the catalog.
    public static IReadOnlyList<AutoLensExamplePreset> Create() =>
    [
        Preset("wide-80", "官方目标 1 · 广角 4.55 mm F/2", 1, 4.55, 2, 40, true, [42],
            "官方目标：4.55 mm、F/2、全视场 80°。片数、几何、玻璃、波长、像质门槛与 CPU 预算为本地适配；不是官方处方或成功保证。"),
        Preset("full-frame-50", "官方目标 2 · 全画幅 50 mm F/2", 2, 50, 2, 23.4126115889501, false, [42],
            "采用官方正文的 F/2，不采用动画替代文字中的 F/3。43.3 mm 对角线按理想无畸变关系换算半视场；实际像面覆盖尚非独立验收项。其余约束为本地适配。", 43.3),
        Preset("wide-80-seeds", "官方目标 3 · 广角 20 随机种子", 3, 4.55, 2, 40, true,
            Enumerable.Range(1, 20).Select(seed => (long)seed).ToArray(),
            "使用与目标 1 相同的光学指标。官方未公布图中 20 个种子的编号；本地协议使用 1–20。载入只设置首个种子，完整批量验证由专用验收工具执行。"),
        Preset("ultra-camera-f12", "官方目标 4A · F/1.2 相机（适配）", 4, 50, 1.2, 23.4126115889501, false, [42],
            "官方仅明确相机 F/1.2；本地补充 50 mm 和 43.3 mm 对角线，其余约束为适配。官方极端规格图未公布八组完整处方，不能宣称逐幅复现。", 43.3),
        Preset("ultra-mobile-120", "官方目标 4B · 120° 手机（适配）", 4, 4.55, 2, 60, true, [42],
            "官方仅明确手机全视场 120°；本地补充 4.55 mm、F/2 及几何材料约束。使用当前真实目录玻璃，而非假定已支持官方塑料材料或原始处方。")
    ];

    private static AutoLensExamplePreset Preset(string id, string name, int example, double focal,
        double fNumber, double halfField, bool compact, IReadOnlyList<long> seeds, string notes, double? diagonal = null)
    {
        var specification = new InitialStructureSpecification
        {
            Name = name,
            EffectiveFocalLengthMillimeters = focal,
            FNumber = fNumber,
            MaximumFieldAngleDegrees = halfField,
            MinimumElementCount = 6,
            MaximumElementCount = 6,
            MaximumTrackLengthMillimeters = compact ? 12 : 120,
            MinimumCenterThicknessMillimeters = compact ? .4 : 2,
            MinimumAirGapMillimeters = compact ? .1 : .5,
            MinimumBackFocusMillimeters = compact ? 1 : 10,
            MaximumRmsSpotRadiusMillimeters = compact ? .01 : .05,
            MaximumSpotRadiusMillimeters = compact ? .03 : .15,
            Wavelengths =
            [
                new() { Label = "B", Nanometers = 486.1, Weight = 1 },
                new() { Label = "G", Nanometers = 587.6, Weight = 1, IsPrimary = true },
                new() { Label = "R", Nanometers = 656.3, Weight = 1 }
            ],
            FlatStart = new()
            {
                SamplingPolicy = FlatStartSamplingPolicy.UniformAreaGaussianV1,
                OptimizationMethod = FlatStartOptimizationMethod.AutoLensManagedV1,
                UsePhysicalStop = true,
                AutomaticLensDiameters = true,
                MinimumEdgeThicknessMillimeters = compact ? .1 : .5,
                AutoLens = new() { EvenAsphereOrder = 5 }
            },
            Budget = new()
            {
                InitialSeedCount = 1,
                MaximumEvaluations = EvaluationsPerRun,
                MaximumParallelism = 1,
                RandomSeed = seeds[0],
                TimeLimit = TimePerRun
            }
        };
        return new(id, name, example, notes, specification, new()
        {
            AllowedGlassNames = ["N-BK7", "N-F2", "N-SF6"],
            MaximumEvaluationsPerTrial = EvaluationsPerRun,
            DesignSearch = new() { Mode = DesignSearchMode.Full, ExploreStopPositions = false, StopSurfaceIndex = compact ? 1 : 7 }
        }, seeds, diagonal);
    }
}

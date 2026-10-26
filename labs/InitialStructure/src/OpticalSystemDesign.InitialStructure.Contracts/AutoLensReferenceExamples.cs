using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Materials;

namespace OpticalSystemDesign.InitialStructure.Contracts;

public static class AutoLensReferenceExamples
{
    public static AutoLensExamplePreset Mobile() => new("upstream-e58-mobile", "原版脚本默认 · 5 mm F/1.8 全视场 90°", 0,
        "1_autolens.py 的 MOBILE_CONFIG：6 片、前置独立光阑、r⁴–r¹⁸、初始总长 10 mm、后焦 2 mm、种子 42；2001 + 3001 次 Adam 更新。材料来自原版固定目录。像质硬门槛和运行预算为本产品设置；本轮不比较最终输出效果。",
        new()
        {
            Name = "AutoLens e58 MOBILE_CONFIG",
            Conjugate = ObjectConjugateMode.Finite,
            EffectiveFocalLengthMillimeters = 5,
            FNumber = 1.8,
            MaximumFieldAngleDegrees = 45,
            MinimumElementCount = 6,
            MaximumElementCount = 6,
            MaximumTrackLengthMillimeters = 10,
            MinimumCenterThicknessMillimeters = .4,
            MinimumAirGapMillimeters = .05,
            MinimumBackFocusMillimeters = 2,
            MaximumRmsSpotRadiusMillimeters = .01,
            MaximumSpotRadiusMillimeters = .03,
            InitialGlass = AutoLensReferenceMaterials.ProjectionNames[5],
            Wavelengths = [new() { Label = "R", Nanometers = 656, Weight = 1 },
                new() { Label = "G", Nanometers = 587, Weight = 1, IsPrimary = true }, new() { Label = "B", Nanometers = 486, Weight = 1 }],
            FlatStart = new()
            {
                OptimizationMethod = FlatStartOptimizationMethod.AutoLensManagedV1,
                SamplingPolicy = FlatStartSamplingPolicy.UniformAreaGaussianV1,
                UsePhysicalStop = true,
                AutomaticLensDiameters = true,
                MinimumEdgeThicknessMillimeters = .4,
                AutoLens = new()
                {
                    Policy = AutoLensManagedPolicy.UpstreamE58,
                    ComputeMode = DesignComputeMode.Automatic,
                    EvenAsphereOrder = 9,
                    TargetPolishEvaluations = 0
                }
            },
            Budget = new()
            {
                InitialSeedCount = 1,
                MaximumEvaluations = 6000,
                MaximumParallelism = 1,
                RandomSeed = 42,
                TimeLimit = TimeSpan.FromHours(6)
            }
        },
        new()
        {
            AllowedGlassNames = AutoLensReferenceMaterials.ProjectionNames,
            MaximumEvaluationsPerTrial = 6000,
            DesignSearch = new() { Mode = DesignSearchMode.Full, StopSurfaceIndex = 1, ExploreStopPositions = false }
        }, [42]);
}

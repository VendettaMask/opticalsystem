using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Rays;

namespace OptilandWorkbench.Core.Optimization;

public sealed class MeritOperandDefinition
{
    public bool Enabled { get; set; } = true;

    public string Type { get; set; } = "RSCE";

    public int Surface { get; set; }

    public int Field { get; set; }

    public int Wavelength { get; set; }

    public double Hx { get; set; }

    public double Hy { get; set; }

    public double Px { get; set; }

    public double Py { get; set; }

    public double Target { get; set; }

    public double Weight { get; set; } = 1;

    public string Comment { get; set; } = string.Empty;

    public int PupilRings { get; set; } = 3;

    public int PupilArms { get; set; } = 6;

    public double PupilObscuration { get; set; }

    public string PupilSampling { get; set; } = "hexapolar";

    public double SpatialFrequency { get; set; } = 30;

    public bool IgnoreLateralColor { get; set; }

    public bool PolychromaticReference { get; set; }

    public MeritOperandDefinition Clone() => new()
    {
        Enabled = Enabled,
        Type = Type,
        Surface = Surface,
        Field = Field,
        Wavelength = Wavelength,
        Hx = Hx,
        Hy = Hy,
        Px = Px,
        Py = Py,
        Target = Target,
        Weight = Weight,
        Comment = Comment,
        PupilRings = PupilRings,
        PupilArms = PupilArms,
        PupilObscuration = PupilObscuration,
        PupilSampling = PupilSampling,
        SpatialFrequency = SpatialFrequency,
        IgnoreLateralColor = IgnoreLateralColor,
        PolychromaticReference = PolychromaticReference
    };
}

public enum MeritImageQuality
{
    RmsSpot,
    RmsWavefront,
    Contrast,
    Angular
}

public enum MeritPupilSampling
{
    GaussianQuadrature,
    RectangularArray
}

public enum MeritSpotReference
{
    Centroid,
    ChiefRay,
    Unreferenced
}

public sealed record MeritFunctionWizardSettings(
    MeritImageQuality ImageQuality,
    MeritPupilSampling PupilSampling,
    int PupilRings,
    int PupilArms,
    double PupilObscuration,
    double WeightScale,
    bool UseAllWavelengths,
    bool IncludeCommonOperands,
    MeritSpotReference Reference = MeritSpotReference.Centroid,
    double SpatialFrequency = 30,
    double XWeight = 1,
    double YWeight = 1,
    bool IgnoreLateralColor = false);

public sealed record MeritOperandType(
    string Code,
    string DisplayName,
    string Description);

public sealed record MeritOperandEvaluation(
    double Value,
    double Contribution,
    string Error = "");

public static class MeritFunctionCatalog
{
    private static readonly AsyncLocal<EvaluationBatch?> ActiveEvaluationBatch = new();

    public static IDisposable BeginEvaluationBatch()
    {
        var previous = ActiveEvaluationBatch.Value;
        ActiveEvaluationBatch.Value = new EvaluationBatch();
        return new EvaluationBatchScope(previous);
    }

    public static IReadOnlyList<MeritOperandType> Types { get; } = new[]
    {
        new MeritOperandType("DMFS", "默认评价函数设置", "默认评价函数向导生成的说明行"),
        new MeritOperandType("BLNK", "空白/注释", "不参与评价函数计算"),
        new MeritOperandType("CONF", "Zemax 配置切换", "从 Zemax 导入并作为只读记录保留"),
        new MeritOperandType("RANG", "Zemax 范围运算", "从 Zemax 导入并作为只读记录保留"),
        new MeritOperandType("CONS", "Zemax 常数", "从 Zemax 导入并作为只读记录保留"),
        new MeritOperandType("PROD", "Zemax 乘积运算", "从 Zemax 导入并作为只读记录保留"),
        new MeritOperandType("OPLT", "Zemax 小于约束", "从 Zemax 导入并作为只读记录保留"),
        new MeritOperandType("MNCA", "最小空气中心厚度", "从 Zemax 导入并作为只读记录保留"),
        new MeritOperandType("MXCA", "最大空气中心厚度", "从 Zemax 导入并作为只读记录保留"),
        new MeritOperandType("MNEA", "最小空气边缘厚度", "从 Zemax 导入并作为只读记录保留"),
        new MeritOperandType("MNCG", "最小玻璃中心厚度", "从 Zemax 导入并作为只读记录保留"),
        new MeritOperandType("MXCG", "最大玻璃中心厚度", "从 Zemax 导入并作为只读记录保留"),
        new MeritOperandType("MNEG", "最小玻璃边缘厚度", "从 Zemax 导入并作为只读记录保留"),
        new MeritOperandType("RSCE", "RMS 点列半径", "指定视场和波长的 RMS 点列半径"),
        new MeritOperandType("RSCH", "RMS 点列半径（主光线参考）", "使用高斯求积采样的主光线参考 RMS 点列半径"),
        new MeritOperandType("RSRE", "RMS 点列半径（矩形采样）", "使用矩形阵列采样的质心参考 RMS 点列半径"),
        new MeritOperandType("RSRH", "RMS 点列半径（矩形/主光线）", "使用矩形阵列采样的主光线参考 RMS 点列半径"),
        new MeritOperandType("RWFE", "RMS 波前差", "指定视场和波长的 RMS 光程差"),
        new MeritOperandType("OPDX", "光程差", "指定视场、波长和瞳孔坐标的光程差（波数）"),
        new MeritOperandType("OPDM", "光程差（主光线）", "减去平均波前但保留倾斜的光程差"),
        new MeritOperandType("OPDC", "光程差（无参考）", "以主光线为零点且不移除平均值或倾斜的光程差"),
        new MeritOperandType("TRAC", "横向像差半径（质心）", "相对于质心的横向像差半径"),
        new MeritOperandType("TRAR", "横向像差半径（主光线）", "相对于主波长主光线的横向像差半径"),
        new MeritOperandType("TRCX", "横向像差 X（质心）", "相对于质心的有符号 X 横向像差"),
        new MeritOperandType("TRCY", "横向像差 Y（质心）", "相对于质心的有符号 Y 横向像差"),
        new MeritOperandType("TRAX", "横向像差 X（主光线）", "相对于主波长主光线的有符号 X 横向像差"),
        new MeritOperandType("TRAY", "横向像差 Y（主光线）", "相对于主波长主光线的有符号 Y 横向像差"),
        new MeritOperandType("ANAC", "角像差半径（质心）", "相对于方向余弦质心的角像差半径"),
        new MeritOperandType("ANAR", "角像差半径（主光线）", "相对于主波长主光线的角像差半径"),
        new MeritOperandType("ANCX", "角像差 X（质心）", "相对于方向余弦质心的有符号 X 角像差"),
        new MeritOperandType("ANCY", "角像差 Y（质心）", "相对于方向余弦质心的有符号 Y 角像差"),
        new MeritOperandType("ANAX", "角像差 X（主光线）", "相对于主波长主光线的有符号 X 角像差"),
        new MeritOperandType("ANAY", "角像差 Y（主光线）", "相对于主波长主光线的有符号 Y 角像差"),
        new MeritOperandType("MECS", "Moore-Elliott 弧矢对比度", "弧矢方向移位光线对的光程差"),
        new MeritOperandType("MECT", "Moore-Elliott 切向对比度", "切向方向移位光线对的光程差"),
        new MeritOperandType("REAX", "实际光线 X", "指定光线在表面上的 X 坐标"),
        new MeritOperandType("REAY", "实际光线 Y", "指定光线在表面上的 Y 坐标"),
        new MeritOperandType("EFFL", "有效焦距", "系统有效焦距"),
        new MeritOperandType("FNUM", "像方 F 数", "系统像方 F 数"),
        new MeritOperandType("TOTR", "系统总长", "系统总光程长度"),
        new MeritOperandType("RADI", "表面曲率半径", "指定表面的曲率半径"),
        new MeritOperandType("THIC", "表面厚度", "指定表面后的轴向厚度")
    };

    private sealed class EvaluationBatch
    {
        public Dictionary<RaySampleCacheKey, RayTraceSample> RaySamples { get; } = new();

        public Dictionary<AberrationReferenceCacheKey, (double X, double Y)> AberrationReferences { get; } = new();

        public Dictionary<WavefrontReferenceCacheKey, (double Piston, double XTilt, double YTilt)> WavefrontReferences { get; } = new();
    }

    private sealed class EvaluationBatchScope(EvaluationBatch? previous) : IDisposable
    {
        public void Dispose()
        {
            ActiveEvaluationBatch.Value = previous;
        }
    }

    private readonly record struct RaySampleCacheKey(
        Optic Optic,
        int Surface,
        int Field,
        int Wavelength,
        double Hx,
        double Hy,
        double Px,
        double Py);

    private readonly record struct AberrationReferenceCacheKey(
        Optic Optic,
        bool Angular,
        bool ChiefReference,
        int Surface,
        int Field,
        int Wavelength,
        double Hx,
        double Hy,
        int PupilRings,
        int PupilArms,
        double PupilObscuration,
        string PupilSampling,
        bool PolychromaticReference);

    private readonly record struct WavefrontReferenceCacheKey(
        Optic Optic,
        string Type,
        int Surface,
        int Field,
        int Wavelength,
        double Hx,
        double Hy,
        int PupilRings,
        int PupilArms,
        double PupilObscuration,
        string PupilSampling,
        bool PolychromaticReference);

    public static MeritOperandEvaluation Evaluate(Optic optic, MeritOperandDefinition definition)
    {
        if (!definition.Enabled || CanonicalType(definition.Type) is "BLNK" or "DMFS")
        {
            return new MeritOperandEvaluation(0, 0);
        }

        try
        {
            var value = EvaluateValue(optic, definition);
            if (!double.IsFinite(value))
            {
                throw new InvalidOperationException("计算结果不是有限数值。");
            }

            var error = value - definition.Target;
            return new MeritOperandEvaluation(
                value,
                Math.Abs(definition.Weight) * error * error);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new MeritOperandEvaluation(double.NaN, double.PositiveInfinity, exception.Message);
        }
    }

    public static Operand CreateOperand(Optic optic, MeritOperandDefinition definition)
    {
        return new Operand(
            CanonicalType(definition.Type),
            definition.Target,
            definition.Weight,
            () =>
            {
                var value = Evaluate(optic, definition);
                return string.IsNullOrEmpty(value.Error) && double.IsFinite(value.Value)
                    ? value.Value
                    : 1_000_000;
            });
    }

    public static IReadOnlyList<MeritOperandDefinition> CreateDefaultRmsSpot(Optic optic)
    {
        return CreateFromWizard(optic, new MeritFunctionWizardSettings(
            MeritImageQuality.RmsSpot,
            MeritPupilSampling.GaussianQuadrature,
            PupilRings: 3,
            PupilArms: 6,
            PupilObscuration: 0,
            WeightScale: 1,
            UseAllWavelengths: true,
            IncludeCommonOperands: false));
    }

    public static IReadOnlyList<MeritOperandDefinition> CreateDefaultRmsWavefront(Optic optic)
    {
        return CreateFromWizard(optic, new MeritFunctionWizardSettings(
            MeritImageQuality.RmsWavefront,
            MeritPupilSampling.GaussianQuadrature,
            PupilRings: 3,
            PupilArms: 6,
            PupilObscuration: 0,
            WeightScale: 1,
            UseAllWavelengths: true,
            IncludeCommonOperands: false));
    }

    public static IReadOnlyList<MeritOperandDefinition> CreateFromWizard(
        Optic optic,
        MeritFunctionWizardSettings settings)
    {
        var rings = Math.Clamp(settings.PupilRings, 1, 20);
        var arms = Math.Clamp(settings.PupilArms, 3, 36);
        var obscuration = Math.Clamp(settings.PupilObscuration, 0, 0.95);
        var weightScale = double.IsFinite(settings.WeightScale)
            ? Math.Max(0, settings.WeightScale)
            : 1;
        var samplingName = settings.PupilSampling == MeritPupilSampling.RectangularArray
            ? "uniform"
            : "gaussian_quad";
        var operands = new List<MeritOperandDefinition>
        {
            new() { Enabled = false, Type = "DMFS" },
            new()
            {
                Enabled = false,
                Type = "BLNK",
                Comment = $"序列评价函数：RMS {QualityName(settings.ImageQuality)}；" +
                          $"{ReferenceName(settings.Reference)}参考；{rings} 环 {arms} 臂"
            },
            new()
            {
                Enabled = false,
                Type = "BLNK",
                Comment = "由优化向导生成；各视场和波长权重已归一化。"
            }
        };

        var fieldWeights = NormalizeWeights(optic.Fields.Select(field => field.Weight).ToArray());
        var wavelengthIndices = settings.UseAllWavelengths
            ? Enumerable.Range(0, optic.Wavelengths.Count).ToArray()
            : new[] { PrimaryWavelengthIndex(optic) };
        var wavelengthWeights = NormalizeWeights(
            wavelengthIndices.Select(index => optic.Wavelengths[index].Weight).ToArray());
        var pupilPrototype = new MeritOperandDefinition
        {
            PupilRings = rings,
            PupilArms = arms,
            PupilObscuration = obscuration,
            PupilSampling = samplingName
        };
        var xWeight = Math.Max(0, double.IsFinite(settings.XWeight) ? settings.XWeight : 1);
        var yWeight = Math.Max(0, double.IsFinite(settings.YWeight) ? settings.YWeight : 1);
        if (settings.ImageQuality == MeritImageQuality.Contrast
            && (settings.SpatialFrequency <= 0 || (xWeight <= 0 && yWeight <= 0)))
        {
            throw new ArgumentException("对比度优化需要正的空间频率，并至少启用一个方向权重。", nameof(settings));
        }

        for (var fieldIndex = 0; fieldIndex < optic.Fields.Count; fieldIndex++)
        {
            var field = optic.Fields[fieldIndex];
            var normalizedField = FieldCoordinates.Normalize(optic.Fields, field.X, field.Y);
            var pupilSamples = CreateWizardOperandPupilSamples(pupilPrototype, normalizedField);
            operands.Add(new MeritOperandDefinition
            {
                Enabled = false,
                Type = "BLNK",
                Comment = $"视场操作数 {fieldIndex + 1}：{field.Label}"
            });

            for (var wavelengthOffset = 0; wavelengthOffset < wavelengthIndices.Length; wavelengthOffset++)
            {
                var wavelengthIndex = wavelengthIndices[wavelengthOffset];
                var baseWeight = fieldWeights[fieldIndex] * wavelengthWeights[wavelengthOffset];
                foreach (var pupilSample in pupilSamples)
                {
                    if (settings.ImageQuality == MeritImageQuality.Contrast)
                    {
                        AddContrastOperands(
                            optic,
                            operands,
                            settings,
                            fieldIndex,
                            wavelengthIndex,
                            pupilSample,
                            baseWeight,
                            weightScale,
                            xWeight,
                            yWeight,
                            pupilPrototype);
                        continue;
                    }

                    var axisWeight = baseWeight * pupilSample.Weight;
                    if (settings.ImageQuality == MeritImageQuality.RmsWavefront)
                    {
                        operands.Add(CreateSampleOperand(
                            settings.Reference switch
                            {
                                MeritSpotReference.ChiefRay => "OPDM",
                                MeritSpotReference.Unreferenced => "OPDC",
                                _ => "OPDX"
                            },
                            fieldIndex,
                            wavelengthIndex,
                            pupilSample,
                            weightScale * axisWeight,
                            settings,
                            pupilPrototype));
                        continue;
                    }

                    AddRayAberrationOperands(
                        operands,
                        settings,
                        fieldIndex,
                        wavelengthIndex,
                        pupilSample,
                        axisWeight,
                        weightScale,
                        xWeight,
                        yWeight,
                        pupilPrototype);
                }
            }
        }

        if (settings.ImageQuality == MeritImageQuality.Contrast
            && !operands.Any(operand => operand.Type is "MECS" or "MECT"))
        {
            throw new InvalidOperationException("当前空间频率没有可用的 Moore-Elliott 移位光线对。");
        }

        AddCommonOperands(optic, operands, weightScale, settings.IncludeCommonOperands);
        return operands;
    }

    private static void AddRayAberrationOperands(
        ICollection<MeritOperandDefinition> operands,
        MeritFunctionWizardSettings settings,
        int fieldIndex,
        int wavelengthIndex,
        Raytrace.PupilSample pupilSample,
        double baseWeight,
        double weightScale,
        double xWeight,
        double yWeight,
        MeritOperandDefinition prototype)
    {
        var angular = settings.ImageQuality == MeritImageQuality.Angular;
        if (xWeight <= 0 && yWeight <= 0)
        {
            operands.Add(CreateSampleOperand(
                angular
                    ? settings.Reference == MeritSpotReference.ChiefRay ? "ANAR" : "ANAC"
                    : settings.Reference == MeritSpotReference.ChiefRay ? "TRAR" : "TRAC",
                fieldIndex,
                wavelengthIndex,
                pupilSample,
                weightScale * baseWeight,
                settings,
                prototype));
            return;
        }

        if (xWeight > 0)
        {
            operands.Add(CreateSampleOperand(
                angular
                    ? settings.Reference == MeritSpotReference.ChiefRay ? "ANAX" : "ANCX"
                    : settings.Reference == MeritSpotReference.ChiefRay ? "TRAX" : "TRCX",
                fieldIndex,
                wavelengthIndex,
                pupilSample,
                weightScale * baseWeight * xWeight,
                settings,
                prototype));
        }

        if (yWeight > 0)
        {
            operands.Add(CreateSampleOperand(
                angular
                    ? settings.Reference == MeritSpotReference.ChiefRay ? "ANAY" : "ANCY"
                    : settings.Reference == MeritSpotReference.ChiefRay ? "TRAY" : "TRCY",
                fieldIndex,
                wavelengthIndex,
                pupilSample,
                weightScale * baseWeight * yWeight,
                settings,
                prototype));
        }
    }

    private static void AddContrastOperands(
        Optic optic,
        ICollection<MeritOperandDefinition> operands,
        MeritFunctionWizardSettings settings,
        int fieldIndex,
        int wavelengthIndex,
        Raytrace.PupilSample pupilSample,
        double baseWeight,
        double weightScale,
        double xWeight,
        double yWeight,
        MeritOperandDefinition prototype)
    {
        var frequency = Math.Max(0, settings.SpatialFrequency);
        var cutoff = DiffractionCutoff(optic, optic.Wavelengths[wavelengthIndex]);
        var pupilShift = cutoff <= 1e-12 ? double.PositiveInfinity : 2 * frequency / cutoff;
        if (!double.IsFinite(pupilShift) || pupilShift > 2 + 1e-12)
        {
            return;
        }

        if (xWeight > 0 && PairFitsPupil(pupilSample.X, pupilSample.Y, pupilShift, sagittal: true))
        {
            var operand = CreateSampleOperand(
                "MECS",
                fieldIndex,
                wavelengthIndex,
                pupilSample,
                weightScale * baseWeight * pupilSample.Weight * xWeight,
                settings,
                prototype);
            operand.SpatialFrequency = frequency;
            operands.Add(operand);
        }

        if (yWeight > 0 && PairFitsPupil(pupilSample.X, pupilSample.Y, pupilShift, sagittal: false))
        {
            var operand = CreateSampleOperand(
                "MECT",
                fieldIndex,
                wavelengthIndex,
                pupilSample,
                weightScale * baseWeight * pupilSample.Weight * yWeight,
                settings,
                prototype);
            operand.SpatialFrequency = frequency;
            operands.Add(operand);
        }
    }

    private static MeritOperandDefinition CreateSampleOperand(
        string type,
        int fieldIndex,
        int wavelengthIndex,
        Raytrace.PupilSample pupilSample,
        double weight,
        MeritFunctionWizardSettings settings,
        MeritOperandDefinition prototype)
    {
        return new MeritOperandDefinition
        {
            Type = type,
            Field = fieldIndex + 1,
            Wavelength = wavelengthIndex + 1,
            Px = pupilSample.X,
            Py = pupilSample.Y,
            Target = 0,
            Weight = weight,
            PupilRings = prototype.PupilRings,
            PupilArms = prototype.PupilArms,
            PupilObscuration = prototype.PupilObscuration,
            PupilSampling = prototype.PupilSampling,
            SpatialFrequency = settings.SpatialFrequency,
            IgnoreLateralColor = settings.IgnoreLateralColor,
            PolychromaticReference = settings.UseAllWavelengths && !settings.IgnoreLateralColor
        };
    }

    private static IReadOnlyList<Raytrace.PupilSample> NormalizePupilSamples(
        IReadOnlyList<Raytrace.PupilSample> samples)
    {
        var total = samples.Sum(sample => Math.Max(0, sample.Weight));
        if (total <= 1e-12)
        {
            return samples.Select(sample => sample with { Weight = 1.0 / Math.Max(1, samples.Count) }).ToArray();
        }

        return samples.Select(sample => sample with { Weight = Math.Max(0, sample.Weight) / total }).ToArray();
    }

    private static IReadOnlyList<Raytrace.PupilSample> CreateWizardOperandPupilSamples(
        MeritOperandDefinition prototype,
        (double X, double Y) normalizedField)
    {
        if (!string.Equals(prototype.PupilSampling, "gaussian_quad", StringComparison.OrdinalIgnoreCase))
        {
            return NormalizePupilSamples(CreateWizardPupilSamples(prototype, 37));
        }

        var rings = Math.Clamp(prototype.PupilRings, 1, 20);
        var arms = Math.Clamp(prototype.PupilArms, 3, 36);
        var obscuration = Math.Clamp(prototype.PupilObscuration, 0, 0.95);
        var onAxis = Math.Abs(normalizedField.X) <= 1e-12 && Math.Abs(normalizedField.Y) <= 1e-12;
        var directionCount = onAxis ? 1 : Math.Max(1, arms / 2);
        return GaussianRadialSamples(rings, obscuration)
            .SelectMany(radialSample => Enumerable.Range(0, directionCount).Select(direction =>
            {
                var angle = onAxis
                    ? 0
                    : (((directionCount - 1) / 2.0) - direction) * (2 * Math.PI / arms);
                return new Raytrace.PupilSample(
                    radialSample.Radius * Math.Cos(angle),
                    radialSample.Radius * Math.Sin(angle),
                    radialSample.Weight / directionCount);
            }))
            .ToArray();
    }

    private static bool PairFitsPupil(double px, double py, double shift, bool sagittal)
    {
        var half = shift / 2;
        var firstX = sagittal ? px - half : px;
        var firstY = sagittal ? py : py - half;
        var secondX = sagittal ? px + half : px;
        var secondY = sagittal ? py : py + half;
        return ((firstX * firstX) + (firstY * firstY) <= 1 + 1e-12)
            && ((secondX * secondX) + (secondY * secondY) <= 1 + 1e-12);
    }

    private static int PrimaryWavelengthIndex(Optic optic)
    {
        var index = optic.Wavelengths
            .Select((wavelength, offset) => (wavelength, offset))
            .FirstOrDefault(item => item.wavelength.IsPrimary).offset;
        return Math.Clamp(index, 0, Math.Max(0, optic.Wavelengths.Count - 1));
    }

    private static double DiffractionCutoff(Optic optic, Wavelength wavelength)
    {
        var fNumber = Math.Abs(optic.Paraxial.EstimateFNumber());
        return fNumber <= 1e-12 || wavelength.Micrometers <= 1e-12
            ? 0
            : 1 / (wavelength.Micrometers * 1e-3 * fNumber);
    }

    private static string QualityName(MeritImageQuality quality) => quality switch
    {
        MeritImageQuality.RmsWavefront => "波前",
        MeritImageQuality.Contrast => "对比度",
        MeritImageQuality.Angular => "角向",
        _ => "点列图"
    };

    private static string ReferenceName(MeritSpotReference reference) => reference switch
    {
        MeritSpotReference.ChiefRay => "主光线",
        MeritSpotReference.Unreferenced => "无参考",
        _ => "质心"
    };

    private static IReadOnlyList<(double Radius, double Weight)> GaussianRadialSamples(
        int sampleCount,
        double obscuration)
    {
        var lower = obscuration * obscuration;
        var span = 1 - lower;
        var samples = new (double Radius, double Weight)[sampleCount];
        var rootsToFind = (sampleCount + 1) / 2;
        for (var rootIndex = 0; rootIndex < rootsToFind; rootIndex++)
        {
            var root = Math.Cos(Math.PI * (rootIndex + 0.75) / (sampleCount + 0.5));
            double derivative;
            for (var iteration = 0; iteration < 32; iteration++)
            {
                var previous = 1.0;
                var current = root;
                for (var order = 2; order <= sampleCount; order++)
                {
                    var next = (((2 * order) - 1) * root * current - ((order - 1) * previous)) / order;
                    previous = current;
                    current = next;
                }

                derivative = sampleCount * ((root * current) - previous) / ((root * root) - 1);
                var nextRoot = root - (current / derivative);
                if (Math.Abs(nextRoot - root) <= 1e-15)
                {
                    root = nextRoot;
                    break;
                }

                root = nextRoot;
            }

            var p0 = 1.0;
            var p1 = root;
            for (var order = 2; order <= sampleCount; order++)
            {
                var next = (((2 * order) - 1) * root * p1 - ((order - 1) * p0)) / order;
                p0 = p1;
                p1 = next;
            }

            derivative = sampleCount * ((root * p1) - p0) / ((root * root) - 1);
            var legendreWeight = 2 / ((1 - (root * root)) * derivative * derivative);
            SetRadialSample(rootIndex, -root, legendreWeight);
            SetRadialSample(sampleCount - rootIndex - 1, root, legendreWeight);
        }

        return samples;

        void SetRadialSample(int index, double node, double legendreWeight)
        {
            var normalizedRadiusSquared = (node + 1) / 2;
            samples[index] = (
                Math.Sqrt(lower + (span * normalizedRadiusSquared)),
                span * legendreWeight / 2);
        }
    }

    private static void AddCommonOperands(
        Optic optic,
        ICollection<MeritOperandDefinition> operands,
        double weightScale,
        bool includeCommonOperands)
    {
        if (!includeCommonOperands)
        {
            return;
        }

        operands.Add(new MeritOperandDefinition
        {
            Type = "EFFL",
            Target = optic.Paraxial.EstimateEffectiveFocalLength(),
            Weight = weightScale,
            Comment = "保持当前有效焦距"
        });
        operands.Add(new MeritOperandDefinition
        {
            Type = "FNUM",
            Target = optic.Paraxial.EstimateFNumber(),
            Weight = weightScale,
            Comment = "保持当前 F 数"
        });
    }

    public static string CanonicalType(string? type)
    {
        var canonical = (type ?? string.Empty).Trim().ToUpperInvariant();
        return Types.Any(item => item.Code == canonical) ? canonical : "BLNK";
    }

    private static double EvaluateValue(Optic optic, MeritOperandDefinition definition)
    {
        return CanonicalType(definition.Type) switch
        {
            "RSCE" => EvaluateRmsSpot(optic, definition),
            "RSCH" => EvaluateRmsSpot(optic, definition),
            "RSRE" => EvaluateRmsSpot(optic, definition),
            "RSRH" => EvaluateRmsSpot(optic, definition),
            "RWFE" => EvaluateRmsWavefront(optic, definition),
            "OPDX" => EvaluateOpticalPathDifference(optic, definition),
            "OPDM" => EvaluateOpticalPathDifference(optic, definition),
            "OPDC" => EvaluateOpticalPathDifference(optic, definition),
            "TRAC" => EvaluateRayAberration(optic, definition),
            "TRAR" => EvaluateRayAberration(optic, definition),
            "TRCX" => EvaluateRayAberration(optic, definition),
            "TRCY" => EvaluateRayAberration(optic, definition),
            "TRAX" => EvaluateRayAberration(optic, definition),
            "TRAY" => EvaluateRayAberration(optic, definition),
            "ANAC" => EvaluateAngularAberration(optic, definition),
            "ANAR" => EvaluateAngularAberration(optic, definition),
            "ANCX" => EvaluateAngularAberration(optic, definition),
            "ANCY" => EvaluateAngularAberration(optic, definition),
            "ANAX" => EvaluateAngularAberration(optic, definition),
            "ANAY" => EvaluateAngularAberration(optic, definition),
            "MECS" => EvaluateMooreElliottDifference(optic, definition, sagittal: true),
            "MECT" => EvaluateMooreElliottDifference(optic, definition, sagittal: false),
            "REAX" => SampleAtSurface(optic, definition).Position.X,
            "REAY" => SampleAtSurface(optic, definition).Position.Y,
            "EFFL" => optic.Paraxial.EstimateEffectiveFocalLength(),
            "FNUM" => optic.Paraxial.EstimateFNumber(),
            "TOTR" => optic.SurfaceGroup.TotalTrack,
            "RADI" => ResolveSurface(optic, definition.Surface).Radius,
            "THIC" => ResolveSurface(optic, definition.Surface).Thickness,
            _ => 0
        };
    }

    private static double EvaluateRmsSpot(Optic optic, MeritOperandDefinition definition)
    {
        var normalized = ResolveNormalizedField(optic, definition);
        var pupilSamples = CreateWizardPupilSamples(definition, 37);
        var wavelengths = definition.Wavelength <= 0
            ? optic.Wavelengths.ToArray()
            : new[] { ResolveWavelength(optic, definition.Wavelength) };
        if (wavelengths.Length == 0)
        {
            throw new InvalidOperationException("系统没有波长。");
        }

        var samplesByWavelength = wavelengths
            .Select(wavelength => TraceSpotSamples(
                optic,
                definition,
                normalized,
                wavelength,
                pupilSamples))
            .ToArray();
        var primaryIndex = definition.Wavelength <= 0
            ? Array.FindIndex(wavelengths, wavelength => wavelength.IsPrimary)
            : 0;
        if (primaryIndex < 0)
        {
            primaryIndex = 0;
        }

        var referenceSamples = samplesByWavelength[primaryIndex];
        if (referenceSamples.Length == 0)
        {
            throw new InvalidOperationException("主波长没有有效光线。");
        }

        var referenceType = CanonicalType(definition.Type);
        double referenceX;
        double referenceY;
        if (referenceType is "RSCH" or "RSRH")
        {
            var chief = TraceChiefRaySample(optic, definition, normalized, wavelengths[primaryIndex]);
            referenceX = chief.Position.X;
            referenceY = chief.Position.Y;
        }
        else
        {
            var referenceWeight = referenceSamples.Sum(sample => sample.Intensity);
            if (referenceWeight <= 1e-12)
            {
                throw new InvalidOperationException("主波长没有有效光线。");
            }

            referenceX = referenceSamples.Sum(sample => sample.Position.X * sample.Intensity) / referenceWeight;
            referenceY = referenceSamples.Sum(sample => sample.Position.Y * sample.Intensity) / referenceWeight;
        }

        var samples = samplesByWavelength.SelectMany(items => items).ToArray();
        var totalWeight = samples.Sum(sample => sample.Intensity);
        if (samples.Length == 0 || totalWeight <= 1e-12)
        {
            throw new InvalidOperationException("没有有效光线。");
        }

        return Math.Sqrt(samples.Sum(sample =>
            (((sample.Position.X - referenceX) * (sample.Position.X - referenceX))
             + ((sample.Position.Y - referenceY) * (sample.Position.Y - referenceY))) * sample.Intensity) / totalWeight);
    }

    private static RayTraceSample TraceChiefRaySample(
        Optic optic,
        MeritOperandDefinition definition,
        (double X, double Y) normalized,
        Wavelength wavelength)
    {
        var chiefDefinition = definition.Clone();
        chiefDefinition.Wavelength = FindWavelengthIndex(optic, wavelength) + 1;
        chiefDefinition.Hx = normalized.X;
        chiefDefinition.Hy = normalized.Y;
        chiefDefinition.Px = 0;
        chiefDefinition.Py = 0;
        var sample = SampleAtSurface(optic, chiefDefinition);
        if (sample.Vignetted || sample.Intensity <= 0)
        {
            throw new InvalidOperationException("主光线未到达指定表面。");
        }

        return sample;
    }

    private static RayTraceSample[] TraceSpotSamples(
        Optic optic,
        MeritOperandDefinition definition,
        (double X, double Y) normalized,
        Wavelength wavelength,
        IReadOnlyList<Raytrace.PupilSample> pupilSamples)
    {
        var bundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(
            normalized.X,
            normalized.Y,
            wavelength.Micrometers,
            pupilSamples);
        var trace = optic.SequentialRayTracer.Trace(bundle);
        var wavelengthIndex = FindWavelengthIndex(optic, wavelength) + 1;
        var samples = new List<RayTraceSample>(trace.RayHistories.Count);
        for (var index = 0; index < trace.RayHistories.Count; index++)
        {
            var history = trace.RayHistories[index];
            var sample = history.Count == 0 ? null : SelectSpotSurfaceSample(history, definition.Surface);
            if (sample is null || sample.Vignetted || sample.Intensity <= 0)
            {
                continue;
            }

            samples.Add(sample);
            var batch = ActiveEvaluationBatch.Value;
            if (batch is not null && index < pupilSamples.Count)
            {
                var pupil = pupilSamples[index];
                batch.RaySamples[CreateRaySampleCacheKey(
                    optic,
                    definition,
                    wavelengthIndex,
                    pupil.X,
                    pupil.Y)] = sample;
            }
        }

        return samples.ToArray();
    }

    private static RayTraceSample? SelectSpotSurfaceSample(
        IReadOnlyList<RayTraceSample> history,
        int surfaceNumber)
    {
        return surfaceNumber <= 0
            ? history.LastOrDefault()
            : history.LastOrDefault(sample => sample.SurfaceNumber == surfaceNumber);
    }

    private static double EvaluateRayAberration(Optic optic, MeritOperandDefinition definition)
    {
        var sample = SampleAtSurface(optic, definition);
        var reference = ResolveAberrationReference(optic, definition, angular: false);
        var x = sample.Position.X - reference.X;
        var y = sample.Position.Y - reference.Y;
        return CanonicalType(definition.Type) switch
        {
            "TRCX" or "TRAX" => x,
            "TRCY" or "TRAY" => y,
            _ => Math.Sqrt((x * x) + (y * y))
        };
    }

    private static double EvaluateAngularAberration(Optic optic, MeritOperandDefinition definition)
    {
        var sample = SampleAtSurface(optic, definition);
        var reference = ResolveAberrationReference(optic, definition, angular: true);
        var x = sample.Direction.X - reference.X;
        var y = sample.Direction.Y - reference.Y;
        return CanonicalType(definition.Type) switch
        {
            "ANCX" or "ANAX" => x,
            "ANCY" or "ANAY" => y,
            _ => Math.Sqrt((x * x) + (y * y))
        };
    }

    private static (double X, double Y) ResolveAberrationReference(
        Optic optic,
        MeritOperandDefinition definition,
        bool angular)
    {
        var type = CanonicalType(definition.Type);
        var chiefReference = type is "TRAR" or "TRAX" or "TRAY" or "ANAR" or "ANAX" or "ANAY";
        var cacheKey = new AberrationReferenceCacheKey(
            optic,
            angular,
            chiefReference,
            definition.Surface,
            definition.Field,
            chiefReference || definition.PolychromaticReference ? 0 : definition.Wavelength,
            definition.Hx,
            definition.Hy,
            definition.PupilRings,
            definition.PupilArms,
            definition.PupilObscuration,
            definition.PupilSampling,
            definition.PolychromaticReference);
        var batch = ActiveEvaluationBatch.Value;
        if (batch is not null && batch.AberrationReferences.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var normalized = ResolveNormalizedField(optic, definition);
        (double X, double Y) result;
        if (chiefReference)
        {
            var primary = optic.Wavelengths[PrimaryWavelengthIndex(optic)];
            var chief = TraceChiefRaySample(optic, definition, normalized, primary);
            result = angular
                ? (chief.Direction.X, chief.Direction.Y)
                : (chief.Position.X, chief.Position.Y);
        }
        else
        {
            var wavelengthIndices = definition.PolychromaticReference
                ? Enumerable.Range(0, optic.Wavelengths.Count).ToArray()
                : new[] { Math.Clamp(definition.Wavelength - 1, 0, optic.Wavelengths.Count - 1) };
            var wavelengthWeights = NormalizeWeights(
                wavelengthIndices.Select(index => optic.Wavelengths[index].Weight).ToArray());
            var pupilSamples = NormalizePupilSamples(CreateWizardPupilSamples(definition, 37));
            var weightedX = 0.0;
            var weightedY = 0.0;
            var totalWeight = 0.0;
            for (var wavelengthOffset = 0; wavelengthOffset < wavelengthIndices.Length; wavelengthOffset++)
            {
                var wavelength = optic.Wavelengths[wavelengthIndices[wavelengthOffset]];
                var samples = TraceSpotSamples(optic, definition, normalized, wavelength, pupilSamples);
                foreach (var sample in samples)
                {
                    var weight = sample.Intensity * wavelengthWeights[wavelengthOffset];
                    weightedX += (angular ? sample.Direction.X : sample.Position.X) * weight;
                    weightedY += (angular ? sample.Direction.Y : sample.Position.Y) * weight;
                    totalWeight += weight;
                }
            }

            if (totalWeight <= 1e-12)
            {
                throw new InvalidOperationException("没有可用于计算参考质心的有效光线。");
            }

            result = (weightedX / totalWeight, weightedY / totalWeight);
        }

        if (batch is not null)
        {
            batch.AberrationReferences[cacheKey] = result;
        }

        return result;
    }

    private static double EvaluateMooreElliottDifference(
        Optic optic,
        MeritOperandDefinition definition,
        bool sagittal)
    {
        var wavelength = ResolveWavelength(optic, definition.Wavelength);
        var cutoff = DiffractionCutoff(optic, wavelength);
        if (cutoff <= 1e-12)
        {
            throw new InvalidOperationException("系统没有有效的衍射截止频率。");
        }

        var shift = 2 * Math.Max(0, definition.SpatialFrequency) / cutoff;
        if (!PairFitsPupil(definition.Px, definition.Py, shift, sagittal))
        {
            throw new InvalidOperationException("Moore-Elliott 移位光线超出当前入瞳。");
        }

        var half = shift / 2;
        var first = definition.Clone();
        var second = definition.Clone();
        if (sagittal)
        {
            first.Px -= half;
            second.Px += half;
        }
        else
        {
            first.Py -= half;
            second.Py += half;
        }

        var firstSample = SampleAtSurface(optic, first);
        var secondSample = SampleAtSurface(optic, second);
        var wavelengthMillimeters = wavelength.Micrometers / 1000.0;
        return (secondSample.CumulativeOpticalPathLength - firstSample.CumulativeOpticalPathLength)
            / Math.Max(1e-12, wavelengthMillimeters);
    }

    private static double EvaluateRmsWavefront(Optic optic, MeritOperandDefinition definition)
    {
        var trace = TraceBundle(optic, definition, 37);
        var paths = trace.RayHistories
            .Where(history => history.Count > 0)
            .Select(history => history[^1])
            .Where(sample => !sample.Vignetted && sample.Intensity > 0)
            .Select(sample => sample.CumulativeOpticalPathLength)
            .ToArray();
        if (paths.Length == 0)
        {
            throw new InvalidOperationException("没有有效光线。");
        }

        var mean = paths.Average();
        var rmsMillimeters = Math.Sqrt(paths.Select(path => (path - mean) * (path - mean)).Average());
        var wavelengthMillimeters = ResolveWavelength(optic, definition.Wavelength).Micrometers / 1000.0;
        return rmsMillimeters / Math.Max(1e-12, wavelengthMillimeters);
    }

    private static double EvaluateOpticalPathDifference(Optic optic, MeritOperandDefinition definition)
    {
        var sample = SampleAtSurface(optic, definition);
        var wavelengthMillimeters = ResolveWavelength(optic, definition.Wavelength).Micrometers / 1000.0;
        var type = CanonicalType(definition.Type);
        if (type == "OPDC")
        {
            var chiefDefinition = definition.Clone();
            chiefDefinition.Px = 0;
            chiefDefinition.Py = 0;
            var chief = SampleAtSurface(optic, chiefDefinition);
            return (sample.CumulativeOpticalPathLength - chief.CumulativeOpticalPathLength)
                / Math.Max(1e-12, wavelengthMillimeters);
        }

        var cacheKey = new WavefrontReferenceCacheKey(
            optic,
            type,
            definition.Surface,
            definition.Field,
            definition.Wavelength,
            definition.Hx,
            definition.Hy,
            definition.PupilRings,
            definition.PupilArms,
            definition.PupilObscuration,
            definition.PupilSampling,
            definition.PolychromaticReference);
        var batch = ActiveEvaluationBatch.Value;
        if (batch is null || !batch.WavefrontReferences.TryGetValue(cacheKey, out var plane))
        {
            var pupilSamples = NormalizePupilSamples(CreateWizardPupilSamples(definition, 37));
            var trace = TraceBundle(optic, definition, 37);
            var fittedSamples = trace.RayHistories
                .Select((history, index) => new
                {
                    Pupil = pupilSamples[index],
                    Sample = history.Count == 0 ? null : SelectSpotSurfaceSample(history, definition.Surface)
                })
                .Where(item => item.Sample is not null && !item.Sample.Vignetted && item.Sample.Intensity > 0)
                .Select(item => (
                    item.Pupil.X,
                    item.Pupil.Y,
                    Path: item.Sample!.CumulativeOpticalPathLength,
                    Weight: item.Sample.Intensity))
                .ToArray();
            if (fittedSamples.Length == 0)
            {
                throw new InvalidOperationException("没有可用于计算波前参考的有效光线。");
            }

            if (type == "OPDM")
            {
                var totalWeight = fittedSamples.Sum(item => item.Weight);
                plane = (
                    fittedSamples.Sum(item => item.Path * item.Weight) / totalWeight,
                    0,
                    0);
            }
            else
            {
                plane = FitWeightedPlane(fittedSamples);
            }

            if (batch is not null)
            {
                batch.WavefrontReferences[cacheKey] = plane;
            }
        }

        var referencePath = plane.Piston + (plane.XTilt * definition.Px) + (plane.YTilt * definition.Py);
        return (sample.CumulativeOpticalPathLength - referencePath)
            / Math.Max(1e-12, wavelengthMillimeters);
    }

    private static (double Piston, double XTilt, double YTilt) FitWeightedPlane(
        IReadOnlyList<(double X, double Y, double Path, double Weight)> samples)
    {
        var matrix = new double[3, 4];
        foreach (var sample in samples)
        {
            var weight = Math.Max(0, sample.Weight);
            var basis = new[] { 1.0, sample.X, sample.Y };
            for (var row = 0; row < 3; row++)
            {
                for (var column = 0; column < 3; column++)
                {
                    matrix[row, column] += weight * basis[row] * basis[column];
                }

                matrix[row, 3] += weight * basis[row] * sample.Path;
            }
        }

        for (var pivot = 0; pivot < 3; pivot++)
        {
            var best = pivot;
            for (var row = pivot + 1; row < 3; row++)
            {
                if (Math.Abs(matrix[row, pivot]) > Math.Abs(matrix[best, pivot]))
                {
                    best = row;
                }
            }

            if (Math.Abs(matrix[best, pivot]) <= 1e-18)
            {
                var totalWeight = samples.Sum(sample => Math.Max(0, sample.Weight));
                var mean = samples.Sum(sample => sample.Path * Math.Max(0, sample.Weight))
                    / Math.Max(1e-12, totalWeight);
                return (mean, 0, 0);
            }

            if (best != pivot)
            {
                for (var column = pivot; column < 4; column++)
                {
                    (matrix[pivot, column], matrix[best, column]) =
                        (matrix[best, column], matrix[pivot, column]);
                }
            }

            var divisor = matrix[pivot, pivot];
            for (var column = pivot; column < 4; column++)
            {
                matrix[pivot, column] /= divisor;
            }

            for (var row = 0; row < 3; row++)
            {
                if (row == pivot)
                {
                    continue;
                }

                var factor = matrix[row, pivot];
                for (var column = pivot; column < 4; column++)
                {
                    matrix[row, column] -= factor * matrix[pivot, column];
                }
            }
        }

        return (matrix[0, 3], matrix[1, 3], matrix[2, 3]);
    }

    private static RayTraceSample SampleAtSurface(Optic optic, MeritOperandDefinition definition)
    {
        var cacheKey = CreateRaySampleCacheKey(
            optic,
            definition,
            definition.Wavelength,
            definition.Px,
            definition.Py);
        var batch = ActiveEvaluationBatch.Value;
        if (batch is not null && batch.RaySamples.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var normalized = ResolveNormalizedField(optic, definition);
        var wavelength = ResolveWavelength(optic, definition.Wavelength);
        var trace = optic.TraceGeneric(
            normalized.X,
            normalized.Y,
            Math.Clamp(definition.Px, -1, 1),
            Math.Clamp(definition.Py, -1, 1),
            wavelength.Micrometers);
        var history = trace.RayHistories.FirstOrDefault()
            ?? throw new InvalidOperationException("光线追迹没有返回结果。");
        if (history.Count == 0)
        {
            throw new InvalidOperationException("光线追迹没有返回采样点。");
        }

        var sample = definition.Surface <= 0
            ? history[^1]
            : history.LastOrDefault(item => item.SurfaceNumber == definition.Surface)
              ?? throw new ArgumentOutOfRangeException(nameof(definition.Surface), "指定表面没有光线数据。");
        if (batch is not null)
        {
            batch.RaySamples[cacheKey] = sample;
        }

        return sample;
    }

    private static RaySampleCacheKey CreateRaySampleCacheKey(
        Optic optic,
        MeritOperandDefinition definition,
        int wavelength,
        double px,
        double py)
    {
        return new RaySampleCacheKey(
            optic,
            definition.Surface,
            definition.Field,
            wavelength,
            Quantize(definition.Hx),
            Quantize(definition.Hy),
            Quantize(px),
            Quantize(py));
    }

    private static int FindWavelengthIndex(Optic optic, Wavelength wavelength)
    {
        for (var index = 0; index < optic.Wavelengths.Count; index++)
        {
            if (ReferenceEquals(optic.Wavelengths[index], wavelength)
                || optic.Wavelengths[index].Equals(wavelength))
            {
                return index;
            }
        }

        return 0;
    }

    private static double Quantize(double value) => Math.Round(value, 12);

    private static Raytrace.SequentialTrace TraceBundle(
        Optic optic,
        MeritOperandDefinition definition,
        int sampleCount)
    {
        var normalized = ResolveNormalizedField(optic, definition);
        var wavelength = ResolveWavelength(optic, definition.Wavelength);
        var pupilSamples = CreateWizardPupilSamples(definition, sampleCount);
        var bundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(
            normalized.X,
            normalized.Y,
            wavelength.Micrometers,
            pupilSamples);
        return optic.SequentialRayTracer.Trace(bundle);
    }

    private static IReadOnlyList<Raytrace.PupilSample> CreateWizardPupilSamples(
        MeritOperandDefinition definition,
        int fallbackSampleCount)
    {
        var rings = Math.Clamp(definition.PupilRings, 1, 20);
        var arms = Math.Clamp(definition.PupilArms, 3, 36);
        var obscuration = Math.Clamp(definition.PupilObscuration, 0, 0.95);
        IReadOnlyList<Raytrace.PupilSample> samples;
        if (string.Equals(definition.PupilSampling, "gaussian_quad", StringComparison.OrdinalIgnoreCase))
        {
            var radialSamples = GaussianRadialSamples(rings, obscuration);
            samples = radialSamples
                .SelectMany(radialSample => Enumerable.Range(0, arms).Select(index =>
                {
                    var angle = 2 * Math.PI * (index + 1) / arms;
                    return new Raytrace.PupilSample(
                        radialSample.Radius * Math.Cos(angle),
                        radialSample.Radius * Math.Sin(angle),
                        radialSample.Weight / arms);
                }))
                .ToArray();
        }
        else if (string.Equals(definition.PupilSampling, "uniform", StringComparison.OrdinalIgnoreCase))
        {
            var side = (rings * 2) + 1;
            samples = Enumerable.Range(0, side)
                .SelectMany(y => Enumerable.Range(0, side).Select(x => new Raytrace.PupilSample(
                    side == 1 ? 0 : -1 + (2.0 * x / (side - 1)),
                    side == 1 ? 0 : -1 + (2.0 * y / (side - 1)),
                    1)))
                .Where(sample =>
                {
                    var radiusSquared = (sample.X * sample.X) + (sample.Y * sample.Y);
                    return radiusSquared <= 1.0 + 1e-12
                        && radiusSquared >= (obscuration * obscuration) - 1e-12;
                })
                .ToArray();
        }
        else
        {
            var generated = new List<Raytrace.PupilSample>();
            if (obscuration <= 1e-12)
            {
                generated.Add(new Raytrace.PupilSample(0, 0, 1));
            }

            for (var ring = 1; ring <= rings; ring++)
            {
                var radius = ring / (double)rings;
                if (radius + 1e-12 < obscuration)
                {
                    continue;
                }

                var points = ring * arms;
                for (var index = 0; index < points; index++)
                {
                    var angle = 2 * Math.PI * index / points;
                    generated.Add(new Raytrace.PupilSample(
                        radius * Math.Cos(angle),
                        radius * Math.Sin(angle),
                        1));
                }
            }

            samples = generated;
        }

        return samples.Count > 0
            ? samples
            : Raytrace.ApertureSampler.Generate(fallbackSampleCount, Raytrace.PupilSampling.Hexapolar);
    }

    private static (double X, double Y) ResolveNormalizedField(
        Optic optic,
        MeritOperandDefinition definition)
    {
        if (Math.Abs(definition.Hx) > 1e-12 || Math.Abs(definition.Hy) > 1e-12)
        {
            return (Math.Clamp(definition.Hx, -1, 1), Math.Clamp(definition.Hy, -1, 1));
        }

        var field = ResolveField(optic, definition.Field);
        return FieldCoordinates.Normalize(optic.Fields, field.X, field.Y);
    }

    private static FieldPoint ResolveField(Optic optic, int oneBasedIndex)
    {
        if (optic.Fields.Count == 0)
        {
            throw new InvalidOperationException("系统没有视场。");
        }

        var index = oneBasedIndex <= 0 ? 0 : oneBasedIndex - 1;
        return optic.Fields[Math.Clamp(index, 0, optic.Fields.Count - 1)];
    }

    private static Wavelength ResolveWavelength(Optic optic, int oneBasedIndex)
    {
        if (optic.Wavelengths.Count == 0)
        {
            throw new InvalidOperationException("系统没有波长。");
        }

        if (oneBasedIndex <= 0)
        {
            return optic.Wavelengths.FirstOrDefault(wavelength => wavelength.IsPrimary)
                ?? optic.Wavelengths[0];
        }

        return optic.Wavelengths[Math.Clamp(oneBasedIndex - 1, 0, optic.Wavelengths.Count - 1)];
    }

    private static OpticalSurface ResolveSurface(Optic optic, int surfaceNumber)
    {
        return optic.SurfaceGroup.Items.FirstOrDefault(surface => surface.Number == surfaceNumber)
            ?? throw new ArgumentOutOfRangeException(nameof(surfaceNumber), "找不到指定表面。");
    }

    private static double[] NormalizeWeights(IReadOnlyList<double> weights)
    {
        var sum = weights.Sum(weight => Math.Max(0, weight));
        return sum <= 1e-12
            ? Enumerable.Repeat(1.0 / Math.Max(1, weights.Count), weights.Count).ToArray()
            : weights.Select(weight => Math.Max(0, weight) / sum).ToArray();
    }
}

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
        PupilSampling = PupilSampling
    };
}

public enum MeritImageQuality
{
    RmsSpot,
    RmsWavefront
}

public enum MeritPupilSampling
{
    GaussianQuadrature,
    RectangularArray
}

public sealed record MeritFunctionWizardSettings(
    MeritImageQuality ImageQuality,
    MeritPupilSampling PupilSampling,
    int PupilRings,
    int PupilArms,
    double PupilObscuration,
    double WeightScale,
    bool UseAllWavelengths,
    bool IncludeCommonOperands);

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
    public static IReadOnlyList<MeritOperandType> Types { get; } = new[]
    {
        new MeritOperandType("DMFS", "默认评价函数设置", "默认评价函数向导生成的说明行"),
        new MeritOperandType("BLNK", "空白/注释", "不参与评价函数计算"),
        new MeritOperandType("RSCE", "RMS 点列半径", "指定视场和波长的 RMS 点列半径"),
        new MeritOperandType("RWFE", "RMS 波前差", "指定视场和波长的 RMS 光程差"),
        new MeritOperandType("OPDX", "光程差", "指定视场、波长和瞳孔坐标的光程差（波数）"),
        new MeritOperandType("REAX", "实际光线 X", "指定光线在表面上的 X 坐标"),
        new MeritOperandType("REAY", "实际光线 Y", "指定光线在表面上的 Y 坐标"),
        new MeritOperandType("EFFL", "有效焦距", "系统有效焦距"),
        new MeritOperandType("FNUM", "像方 F 数", "系统像方 F 数"),
        new MeritOperandType("TOTR", "系统总长", "系统总光程长度"),
        new MeritOperandType("RADI", "表面曲率半径", "指定表面的曲率半径"),
        new MeritOperandType("THIC", "表面厚度", "指定表面后的轴向厚度")
    };

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

            var residual = (value - definition.Target) * definition.Weight;
            return new MeritOperandEvaluation(value, residual * residual);
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
        var operands = new List<MeritOperandDefinition>
        {
            new()
            {
                Enabled = false,
                Type = "DMFS",
                Comment = "序列评价函数：RMS 点列半径"
            }
        };
        var fieldWeights = NormalizeWeights(optic.Fields.Select(field => field.Weight).ToArray());
        var wavelengthWeights = NormalizeWeights(optic.Wavelengths.Select(wavelength => wavelength.Weight).ToArray());
        for (var field = 0; field < optic.Fields.Count; field++)
        {
            for (var wavelength = 0; wavelength < optic.Wavelengths.Count; wavelength++)
            {
                operands.Add(new MeritOperandDefinition
                {
                    Type = "RSCE",
                    Field = field + 1,
                    Wavelength = wavelength + 1,
                    Target = 0,
                    Weight = Math.Sqrt(fieldWeights[field] * wavelengthWeights[wavelength]),
                    Comment = $"{optic.Fields[field].Label} · {optic.Wavelengths[wavelength].Label}"
                });
            }
        }

        return operands;
    }

    public static IReadOnlyList<MeritOperandDefinition> CreateDefaultRmsWavefront(Optic optic)
    {
        var operands = new List<MeritOperandDefinition>
        {
            new()
            {
                Enabled = false,
                Type = "DMFS",
                Comment = "序列评价函数：RMS 波前差"
            }
        };
        var primaryIndex = optic.Wavelengths
            .Select((wavelength, index) => (wavelength, index))
            .FirstOrDefault(item => item.wavelength.IsPrimary).index;
        var pupilSamples = new[]
        {
            (0.333, 0.0), (0.0, 0.333), (-0.333, 0.0), (0.0, -0.333),
            (0.667, 0.0), (0.0, 0.667), (-0.667, 0.0), (0.0, -0.667),
            (0.95, 0.0), (0.0, 0.95), (-0.95, 0.0), (0.0, -0.95)
        };
        var fieldWeights = NormalizeWeights(optic.Fields.Select(field => field.Weight).ToArray());
        for (var field = 0; field < optic.Fields.Count; field++)
        {
            operands.Add(new MeritOperandDefinition
            {
                Enabled = false,
                Type = "BLNK",
                Comment = $"视场操作数 {field + 1}：{optic.Fields[field].Label}"
            });
            foreach (var (px, py) in pupilSamples)
            {
                operands.Add(new MeritOperandDefinition
                {
                    Type = "OPDX",
                    Field = field + 1,
                    Wavelength = primaryIndex + 1,
                    Px = px,
                    Py = py,
                    Target = 0,
                    Weight = Math.Sqrt(fieldWeights[field] / pupilSamples.Length)
                });
            }
        }

        return operands;
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
            : "hexapolar";
        var operands = new List<MeritOperandDefinition>
        {
            new()
            {
                Enabled = false,
                Type = "DMFS",
                Comment = settings.ImageQuality == MeritImageQuality.RmsWavefront
                    ? $"优化向导：RMS 波前差，{rings} 环 {arms} 臂"
                    : $"优化向导：RMS 点列半径，{rings} 环 {arms} 臂"
            }
        };

        var fieldWeights = NormalizeWeights(optic.Fields.Select(field => field.Weight).ToArray());
        var wavelengthIndices = settings.UseAllWavelengths
            ? Enumerable.Range(0, optic.Wavelengths.Count).ToArray()
            : new[]
            {
                optic.Wavelengths
                    .Select((wavelength, index) => (wavelength, index))
                    .FirstOrDefault(item => item.wavelength.IsPrimary).index
            };
        var wavelengthWeights = NormalizeWeights(
            wavelengthIndices.Select(index => optic.Wavelengths[index].Weight).ToArray());

        for (var field = 0; field < optic.Fields.Count; field++)
        {
            for (var wavelengthOffset = 0; wavelengthOffset < wavelengthIndices.Length; wavelengthOffset++)
            {
                var wavelength = wavelengthIndices[wavelengthOffset];
                operands.Add(new MeritOperandDefinition
                {
                    Type = settings.ImageQuality == MeritImageQuality.RmsWavefront ? "RWFE" : "RSCE",
                    Field = field + 1,
                    Wavelength = wavelength + 1,
                    Target = 0,
                    Weight = weightScale * Math.Sqrt(fieldWeights[field] * wavelengthWeights[wavelengthOffset]),
                    Comment = $"{optic.Fields[field].Label} · {optic.Wavelengths[wavelength].Label}",
                    PupilRings = rings,
                    PupilArms = arms,
                    PupilObscuration = obscuration,
                    PupilSampling = samplingName
                });
            }
        }

        if (settings.IncludeCommonOperands)
        {
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

        return operands;
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
            "RWFE" => EvaluateRmsWavefront(optic, definition),
            "OPDX" => EvaluateOpticalPathDifference(optic, definition),
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
        var trace = TraceBundle(optic, definition, 37);
        var samples = trace.RayHistories
            .Where(history => history.Count > 0)
            .Select(history => history[^1])
            .Where(sample => !sample.Vignetted && sample.Intensity > 0)
            .ToArray();
        if (samples.Length == 0)
        {
            throw new InvalidOperationException("没有有效光线。");
        }

        var totalWeight = samples.Sum(sample => sample.Intensity);
        var centroidX = samples.Sum(sample => sample.Position.X * sample.Intensity) / totalWeight;
        var centroidY = samples.Sum(sample => sample.Position.Y * sample.Intensity) / totalWeight;
        return Math.Sqrt(samples.Sum(sample =>
            (((sample.Position.X - centroidX) * (sample.Position.X - centroidX))
             + ((sample.Position.Y - centroidY) * (sample.Position.Y - centroidY))) * sample.Intensity) / totalWeight);
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
        var chiefDefinition = definition.Clone();
        chiefDefinition.Px = 0;
        chiefDefinition.Py = 0;
        var chief = SampleAtSurface(optic, chiefDefinition);
        var wavelengthMillimeters = ResolveWavelength(optic, definition.Wavelength).Micrometers / 1000.0;
        return (sample.CumulativeOpticalPathLength - chief.CumulativeOpticalPathLength)
            / Math.Max(1e-12, wavelengthMillimeters);
    }

    private static RayTraceSample SampleAtSurface(Optic optic, MeritOperandDefinition definition)
    {
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

        if (definition.Surface <= 0)
        {
            return history[^1];
        }

        return history.LastOrDefault(sample => sample.SurfaceNumber == definition.Surface)
            ?? throw new ArgumentOutOfRangeException(nameof(definition.Surface), "指定表面没有光线数据。");
    }

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
        if (string.Equals(definition.PupilSampling, "uniform", StringComparison.OrdinalIgnoreCase))
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

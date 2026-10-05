using OptilandWorkbench.Core.Capabilities;
using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Core.Services;

/// <summary>
/// Fits the four cardinal marginal rays in the nominal pupil through physical apertures.
/// Uses the formal ray generator and surface tracer, on an isolated evaluation copy.
/// This is not an all-pupil transmission guarantee or a native sampling equivalence claim.
/// </summary>
public static partial class VignettingSolver
{
    public static IReadOnlyList<PupilVignetting> Calculate(Optic optic, int precision = 0)
    {
        ArgumentNullException.ThrowIfNull(optic);
        ComputationCancellation.ThrowIfCancellationRequested();
        if (precision is < 0 or > 2) throw new ArgumentOutOfRangeException(nameof(precision), "SVIG Precision 必须为 0（高）、1（中）或 2（低）。");
        OpticCapabilityPreflight.EnsureSupported(optic, OpticCapabilityOperation.RayTrace);
        if (optic.Fields.Count == 0 || optic.SurfaceGroup.Items.Count < 2)
            throw new InvalidOperationException("自动渐晕需要已定义的视场、物面和像面。");
        if (optic.SurfaceGroup.Items.Any(surface => surface.ScatteringModel is not null))
            throw new NotSupportedException("自动渐晕暂不支持带散射模型的系统。");
        var primary = optic.Wavelengths.Where(wave => wave.IsPrimary).ToArray();
        if (primary.Length != 1) throw new InvalidOperationException("自动渐晕需要唯一的主波长。");
        var wavelength = primary[0];
        var working = optic.CreateMeritEvaluationCopy();
        var angles = working.Fields.Select(field => field.VignetteAngleDegrees).ToArray();
        foreach (var field in working.Fields) PupilVignetting.Clear(field);
        var result = new PupilVignetting[working.Fields.Count];
        for (var index = 0; index < result.Length; index++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var field = working.Fields[index];
            var normalized = FieldCoordinates.Normalize(working.Fields, field.X, field.Y);
            var generate = working.SequentialRayTracer.RayGenerator.CreatePupilRaySampler(
                normalized.X, normalized.Y, wavelength.Micrometers, working.RayAimingEnabled);
            var (sin, cos) = Math.SinCos((angles[index] % 360) * (Math.PI / 180));
            bool Transmits(double x, double y)
            {
                try
                {
                    var ray = generate(x * cos - y * sin, x * sin + y * cos);
                    return working.SequentialRayTracer.DiagnoseApertures(ray,
                        ComputationCancellation.Current, stopAtIncidentImage: true).InsideAllApertures;
                }
                catch (InvalidOperationException) { return false; }
            }
            try { result[index] = new PupilSearch(Transmits, precision).Solve(angles[index]); }
            catch (InvalidOperationException error)
            { throw new InvalidOperationException($"视场 {index + 1} 自动渐晕失败：{error.Message}", error); }
        }
        return result;
    }

    public static void Apply(Optic optic, int precision = 0) => Apply(optic, Calculate(optic, precision));

    internal static void Apply(Optic optic, IReadOnlyList<PupilVignetting> factors)
    {
        if (factors.Count != optic.Fields.Count) throw new ArgumentException("渐晕结果与视场数量不一致。");
        // No destination mutation until every field has a validated result.
        optic.InvalidateRayTraceCache();
        for (var index = 0; index < factors.Count; index++)
        {
            var field = optic.Fields[index]; var value = factors[index];
            field.VignetteDecenterX = value.DecenterX; field.VignetteDecenterY = value.DecenterY;
            field.VignetteFactorX = value.CompressionX; field.VignetteFactorY = value.CompressionY;
            field.VignetteAngleDegrees = value.AngleDegrees;
        }
    }
}

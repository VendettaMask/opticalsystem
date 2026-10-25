using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Raytrace;

namespace OpticalSystemDesign.Core.Differentiation;

public sealed record FixedInputRayField(int Ring, int Arm, int Wave, double IdealX, double IdealY, IReadOnlyList<RealRay> Rays);
public sealed record FixedInputRaySet(IReadOnlyList<FixedInputRayField> Fields);
public sealed record FixedInputSpotRay(DifferentialScalar X, DifferentialScalar Y, DifferentialScalar DirectionZ,
    DifferentialScalar BendPenalty, bool Valid);
public sealed record FixedInputSpotField(FixedInputRayField Input, IReadOnlyList<FixedInputSpotRay> Rays);
public sealed record FixedInputOpticalResult(IReadOnlyList<FixedInputSpotField> Fields, DesignComputeExecution Execution);

/// <summary>Finite-object sampling and derivatives with immutable input rays, without differentiating a pupil aim.
/// Invalid physical rays have an explicit mask; they never become throughput or acceptance evidence.</summary>
public static class FixedInputOpticalMetrics
{
    public const long MaximumDerivativeValues = 64_000_000;

    public static FixedInputRaySet SampleRingArm(Optic optic, double halfFieldRadians, double referenceFocalLength,
        int rings, int arms, int samplesPerField, double pupilScale, ulong seed, bool moreOffAxis = true,
        bool meridional = false, bool primaryOnly = false, double minimumHalfFieldRadians = 0)
    {
        if (ObjectConjugate.IsInfinite(optic.SurfaceGroup.Items[0]) || rings is < 2 or > 101 || arms is < 1 or > 16
            || samplesPerField is < 1 or > 4096 || !double.IsFinite(halfFieldRadians) || halfFieldRadians < 0
            || halfFieldRadians >= Math.PI / 2 || !double.IsFinite(referenceFocalLength) || referenceFocalLength <= 0
            || !double.IsFinite(minimumHalfFieldRadians) || minimumHalfFieldRadians < 0 || minimumHalfFieldRadians > halfFieldRadians
            || !double.IsFinite(pupilScale) || pupilScale <= 0 || pupilScale > 2)
            throw new ArgumentException("Invalid finite-conjugate field/pupil sampling request.");
        var depth = optic.SurfaceGroup.Items[1].CoordinateSystem.Origin.Z - optic.SurfaceGroup.Items[0].CoordinateSystem.Origin.Z;
        var pupilZ = optic.Paraxial.EstimateEntrancePupilLocation();
        var pupilRadius = optic.Paraxial.EstimateEntrancePupilDiameter() * pupilScale / 2;
        if (!(depth > 0) || !double.IsFinite(pupilZ) || !double.IsFinite(pupilRadius) || pupilRadius <= 0)
            throw new ArithmeticException("A finite entrance pupil and object distance are required.");
        var waves = optic.Wavelengths.Where(wave => wave.Weight > 0 && (!primaryOnly || wave.IsPrimary)).ToArray();
        if ((long)rings * arms * samplesPerField * waves.Length > SequentialTraceLimits.MaximumRayCount)
            throw new ArgumentException("The finite-conjugate ray set exceeds the bounded Core ray capacity.");
        var random = new DesignSampleRandom(seed);
        var fields = new List<FixedInputRayField>();
        for (var wave = 0; wave < waves.Length; wave++)
            for (var ring = 0; ring < rings; ring++)
                for (var arm = 0; arm < arms; arm++)
                {
                    var beta = (double)ring / (rings - 1);
                    if (moreOffAxis) beta = Math.Sqrt(beta);
                    var height = Math.Tan(minimumHalfFieldRadians + beta * (halfFieldRadians - minimumHalfFieldRadians));
                    var angle = meridional ? Math.PI / 2 : 2 * Math.PI * arm / arms;
                    var hx = height * Math.Cos(angle);
                    var hy = height * Math.Sin(angle);
                    var origin = new Vector3D(-depth * hx, -depth * hy, optic.SurfaceGroup.Items[0].CoordinateSystem.Origin.Z);
                    // Match the unstratified area-uniform distribution and independent draws for every field and colour.
                    var angles = Enumerable.Range(0, samplesPerField).Select(_ => 2 * Math.PI * random.Next()).ToArray();
                    var rays = new RealRay[samplesPerField];
                    for (var sample = 0; sample < samplesPerField; sample++)
                    {
                        var radius = pupilRadius * Math.Sqrt(random.Next());
                        var pupil = new Vector3D(radius * Math.Cos(angles[sample]), radius * Math.Sin(angles[sample]), pupilZ);
                        rays[sample] = new RealRay(origin, pupil - origin, waves[wave].Nanometers).Normalize();
                    }
                    fields.Add(new(ring, arm, wave, referenceFocalLength * hx, referenceFocalLength * hy, rays));
                }
        return new(fields);
    }

    public static FixedInputOpticalResult Evaluate(Optic optic, IReadOnlyList<OpticalDesignParameter> parameters,
        FixedInputRaySet inputs, double bendCosineGate, DesignComputeMode mode, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(mode) || !double.IsFinite(bendCosineGate) || bendCosineGate is < -1 or > 1)
            throw new ArgumentException("Invalid fixed-input differentiation settings.");
        if (inputs.Fields.Count is < 1 or > 2048 || inputs.Fields.Any(field => field.Rays.Count is < 1 or > 4096)
            || inputs.Fields.Sum(field => (long)field.Rays.Count) > SequentialTraceLimits.MaximumRayCount
            || inputs.Fields.Sum(field => (long)field.Rays.Count) * parameters.Count * 4 > MaximumDerivativeValues)
            throw new ArgumentException("The fixed-input derivative request exceeds the bounded ray capacity.");
        using var session = SequentialGpuSession.Current is null || SequentialGpuSession.Current.Mode != mode
            ? new SequentialGpuSession(mode, cancellationToken) : null;
        var gpu = mode != DesignComputeMode.Cpu && DesignGpu.Status.IsAvailable;
        if (mode == DesignComputeMode.Gpu && !gpu) throw new NotSupportedException("CUDA is unavailable: " + DesignGpu.Status.UnavailableReason);
        try
        {
            var result = EvaluateCore(optic, parameters, inputs, bendCosineGate, gpu, cancellationToken);
            return !gpu && mode == DesignComputeMode.Automatic
                ? result with { Execution = result.Execution with { FallbackReason = DesignGpu.Status.UnavailableReason } } : result;
        }
        catch (Exception error) when (mode == DesignComputeMode.Automatic
            && error is GpuTapeRangeException or ILGPU.Runtime.Cuda.CudaException or NotSupportedException)
        {
            return EvaluateCore(optic, parameters, inputs, bendCosineGate, false, cancellationToken)
                with { Execution = DesignComputeExecution.Cpu with { FallbackReason = error.Message } };
        }
    }

    private static FixedInputOpticalResult EvaluateCore(Optic optic, IReadOnlyList<OpticalDesignParameter> parameters,
        FixedInputRaySet inputs, double bendCosineGate, bool gpu, CancellationToken cancellationToken)
    {
        DifferentiableOpticalMetrics.EnsureSupported(optic, fixedInput: true);
        OpticalDesignParameters.Validate(optic, parameters);
        var context = new OpticalDifferentialContext(optic, parameters, gpu ? 2 : 0, analyticNormals: true);
        var stop = optic.SurfaceGroup.Items.ToList().FindIndex(surface => surface.IsStop);
        var batch = gpu ? new GpuRaySensitivityBatch(parameters.Count, cancellationToken) : null;
        var fields = new List<FixedInputSpotField>();
        foreach (var field in inputs.Fields)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var nominal = gpu ? optic.SequentialRayTracer.TraceGpuNominal(field.Rays, cancellationToken, ignoreApertures: false) : null;
            var output = new FixedInputSpotRay[field.Rays.Count];
            for (var index = 0; index < field.Rays.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var slot = index;
                try
                {
                    if (batch is null) Complete(optic.SequentialRayTracer.TraceDesignSensitivity(field.Rays[index], context,
                        stop, 0, 0, cancellationToken, constrainStop: false, bendCosineGate: bendCosineGate));
                    else
                    {
                        var tape = new DifferentialTape(parameters.Count);
                        DesignRaySensitivity ray;
                        using (tape) ray = optic.SequentialRayTracer.TraceDesignSensitivity(field.Rays[index], context, stop,
                            0, 0, cancellationToken, nominal, index * optic.SurfaceGroup.Items.Count, false, bendCosineGate);
                        batch.Add(tape, ray, Complete);
                    }
                }
                catch (DesignRayUnavailableException) { output[slot] = new(0, 0, 0, 0, false); }
                void Complete(DesignRaySensitivity ray) => output[slot] = new(ray.X, ray.Y, ray.DirectionZ, ray.BendPenalty, true);
            }
            batch?.Flush();
            fields.Add(new(field, output));
        }
        return new(fields, batch is { Batches: > 0 }
            ? new("cuda-reverse-tape-v1", DesignGpu.Status.DeviceName, batch.Batches, batch.Rays, batch.Milliseconds)
            : DesignComputeExecution.Cpu);
    }
}

/// <summary>Explicit stable stream: storage/resume does not depend on System.Random or a device's hidden RNG state.</summary>
public sealed class DesignSampleRandom(ulong seed)
{
    private ulong _state = seed;
    public double Next()
    {
        var value = (_state += 0x9e3779b97f4a7c15UL);
        value = (value ^ (value >> 30)) * 0xbf58476d1ce4e5b9UL;
        value = (value ^ (value >> 27)) * 0x94d049bb133111ebUL;
        return ((value ^ (value >> 31)) >> 11) * (1.0 / 9007199254740992.0);
    }
}

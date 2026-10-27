using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Propagation;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Differentiation;

public sealed record DifferentiableSpotRay(DifferentialScalar X, DifferentialScalar Y, double Weight,
    DifferentialScalar DirectionZ, DifferentialScalar MinimumClearanceMillimeters, DifferentialScalar MinimumBendCosine);
public sealed record DifferentiableSpotField(double NormalizedFieldY, DifferentialScalar CentroidX, DifferentialScalar CentroidY,
    DifferentialScalar RmsRadiusMillimeters, DifferentialScalar MaximumRadiusMillimeters, IReadOnlyList<DifferentiableSpotRay> Rays)
{
    public DifferentialScalar ChiefRayDirectionZ { get; init; }
    public double NormalizedFieldX { get; init; }
    public DifferentialScalar DistortionSquaredFraction { get; init; }
}
public sealed record DifferentiableOpticalResult(DifferentialScalar OpticalPowerPerMillimeter,
    IReadOnlyList<DifferentiableSpotField> Fields, long TracedRayCount)
{
    public DesignComputeExecution Execution { get; init; } = DesignComputeExecution.Cpu;
    public DifferentialScalar EntrancePupilDiameterMillimeters { get; init; }
    public DifferentialScalar InverseFNumber { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public SequentialGpuExecution? PropagationExecution { get; init; }
}

/// <summary>
/// Differentiates shared Core's centered, infinite-conjugate, refractive design path.
/// This is a training diagnostic: unclipped coordinates never certify physical throughput.
/// </summary>
public static class DifferentiableOpticalMetrics
{
    /// <summary>Maximum over integration and independent check rays about the integration centroid.
    /// Check rays never change its area integral. Unclipped design rays do not certify physical throughput.</summary>
    public static DifferentialScalar MaximumRadiusWithChecks(DifferentiableSpotField integration, DifferentiableSpotField checks)
    {
        ArgumentNullException.ThrowIfNull(integration);
        ArgumentNullException.ThrowIfNull(checks);
        if (integration.NormalizedFieldX != checks.NormalizedFieldX || integration.NormalizedFieldY != checks.NormalizedFieldY
            || integration.Rays.Count == 0 || checks.Rays.Count == 0)
            throw new ArgumentException("Integration and check rays must refer to the same nonempty field.");
        DifferentialScalar maximum = 0;
        foreach (var ray in integration.Rays.Concat(checks.Rays))
        {
            var squared = DifferentialScalar.Square(ray.X - integration.CentroidX) + DifferentialScalar.Square(ray.Y - integration.CentroidY);
            if (!squared.IsFinite) throw new ArithmeticException("A common-centroid check radius is unavailable.");
            if (squared.Value > maximum.Value) maximum = squared;
        }
        return DifferentialScalar.Sqrt(maximum);
    }

    public static DifferentiableOpticalResult Evaluate(Optic optic, IReadOnlyList<OpticalDesignParameter> parameters,
        IReadOnlyList<double> normalizedFields, IReadOnlyList<PupilSample> pupils, CancellationToken cancellationToken = default,
        DesignComputeMode computeMode = DesignComputeMode.Cpu)
        => EvaluateAtFields(optic, parameters, normalizedFields.Select(field => (0.0, field)).ToArray(), pupils,
            cancellationToken, computeMode);

    /// <summary>Formal implicit stop aiming at explicit two-dimensional fields; fixed-stop sizing is opt-in.</summary>
    public static DifferentiableOpticalResult EvaluateAtFields(Optic optic, IReadOnlyList<OpticalDesignParameter> parameters,
        IReadOnlyList<(double X, double Y)> normalizedFields, IReadOnlyList<PupilSample> pupils, CancellationToken cancellationToken = default,
        DesignComputeMode computeMode = DesignComputeMode.Cpu, bool usePhysicalStop = false)
    {
        if (!Enum.IsDefined(computeMode)) throw new ArgumentOutOfRangeException(nameof(computeMode));
        cancellationToken.ThrowIfCancellationRequested();
        using var ownedCompute = SequentialGpuSession.Current is null || SequentialGpuSession.Current.Mode != computeMode
            ? new SequentialGpuSession(computeMode, cancellationToken) : null;
        if (computeMode == DesignComputeMode.Cpu) return EvaluateCore(optic, parameters, normalizedFields, pupils, cancellationToken, false, usePhysicalStop);
        var status = DesignGpu.Status;
        if (!status.IsAvailable)
        {
            if (computeMode == DesignComputeMode.Gpu) throw new NotSupportedException("CUDA is unavailable: " + status.UnavailableReason);
            return EvaluateCore(optic, parameters, normalizedFields, pupils, cancellationToken, false, usePhysicalStop)
                with
            { Execution = DesignComputeExecution.Cpu with { FallbackReason = status.UnavailableReason } };
        }
        using var executionMeasurement = SequentialTraceMeasurement.Begin();
        try { return EvaluateCore(optic, parameters, normalizedFields, pupils, cancellationToken, true, usePhysicalStop); }
        catch (Exception error) when (computeMode == DesignComputeMode.Automatic
            && error is GpuTapeRangeException or ILGPU.Runtime.Cuda.CudaException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var fallbackCompute = new SequentialGpuSession(DesignComputeMode.Cpu, cancellationToken);
            var result = EvaluateCore(optic, parameters, normalizedFields, pupils, cancellationToken, false, usePhysicalStop);
            return result with
            {
                TracedRayCount = executionMeasurement.RayCount,
                Execution = DesignComputeExecution.Cpu with { FallbackReason = error.Message }
            };
        }
        catch (ILGPU.Runtime.Cuda.CudaException error)
        {
            throw new NotSupportedException("CUDA computation failed; select Automatic or CPU. " + error.Message, error);
        }
    }

    private static DifferentiableOpticalResult EvaluateCore(Optic optic, IReadOnlyList<OpticalDesignParameter> parameters,
        IReadOnlyList<(double X, double Y)> normalizedFields, IReadOnlyList<PupilSample> pupils, CancellationToken cancellationToken, bool gpu, bool usePhysicalStop)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(normalizedFields);
        ArgumentNullException.ThrowIfNull(pupils);
        EnsureSupported(optic, physicalStop: usePhysicalStop);
        OpticalDesignParameters.Validate(optic, parameters);
        if (normalizedFields.Count is < 1 or > 101 || normalizedFields.Any(field => !double.IsFinite(field.X) || !double.IsFinite(field.Y) || double.Hypot(field.X, field.Y) > 1 + 1e-12)
            || pupils.Count is < 1 or > 4096 || pupils.Any(pupil => !double.IsFinite(pupil.X) || !double.IsFinite(pupil.Y)
                || pupil.X * pupil.X + pupil.Y * pupil.Y > 1 + 1e-12 || !double.IsFinite(pupil.Weight) || pupil.Weight <= 0))
            throw new ArgumentException("Invalid differentiable field or pupil quadrature.");
        var requestedRays = (long)normalizedFields.Count * (pupils.Count * (long)optic.Wavelengths.Count(wave => wave.Weight > 0) + 1);
        if (requestedRays > SequentialTraceLimits.MaximumRayCount || requestedRays * Math.Max(1, parameters.Count) * 5 > 8_000_000)
            throw new ArgumentException("The requested automatic-tangent working set exceeds the bounded design limit; reduce fields, pupil samples, wavelengths or parameters.");
        using var measurement = SequentialTraceMeasurement.Begin();
        var context = new OpticalDifferentialContext(optic, parameters, 2, analyticNormals: usePhysicalStop);
        var paraxial = context.Paraxial();
        var stopIndex = optic.SurfaceGroup.Items.ToList().FindIndex(surface => surface.IsStop);
        var targets = pupils.Select(pupil => (X: paraxial.StopHeight.Value * pupil.X, Y: paraxial.StopHeight.Value * pupil.Y)).ToArray();
        var fields = new List<DifferentiableSpotField>();
        using var batch = gpu ? new GpuRaySensitivityBatch(parameters.Count, cancellationToken) : null;
        foreach (var field in normalizedFields)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rays = new List<DifferentiableSpotRay>();
            DifferentialScalar chiefDirection = double.NaN;
            DifferentialScalar chiefX = double.NaN, chiefY = double.NaN;
            foreach (var wave in optic.Wavelengths.Where(wave => wave.Weight > 0))
            {
                var bundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(field.X, field.Y,
                    wave.Micrometers, pupils, aimAtStop: true, applyVignettingFactors: false, stopTargets: targets);
                var nominal = gpu ? optic.SequentialRayTracer.TraceGpuNominal(bundle.Rays, cancellationToken) : null;
                for (var index = 0; index < pupils.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!bundle.Rays[index].CanTrace || bundle.Rays[index].Intensity <= 0)
                        throw new ArithmeticException("Formal Core could not aim a differentiable pupil ray.");
                    var slot = rays.Count;
                    var rayWeight = pupils[index].Weight * wave.Weight;
                    rays.Add(null!);
                    Trace(bundle.Rays[index], paraxial.StopHeight * pupils[index].X, paraxial.StopHeight * pupils[index].Y,
                        ray => rays[slot] = new(ray.X, ray.Y, rayWeight, ray.DirectionZ, ray.MinimumClearance, ray.MinimumBendCosine),
                        nominal, index * optic.SurfaceGroup.Items.Count);
                }
                if (wave.IsPrimary)
                {
                    var chief = optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(field.X, field.Y,
                        wave.Micrometers, [new(0, 0, 1)], aimAtStop: true, applyVignettingFactors: false, stopTargets: [(0, 0)]).Rays[0];
                    if (!chief.CanTrace || chief.Intensity <= 0) throw new ArithmeticException("Formal Core could not aim the chief ray.");
                    var chiefNominal = gpu ? optic.SequentialRayTracer.TraceGpuNominal([chief], cancellationToken) : null;
                    Trace(chief, 0, 0, ray => { chiefDirection = ray.DirectionZ; chiefX = ray.X; chiefY = ray.Y; }, chiefNominal, 0);
                }
            }
            batch?.Flush();
            var weight = rays.Sum(ray => ray.Weight);
            if (!double.IsFinite(weight) || weight <= 0) throw new ArithmeticException("No positive spectral pupil weight.");
            DifferentialScalar cx = 0, cy = 0;
            foreach (var ray in rays) { cx += ray.X * ray.Weight / weight; cy += ray.Y * ray.Weight / weight; }
            DifferentialScalar energy = 0, maximum = 0;
            foreach (var ray in rays)
            {
                var radiusSquared = DifferentialScalar.Square(ray.X - cx) + DifferentialScalar.Square(ray.Y - cy);
                energy += radiusSquared * ray.Weight / weight;
                if (radiusSquared.Value > maximum.Value) maximum = radiusSquared;
            }
            DifferentialScalar distortionSquared = 0;
            if (usePhysicalStop && double.Hypot(field.X, field.Y) > 1e-12)
            {
                var unit = FirstOrderImagePoint(optic, 1, field.X, field.Y);
                var idealX = Trim(paraxial.ChiefImageScale) * unit.X;
                var idealY = Trim(paraxial.ChiefImageScale) * unit.Y;
                var norm = idealX * idealX + idealY * idealY;
                if (norm.Value <= 1e-30) throw new ArithmeticException("Chief distortion has no finite reference image height.");
                distortionSquared = (DifferentialScalar.Square(chiefX - idealX) + DifferentialScalar.Square(chiefY - idealY)) / norm;
            }
            fields.Add(new(field.Y, cx, cy, DifferentialScalar.Sqrt(energy), DifferentialScalar.Sqrt(maximum), rays)
            { ChiefRayDirectionZ = chiefDirection, NormalizedFieldX = field.X, DistortionSquaredFraction = distortionSquared });
        }
        var inverseFNumber = DifferentialScalar.Abs(paraxial.Power) * paraxial.EntrancePupilDiameter;
        var formalFNumber = optic.Paraxial.EstimateFNumber();
        // The formal report uses zero for undefined F number at exact zero power.
        // Its continuous inverse still has a finite training tangent there.
        if (formalFNumber > 0 && double.IsFinite(formalFNumber))
            OpticalDifferentialContext.Verify(inverseFNumber, 1 / formalFNumber, "inverse f-number");
        else if (paraxial.Power.Value == 0) OpticalDifferentialContext.Verify(inverseFNumber, 0, "zero-power inverse f-number");
        else throw new ArithmeticException("The formal f-number is unavailable for this nonzero-power system.");
        return new(Trim(paraxial.Power), fields, measurement.RayCount)
        {
            EntrancePupilDiameterMillimeters = Trim(paraxial.EntrancePupilDiameter),
            InverseFNumber = Trim(inverseFNumber),
            Execution = batch is null ? DesignComputeExecution.Cpu
                : new("cuda-reverse-tape-v1", DesignGpu.Status.DeviceName, batch.Batches, batch.Rays, batch.Milliseconds),
            PropagationExecution = gpu ? SequentialGpuSession.Current?.Execution : null
        };

        void Trace(Rays.RealRay source, DifferentialScalar targetX, DifferentialScalar targetY, Action<DesignRaySensitivity> complete,
            SequentialGpuSample[]? nominal, int offset)
        {
            if (batch is null)
                complete(optic.SequentialRayTracer.TraceDesignSensitivity(source, context, stopIndex, targetX, targetY, cancellationToken));
            else
            {
                var tape = DifferentialTape.Rent(parameters.Count);
                DesignRaySensitivity ray;
                try
                {
                    using (tape) ray = optic.SequentialRayTracer.TraceDesignSensitivity(source, context, stopIndex, targetX, targetY, cancellationToken, nominal, offset);
                }
                catch { tape.Release(); throw; }
                batch.Add(tape, ray, complete);
            }
        }

        DifferentialScalar Trim(DifferentialScalar value) =>
            DifferentialScalar.FromGradient(value.Value, Enumerable.Range(0, parameters.Count)
                .Select(index => value.Dimension == 0 ? 0 : value.Derivative(index)).ToArray());
    }

    public static DifferentialScalar SurfaceSag(Optic optic, IReadOnlyList<OpticalDesignParameter> parameters, int surfaceIndex, double radius,
        bool relaxedDomain = false, bool maskOutsideProfile = false)
    {
        if (maskOutsideProfile && !InsideProfile(optic, surfaceIndex, radius)) radius = 0;
        var result = new OpticalDifferentialContext(optic, parameters).Profile(surfaceIndex, radius, 0, relaxedDomain).Sag;
        if (!relaxedDomain) OpticalDifferentialContext.Verify(result, optic.SurfaceGroup.Items[surfaceIndex].Geometry.Sag(radius, 0), "surface sag");
        return result;
    }

    public static DifferentialScalar AxialSeparation(Optic optic, IReadOnlyList<OpticalDesignParameter> parameters,
        int firstSurface, int secondSurface, double radius, bool relaxedDomain = false, bool maskOutsideProfile = false)
    {
        var context = new OpticalDifferentialContext(optic, parameters);
        return context.Position(secondSurface) - context.Position(firstSurface)
            + context.Profile(secondSurface, maskOutsideProfile && !InsideProfile(optic, secondSurface, radius) ? 0 : radius, 0, relaxedDomain).Sag
            - context.Profile(firstSurface, maskOutsideProfile && !InsideProfile(optic, firstSurface, radius) ? 0 : radius, 0, relaxedDomain).Sag;
    }

    public static DifferentialScalar SurfaceSlope(Optic optic, IReadOnlyList<OpticalDesignParameter> parameters, int surfaceIndex, double radius,
        bool relaxedDomain = false, bool maskOutsideProfile = false)
    {
        if (maskOutsideProfile && !InsideProfile(optic, surfaceIndex, radius)) radius = 0;
        return 2 * radius * new OpticalDifferentialContext(optic, parameters).Profile(surfaceIndex, radius, 0, relaxedDomain).RadialSlope;
    }

    private static bool InsideProfile(Optic optic, int surfaceIndex, double radius)
    {
        var surface = optic.SurfaceGroup.Items[surfaceIndex];
        var profile = OpticalDesignParameters.Profile(surface.Geometry);
        return Math.Abs(radius) <= surface.SemiDiameter && (profile.Conic <= -1 || (1 + profile.Conic) * profile.Curvature * profile.Curvature * radius * radius < 1);
    }

    public static double FirstOrderImageHeight(Optic optic, double focalLength, double normalizedFieldY)
    {
        if (!double.IsFinite(focalLength) || focalLength <= 0 || !double.IsFinite(normalizedFieldY) || Math.Abs(normalizedFieldY) > 1)
            throw new ArgumentOutOfRangeException(nameof(focalLength));
        return focalLength * Math.Tan(normalizedFieldY * FieldCoordinates.MaximumRadius(optic.Fields) * Math.PI / 180);
    }

    public static (double X, double Y) FirstOrderImagePoint(Optic optic, double focalLength, double normalizedFieldX, double normalizedFieldY) =>
        (FirstOrderImageHeight(optic, focalLength, normalizedFieldX), FirstOrderImageHeight(optic, focalLength, normalizedFieldY));

    public static void EnsureSupported(Optic optic, bool fixedInput = false, bool physicalStop = false)
    {
        ArgumentNullException.ThrowIfNull(optic);
        var surfaces = optic.SurfaceGroup.Items;
        if (surfaces.Count < 3 || !fixedInput && !ObjectConjugate.IsInfinite(surfaces[0]) || optic.FieldDefinition != Domain.FieldDefinitionKind.Angle
            || optic.Aperture.Kind != (fixedInput || physicalStop ? ApertureKind.FloatByStopSize : ApertureKind.EntrancePupilDiameter) || !double.IsFinite(optic.Aperture.Value) || optic.Aperture.Value <= 0
            || optic.ObjectSpaceTelecentric || optic.FieldGroupTelecentric || optic.Apodization is not null
            || optic.Wavelengths.Count(wave => wave.IsPrimary) != 1 || !optic.Wavelengths.Any(wave => wave.IsPrimary && wave.Weight > 0)
            || surfaces.Count(surface => surface.IsStop) != 1 || surfaces[0].IsStop || surfaces[^1].IsStop)
            throw new NotSupportedException("Design derivatives require a centered infinite-object angle-field EPD system with one internal stop and no apodization.");
        foreach (var surface in surfaces)
        {
            var coordinates = surface.CoordinateSystem;
            if (coordinates.RotationXDegrees != 0 || coordinates.RotationYDegrees != 0 || coordinates.RotationZDegrees != 0
                || coordinates.Origin.X != 0 || coordinates.Origin.Y != 0
                || surface.Geometry is not (PlaneGeometry or StandardGeometry or EvenAsphereGeometry)
                || surface.Geometry is EvenAsphereGeometry asphere && asphere.Coefficients.Count > 0 && asphere.Coefficients[0] != 0
                || surface.InteractionModel is not RefractiveReflectiveInteractionModel { IsReflective: false }
                || surface.IsReflective || surface.ScatteringModel is not null
                || surface.MaterialBefore.PropagationModel is not HomogeneousPropagationModel
                || surface.MaterialAfter.PropagationModel is not HomogeneousPropagationModel
                || surface.PhysicalAperture is not (null or CircularAperture))
                throw new NotSupportedException("Design derivatives support only centered homogeneous refractive spherical/even-aspheric surfaces and circular apertures.");
        }
    }
}

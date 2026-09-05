using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Apodization;
using OptilandWorkbench.Core.Capabilities;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Rays;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Raytrace;

public sealed class RayGenerationSettings
{
    public int SamplesPerField { get; set; } = 9;

    public PupilSampling Sampling { get; set; } = PupilSampling.Hexapolar;
}

public sealed class RayGenerator
{
    private readonly Optic _optic;
    private const double NormalizedCoordinateTolerance = 1e-12;
    private sealed record FieldRayContext(
        double NormalizedFieldX,
        double NormalizedFieldY,
        double VignetteScaleX,
        double VignetteScaleY,
        double EntrancePupilGlobalZ,
        Vector3D BaseOrigin,
        bool TranslateOriginWithPupil);

    public RayGenerator(Optic optic)
    {
        _optic = optic;
    }

    public RayGenerationSettings Settings { get; } = new();

    public RealRayBundle Generate()
    {
        return GenerateFor(_optic.Fields, _optic.Wavelengths);
    }

    public RealRayBundle GenerateFor(
        FieldPoint field,
        bool applyFieldWeight = true,
        bool applyWavelengthWeight = true)
    {
        return GenerateFor(new[] { field }, _optic.Wavelengths, applyFieldWeight, applyWavelengthWeight);
    }

    public RealRayBundle GenerateFor(
        Wavelength wavelength,
        bool applyFieldWeight = true,
        bool applyWavelengthWeight = true)
    {
        return GenerateFor(_optic.Fields, new[] { wavelength }, applyFieldWeight, applyWavelengthWeight);
    }

    public RealRayBundle GenerateFor(
        IEnumerable<FieldPoint> fields,
        IEnumerable<Wavelength> wavelengths,
        bool applyFieldWeight = true,
        bool applyWavelengthWeight = true)
    {
        OpticCapabilityPreflight.EnsureSupported(_optic, OpticCapabilityOperation.RayTrace);
        var apertureRadius = EntrancePupilRadius();
        var samples = ApertureSampler.Generate(Settings.SamplesPerField, Settings.Sampling);
        var rays = new List<RealRay>();

        foreach (var field in fields)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var realImageLaunch = ResolveRealImageLaunch(field.X, field.Y);
            foreach (var wavelength in wavelengths)
            {
                ComputationCancellation.ThrowIfCancellationRequested();
                foreach (var sample in samples)
                {
                    ComputationCancellation.ThrowIfCancellationRequested();
                    var rayGeometry = CreateFieldRay(
                        field.X,
                        field.Y,
                        sample.X,
                        sample.Y,
                        apertureRadius,
                        applyVignetting: true,
                        realImageLaunch: realImageLaunch);
                    var fieldWeight = applyFieldWeight ? field.Weight : 1.0;
                    var wavelengthWeight = applyWavelengthWeight ? wavelength.Weight : 1.0;

                    var apodization = ApodizationIntensity(sample.X, sample.Y, apertureRadius);
                    rays.Add(new RealRay(
                        rayGeometry.Origin,
                        rayGeometry.Direction,
                        wavelength.Nanometers,
                        fieldWeight * wavelengthWeight * sample.Weight * apodization));
                }
            }
        }

        return new RealRayBundle(rays);
    }

    public RealRayBundle GenerateNormalized(
        double normalizedFieldX,
        double normalizedFieldY,
        double wavelengthMicrometers,
        int sampleCount,
        string distribution)
    {
        OpticCapabilityPreflight.EnsureSupported(_optic, OpticCapabilityOperation.RayTrace);
        ValidateNormalized(normalizedFieldX, nameof(normalizedFieldX));
        ValidateNormalized(normalizedFieldY, nameof(normalizedFieldY));
        var sampling = ParseSampling(distribution);
        var apertureRadius = EntrancePupilRadius();
        var field = NormalizedFieldToValues(normalizedFieldX, normalizedFieldY);
        var realImageLaunch = ResolveRealImageLaunch(field.X, field.Y);
        var wavelengthNanometers = MicrometersToNanometers(wavelengthMicrometers);
        var rays = ApertureSampler.Generate(sampleCount, sampling)
            .Select(sample => CreateRay(
                field.X,
                field.Y,
                sample.X,
                sample.Y,
                apertureRadius,
                wavelengthNanometers,
                sample.Weight,
                realImageLaunch))
            .ToArray();

        return new RealRayBundle(rays);
    }

    public RealRayBundle GenerateGeneric(
        double normalizedFieldX,
        double normalizedFieldY,
        double normalizedPupilX,
        double normalizedPupilY,
        double wavelengthMicrometers,
        bool aimAtStop = false,
        (double X, double Y)? resolvedRealImageLaunch = null,
        bool allowOutsideUnitPupil = false)
    {
        OpticCapabilityPreflight.EnsureSupported(_optic, OpticCapabilityOperation.RayTrace);
        ValidateNormalized(normalizedFieldX, nameof(normalizedFieldX));
        ValidateNormalized(normalizedFieldY, nameof(normalizedFieldY));
        ValidateNormalized(normalizedPupilX, nameof(normalizedPupilX));
        ValidateNormalized(normalizedPupilY, nameof(normalizedPupilY));
        if (!allowOutsideUnitPupil
            && (normalizedPupilX * normalizedPupilX) + (normalizedPupilY * normalizedPupilY) > 1.0 + NormalizedCoordinateTolerance)
        {
            throw new ArgumentOutOfRangeException(nameof(normalizedPupilX), "Normalized pupil coordinates must lie inside the unit pupil.");
        }

        var apertureRadius = EntrancePupilRadius();
        var field = NormalizedFieldToValues(normalizedFieldX, normalizedFieldY);
        var realImageLaunch = resolvedRealImageLaunch
            ?? ResolveRealImageLaunch(field.X, field.Y, aimAtStop);
        var vignetteScale = VignetteScale(normalizedFieldX, normalizedFieldY);
        var ray = CreateRay(
            field.X,
            field.Y,
            normalizedPupilX * vignetteScale.X,
            normalizedPupilY * vignetteScale.Y,
            apertureRadius,
            MicrometersToNanometers(wavelengthMicrometers),
            intensity: 1.0,
            realImageLaunch,
            aimAtStop);
        return new RealRayBundle(new[] { ray });
    }

    public RealRayBundle GenerateNormalizedPupilSamples(
        double normalizedFieldX,
        double normalizedFieldY,
        double wavelengthMicrometers,
        IEnumerable<PupilSample> pupilSamples,
        bool aimAtStop = false,
        (double X, double Y)? resolvedRealImageLaunch = null,
        bool applyVignettingFactors = true,
        IReadOnlyList<(double X, double Y)>? stopTargets = null)
    {
        OpticCapabilityPreflight.EnsureSupported(_optic, OpticCapabilityOperation.RayTrace);
        ValidateNormalized(normalizedFieldX, nameof(normalizedFieldX));
        ValidateNormalized(normalizedFieldY, nameof(normalizedFieldY));
        var field = NormalizedFieldToValues(normalizedFieldX, normalizedFieldY);
        var realImageLaunch = resolvedRealImageLaunch
            ?? ResolveRealImageLaunch(field.X, field.Y, aimAtStop);
        var apertureRadius = EntrancePupilRadius();
        var wavelengthNanometers = MicrometersToNanometers(wavelengthMicrometers);
        var samples = pupilSamples.ToArray();
        foreach (var sample in samples)
        {
            ValidateNormalized(sample.X, nameof(sample.X));
            ValidateNormalized(sample.Y, nameof(sample.Y));
            if ((sample.X * sample.X) + (sample.Y * sample.Y) > 1.0 + NormalizedCoordinateTolerance)
            {
                throw new ArgumentOutOfRangeException(nameof(pupilSamples), "Normalized pupil coordinates must lie inside the unit pupil.");
            }
        }

        var vignetteScale = applyVignettingFactors
            ? VignetteScale(normalizedFieldX, normalizedFieldY)
            : (X: 1.0, Y: 1.0);
        var fieldRayContext = new FieldRayContext(
            normalizedFieldX,
            normalizedFieldY,
            vignetteScale.X,
            vignetteScale.Y,
            EntrancePupilGlobalZ(),
            FieldOrigin(field.X, field.Y, 0, 0, apertureRadius, realImageLaunch),
            ObjectConjugate.IsInfinite(_optic.SurfaceGroup.Items.FirstOrDefault()));
        var resolvedStopTargets = aimAtStop
            ? stopTargets ?? ParaxialStopTargets(
                normalizedFieldX,
                normalizedFieldY,
                samples)
            : null;
        if (resolvedStopTargets is not null && resolvedStopTargets.Count != samples.Length)
        {
            throw new ArgumentException(
                "Stop-target count must match the pupil-sample count.",
                nameof(stopTargets));
        }
        var rays = new RealRay[samples.Length];
        void GenerateRay(int index)
        {
            var sample = samples[index];
            try
            {
                rays[index] = CreateRay(
                    field.X,
                    field.Y,
                    sample.X,
                    sample.Y,
                    apertureRadius,
                    wavelengthNanometers,
                    sample.Weight,
                    realImageLaunch,
                    aimAtStop,
                    resolvedStopTargets?[index],
                    fieldRayContext);
            }
            catch (RayAimingException) when (aimAtStop)
            {
                // A batch can legitimately contain pupil samples that cannot reach
                // the stop. Preserve the sample position but mark it invalid; the
                // consuming analysis decides whether enough valid rays remain.
                rays[index] = CreateRay(
                    field.X,
                    field.Y,
                    sample.X,
                    sample.Y,
                    apertureRadius,
                    wavelengthNanometers,
                    intensity: 0,
                    realImageLaunch,
                    aimAtStop: false,
                    fieldRayContext: fieldRayContext);
            }
        }

        if (samples.Length >= 64)
        {
            Parallel.For(
                0,
                samples.Length,
                new ParallelOptions { CancellationToken = ComputationCancellation.Current },
                GenerateRay);
        }
        else
        {
            for (var index = 0; index < samples.Length; index++)
            {
                GenerateRay(index);
            }
        }

        return new RealRayBundle(rays);
    }

    public static PupilSampling ParseSampling(string distribution)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(distribution);
        return distribution.Trim().ToLowerInvariant() switch
        {
            "hexapolar" => PupilSampling.Hexapolar,
            "random" => PupilSampling.Random,
            "sobol" => PupilSampling.Sobol,
            "line_x" or "linex" => PupilSampling.LineX,
            "line_y" or "liney" => PupilSampling.LineY,
            "ring" => PupilSampling.Ring,
            "grid" or "uniform_grid" or "uniform" => PupilSampling.UniformGrid,
            _ => throw new ArgumentException(
                $"Pupil sampling distribution '{distribution}' is not supported.",
                nameof(distribution))
        };
    }

    public static double MicrometersToNanometers(double wavelengthMicrometers)
    {
        if (!double.IsFinite(wavelengthMicrometers) || wavelengthMicrometers <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(wavelengthMicrometers), "Wavelength must be a positive finite value in micrometers.");
        }

        return wavelengthMicrometers * 1000.0;
    }

    public static double NanometersToMicrometers(double wavelengthNanometers)
    {
        if (!double.IsFinite(wavelengthNanometers) || wavelengthNanometers <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(wavelengthNanometers), "Wavelength must be a positive finite value in nanometers.");
        }

        return wavelengthNanometers / 1000.0;
    }

    private (double X, double Y) NormalizedFieldToValues(double normalizedFieldX, double normalizedFieldY)
    {
        return FieldCoordinates.Denormalize(_optic.Fields, normalizedFieldX, normalizedFieldY);
    }

    private RealRay CreateRay(
        double fieldX,
        double fieldY,
        double normalizedPupilX,
        double normalizedPupilY,
        double apertureRadius,
        double wavelengthNanometers,
        double intensity,
        (double X, double Y)? realImageLaunch = null,
        bool aimAtStop = false,
        (double X, double Y)? paraxialStopTarget = null,
        FieldRayContext? fieldRayContext = null)
    {
        var geometry = CreateFieldRay(
            fieldX,
            fieldY,
            normalizedPupilX,
            normalizedPupilY,
            apertureRadius,
            applyVignetting: true,
            realImageLaunch: realImageLaunch,
            wavelengthNanometers: wavelengthNanometers,
            aimAtStop: aimAtStop,
            paraxialStopTarget: paraxialStopTarget,
            fieldRayContext: fieldRayContext);
        var apodization = ApodizationIntensity(normalizedPupilX, normalizedPupilY, apertureRadius);
        return new RealRay(geometry.Origin, geometry.Direction, wavelengthNanometers, intensity * apodization);
    }

    private double ApodizationIntensity(double pupilX, double pupilY, double apertureRadius)
    {
        if (_optic.Apodization is not ZemaxApodization { Type: ZemaxApodizationType.CosineCubed } cosine)
        {
            return _optic.Apodization?.Intensity(pupilX, pupilY) ?? 1;
        }

        var objectSurface = _optic.SurfaceGroup.Items.FirstOrDefault();
        var marginalSlope = ObjectConjugate.IsInfinite(objectSurface)
            ? 0
            : apertureRadius / (_optic.Paraxial.EstimateEntrancePupilLocation()
                - (objectSurface?.CoordinateSystem.Origin.Z ?? 0));
        return cosine.Intensity(pupilX, pupilY, marginalSlope);
    }

    private static Vector3D Normalize(Vector3D vector)
    {
        var length = vector.Length;
        return length <= 1e-12 ? new Vector3D(0, 0, 1) : vector / length;
    }

    private static double DegreesToRadians(double degrees)
    {
        return degrees * Math.PI / 180.0;
    }

    private double EntrancePupilRadius()
    {
        return _optic.Paraxial.EstimateEntrancePupilDiameter() / 2.0;
    }

    private (Vector3D Origin, Vector3D Direction) CreateFieldRay(
        double fieldX,
        double fieldY,
        double normalizedPupilX,
        double normalizedPupilY,
        double apertureRadius,
        bool applyVignetting,
        (double X, double Y)? realImageLaunch = null,
        double wavelengthNanometers = 0,
        bool aimAtStop = false,
        (double X, double Y)? paraxialStopTarget = null,
        FieldRayContext? fieldRayContext = null)
    {
        (double X, double Y) normalizedField = fieldRayContext is null
            ? DefinitionValuesToNormalized(fieldX, fieldY)
            : (fieldRayContext.NormalizedFieldX, fieldRayContext.NormalizedFieldY);
        (double X, double Y) vignetteScale = !applyVignetting
            ? (1.0, 1.0)
            : fieldRayContext is null
                ? VignetteScale(normalizedField.X, normalizedField.Y)
                : (fieldRayContext.VignetteScaleX, fieldRayContext.VignetteScaleY);
        var pupilX = normalizedPupilX * vignetteScale.X;
        var pupilY = normalizedPupilY * vignetteScale.Y;
        var origin = fieldRayContext is null
            ? FieldOrigin(fieldX, fieldY, pupilX, pupilY, apertureRadius, realImageLaunch)
            : fieldRayContext.TranslateOriginWithPupil
                ? fieldRayContext.BaseOrigin + new Vector3D(
                    pupilX * apertureRadius,
                    pupilY * apertureRadius,
                    0)
                : fieldRayContext.BaseOrigin;

        if (_optic.ObjectSpaceTelecentric || _optic.FieldGroupTelecentric)
        {
            if (_optic.FieldDefinition == FieldDefinitionKind.Angle)
            {
                throw new InvalidOperationException("Angle fields are not valid for object-space telecentric systems.");
            }

            if (_optic.Aperture.Kind != ApertureKind.NumericalAperture)
            {
                throw new InvalidOperationException("Object-space telecentric systems require an object numerical-aperture definition.");
            }

            var sine = _optic.Aperture.Value;
            if (!double.IsFinite(sine) || sine <= 0 || sine > 1)
            {
                throw new InvalidOperationException("Object numerical aperture must be in (0, 1] for a telecentric field.");
            }

            var target = new Vector3D(
                origin.X + pupilX,
                origin.Y + pupilY,
                origin.Z + (Math.Sqrt(1 - (sine * sine)) / sine));
            return (origin, Normalize(target - origin));
        }

        var entrancePupil = new Vector3D(
            pupilX * apertureRadius,
            pupilY * apertureRadius,
            fieldRayContext?.EntrancePupilGlobalZ ?? EntrancePupilGlobalZ());
        var direction = Normalize(entrancePupil - origin);
        if (!aimAtStop)
        {
            return (origin, direction);
        }

        var effectiveWavelength = wavelengthNanometers > 0
            ? wavelengthNanometers
            : PrimaryWavelengthMicrometers() * 1000;
        return ObjectConjugate.IsInfinite(_optic.SurfaceGroup.Items.FirstOrDefault())
            ? AimInfiniteRayAtStop(
                origin,
                direction,
                pupilX,
                pupilY,
                effectiveWavelength,
                paraxialStopTarget)
            : AimFiniteRayAtStop(
                origin,
                direction,
                pupilX,
                pupilY,
                effectiveWavelength,
                paraxialStopTarget);
    }

    private (Vector3D Origin, Vector3D Direction) AimInfiniteRayAtStop(
        Vector3D origin,
        Vector3D direction,
        double normalizedPupilX,
        double normalizedPupilY,
        double wavelengthNanometers,
        (double X, double Y)? paraxialStopTarget = null)
    {
        var stopIndex = _optic.SurfaceGroup.Items.ToList().FindIndex(surface => surface.IsStop);
        if (stopIndex <= 0)
        {
            return (origin, direction);
        }

        var stop = _optic.SurfaceGroup.Items[stopIndex];
        const int maximumIterations = 16;
        var (targetX, targetY) = paraxialStopTarget ?? ParaxialStopTarget(
            normalizedPupilX,
            normalizedPupilY,
            stopIndex);
        var aimedOrigin = origin;
        var lastErrorSquared = double.PositiveInfinity;
        for (var iteration = 0; iteration < maximumIterations; iteration++)
        {
            var stopSample = _optic.SequentialRayTracer.TraceToSurface(
                new RealRay(aimedOrigin, direction, wavelengthNanometers),
                stopIndex);
            if (stopSample is null)
            {
                throw new RayAimingException(stop.Number, iteration + 1, double.PositiveInfinity);
            }

            var stopPoint = stop.CoordinateSystem.ToLocalPoint(stopSample.Position);
            var errorX = targetX - stopPoint.X;
            var errorY = targetY - stopPoint.Y;
            lastErrorSquared = (errorX * errorX) + (errorY * errorY);
            if (lastErrorSquared <= 1e-16)
            {
                return (aimedOrigin, direction);
            }

            var correction = stop.CoordinateSystem.ToGlobalDirection(new Vector3D(errorX, errorY, 0));
            aimedOrigin += new Vector3D(correction.X, correction.Y, 0);
        }

        if (lastErrorSquared <= 1e-8)
        {
            return (aimedOrigin, direction);
        }

        throw new RayAimingException(stop.Number, maximumIterations, Math.Sqrt(lastErrorSquared));
    }

    private (Vector3D Origin, Vector3D Direction) AimFiniteRayAtStop(
        Vector3D origin,
        Vector3D direction,
        double normalizedPupilX,
        double normalizedPupilY,
        double wavelengthNanometers,
        (double X, double Y)? paraxialStopTarget = null)
    {
        var stopIndex = _optic.SurfaceGroup.Items.ToList().FindIndex(surface => surface.IsStop);
        if (stopIndex <= 0)
        {
            return (origin, direction);
        }

        var stop = _optic.SurfaceGroup.Items[stopIndex];
        var (targetX, targetY) = paraxialStopTarget ?? ParaxialStopTarget(
            normalizedPupilX,
            normalizedPupilY,
            stopIndex);
        var slopeX = direction.X / Math.Max(1e-30, direction.Z);
        var slopeY = direction.Y / Math.Max(1e-30, direction.Z);
        if (TryAimFiniteTarget(
                origin,
                wavelengthNanometers,
                stopIndex,
                targetX,
                targetY,
                slopeX,
                slopeY,
                out var aimedDirection,
                out var residual))
        {
            return (origin, aimedDirection);
        }

        // In high-NA finite-conjugate systems, the direct paraxial marginal-ray
        // estimate can miss an early surface before Newton iteration obtains a
        // usable sample. Continue from the chief ray to the requested stop point
        // so every intermediate trial remains on a traceable branch.
        var entrancePupilCenter = new Vector3D(0, 0, EntrancePupilGlobalZ());
        var centralDirection = Normalize(entrancePupilCenter - origin);
        var centralSlopeX = centralDirection.X / Math.Max(1e-30, centralDirection.Z);
        var centralSlopeY = centralDirection.Y / Math.Max(1e-30, centralDirection.Z);
        if (!TryAimFiniteTarget(
                origin,
                wavelengthNanometers,
                stopIndex,
                0,
                0,
                centralSlopeX,
                centralSlopeY,
                out aimedDirection,
                out residual))
        {
            throw new RayAimingException(stop.Number, 1, residual);
        }

        const int continuationSteps = 16;
        for (var step = 1; step <= continuationSteps; step++)
        {
            slopeX = aimedDirection.X / Math.Max(1e-30, aimedDirection.Z);
            slopeY = aimedDirection.Y / Math.Max(1e-30, aimedDirection.Z);
            var fraction = (double)step / continuationSteps;
            if (!TryAimFiniteTarget(
                    origin,
                    wavelengthNanometers,
                    stopIndex,
                    targetX * fraction,
                    targetY * fraction,
                    slopeX,
                    slopeY,
                    out aimedDirection,
                    out residual))
            {
                throw new RayAimingException(stop.Number, step, residual);
            }
        }

        return (origin, aimedDirection);
    }

    private bool TryAimFiniteTarget(
        Vector3D origin,
        double wavelengthNanometers,
        int stopIndex,
        double targetX,
        double targetY,
        double slopeX,
        double slopeY,
        out Vector3D aimedDirection,
        out double residual)
    {
        var stop = _optic.SurfaceGroup.Items[stopIndex];
        var distance = Math.Max(
            1e-9,
            Math.Abs(stop.CoordinateSystem.Origin.Z - origin.Z));
        double? previousSlopeX = null;
        double? previousSlopeY = null;
        double? previousStopX = null;
        double? previousStopY = null;
        var lastErrorSquared = double.PositiveInfinity;
        for (var iteration = 0; iteration < 12; iteration++)
        {
            var trialDirection = Normalize(new Vector3D(slopeX, slopeY, 1));
            var stopSample = _optic.SequentialRayTracer.TraceToSurface(
                new RealRay(origin, trialDirection, wavelengthNanometers),
                stopIndex);
            if (stopSample is null)
            {
                aimedDirection = default;
                residual = double.PositiveInfinity;
                return false;
            }

            var stopPoint = stop.CoordinateSystem.ToLocalPoint(stopSample.Position);
            var errorX = targetX - stopPoint.X;
            var errorY = targetY - stopPoint.Y;
            lastErrorSquared = (errorX * errorX) + (errorY * errorY);
            if (lastErrorSquared <= 1e-16)
            {
                aimedDirection = trialDirection;
                residual = Math.Sqrt(lastErrorSquared);
                return true;
            }

            var correctionX = errorX / distance;
            var correctionY = errorY / distance;
            if (previousSlopeX.HasValue
                && previousStopX.HasValue
                && Math.Abs(slopeX - previousSlopeX.Value) > 1e-18)
            {
                var derivativeX = (stopPoint.X - previousStopX.Value)
                    / (slopeX - previousSlopeX.Value);
                if (double.IsFinite(derivativeX) && Math.Abs(derivativeX) > 1e-12)
                {
                    correctionX = errorX / derivativeX;
                }
            }

            if (previousSlopeY.HasValue
                && previousStopY.HasValue
                && Math.Abs(slopeY - previousSlopeY.Value) > 1e-18)
            {
                var derivativeY = (stopPoint.Y - previousStopY.Value)
                    / (slopeY - previousSlopeY.Value);
                if (double.IsFinite(derivativeY) && Math.Abs(derivativeY) > 1e-12)
                {
                    correctionY = errorY / derivativeY;
                }
            }

            previousSlopeX = slopeX;
            previousSlopeY = slopeY;
            previousStopX = stopPoint.X;
            previousStopY = stopPoint.Y;
            slopeX += Math.Clamp(correctionX, -0.1, 0.1);
            slopeY += Math.Clamp(correctionY, -0.1, 0.1);
        }

        if (lastErrorSquared <= 1e-8)
        {
            aimedDirection = Normalize(new Vector3D(slopeX, slopeY, 1));
            residual = Math.Sqrt(lastErrorSquared);
            return true;
        }

        aimedDirection = default;
        residual = Math.Sqrt(lastErrorSquared);
        return false;
    }

    private (double X, double Y) ParaxialStopTarget(
        double normalizedPupilX,
        double normalizedPupilY,
        int stopIndex)
    {
        var wavelengthMicrometers = PrimaryWavelengthMicrometers();
        var xTrace = _optic.Paraxial.TraceNormalizedPupil(
            0,
            new[] { normalizedPupilX },
            wavelengthMicrometers);
        var yTrace = _optic.Paraxial.TraceNormalizedPupil(
            0,
            new[] { normalizedPupilY },
            wavelengthMicrometers);
        if (stopIndex >= xTrace.Heights.Count || stopIndex >= yTrace.Heights.Count)
        {
            var stopRadius = _optic.SurfaceGroup.Items[stopIndex].SemiDiameter;
            return (
                normalizedPupilX * stopRadius,
                normalizedPupilY * stopRadius);
        }

        return (
            xTrace.Heights[stopIndex][0],
            yTrace.Heights[stopIndex][0]);
    }

    private (double X, double Y)[]? ParaxialStopTargets(
        double normalizedFieldX,
        double normalizedFieldY,
        IReadOnlyList<PupilSample> samples)
    {
        var stopIndex = _optic.SurfaceGroup.Items.ToList().FindIndex(surface => surface.IsStop);
        if (stopIndex <= 0 || samples.Count == 0)
        {
            return null;
        }

        var vignetteScale = VignetteScale(normalizedFieldX, normalizedFieldY);
        var pupilX = samples.Select(sample => sample.X * vignetteScale.X).ToArray();
        var pupilY = samples.Select(sample => sample.Y * vignetteScale.Y).ToArray();
        var wavelengthMicrometers = PrimaryWavelengthMicrometers();
        var xTrace = _optic.Paraxial.TraceNormalizedPupil(
            0,
            pupilX,
            wavelengthMicrometers);
        var yTrace = _optic.Paraxial.TraceNormalizedPupil(
            0,
            pupilY,
            wavelengthMicrometers);
        var targets = new (double X, double Y)[samples.Count];
        if (stopIndex >= xTrace.Heights.Count || stopIndex >= yTrace.Heights.Count)
        {
            var stopRadius = _optic.SurfaceGroup.Items[stopIndex].SemiDiameter;
            for (var index = 0; index < samples.Count; index++)
            {
                targets[index] = (
                    pupilX[index] * stopRadius,
                    pupilY[index] * stopRadius);
            }

            return targets;
        }

        for (var index = 0; index < samples.Count; index++)
        {
            targets[index] = (
                xTrace.Heights[stopIndex][index],
                yTrace.Heights[stopIndex][index]);
        }

        return targets;
    }

    private Vector3D FieldOrigin(
        double fieldX,
        double fieldY,
        double pupilX,
        double pupilY,
        double apertureRadius,
        (double X, double Y)? realImageLaunch)
    {
        return _optic.FieldDefinition switch
        {
            FieldDefinitionKind.ObjectHeight => ObjectHeightOrigin(fieldX, fieldY),
            FieldDefinitionKind.ParaxialImageHeight => ParaxialImageHeightOrigin(
                fieldX,
                fieldY,
                pupilX,
                pupilY,
                apertureRadius),
            FieldDefinitionKind.RealImageHeight => RealImageHeightOrigin(
                realImageLaunch ?? ResolveRealImageFieldCoordinates(fieldX, fieldY),
                pupilX,
                pupilY,
                apertureRadius),
            _ => AngleFieldOrigin(fieldX, fieldY, pupilX, pupilY, apertureRadius)
        };
    }

    private Vector3D RealImageHeightOrigin(
        (double X, double Y) launchField,
        double pupilX,
        double pupilY,
        double apertureRadius)
    {
        var objectSurface = _optic.SurfaceGroup.Items.FirstOrDefault();
        return ObjectConjugate.IsInfinite(objectSurface)
            ? AngleFieldOrigin(launchField.X, launchField.Y, pupilX, pupilY, apertureRadius)
            : ObjectHeightOrigin(launchField.X, launchField.Y);
    }

    private Vector3D AngleFieldOrigin(
        double fieldX,
        double fieldY,
        double pupilX,
        double pupilY,
        double apertureRadius)
    {
        var objectSurface = _optic.SurfaceGroup.Items.FirstOrDefault();
        var entrancePupilZ = _optic.Paraxial.EstimateEntrancePupilLocation();
        if (!ObjectConjugate.IsInfinite(objectSurface))
        {
            var objectZ = objectSurface?.CoordinateSystem.Origin.Z ?? 0;
            return new Vector3D(
                -Math.Tan(DegreesToRadians(fieldX)) * (entrancePupilZ - objectZ),
                -Math.Tan(DegreesToRadians(fieldY)) * (entrancePupilZ - objectZ),
                objectZ);
        }

        var (firstSurfaceZ, offset) = InfiniteObjectStart(apertureRadius);
        var startZ = firstSurfaceZ - offset;
        return new Vector3D(
            (pupilX * apertureRadius) - (Math.Tan(DegreesToRadians(fieldX)) * (offset + entrancePupilZ)),
            (pupilY * apertureRadius) - (Math.Tan(DegreesToRadians(fieldY)) * (offset + entrancePupilZ)),
            startZ);
    }

    private Vector3D ObjectHeightOrigin(double fieldX, double fieldY)
    {
        var objectSurface = _optic.SurfaceGroup.Items.FirstOrDefault();
        if (ObjectConjugate.IsInfinite(objectSurface))
        {
            throw new InvalidOperationException("Object-height fields require a finite object surface.");
        }

        var objectZ = objectSurface?.CoordinateSystem.Origin.Z ?? 0;
        var sag = objectSurface?.Geometry.Sag(fieldX, fieldY) ?? 0;
        return new Vector3D(fieldX, fieldY, objectZ + sag);
    }

    private Vector3D ParaxialImageHeightOrigin(
        double fieldX,
        double fieldY,
        double pupilX,
        double pupilY,
        double apertureRadius)
    {
        var (imageHeightUnit, objectHeightUnit, objectSlopeUnit) = TraceUnitChiefRay();
        if (Math.Abs(imageHeightUnit) <= 1e-15)
        {
            throw new InvalidOperationException("The paraxial image height cannot be resolved for this optical system.");
        }

        var objectSurface = _optic.SurfaceGroup.Items.FirstOrDefault();
        if (!ObjectConjugate.IsInfinite(objectSurface))
        {
            var objectX = objectHeightUnit * (fieldX / imageHeightUnit);
            var objectY = objectHeightUnit * (fieldY / imageHeightUnit);
            var objectZ = objectSurface?.CoordinateSystem.Origin.Z ?? 0;
            var sag = objectSurface?.Geometry.Sag(objectX, objectY) ?? 0;
            return new Vector3D(objectX, objectY, objectZ + sag);
        }

        var entrancePupilZ = _optic.Paraxial.EstimateEntrancePupilLocation();
        var (firstSurfaceZ, offset) = InfiniteObjectStart(apertureRadius);
        var objectSlopeX = objectSlopeUnit * (fieldX / imageHeightUnit);
        var objectSlopeY = objectSlopeUnit * (fieldY / imageHeightUnit);
        return new Vector3D(
            (pupilX * apertureRadius) - (objectSlopeX * (offset + entrancePupilZ)),
            (pupilY * apertureRadius) - (objectSlopeY * (offset + entrancePupilZ)),
            firstSurfaceZ - offset);
    }

    private (double ImageHeight, double ObjectHeight, double ObjectSlope) TraceUnitChiefRay()
    {
        var surfaces = _optic.SurfaceGroup.Items;
        var stopIndex = surfaces.ToList().FindIndex(surface => surface.IsStop);
        if (stopIndex < 0)
        {
            throw new InvalidOperationException("Paraxial image-height fields require an aperture stop.");
        }

        var positions = surfaces.Select(surface => surface.CoordinateSystem.Origin.Z).ToArray();
        var wavelength = PrimaryWavelengthMicrometers();
        var imageTrace = _optic.Paraxial.TraceGeneric(
            new[] { 0.0 },
            new[] { 1.0 },
            positions[stopIndex],
            wavelength,
            stopIndex);
        var objectTrace = _optic.Paraxial.TraceGenericReverse(
            new[] { 0.0 },
            new[] { 1.0 },
            positions[^1] - positions[stopIndex],
            wavelength,
            surfaces.Count - stopIndex);
        return (
            imageTrace.Heights[^1][0],
            objectTrace.Heights[^1][0],
            objectTrace.Slopes[^1][0]);
    }

    private (double X, double Y)? ResolveRealImageLaunch(
        double targetX,
        double targetY,
        bool aimAtStop = false)
    {
        return _optic.FieldDefinition == FieldDefinitionKind.RealImageHeight
            ? ResolveRealImageFieldCoordinates(targetX, targetY, aimAtStop)
            : null;
    }

    internal (double X, double Y) ResolveRealImageFieldCoordinates(
        double targetX,
        double targetY,
        bool aimAtStop = false)
    {
        if (!double.IsFinite(targetX) || !double.IsFinite(targetY))
        {
            throw new ArgumentOutOfRangeException(nameof(targetX), "Real image-height coordinates must be finite.");
        }

        var guess = InitialRealImageFieldGuess(targetX, targetY);
        if (!TryEvaluateRealImageChief(guess.X, guess.Y, out var current, aimAtStop))
        {
            guess = (0, 0);
            if (!TryEvaluateRealImageChief(guess.X, guess.Y, out current, aimAtStop))
            {
                throw RealImageSolveFailure(targetX, targetY);
            }
        }

        var tolerance = 1e-9 * Math.Max(1, Math.Sqrt((targetX * targetX) + (targetY * targetY)));

        for (var iteration = 0; iteration < 24; iteration++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var errorX = current.X - targetX;
            var errorY = current.Y - targetY;
            var errorNorm = Math.Sqrt((errorX * errorX) + (errorY * errorY));
            if (errorNorm <= tolerance)
            {
                return guess;
            }

            var stepX = DerivativeStep(guess.X, targetX);
            var stepY = DerivativeStep(guess.Y, targetY);
            var derivativeX = ImageDerivative(guess, current, stepX, varyX: true, aimAtStop);
            var derivativeY = ImageDerivative(guess, current, stepY, varyX: false, aimAtStop);
            var jxx = derivativeX.X;
            var jyx = derivativeX.Y;
            var jxy = derivativeY.X;
            var jyy = derivativeY.Y;
            var determinant = (jxx * jyy) - (jxy * jyx);
            if (!double.IsFinite(determinant) || Math.Abs(determinant) <= 1e-18)
            {
                throw RealImageSolveFailure(targetX, targetY);
            }

            var deltaX = ((-errorX * jyy) + (jxy * errorY)) / determinant;
            var deltaY = ((jyx * errorX) - (jxx * errorY)) / determinant;
            var objectSurface = _optic.SurfaceGroup.Items.FirstOrDefault();
            var maximumStep = ObjectConjugate.IsInfinite(objectSurface)
                ? 15.0
                : Math.Max(
                    10.0,
                    2 * Math.Abs(objectSurface?.CoordinateSystem.Origin.Z ?? 0));
            var deltaNorm = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
            if (deltaNorm > maximumStep)
            {
                var scale = maximumStep / deltaNorm;
                deltaX *= scale;
                deltaY *= scale;
            }

            var accepted = false;
            for (var lineSearch = 0; lineSearch < 12; lineSearch++)
            {
                var scale = Math.Pow(0.5, lineSearch);
                var candidate = (
                    X: LimitLaunchCoordinate(guess.X + (scale * deltaX)),
                    Y: LimitLaunchCoordinate(guess.Y + (scale * deltaY)));
                if (!TryEvaluateRealImageChief(
                    candidate.X,
                    candidate.Y,
                    out var candidateImage,
                    aimAtStop))
                {
                    continue;
                }

                var candidateErrorX = candidateImage.X - targetX;
                var candidateErrorY = candidateImage.Y - targetY;
                var candidateNorm = Math.Sqrt(
                    (candidateErrorX * candidateErrorX) + (candidateErrorY * candidateErrorY));
                if (candidateNorm < errorNorm)
                {
                    guess = candidate;
                    current = candidateImage;
                    accepted = true;
                    break;
                }
            }

            if (!accepted)
            {
                throw RealImageSolveFailure(targetX, targetY);
            }
        }

        throw RealImageSolveFailure(targetX, targetY);
    }

    private (double X, double Y) InitialRealImageFieldGuess(double targetX, double targetY)
    {
        var (imageHeightUnit, objectHeightUnit, objectSlopeUnit) = TraceUnitChiefRay();
        if (Math.Abs(imageHeightUnit) <= 1e-15)
        {
            return (0, 0);
        }

        if (ObjectConjugate.IsInfinite(_optic.SurfaceGroup.Items.FirstOrDefault()))
        {
            return (
                RadiansToDegrees(Math.Atan(objectSlopeUnit * targetX / imageHeightUnit)),
                RadiansToDegrees(Math.Atan(objectSlopeUnit * targetY / imageHeightUnit)));
        }

        return (
            objectHeightUnit * targetX / imageHeightUnit,
            objectHeightUnit * targetY / imageHeightUnit);
    }

    private (double X, double Y) EvaluateRealImageChief(
        double launchX,
        double launchY,
        bool aimAtStop = false)
    {
        var objectSurface = _optic.SurfaceGroup.Items.FirstOrDefault();
        var apertureRadius = EntrancePupilRadius();
        var origin = ObjectConjugate.IsInfinite(objectSurface)
            ? AngleFieldOrigin(launchX, launchY, 0, 0, apertureRadius)
            : ObjectHeightOrigin(launchX, launchY);
        Vector3D direction;
        if (_optic.ObjectSpaceTelecentric || _optic.FieldGroupTelecentric)
        {
            ValidateTelecentricField();
            direction = new Vector3D(0, 0, 1);
        }
        else
        {
            var entrancePupil = new Vector3D(0, 0, EntrancePupilGlobalZ());
            direction = Normalize(entrancePupil - origin);
        }

        var wavelengthNanometers = PrimaryWavelengthMicrometers() * 1000;
        if (aimAtStop)
        {
            (origin, direction) = ObjectConjugate.IsInfinite(objectSurface)
                ? AimInfiniteRayAtStop(origin, direction, 0, 0, wavelengthNanometers)
                : AimFiniteRayAtStop(origin, direction, 0, 0, wavelengthNanometers);
        }

        var ray = new RealRay(origin, direction, wavelengthNanometers);
        var history = _optic.SequentialRayTracer.Trace(new RealRayBundle(new[] { ray })).RayHistories.Single();
        var imageSurface = _optic.SurfaceGroup.Items.LastOrDefault();
        if (imageSurface is null
            || history.Count == 0
            || history[^1].SurfaceNumber != imageSurface.Number
            || history[^1].Vignetted
            || history[^1].Intensity <= 0)
        {
            throw RealImageSolveFailure(double.NaN, double.NaN);
        }

        var localImagePoint = imageSurface.CoordinateSystem.ToLocalPoint(history[^1].Position);
        return (localImagePoint.X, localImagePoint.Y);
    }

    private bool TryEvaluateRealImageChief(
        double launchX,
        double launchY,
        out (double X, double Y) imagePoint,
        bool aimAtStop = false)
    {
        try
        {
            imagePoint = EvaluateRealImageChief(launchX, launchY, aimAtStop);
            return true;
        }
        catch (FieldAimingException)
        {
            imagePoint = default;
            return false;
        }
    }

    private (double X, double Y) ImageDerivative(
        (double X, double Y) launch,
        (double X, double Y) currentImage,
        double step,
        bool varyX,
        bool aimAtStop)
    {
        var plusLaunch = varyX
            ? (launch.X + step, launch.Y)
            : (launch.X, launch.Y + step);
        var minusLaunch = varyX
            ? (launch.X - step, launch.Y)
            : (launch.X, launch.Y - step);
        var hasPlus = TryEvaluateRealImageChief(
            plusLaunch.Item1,
            plusLaunch.Item2,
            out var plusImage,
            aimAtStop);
        var hasMinus = TryEvaluateRealImageChief(
            minusLaunch.Item1,
            minusLaunch.Item2,
            out var minusImage,
            aimAtStop);
        if (hasPlus && hasMinus)
        {
            return (
                (plusImage.X - minusImage.X) / (2 * step),
                (plusImage.Y - minusImage.Y) / (2 * step));
        }

        if (hasPlus)
        {
            return (
                (plusImage.X - currentImage.X) / step,
                (plusImage.Y - currentImage.Y) / step);
        }

        if (hasMinus)
        {
            return (
                (currentImage.X - minusImage.X) / step,
                (currentImage.Y - minusImage.Y) / step);
        }

        throw RealImageSolveFailure(double.NaN, double.NaN);
    }

    private void ValidateTelecentricField()
    {
        if (_optic.Aperture.Kind != ApertureKind.NumericalAperture)
        {
            throw new InvalidOperationException("Object-space telecentric systems require an object numerical-aperture definition.");
        }

        var sine = _optic.Aperture.Value;
        if (!double.IsFinite(sine) || sine <= 0 || sine > 1)
        {
            throw new InvalidOperationException("Object numerical aperture must be in (0, 1] for a telecentric field.");
        }
    }

    private double LimitLaunchCoordinate(double value)
    {
        return ObjectConjugate.IsInfinite(_optic.SurfaceGroup.Items.FirstOrDefault())
            ? Math.Clamp(value, -89.0, 89.0)
            : value;
    }

    private static double DerivativeStep(double value, double target)
    {
        return Math.Max(1e-6, Math.Max(Math.Abs(value), Math.Abs(target)) * 1e-5);
    }

    private static InvalidOperationException RealImageSolveFailure(double targetX, double targetY)
    {
        var suffix = double.IsFinite(targetX) && double.IsFinite(targetY)
            ? $" ({targetX:R}, {targetY:R})"
            : string.Empty;
        return new FieldAimingException($"Cannot find rays to yield requested real image height{suffix}.");
    }

    private static double RadiansToDegrees(double radians)
    {
        return radians * 180.0 / Math.PI;
    }

    private (double FirstSurfaceZ, double Offset) InfiniteObjectStart(double apertureRadius)
    {
        var physicalSurfaces = _optic.SurfaceGroup.Items.Skip(1).SkipLast(1).ToArray();
        var firstSurfaceZ = physicalSurfaces.FirstOrDefault()?.CoordinateSystem.Origin.Z ?? 0;
        return (firstSurfaceZ, Math.Max(apertureRadius * 2.0, 1e-6));
    }

    private double EntrancePupilGlobalZ()
    {
        var relativeOrGlobal = _optic.Paraxial.EstimateEntrancePupilLocation();
        var objectSurface = _optic.SurfaceGroup.Items.FirstOrDefault();
        if (!ObjectConjugate.IsInfinite(objectSurface))
        {
            return relativeOrGlobal;
        }

        var firstSurfaceZ = _optic.SurfaceGroup.Items
            .Skip(1)
            .FirstOrDefault()
            ?.CoordinateSystem.Origin.Z ?? 0;
        return firstSurfaceZ + relativeOrGlobal;
    }

    private (double X, double Y) DefinitionValuesToNormalized(double fieldX, double fieldY)
    {
        var maxField = MaximumField();
        return maxField <= 1e-15 ? (0, 0) : (fieldX / maxField, fieldY / maxField);
    }

    private (double X, double Y) VignetteScale(double normalizedFieldX, double normalizedFieldY)
    {
        if (_optic.Fields.Count == 0)
        {
            return (1, 1);
        }

        var maxField = MaximumField();
        var nearest = _optic.Fields
            .Select((field, index) =>
            {
                var x = maxField <= 1e-15 ? field.X : field.X / maxField;
                var y = maxField <= 1e-15 ? field.Y : field.Y / maxField;
                var dx = x - normalizedFieldX;
                var dy = y - normalizedFieldY;
                return (Field: field, Index: index, Distance: (dx * dx) + (dy * dy));
            })
            .OrderBy(candidate => candidate.Distance)
            .ThenBy(candidate => candidate.Index)
            .First().Field;
        return (1 - nearest.VignetteFactorX, 1 - nearest.VignetteFactorY);
    }

    private double MaximumField()
    {
        return FieldCoordinates.MaximumRadius(_optic.Fields);
    }

    private double PrimaryWavelengthMicrometers()
    {
        return (_optic.Wavelengths.FirstOrDefault(wavelength => wavelength.IsPrimary)
            ?? _optic.Wavelengths.FirstOrDefault())?.Micrometers ?? 0.5876;
    }

    private static void ValidateNormalized(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value < -1.0 - NormalizedCoordinateTolerance || value > 1.0 + NormalizedCoordinateTolerance)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Normalized coordinates must be finite values in [-1, 1].");
        }
    }
}

public sealed class FieldAimingException : InvalidOperationException
{
    public FieldAimingException(string message) : base(message) { }
}

public sealed class RayAimingException : InvalidOperationException
{
    public RayAimingException(int stopSurfaceNumber, int iterations, double residual)
        : base($"Ray aiming did not converge at stop surface {stopSurfaceNumber} after {iterations} iterations; residual={residual:G6} mm.")
    {
        StopSurfaceNumber = stopSurfaceNumber;
        Iterations = iterations;
        Residual = residual;
    }

    public int StopSurfaceNumber { get; }

    public int Iterations { get; }

    public double Residual { get; }
}

using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Rays;

namespace OptilandWorkbench.Core.Raytrace;

public sealed class RayGenerationSettings
{
    public int SamplesPerField { get; set; } = 9;

    public PupilSampling Sampling { get; set; } = PupilSampling.Hexapolar;

    public bool Telecentric { get; set; }

    public double ApodizationPower { get; set; }
}

public sealed class RayGenerator
{
    private readonly Optic _optic;
    private const double NormalizedCoordinateTolerance = 1e-12;

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
        var apertureRadius = EntrancePupilRadius();
        var samples = ApertureSampler.Generate(Settings.SamplesPerField, Settings.Sampling);
        var rays = new List<RealRay>();

        foreach (var field in fields)
        {
            foreach (var wavelength in wavelengths)
            {
                foreach (var sample in samples)
                {
                    var rayGeometry = CreateFieldRay(
                        field.X,
                        field.Y,
                        sample.X,
                        sample.Y,
                        apertureRadius,
                        applyVignetting: true);
                    var direction = Settings.Telecentric ? new Vector3D(0, 0, 1) : rayGeometry.Direction;
                    var radius = Math.Sqrt((sample.X * sample.X) + (sample.Y * sample.Y));
                    var legacyApodization = Settings.ApodizationPower <= 0
                        ? 1.0
                        : Math.Pow(Math.Max(0, 1 - (radius * radius)), Settings.ApodizationPower);
                    var fieldWeight = applyFieldWeight ? field.Weight : 1.0;
                    var wavelengthWeight = applyWavelengthWeight ? wavelength.Weight : 1.0;

                    var apodization = _optic.Apodization?.Intensity(sample.X, sample.Y) ?? 1.0;
                    rays.Add(new RealRay(
                        rayGeometry.Origin,
                        direction,
                        wavelength.Nanometers,
                        fieldWeight * wavelengthWeight * sample.Weight * legacyApodization * apodization));
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
        ValidateNormalized(normalizedFieldX, nameof(normalizedFieldX));
        ValidateNormalized(normalizedFieldY, nameof(normalizedFieldY));
        var sampling = ParseSampling(distribution);
        var apertureRadius = EntrancePupilRadius();
        var field = NormalizedFieldToValues(normalizedFieldX, normalizedFieldY);
        var wavelengthNanometers = MicrometersToNanometers(wavelengthMicrometers);
        var rays = ApertureSampler.Generate(sampleCount, sampling)
            .Select(sample => CreateRay(field.X, field.Y, sample.X, sample.Y, apertureRadius, wavelengthNanometers, sample.Weight))
            .ToArray();

        return new RealRayBundle(rays);
    }

    public RealRayBundle GenerateGeneric(
        double normalizedFieldX,
        double normalizedFieldY,
        double normalizedPupilX,
        double normalizedPupilY,
        double wavelengthMicrometers)
    {
        ValidateNormalized(normalizedFieldX, nameof(normalizedFieldX));
        ValidateNormalized(normalizedFieldY, nameof(normalizedFieldY));
        ValidateNormalized(normalizedPupilX, nameof(normalizedPupilX));
        ValidateNormalized(normalizedPupilY, nameof(normalizedPupilY));
        if ((normalizedPupilX * normalizedPupilX) + (normalizedPupilY * normalizedPupilY) > 1.0 + NormalizedCoordinateTolerance)
        {
            throw new ArgumentOutOfRangeException(nameof(normalizedPupilX), "Normalized pupil coordinates must lie inside the unit pupil.");
        }

        var apertureRadius = EntrancePupilRadius();
        var field = NormalizedFieldToValues(normalizedFieldX, normalizedFieldY);
        var vignetteScale = VignetteScale(normalizedFieldX, normalizedFieldY);
        var ray = CreateRay(
            field.X,
            field.Y,
            normalizedPupilX * vignetteScale.X,
            normalizedPupilY * vignetteScale.Y,
            apertureRadius,
            MicrometersToNanometers(wavelengthMicrometers),
            intensity: 1.0);
        return new RealRayBundle(new[] { ray });
    }

    public RealRayBundle GenerateNormalizedPupilSamples(
        double normalizedFieldX,
        double normalizedFieldY,
        double wavelengthMicrometers,
        IEnumerable<PupilSample> pupilSamples)
    {
        ValidateNormalized(normalizedFieldX, nameof(normalizedFieldX));
        ValidateNormalized(normalizedFieldY, nameof(normalizedFieldY));
        var field = NormalizedFieldToValues(normalizedFieldX, normalizedFieldY);
        var apertureRadius = EntrancePupilRadius();
        var wavelengthNanometers = MicrometersToNanometers(wavelengthMicrometers);
        var rays = pupilSamples.Select(sample =>
        {
            ValidateNormalized(sample.X, nameof(sample.X));
            ValidateNormalized(sample.Y, nameof(sample.Y));
            if ((sample.X * sample.X) + (sample.Y * sample.Y) > 1.0 + NormalizedCoordinateTolerance)
            {
                throw new ArgumentOutOfRangeException(nameof(pupilSamples), "Normalized pupil coordinates must lie inside the unit pupil.");
            }

            return CreateRay(
                field.X,
                field.Y,
                sample.X,
                sample.Y,
                apertureRadius,
                wavelengthNanometers,
                sample.Weight);
        }).ToArray();
        return new RealRayBundle(rays);
    }

    public static PupilSampling ParseSampling(string distribution)
    {
        return distribution.Trim().ToLowerInvariant() switch
        {
            "hexapolar" => PupilSampling.Hexapolar,
            "random" => PupilSampling.Random,
            "sobol" => PupilSampling.Sobol,
            "line_x" or "linex" => PupilSampling.LineX,
            "line_y" or "liney" => PupilSampling.LineY,
            "ring" => PupilSampling.Ring,
            "grid" or "uniform_grid" or "uniform" => PupilSampling.UniformGrid,
            _ => PupilSampling.Hexapolar
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
        var maxField = _optic.Fields.Select(field => Math.Sqrt(
                (field.X * field.X)
                + (field.Y * field.Y)))
            .DefaultIfEmpty(0)
            .Max();

        return (normalizedFieldX * maxField, normalizedFieldY * maxField);
    }

    private RealRay CreateRay(
        double fieldX,
        double fieldY,
        double normalizedPupilX,
        double normalizedPupilY,
        double apertureRadius,
        double wavelengthNanometers,
        double intensity)
    {
        var geometry = CreateFieldRay(
            fieldX,
            fieldY,
            normalizedPupilX,
            normalizedPupilY,
            apertureRadius,
            applyVignetting: true);
        var apodization = _optic.Apodization?.Intensity(normalizedPupilX, normalizedPupilY) ?? 1.0;
        return new RealRay(geometry.Origin, geometry.Direction, wavelengthNanometers, intensity * apodization);
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
        bool applyVignetting)
    {
        var normalizedField = DefinitionValuesToNormalized(fieldX, fieldY);
        var vignetteScale = applyVignetting
            ? VignetteScale(normalizedField.X, normalizedField.Y)
            : (X: 1.0, Y: 1.0);
        var pupilX = normalizedPupilX * vignetteScale.X;
        var pupilY = normalizedPupilY * vignetteScale.Y;
        var origin = FieldOrigin(fieldX, fieldY, pupilX, pupilY, apertureRadius);

        if (_optic.ObjectSpaceTelecentric)
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
            _optic.Paraxial.EstimateEntrancePupilLocation());
        return (origin, Normalize(entrancePupil - origin));
    }

    private Vector3D FieldOrigin(
        double fieldX,
        double fieldY,
        double pupilX,
        double pupilY,
        double apertureRadius)
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
            _ => AngleFieldOrigin(fieldX, fieldY, pupilX, pupilY, apertureRadius)
        };
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
        if (!IsObjectAtInfinity(objectSurface))
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
        if (IsObjectAtInfinity(objectSurface))
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
        if (!IsObjectAtInfinity(objectSurface))
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

    private (double FirstSurfaceZ, double Offset) InfiniteObjectStart(double apertureRadius)
    {
        var physicalSurfaces = _optic.SurfaceGroup.Items.Skip(1).SkipLast(1).ToArray();
        var firstSurfaceZ = physicalSurfaces.FirstOrDefault()?.CoordinateSystem.Origin.Z ?? 0;
        var minimumSurfaceZ = physicalSurfaces
            .Select(surface => surface.CoordinateSystem.Origin.Z)
            .DefaultIfEmpty(firstSurfaceZ)
            .Min();
        return (firstSurfaceZ, (apertureRadius * 2.0) - minimumSurfaceZ);
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
        return _optic.Fields
            .Select(field => Math.Sqrt((field.X * field.X) + (field.Y * field.Y)))
            .DefaultIfEmpty(0)
            .Max();
    }

    private double PrimaryWavelengthMicrometers()
    {
        return (_optic.Wavelengths.FirstOrDefault(wavelength => wavelength.IsPrimary)
            ?? _optic.Wavelengths.FirstOrDefault())?.Micrometers ?? 0.5876;
    }

    private static bool IsObjectAtInfinity(OpticalSurface? objectSurface)
    {
        return objectSurface is null
            || double.IsInfinity(objectSurface.CoordinateSystem.Origin.Z)
            || Math.Abs(objectSurface.Thickness) <= 1e-12;
    }

    private static void ValidateNormalized(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value < -1.0 - NormalizedCoordinateTolerance || value > 1.0 + NormalizedCoordinateTolerance)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Normalized coordinates must be finite values in [-1, 1].");
        }
    }
}

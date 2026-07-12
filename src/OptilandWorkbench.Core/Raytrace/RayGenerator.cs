using OptilandWorkbench.Core.Backend;
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
                var fieldAngle = DegreesToRadians(field.YAngleDegrees);
                foreach (var sample in samples)
                {
                    var xFieldAngle = DegreesToRadians(field.XAngleDegrees);
                    var rayGeometry = CreateInfiniteConjugateRay(
                        xFieldAngle,
                        fieldAngle,
                        sample.X,
                        sample.Y,
                        apertureRadius);
                    var direction = Settings.Telecentric ? new Vector3D(0, 0, 1) : rayGeometry.Direction;
                    var radius = Math.Sqrt((sample.X * sample.X) + (sample.Y * sample.Y));
                    var apodization = Settings.ApodizationPower <= 0
                        ? 1.0
                        : Math.Pow(Math.Max(0, 1 - (radius * radius)), Settings.ApodizationPower);
                    var fieldWeight = applyFieldWeight ? field.Weight : 1.0;
                    var wavelengthWeight = applyWavelengthWeight ? wavelength.Weight : 1.0;

                    rays.Add(new RealRay(rayGeometry.Origin, direction, wavelength.Nanometers, fieldWeight * wavelengthWeight * sample.Weight * apodization));
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
        var field = NormalizedFieldToAngles(normalizedFieldX, normalizedFieldY);
        var wavelengthNanometers = MicrometersToNanometers(wavelengthMicrometers);
        var rays = ApertureSampler.Generate(sampleCount, sampling)
            .Select(sample => CreateRay(field.XAngleDegrees, field.YAngleDegrees, sample.X, sample.Y, apertureRadius, wavelengthNanometers, sample.Weight))
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
        var field = NormalizedFieldToAngles(normalizedFieldX, normalizedFieldY);
        var ray = CreateRay(
            field.XAngleDegrees,
            field.YAngleDegrees,
            normalizedPupilX,
            normalizedPupilY,
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
        var field = NormalizedFieldToAngles(normalizedFieldX, normalizedFieldY);
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
                field.XAngleDegrees,
                field.YAngleDegrees,
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

    private (double XAngleDegrees, double YAngleDegrees) NormalizedFieldToAngles(double normalizedFieldX, double normalizedFieldY)
    {
        var maxField = _optic.Fields.Select(field => Math.Sqrt(
                (field.XAngleDegrees * field.XAngleDegrees)
                + (field.YAngleDegrees * field.YAngleDegrees)))
            .DefaultIfEmpty(0)
            .Max();
        if (maxField <= 1e-12)
        {
            maxField = 1.0;
        }

        return (normalizedFieldX * maxField, normalizedFieldY * maxField);
    }

    private RealRay CreateRay(
        double xAngleDegrees,
        double yAngleDegrees,
        double normalizedPupilX,
        double normalizedPupilY,
        double apertureRadius,
        double wavelengthNanometers,
        double intensity)
    {
        var geometry = CreateInfiniteConjugateRay(
            DegreesToRadians(xAngleDegrees),
            DegreesToRadians(yAngleDegrees),
            normalizedPupilX,
            normalizedPupilY,
            apertureRadius);
        return new RealRay(geometry.Origin, geometry.Direction, wavelengthNanometers, intensity);
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

    private (Vector3D Origin, Vector3D Direction) CreateInfiniteConjugateRay(
        double xFieldAngleRadians,
        double yFieldAngleRadians,
        double normalizedPupilX,
        double normalizedPupilY,
        double apertureRadius)
    {
        var objectSurface = _optic.SurfaceGroup.Items.FirstOrDefault();
        if (objectSurface is not null && objectSurface.Thickness > 1e-12)
        {
            return (
                new Vector3D(
                    normalizedPupilX * apertureRadius,
                    normalizedPupilY * apertureRadius,
                    objectSurface.CoordinateSystem.Origin.Z),
                FieldAnglesToDirection(xFieldAngleRadians, yFieldAngleRadians));
        }

        var physicalSurfaces = _optic.SurfaceGroup.Items.Skip(1).SkipLast(1).ToArray();
        var firstSurfaceZ = physicalSurfaces.FirstOrDefault()?.CoordinateSystem.Origin.Z ?? 0;
        var minimumSurfaceZ = physicalSurfaces.Select(surface => surface.CoordinateSystem.Origin.Z).DefaultIfEmpty(firstSurfaceZ).Min();
        var entrancePupilDiameter = apertureRadius * 2.0;
        var offset = entrancePupilDiameter - minimumSurfaceZ;
        var startZ = firstSurfaceZ - offset;
        var entrancePupilZ = _optic.Paraxial.EstimateEntrancePupilLocation();
        var pupilX = normalizedPupilX * apertureRadius;
        var pupilY = normalizedPupilY * apertureRadius;
        var origin = new Vector3D(
            pupilX - (Math.Tan(xFieldAngleRadians) * (offset + entrancePupilZ)),
            pupilY - (Math.Tan(yFieldAngleRadians) * (offset + entrancePupilZ)),
            startZ);
        var pupilPoint = new Vector3D(pupilX, pupilY, entrancePupilZ);
        return (origin, Normalize(pupilPoint - origin));
    }

    private static Vector3D FieldAnglesToDirection(double xFieldAngleRadians, double yFieldAngleRadians)
    {
        return Normalize(new Vector3D(
            Math.Sin(xFieldAngleRadians),
            Math.Sin(yFieldAngleRadians),
            Math.Cos(xFieldAngleRadians) * Math.Cos(yFieldAngleRadians)));
    }

    private static void ValidateNormalized(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value < -1.0 - NormalizedCoordinateTolerance || value > 1.0 + NormalizedCoordinateTolerance)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Normalized coordinates must be finite values in [-1, 1].");
        }
    }
}

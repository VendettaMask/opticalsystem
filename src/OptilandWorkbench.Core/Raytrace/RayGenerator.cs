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
        var apertureRadius = _optic.SurfaceGroup.ApertureRadius();
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
                    var origin = PupilToOrigin(sample.X, sample.Y, apertureRadius);
                    var direction = Settings.Telecentric
                        ? new Vector3D(0, 0, 1)
                        : FieldAnglesToDirection(xFieldAngle, fieldAngle);
                    var radius = Math.Sqrt((sample.X * sample.X) + (sample.Y * sample.Y));
                    var apodization = Settings.ApodizationPower <= 0
                        ? 1.0
                        : Math.Pow(Math.Max(0, 1 - (radius * radius)), Settings.ApodizationPower);
                    var fieldWeight = applyFieldWeight ? field.Weight : 1.0;
                    var wavelengthWeight = applyWavelengthWeight ? wavelength.Weight : 1.0;

                    rays.Add(new RealRay(origin, direction, wavelength.Nanometers, fieldWeight * wavelengthWeight * sample.Weight * apodization));
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
        var apertureRadius = _optic.SurfaceGroup.ApertureRadius();
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

        var apertureRadius = _optic.SurfaceGroup.ApertureRadius();
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
        var maxX = _optic.Fields.Select(field => Math.Abs(field.XAngleDegrees)).DefaultIfEmpty(0).Max();
        var maxY = _optic.Fields.Select(field => Math.Abs(field.YAngleDegrees)).DefaultIfEmpty(0).Max();
        maxX = maxX <= 1e-12 ? maxY : maxX;
        maxY = maxY <= 1e-12 ? maxX : maxY;
        if (maxX <= 1e-12)
        {
            maxX = 1.0;
        }

        if (maxY <= 1e-12)
        {
            maxY = 1.0;
        }

        return (normalizedFieldX * maxX, normalizedFieldY * maxY);
    }

    private static RealRay CreateRay(
        double xAngleDegrees,
        double yAngleDegrees,
        double normalizedPupilX,
        double normalizedPupilY,
        double apertureRadius,
        double wavelengthNanometers,
        double intensity)
    {
        var origin = PupilToOrigin(normalizedPupilX, normalizedPupilY, apertureRadius);
        var direction = FieldAnglesToDirection(DegreesToRadians(xAngleDegrees), DegreesToRadians(yAngleDegrees));
        return new RealRay(origin, direction, wavelengthNanometers, intensity);
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

    private static Vector3D PupilToOrigin(double normalizedPupilX, double normalizedPupilY, double apertureRadius)
    {
        return new Vector3D(normalizedPupilX * apertureRadius, normalizedPupilY * apertureRadius, 0);
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

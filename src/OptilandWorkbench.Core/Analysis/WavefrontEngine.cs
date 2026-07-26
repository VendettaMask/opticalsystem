using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Rays;

namespace OptilandWorkbench.Core.Analysis;

public sealed record WavefrontSample(
    double NormalizedPupilX,
    double NormalizedPupilY,
    double PupilX,
    double PupilY,
    double PupilZ,
    double OpdWaves,
    double Intensity,
    double ImageDirectionZ = 1);

public sealed record WavefrontResult(
    IReadOnlyList<WavefrontSample> Samples,
    double Radius,
    double ReferenceOpticalPath,
    int VignettedRayCount,
    double ChiefImageDirectionZ = 1,
    double ImageRefractiveIndex = 1)
{
    public double Rms => Samples.Where(sample => sample.Intensity > 0)
        .Select(sample => sample.OpdWaves * sample.OpdWaves)
        .DefaultIfEmpty(0)
        .Average() is var mean ? Math.Sqrt(mean) : 0;
}

public static class WavefrontEngine
{
    public static WavefrontResult GenerateChiefRay(
        Optic optic,
        (double Hx, double Hy) field,
        Wavelength wavelength,
        int numRings)
    {
        var pupilSamples = ApertureSampler.GenerateHexapolarRings(numRings);
        return GenerateChiefRay(optic, field, wavelength, pupilSamples, aimAtStop: false);
    }

    public static WavefrontResult GenerateChiefRayUniform(
        Optic optic,
        (double Hx, double Hy) field,
        Wavelength wavelength,
        int samplesAcrossPupil,
        bool cellCentered = false,
        bool aimAtStop = false)
    {
        samplesAcrossPupil = Math.Max(2, samplesAcrossPupil);
        var pupilSamples = new List<PupilSample>();
        for (var row = 0; row < samplesAcrossPupil; row++)
        {
            var y = cellCentered
                ? -1 + ((2.0 * row + 1) / samplesAcrossPupil)
                : -1 + (2.0 * row / (samplesAcrossPupil - 1.0));
            for (var column = 0; column < samplesAcrossPupil; column++)
            {
                var x = cellCentered
                    ? -1 + ((2.0 * column + 1) / samplesAcrossPupil)
                    : -1 + (2.0 * column / (samplesAcrossPupil - 1.0));
                if ((x * x) + (y * y) <= 1)
                {
                    pupilSamples.Add(new PupilSample(x, y, 1));
                }
            }
        }

        return GenerateChiefRay(optic, field, wavelength, pupilSamples, aimAtStop);
    }

    public static WavefrontResult GenerateChiefRaySamples(
        Optic optic,
        (double Hx, double Hy) field,
        Wavelength wavelength,
        IReadOnlyList<(double X, double Y)> samples,
        bool aimAtStop = true,
        (double X, double Y)? resolvedRealImageLaunch = null)
    {
        return GenerateChiefRay(
            optic,
            field,
            wavelength,
            samples.Select(sample => new PupilSample(sample.X, sample.Y, 1)).ToArray(),
            aimAtStop,
            resolvedRealImageLaunch);
    }

    private static WavefrontResult GenerateChiefRay(
        Optic optic,
        (double Hx, double Hy) field,
        Wavelength wavelength,
        IReadOnlyList<PupilSample> pupilSamples,
        bool aimAtStop,
        (double X, double Y)? resolvedRealImageLaunch = null)
    {
        var chiefBundle = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(
            field.Hx,
            field.Hy,
            0,
            0,
            wavelength.Micrometers,
            aimAtStop,
            resolvedRealImageLaunch);
        var chief = optic.SequentialRayTracer.TraceFinalSamples(chiefBundle).Single();
        if (chief is null)
        {
            throw new InvalidOperationException("Chief ray did not reach the image surface.");
        }

        var imagePosition = chief.Position;
        var imageSurfacePosition = optic.SurfaceGroup.Items.LastOrDefault()?.CoordinateSystem.Origin.Z
            ?? imagePosition.Z;
        var spherePupilZ = imageSurfacePosition + optic.Paraxial.EstimateExitPupilLocation();
        var radius = Math.Sqrt(
            (imagePosition.X * imagePosition.X)
            + (imagePosition.Y * imagePosition.Y)
            + ((imagePosition.Z - spherePupilZ) * (imagePosition.Z - spherePupilZ)));
        var imageIndex = (optic.SurfaceGroup.Items.LastOrDefault()?.MaterialAfter ?? optic.Materials.Resolve("Air"))
            .RefractiveIndex(wavelength.Nanometers);
        var chiefImagePath = ImageToReferenceSphere(
            chief,
            imagePosition.X,
            imagePosition.Y,
            imagePosition.Z,
            radius,
            imageIndex);
        var referenceOpticalPath = chief.CumulativeOpticalPathLength - chiefImagePath;

        var bundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(
            field.Hx,
            field.Hy,
            wavelength.Micrometers,
            pupilSamples,
            aimAtStop,
            resolvedRealImageLaunch);
        var finalSamples = optic.SequentialRayTracer.TraceFinalSamples(bundle);
        var (ux, uy) = LaunchTiltDirection(optic, field, aimAtStop);
        var entrancePupilRadius = optic.Paraxial.EstimateEntrancePupilDiameter() / 2;
        var samples = new List<WavefrontSample>(pupilSamples.Count);
        var vignetted = 0;

        for (var index = 0; index < pupilSamples.Count; index++)
        {
            var pupil = pupilSamples[index];
            var ray = finalSamples[index];
            if (ray is null)
            {
                samples.Add(new WavefrontSample(pupil.X, pupil.Y, 0, 0, 0, 0, 0));
                vignetted++;
                continue;
            }

            var imagePath = ImageToReferenceSphere(
                ray,
                imagePosition.X,
                imagePosition.Y,
                imagePosition.Z,
                radius,
                imageIndex);
            var tilt = (ux * pupil.X * entrancePupilRadius) + (uy * pupil.Y * entrancePupilRadius);
            var opticalPath = ray.CumulativeOpticalPathLength - imagePath + tilt;
            var opdWaves = (referenceOpticalPath - opticalPath) / (wavelength.Micrometers * 1e-3);
            var t = imageIndex <= 1e-30 ? 0 : imagePath / imageIndex;
            var pupilPosition = ray.Position - (ray.Direction * t);
            var intensity = ray.Intensity;
            if (intensity <= 0)
            {
                vignetted++;
            }

            samples.Add(new WavefrontSample(
                pupil.X,
                pupil.Y,
                pupilPosition.X,
                pupilPosition.Y,
                pupilPosition.Z,
                opdWaves,
                intensity,
                ray.Direction.Z));
        }

        return new WavefrontResult(
            samples,
            radius,
            referenceOpticalPath,
            vignetted,
            chief.Direction.Z,
            imageIndex);
    }

    internal static (double X, double Y) LaunchTiltDirection(
        Optic optic,
        (double Hx, double Hy) field,
        bool aimAtStop = false)
    {
        double fieldX;
        double fieldY;
        if (optic.FieldDefinition == FieldDefinitionKind.Angle)
        {
            (fieldX, fieldY) = FieldCoordinates.Denormalize(optic.Fields, field.Hx, field.Hy);
        }
        else if (optic.FieldDefinition == FieldDefinitionKind.RealImageHeight && IsObjectAtInfinity(optic))
        {
            var target = FieldCoordinates.Denormalize(optic.Fields, field.Hx, field.Hy);
            (fieldX, fieldY) = optic.SequentialRayTracer.RayGenerator.ResolveRealImageFieldCoordinates(
                target.X,
                target.Y,
                aimAtStop);
        }
        else
        {
            return (0, 0);
        }

        var tx = Math.Tan(fieldX * Math.PI / 180.0);
        var ty = Math.Tan(fieldY * Math.PI / 180.0);
        var uz = 1 / Math.Sqrt(1 + (tx * tx) + (ty * ty));
        return (tx * uz, ty * uz);
    }

    private static bool IsObjectAtInfinity(Optic optic)
    {
        var objectSurface = optic.SurfaceGroup.Items.FirstOrDefault();
        return objectSurface is null
            || double.IsInfinity(objectSurface.CoordinateSystem.Origin.Z)
            || Math.Abs(objectSurface.Thickness) <= 1e-12;
    }

    private static double ImageToReferenceSphere(
        RayTraceSample ray,
        double centerX,
        double centerY,
        double centerZ,
        double radius,
        double imageIndex)
    {
        var direction = ray.Direction * -1;
        var relative = ray.Position - new Vector3D(centerX, centerY, centerZ);
        var a = Dot(direction, direction);
        var b = 2 * Dot(direction, relative);
        var c = Dot(relative, relative) - (radius * radius);
        var discriminant = Math.Max(0, (b * b) - (4 * a * c));
        var root = Math.Sqrt(discriminant);
        var t = (-b - root) / (2 * a);
        if (t < 0)
        {
            t = (-b + root) / (2 * a);
        }

        return imageIndex * t;
    }

    private static double Dot(Vector3D left, Vector3D right)
    {
        return (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);
    }
}

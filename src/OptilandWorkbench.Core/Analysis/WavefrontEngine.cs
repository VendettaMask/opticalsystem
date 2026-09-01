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
    double ImageRefractiveIndex = 1,
    bool ImageSpaceAfocal = false,
    double AfocalPupilDiameterMillimeters = 0)
{
    public double Rms => Samples.Where(sample => sample.Intensity > 0)
        .Select(sample => sample.OpdWaves * sample.OpdWaves)
        .DefaultIfEmpty(0)
        .Average() is var mean ? Math.Sqrt(mean) : 0;
}

public sealed record WavefrontReferenceSphere(
    double CenterX,
    double CenterY,
    double CenterZ,
    double Radius);

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
        bool aimAtStop = false,
        double pupilGridStretch = 1,
        bool zemaxCentered = false)
    {
        samplesAcrossPupil = Math.Max(2, samplesAcrossPupil);
        var pupilSamples = new List<PupilSample>();
        for (var row = 0; row < samplesAcrossPupil; row++)
        {
            var y = PupilGridCoordinate(
                row,
                samplesAcrossPupil,
                cellCentered,
                zemaxCentered) * pupilGridStretch;
            for (var column = 0; column < samplesAcrossPupil; column++)
            {
                var x = PupilGridCoordinate(
                    column,
                    samplesAcrossPupil,
                    cellCentered,
                    zemaxCentered) * pupilGridStretch;
                if ((x * x) + (y * y) <= 1)
                {
                    pupilSamples.Add(new PupilSample(x, y, 1));
                }
            }
        }

        return GenerateChiefRay(optic, field, wavelength, pupilSamples, aimAtStop);
    }

    private static double PupilGridCoordinate(
        int index,
        int sampleCount,
        bool cellCentered,
        bool zemaxCentered)
    {
        if (zemaxCentered && sampleCount % 2 == 0)
        {
            // Zemax places the chief-ray sample at index N/2 on even Wavefront
            // Map grids. The usable pupil radius is N/2-1, leaving the first
            // row/column outside the unit pupil for a 64x64 grid.
            return (index - (sampleCount / 2.0)) / Math.Max(1, (sampleCount / 2.0) - 1);
        }

        return cellCentered
            ? -1 + ((2.0 * index + 1) / sampleCount)
            : -1 + (2.0 * index / (sampleCount - 1.0));
    }

    public static WavefrontResult GenerateChiefRaySamples(
        Optic optic,
        (double Hx, double Hy) field,
        Wavelength wavelength,
        IReadOnlyList<(double X, double Y)> samples,
        bool aimAtStop = true,
        (double X, double Y)? resolvedRealImageLaunch = null,
        WavefrontReferenceSphere? referenceSphere = null,
        bool usePolarization = false)
    {
        return GenerateChiefRay(
            optic,
            field,
            wavelength,
            samples.Select(sample => new PupilSample(sample.X, sample.Y, 1)).ToArray(),
            aimAtStop,
            resolvedRealImageLaunch,
            referenceSphere,
            usePolarization);
    }

    public static WavefrontReferenceSphere CreateChiefRayReferenceSphere(
        Optic optic,
        (double Hx, double Hy) field,
        Wavelength wavelength,
        bool aimAtStop = true,
        (double X, double Y)? resolvedRealImageLaunch = null,
        bool usePolarization = false)
    {
        var chiefBundle = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(
            field.Hx,
            field.Hy,
            0,
            0,
            wavelength.Micrometers,
            aimAtStop,
            resolvedRealImageLaunch);
        if (usePolarization)
        {
            chiefBundle = WithPolarization(chiefBundle);
        }
        var chief = optic.SequentialRayTracer.TraceFinalSamples(chiefBundle).Single();
        if (chief is null)
        {
            throw new InvalidOperationException("Chief ray did not reach the image surface.");
        }

        var imagePosition = chief.Position;
        var imageSurfacePosition = optic.SurfaceGroup.Items.LastOrDefault()?.CoordinateSystem.Origin.Z
            ?? imagePosition.Z;
        var spherePupilZ = imageSurfacePosition
            + optic.Paraxial.EstimateExitPupilLocation(wavelength.Micrometers);
        var radius = Math.Sqrt(
            (imagePosition.X * imagePosition.X)
            + (imagePosition.Y * imagePosition.Y)
            + ((imagePosition.Z - spherePupilZ) * (imagePosition.Z - spherePupilZ)));
        return new WavefrontReferenceSphere(
            imagePosition.X,
            imagePosition.Y,
            imagePosition.Z,
            radius);
    }

    private static WavefrontResult GenerateChiefRay(
        Optic optic,
        (double Hx, double Hy) field,
        Wavelength wavelength,
        IReadOnlyList<PupilSample> pupilSamples,
        bool aimAtStop,
        (double X, double Y)? resolvedRealImageLaunch = null,
        WavefrontReferenceSphere? referenceSphere = null,
        bool usePolarization = false)
    {
        var chiefBundle = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(
            field.Hx,
            field.Hy,
            0,
            0,
            wavelength.Micrometers,
            aimAtStop,
            resolvedRealImageLaunch);
        if (usePolarization)
        {
            chiefBundle = WithPolarization(chiefBundle);
        }
        var chief = optic.SequentialRayTracer.TraceFinalSamples(chiefBundle).Single();
        if (chief is null)
        {
            throw new InvalidOperationException("Chief ray did not reach the image surface.");
        }

        var imageIndex = (optic.SurfaceGroup.Items.LastOrDefault()?.MaterialAfter ?? optic.Materials.Resolve("Air"))
            .RefractiveIndex(wavelength.Nanometers);
        if (optic.ImageSpaceAfocal)
        {
            return GenerateAfocalChiefRay(
                optic,
                field,
                wavelength,
                pupilSamples,
                aimAtStop,
                resolvedRealImageLaunch,
                usePolarization,
                chief,
                imageIndex);
        }

        var sphere = referenceSphere ?? CreateChiefRayReferenceSphere(
            optic,
            field,
            wavelength,
            aimAtStop,
            resolvedRealImageLaunch,
            usePolarization);
        var radius = sphere.Radius;
        var chiefImagePath = ImageToReferenceSphere(
            chief,
            sphere.CenterX,
            sphere.CenterY,
            sphere.CenterZ,
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
        if (usePolarization)
        {
            bundle = WithPolarization(bundle);
        }
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
                sphere.CenterX,
                sphere.CenterY,
                sphere.CenterZ,
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

    private static WavefrontResult GenerateAfocalChiefRay(
        Optic optic,
        (double Hx, double Hy) field,
        Wavelength wavelength,
        IReadOnlyList<PupilSample> pupilSamples,
        bool aimAtStop,
        (double X, double Y)? resolvedRealImageLaunch,
        bool usePolarization,
        RayTraceSample chief,
        double imageIndex)
    {
        var chiefDirection = Normalize(chief.Direction);
        var referenceOpticalPath = chief.CumulativeOpticalPathLength;
        var bundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(
            field.Hx,
            field.Hy,
            wavelength.Micrometers,
            pupilSamples,
            aimAtStop,
            resolvedRealImageLaunch);
        if (usePolarization)
        {
            bundle = WithPolarization(bundle);
        }

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

            var imagePath = ImageToReferencePlane(
                ray,
                chief.Position,
                chiefDirection,
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
                Dot(Normalize(ray.Direction), chiefDirection)));
        }

        return new WavefrontResult(
            samples,
            double.PositiveInfinity,
            referenceOpticalPath,
            vignetted,
            1,
            imageIndex,
            ImageSpaceAfocal: true,
            AfocalPupilDiameterMillimeters:
                ImageSpaceAnalysisSupport.AfocalDiffractionPupilDiameterMillimeters(optic));
    }

    private static RealRayBundle WithPolarization(RealRayBundle bundle)
    {
        return new RealRayBundle(bundle.Rays.Select(ray => ray with
        {
            PolarizationMatrix = Matrix3x3.Identity
        }));
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
        else if (optic.FieldDefinition == FieldDefinitionKind.RealImageHeight
            && ObjectConjugate.IsInfinite(optic.SurfaceGroup.Items.FirstOrDefault()))
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

    private static double ImageToReferencePlane(
        RayTraceSample ray,
        Vector3D planePoint,
        Vector3D planeNormal,
        double imageIndex)
    {
        var direction = Normalize(ray.Direction);
        var denominator = Dot(direction, planeNormal);
        if (Math.Abs(denominator) <= 1e-30)
        {
            return 0;
        }

        var t = Dot(ray.Position - planePoint, planeNormal) / denominator;
        return imageIndex * t;
    }

    private static double Dot(Vector3D left, Vector3D right)
    {
        return (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);
    }

    private static Vector3D Normalize(Vector3D value)
    {
        var length = value.Length;
        return length <= 1e-30 ? Vector3D.Zero : value / length;
    }
}

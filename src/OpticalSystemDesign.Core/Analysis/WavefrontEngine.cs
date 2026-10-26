using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Raytrace;

namespace OpticalSystemDesign.Core.Analysis;

public sealed record WavefrontSample(
    double NormalizedPupilX,
    double NormalizedPupilY,
    double PupilX,
    double PupilY,
    double PupilZ,
    double OpdWaves,
    double Intensity,
    double ImageDirectionZ = 1)
{
    /// <summary>Power at launch, before material/coating attenuation; null for external samples.</summary>
    public double? LaunchIntensity { get; init; }
    /// <summary>Traced global image-space direction, retained for planar propagation.</summary>
    public Vector3D? ImageDirection { get; init; }
}

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
    /// <summary>Physical chief-ray OPL to the image, before reference-sphere subtraction.</summary>
    public double? ChiefOpticalPathLength { get; init; }
    public bool? UseRayAiming { get; init; }
    public (double Hx, double Hy)? SourceField { get; init; }
    public double? SourceWavelengthNanometers { get; init; }
    public double? SourceReferenceWavelengthNanometers { get; init; }
    public PupilGridSpecification? PupilGrid { get; init; }
    public PupilSourceIdentity? SourceIdentity { get; init; }
    public WavefrontReferenceSphere? ReferenceSphere { get; init; }
    // Datum of the existing scalar Huygens weighting convention. This is not
    // a physical reference-sphere normal; keep that distinction explicit.
    public Vector3D HuygensCoordinateOrigin { get; init; }

    public double Rms => Samples.Where(sample => sample.Intensity > 0)
        .Select(sample => sample.OpdWaves * sample.OpdWaves)
        .DefaultIfEmpty(0)
        .Average() is var mean ? Math.Sqrt(mean) : 0;
}

public sealed record PupilGridSpecification(
    int SampleCount, bool CellCentered, bool ZemaxCentered, double Stretch)
{
    internal double? ExplicitCoordinateStep { get; init; }
    internal double? ExplicitCoordinateStepY { get; init; }
    internal double? StretchY { get; init; }
    internal bool UseCachedFftStopMap { get; init; }
    internal double? CoordinateDivisor { get; init; }
    internal bool ClipInBaseGrid { get; init; }

    public double Coordinate(int index) => Coordinate(index, Stretch, ExplicitCoordinateStep);

    internal double YCoordinate(int index) => Coordinate(index, StretchY ?? Stretch,
        ExplicitCoordinateStepY ?? ExplicitCoordinateStep);

    private double Coordinate(int index, double stretch, double? explicitStep) => CoordinateDivisor is { } divisor
        ? BaseCoordinate(index) / divisor
        : explicitStep is { } step
        ? (index - SampleCount / 2d) * step
        : stretch * BaseCoordinate(index);

    private double BaseCoordinate(int index) => ZemaxCentered && SampleCount % 2 == 0
        ? (index - SampleCount / 2.0) / Math.Max(1, SampleCount / 2.0 - 1)
        : CellCentered
            ? -1 + (2.0 * index + 1) / SampleCount
            : -1 + 2.0 * index / (SampleCount - 1.0);

    internal bool ContainsSample(int column, int row)
    {
        if (ClipInBaseGrid && CoordinateDivisor is { } divisor)
        {
            var baseX = BaseCoordinate(column);
            var baseY = BaseCoordinate(row);
            return baseX * baseX + baseY * baseY <= divisor * divisor;
        }
        var x = Coordinate(column); var y = YCoordinate(row);
        return x * x + y * y <= 1;
    }

    internal int ColumnIndex(double coordinate) => Index(coordinate, Stretch, ExplicitCoordinateStep);
    internal int RowIndex(double coordinate) => Index(coordinate, StretchY ?? Stretch,
        ExplicitCoordinateStepY ?? ExplicitCoordinateStep);

    private int Index(double coordinate, double stretch, double? step) => (int)Math.Round(CoordinateDivisor is { } divisor
        ? coordinate * divisor * (SampleCount / 2d - 1) + SampleCount / 2d
        : step is { } spacing
        ? coordinate / spacing + SampleCount / 2d
        : ZemaxCentered && SampleCount % 2 == 0
            ? coordinate / stretch * (SampleCount / 2d - 1) + SampleCount / 2d
            : (coordinate / stretch + 1) / 2 * (SampleCount - 1));
}

public sealed record WavefrontReferenceSphere(
    double CenterX,
    double CenterY,
    double CenterZ,
    double Radius)
{
    /// <summary>Use the forward intersection for a virtual exit-pupil reference.</summary>
    public bool IsVirtual { get; init; }
}

public static class WavefrontEngine
{
    // Explicit image-side plane reference, without toggling the optic's physical
    // afocal flag, changing its source identity or detaching a shared snapshot.
    internal static WavefrontResult GenerateImagePlaneUniform(Optic optic, (double Hx, double Hy) field,
        Wavelength wavelength, int sampling, bool aimAtStop)
    {
        var pupils = Enumerable.Range(0, sampling).SelectMany(row => Enumerable.Range(0, sampling)
            .Select(column => new PupilSample(-1 + (2d * column + 1) / sampling,
                -1 + (2d * row + 1) / sampling, 1)))
            .Where(p => p.X * p.X + p.Y * p.Y <= 1).ToArray();
        return GenerateChiefRay(optic, field, wavelength, pupils, aimAtStop, forceReferencePlane: true)
            with
        { PupilGrid = new(sampling, true, false, 1), SourceIdentity = PupilSourceIdentity.Capture(optic) };
    }
    public static WavefrontResult GenerateChiefRay(
        Optic optic,
        (double Hx, double Hy) field,
        Wavelength wavelength,
        int numRings,
        bool aimAtStop = false)
    {
        var pupilSamples = ApertureSampler.GenerateHexapolarRings(numRings);
        return GenerateChiefRay(optic, field, wavelength, pupilSamples, aimAtStop);
    }

    public static WavefrontResult GenerateChiefRayUniform(
        Optic optic,
        (double Hx, double Hy) field,
        Wavelength wavelength,
        int samplesAcrossPupil,
        bool cellCentered = false,
        bool aimAtStop = false,
        double pupilGridStretch = 1,
        bool zemaxCentered = false,
        Wavelength? referenceWavelength = null)
    {
        samplesAcrossPupil = Math.Max(2, samplesAcrossPupil);
        return GenerateChiefRayGrid(optic, field, wavelength,
            new(samplesAcrossPupil, cellCentered, zemaxCentered, pupilGridStretch),
            aimAtStop, referenceWavelength, null);
    }

    internal static WavefrontResult GenerateChiefRayGrid(
        Optic optic, (double Hx, double Hy) field, Wavelength wavelength,
        PupilGridSpecification grid, bool aimAtStop, Wavelength? referenceWavelength,
        WavefrontReferenceSphere? referenceSphere)
    {
        var pupilSamples = new List<PupilSample>();
        for (var row = 0; row < grid.SampleCount; row++)
        {
            var y = grid.YCoordinate(row);
            for (var column = 0; column < grid.SampleCount; column++)
            {
                var x = grid.Coordinate(column);
                if (grid.ContainsSample(column, row))
                {
                    pupilSamples.Add(new PupilSample(x, y, 1));
                }
            }
        }

        return GenerateChiefRay(optic, field, wavelength, pupilSamples, aimAtStop,
            referenceWavelength: referenceWavelength, referenceSphere: referenceSphere,
            useCachedFftStopMap: grid.UseCachedFftStopMap && grid.SampleCount >= 32) with
        {
            PupilGrid = grid,
            SourceIdentity = PupilSourceIdentity.Capture(optic)
        };
    }

    public static WavefrontResult GenerateChiefRaySamples(
        Optic optic,
        (double Hx, double Hy) field,
        Wavelength wavelength,
        IReadOnlyList<(double X, double Y)> samples,
        bool aimAtStop = true,
        (double X, double Y)? resolvedRealImageLaunch = null,
        WavefrontReferenceSphere? referenceSphere = null,
        bool usePolarization = false,
        Wavelength? referenceWavelength = null)
    {
        return GenerateChiefRay(
            optic,
            field,
            wavelength,
            samples.Select(sample => new PupilSample(sample.X, sample.Y, 1)).ToArray(),
            aimAtStop,
            resolvedRealImageLaunch,
            referenceSphere,
            usePolarization,
            referenceWavelength);
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
        var chief = TraceFinalSamples(optic, chiefBundle, usePolarization).Single();
        if (chief is null)
        {
            throw new InvalidOperationException("Chief ray did not reach the image surface.");
        }

        var imagePosition = chief.Position;
        var imageFrame = optic.SurfaceGroup.Items[^1].CoordinateSystem;
        // The formal scalar paraxial tracer measures its image-relative pupil
        // distance along global Z, not along a tilted detector's local normal.
        // Anchor that axial reference at the image vertex in the same global
        // frame as the chief intercept; world X/Y=0 is not the pupil center.
        // This repairs rigid translations of the paraxial model. Arbitrary
        // decentered/folded systems still require an aberrated 3-D pupil model.
        var pupilPosition = imageFrame.Origin + new Vector3D(0, 0,
            optic.Paraxial.EstimateExitPupilLocation(wavelength.Micrometers));
        var radius = (imagePosition - pupilPosition).Length;
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
        bool usePolarization = false,
        Wavelength? referenceWavelength = null,
        bool forceReferencePlane = false,
        bool useCachedFftStopMap = false)
    {
        var chiefBundle = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(
            field.Hx,
            field.Hy,
            0,
            0,
            wavelength.Micrometers,
            aimAtStop,
            resolvedRealImageLaunch);
        var chief = TraceFinalSamples(optic, chiefBundle, usePolarization).Single();
        if (chief is null)
        {
            throw new InvalidOperationException("Chief ray did not reach the image surface.");
        }

        var imageIndex = (optic.SurfaceGroup.Items.LastOrDefault()?.MaterialAfter ?? optic.Materials.Resolve("Air"))
            .RefractiveIndex(wavelength.Nanometers);
        if (optic.ImageSpaceAfocal)
            referenceWavelength ??= optic.Wavelengths.FirstOrDefault(item => item.IsPrimary) ?? wavelength;
        var referenceChief = referenceWavelength is null || referenceWavelength.Nanometers == wavelength.Nanometers
            ? chief
            : optic.SequentialRayTracer.TraceFinalSamples(optic.SequentialRayTracer.RayGenerator.GenerateGeneric(
                field.Hx, field.Hy, 0, 0, referenceWavelength.Micrometers, aimAtStop, resolvedRealImageLaunch)).Single()
                ?? throw new InvalidOperationException("Reference chief ray did not reach the image surface.");
        if (optic.ImageSpaceAfocal || forceReferencePlane)
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
                imageIndex,
                referenceChief,
                referenceWavelength?.Nanometers ?? wavelength.Nanometers,
                forceReferencePlane,
                chiefBundle);
        }

        var sphere = referenceSphere ?? CreateChiefRayReferenceSphere(
            optic,
            field,
            wavelength,
            aimAtStop,
            resolvedRealImageLaunch,
            usePolarization);
        if (referenceSphere is null && referenceWavelength is not null)
        {
            sphere = sphere with
            {
                CenterX = referenceChief.Position.X,
                CenterY = referenceChief.Position.Y,
                CenterZ = referenceChief.Position.Z
            };
        }
        var radius = sphere.Radius;
        var chiefImagePath = ImageToReferenceSphere(
            chief,
            sphere.CenterX,
            sphere.CenterY,
            sphere.CenterZ,
            sphere.IsVirtual ? -radius : radius,
            imageIndex);
        var referenceOpticalPath = chief.CumulativeOpticalPathLength - chiefImagePath;

        var generator = optic.SequentialRayTracer.RayGenerator;
        var bundle = useCachedFftStopMap && aimAtStop
            && generator.TryGenerateCachedFftPupil(field.Hx, field.Hy, wavelength.Micrometers,
                pupilSamples, out var cachedBundle) ? cachedBundle!
            : generator.GenerateNormalizedPupilSamples(
            field.Hx,
            field.Hy,
            wavelength.Micrometers,
            pupilSamples,
            aimAtStop,
            resolvedRealImageLaunch);
        var finalSamples = TraceFinalSamples(optic, bundle, usePolarization);
        var (ux, uy) = LaunchTiltDirection(optic, field, aimAtStop);
        var entrancePupilRadius = optic.Paraxial.EstimateEntrancePupilDiameter() / 2;
        var vignetting = optic.SequentialRayTracer.RayGenerator.GetPupilVignetting(field.Hx, field.Hy);
        var chiefPupil = vignetting.Transform(0, 0);
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
                sphere.IsVirtual ? -radius : radius,
                imageIndex);
            var tilt = EntrancePupilPhasePath((ux, uy), entrancePupilRadius,
                vignetting.Transform(pupil.X, pupil.Y), chiefPupil);
            if (ObjectConjugate.IsInfinite(optic.SurfaceGroup.Items.FirstOrDefault())
                && (aimAtStop || chiefBundle.Rays[0].Direction.Z < 0))
            {
                // Stop aiming changes the entrance coordinates, and backward
                // fields have pupil-dependent upstream launch distances. Refer
                // both to the actual incident plane wave rather than assuming
                // a translated paraxial entrance-pupil grid.
                tilt = Dot(bundle.Rays[index].Origin - chiefBundle.Rays[0].Origin,
                    chiefBundle.Rays[0].Direction);
            }
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
                ray.Direction.Z)
            { LaunchIntensity = bundle.Rays[index].Intensity, ImageDirection = ray.Direction });
        }

        return new WavefrontResult(
            samples,
            radius,
            referenceOpticalPath,
            vignetted,
            chief.Direction.Z,
            imageIndex)
        {
            ChiefOpticalPathLength = chief.CumulativeOpticalPathLength,
            UseRayAiming = aimAtStop,
            SourceField = field,
            SourceWavelengthNanometers = wavelength.Nanometers,
            SourceReferenceWavelengthNanometers = referenceWavelength?.Nanometers ?? wavelength.Nanometers,
            ReferenceSphere = sphere,
            HuygensCoordinateOrigin = optic.SurfaceGroup.Items.Count > 1
                ? optic.SurfaceGroup.Items[1].CoordinateSystem.Origin : Vector3D.Zero
        };
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
        double imageIndex,
        RayTraceSample referenceChief,
        double referenceWavelengthNanometers,
        bool forceImageReference,
        RealRayBundle chiefBundle)
    {
        var chiefDirection = Normalize(referenceChief.Direction);
        var referencePoint = referenceChief.Position;
        if (!forceImageReference)
        {
            if (optic.ReferenceOpd == ReferenceOpdMode.Absolute2)
            {
                var image = optic.SurfaceGroup.Items[^1].CoordinateSystem;
                referencePoint = image.Origin;
                chiefDirection = Normalize(image.ToGlobalDirection(new Vector3D(0, 0, 1)));
            }
            else if (optic.ReferenceOpd == ReferenceOpdMode.ExitPupil)
            {
                var distance = optic.SurfaceGroup.Items.Any(s => s.Geometry is PolygonGroupGeometry)
                    ? ImageSpaceAnalysisSupport.CompoundExitPupil(optic).Position
                    : optic.Paraxial.EstimateExitPupilLocation(referenceWavelengthNanometers / 1000);
                if (!double.IsFinite(distance))
                    throw new InvalidOperationException("Afocal exit-pupil OPD requires a finite exit-pupil reference position.");
                var frame = optic.SurfaceGroup.Items[^1].CoordinateSystem;
                var axialDirection = frame.ToLocalDirection(chiefDirection).Z;
                if (Math.Abs(axialDirection) <= 1e-12)
                    throw new InvalidOperationException("The reference chief ray is parallel to the exit-pupil location plane.");
                referencePoint = referenceChief.Position + chiefDirection
                    * ((distance - frame.ToLocalPoint(referenceChief.Position).Z) / axialDirection);
            }
            else if (optic.ReferenceOpd == ReferenceOpdMode.Infinity)
                throw new NotSupportedException("The infinity OPD reference is not defined for an afocal image; select ExitPupil, Absolute or Absolute2.");
        }
        var referenceOpticalPath = chief.CumulativeOpticalPathLength
            - ImageToReferencePlane(chief, referencePoint, chiefDirection, imageIndex);
        var bundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(
            field.Hx,
            field.Hy,
            wavelength.Micrometers,
            pupilSamples,
            aimAtStop,
            resolvedRealImageLaunch);
        var finalSamples = TraceFinalSamples(optic, bundle, usePolarization);
        var (ux, uy) = LaunchTiltDirection(optic, field, aimAtStop);
        var entrancePupilRadius = optic.Paraxial.EstimateEntrancePupilDiameter() / 2;
        var vignetting = optic.SequentialRayTracer.RayGenerator.GetPupilVignetting(field.Hx, field.Hy);
        var chiefPupil = vignetting.Transform(0, 0);
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
                referencePoint,
                chiefDirection,
                imageIndex);
            var tilt = EntrancePupilPhasePath((ux, uy), entrancePupilRadius,
                vignetting.Transform(pupil.X, pupil.Y), chiefPupil);
            if (ObjectConjugate.IsInfinite(optic.SurfaceGroup.Items.FirstOrDefault())
                && (aimAtStop || chiefBundle.Rays[0].Direction.Z < 0))
                tilt = Dot(bundle.Rays[index].Origin - chiefBundle.Rays[0].Origin, chiefBundle.Rays[0].Direction);
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
                Dot(Normalize(ray.Direction), chiefDirection))
            { LaunchIntensity = bundle.Rays[index].Intensity, ImageDirection = ray.Direction });
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
                ImageSpaceAnalysisSupport.AfocalDiffractionPupilDiameterMillimeters(optic))
        {
            ChiefOpticalPathLength = chief.CumulativeOpticalPathLength,
            UseRayAiming = aimAtStop,
            SourceField = field,
            SourceWavelengthNanometers = wavelength.Nanometers,
            SourceReferenceWavelengthNanometers = referenceWavelengthNanometers
        };
    }

    private static IReadOnlyList<RayTraceSample?> TraceFinalSamples(Optic optic, RealRayBundle bundle, bool usePolarization)
    {
        if (!usePolarization) return optic.SequentialRayTracer.TraceFinalSamples(bundle);
        if (bundle.Rays.Count > SequentialTraceLimits.MaximumRayCount)
            throw new ArgumentOutOfRangeException(nameof(bundle), "Polarized wavefront exceeds the sequential ray-count safety budget.");
        // Use the same formal Jones-chain power transport as polarized spot metrics.
        // Diagnostic continuation is never accepted outside an actual physical aperture.
        // Coating attenuation is included exactly once; the obsolete real matrix marker
        // did not provide Fresnel power or the system's Jones input.
        return bundle.Rays.Select(ray =>
        {
            var diagnostic = optic.SequentialRayTracer.DiagnoseApertures(ray, usePolarization: true);
            return diagnostic.InsideAllApertures && diagnostic.UnclippedImage is { } image
                && diagnostic.PolarizationWeightedIntensity is { } intensity
                ? image with { Intensity = intensity } : null;
        }).ToArray();
    }

    internal static double EntrancePupilPhasePath(
        (double X, double Y) direction,
        double pupilRadius,
        (double X, double Y) pupil,
        (double X, double Y) reference = default) =>
        pupilRadius * (direction.X * (pupil.X - reference.X)
            + direction.Y * (pupil.Y - reference.Y));

    internal static (double X, double Y) LaunchTiltDirection(
        Optic optic,
        (double Hx, double Hy) field,
        bool aimAtStop = false)
    {
        // Finite-object rays already accumulate the path from their common
        // object point. Only a plane wave needs the entrance-pupil phase term.
        if (!ObjectConjugate.IsInfinite(optic.SurfaceGroup.Items.FirstOrDefault()))
        {
            return (0, 0);
        }

        double fieldX;
        double fieldY;
        if (optic.FieldDefinition == FieldDefinitionKind.Angle)
        {
            (fieldX, fieldY) = FieldCoordinates.Denormalize(optic.Fields, field.Hx, field.Hy);
        }
        else if (optic.FieldDefinition == FieldDefinitionKind.RealImageHeight)
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
        // Match the launch hemisphere used by RayGenerator. Tangents alone
        // fold fields beyond 90 degrees onto the opposite incoming plane wave.
        var hemisphere = Math.Abs(fieldX) > 90 || Math.Abs(fieldY) > 90 ? -1 : 1;
        return (hemisphere * tx * uz, hemisphere * ty * uz);
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
        if (radius < 0)
            return imageIndex * ((-b - root) / (2 * a));
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

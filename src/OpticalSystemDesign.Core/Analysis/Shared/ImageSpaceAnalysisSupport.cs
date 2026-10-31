using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Rays;

namespace OpticalSystemDesign.Core.Analysis;

internal enum ImageSpaceCoordinateKind
{
    ImageHeight,
    DirectionCosine,
    AfocalAngle
}

internal readonly record struct ImageSpaceCoordinateDescriptor(
    ImageSpaceCoordinateKind Kind,
    string AxisUnitLabel,
    string MetricUnitLabel,
    AnalysisAxisQuantity Quantity,
    AnalysisAxisUnit Unit,
    double MetricScale = 1)
{
    public bool IsAfocalAngle => Kind == ImageSpaceCoordinateKind.AfocalAngle;
}

internal static class ImageSpaceAnalysisSupport
{
    public const double MilliradiansPerRadian = 1_000.0;

    public const string MilliradianLabel = "mrad";

    public const string CyclesPerMilliradianLabel = "cycles/mrad";

    public const string DiopterLabel = "D";

    public static ImageSpaceCoordinateDescriptor CoordinateDescriptor(
        Optic optic,
        int surfaceNumber = -1,
        bool directionCosines = false)
    {
        if (directionCosines)
        {
            return new ImageSpaceCoordinateDescriptor(
                ImageSpaceCoordinateKind.DirectionCosine,
                "direction cosine",
                string.Empty,
                AnalysisAxisQuantity.ImageHeight,
                AnalysisAxisUnit.Dimensionless);
        }

        return UsesAfocalAngleCoordinates(optic, surfaceNumber)
            ? new ImageSpaceCoordinateDescriptor(
                ImageSpaceCoordinateKind.AfocalAngle,
                MilliradianLabel,
                MilliradianLabel,
                AnalysisAxisQuantity.IncidentAngle,
                AnalysisAxisUnit.Milliradian)
            : new ImageSpaceCoordinateDescriptor(
                ImageSpaceCoordinateKind.ImageHeight,
                "mm",
                "µm",
                AnalysisAxisQuantity.ImageHeight,
                AnalysisAxisUnit.Millimeter,
                MetricScale: 1_000);
    }

    public static bool UsesAfocalAngleCoordinates(Optic optic, int surfaceNumber = -1)
    {
        if (!optic.ImageSpaceAfocal || optic.SurfaceGroup.Items.Count == 0)
        {
            return false;
        }

        var surfaceIndex = ResolveSurfaceIndex(optic, surfaceNumber);
        return surfaceIndex == optic.SurfaceGroup.Items.Count - 1;
    }

    public static int ResolveSurfaceIndex(Optic optic, int surfaceNumber)
    {
        if (optic.SurfaceGroup.Items.Count == 0)
        {
            return -1;
        }

        if (surfaceNumber < 0)
        {
            return optic.SurfaceGroup.Items.Count - 1;
        }

        return optic.SurfaceGroup.Items
            .Select((surface, index) => (surface, index))
            .Where(item => item.surface.Number == surfaceNumber)
            .Select(item => item.index)
            .DefaultIfEmpty(-1)
            .First();
    }

    public static OpticalSurface? ResolveSurface(Optic optic, int surfaceNumber)
    {
        var index = ResolveSurfaceIndex(optic, surfaceNumber);
        return index < 0 ? null : optic.SurfaceGroup.Items[index];
    }

    public static SpotRayData ToImageSpaceRayData(
        Optic optic,
        RayTraceSample sample,
        OpticalSurface targetSurface,
        ImageSpaceCoordinateDescriptor descriptor,
        double defocus = 0)
    {
        var localDirection = targetSurface.CoordinateSystem.ToLocalDirection(sample.Direction);
        if (descriptor.Kind == ImageSpaceCoordinateKind.DirectionCosine)
        {
            return new SpotRayData(
                localDirection.X,
                localDirection.Y,
                sample.Intensity);
        }

        if (descriptor.Kind == ImageSpaceCoordinateKind.AfocalAngle)
        {
            var angle = DirectionAnglesMilliradians(targetSurface, sample.Direction);
            if (Math.Abs(defocus) > 1e-12)
            {
                var height = targetSurface.CoordinateSystem.ToLocalPoint(sample.Position);
                angle = (
                    angle.X + (height.X * defocus),
                    angle.Y + (height.Y * defocus));
            }

            return new SpotRayData(angle.X, angle.Y, sample.Intensity);
        }

        var position = targetSurface.CoordinateSystem.ToLocalPoint(sample.Position);
        if (Math.Abs(defocus) > 1e-12
            && Math.Abs(localDirection.Z) > 1e-12)
        {
            position += localDirection * (defocus / localDirection.Z);
        }

        return new SpotRayData(position.X, position.Y, sample.Intensity);
    }

    public static (double X, double Y) DirectionAnglesMilliradians(
        OpticalSurface referenceSurface,
        Vector3D globalDirection)
    {
        var local = Normalize(referenceSurface.CoordinateSystem.ToLocalDirection(globalDirection));
        if (local.Length <= 1e-30)
        {
            return (0, 0);
        }

        return (
            Math.Atan2(local.X, local.Z) * MilliradiansPerRadian,
            Math.Atan2(local.Y, local.Z) * MilliradiansPerRadian);
    }

    public static bool UsesAfocalFrequency(Optic optic) => optic.ImageSpaceAfocal;

    public static AnalysisAxisUnit SpatialFrequencyUnit(Optic optic) =>
        UsesAfocalFrequency(optic)
            ? AnalysisAxisUnit.CyclesPerMilliradian
            : AnalysisAxisUnit.CyclesPerMillimeter;

    public static string SpatialFrequencyLabel(Optic optic) =>
        UsesAfocalFrequency(optic)
            ? $"Frequency ({CyclesPerMilliradianLabel})"
            : "Frequency (cycles/mm)";

    public static string SpatialFrequencyUnitLabel(Optic optic) =>
        UsesAfocalFrequency(optic)
            ? CyclesPerMilliradianLabel
            : "cycles/mm";

    public static AnalysisAxisUnit DefocusUnit(Optic optic) =>
        optic.ImageSpaceAfocal ? AnalysisAxisUnit.Diopter : AnalysisAxisUnit.Millimeter;

    public static string DefocusLabel(Optic optic) =>
        optic.ImageSpaceAfocal ? $"Defocus ({DiopterLabel})" : "Defocus (mm)";

    public static string DefocusUnitLabel(Optic optic) =>
        optic.ImageSpaceAfocal ? DiopterLabel : "mm";

    public static string FocusStepKey(Optic optic) =>
        optic.ImageSpaceAfocal ? "FocusStepDiopters" : "FocusStep";

    public static double AfocalAiryRadiusMilliradians(
        Optic optic,
        Wavelength wavelength)
    {
        var diameter = AfocalDiffractionPupilDiameterMillimeters(optic);
        if (diameter <= 1e-30)
        {
            return 0;
        }

        return 1.22 * wavelength.Micrometers / diameter;
    }

    public static double AfocalCutoffFrequencyCyclesPerMilliradian(
        Optic optic,
        Wavelength wavelength)
    {
        var diameter = AfocalDiffractionPupilDiameterMillimeters(optic);
        if (diameter <= 1e-30)
        {
            return 0;
        }

        return diameter / wavelength.Micrometers;
    }

    public static double AfocalDiffractionPupilDiameterMillimeters(Optic optic)
    {
        if (optic.SurfaceGroup.Items.Any(s => s.Geometry is PolygonGroupGeometry))
            return TangentialExitPupilDiameter(optic);
        var image = optic.SurfaceGroup.Items[^1].CoordinateSystem;
        var axis = image.ToGlobalDirection(new Vector3D(0, 0, 1));
        if (optic.SurfaceGroup.Items.Skip(1).Any(surface =>
            Math.Abs(Dot(surface.CoordinateSystem.ToGlobalDirection(new Vector3D(0, 0, 1)), axis)) < 1 - 1e-12))
            return TangentialExitPupilDiameter(optic);
        if (OpticalSystemDesign.Core.Services.AfocalParabasalPupilScale.TryCompute(optic, out var tracedDiameter, out _))
            return tracedDiameter;
        var exit = Math.Abs(optic.Paraxial.EstimateExitPupilDiameter());
        if (double.IsFinite(exit) && exit > 1e-12)
        {
            return exit;
        }

        var entrance = Math.Abs(optic.Paraxial.EstimateEntrancePupilDiameter());
        return double.IsFinite(entrance) && entrance > 1e-12 ? entrance : 0;
    }

    private static double TangentialExitPupilDiameter(Optic optic)
    {
        // An oblique or folded train can have different X/Y pupil scales.
        // The scalar sequential Y-Ybar estimate omits that projection. Measure
        // the tangential image of an infinitesimal pupil pencil instead.
        const double probe = 1e-3;
        // Diffraction uses the traced tangential beam on the chief-normal pupil
        // plane. A geometric stop-image fit provides its axial datum, not its
        // display radius; a one-sided compound pencil avoids a seam jump.
        var compound = optic.SurfaceGroup.Items.Any(s => s.Geometry is PolygonGroupGeometry);
        var wave = optic.Wavelengths.FirstOrDefault(w => w.IsPrimary) ?? optic.Wavelengths[0];
        var pupils = new[] { new OpticalSystemDesign.Core.Raytrace.PupilSample(0, compound ? 0 : -probe, 1),
            new OpticalSystemDesign.Core.Raytrace.PupilSample(0, probe, 1),
            new OpticalSystemDesign.Core.Raytrace.PupilSample(0, 0, 1) };
        var rays = optic.SequentialRayTracer.TraceFinalSamples(
            optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(0, 0,
                wave.Micrometers, pupils, optic.RayAimingEnabled));
        if (rays.Any(ray => ray is not { Vignetted: false, Intensity: > 0 }))
            throw new AnalysisDataUnavailableException("Afocal pupil", "tangential pupil pencil is blocked");
        var frame = optic.SurfaceGroup.Items[^1].CoordinateSystem;
        var normal = Normalize(rays[2]!.Direction);
        var distance = compound ? CompoundExitPupil(optic).Position
            : optic.Paraxial.EstimateExitPupilLocation(wave.Micrometers);
        var axialCosine = frame.ToLocalDirection(normal).Z;
        if (!double.IsFinite(distance) || Math.Abs(axialCosine) <= 1e-12)
            throw new AnalysisDataUnavailableException("Afocal pupil", "no finite exit reference plane");
        var origin = rays[2]!.Position + normal
            * ((distance - frame.ToLocalPoint(rays[2]!.Position).Z) / axialCosine);
        Vector3D Project(RayTraceSample ray)
        {
            var denominator = Dot(ray.Direction, normal);
            if (Math.Abs(denominator) <= 1e-12)
                throw new AnalysisDataUnavailableException("Afocal pupil", "pupil ray is tangent to its reference plane");
            return frame.ToLocalPoint(ray.Position + ray.Direction * (Dot(origin - ray.Position, normal) / denominator));
        }
        var interval = Project(rays[1]!) - Project(rays[0]!);
        // A compound train may rotate the entrance-Y pencil within the pupil
        // plane. Its physical interval must not depend on image-axis rotation.
        var diameter = compound ? 2 * interval.Length / probe : Math.Abs(interval.Y) / probe;
        if (!double.IsFinite(diameter) || diameter <= 1e-12)
            throw new AnalysisDataUnavailableException("Afocal pupil", "no finite tangential pupil diameter");
        return diameter;
    }

    internal static (double Position, double Diameter) CompoundExitPupil(Optic optic)
    {
        const double probe = 1e-3;
        var wavelength = optic.Wavelengths.FirstOrDefault(w => w.IsPrimary) ?? optic.Wavelengths[0];
        var result = ExitPupilGeometryEngine.Compute(optic, (0, 0), wavelength,
            new[] { (0d, 0d), (probe, 0d), (0d, probe) });
        return (optic.SurfaceGroup.Items[^1].CoordinateSystem.ToLocalPoint(result.Center).Z,
            2 * result.DisplayRadiusMillimeters / probe);
    }

    internal static (double X, double Y) AfocalFftPupilAspectStretch(
        Optic optic, (double Hx, double Hy) field, Wavelength wavelength, bool aimAtStop)
    {
        // The scalar angular pitch and the pupil's two spatial intervals are
        // separate: an oblique/folded exit pupil need not be circular. Preserve
        // entrance-pupil orientation and trace the aspect before building the
        // FFT grid, rather than resizing its intensity after the transform.
        const double probe = 1e-3;
        // A finite difference across a triangulation seam mixes two paths.
        // Compound pupils use the same chief-to-positive pencil for both axes.
        var positivePencil = optic.SurfaceGroup.Items.Any(surface => surface.Geometry is PolygonGroupGeometry);
        var pencil = WavefrontEngine.GenerateChiefRaySamples(optic, field, wavelength,
            positivePencil ? new[] { (0d, 0d), (probe, 0d), (0d, probe) }
                : new[] { (-probe, 0d), (probe, 0d), (0d, -probe), (0d, probe) },
            aimAtStop: aimAtStop, referenceWavelength: wavelength);
        if (pencil.Samples.Any(sample => sample.Intensity <= 0))
            throw new AnalysisDataUnavailableException("FFT PSF", "exit-pupil aspect pencil is blocked");
        Vector3D Point(int index) => new(pencil.Samples[index].PupilX,
            pencil.Samples[index].PupilY, pencil.Samples[index].PupilZ);
        var x = (Point(1) - Point(0)).Length;
        var y = positivePencil ? (Point(2) - Point(0)).Length : (Point(3) - Point(2)).Length;
        if (!double.IsFinite(x) || !double.IsFinite(y) || x <= 1e-15 || y <= 1e-15)
            throw new AnalysisDataUnavailableException("FFT PSF", "no finite exit-pupil aspect");
        // Even a small nonzero stretch affects the discrete circular boundary;
        // do not snap distinct physical intervals to a common unit interval.
        if (x == y) return (1, 1);
        var larger = Math.Max(x, y);
        return (larger / x, larger / y);
    }

    public static double AfocalDefocusOpdWaves(
        WavefrontSample sample,
        Wavelength wavelength,
        double defocusDiopters,
        double pupilDiameterMillimeters)
    {
        if (Math.Abs(defocusDiopters) <= 1e-30 || pupilDiameterMillimeters <= 1e-30)
        {
            return 0;
        }

        var pupilRadius = pupilDiameterMillimeters / 2.0;
        var radiusSquared = pupilRadius * pupilRadius
            * ((sample.NormalizedPupilX * sample.NormalizedPupilX)
                + (sample.NormalizedPupilY * sample.NormalizedPupilY));
        var opdMillimeters = -defocusDiopters * radiusSquared / 2_000.0;
        return opdMillimeters / (wavelength.Micrometers * 1e-3);
    }

    public static double FftSampleSpacingMilliradians(
        Optic optic,
        Wavelength wavelength,
        int pupilSampling,
        int gridSize,
        bool zemaxFftSampling,
        double pupilGridStretch)
    {
        var diameter = AfocalDiffractionPupilDiameterMillimeters(optic);
        if (diameter <= 1e-30)
        {
            return 0;
        }

        var sampleCount = zemaxFftSampling
            ? pupilSampling - 2
            : pupilSampling - 1;
        return wavelength.Micrometers * Math.Max(1, sampleCount)
            / (diameter * gridSize * Math.Max(1e-12, pupilGridStretch));
    }

    private static Vector3D Normalize(Vector3D value)
    {
        var length = value.Length;
        return length <= 1e-30 ? Vector3D.Zero : value / length;
    }

    private static double Dot(Vector3D left, Vector3D right) =>
        left.X * right.X + left.Y * right.Y + left.Z * right.Z;
}

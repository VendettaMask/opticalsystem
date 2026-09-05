using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Rays;

namespace OptilandWorkbench.Core.Analysis;

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
        var exit = Math.Abs(optic.Paraxial.EstimateExitPupilDiameter());
        if (double.IsFinite(exit) && exit > 1e-12)
        {
            return exit;
        }

        var entrance = Math.Abs(optic.Paraxial.EstimateEntrancePupilDiameter());
        return double.IsFinite(entrance) && entrance > 1e-12 ? entrance : 0;
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
        var opdMillimeters = defocusDiopters * radiusSquared / 2_000.0;
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
}

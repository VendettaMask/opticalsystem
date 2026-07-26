using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Interactions;

namespace OptilandWorkbench.Core.Services;

public sealed record ParaxialTrace(
    IReadOnlyList<IReadOnlyList<double>> Heights,
    IReadOnlyList<IReadOnlyList<double>> Slopes);

public sealed record CardinalPointEstimate(
    double EffectiveFocalLength,
    double FrontFocalPosition,
    double BackFocalPosition,
    double FrontPrincipalPlanePosition,
    double BackPrincipalPlanePosition,
    double FrontNodalPlanePosition,
    double BackNodalPlanePosition,
    double FirstReferencePosition,
    double LastReferencePosition);

public sealed class Paraxial
{
    private readonly Optic _optic;

    public Paraxial(Optic optic)
    {
        _optic = optic;
    }

    public double EstimateEffectiveFocalLength()
    {
        var matrix = TraceSystemMatrix(PrimaryWavelengthNanometers());
        return Math.Abs(matrix.C) < 1e-12 ? 0 : -1.0 / matrix.C;
    }

    public double EstimateFNumber()
    {
        var focalLength = Math.Abs(EstimateEffectiveFocalLength());
        var apertureDiameter = EstimateEntrancePupilDiameter();
        return apertureDiameter <= 0 || focalLength <= 0 ? 0 : focalLength / apertureDiameter;
    }

    public double EstimateEntrancePupilDiameter()
    {
        var fallbackDiameter = _optic.SurfaceGroup.ApertureRadius() * 2.0;
        return _optic.Aperture.Kind switch
        {
            ApertureKind.FNumber => Math.Abs(EstimateEffectiveFocalLength()) / Math.Max(1e-12, _optic.Aperture.Value),
            ApertureKind.NumericalAperture => EntrancePupilDiameterFromObjectNumericalAperture(),
            ApertureKind.FloatByStopSize => fallbackDiameter,
            _ => _optic.Aperture.Diameter(fallbackDiameter)
        };
    }

    private double EntrancePupilDiameterFromObjectNumericalAperture()
    {
        var objectSurface = _optic.SurfaceGroup.Items.FirstOrDefault();
        if (objectSurface is null || IsObjectAtInfinity(objectSurface))
        {
            throw new InvalidOperationException("Object numerical aperture requires a finite object surface.");
        }

        var wavelength = PrimaryWavelengthNanometers();
        var objectIndex = objectSurface.MaterialAfter.RefractiveIndex(wavelength);
        var sine = _optic.Aperture.Value / objectIndex;
        if (!double.IsFinite(sine) || sine <= 0 || sine > 1)
        {
            throw new InvalidOperationException("Object numerical aperture divided by object-space index must be in (0, 1].");
        }

        var objectPosition = objectSurface.CoordinateSystem.Origin.Z;
        var distance = EstimateEntrancePupilLocation() - objectPosition;
        return 2 * distance * Math.Tan(Math.Asin(sine));
    }

    public double EstimateEntrancePupilLocation()
    {
        var matrix = RayMatrix.Identity;
        var currentIndex = 1.0;
        var wavelengthNanometers = PrimaryWavelengthNanometers();
        var objectSurface = _optic.SurfaceGroup.Items.FirstOrDefault();

        foreach (var surface in _optic.SurfaceGroup.Items)
        {
            if (surface.IsStop)
            {
                break;
            }

            var nextIndex = surface.MaterialAfter.RefractiveIndex(wavelengthNanometers);
            matrix = Refract(matrix, surface.Radius, currentIndex, nextIndex);
            matrix = Translate(matrix, surface.Thickness);
            currentIndex = nextIndex;
        }

        if (Math.Abs(matrix.A) < 1e-12)
        {
            return 0;
        }

        var relativeLocation = matrix.B / matrix.A;
        return IsObjectAtInfinity(objectSurface)
            ? relativeLocation
            : (objectSurface?.CoordinateSystem.Origin.Z ?? 0) + relativeLocation;
    }

    public CardinalPointEstimate EstimateCardinalPoints()
    {
        var positions = SurfacePositions();
        if (positions.Count == 0)
        {
            return new CardinalPointEstimate(0, 0, 0, 0, 0, 0, 0, 0, 0);
        }

        var matrix = TraceSystemMatrix(PrimaryWavelengthNanometers());
        var effectiveFocalLength = Math.Abs(matrix.C) < 1e-12 ? 0 : -1.0 / matrix.C;
        var matrixStart = positions[0];
        var matrixEnd = matrixStart + _optic.SurfaceGroup.Items.Sum(surface => surface.Thickness);
        var firstReference = positions.Count > 1 ? positions[1] : positions[0];
        var lastReference = positions[^1];
        var frontFocalPosition = Math.Abs(matrix.C) < 1e-12
            ? firstReference
            : matrixStart + (matrix.D / matrix.C);
        var backFocalPosition = Math.Abs(matrix.C) < 1e-12
            ? lastReference
            : matrixEnd - (matrix.A / matrix.C);
        var frontPrincipalPlane = frontFocalPosition + effectiveFocalLength;
        var backPrincipalPlane = backFocalPosition - effectiveFocalLength;
        return new CardinalPointEstimate(
            effectiveFocalLength,
            frontFocalPosition,
            backFocalPosition,
            frontPrincipalPlane,
            backPrincipalPlane,
            frontPrincipalPlane,
            backPrincipalPlane,
            firstReference,
            lastReference);
    }

    public double EstimateExitPupilLocation()
    {
        return EstimateExitPupilLocation(PrimaryWavelengthNanometers() / 1000.0);
    }

    public double EstimateExitPupilLocation(double wavelengthMicrometers)
    {
        var stopIndex = _optic.SurfaceGroup.Items.ToList().FindIndex(surface => surface.IsStop);
        if (stopIndex < 0 || stopIndex >= _optic.SurfaceGroup.Items.Count - 1)
        {
            return 0;
        }

        var positions = SurfacePositions();
        var trace = TraceGeneric(
            new[] { 0.0 },
            new[] { 0.1 },
            positions[stopIndex],
            wavelengthMicrometers,
            stopIndex + 1);
        var finalHeight = trace.Heights[^1][0];
        var finalSlope = trace.Slopes[^1][0];
        return Math.Abs(finalSlope) <= 1e-12 ? 0 : -finalHeight / finalSlope;
    }

    public double EstimateExitPupilDiameter()
    {
        return EstimateExitPupilDiameter(PrimaryWavelengthNanometers() / 1000.0);
    }

    public double EstimateExitPupilDiameter(double wavelengthMicrometers)
    {
        var marginal = MarginalRay(wavelengthMicrometers);
        var imageHeight = marginal.Heights[^1][0];
        var imageSlope = marginal.Slopes[^1][0];
        return 2 * (imageHeight + (imageSlope * EstimateExitPupilLocation(wavelengthMicrometers)));
    }

    public ParaxialTrace TraceNormalizedPupil(
        double normalizedFieldY,
        IReadOnlyList<double> normalizedPupilY,
        double wavelengthMicrometers)
    {
        var positions = SurfacePositions();
        var firstSurfacePosition = positions.Count > 1 ? positions[1] : 0;
        var entrancePupilLocation = EstimateEntrancePupilLocation();
        var entrancePupilRadius = EstimateEntrancePupilDiameter() / 2;
        var fieldY = normalizedFieldY * FieldCoordinates.MaximumRadius(_optic.Fields);
        var pupilHeights = normalizedPupilY.Select(pupil => pupil * entrancePupilRadius).ToArray();
        var objectSurface = _optic.SurfaceGroup.Items.FirstOrDefault();
        var objectAtInfinity = IsObjectAtInfinity(objectSurface);
        double objectHeight;
        double objectPosition;

        switch (_optic.FieldDefinition)
        {
            case FieldDefinitionKind.ObjectHeight:
                if (objectAtInfinity)
                {
                    throw new InvalidOperationException("Object-height fields require a finite object surface.");
                }

                objectHeight = -fieldY;
                objectPosition = objectSurface?.CoordinateSystem.Origin.Z ?? 0;
                break;
            case FieldDefinitionKind.ParaxialImageHeight:
                (objectHeight, objectPosition) = ParaxialImageObjectPosition(
                    fieldY,
                    entrancePupilLocation,
                    firstSurfacePosition,
                    objectSurface,
                    objectAtInfinity);
                break;
            case FieldDefinitionKind.RealImageHeight:
                var launch = _optic.SequentialRayTracer.RayGenerator.ResolveRealImageFieldCoordinates(0, fieldY);
                if (objectAtInfinity)
                {
                    var slope = Math.Tan(launch.Y * Math.PI / 180.0);
                    objectHeight = -slope * entrancePupilLocation;
                    objectPosition = firstSurfacePosition;
                }
                else
                {
                    objectHeight = -launch.Y;
                    objectPosition = objectSurface?.CoordinateSystem.Origin.Z ?? 0;
                }

                break;
            default:
                var angleSlope = Math.Tan(fieldY * Math.PI / 180.0);
                objectPosition = objectAtInfinity
                    ? firstSurfacePosition
                    : objectSurface?.CoordinateSystem.Origin.Z ?? 0;
                objectHeight = -angleSlope * (entrancePupilLocation - objectPosition);
                break;
        }

        var heights = pupilHeights.Select(pupilHeight => objectAtInfinity
            ? pupilHeight + objectHeight
            : objectHeight).ToArray();
        var slopes = pupilHeights.Select((pupilHeight, index) =>
        {
            var denominator = entrancePupilLocation - objectPosition;
            return Math.Abs(denominator) <= 1e-15
                ? 0
                : (pupilHeight - heights[index]) / denominator;
        }).ToArray();
        return TraceGeneric(heights, slopes, objectPosition, wavelengthMicrometers);
    }

    public ParaxialTrace MarginalRay(double wavelengthMicrometers)
    {
        var positions = SurfacePositions();
        var firstSurfacePosition = positions.Count > 1 ? positions[1] : 0;
        var objectSurface = _optic.SurfaceGroup.Items.FirstOrDefault();
        if (IsObjectAtInfinity(objectSurface))
        {
            return TraceGeneric(
                new[] { EstimateEntrancePupilDiameter() / 2 },
                new[] { 0.0 },
                firstSurfacePosition - 10,
                wavelengthMicrometers);
        }

        var objectPosition = objectSurface?.CoordinateSystem.Origin.Z ?? 0;
        var pupilDistance = EstimateEntrancePupilLocation() - objectPosition;
        var slope = Math.Abs(pupilDistance) <= 1e-15
            ? 0
            : EstimateEntrancePupilDiameter() / (2 * pupilDistance);
        return TraceGeneric(new[] { 0.0 }, new[] { slope }, objectPosition, wavelengthMicrometers);
    }

    public ParaxialTrace ChiefRay(double wavelengthMicrometers)
    {
        var maximumRadius = FieldCoordinates.MaximumRadius(_optic.Fields);
        var maximumY = _optic.Fields
            .OrderByDescending(field => Math.Abs(field.Y))
            .Select(field => field.Y)
            .FirstOrDefault();
        var normalizedY = maximumRadius <= 1e-15 ? 0 : maximumY / maximumRadius;
        return TraceNormalizedPupil(normalizedY, new[] { 0.0 }, wavelengthMicrometers);
    }

    private (double Height, double Position) ParaxialImageObjectPosition(
        double imageHeight,
        double entrancePupilLocation,
        double firstSurfacePosition,
        OpticalSurface? objectSurface,
        bool objectAtInfinity)
    {
        var (imageHeightUnit, objectHeightUnit, objectSlopeUnit) = TraceUnitChiefRay();
        if (Math.Abs(imageHeightUnit) <= 1e-15)
        {
            throw new InvalidOperationException("The paraxial image height cannot be resolved for this optical system.");
        }

        if (objectAtInfinity)
        {
            var slope = objectSlopeUnit * imageHeight / imageHeightUnit;
            return (-slope * entrancePupilLocation, firstSurfacePosition);
        }

        return (
            objectHeightUnit * imageHeight / imageHeightUnit,
            objectSurface?.CoordinateSystem.Origin.Z ?? 0);
    }

    private (double ImageHeight, double ObjectHeight, double ObjectSlope) TraceUnitChiefRay()
    {
        var surfaces = _optic.SurfaceGroup.Items;
        var stopIndex = surfaces.ToList().FindIndex(surface => surface.IsStop);
        if (stopIndex < 0)
        {
            throw new InvalidOperationException("Image-height fields require an aperture stop.");
        }

        var positions = SurfacePositions();
        var wavelength = PrimaryWavelengthNanometers() / 1000.0;
        var imageTrace = TraceGeneric(
            new[] { 0.0 },
            new[] { 1.0 },
            positions[stopIndex],
            wavelength,
            stopIndex);
        var objectTrace = TraceGenericReverse(
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

    public ParaxialTrace TraceGeneric(
        IReadOnlyList<double> initialHeights,
        IReadOnlyList<double> initialSlopes,
        double initialZ,
        double wavelengthMicrometers,
        int startSurfaceIndex = 0)
    {
        return TraceGenericCore(
            initialHeights,
            initialSlopes,
            initialZ,
            wavelengthMicrometers,
            startSurfaceIndex,
            reverse: false);
    }

    public ParaxialTrace TraceGenericReverse(
        IReadOnlyList<double> initialHeights,
        IReadOnlyList<double> initialSlopes,
        double initialZ,
        double wavelengthMicrometers,
        int skipSurfaceCount = 0)
    {
        return TraceGenericCore(
            initialHeights,
            initialSlopes,
            initialZ,
            wavelengthMicrometers,
            skipSurfaceCount,
            reverse: true);
    }

    private ParaxialTrace TraceGenericCore(
        IReadOnlyList<double> initialHeights,
        IReadOnlyList<double> initialSlopes,
        double initialZ,
        double wavelengthMicrometers,
        int skipSurfaceCount,
        bool reverse)
    {
        if (initialHeights.Count != initialSlopes.Count)
        {
            throw new ArgumentException("Paraxial height and slope arrays must have equal length.");
        }

        var wavelengthNanometers = wavelengthMicrometers * 1000;
        var surfaces = _optic.SurfaceGroup.Items.ToArray();
        var positions = SurfacePositions().ToArray();
        var radii = surfaces
            .Select(surface => surface.IsPlane ? double.PositiveInfinity : surface.Radius)
            .ToArray();
        var indices = surfaces
            .Select(surface => surface.MaterialAfter.RefractiveIndex(wavelengthNanometers))
            .ToArray();
        if (reverse)
        {
            var imagePosition = positions[^1];
            surfaces = surfaces.Reverse().ToArray();
            positions = positions.Reverse().Select(position => imagePosition - position).ToArray();
            radii = radii.Reverse().Select(radius => -radius).ToArray();
            indices = indices
                .Select((_, index) => indices[(index + indices.Length - 1) % indices.Length])
                .Reverse()
                .ToArray();
        }

        var y = initialHeights.ToArray();
        var u = initialSlopes.ToArray();
        var z = initialZ;
        var heights = new List<IReadOnlyList<double>>(surfaces.Length);
        var slopes = new List<IReadOnlyList<double>>(surfaces.Length);

        for (var surfaceIndex = Math.Clamp(skipSurfaceCount, 0, surfaces.Length); surfaceIndex < surfaces.Length; surfaceIndex++)
        {
            var surface = surfaces[surfaceIndex];
            if (surface.Label.Equals("Object", StringComparison.OrdinalIgnoreCase))
            {
                heights.Add(y.ToArray());
                slopes.Add(u.ToArray());
                continue;
            }

            var distance = positions[surfaceIndex] - z;
            for (var rayIndex = 0; rayIndex < y.Length; rayIndex++)
            {
                y[rayIndex] += distance * u[rayIndex];
            }

            z = positions[surfaceIndex];
            var indexBefore = indices[(surfaceIndex + indices.Length - 1) % indices.Length];
            var indexAfter = indices[surfaceIndex];
            var power = double.IsInfinity(radii[surfaceIndex])
                ? 0
                : (indexAfter - indexBefore) / radii[surfaceIndex];
            var reflective = surface.IsReflective
                || surface.InteractionModel is RefractiveReflectiveInteractionModel { IsReflective: true }
                || surface.InteractionModel is ThinLensInteractionModel { IsReflective: true };
            for (var rayIndex = 0; rayIndex < u.Length; rayIndex++)
            {
                u[rayIndex] = surface.InteractionModel switch
                {
                    ThinLensInteractionModel thinLens when reflective =>
                        -u[rayIndex] - (y[rayIndex] / thinLens.FocalLength),
                    ThinLensInteractionModel thinLens =>
                        ((indexBefore * u[rayIndex]) - (y[rayIndex] / thinLens.FocalLength)) / indexAfter,
                    _ when reflective => -u[rayIndex] - (2 * y[rayIndex] / radii[surfaceIndex]),
                    _ => ((indexBefore * u[rayIndex]) - (y[rayIndex] * power)) / indexAfter
                };
            }

            heights.Add(y.ToArray());
            slopes.Add(u.ToArray());
        }

        return new ParaxialTrace(heights, slopes);
    }

    private IReadOnlyList<double> SurfacePositions()
    {
        return _optic.SurfaceGroup.Items
            .Select(surface => surface.CoordinateSystem.Origin.Z)
            .ToArray();
    }

    private RayMatrix TraceSystemMatrix(double wavelengthNanometers)
    {
        var matrix = RayMatrix.Identity;
        var currentIndex = 1.0;

        foreach (var surface in _optic.SurfaceGroup.Items)
        {
            var nextIndex = surface.MaterialAfter.RefractiveIndex(wavelengthNanometers);
            matrix = Refract(matrix, surface.Radius, currentIndex, nextIndex);
            matrix = Translate(matrix, surface.Thickness);
            currentIndex = nextIndex;
        }

        return matrix;
    }

    private double PrimaryWavelengthNanometers()
    {
        return (_optic.Wavelengths.FirstOrDefault(item => item.IsPrimary) ?? _optic.Wavelengths.FirstOrDefault())?.Nanometers
            ?? 587.6;
    }

    private static bool IsObjectAtInfinity(OpticalSurface? objectSurface)
    {
        return objectSurface is null
            || double.IsInfinity(objectSurface.CoordinateSystem.Origin.Z)
            || Math.Abs(objectSurface.Thickness) <= 1e-12;
    }

    private static RayMatrix Refract(RayMatrix matrix, double radius, double indexBefore, double indexAfter)
    {
        if (Math.Abs(radius) < 1e-12 || double.IsInfinity(radius))
        {
            return matrix with
            {
                C = (indexBefore / indexAfter) * matrix.C,
                D = (indexBefore / indexAfter) * matrix.D
            };
        }

        var c = -(indexAfter - indexBefore) / (indexAfter * radius);
        var d = indexBefore / indexAfter;
        return new RayMatrix(
            matrix.A,
            matrix.B,
            (c * matrix.A) + (d * matrix.C),
            (c * matrix.B) + (d * matrix.D));
    }

    private static RayMatrix Translate(RayMatrix matrix, double distance)
    {
        return new RayMatrix(
            matrix.A + (distance * matrix.C),
            matrix.B + (distance * matrix.D),
            matrix.C,
            matrix.D);
    }

    private sealed record RayMatrix(double A, double B, double C, double D)
    {
        public static RayMatrix Identity { get; } = new(1, 0, 0, 1);
    }
}

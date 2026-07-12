using OptilandWorkbench.Core.Apertures;

namespace OptilandWorkbench.Core.Services;

public sealed record ParaxialTrace(
    IReadOnlyList<IReadOnlyList<double>> Heights,
    IReadOnlyList<IReadOnlyList<double>> Slopes);

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
            _ => _optic.Aperture.Diameter(fallbackDiameter)
        };
    }

    public double EstimateEntrancePupilLocation()
    {
        var matrix = RayMatrix.Identity;
        var currentIndex = 1.0;
        var wavelengthNanometers = PrimaryWavelengthNanometers();

        foreach (var surface in _optic.SurfaceGroup.Items)
        {
            if (surface.IsStop)
            {
                break;
            }

            var nextIndex = _optic.Materials.Resolve(surface.MaterialAfterName).RefractiveIndex(wavelengthNanometers);
            matrix = Refract(matrix, surface.Radius, currentIndex, nextIndex);
            matrix = Translate(matrix, surface.Thickness);
            currentIndex = nextIndex;
        }

        return Math.Abs(matrix.A) < 1e-12 ? 0 : matrix.B / matrix.A;
    }

    public double EstimateExitPupilLocation()
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
            PrimaryWavelengthNanometers() / 1000.0,
            stopIndex + 1);
        var finalHeight = trace.Heights[^1][0];
        var finalSlope = trace.Slopes[^1][0];
        return Math.Abs(finalSlope) <= 1e-12 ? 0 : -finalHeight / finalSlope;
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
        var maxField = _optic.Fields.Select(field => Math.Abs(field.YAngleDegrees)).DefaultIfEmpty(0).Max();
        var slope = Math.Tan(normalizedFieldY * maxField * Math.PI / 180.0);
        var heights = normalizedPupilY
            .Select(pupil => (pupil * entrancePupilRadius) + (slope * (firstSurfacePosition - entrancePupilLocation)))
            .ToArray();
        var slopes = Enumerable.Repeat(slope, heights.Length).ToArray();
        return TraceGeneric(heights, slopes, firstSurfacePosition, wavelengthMicrometers);
    }

    public ParaxialTrace MarginalRay(double wavelengthMicrometers)
    {
        var positions = SurfacePositions();
        var firstSurfacePosition = positions.Count > 1 ? positions[1] : 0;
        return TraceGeneric(
            new[] { EstimateEntrancePupilDiameter() / 2 },
            new[] { 0.0 },
            firstSurfacePosition - 10,
            wavelengthMicrometers);
    }

    public ParaxialTrace ChiefRay(double wavelengthMicrometers)
    {
        var positions = SurfacePositions();
        var firstSurfacePosition = positions.Count > 1 ? positions[1] : 0;
        var fieldRadians = _optic.Fields.Select(field => Math.Abs(field.YAngleDegrees)).DefaultIfEmpty(0).Max() * Math.PI / 180.0;
        var slope = Math.Tan(fieldRadians);
        var height = slope * (firstSurfacePosition - EstimateEntrancePupilLocation());
        return TraceGeneric(new[] { height }, new[] { slope }, firstSurfacePosition, wavelengthMicrometers);
    }

    public ParaxialTrace TraceGeneric(
        IReadOnlyList<double> initialHeights,
        IReadOnlyList<double> initialSlopes,
        double initialZ,
        double wavelengthMicrometers,
        int startSurfaceIndex = 0)
    {
        if (initialHeights.Count != initialSlopes.Count)
        {
            throw new ArgumentException("Paraxial height and slope arrays must have equal length.");
        }

        var wavelengthNanometers = wavelengthMicrometers * 1000;
        var positions = SurfacePositions();
        var indices = _optic.SurfaceGroup.Items
            .Select(surface => _optic.Materials.Resolve(surface.MaterialAfterName).RefractiveIndex(wavelengthNanometers))
            .ToArray();
        var y = initialHeights.ToArray();
        var u = initialSlopes.ToArray();
        var z = initialZ;
        var heights = new List<IReadOnlyList<double>>(_optic.SurfaceGroup.Items.Count);
        var slopes = new List<IReadOnlyList<double>>(_optic.SurfaceGroup.Items.Count);

        for (var surfaceIndex = Math.Clamp(startSurfaceIndex, 0, _optic.SurfaceGroup.Items.Count); surfaceIndex < _optic.SurfaceGroup.Items.Count; surfaceIndex++)
        {
            var surface = _optic.SurfaceGroup.Items[surfaceIndex];
            if (surfaceIndex == 0 && surface.Label.Equals("Object", StringComparison.OrdinalIgnoreCase))
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
            var indexBefore = surfaceIndex == 0 ? indices[0] : indices[surfaceIndex - 1];
            var indexAfter = indices[surfaceIndex];
            var power = surface.IsPlane ? 0 : (indexAfter - indexBefore) / surface.Radius;
            for (var rayIndex = 0; rayIndex < u.Length; rayIndex++)
            {
                u[rayIndex] = ((indexBefore * u[rayIndex]) - (y[rayIndex] * power)) / indexAfter;
            }

            heights.Add(y.ToArray());
            slopes.Add(u.ToArray());
        }

        return new ParaxialTrace(heights, slopes);
    }

    private IReadOnlyList<double> SurfacePositions()
    {
        var position = 0.0;
        var positions = new double[_optic.SurfaceGroup.Items.Count];
        for (var index = 0; index < positions.Length; index++)
        {
            positions[index] = position;
            position += _optic.SurfaceGroup.Items[index].Thickness;
        }

        return positions;
    }

    private RayMatrix TraceSystemMatrix(double wavelengthNanometers)
    {
        var matrix = RayMatrix.Identity;
        var currentIndex = 1.0;

        foreach (var surface in _optic.SurfaceGroup.Items)
        {
            var nextIndex = _optic.Materials.Resolve(surface.MaterialAfterName).RefractiveIndex(wavelengthNanometers);
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

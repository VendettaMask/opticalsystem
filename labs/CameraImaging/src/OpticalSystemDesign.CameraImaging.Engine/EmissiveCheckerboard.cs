using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Coordinates;
using OpticalSystemDesign.Core.NonSequential;

namespace OpticalSystemDesign.Core.Analysis.Imaging;

/// <summary>World units mm, pose relative to the first physical lens vertex. Local +Z emits toward the camera.</summary>
public sealed record EmissiveCheckerboardSettings(int Columns = 14, int Rows = 14, double CellSizeMillimeters = 20,
    double DistanceMillimeters = 10000, double CenterXMillimeters = 0, double CenterYMillimeters = 0,
    double RotationXDegrees = 0, double RotationYDegrees = 0, double RotationZDegrees = 5);

/// <summary>Band-integrated radiance W/(m² sr), not spectral density or normalized RGB.</summary>
public sealed record EmissiveCheckerboardBand(double WavelengthNanometers, double BrightRadiance, double DarkRadiance = 0,
    double BrightReflectance = 0, double DarkReflectance = 0);

/// <summary>A finite physical surface with an exact local-coordinate emission partition.
/// One coplanar rectangle represents all cells; intersections and transport remain in shared Core.</summary>
public static class EmissiveCheckerboard
{
    public const string Algorithm = "PhysicalEmissiveCheckerboard/1";

    public static DirectCameraScene Create(EmissiveCheckerboardSettings settings,
        IReadOnlyList<EmissiveCheckerboardBand> bands, double firstSurfaceZMillimeters = 0)
    {
        Validate(settings, bands);
        if (!double.IsFinite(firstSurfaceZMillimeters)) throw new ArgumentException("Finite lens vertex required.");
        var width = settings.Columns * settings.CellSizeMillimeters;
        var height = settings.Rows * settings.CellSizeMillimeters;
        var surface = NonSequentialObjectDefinition.Create(NonSequentialObjectKind.PlaneRectangle) with
        {
            LocalCoordinateSystem = new CoordinateSystem(new Vector3D(settings.CenterXMillimeters,
                settings.CenterYMillimeters, firstSurfaceZMillimeters - settings.DistanceMillimeters),
                settings.RotationXDegrees, settings.RotationYDegrees, settings.RotationZDegrees),
            Parameters = new PlaneRectangleParameters(width, height)
        };
        var geometry = new NonSequentialDocument("Physical emissive checkerboard",
            bands.Select((b, i) => new NonSequentialWavelength($"Band {i + 1}", b.WavelengthNanometers, 1, i == 0)).ToArray(), [surface]);
        return new DirectCameraScene(geometry, [new DirectSceneMaterial(surface.Id,
            bands.Select(b => b.BrightReflectance).ToArray(), bands.Select(b => b.BrightRadiance).ToArray(),
            bands.Select(b => b.DarkReflectance).ToArray(), settings.CellSizeMillimeters,
            DarkEmittedRadiance: bands.Select(b => b.DarkRadiance).ToArray(),
            CheckerOriginXMillimeters: -width / 2, CheckerOriginYMillimeters: -height / 2)], []);
    }

    public static void Validate(EmissiveCheckerboardSettings settings, IReadOnlyList<EmissiveCheckerboardBand> bands)
    {
        ArgumentNullException.ThrowIfNull(settings); ArgumentNullException.ThrowIfNull(bands);
        if (settings.Columns is < 1 or > 4096 || settings.Rows is < 1 or > 4096
            || !double.IsFinite(settings.CellSizeMillimeters) || settings.CellSizeMillimeters is <= 0 or > 1e5
            || settings.Columns * settings.CellSizeMillimeters > 1e5 || settings.Rows * settings.CellSizeMillimeters > 1e5
            || !double.IsFinite(settings.DistanceMillimeters) || settings.DistanceMillimeters is <= 1 or > 1e6
            || new[] { settings.CenterXMillimeters, settings.CenterYMillimeters, settings.RotationXDegrees,
                settings.RotationYDegrees, settings.RotationZDegrees }.Any(v => !double.IsFinite(v) || Math.Abs(v) > 1e6))
            throw new ArgumentException("Invalid physical checkerboard dimensions or pose.");
        // All chart corners must remain on the object side; avoid a bounding-sphere rejection for flat, large targets.
        var pose = new CoordinateSystem(new(0, 0, -settings.DistanceMillimeters), settings.RotationXDegrees,
            settings.RotationYDegrees, settings.RotationZDegrees);
        foreach (var x in new[] { -.5, .5 }) foreach (var y in new[] { -.5, .5 })
            if (pose.ToGlobalPoint(new(x * settings.Columns * settings.CellSizeMillimeters,
                y * settings.Rows * settings.CellSizeMillimeters, 0)).Z >= -1)
                throw new ArgumentException("Every chart corner must be at least 1 mm before the first physical lens vertex.");
        if (bands.Count is < 1 or > 16 || bands.Any(b => b is null || !double.IsFinite(b.WavelengthNanometers)
            || b.WavelengthNanometers is < 200 or > 20000
            || new[] { b.BrightRadiance, b.DarkRadiance }.Any(v => !double.IsFinite(v) || v is < 0 or > 1e6)
            || new[] { b.BrightReflectance, b.DarkReflectance }.Any(v => !double.IsFinite(v) || v is < 0 or > 1))
            || bands.Select(b => b.WavelengthNanometers).Distinct().Count() != bands.Count)
            throw new ArgumentException("Declare 1–16 distinct wavelengths and finite physical band radiances/reflectances.");
    }
}

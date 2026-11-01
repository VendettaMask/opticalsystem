using OpticalSystemDesign.Core.Backend;

namespace OpticalSystemDesign.Core.Geometries;

/// <summary>The US_MEMS sample contract: a plane intersection with three-state pixel normals.</summary>
public sealed class TernaryMirrorArrayGeometry : IGeometry
{
    private static readonly PlaneGeometry Plane = new();
    public TernaryMirrorArrayGeometry(int pixelsX, int pixelsY, double widthX, double widthY,
        IReadOnlyList<double> tiltRadians, double azimuthRadians, int addressingMode, IReadOnlyList<int> packedStates)
    {
        if (pixelsX < 0 || pixelsY < 0 || (long)pixelsX * pixelsY > int.MaxValue
            || !double.IsFinite(widthX) || widthX <= 0 || !double.IsFinite(widthY) || widthY <= 0
            || tiltRadians.Count != 3 || tiltRadians.Any(v => !double.IsFinite(v)) || !double.IsFinite(azimuthRadians)
            || addressingMode is < 0 or > 2 || packedStates.Count > 199 || packedStates.Any(v => v < 0))
            throw new ArgumentException("Invalid ternary mirror-array definition.");
        PixelsX = pixelsX; PixelsY = pixelsY; WidthX = widthX; WidthY = widthY;
        TiltRadians = Array.AsReadOnly(tiltRadians.ToArray()); AzimuthRadians = azimuthRadians; AddressingMode = addressingMode;
        PackedStates = Array.AsReadOnly(packedStates.ToArray());
    }
    public string Kind => "ternary_mirror_array_v1";
    public int PixelsX { get; }
    public int PixelsY { get; }
    public double WidthX { get; }
    public double WidthY { get; }
    public IReadOnlyList<double> TiltRadians { get; }
    public double AzimuthRadians { get; }
    public int AddressingMode { get; }
    public IReadOnlyList<int> PackedStates { get; }
    public double Sag(double x, double y) => 0;
    public Vector3D SurfaceNormal(Vector3D point)
    {
        if (PixelsX == 0 || PixelsY == 0 || point.X < -.5 * WidthX || point.X >= .5 * WidthX
            || point.Y < -.5 * WidthY || point.Y >= .5 * WidthY) return new(0, 0, 1);
        var x = (int)Math.Floor((point.X + .5 * WidthX) / (WidthX / PixelsX));
        var y = (int)Math.Floor((point.Y + .5 * WidthY) / (WidthY / PixelsY));
        var address = AddressingMode switch { 0 => y, 1 => x, _ => x + y * PixelsX };
        var group = address / 15;
        if (group >= 199) return new(0, 0, 1);
        var word = group < PackedStates.Count ? PackedStates[group] : 0;
        for (var digit = 0; digit < address % 15; digit++) word /= 3;
        var tilt = TiltRadians[word % 3];
        return new(-Math.Sin(tilt) * Math.Sin(AzimuthRadians), -Math.Sin(tilt) * Math.Cos(AzimuthRadians), Math.Cos(tilt));
    }
    public IntersectionResult DistanceToIntersection(Vector3D origin, Vector3D direction)
    {
        var hit = Plane.DistanceToIntersection(origin, direction);
        return hit.IsHit ? hit with { Normal = SurfaceNormal(hit.Point) } : hit;
    }
    public IGeometry Clone() => new TernaryMirrorArrayGeometry(PixelsX, PixelsY, WidthX, WidthY, TiltRadians,
        AzimuthRadians, AddressingMode, PackedStates);
}

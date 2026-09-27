using OptilandWorkbench.Core.Capabilities;

namespace OptilandWorkbench.Core.Analysis;

/// <summary>Signed axial separation of adjacent, coaxial surfaces at a common local pupil coordinate.</summary>
public static class AxialSurfaceSeparation
{
    public static double Evaluate(Optic optic, int frontSurfaceIndex, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(optic);
        if (frontSurfaceIndex < 1 || frontSurfaceIndex + 1 >= optic.SurfaceGroup.Items.Count)
            throw new ArgumentOutOfRangeException(nameof(frontSurfaceIndex));
        if (!double.IsFinite(x) || !double.IsFinite(y))
            throw new ArgumentOutOfRangeException(nameof(x));
        var front = optic.SurfaceGroup.Items[frontSurfaceIndex];
        var back = optic.SurfaceGroup.Items[frontSurfaceIndex + 1];
        OpticCapabilityPreflight.EnsureSurfaceSupported(front, OpticCapabilityOperation.Analysis);
        OpticCapabilityPreflight.EnsureSurfaceSupported(back, OpticCapabilityOperation.Analysis);
        var axis = front.CoordinateSystem.ToGlobalDirection(new(0, 0, 1));
        var otherAxis = back.CoordinateSystem.ToGlobalDirection(new(0, 0, 1));
        var offset = front.CoordinateSystem.ToLocalPoint(back.CoordinateSystem.Origin);
        if ((axis - otherAxis).Length > 1e-12 || Math.Abs(offset.X) > 1e-12 || Math.Abs(offset.Y) > 1e-12)
            throw new NotSupportedException("Axial separation requires adjacent coaxial surface frames.");
        // Preserve NaN for an undefined sag; callers must report an unavailable measurement.
        var backCoordinate = back.CoordinateSystem.ToLocalPoint(front.CoordinateSystem.ToGlobalPoint(new(x, y, 0)));
        return offset.Z + back.Geometry.Sag(backCoordinate.X, backCoordinate.Y) - front.Geometry.Sag(x, y);
    }
}

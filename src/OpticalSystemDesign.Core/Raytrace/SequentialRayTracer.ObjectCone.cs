using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;

namespace OpticalSystemDesign.Core.Raytrace;

internal readonly record struct FloatingConicEdge(double Depth, double DirectionSign)
{
    internal bool Contains(Vector3D point)
    {
        // An intercept on the rim is vignetted. A small axial roundoff bound
        // keeps the tangent construction and the ray intersection consistent.
        var tolerance = 32 * 2.2204460492503131e-16 * Math.Max(1, Depth);
        return DirectionSign * point.Z < Depth - tolerance;
    }
}

public sealed partial class SequentialRayTracer
{
    internal FloatingConicEdge? ResolveFloatingConicEdge(OpticalSurface surface, int surfaceIndex)
    {
        if (_optic.Aperture.Kind != ApertureKind.ObjectConeAngle || !surface.SemiDiameterDefinesPhysicalAperture
            || surface.PhysicalAperture is not (CircularAperture or OffsetRadialAperture)
            || surface.Geometry is not StandardGeometry { Conic: > -1 } conic
            || !double.IsFinite(conic.Radius) || Math.Abs(conic.Radius) < 1e-12)
            return null;

        var frame = surface.CoordinateSystem;
        var source = _optic.SurfaceGroup.Items[0];
        var axialOrigin = frame.ToLocalPoint(source.CoordinateSystem.Origin);
        if (surfaceIndex != 1 || !surface.IsStop || _optic.FieldDefinition != FieldDefinitionKind.Angle
            || ObjectConjugate.IsInfinite(source) || axialOrigin.Z >= 0 || Math.Abs(axialOrigin.X) > 1e-12
            || Math.Abs(axialOrigin.Y) > 1e-12 || frame.RotationXDegrees != 0 || frame.RotationYDegrees != 0
            || source.CoordinateSystem.RotationXDegrees != 0 || source.CoordinateSystem.RotationYDegrees != 0
            || _optic.Fields.Any(field => PupilVignetting.FromField(field) != PupilVignetting.Identity))
        {
            if (_optic.Aperture.Value <= 90) return null;
            throw new NotSupportedException("A wide-cone floating conic edge requires a centered first-surface stop and unvignetted angle fields.");
        }

        var vertexRadius = Math.Abs(conic.Radius);
        var sign = Math.CopySign(1, conic.Radius);
        var equatorDepth = vertexRadius / (1 + conic.Conic);
        var wavelength = _optic.Wavelengths.FirstOrDefault(w => w.IsPrimary)
            ?? _optic.Wavelengths.FirstOrDefault();
        var micrometers = wavelength?.Micrometers ?? .55;
        var farEdge = BeyondEquator(0, 0, 1, 0);
        foreach (var field in _optic.Fields)
        {
            var x = Math.Tan(field.X * Math.PI / 180);
            var y = Math.Tan(field.Y * Math.PI / 180);
            // Project the global Z axis onto the native pupil basis. The
            // components are proportional to (axis.X * axis.Z, axis.Y).
            x /= Math.Sqrt(1 + x * x + y * y);
            var length = Math.Sqrt(x * x + y * y);
            if (length <= 1e-15) continue;
            // The two outer meridional rays bound axial coverage for this
            // centered rotational conic. Every declared field shares the rim.
            farEdge |= BeyondEquator(field.X, field.Y, x / length, y / length)
                | BeyondEquator(field.X, field.Y, -x / length, -y / length);
        }

        var radius = surface.PhysicalAperture is CircularAperture circle ? circle.Radius
            : ((OffsetRadialAperture)surface.PhysicalAperture).OuterRadius;
        var ratio = Math.Min(1, radius * Math.Sqrt(1 + conic.Conic) / vertexRadius);
        var root = Math.Sqrt(1 - ratio * ratio);
        // The floating radius is the radial coordinate of a rim on the
        // selected branch. Radii beyond the equator reach the equator itself.
        var depth = farEdge ? equatorDepth * (1 + root) : equatorDepth * ratio * ratio / (1 + root);
        return new(depth, sign);

        bool BeyondEquator(double fieldX, double fieldY, double pupilX, double pupilY)
        {
            var ray = RayGenerator.GenerateAtPhysicalField(fieldX, fieldY, pupilX, pupilY, micrometers).Rays[0];
            var origin = frame.ToLocalPoint(ray.Origin);
            if (!conic.IsInsideClosedConic(origin))
            {
                if (_optic.Aperture.Value <= 90) return false;
                throw new NotSupportedException("Wide-cone floating conic edges require all declared field origins inside the conic.");
            }
            var hit = conic.IntersectFromObjectCone(origin, frame.ToLocalDirection(ray.Direction));
            return hit.IsHit && sign * hit.Point.Z > equatorDepth;
        }
    }
}

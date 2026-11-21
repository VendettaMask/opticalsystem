using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Domain;

namespace OpticalSystemDesign.Core.Visualization;

public sealed partial class Layout2DBuilder
{
    private static int DrawingSamples(OpticalSurface surface, int samples, bool odd = true)
    {
        var multiplier = surface.Drawing.Precision switch
        {
            SurfaceDrawingPrecision.Medium => 1.5,
            SurfaceDrawingPrecision.High => 2,
            SurfaceDrawingPrecision.Presentation => 3,
            _ => 1
        };
        var value = Math.Clamp((int)Math.Ceiling(samples * multiplier), 3, 513);
        return odd ? NormalizeSampleCount(value) : value;
    }

    private static LayoutLocalFrame? BuildLocalFrame(OpticalSurface surface)
    {
        var drawing = surface.Drawing;
        if (!drawing.DrawLocalAxes && !drawing.DrawLocalOrigin) return null;
        var length = Math.Max(0.1, SurfaceExtent(surface) * 0.3);
        var coordinates = surface.CoordinateSystem;
        return new LayoutLocalFrame(ToLayoutPoint(coordinates.Origin),
            ToLayoutPoint(coordinates.ToGlobalPoint(new Vector3D(length, 0, 0))),
            ToLayoutPoint(coordinates.ToGlobalPoint(new Vector3D(0, length, 0))),
            ToLayoutPoint(coordinates.ToGlobalPoint(new Vector3D(0, 0, length))),
            drawing.DrawLocalAxes, drawing.DrawLocalOrigin);
    }

    private IReadOnlyList<Layout3DRaySegment> ApplyRayDrawing(IReadOnlyList<Layout3DRaySegment> physicalSegments)
    {
        if (!_optic.SurfaceGroup.Items.Any(surface => surface.Drawing.HideRays || surface.Drawing.SkipRayDrawing))
            return physicalSegments;
        var settings = _optic.SurfaceGroup.Items.ToDictionary(surface => surface.Number, surface => surface.Drawing);
        var displayed = new List<Layout3DRaySegment>();
        Layout3DRaySegment? pending = null;
        foreach (var segment in physicalSegments)
        {
            var joined = pending is null ? segment : pending with { End = segment.End, TargetSurfaceNumber = segment.TargetSurfaceNumber };
            var target = segment.TargetSurfaceNumber is int number && settings.TryGetValue(number, out var value)
                ? value : SurfaceDrawingOptions.Default;
            if (target.SkipRayDrawing) { pending = joined; continue; }
            if (!target.HideRays) displayed.Add(joined);
            pending = null;
        }
        return displayed;
    }
}

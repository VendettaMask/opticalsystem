using System.Collections.Concurrent;
using OptilandWorkbench.Application.Contracts;
using SkiaSharp;

namespace OptilandWorkbench.App.Manufacturing;

internal static partial class OpticalDrawingRendererCore
{
    internal const int ManufacturingSurfaceSamples = 65;

    internal sealed record ManufacturingProfilePoint(double Z, double Y);

    internal sealed record ManufacturingComponentProfile(
        OpticalElementDefinition Component,
        IReadOnlyList<ManufacturingProfilePoint> Front,
        IReadOnlyList<ManufacturingProfilePoint> Back,
        IReadOnlyList<ManufacturingProfilePoint> Boundary,
        double FrontVertexZ,
        double BackVertexZ,
        double ProfileSemiDiameter);

    internal static ManufacturingComponentProfile BuildManufacturingComponentProfile(
        OpticalElementDefinition component,
        double frontVertexZ,
        int surfaceSamples = ManufacturingSurfaceSamples)
    {
        ArgumentNullException.ThrowIfNull(component);

        surfaceSamples = NormalizeManufacturingSampleCount(surfaceSamples);
        var backVertexZ = frontVertexZ + component.CenterThickness;
        var profileSemiDiameter = ElementProfileExtent(
            component.FrontSurface,
            component.BackSurface,
            frontVertexZ,
            backVertexZ);
        var front = BuildExtendedManufacturingSurfaceCurve(
            component.FrontSurface,
            frontVertexZ,
            surfaceSamples,
            profileSemiDiameter);
        var back = BuildExtendedManufacturingSurfaceCurve(
            component.BackSurface,
            backVertexZ,
            surfaceSamples,
            profileSemiDiameter);
        var boundary = new List<ManufacturingProfilePoint>(front.Count + back.Count);
        boundary.AddRange(front);
        for (var pointIndex = back.Count - 1; pointIndex >= 0; pointIndex--)
        {
            boundary.Add(back[pointIndex]);
        }

        return new ManufacturingComponentProfile(
            component,
            front,
            back,
            boundary,
            frontVertexZ,
            backVertexZ,
            profileSemiDiameter);
    }

    private static IReadOnlyList<ManufacturingProfilePoint> BuildExtendedManufacturingSurfaceCurve(
        SurfaceRowDto surface,
        double vertexZ,
        int surfaceSamples,
        double targetExtent)
    {
        var surfaceExtent = ManufacturingSurfaceExtent(surface);
        var points = BuildManufacturingSurfaceCurvePoints(
            surface,
            vertexZ,
            surfaceSamples,
            surfaceExtent);
        if (targetExtent <= surfaceExtent + 1e-9)
        {
            return points;
        }

        var extended = new List<ManufacturingProfilePoint>(points.Count + 2)
        {
            new(points[0].Z, -targetExtent)
        };
        extended.AddRange(points);
        extended.Add(new ManufacturingProfilePoint(points[^1].Z, targetExtent));
        return extended;
    }

    private static IReadOnlyList<ManufacturingProfilePoint> BuildManufacturingSurfaceCurvePoints(
        SurfaceRowDto surface,
        double vertexZ,
        int surfaceSamples,
        double extent)
    {
        var points = new List<ManufacturingProfilePoint>(surfaceSamples);
        for (var sample = 0; sample < surfaceSamples; sample++)
        {
            var fraction = sample / (double)(surfaceSamples - 1);
            var height = -extent + (2 * extent * fraction);
            points.Add(new ManufacturingProfilePoint(
                vertexZ + ManufacturingSagOrZero(surface, height),
                height));
        }

        return points;
    }

    private static double ElementProfileExtent(
        SurfaceRowDto front,
        SurfaceRowDto back,
        double frontVertexZ,
        double backVertexZ)
    {
        var target = Math.Max(
            ManufacturingSurfaceExtent(front),
            ManufacturingSurfaceExtent(back));
        var previous = 0.0;
        const int searchSteps = 256;
        const double minimumGap = 1e-6;

        for (var index = 1; index <= searchSteps; index++)
        {
            var current = target * index / searchSteps;
            if (ManufacturingElementGap(front, back, frontVertexZ, backVertexZ, current) > minimumGap)
            {
                previous = current;
                continue;
            }

            var low = previous;
            var high = current;
            for (var iteration = 0; iteration < 48; iteration++)
            {
                var middle = (low + high) / 2.0;
                if (ManufacturingElementGap(front, back, frontVertexZ, backVertexZ, middle) > minimumGap)
                {
                    low = middle;
                }
                else
                {
                    high = middle;
                }
            }

            return Math.Max(0.1, low * 0.995);
        }

        return target;
    }

    private static double ManufacturingElementGap(
        SurfaceRowDto front,
        SurfaceRowDto back,
        double frontVertexZ,
        double backVertexZ,
        double y) =>
        Math.Min(
            ManufacturingSurfaceZ(back, backVertexZ, y) - ManufacturingSurfaceZ(front, frontVertexZ, y),
            ManufacturingSurfaceZ(back, backVertexZ, -y) - ManufacturingSurfaceZ(front, frontVertexZ, -y));

    private static double ManufacturingSurfaceZ(
        SurfaceRowDto surface,
        double vertexZ,
        double y)
    {
        var extent = ManufacturingSurfaceExtent(surface);
        var sampledY = Math.Clamp(y, -extent, extent);
        return vertexZ + ManufacturingSagOrZero(surface, sampledY);
    }

    private static double ManufacturingSurfaceExtent(SurfaceRowDto surface)
    {
        var extent = Math.Max(0.1, surface.SemiDiameter);
        if (Math.Abs(surface.Radius) < 1e-12
            || !double.IsFinite(surface.Radius)
            || !double.IsFinite(surface.Conic)
            || 1.0 + surface.Conic <= 0)
        {
            return extent;
        }

        var realDomain = Math.Abs(surface.Radius) / Math.Sqrt(1.0 + surface.Conic);
        return Math.Min(extent, realDomain * 0.98);
    }

    private static double ManufacturingSagOrZero(SurfaceRowDto surface, double height)
    {
        var sag = OpticalManufacturingModel.Sag(
            surface.Radius,
            surface.Conic,
            Math.Abs(height));
        return sag is { } value && double.IsFinite(value)
            ? value
            : 0;
    }

    private static int NormalizeManufacturingSampleCount(int surfaceSamples)
    {
        surfaceSamples = Math.Max(3, surfaceSamples);
        return surfaceSamples % 2 == 0 ? surfaceSamples + 1 : surfaceSamples;
    }

    private static void DrawVerticalDimension(
        SKCanvas canvas,
        float x,
        float top,
        float bottom,
        float extensionX,
        string text,
        SKPaint paint,
        double? upperDeviation = null,
        double? lowerDeviation = null)
    {
        DrawExtensionLine(canvas, new SKPoint(extensionX, top), new SKPoint(x, top), paint);
        DrawExtensionLine(canvas, new SKPoint(extensionX, bottom), new SKPoint(x, bottom), paint);
        canvas.DrawLine(x, top, x, bottom, paint);
        Arrow(canvas, new SKPoint(x, top), new SKPoint(x, top + 9), paint);
        Arrow(canvas, new SKPoint(x, bottom), new SKPoint(x, bottom - 9), paint);
        canvas.Save();
        canvas.RotateDegrees(-90, x - 8, (top + bottom) / 2);
        DrawDimensionText(
            canvas,
            text,
            x - 8,
            (top + bottom) / 2,
            upperDeviation,
            lowerDeviation);
        canvas.Restore();
    }

    private static void DrawHorizontalDimension(
        SKCanvas canvas,
        float left,
        float right,
        float y,
        float extensionY,
        string text,
        SKPaint paint,
        double? upperDeviation = null,
        double? lowerDeviation = null)
    {
        DrawExtensionLine(canvas, new SKPoint(left, extensionY), new SKPoint(left, y), paint);
        DrawExtensionLine(canvas, new SKPoint(right, extensionY), new SKPoint(right, y), paint);
        canvas.DrawLine(left, y, right, y, paint);
        Arrow(canvas, new SKPoint(left, y), new SKPoint(left + 9, y), paint);
        Arrow(canvas, new SKPoint(right, y), new SKPoint(right - 9, y), paint);
        DrawDimensionText(
            canvas,
            text,
            (left + right) / 2,
            y - 7,
            upperDeviation,
            lowerDeviation);
    }

    private static void DrawDimensionText(
        SKCanvas canvas,
        string nominal,
        float centerX,
        float baselineY,
        double? upperDeviation,
        double? lowerDeviation)
    {
        const float nominalSize = 7;
        if (upperDeviation is null || lowerDeviation is null)
        {
            DrawText(canvas, nominal, centerX, baselineY, nominalSize, SKTextAlign.Center);
            return;
        }

        const float deviationSize = 5.4f;
        const float gap = 2;
        var upper = FormatDeviation(upperDeviation.Value);
        var lower = FormatDeviation(lowerDeviation.Value);
        var nominalWidth = MeasureText(nominal, nominalSize, false);
        var deviationWidth = Math.Max(
            MeasureText(upper, deviationSize, false),
            MeasureText(lower, deviationSize, false));
        var left = centerX - ((nominalWidth + gap + deviationWidth) / 2);
        var deviationX = left + nominalWidth + gap;

        DrawText(canvas, nominal, left, baselineY + 1.5f, nominalSize, SKTextAlign.Left);
        DrawText(canvas, upper, deviationX, baselineY - 2.5f, deviationSize, SKTextAlign.Left);
        DrawText(canvas, lower, deviationX, baselineY + 4.2f, deviationSize, SKTextAlign.Left);
    }

    private static string FormatDeviation(double value) => value switch
    {
        > 0 => $"+{value:0.###}",
        < 0 => $"{value:0.###}",
        _ => "0"
    };

    private static void DrawSurfaceRadiusDimension(
        SKCanvas canvas,
        IReadOnlyList<SKPoint> surfacePoints,
        float vertexX,
        float axisY,
        double radius,
        double tolerance,
        float xScale,
        SKRect area,
        SKPaint paint,
        bool upperSurface)
    {
        var surfacePoint = surfacePoints[upperSurface ? 8 : 56];
        var isPlane = Math.Abs(radius) < 1e-12 || !double.IsFinite(radius);
        var directionX = 1f;
        var directionY = upperSurface ? 0.28f : 0.18f;
        var center = new SKPoint(float.NaN, float.NaN);
        if (!isPlane)
        {
            center = new SKPoint(vertexX + ((float)radius * xScale), axisY);
            directionX = center.X - surfacePoint.X;
            directionY = center.Y - surfacePoint.Y;
            if (directionX < 0)
            {
                directionX = -directionX;
                directionY = -directionY;
            }
        }

        var directionLength = MathF.Sqrt(
            (directionX * directionX) + (directionY * directionY));
        if (directionLength < 1e-3f)
        {
            directionX = 1;
            directionY = 0;
            directionLength = 1;
        }

        directionX /= directionLength;
        directionY /= directionLength;
        var lineLength = 96f;
        lineLength = Math.Min(
            lineLength,
            AvailableLength(surfacePoint.X, directionX, area.Left + 12, area.Right - 12));
        lineLength = Math.Min(
            lineLength,
            AvailableLength(surfacePoint.Y, directionY, area.Top + 12, area.Bottom - 12));
        lineLength = Math.Max(42, lineLength);

        var centerDistance = isPlane
            ? float.PositiveInfinity
            : Distance(surfacePoint, center);
        var reachesCenter = !isPlane
            && centerDistance >= 42
            && centerDistance <= lineLength
            && area.Contains(center.X, center.Y);
        if (reachesCenter)
        {
            lineLength = centerDistance;
        }

        var lineEnd = reachesCenter
            ? center
            : new SKPoint(
                surfacePoint.X + (directionX * lineLength),
                surfacePoint.Y + (directionY * lineLength));
        canvas.DrawLine(surfacePoint, lineEnd, paint);
        Arrow(canvas, surfacePoint, lineEnd, paint);
        if (reachesCenter)
        {
            DrawCenterMark(canvas, center, paint);
        }

        var labelX = surfacePoint.X + (directionX * lineLength * 0.62f);
        var labelY = surfacePoint.Y + (directionY * lineLength * 0.62f);
        var angle = MathF.Atan2(directionY, directionX) * 180f / MathF.PI;
        canvas.Save();
        canvas.RotateDegrees(angle, labelX, labelY);
        DrawText(
            canvas,
            RadiusDimensionText(radius, tolerance),
            labelX,
            labelY - 4.5f,
            7.2f,
            SKTextAlign.Center);
        canvas.Restore();
    }

    internal static string RadiusDimensionText(double radius, double tolerance) =>
        Math.Abs(radius) < 1e-12 || !double.IsFinite(radius)
            ? "R∞"
            : $"R{Math.Abs(radius):0.###} ±{tolerance:0.###}";

    private static float AvailableLength(float value, float direction, float minimum, float maximum)
    {
        if (Math.Abs(direction) < 1e-4f)
        {
            return float.PositiveInfinity;
        }

        return direction > 0
            ? Math.Max(0, (maximum - value) / direction)
            : Math.Max(0, (minimum - value) / direction);
    }

    private static float Distance(SKPoint first, SKPoint second)
    {
        var dx = second.X - first.X;
        var dy = second.Y - first.Y;
        return MathF.Sqrt((dx * dx) + (dy * dy));
    }

    private static void DrawCenterMark(SKCanvas canvas, SKPoint center, SKPaint paint)
    {
        const float arm = 4;
        canvas.DrawLine(center.X - arm, center.Y, center.X + arm, center.Y, paint);
        canvas.DrawLine(center.X, center.Y - arm, center.X, center.Y + arm, paint);
    }

    private static void DrawExtensionLine(
        SKCanvas canvas,
        SKPoint objectPoint,
        SKPoint dimensionPoint,
        SKPaint paint)
    {
        var dx = dimensionPoint.X - objectPoint.X;
        var dy = dimensionPoint.Y - objectPoint.Y;
        var length = MathF.Sqrt((dx * dx) + (dy * dy));
        if (length < 1e-3f)
        {
            return;
        }

        dx /= length;
        dy /= length;
        const float objectGap = 1.5f;
        const float overshoot = 2.5f;
        canvas.DrawLine(
            objectPoint.X + (dx * objectGap),
            objectPoint.Y + (dy * objectGap),
            dimensionPoint.X + (dx * overshoot),
            dimensionPoint.Y + (dy * overshoot),
            paint);
    }

    private static void Arrow(SKCanvas canvas, SKPoint tip, SKPoint toward, SKPaint paint)
    {
        var dx = toward.X - tip.X;
        var dy = toward.Y - tip.Y;
        var length = MathF.Sqrt((dx * dx) + (dy * dy));
        if (length < 1e-3f)
        {
            return;
        }

        dx /= length;
        dy /= length;
        var normalX = -dy;
        var normalY = dx;
        const float arrowLength = 5.8f;
        const float halfWidth = 2.1f;
        using var arrow = new SKPath();
        arrow.MoveTo(tip);
        arrow.LineTo(
            tip.X + (dx * arrowLength) + (normalX * halfWidth),
            tip.Y + (dy * arrowLength) + (normalY * halfWidth));
        arrow.LineTo(
            tip.X + (dx * arrowLength) - (normalX * halfWidth),
            tip.Y + (dy * arrowLength) - (normalY * halfWidth));
        arrow.Close();
        using var fill = new SKPaint
        {
            Color = paint.Color,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        canvas.DrawPath(arrow, fill);
    }
}

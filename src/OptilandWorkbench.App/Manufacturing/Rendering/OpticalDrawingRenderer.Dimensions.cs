using System.Collections.Concurrent;
using OptilandWorkbench.Application.Contracts;
using SkiaSharp;

namespace OptilandWorkbench.App.Manufacturing;

internal static partial class OpticalDrawingRendererCore
{
private static IReadOnlyList<SKPoint> SurfacePoints(
        double radius,
        double conic,
        double semiDiameter,
        float centerX,
        float centerY,
        float xScale,
        float yScale)
    {
        var points = new List<SKPoint>(65);
        for (var sample = 0; sample <= 64; sample++)
        {
            var height = -semiDiameter + ((2 * semiDiameter * sample) / 64);
            var sag = OpticalManufacturingModel.Sag(radius, conic, Math.Abs(height)) ?? 0;
            points.Add(new SKPoint(
                centerX + ((float)sag * xScale),
                centerY + ((float)height * yScale)));
        }

        return points;
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

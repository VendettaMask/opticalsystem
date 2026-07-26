using System.Collections.Concurrent;
using OptilandWorkbench.Application.Contracts;
using SkiaSharp;

namespace OptilandWorkbench.App.Manufacturing;

internal static partial class OpticalDrawingRendererCore
{
    internal static void RenderSystem(
        SKCanvas canvas,
        OpticalSystemDrawingSheet sheet,
        float pageWidth,
        float pageHeight)
    {
        canvas.Clear(SKColors.White);
        canvas.Save();
        canvas.Scale(pageWidth / A4Width, pageHeight / A4Height);
        using var thin = Stroke(SKColors.Black, 0.65f);
        using var medium = Stroke(SKColors.Black, 1.05f);
        using var heavy = Stroke(SKColors.Black, 1.7f);
        using var dimension = Stroke(new SKColor(43, 43, 46), 0.7f);

        const float outer = 12;
        const float inner = 18;
        const float titleTop = 754;
        var right = A4Width - inner;
        var bottom = A4Height - inner;
        canvas.DrawRect(outer, outer, A4Width - (outer * 2), A4Height - (outer * 2), heavy);
        canvas.DrawRect(inner, inner, A4Width - (inner * 2), A4Height - (inner * 2), thin);

        var scaleDesignation = DrawSystemScene(
            canvas,
            sheet.Scene,
            new SKRect(inner + 14, inner + 14, right - 14, titleTop - 12),
            medium,
            dimension);
        DrawSystemTitleBlock(
            canvas,
            sheet,
            inner,
            titleTop,
            right - inner,
            bottom - titleTop,
            thin,
            medium,
            scaleDesignation);
        canvas.Restore();
    }

    private static string DrawSystemScene(
        SKCanvas canvas,
        Scene2Dto scene,
        SKRect area,
        SKPaint outline,
        SKPaint dimension)
    {
        var lenses = BuildSystemLensGeometry(scene);
        var lensPoints = lenses
            .SelectMany(lens => lens.Boundary)
            .ToArray();
        if (lensPoints.Length == 0)
        {
            return "1:1";
        }

        var zMin = lensPoints.Min(point => point.Z);
        var zMax = lensPoints.Max(point => point.Z);
        var yMin = lensPoints.Min(point => point.Y);
        var yMax = lensPoints.Max(point => point.Y);
        var zSpan = Math.Max(1e-6, zMax - zMin);
        var ySpan = Math.Max(1e-6, yMax - yMin);
        const float dimensionBand = 50;
        var lensArea = new SKRect(
            area.Left + 8,
            area.Top + 8,
            area.Right - 8,
            area.Bottom - dimensionBand);
        var scale = Math.Min(
            (lensArea.Width - 24) / (float)zSpan,
            (lensArea.Height - 24) / (float)ySpan);
        var drawingWidth = (float)zSpan * scale;
        var drawingHeight = (float)ySpan * scale;
        var originX = lensArea.MidX - (drawingWidth / 2);
        var originY = lensArea.MidY + (drawingHeight / 2);

        SKPoint Map(ScenePoint2Dto point) => new(
            originX + ((float)(point.Z - zMin) * scale),
            originY - ((float)(point.Y - yMin) * scale));
        float MapZ(double z) => originX + ((float)(z - zMin) * scale);

        var fills = new[]
        {
            new SKColor(198, 218, 232, 176),
            new SKColor(210, 226, 207, 176),
            new SKColor(232, 219, 192, 176),
            new SKColor(218, 207, 229, 176)
        };
        for (var index = 0; index < lenses.Count; index++)
        {
            using var path = new SKPath();
            path.MoveTo(Map(lenses[index].Boundary[0]));
            foreach (var point in lenses[index].Boundary.Skip(1))
            {
                path.LineTo(Map(point));
            }

            path.Close();
            using var fill = new SKPaint
            {
                Color = fills[index % fills.Length],
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            };
            canvas.DrawPath(path, fill);
            canvas.DrawPath(path, outline);
        }

        var dimensionIndex = 0;
        for (var index = 0; index + 1 < lenses.Count; index++)
        {
            var left = lenses[index].BackVertexZ;
            var right = lenses[index + 1].FrontVertexZ;
            var gap = right - left;
            if (gap <= 1e-6)
            {
                continue;
            }

            var dimensionY = area.Bottom - 13 - ((dimensionIndex % 2) * 17);
            DrawHorizontalDimension(
                canvas,
                MapZ(left),
                MapZ(right),
                dimensionY,
                originY + 5,
                gap.ToString("0.###"),
                dimension);
            dimensionIndex++;
        }

        return ScaleDesignation(scale / MillimetersToPoints);
    }

    internal static IReadOnlyList<double> SystemAirGaps(Scene2Dto scene)
    {
        var lenses = BuildSystemLensGeometry(scene);
        var gaps = new List<double>();
        for (var index = 0; index + 1 < lenses.Count; index++)
        {
            var gap = lenses[index + 1].FrontVertexZ - lenses[index].BackVertexZ;
            if (gap > 1e-6)
            {
                gaps.Add(gap);
            }
        }

        return gaps;
    }

    private static IReadOnlyList<SystemLensGeometry> BuildSystemLensGeometry(Scene2Dto scene)
    {
        var lenses = new List<SystemLensGeometry>();
        foreach (var element in scene.LensElements)
        {
            var boundary = element.Boundary
                .Where(point => double.IsFinite(point.Z) && double.IsFinite(point.Y))
                .ToArray();
            if (boundary.Length < 4)
            {
                continue;
            }

            var midpoint = boundary.Length / 2;
            var frontVertex = boundary
                .Take(midpoint)
                .MinBy(point => Math.Abs(point.Y))!;
            var backVertex = boundary
                .Skip(midpoint)
                .MinBy(point => Math.Abs(point.Y))!;
            lenses.Add(new SystemLensGeometry(
                element,
                boundary,
                frontVertex.Z,
                backVertex.Z));
        }

        return lenses
            .OrderBy(lens => lens.FrontVertexZ)
            .ToArray();
    }

    private sealed record SystemLensGeometry(
        SceneLensElement2Dto Element,
        IReadOnlyList<ScenePoint2Dto> Boundary,
        double FrontVertexZ,
        double BackVertexZ);
}

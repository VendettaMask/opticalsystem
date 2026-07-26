using System.Collections.Concurrent;
using OptilandWorkbench.Application.Contracts;
using SkiaSharp;

namespace OptilandWorkbench.App.Manufacturing;

internal static partial class OpticalDrawingRendererCore
{
private static void DrawTitleBlock(
        SKCanvas canvas,
        OpticalDrawingSheet sheet,
        float x,
        float y,
        float width,
        float height,
        SKPaint thin,
        SKPaint medium,
        string scaleDesignation)
    {
        var brandWidth = width * 0.35f;
        var approvalWidth = width * 0.27f;
        var approvalX = x + brandWidth;
        var detailX = approvalX + approvalWidth;
        var detailWidth = width - brandWidth - approvalWidth;
        canvas.DrawRect(x, y, width, height, medium);
        canvas.DrawLine(approvalX, y, approvalX, y + height, thin);
        canvas.DrawLine(detailX, y, detailX, y + height, thin);

        DrawCompanyLogo(
            canvas,
            sheet.CompanyLogoPng,
            new SKRect(x + 8, y + 8, x + brandWidth - 8, y + height - 8));

        var approvalRow = height / 4;
        for (var index = 1; index < 4; index++)
        {
            canvas.DrawLine(approvalX, y + (approvalRow * index), detailX, y + (approvalRow * index), thin);
        }

        var approvalSplit = approvalX + (approvalWidth * 0.42f);
        canvas.DrawLine(approvalSplit, y + approvalRow, approvalSplit, y + height, thin);
        DrawFittedText(
            canvas,
            StandardDesignation(sheet.Standard),
            approvalX + (approvalWidth / 2),
            y + 13,
            approvalWidth - 10,
            6.2f,
            SKTextAlign.Center,
            true);
        DrawText(canvas, "设计", approvalX + 7, y + approvalRow + 13, 6.4f, SKTextAlign.Left);
        DrawFittedText(canvas, sheet.Designer, approvalSplit + 5, y + approvalRow + 13, detailX - approvalSplit - 10, 6.4f, SKTextAlign.Left);
        DrawText(canvas, "审核", approvalX + 7, y + (approvalRow * 2) + 13, 6.4f, SKTextAlign.Left);
        DrawFittedText(canvas, sheet.Reviewer, approvalSplit + 5, y + (approvalRow * 2) + 13, detailX - approvalSplit - 10, 6.4f, SKTextAlign.Left);
        DrawText(canvas, "批准", approvalX + 7, y + (approvalRow * 3) + 13, 6.4f, SKTextAlign.Left);
        DrawText(canvas, "投影：第一角法", approvalSplit + 5, y + (approvalRow * 3) + 13, 6.1f, SKTextAlign.Left);

        var titleRow = 27f;
        var numberRow = 24f;
        canvas.DrawLine(detailX, y + titleRow, x + width, y + titleRow, thin);
        canvas.DrawLine(detailX, y + titleRow + numberRow, x + width, y + titleRow + numberRow, thin);
        var revisionX = x + width - 45;
        canvas.DrawLine(revisionX, y, revisionX, y + titleRow + numberRow, thin);
        DrawFittedText(canvas, sheet.PartName, detailX + 8, y + 18, revisionX - detailX - 16, 10.5f, SKTextAlign.Left, true);
        DrawText(canvas, $"版本 {sheet.Revision}", revisionX + 5, y + 17, 6.4f, SKTextAlign.Left);
        DrawText(canvas, "图号", detailX + 7, y + titleRow + 15, 6.3f, SKTextAlign.Left);
        DrawFittedText(canvas, sheet.DrawingNumber, detailX + 48, y + titleRow + 17, revisionX - detailX - 54, 9.5f, SKTextAlign.Left, true);
        DrawText(canvas, sheet.PageSize == OpticalDrawingPageSize.A3 ? "A3" : "A4", revisionX + 15, y + titleRow + 17, 9, SKTextAlign.Center, true);

        var bottomTop = y + titleRow + numberRow;
        var bottomWidth = detailWidth / 3;
        canvas.DrawLine(detailX + bottomWidth, bottomTop, detailX + bottomWidth, y + height, thin);
        canvas.DrawLine(detailX + (bottomWidth * 2), bottomTop, detailX + (bottomWidth * 2), y + height, thin);
        DrawText(canvas, "尺寸：mm", detailX + 7, bottomTop + 14, 6.5f, SKTextAlign.Left);
        DrawText(canvas, $"比例：{scaleDesignation}", detailX + bottomWidth + 7, bottomTop + 14, 6.5f, SKTextAlign.Left);
        DrawText(canvas, "页码：1 / 1", detailX + (bottomWidth * 2) + 7, bottomTop + 14, 6.5f, SKTextAlign.Left);
    }
    private static void DrawSystemTitleBlock(
        SKCanvas canvas,
        OpticalSystemDrawingSheet sheet,
        float x,
        float y,
        float width,
        float height,
        SKPaint thin,
        SKPaint medium,
        string scaleDesignation)
    {
        var brandWidth = width * 0.35f;
        var approvalWidth = width * 0.27f;
        var approvalX = x + brandWidth;
        var detailX = approvalX + approvalWidth;
        var detailWidth = width - brandWidth - approvalWidth;
        canvas.DrawRect(x, y, width, height, medium);
        canvas.DrawLine(approvalX, y, approvalX, y + height, thin);
        canvas.DrawLine(detailX, y, detailX, y + height, thin);

        DrawCompanyLogo(
            canvas,
            sheet.CompanyLogoPng,
            new SKRect(x + 8, y + 8, x + brandWidth - 8, y + height - 8));

        var approvalRow = height / 4;
        for (var index = 1; index < 4; index++)
        {
            canvas.DrawLine(approvalX, y + (approvalRow * index), detailX, y + (approvalRow * index), thin);
        }

        var approvalSplit = approvalX + (approvalWidth * 0.42f);
        canvas.DrawLine(approvalSplit, y + approvalRow, approvalSplit, y + height, thin);
        DrawFittedText(
            canvas,
            StandardDesignation(sheet.Standard),
            approvalX + (approvalWidth / 2),
            y + 13,
            approvalWidth - 10,
            6.2f,
            SKTextAlign.Center,
            true);
        DrawText(canvas, "\u8bbe\u8ba1", approvalX + 7, y + approvalRow + 13, 6.4f, SKTextAlign.Left);
        DrawFittedText(canvas, sheet.Designer, approvalSplit + 5, y + approvalRow + 13, detailX - approvalSplit - 10, 6.4f, SKTextAlign.Left);
        DrawText(canvas, "\u5ba1\u6838", approvalX + 7, y + (approvalRow * 2) + 13, 6.4f, SKTextAlign.Left);
        DrawFittedText(canvas, sheet.Reviewer, approvalSplit + 5, y + (approvalRow * 2) + 13, detailX - approvalSplit - 10, 6.4f, SKTextAlign.Left);
        DrawText(canvas, "\u6279\u51c6", approvalX + 7, y + (approvalRow * 3) + 13, 6.4f, SKTextAlign.Left);
        DrawText(canvas, "\u6295\u5f71\uff1a\u7b2c\u4e00\u89d2\u6cd5", approvalSplit + 5, y + (approvalRow * 3) + 13, 6.1f, SKTextAlign.Left);

        const float titleRow = 27;
        const float numberRow = 24;
        canvas.DrawLine(detailX, y + titleRow, x + width, y + titleRow, thin);
        canvas.DrawLine(detailX, y + titleRow + numberRow, x + width, y + titleRow + numberRow, thin);
        var revisionX = x + width - 45;
        canvas.DrawLine(revisionX, y, revisionX, y + titleRow + numberRow, thin);
        DrawFittedText(canvas, sheet.PartName, detailX + 8, y + 18, revisionX - detailX - 16, 10.5f, SKTextAlign.Left, true);
        DrawText(canvas, $"\u7248\u672c {sheet.Revision}", revisionX + 5, y + 17, 6.4f, SKTextAlign.Left);
        DrawText(canvas, "\u56fe\u53f7", detailX + 7, y + titleRow + 15, 6.3f, SKTextAlign.Left);
        DrawFittedText(canvas, sheet.DrawingNumber, detailX + 48, y + titleRow + 17, revisionX - detailX - 54, 9.5f, SKTextAlign.Left, true);
        DrawText(canvas, sheet.PageSize == OpticalDrawingPageSize.A3 ? "A3" : "A4", revisionX + 15, y + titleRow + 17, 9, SKTextAlign.Center, true);

        var bottomTop = y + titleRow + numberRow;
        var bottomWidth = detailWidth / 3;
        canvas.DrawLine(detailX + bottomWidth, bottomTop, detailX + bottomWidth, y + height, thin);
        canvas.DrawLine(detailX + (bottomWidth * 2), bottomTop, detailX + (bottomWidth * 2), y + height, thin);
        DrawText(canvas, "\u5c3a\u5bf8\uff1amm", detailX + 7, bottomTop + 14, 6.5f, SKTextAlign.Left);
        DrawText(canvas, $"\u6bd4\u4f8b\uff1a{scaleDesignation}", detailX + bottomWidth + 7, bottomTop + 14, 6.5f, SKTextAlign.Left);
        DrawText(canvas, "\u9875\u7801\uff1a1 / 1", detailX + (bottomWidth * 2) + 7, bottomTop + 14, 6.5f, SKTextAlign.Left);
    }


    private static void DrawCompanyLogo(SKCanvas canvas, byte[]? png, SKRect bounds)
    {
        if (png is { Length: > 0 } && DrawImportedLogo(canvas, png, bounds))
        {
            return;
        }

        if (DefaultCompanyLogoPng.Value is { Length: > 0 } defaultLogo
            && DrawImportedLogo(canvas, defaultLogo, bounds))
        {
            return;
        }

        var centerX = bounds.MidX;
        var centerY = bounds.MidY;
        DrawText(canvas, "S.T.A.R.", centerX, centerY - 8, 18, SKTextAlign.Center, true);
        using var accent = Stroke(new SKColor(39, 93, 119), 1.2f);
        canvas.DrawLine(bounds.Left + 24, centerY, bounds.Right - 24, centerY, accent);
        DrawText(canvas, "L A B S", centerX, centerY + 14, 8.5f, SKTextAlign.Center, true);
    }

    private static bool DrawImportedLogo(
        SKCanvas canvas,
        byte[] png,
        SKRect bounds)
    {
        try
        {
            using var data = SKData.CreateCopy(png);
            using var decoded = SKBitmap.Decode(data);
            if (decoded is null || decoded.Width <= 0 || decoded.Height <= 0)
            {
                return false;
            }

            using var image = SKImage.FromBitmap(decoded);

            var scale = Math.Min(bounds.Width / image.Width, bounds.Height / image.Height);
            var width = image.Width * scale;
            var height = image.Height * scale;
            var destination = SKRect.Create(
                bounds.MidX - (width / 2),
                bounds.MidY - (height / 2),
                width,
                height);
            canvas.DrawImage(image, destination);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static SKBitmap RemoveLightBackgroundAndCrop(SKBitmap source)
    {
        var left = source.Width;
        var top = source.Height;
        var right = -1;
        var bottom = -1;
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                if (IsLightNeutral(source.GetPixel(x, y)))
                {
                    continue;
                }

                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }
        }

        if (right < left || bottom < top)
        {
            return new SKBitmap(1, 1, true);
        }

        const int padding = 12;
        left = Math.Max(0, left - padding);
        top = Math.Max(0, top - padding);
        right = Math.Min(source.Width - 1, right + padding);
        bottom = Math.Min(source.Height - 1, bottom + padding);
        var result = new SKBitmap(
            new SKImageInfo(
                right - left + 1,
                bottom - top + 1,
                SKColorType.Rgba8888,
                SKAlphaType.Premul));
        result.Erase(SKColors.Transparent);
        for (var y = top; y <= bottom; y++)
        {
            for (var x = left; x <= right; x++)
            {
                var color = source.GetPixel(x, y);
                if (!IsLightNeutral(color))
                {
                    result.SetPixel(x - left, y - top, color);
                }
            }
        }

        return result;
    }

    private static bool IsLightNeutral(SKColor color)
    {
        var minimum = Math.Min(color.Red, Math.Min(color.Green, color.Blue));
        var maximum = Math.Max(color.Red, Math.Max(color.Green, color.Blue));
        return color.Alpha == 0 || (minimum >= 232 && maximum - minimum <= 10);
    }

    private static byte[]? LoadDefaultCompanyLogo()
    {
        using var stream = typeof(OpticalDrawingRenderer).Assembly
            .GetManifestResourceStream(CompanyLogoResourceName);
        if (stream is null)
        {
            return null;
        }

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        using var data = SKData.CreateCopy(memory.ToArray());
        using var decoded = SKBitmap.Decode(data);
        if (decoded is null || decoded.Width <= 0 || decoded.Height <= 0)
        {
            return null;
        }

        using var prepared = RemoveLightBackgroundAndCrop(decoded);
        using var image = SKImage.FromBitmap(prepared);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded?.ToArray();
    }
}

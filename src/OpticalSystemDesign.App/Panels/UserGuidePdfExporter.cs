using System.Globalization;
using Avalonia.Platform;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Services;
using SkiaSharp;

namespace OpticalSystemDesign.App.Panels;

internal enum UserGuidePdfScope { CurrentChapter, Calculations, CompleteGuide }
internal sealed record UserGuidePdfResult(int Pages, int Chapters, int Formulas, int Screenshots);

/// <summary>Native offline PDF: selectable embedded-font text, vector mathematics, real screenshots.</summary>
internal static class UserGuidePdfExporter
{
    internal static UserGuidePdfResult Export(string path, IReadOnlyList<UserGuidePanel.GuideChapter> chapters,
        string language, CancellationToken cancellationToken = default, Action<int>? pageProgress = null)
    {
        if (chapters.Count == 0) throw new InvalidOperationException("No guide chapters selected.");
        UserGuidePdfResult? result = null;
        BoundedApplicationFile.WriteAtomic(path, BoundedApplicationFile.MaximumImageDataBytes, "User guide PDF", stream =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var document = SKDocument.CreatePdf(stream, new SKDocumentPdfMetadata
            { Title = L("Optical System Design 用户指南", "Optical System Design User Guide", "Optical System Design ユーザーガイド"), Author = "S.T.A.R.Labs" })
                ?? throw new InvalidOperationException("Unable to create PDF.");
            using var fontStream = typeof(UserGuidePdfExporter).Assembly.GetManifestResourceStream("OpticalSystemDesign.App.Assets.Fonts.NotoSansCJKsc-Regular.otf");
            using var typeface = SKTypeface.FromStream(fontStream!) ?? throw new InvalidOperationException("Guide font unavailable.");
            using var renderer = new Writer(document, typeface, language, cancellationToken, pageProgress);
            renderer.StartPage();
            renderer.Text(L("用户指南", "User Guide", "ユーザーガイド"), 25, true, 16);
            renderer.Text("Optical System Design", 15, false, 12);
            renderer.Text(L("包含操作说明、公式及界面截图。数值验证范围以各分析说明为准。", "Instructions, typeset formulas and UI screenshots. Numerical validation remains limited to each analysis' documented scope.", "操作説明、数式、実画面を収録。数値検証の範囲は各解析の説明に従います。"), 10.5f, false, 18);
            if (chapters.Count > 1)
            {
                renderer.Text(L("目录", "Contents", "目次"), 15, true, 10);
                for (var i = 0; i < chapters.Count; i++) renderer.Text($"{i + 1}. {chapters[i].Title.Value(language)}", 10.5f, false, 3);
            }
            var first = true;
            foreach (var chapter in chapters)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (chapters.Count > 1 || !first) renderer.StartPage();
                first = false;
                renderer.Text(chapter.Group.Value(language), 9, false, 8);
                renderer.Text(chapter.Title.Value(language), 19, true, 10);
                renderer.Text(chapter.Summary.Value(language), 11, false, 20);
                foreach (var section in chapter.Sections)
                {
                    renderer.ReserveSection(section.Title.Value(language), section.Body.Value(language), section.Formula);
                    renderer.Text(section.Title.Value(language), 13, true, 8);
                    renderer.Text(section.Body.Value(language), 10.5f, false, 10);
                    if (section.Formula is { } formula) renderer.Formula(formula);
                    if (section.Screenshot is { } screenshot) renderer.Screenshot(screenshot);
                }
                foreach (var screenshot in UserGuideScreenshots.ForChapter(chapter.Id)) renderer.Screenshot(screenshot);
            }
            renderer.End();
            cancellationToken.ThrowIfCancellationRequested();
            document.Close();
            result = new(renderer.PageCount, chapters.Count, renderer.FormulaCount, renderer.ScreenshotCount);
        });
        return result!;
        string L(string zh, string en, string ja) => language switch { UiLocalization.English => en, UiLocalization.Japanese => ja, _ => zh };
    }

    private sealed class Writer(SKDocument document, SKTypeface typeface, string language, CancellationToken cancellationToken, Action<int>? pageProgress) : IDisposable
    {
        private const float Width = 595.28f, Height = 841.89f, Margin = 44, Bottom = Height - 48;
        private const float ContentWidth = Width - 2 * Margin;
        private readonly Dictionary<string, SKImage> _images = new(StringComparer.Ordinal);
        private SKCanvas? _canvas;
        private float _y;
        internal int PageCount { get; private set; }
        internal int FormulaCount { get; private set; }
        internal int ScreenshotCount { get; private set; }
        internal void StartPage()
        {
            End();
            if (++PageCount > 1000) throw new InvalidOperationException("Guide PDF exceeds the page budget.");
            _canvas = document.BeginPage(Width, Height);
            _canvas.Clear(SKColors.White);
            using var font = new SKFont(typeface, 8);
            using var paint = new SKPaint { Color = new SKColor(80, 92, 110), IsAntialias = true };
            _canvas.DrawText("Optical System Design", Margin, 25, SKTextAlign.Left, font, paint);
            BrandAssets.DrawCompanyLogo(_canvas, new SKRect(Width - Margin - 82, 14, Width - Margin, 26), new SKColor(60, 75, 95));
            _canvas.DrawText(PageCount.ToString(CultureInfo.InvariantCulture), Width / 2, Height - 22, SKTextAlign.Center, font, paint);
            _y = 57;
            pageProgress?.Invoke(PageCount);
        }
        internal void Reserve(float height)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (height > Bottom - 57) throw new InvalidOperationException("Guide block exceeds printable page height.");
            if (_y + height > Bottom) StartPage();
        }
        internal void Text(string text, float size, bool bold, float after)
        {
            using var font = new SKFont(typeface, size) { Embolden = bold };
            using var paint = new SKPaint { Color = new SKColor(28, 43, 62), IsAntialias = true };
            var lineHeight = size * 1.65f;
            foreach (var paragraph in text.Replace("\r", "", StringComparison.Ordinal).Split('\n'))
            {
                foreach (var line in Wrap(paragraph, font))
                {
                    Reserve(lineHeight);
                    _canvas!.DrawText(line, Margin, _y + size * 1.12f, SKTextAlign.Left, font, paint);
                    _y += lineHeight;
                }
            }
            _y += after;
        }
        internal void ReserveSection(string title, string body, string? formula)
        {
            if (formula is null) { Reserve(65); return; }
            using var titleFont = new SKFont(typeface, 13) { Embolden = true };
            using var bodyFont = new SKFont(typeface, 10.5f);
            var height = Wrap(title, titleFont).Count() * 13 * 1.65f + 8;
            height += body.Replace("\r", "", StringComparison.Ordinal).Split('\n').Sum(p => Wrap(p, bodyFont).Count()) * 10.5f * 1.65f + 10;
            var art = UserGuideMath.Load(formula);
            var em = Math.Min(12f, (ContentWidth - 16) * 1000 / art.Width);
            height += art.Height * em / 1000 + 26;
            Reserve(Math.Min(height, Bottom - 57));
        }
        private static IEnumerable<string> Wrap(string text, SKFont font)
        {
            if (text.Length == 0) { yield return ""; yield break; }
            var start = 0;
            while (start < text.Length)
            {
                var end = start;
                var lastSpace = -1;
                var width = 0f;
                while (end < text.Length)
                {
                    var count = char.IsHighSurrogate(text[end]) && end + 1 < text.Length ? 2 : 1;
                    var nextWidth = font.MeasureText(text.AsSpan(end, count));
                    if (width + nextWidth > ContentWidth && end > start) break;
                    width += nextWidth;
                    if (char.IsWhiteSpace(text[end])) lastSpace = end;
                    end += count;
                }
                if (end < text.Length && lastSpace > start && end - lastSpace < 24) end = lastSpace + 1;
                yield return text[start..end].TrimEnd();
                start = end;
                while (start < text.Length && text[start] == ' ') start++;
            }
        }
        internal void Formula(string id)
        {
            var artwork = UserGuideMath.Load(id);
            var em = Math.Min(12f, (ContentWidth - 16) * 1000 / artwork.Width);
            if (em < 9.5f) throw new InvalidDataException("Formula must be split into more lines for print: " + id);
            var height = artwork.Height * em / 1000;
            Reserve(height + 26);
            UserGuideMath.Draw(_canvas!, artwork, Margin + 8, _y + 8, em, new SKColor(20, 42, 70));
            _y += height + 26;
            FormulaCount++;
        }
        internal void Screenshot(string key)
        {
            if (!_images.TryGetValue(key, out var image))
            {
                using var resource = AssetLoader.Open(UserGuideScreenshots.Resource(key, language));
                using var data = SKData.Create(resource);
                image = SKImage.FromEncodedData(data) ?? throw new InvalidDataException("Invalid guide screenshot: " + key);
                _images.Add(key, image);
            }
            Reserve(200);
            var availableHeight = Math.Min(320, Bottom - _y - 53);
            var scale = Math.Min(ContentWidth / image.Width, availableHeight / image.Height);
            var width = image.Width * scale; var height = image.Height * scale;
            Reserve(height + 53);
            _canvas!.DrawImage(image, new SKRect(Margin, _y, Margin + width, _y + height), new SKSamplingOptions(SKFilterMode.Linear));
            _y += height + 6;
            Text(UserGuideScreenshots.All.Single(s => s.Key == key).Title(language), 9, false, 13);
            ScreenshotCount++;
        }
        internal void End()
        {
            if (_canvas is null) return;
            document.EndPage(); _canvas = null;
        }
        public void Dispose() { foreach (var image in _images.Values) image.Dispose(); }
    }
}

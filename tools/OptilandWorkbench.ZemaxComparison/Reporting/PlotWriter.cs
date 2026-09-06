using SkiaSharp;
using OptilandWorkbench.ZemaxComparison.Metrics;

namespace OptilandWorkbench.ZemaxComparison.Reporting;

public static class PlotWriter
{
    public static void Curves(string directory, string id, MatchedValues v, string xLabel, string yLabel)
    {
        Draw(false); Draw(true);
        void Draw(bool difference)
        {
            using var bitmap = new SKBitmap(1000, 650); using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.White); using var paint = new SKPaint { IsAntialias = true, Color = SKColors.Black };
            using var font = new SKFont(SKTypeface.Default, 18);
            var a = difference ? v.Workbench.Zip(v.Zemax, (w, z) => w - z).ToArray() : v.Workbench;
            var b = difference ? a : v.Zemax;
            var minX = v.X.Min(); var maxX = v.X.Max(); var minY = Math.Min(a.Min(), b.Min()); var maxY = Math.Max(a.Max(), b.Max());
            if (maxY == minY) { minY -= 0.5; maxY += 0.5; }
            if (maxX == minX) maxX = minX + 1;
            float X(double x) => (float)(90 + 860 * (x - minX) / (maxX - minX));
            float Y(double y) => (float)(540 - 440 * (y - minY) / (maxY - minY));
            canvas.DrawText(id + (difference ? " : Workbench - Zemax" : " : blue Workbench / red Zemax"), 90, 40, font, paint);
            for (var i = 0; i <= 5; i++)
            {
                var xx = minX + (maxX - minX) * i / 5; var yy = minY + (maxY - minY) * i / 5;
                canvas.DrawText(xx.ToString("G4", System.Globalization.CultureInfo.InvariantCulture), X(xx) - 20, 570, font, paint);
                canvas.DrawText(yy.ToString("G4", System.Globalization.CultureInfo.InvariantCulture), 3, Y(yy), font, paint);
            }
            canvas.DrawLine(90, 100, 90, 540, paint); canvas.DrawLine(90, 540, 950, 540, paint);
            canvas.DrawText(xLabel, 380, 610, font, paint); canvas.DrawText(yLabel, 90, 80, font, paint);
            foreach (var (values, color) in new[] { (b, SKColors.IndianRed), (a, SKColors.RoyalBlue) })
            {
                paint.Color = color; paint.StrokeWidth = 2;
                for (var i = 1; i < values.Length; i++) canvas.DrawLine(X(v.X[i - 1]), Y(values[i - 1]), X(v.X[i]), Y(values[i]), paint);
            }
            Save(bitmap, Path.Combine(directory, id + (difference ? "-difference.png" : "-overlay.png")));
        }
    }
    public static void Grid(string directory, string id, MatchedValues v)
    {
        var x = v.X.Distinct().Order().ToArray(); var y = v.Y!.Distinct().Order().ToArray();
        var commonMin = Math.Min(v.Workbench.Min(), v.Zemax.Min()); var commonMax = Math.Max(v.Workbench.Max(), v.Zemax.Max());
        foreach (var (tag, values) in new[] { ("workbench", v.Workbench), ("zemax", v.Zemax), ("difference", v.Workbench.Zip(v.Zemax, (a, b) => a - b).ToArray()) })
        {
            using var bitmap = new SKBitmap(800, 780); using var canvas = new SKCanvas(bitmap); canvas.Clear(SKColors.White);
            using var paint = new SKPaint { IsAntialias = false }; using var font = new SKFont(SKTypeface.Default, 18);
            var lo = tag == "difference" ? -values.Max(Math.Abs) : commonMin; var hi = tag == "difference" ? values.Max(Math.Abs) : commonMax;
            paint.Color = SKColors.Black; canvas.DrawText(id + " " + tag + " (raw numerical redraw)", 60, 35, font, paint);
            canvas.DrawText($"scale [{lo:G6}, {hi:G6}]; white = invalid; +Y up", 60, 65, font, paint);
            for (var i = 0; i < values.Length; i++)
            {
                var cx = Array.BinarySearch(x, v.X[i]); var cy = Array.BinarySearch(y, v.Y![i]);
                var t = hi == lo ? 0.5 : Math.Clamp((values[i] - lo) / (hi - lo), 0, 1);
                paint.Color = new SKColor((byte)(255 * t), (byte)(180 * (1 - Math.Abs(t - 0.5) * 2)), (byte)(255 * (1 - t)));
                canvas.DrawRect(70 + 640f * cx / x.Length, 100 + 600f * (y.Length - cy - 1) / y.Length,
                    640f / x.Length + 0.1f, 600f / y.Length + 0.1f, paint);
            }
            paint.Color = SKColors.Black;
            canvas.DrawText($"X [{x[0]:G6}, {x[^1]:G6}] ; Y [{y[0]:G6}, {y[^1]:G6}]", 70, 740, font, paint);
            Save(bitmap, Path.Combine(directory, id + "-" + tag + ".png"));
        }
    }
    private static void Save(SKBitmap bitmap, string path)
    {
        using var image = SKImage.FromBitmap(bitmap); using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path); data.SaveTo(stream);
    }
}

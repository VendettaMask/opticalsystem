using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class ScalarMapRasterTests
{
    public static IEnumerable<object[]> PalettesAndTransforms()
    {
        foreach (var map in new[] { AnalysisColorMap.InverseGray, AnalysisColorMap.Jet, AnalysisColorMap.Viridis })
            foreach (var opacity in new[] { 1d, .85 })
                foreach (var scale in new[] { 1d, 1.75 })
                    yield return [map, opacity, scale];
    }

    [Theory]
    [MemberData(nameof(PalettesAndTransforms))]
    public async Task RasterKernelRetainsCellValuesCoordinatesOpacityAndMissingSamples(
        AnalysisColorMap palette, double opacity, double scale)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(HeadlessTestApplication));
        await session.Dispatch(() =>
        {
            var previous = ScientificColorMaps.Current;
            try
            {
                ScientificColorMaps.Configure(new AppSettings
                {
                    AnalysisColorMap = palette == AnalysisColorMap.Jet ? "Rainbow" : "Viridis"
                });
                using var vector = Capture(new ReferenceRasterMap(palette, opacity, scale), scale);
                using var raster = Capture(new SampleMap(true, true, palette, opacity), scale);
                var expected = Pixels(vector);
                var actual = Pixels(raster);
                for (var row = 0; row < 3; row++)
                    for (var column = 0; column < 3; column++)
                    {
                        var x = (int)Math.Floor((25 + column * 30) * scale);
                        var y = (int)Math.Floor((25 + row * 30) * scale);
                        var index = (y * vector.PixelSize.Width + x) * 4;
                        // Independent CPU image composition uses the public image
                        // API. Compare exact sample colours and alpha, not a fitted
                        // tolerance between Skia's solid and image blitters.
                        for (var channel = 0; channel < 3; channel++)
                            Assert.Equal(expected[index + channel], actual[index + channel]);
                        Assert.Equal(255, actual[index + 3]);
                    }
            }
            finally { ScientificColorMaps.Configure(new AppSettings { AnalysisColorMap = previous.ToString() }); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(.85, 1.75)]
    public async Task SoftwareDispatchKeepsEveryOriginalPixel(double opacity, double scale)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(HeadlessTestApplication));
        await session.Dispatch(() =>
        {
            using var vector = Capture(new SampleMap(false, false, AnalysisColorMap.Jet, opacity), scale);
            using var dispatched = Capture(new SampleMap(true, false, AnalysisColorMap.Jet, opacity), scale);
            Assert.Equal(Pixels(vector), Pixels(dispatched));
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RasterKernelReusesOneImageForTheSameDeviceState()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(HeadlessTestApplication));
        var listener = new CapturedTrace();
        Trace.Listeners.Add(listener);
        try
        {
            await session.Dispatch(() =>
            {
                using var bitmap = Capture(new SampleMap(true, true, AnalysisColorMap.InverseGray, .85, 2), 1.75);
            }, CancellationToken.None);
            Assert.Single(listener.Lines, line => line.Contains("native submission=one image.", StringComparison.Ordinal));
        }
        finally { Trace.Listeners.Remove(listener); listener.Dispose(); }
    }

    [Fact]
    public void PaletteChangeInvalidatesTheRecordedOperationWithTheSameGrid()
    {
        var before = ScientificColorMaps.Current;
        try
        {
            double[] coordinates = [-1, 0, 1];
            var values = new double[3, 3];
            ScientificColorMaps.Configure(new AppSettings { AnalysisColorMap = "Viridis" });
            using var first = new ScalarMapRasterOperation(coordinates, coordinates, values,
                new Rect(0, 0, 90, 90), 1, 1, 0, 1, AnalysisColorMap.Jet, EdgeMode.Unspecified);
            ScientificColorMaps.Configure(new AppSettings { AnalysisColorMap = "Rainbow" });
            using var second = new ScalarMapRasterOperation(coordinates, coordinates, values,
                new Rect(0, 0, 90, 90), 1, 1, 0, 1, AnalysisColorMap.Jet, EdgeMode.Unspecified);
            Assert.False(first.Equals(second));
        }
        finally { ScientificColorMaps.Configure(new AppSettings { AnalysisColorMap = before.ToString() }); }
    }

    private static RenderTargetBitmap Capture(Control control, double scale)
    {
        control.Width = control.Height = 110;
        control.Measure(new Size(110, 110));
        control.Arrange(new Rect(0, 0, 110, 110));
        var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(110 * scale), (int)Math.Ceiling(110 * scale)),
            new Vector(96 * scale, 96 * scale));
        bitmap.Render(control);
        return bitmap;
    }

    private static byte[] Pixels(Bitmap bitmap)
    {
        var stride = bitmap.PixelSize.Width * 4;
        var result = new byte[stride * bitmap.PixelSize.Height];
        var pointer = Marshal.AllocHGlobal(result.Length);
        try { bitmap.CopyPixels(new PixelRect(bitmap.PixelSize), pointer, result.Length, stride); Marshal.Copy(pointer, result, 0, result.Length); }
        finally { Marshal.FreeHGlobal(pointer); }
        return result;
    }

    private sealed class SampleMap(bool custom, bool forceKernel, AnalysisColorMap palette, double opacity,
        int repetitions = 1) : Control
    {
        private static readonly double[,] Values = { { 0, .5, 1 }, { .25, double.NaN, .75 }, { 1, 0, .5 } };
        public override void Render(DrawingContext context)
        {
            context.FillRectangle(Brushes.CornflowerBlue, new Rect(0, 0, 110, 110));
            using var alpha = context.PushOpacity(opacity);
            using var clip = context.PushClip(new Rect(10, 10, 90, 90));
            if (custom)
            {
                var map = new ScalarMapRasterOperation([-1, 0, 1], [-1, 0, 1], Values,
                    new Rect(10, 10, 90, 90), 1, 1, 0, 1, palette, EdgeMode.Unspecified);
                context.Custom(forceKernel ? new RasterKernel(map, repetitions) : map);
            }
            else
                for (var row = 0; row < 3; row++)
                    for (var column = 0; column < 3; column++)
                        if (double.IsFinite(Values[row, column]))
                            context.DrawRectangle(new ImmutableSolidColorBrush(ScientificColorMaps.ScalarColor(palette, Values[row, column])),
                                null, new Rect(10 + column * 30, 10 + (2 - row) * 30, 30.35, 30.35));
        }
    }

    private sealed class ReferenceRasterMap(AnalysisColorMap palette, double opacity, double scale) : Control
    {
        public override void Render(DrawingContext context)
        {
            context.FillRectangle(Brushes.CornflowerBlue, new Rect(0, 0, 110, 110));
            var pixels = (int)Math.Ceiling(110 * scale);
            using var image = new RenderTargetBitmap(new PixelSize(pixels, pixels), new Vector(96, 96));
            using (var drawing = image.CreateDrawingContext())
            using (drawing.PushTransform(Matrix.CreateScale(scale, scale)))
            using (drawing.PushOpacity(opacity))
            using (drawing.PushClip(new Rect(10, 10, 90, 90)))
            {
                double[,] samples = { { 0, .5, 1 }, { .25, double.NaN, .75 }, { 1, 0, .5 } };
                for (var row = 0; row < 3; row++)
                    for (var column = 0; column < 3; column++)
                        if (double.IsFinite(samples[row, column]))
                            drawing.DrawRectangle(new ImmutableSolidColorBrush(ScientificColorMaps.ScalarColor(palette, samples[row, column])),
                                null, new Rect(10 + column * 30, 10 + (2 - row) * 30, 30.35, 30.35));
            }
            context.DrawImage(image, new Rect(0, 0, pixels / scale, pixels / scale));
        }
    }

    private sealed class RasterKernel(ScalarMapRasterOperation map, int repetitions) : ICustomDrawOperation
    {
        public Rect Bounds => map.Bounds;
        public bool HitTest(Point point) => map.HitTest(point);
        public bool Equals(ICustomDrawOperation? other) => ReferenceEquals(this, other);
        public void Render(ImmediateDrawingContext context)
        {
            var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>()!;
            using var lease = feature.Lease();
            for (var index = 0; index < repetitions; index++) map.RenderRasterized(lease);
        }
        public void Dispose() => map.Dispose();
    }

    private sealed class CapturedTrace : TraceListener
    {
        internal ConcurrentQueue<string> Lines { get; } = new();
        public override void Write(string? message) { if (message is not null) Lines.Enqueue(message); }
        public override void WriteLine(string? message) => Write(message);
    }
}

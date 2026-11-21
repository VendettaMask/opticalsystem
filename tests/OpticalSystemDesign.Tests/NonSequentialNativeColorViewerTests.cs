using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class NonSequentialNativeColorViewerTests
{
    [Fact]
    public async Task NativeAndBeforeAfterComputedPixelsUseIdenticalScientificPlotGeometry()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OpticalSystemDesign.App.App));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
            DisplayTypography.Configure(new() { Theme = "Light", Language = "en-US" }); UiLocalization.Apply("en-US");
            var root = NonSequentialColorFringesTests.Root;
            var path = Path.Combine(root, "validation/rechecks/2026-11-21/nsc-color-parity");
            using var reference = JsonDocument.Parse(File.ReadAllText(Path.Combine(path, "native/viewer-rgb.json")));
            using var before = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "validation/rechecks/2026-11-21/nsc-color-fringes/million-ray-data.json")));
            using var after = JsonDocument.Parse(File.ReadAllText(Path.Combine(path, "after.json")));
            var channels = new[] { "r", "g", "b" }.Select(c => reference.RootElement.GetProperty(c).Deserialize<double[]>()!).ToArray();
            var colors = new[] { Enumerable.Range(0, 30000).Select(i => channels[i % 3][i / 3] / 255).ToArray(),
                before.RootElement.GetProperty("Rgb").Deserialize<double[]>()!, after.RootElement.GetProperty("Rgb").Deserialize<double[]>()! };
            var names = new[] { "Zemax 2026 R1", "Before", "After" };
            var grid = new Grid { ColumnDefinitions = new("*,*,*"), ColumnSpacing = 12 };
            for (var c = 0; c < 3; c++)
            {
                var rgb = colors[c]; Assert.Equal(30000, rgb.Length);
                var points = Enumerable.Range(0, 10000).Select(i => new AnalysisPointDto(-10 + (i % 100 + .5) * .2, -10 + (i / 100 + .5) * .2,
                    Red: rgb[i * 3], Green: rgb[i * 3 + 1], Blue: rgb[i * 3 + 2])).ToArray();
                var plot = new AnalysisPlotControl
                {
                    PlotOptions = new(names[c] + " · 1,000,000 rays", EqualAspect: true, XMinimum: -10, XMaximum: 10, YMinimum: -10, YMaximum: 10,
                        GridOpacity: 0, ShowHorizontalZeroLine: false, ShowVerticalZeroLine: false),
                    Series = [new("X", "Y", points, AnalysisSeriesKind.Raster, XQuantity: AnalysisAxisQuantity.Coordinate, XUnit: AnalysisAxisUnit.Millimeter,
                        YQuantity: AnalysisAxisQuantity.Coordinate, YUnit: AnalysisAxisUnit.Millimeter)]
                };
                Grid.SetColumn(plot, c); grid.Children.Add(plot);
            }
            var window = new Window { Content = grid, Width = 1800, Height = 650 };
            try
            {
                window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                using var image = ExportImageFrame.Capture(grid, 1, grid);
                foreach (var x in new[] { 20, 620, 1220 })
                {
                    var pixel = new byte[4]; var handle = System.Runtime.InteropServices.GCHandle.Alloc(pixel, System.Runtime.InteropServices.GCHandleType.Pinned);
                    try { image.CopyPixels(new PixelRect(x, 5, 1, 1), handle.AddrOfPinnedObject(), 4, 4); }
                    finally { handle.Free(); }
                    Assert.All(pixel, channel => Assert.Equal(255, channel));
                }
                var capture = Environment.GetEnvironmentVariable("NSC_COLOR_CAPTURE_DIR");
                if (!string.IsNullOrWhiteSpace(capture)) image.Save(Path.Combine(capture, "comparison.png"), PngBitmapEncoderOptions.Default);
                Assert.Equal(1802, image.PixelSize.Width);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }
}

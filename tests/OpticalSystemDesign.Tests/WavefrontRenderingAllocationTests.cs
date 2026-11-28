using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class WavefrontRenderingAllocationTests
{
    [Fact]
    public void UnchangedLargeGridDoesNotAllocatePerSampleOnEveryRead()
    {
        var points = Enumerable.Range(0, 128 * 128)
            .Select(index => new AnalysisPointDto(index % 128, index / 128, Value: index / 16384d)).ToArray();
        var control = Surface(points);
        Assert.True(control.HasRenderableGrid);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 8; index++)
            Assert.True(control.HasRenderableGrid);
        Assert.InRange(GC.GetAllocatedBytesForCurrentThread() - before, 0, 128 * 1024);
    }

    [Fact]
    public void MutableInputListEditsInvalidateValuesCoordinatesCountAndUnavailableState()
    {
        var points = new List<AnalysisPointDto>
        {
            new(0, 0, Value: 0), new(1, 0, Value: 1),
            new(0, 1, Value: 2), new(1, 1, Value: 3)
        };
        var control = Surface(points);
        Assert.Equal(3, GridValue<double>(control, "Maximum"));
        points[3] = points[3] with { Value = -7 };
        Assert.Equal(-7, GridValue<double>(control, "Minimum"));
        points[3] = points[3] with { X = 2 };
        Assert.Equal(new[] { 0d, 1d, 2d }, GridValue<double[]>(control, "Xs"));
        points.RemoveAt(3);
        Assert.False(control.HasRenderableGrid);
        points.Add(new(1, 1, Value: 8));
        Assert.Equal(8, GridValue<double>(control, "Maximum"));
        for (var index = 0; index < points.Count; index++)
            points[index] = points[index] with { Value = double.NaN };
        Assert.False(control.HasRenderableGrid);
        points[0] = new(0, 0, Value: 0);
        points[1] = new(1, 0, Value: 1);
        points[2] = new(0, 1, Value: 2);
        points[3] = new(1, 1, Value: 3);
        Assert.True(control.HasRenderableGrid);
        Assert.Equal(3, GridValue<double>(control, "Maximum"));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0.5)]
    [InlineData(1)]
    public void FlatContourCellsDoNotAllocateIntersectionLists(double value)
    {
        var grid = new double[512, 512];
        for (var row = 0; row < 512; row++)
            for (var column = 0; column < 512; column++)
                grid[row, column] = value;
        _ = WavefrontSurfaceControl.BuildContourSegments(grid, 0.5);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var segments = WavefrontSurfaceControl.BuildContourSegments(grid, 0.5);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Empty(segments);
        Assert.InRange(allocated, 0, 64 * 1024);
    }

    [Theory]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(1)]
    public void PlaneContoursRetainExactInterpolatedEdgesAndLevelEquality(double level)
    {
        var grid = new double[5, 7];
        for (var row = 0; row < 5; row++)
            for (var column = 0; column < 7; column++)
                grid[row, column] = column / 6d;
        var segments = WavefrontSurfaceControl.BuildContourSegments(grid, level);
        Assert.Equal(4, segments.Count);
        for (var row = 0; row < 4; row++)
        {
            Assert.Equal(level * 6, segments[row].Start.X, 12);
            Assert.Equal(level * 6, segments[row].End.X, 12);
            Assert.Equal(new[] { (double)row, row + 1d },
                new[] { segments[row].Start.Y, segments[row].End.Y }.Order());
        }
    }

    [Fact]
    public async Task FirstFrameLogsStartAndCompletionForDiagnosingUIStalls()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(HeadlessTestApplication));
        var listener = new CapturedTrace();
        Trace.Listeners.Add(listener);
        try
        {
            await session.Dispatch(() =>
            {
                var control = Surface(new[]
                {
                    new AnalysisPointDto(0, 0, Value: 0), new(1, 0, Value: 1),
                    new(0, 1, Value: 0), new(1, 1, Value: 1)
                });
                var window = new Window { Width = 640, Height = 480, Content = control };
                try
                {
                    window.Show();
                    window.UpdateLayout();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using var bitmap = new RenderTargetBitmap(new PixelSize(640, 480));
                    bitmap.Render(control);
                }
                finally { window.Close(); }
            }, CancellationToken.None);
            var text = string.Join('\n', listener.Lines);
            Assert.Contains("Wavefront plot first frame started.", text);
            Assert.Contains("Points=4", text);
            Assert.Contains("Wavefront plot frame completed in", text);
        }
        finally { Trace.Listeners.Remove(listener); listener.Dispose(); }
    }

    private static WavefrontSurfaceControl Surface(IReadOnlyList<AnalysisPointDto> points) => new()
    {
        Series = new("Pupil X", "Pupil Y", points, AnalysisSeriesKind.Heatmap),
        DisplayAs = "灰度"
    };

    private static T GridValue<T>(WavefrontSurfaceControl control, string property)
    {
        object?[] arguments = [null];
        Assert.True((bool)typeof(WavefrontSurfaceControl).GetMethod("TryBuildGrid", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(control, arguments)!);
        var grid = arguments[0]!;
        return (T)grid.GetType().GetProperty(property)!.GetValue(grid)!;
    }

    private sealed class CapturedTrace : TraceListener
    {
        internal ConcurrentQueue<string> Lines { get; } = new();
        public override void Write(string? message) { if (message is not null) Lines.Enqueue(message); }
        public override void WriteLine(string? message) => Write(message);
    }
}

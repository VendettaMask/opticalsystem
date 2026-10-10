using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Panels;
using OptilandWorkbench.App.Theming;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;

namespace OptilandWorkbench.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class PupilShapeAndFoucaultPanelTests
{
    public static AppBuilder BuildAvaloniaApp()
    {
        var capture = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPTICAL_FOUCAULT_CAPTURE_DIR"));
        var builder = AppBuilder.Configure<global::OptilandWorkbench.App.App>();
        if (capture) builder.UseSkia();
        return builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = !capture });
    }

    [Theory]
    [InlineData("伪彩色")]
    [InlineData("等高线")]
    public async Task TypedExitPupilProjectionReachesPlanarViewportWithoutChangingData(string display)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(PupilShapeAndFoucaultPanelTests));
        await session.Dispatch(() =>
        {
            var runtime = new WorkbenchRuntime(Optic.CreateCookeTriplet());
            var circular = WavefrontView(runtime, false, display);
            var projected = WavefrontView(runtime, true, display);
            Assert.Equal(circular.Series.Single().Points, projected.Series.Single().Points);
            var control = Build<WavefrontSurfaceControl>("BuildWavefrontMapPlot", projected);
            control.Measure(new Size(600, 420)); control.Arrange(new Rect(0, 0, 600, 420));
            Assert.Equal(projected.PlotOptions.SpatialDisplayScaleX, control.SpatialDisplayScaleX);
            Assert.Equal(projected.PlotOptions.SpatialDisplayScaleY, control.SpatialDisplayScaleY);
            Assert.Equal(control.SpatialDisplayScaleX / control.SpatialDisplayScaleY,
                control.PlanarPlotBounds.Width / control.PlanarPlotBounds.Height, 12);
            Assert.True(control.SpatialDisplayScaleX != 1 || control.SpatialDisplayScaleY != 1);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task FoucaultDisplayMappingPreservesIntensityAndUsesBrightWhiteDarkBlack()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(PupilShapeAndFoucaultPanelTests));
        await session.Dispatch(() =>
        {
            var runtime = new WorkbenchRuntime(Optic.CreateCookeTriplet());
            var linear = Build<FoucaultPlotControl>("BuildFoucaultPlot", FoucaultView(runtime, "线性"));
            var log = Build<FoucaultPlotControl>("BuildFoucaultPlot", FoucaultView(runtime, "对数"));
            Assert.False(linear.Logarithmic); Assert.True(log.Logarithmic);
            var renamed = FoucaultView(runtime, "对数") with { Rows = Array.Empty<AnalysisRowDto>() };
            Assert.True(Build<FoucaultPlotControl>("BuildFoucaultPlot", renamed).Logarithmic);
            Assert.Equal(linear.Series!.Points, log.Series!.Points);
            // Use an explicit unit-scale display fixture so the dynamic legend cannot affect this contract.
            var unitLinear = new FoucaultPlotControl(); var unitLog = new FoucaultPlotControl { Logarithmic = true };
            Assert.Equal(.01, unitLinear.DisplayIntensityFraction(.01), 12);
            Assert.Equal(2d / 3, unitLog.DisplayIntensityFraction(.01), 12);
            Assert.Equal(0, unitLog.DisplayIntensityFraction(0));
            var color = typeof(FoucaultPlotControl).GetMethod("DisplayColor", BindingFlags.NonPublic | BindingFlags.Instance)!;
            Assert.Equal(Colors.Black, color.Invoke(unitLinear, new object[] { 0d }));
            Assert.Equal(Colors.White, color.Invoke(unitLinear, new object[] { 1d }));
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RealLensPhysicalPlotsRenderTogether()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(PupilShapeAndFoucaultPanelTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(global::Avalonia.Application.Current!, "Light");
            var runtime = new WorkbenchRuntime(Optic.CreateCookeTriplet());
            var grid = new Grid { ColumnDefinitions = new("*,*"), RowDefinitions = new("*,*") };
            Add("Entrance pupil coordinates", Build<WavefrontSurfaceControl>("BuildWavefrontMapPlot", WavefrontView(runtime, false, "伪彩色")), 0, 0);
            Add("Exit pupil F-number projection", Build<WavefrontSurfaceControl>("BuildWavefrontMapPlot", WavefrontView(runtime, true, "伪彩色")), 0, 1);
            Add("Physical knife: linear intensity", Build<FoucaultPlotControl>("BuildFoucaultPlot", FoucaultView(runtime, "线性")), 1, 0);
            Add("Physical knife: logarithmic display", Build<FoucaultPlotControl>("BuildFoucaultPlot", FoucaultView(runtime, "对数")), 1, 1);
            var window = new Window { Content = grid, Width = 1240, Height = 940 };
            try
            {
                window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Assert.Equal(4, grid.Children.Count);
                var directory = Environment.GetEnvironmentVariable("OPTICAL_FOUCAULT_CAPTURE_DIR");
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                    using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
                    frame.Save(Path.Combine(directory, "pupil-shape-foucault.png"), PngBitmapEncoderOptions.Default);
                }
            }
            finally { window.Close(); }

            void Add(string title, Control plot, int row, int column)
            {
                var container = new DockPanel { Margin = new Thickness(8) };
                var label = new TextBlock { Text = title, FontSize = 16, Margin = new Thickness(12, 4) };
                DockPanel.SetDock(label, Avalonia.Controls.Dock.Top); container.Children.Add(label); container.Children.Add(plot);
                Grid.SetRow(container, row); Grid.SetColumn(container, column); grid.Children.Add(container);
            }
        }, CancellationToken.None);
    }

    private static AnalysisViewDto WavefrontView(WorkbenchRuntime runtime, bool shape, string display) =>
        WorkbenchMapper.ToAnalysisViewDto(runtime.BuildAnalysisView("波前图", new Dictionary<string, string>
        { ["Sampling"] = "32", ["FieldNumber"] = "3", ["UseExitPupilShape"] = shape.ToString(), ["DisplayAs"] = display }));

    private static AnalysisViewDto FoucaultView(WorkbenchRuntime runtime, string type) =>
        WorkbenchMapper.ToAnalysisViewDto(runtime.BuildAnalysisView("Foucault Analysis", new Dictionary<string, string>
        { ["Sampling"] = "32", ["Type"] = type, ["KnifeEdge"] = "垂直线右", ["YPositionMicrometers"] = "0" }));

    private static T Build<T>(string method, AnalysisViewDto view) where T : Control =>
        Assert.IsType<T>(typeof(AnalysisPanel).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { view }));
}

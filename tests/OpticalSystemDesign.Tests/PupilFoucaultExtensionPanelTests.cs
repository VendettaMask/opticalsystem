using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core.Coordinates;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class PupilFoucaultExtensionPanelTests
{
    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<global::OpticalSystemDesign.App.App>();
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPTICAL_PUPIL_EXTENSION_CAPTURE_DIR"))) builder.UseSkia();
        return builder.UseHeadless(new AvaloniaHeadlessPlatformOptions
        { UseHeadlessDrawing = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPTICAL_PUPIL_EXTENSION_CAPTURE_DIR")) });
    }

    [Theory]
    [InlineData("表面")]
    [InlineData("等高线")]
    public async Task TypedThreeDimensionalGeometryReachesTheRealControlAndKeepsRawCoordinates(string display)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(PupilFoucaultExtensionPanelTests));
        await session.Dispatch(() =>
        {
            var optic = PupilFoucaultExtensionTests.LensWithCurvedStop();
            var runtime = new WorkbenchRuntime(optic);
            var plain = Wavefront(runtime, "F 数近似", display);
            var projected = Wavefront(runtime, "三维光阑像", display);
            Assert.Equal(plain.Series.Single().Points, projected.Series.Single().Points);
            var geometry = Assert.IsAssignableFrom<IReadOnlyList<AnalysisSpatialPointDto>>(projected.PlotOptions.SpatialGeometry);
            var control = Build<WavefrontSurfaceControl>("BuildWavefrontMapPlot", projected);
            Assert.Same(projected.PlotOptions.SpatialGeometry, control.SpatialGeometry);
            foreach (var point in geometry) Assert.Equal(point, control.SpatialPoint(point.SourceX, point.SourceY));
            control.Measure(new Size(600, 440)); control.Arrange(new Rect(0, 0, 600, 440));
            Assert.True(control.HasRenderableGrid);
            var settings = runtime.GetAnalysisParameters("波前图");
            Assert.Contains(settings, p => p.Key == "ExitPupilGeometry");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task PhysicalKnifeCoordinatesReachTheTypedApplicationBoundaryAndLogDisplayKeepsIntensity()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(PupilFoucaultExtensionPanelTests));
        await session.Dispatch(() =>
        {
            var runtime = new WorkbenchRuntime(PupilFoucaultExtensionTests.CollimatedPlate());
            var linear = Foucault(runtime, "物理平面矢量角谱", "线性");
            var logarithmic = Foucault(runtime, "物理平面矢量角谱", "对数");
            Assert.Equal(AnalysisAxisUnit.Micrometer, linear.Series.Single().XUnit);
            Assert.Equal(linear.Series.Single().Points, logarithmic.Series.Single().Points);
            Assert.True(Build<FoucaultPlotControl>("BuildFoucaultPlot", logarithmic).Logarithmic);
            Assert.Contains(runtime.GetAnalysisParameters("Foucault Analysis"), p => p.Key == "FoucaultPropagation");
            Assert.Contains(runtime.GetAnalysisParameters("Foucault Analysis"), p => p.Key == "KnifeDistanceMillimeters");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ExtendedPhysicalPlotsRenderWithRealGeometryAndPhysicalPlaneAxes()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(PupilFoucaultExtensionPanelTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(global::Avalonia.Application.Current!, "Light");
            var optic = PupilFoucaultExtensionTests.LensWithCurvedStop();
            var detector = optic.SurfaceGroup.Items[^1];
            detector.CoordinateSystem = new CoordinateSystem(detector.CoordinateSystem.Origin, 30, 0, 0);
            var geometricRuntime = new WorkbenchRuntime(optic);
            var physicalRuntime = new WorkbenchRuntime(PupilFoucaultExtensionTests.CollimatedPlate());
            var grid = new Grid { ColumnDefinitions = new("*,*"), RowDefinitions = new("*,*") };
            Add("3D geometric stop image, OPD color", Build<WavefrontSurfaceControl>("BuildWavefrontMapPlot", Wavefront(geometricRuntime, "三维光阑像", "表面")), 0, 0);
            Add("Projected geometric pupil contours", Build<WavefrontSurfaceControl>("BuildWavefrontMapPlot", Wavefront(geometricRuntime, "三维光阑像", "等高线")), 0, 1);
            Add("Afocal physical knife, scalar propagation", Build<FoucaultPlotControl>("BuildFoucaultPlot", Foucault(physicalRuntime, "物理平面标量角谱", "线性")), 1, 0);
            Add("Afocal physical knife, Maxwell vector propagation", Build<FoucaultPlotControl>("BuildFoucaultPlot", Foucault(physicalRuntime, "物理平面矢量角谱", "线性")), 1, 1);
            var window = new Window { Content = grid, Width = 1240, Height = 960 };
            try
            {
                window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Assert.Equal(4, grid.Children.Count);
                var directory = Environment.GetEnvironmentVariable("OPTICAL_PUPIL_EXTENSION_CAPTURE_DIR");
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory); using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
                    frame.Save(Path.Combine(directory, "pupil-foucault-extension.png"), PngBitmapEncoderOptions.Default);
                }
            }
            finally { window.Close(); }
            void Add(string title, Control plot, int row, int column)
            {
                var panel = new DockPanel { Margin = new Thickness(8) };
                var label = new TextBlock { Text = title, FontSize = 16, Margin = new Thickness(10, 4) };
                DockPanel.SetDock(label, Avalonia.Controls.Dock.Top); panel.Children.Add(label); panel.Children.Add(plot);
                Grid.SetRow(panel, row); Grid.SetColumn(panel, column); grid.Children.Add(panel);
            }
        }, CancellationToken.None);
    }
    private static AnalysisViewDto Wavefront(WorkbenchRuntime runtime, string geometry, string display) =>
        WorkbenchMapper.ToAnalysisViewDto(runtime.BuildAnalysisView("波前图", new Dictionary<string, string>
        { ["Sampling"] = "16", ["FieldNumber"] = "1", ["ExitPupilGeometry"] = geometry, ["DisplayAs"] = display }));
    private static AnalysisViewDto Foucault(WorkbenchRuntime runtime, string propagation, string type) =>
        WorkbenchMapper.ToAnalysisViewDto(runtime.BuildAnalysisView("Foucault Analysis", new Dictionary<string, string>
        {
            ["Sampling"] = "16",
            ["FoucaultPropagation"] = propagation,
            ["Type"] = type,
            ["UsePolarization"] = (propagation == "物理平面矢量角谱").ToString(),
            ["KnifeDistanceMillimeters"] = "1",
            ["ObservationDistanceMillimeters"] = "2"
        }));
    private static T Build<T>(string method, AnalysisViewDto view) where T : Control =>
        Assert.IsType<T>(typeof(AnalysisPanel).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { view }));
}

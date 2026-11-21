using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Reflection;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.App.ViewModels;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class SurfaceDrawingPanelTests
{
    public static AppBuilder BuildAvaloniaApp() => DiagramViewportFitTests.BuildAvaloniaApp();

    [Theory]
    [InlineData("Light", 1100)] [InlineData("Light", 620)] [InlineData("Dark", 1100)]
    [InlineData("Lumen", 1100)] [InlineData("Isekai", 1100)] [InlineData("Pixel", 1100)]
    public async Task RealDrawingControlsApplyAndRevertWithoutLosingTheSelectedSurface(string theme, int width)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(SurfaceDrawingPanelTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            DisplayTypography.Configure(new() { Theme = theme });
            using var app = WorkbenchApplication.Create("cooke");
            var service = DispatchProxy.Create<IPrescriptionService, DrawingPrescriptionProxy>();
            var calls = (DrawingPrescriptionProxy)service;
            calls.Inner = app.Prescription;
            using var panel = new LensEditorPanel(service, app.Events, new SurfaceSelectionService());
            var window = new Window { Width = width, Height = 850, Content = panel };
            try
            {
                window.Show(); Tick(window); UiLocalization.Attach(window);
                T Named<T>(string name) where T : Control => panel.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
                var grid = panel.GetVisualDescendants().OfType<DataGrid>().Single();
                grid.SelectedItem = grid.ItemsSource!.Cast<SurfaceEditorRow>().Single(row => row.Number == 1);
                Named<Button>("SurfacePropertiesToggle").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Tick(window);
                var opacity = Named<ComboBox>("SurfaceOpacity");
                Assert.True(opacity.IsEnabled);
                opacity.SelectedItem = opacity.Items.Cast<ComboBoxItem>().Single(item => Equals(item.Tag, 0.5));
                Named<Button>("ApplySurfaceProperties").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Tick(window);
                Assert.Equal(1, calls.DrawingUpdates);
                Assert.Equal(0, calls.PhysicalUpdates);
                var navigation = Named<ListBox>("SurfacePropertyNavigation");
                navigation.SelectedItem = navigation.Items.Cast<ListBoxItem>().Single(item => item.Tag!.ToString() == "Drawing"); Tick(window);
                foreach (var name in new[] { "HideSurfaceRays", "SkipSurfaceRays", "HideSurface", "HideSurfaceEdges", "DrawSurfaceLocalAxes", "DrawSurfaceLocalOrigin" })
                {
                    var flag = Named<CheckBox>(name); Assert.True(flag.IsEnabled); flag.IsChecked = true;
                    flag.BringIntoView(); Tick(window);
                    var bounds = new Rect(flag.Bounds.Size).TransformToAABB(flag.TransformToVisual(window)!.Value);
                    Assert.True(bounds.Width >= flag.DesiredSize.Width - 1 && bounds.Height >= 32
                        && bounds.Left >= 0 && bounds.Right <= window.ClientSize.Width + 1,
                        $"{name}: bounds={bounds}; desired={flag.DesiredSize}; window={window.ClientSize}");
                }
                var edge = Named<ComboBox>("SurfaceEdgeMode");
                edge.SelectedItem = edge.Items.Cast<ComboBoxItem>().Single(item => Equals(item.Tag, SurfaceEdgeModeDto.DirectToNext));
                var precision = Named<ComboBox>("SurfaceDrawingPrecision");
                precision.SelectedItem = precision.Items.Cast<ComboBoxItem>().Single(item => Equals(item.Tag, SurfaceDrawingPrecisionDto.High));
                var scroll = Named<ScrollViewer>("SurfacePropertyScroll");
                foreach (var picker in new[] { edge, precision })
                {
                    picker.BringIntoView(); Tick(window);
                    var bounds = new Rect(picker.Bounds.Size).TransformToAABB(picker.TransformToVisual(window)!.Value);
                    var viewport = new Rect(scroll.Bounds.Size).TransformToAABB(scroll.TransformToVisual(window)!.Value);
                    Assert.True(bounds.Width > 100 && bounds.Left >= viewport.Left && bounds.Right <= viewport.Right + 1
                        && bounds.Top >= viewport.Top && bounds.Bottom <= viewport.Bottom + 1,
                        $"{picker.Name}: bounds={bounds}; viewport={viewport}");
                }
                Save(window, $"drawing-settings-{theme}-{width}.png");
                Named<Button>("ApplySurfaceProperties").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Tick(window);
                var applied = app.Prescription.GetSurfaces()[1].Drawing!;
                Assert.Equal(new SurfaceDrawingOptionsDto(0.5, true, true, true, true, true, true,
                    SurfaceEdgeModeDto.DirectToNext, SurfaceDrawingPrecisionDto.High), applied);
                Assert.Equal(1, ((SurfaceEditorRow)grid.SelectedItem!).Number);
                Assert.Equal(2, calls.DrawingUpdates);
                Assert.Equal(0, calls.PhysicalUpdates);
                Named<CheckBox>("HideSurface").IsChecked = false;
                Named<Button>("RevertSurfaceProperties").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Tick(window);
                Assert.True(Named<CheckBox>("HideSurface").IsChecked);
                Save(window, $"drawing-{theme}-{width}.png");
                app.Documents.Undo(); app.Documents.Undo(); Tick(window);
                Assert.False(Named<CheckBox>("HideSurface").IsChecked);
                Assert.Equal(new SurfaceDrawingOptionsDto(), app.Prescription.GetSurfaces()[1].Drawing);
            }
            finally { window.Close(); UiLocalization.Apply("zh-CN"); }
        }, CancellationToken.None);
    }

    public class DrawingPrescriptionProxy : DispatchProxy
    {
        public IPrescriptionService Inner { get; set; } = null!;
        public int DrawingUpdates { get; private set; }
        public int PhysicalUpdates { get; private set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod!.Name == nameof(IPrescriptionService.UpdateSurfaceDrawingSettings)) DrawingUpdates++;
            if (targetMethod.Name == nameof(IPrescriptionService.UpdateSurfaceComponents)) PhysicalUpdates++;
            return targetMethod.Invoke(Inner, args);
        }
    }

    private static void Tick(Window window)
    { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    private static void Save(Window window, string name)
    {
        var path = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SURFACE_DRAWING_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(path)) return;
        Directory.CreateDirectory(path);
        using var image = window.CaptureRenderedFrame(); image?.Save(Path.Combine(path, name), PngBitmapEncoderOptions.Default);
    }
}

using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class AnalysisAutomaticAspectCoverageTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public static AppBuilder BuildAvaloniaApp() => DiagramViewportFitTests.BuildAvaloniaApp();

    public static IEnumerable<object[]> Drawings()
    {
        foreach (var kind in Enum.GetValues<AnalysisPresentationKind>().Where(kind => kind is not
            (AnalysisPresentationKind.SeidelCoefficients or AnalysisPresentationKind.ZernikeFringe
            or AnalysisPresentationKind.ZernikeStandard or AnalysisPresentationKind.ZernikeAnnular or AnalysisPresentationKind.TextReport)))
            yield return [kind, "Lumen", "default"];
        foreach (var kind in new[] { AnalysisPresentationKind.SpotDiagram, AnalysisPresentationKind.ConfigurationMatrixSpot,
            AnalysisPresentationKind.WavefrontMap, AnalysisPresentationKind.Mtf })
            yield return [kind, "Isekai", "default"];
        yield return [AnalysisPresentationKind.FootprintDiagram, "Lumen", "equal-aspect"];
        yield return [AnalysisPresentationKind.WavefrontMap, "Lumen", "伪彩色"];
        yield return [AnalysisPresentationKind.WavefrontMap, "Lumen", "等高线"];
        yield return [AnalysisPresentationKind.FftPsf, "Lumen", "表面"];
        yield return [AnalysisPresentationKind.HuygensPsf, "Lumen", "表面"];
    }

    [Theory]
    [MemberData(nameof(Drawings))]
    public async Task EveryDrawingKeepsItsDataAxesAndInteractionThroughAutomaticAndManualSwitching(
        AnalysisPresentationKind kind, string theme, string variant)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(AnalysisAutomaticAspectCoverageTests));
        await session.Dispatch(async () =>
        {
            var settings = new AppSettings(); ImageExportResolution.Configure(settings); DisplayTypography.Configure(settings);
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            using var app = WorkbenchApplication.Create("cooke");
            var view = (AnalysisViewDto)typeof(AnalysisResponsivePlotTests).GetMethod("View", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [kind])!;
            if (variant == "equal-aspect") view = view with { PlotOptions = view.PlotOptions with { EqualAspect = true } };
            else if (variant != "default") view = view with { Rows = view.Rows.Concat([new AnalysisRowDto("显示为", variant)]).ToArray() };
            var data = JsonSerializer.Serialize(view); var source = app.Documents.GetRecoveryState();
            var scene = kind == AnalysisPresentationKind.CardinalPoints
                ? await app.Visualization.BuildSceneAsync(SceneDimension.TwoDimensional) : null;
            using var panel = new AnalysisPanel(app.Analyses, app.Visualization, app.Documents, app.Events, settings, "MTF");
            typeof(AnalysisPanel).GetField("_initialRunRequested", Private)!.SetValue(panel, true);
            panel.PresentResult(view, app.Documents.GetSnapshot(), new(2026, 11, 4, 20, 30, 0, TimeSpan.FromHours(8)), scene);
            var window = new Window { Width = 1000, Height = 1100, Content = panel };
            try
            {
                window.Show(); Tick(window);
                var host = Assert.Single(panel.GetVisualDescendants().OfType<ResponsiveAnalysisPlotHost>());
                var sheet = Assert.IsType<UniformDiagramPage>(host.Child);
                var controls = sheet.GetVisualDescendants().Where(c => c is AnalysisPlotControl or WavefrontSurfaceControl
                    or FoucaultPlotControl or FullFieldAberrationControl or SeidelDiagramControl or OpticSceneControl).OfType<Control>().ToArray();
                Assert.NotEmpty(controls);
                var properties = controls.Select(ScientificData).ToArray();
                var initialAxes = controls.OfType<AnalysisPlotControl>().Select(Axes).ToArray();
                foreach (var width in new[] { 1900, 1000, 1900, 1000 })
                {
                    window.Width = width; Tick(window);
                    Assert.Equal(width == 1900 ? 1600 : 1200, sheet.Width);
                    AssertStable();
                    AssertAxes(initialAxes);
                    if (width == 1000 || variant != "default") continue;
                    using var image = panel.CapturePlotImage(1);
                    Save(image, $"{kind}-{theme}-{variant}-wide.png");
                }
                foreach (var control in controls.Where(c => c is AnalysisPlotControl or WavefrontSurfaceControl))
                {
                    Key(control, Avalonia.Input.Key.Add); Key(control, Avalonia.Input.Key.Right);
                    if (control is WavefrontSurfaceControl) Key(control, Avalonia.Input.Key.Down, KeyModifiers.Shift);
                }
                Tick(window);
                var interactions = controls.Select(Interaction).ToArray();
                var zoomedAxes = controls.OfType<AnalysisPlotControl>().Select(Axes).ToArray();
                foreach (var (mode, width, sourceWidth) in new[]
                {
                    ("Auto", 1900, 1600), ("Auto", 1000, 1200),
                    ("16:9", 1000, 1600), ("16:9", 1900, 1600),
                    ("4:3", 1900, 1200), ("4:3", 1000, 1200), ("Auto", 1900, 1600)
                })
                {
                    settings.DiagramAspectRatio = mode; ImageExportResolution.Configure(settings);
                    window.Width = width; Tick(window);
                    Assert.Equal(sourceWidth, sheet.Width); AssertStable(); AssertAxes(zoomedAxes);
                    Assert.Equal(interactions, controls.Select(Interaction).ToArray());
                }
                var legends = sheet.GetVisualDescendants().OfType<CheckBox>().ToArray();
                foreach (var legend in legends) legend.IsChecked = false;
                Tick(window);
                var hiddenSeries = controls.Select(ScientificData).ToArray();
                window.Width = 1000; Tick(window);
                Assert.Equal(hiddenSeries, controls.Select(ScientificData).ToArray());
                AssertAxes(zoomedAxes);
                foreach (var legend in legends) Assert.False(legend.IsChecked);
                foreach (var legend in legends) legend.IsChecked = true;
                Tick(window);
                Assert.Equal(properties, controls.Select(ScientificData).ToArray());
                using var restored = panel.CapturePlotImage(1);
                Save(restored, $"{kind}-{theme}-{variant}-narrow-zoomed.png");

                void AssertStable()
                {
                    Assert.Same(sheet, host.Child);
                    Assert.Equal(controls, sheet.GetVisualDescendants().Where(c => c is AnalysisPlotControl or WavefrontSurfaceControl
                        or FoucaultPlotControl or FullFieldAberrationControl or SeidelDiagramControl or OpticSceneControl).OfType<Control>().ToArray());
                    Assert.Equal(properties, controls.Select(ScientificData).ToArray());
                    Assert.Equal(data, JsonSerializer.Serialize(view)); Assert.Equal(source, app.Documents.GetRecoveryState());
                    Assert.False(sheet.HasOverflow, $"{kind}/{variant}: source overflow");
                    Assert.Equal(160, sheet.Caption.Bounds.Height);
                    var bounds = new Rect(sheet.Bounds.Size).TransformToAABB(sheet.TransformToVisual(host)!.Value);
                    Assert.True(bounds.Left >= -1 && bounds.Top >= -1 && bounds.Right <= host.Viewport.Width + 1 && bounds.Bottom <= host.Viewport.Height + 1);
                }
                void AssertAxes(object?[] expected)
                {
                    var actual = controls.OfType<AnalysisPlotControl>().Select(Axes).ToArray();
                    Assert.True(expected.SequenceEqual(actual), $"{kind}/{variant}: axes changed\n{string.Join("; ", expected)}\n{string.Join("; ", actual)}");
                }
            }
            finally { window.Close(); ImageExportResolution.Configure(new()); }
        }, CancellationToken.None);
    }

    private static object? Axes(AnalysisPlotControl control) => typeof(AnalysisPlotControl).GetField("_lastRenderedViewport", Private)!.GetValue(control);

    [Theory]
    [InlineData("Spot Diagram")]
    [InlineData("Ray Fan")]
    [InlineData("Field Curvature and Distortion")]
    [InlineData("Wavefront Map")]
    [InlineData("Through Focus")]
    [InlineData("Incoherent Irradiance")]
    [InlineData("Footprint Diagram")]
    [InlineData("MTF")]
    [InlineData("Huygens MTF")]
    [InlineData("Geometric MTF")]
    [InlineData("Sampled MTF")]
    [InlineData("Fourier Through Focus MTF")]
    [InlineData("Huygens Through Focus MTF")]
    [InlineData("Geometric Through Focus MTF")]
    [InlineData("Fourier MTF vs Field")]
    [InlineData("Huygens MTF vs Field")]
    [InlineData("Geometric MTF vs Field")]
    public async Task RealAnalysisResultIsReusedWithoutChangingItsAxesAndZoom(string analysis)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(AnalysisAutomaticAspectCoverageTests));
        await session.Dispatch(async () =>
        {
            var settings = new AppSettings(); ImageExportResolution.Configure(settings); DisplayTypography.Configure(settings);
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Lumen");
            using var app = WorkbenchApplication.Create("cooke");
            if (analysis is "Through Focus" or "Incoherent Irradiance")
                await app.Documents.OpenAsync(SequentialExampleCatalog.Examples.Single(e =>
                    e.RelativePath == "Objectives/Double Gauss 28 degree field.zmx").FilePath);
            var source = app.Documents.GetRecoveryState();
            var sampling = analysis == "MTF" ? "256" : "64";
            var requested = new Dictionary<string, string>
            {
                ["Sampling"] = sampling, ["PupilSampling"] = sampling, ["ImageSampling"] = "32", ["NumRays"] = "32",
                ["PlotPointCount"] = "16", ["WavelengthNumber"] = "1", ["NumberOfSteps"] = "3",
                ["Steps"] = "3", ["FieldDensity"] = "3", ["MaximumFrequency"] = "100", ["Frequency1"] = "10",
                ["Frequency2"] = "20", ["Frequency3"] = "30", ["Frequency4"] = "0", ["Frequency5"] = "0", ["Frequency6"] = "0"
            };
            // Use each analysis's declared UI choices/ranges; these small inputs
            // exercise presentation and are not optical-precision acceptance settings.
            var parameters = new Dictionary<string, string>();
            foreach (var descriptor in app.Analyses.GetParameters(analysis))
            {
                if (!requested.TryGetValue(descriptor.Key, out var value)) continue;
                if (descriptor.Kind is AnalysisParameterKind.Integer or AnalysisParameterKind.Double)
                    parameters[descriptor.Key] = Math.Clamp(double.Parse(value, CultureInfo.InvariantCulture), descriptor.Minimum, descriptor.Maximum)
                        .ToString(CultureInfo.InvariantCulture);
                else if (descriptor.Kind == AnalysisParameterKind.Choice)
                {
                    var choice = descriptor.Choices?.FirstOrDefault(choice => choice == value || choice == $"{value} x {value}");
                    if (choice is not null) parameters[descriptor.Key] = choice;
                }
            }
            var result = await app.Analyses.RunAsync(new(Guid.NewGuid(), 1, analysis, parameters));
            Assert.Equal(AnalysisOutcome.Success, result.View.Outcome);
            var data = JsonSerializer.Serialize(result.View);
            using var panel = new AnalysisPanel(app.Analyses, app.Visualization, app.Documents, app.Events, settings, analysis);
            typeof(AnalysisPanel).GetField("_initialRunRequested", Private)!.SetValue(panel, true);
            panel.PresentResult(result.View, app.Documents.GetSnapshot(), new(2026, 11, 4, 20, 30, 0, TimeSpan.FromHours(8)));
            var window = new Window { Width = 1000, Height = 1100, Content = panel };
            try
            {
                window.Show(); Tick(window);
                var host = Assert.Single(panel.GetVisualDescendants().OfType<ResponsiveAnalysisPlotHost>());
                var sheet = Assert.IsType<UniformDiagramPage>(host.Child);
                var plots = sheet.GetVisualDescendants().OfType<AnalysisPlotControl>().ToArray();
                var surfaces = sheet.GetVisualDescendants().OfType<WavefrontSurfaceControl>().ToArray();
                Assert.NotEmpty(plots.Cast<Control>().Concat(surfaces));
                var originalData = plots.Cast<Control>().Concat(surfaces).Select(ScientificData).ToArray();
                var axes = plots.Select(Axes).ToArray();
                foreach (var width in new[] { 1900, 1000 })
                {
                    window.Width = width; Tick(window);
                    Assert.Equal(width == 1900 ? 1600 : 1200, sheet.Width);
                    Assert.Equal(axes, plots.Select(Axes).ToArray());
                    Assert.False(sheet.HasOverflow, analysis);
                    using var image = panel.CapturePlotImage(1);
                    Save(image, $"real-{analysis.Replace(' ', '-')}-{(width == 1900 ? "wide" : "narrow")}.png");
                }
                foreach (var control in plots.Cast<Control>().Concat(surfaces)) { Key(control, Avalonia.Input.Key.Add); Key(control, Avalonia.Input.Key.Right); }
                Tick(window); var state = plots.Cast<Control>().Concat(surfaces).Select(Interaction).ToArray();
                var zoomedAxes = plots.Select(Axes).ToArray();
                foreach (var mode in new[] { "Auto", "16:9", "4:3", "Auto" })
                {
                    settings.DiagramAspectRatio = mode; ImageExportResolution.Configure(settings);
                    foreach (var width in new[] { 1900, 1000 })
                    {
                        window.Width = width; Tick(window);
                        Assert.Equal(mode == "16:9" || mode == "Auto" && width == 1900 ? 1600 : 1200, sheet.Width);
                        Assert.Equal(zoomedAxes, plots.Select(Axes).ToArray());
                        Assert.Equal(state, plots.Cast<Control>().Concat(surfaces).Select(Interaction).ToArray());
                        Assert.Equal(originalData, plots.Cast<Control>().Concat(surfaces).Select(ScientificData).ToArray());
                        Assert.Equal(data, JsonSerializer.Serialize(result.View)); Assert.Equal(source, app.Documents.GetRecoveryState());
                        Assert.Equal(plots, sheet.GetVisualDescendants().OfType<AnalysisPlotControl>().ToArray());
                    }
                }
            }
            finally { window.Close(); ImageExportResolution.Configure(new()); }
        }, CancellationToken.None);
    }

    private static string ScientificData(Control control) => control switch
    {
        AnalysisPlotControl plot => JsonSerializer.Serialize(new { plot.Series, plot.PlotOptions }),
        WavefrontSurfaceControl surface => JsonSerializer.Serialize(new { surface.Series, surface.SpatialGeometry, surface.SpatialDisplayScales,
            surface.ValueMinimum, surface.ValueMaximum, surface.ContourLevels }),
        FoucaultPlotControl plot => JsonSerializer.Serialize(plot.Series),
        FullFieldAberrationControl plot => JsonSerializer.Serialize(new { plot.Series, plot.XFieldWidth, plot.YFieldWidth, plot.FieldCenterX, plot.FieldCenterY }),
        SeidelDiagramControl plot => JsonSerializer.Serialize(new { plot.Table, plot.MaximumAberration, plot.GridInterval }),
        OpticSceneControl scene => JsonSerializer.Serialize(scene.Scene),
        _ => throw new InvalidOperationException(control.GetType().Name)
    };
    private static string Interaction(Control control) => control switch
    {
        AnalysisPlotControl plot => typeof(AnalysisPlotControl).GetField("_viewport", Private)!.GetValue(plot)?.ToString() ?? "default",
        WavefrontSurfaceControl surface => $"{surface.ViewYawDegrees}|{surface.ViewPitchDegrees}|{surface.ViewZoom}|{typeof(WavefrontSurfaceControl).GetField("_pan", Private)!.GetValue(surface)}",
        OpticSceneControl scene => JsonSerializer.Serialize(scene.OrientationAxesForRendering()),
        _ => "read-only"
    };
    private static void Key(Control control, Key key, KeyModifiers modifiers = KeyModifiers.None) =>
        control.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers });
    private static void Tick(Window window)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
    }
    private static void Save(Bitmap bitmap, string name)
    {
        var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_ALL_ASPECT_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory); bitmap.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }
}

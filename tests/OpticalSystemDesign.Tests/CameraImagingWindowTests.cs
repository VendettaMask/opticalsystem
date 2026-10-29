using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Laboratories;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class CameraImagingWindowTests
{
    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public async Task ExperimentOpenEditedReplayAndCalibrationPreserveAAndRejectInvalidImports(string theme)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(CameraImagingWindowTests));
        await session.Dispatch<bool>(async () =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            using var app = WorkbenchApplication.Create();
            var a = await app.CameraImaging.SimulateAsync(new(Width: 8, Height: 8, Optics: CameraOptics.Ideal));
            var b = await app.CameraImaging.SimulateAsync(a.Request with { Isp = new(Gamma: 1.8) });
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".camera-experiment.zip");
            var calibrationPath = path + ".json";
            var window = new CameraImagingWindow(app);
            try
            {
                await app.CameraImaging.SaveExperimentAsync(new(CameraExperiment.FormatIdentifier, 1, b, a), path);
                window.Show(); Assert.True(await window.OpenExperimentFromPathAsync(path));
                Assert.Equal(b.RawDn, window.Result!.RawDn); Assert.Equal(a.RawDn, window.Comparison!.RawDn);
                app.Documents.NewBlank(); Assert.NotNull(window.Result); // Explicit saved-input mode survives unrelated project changes.
                var controls = window.GetVisualDescendants().OfType<Control>().ToArray();
                controls.OfType<NumericUpDown>().Single(c => c.Name == "camera-exposure").Value = 20;
                Assert.Null(window.Result); Assert.NotNull(window.Comparison);
                await window.RunAsync();
                Assert.Equal(b.MeanPhotoElectrons * 2, window.Result!.MeanPhotoElectrons, 8);
                var edited = window.Result;
                await window.RecomputeExperimentAsync(); Assert.Equal(edited.RawDn, window.Result!.RawDn);
                var beforeInvalid = window.Result;
                await File.WriteAllTextAsync(calibrationPath, "{}");
                Assert.False(await window.OpenExperimentFromPathAsync(calibrationPath)); Assert.Same(beforeInvalid, window.Result);
                Assert.False(await window.CalibrateSensorFromPathAsync(calibrationPath)); Assert.Same(beforeInvalid, window.Result);
                await File.WriteAllTextAsync(calibrationPath, System.Text.Json.JsonSerializer.Serialize(CameraCalibrationTests.Capture(), CameraCalibrationTests.Json));
                Assert.True(await window.CalibrateSensorFromPathAsync(calibrationPath));
                Assert.Null(window.Result); Assert.NotNull(window.Comparison);
                await window.RunAsync(); Assert.NotNull(window.Result!.Request.SensorIdentity?.Calibration);
                Capture(window, theme + "-experiment-calibration");
                window.Width = 760; window.Height = 540; Capture(window, theme + "-experiment-narrow");
                Assert.True(controls.OfType<Button>().Single(c => c.Name == "camera-save-experiment").IsEnabled);
                return true;
            }
            finally { window.Close(); File.Delete(path); File.Delete(calibrationPath); }
        }, CancellationToken.None);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        var capture = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPTICAL_CAMERA_CAPTURE_DIR"));
        var builder = AppBuilder.Configure<global::OpticalSystemDesign.App.App>();
        if (capture) builder.UseSkia();
        return builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = !capture }).UseHarfBuzz();
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public async Task StageAndABControlsAreFunctionalAndClearResultsOnDocumentChange(string theme)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(CameraImagingWindowTests));
        await session.Dispatch<bool>(async () =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            using var app = WorkbenchApplication.Create();
            var window = new CameraImagingWindow(app);
            try
            {
                window.Show(); await window.RunAsync();
                var a = Assert.IsType<CameraImagingResult>(window.Result);
                Assert.Equal(CameraOptics.Ideal, a.Request.Optics);
                var controls = window.GetVisualDescendants().OfType<Control>().ToArray();
                var run = controls.OfType<Button>().Single(x => x.Name == "camera-run");
                Assert.Contains("accent", run.Classes);
                Assert.True(controls.OfType<Button>().Single(x => x.Name == "camera-export").IsEnabled);
                window.PinComparison(); Assert.Same(a, window.Comparison);
                var exposure = controls.OfType<NumericUpDown>().Single(x => x.Name == "camera-exposure");
                exposure.Value = 20; Assert.Null(window.Result); Assert.Same(a, window.Comparison);
                await window.RunAsync();
                Assert.Equal(a.MeanPhotoElectrons * 2, window.Result!.MeanPhotoElectrons, 8);
                Capture(window, theme + "-stages");
                var tabs = controls.OfType<TabControl>().Single(); tabs.SelectedIndex = 1;
                Capture(window, theme + "-AB");
                Assert.Contains("平均绝对差", window.GetVisualDescendants().OfType<TextBlock>().Single(x => x.Text?.Contains("A：") == true).Text);
                window.Width = 760; window.Height = 540; Capture(window, theme + "-narrow");
                var bottom = run.TranslatePoint(new Point(0, run.Bounds.Height), window);
                Assert.InRange(bottom!.Value.Y, 1, window.ClientSize.Height);
                app.Documents.NewBlank(); Assert.Null(window.Result); Assert.Null(window.Comparison);
                app.Modes.SwitchTo(OpticalWorkbenchMode.NonSequential);
                Assert.True(run.IsEnabled); // Ideal control remains available.
                controls.OfType<ComboBox>().Single(x => x.Name == "camera-optics").SelectedIndex = 0;
                Assert.False(run.IsEnabled); Assert.NotNull(AutomationProperties.GetHelpText(run));
                return true;
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ClosingOrChangingParametersCancelsPendingWorkWithoutResurrectingResults()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(CameraImagingWindowTests));
        await session.Dispatch<bool>(async () =>
        {
            using var app = WorkbenchApplication.Create();
            var window = new CameraImagingWindow(app); window.Show();
            var pending = window.RunAsync(); window.Close(); await pending;
            Assert.Null(window.Result);
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task LaboratoryRibbonOpensOneOwnedWindow()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(CameraImagingWindowTests));
        await session.Dispatch(() =>
        {
            var previous = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SETTINGS_DIRECTORY");
            var settings = Path.Combine(Path.GetTempPath(), "camera-window-" + Guid.NewGuid().ToString("N"));
            Environment.SetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SETTINGS_DIRECTORY", settings);
            global::OpticalSystemDesign.App.MainWindow? main = null;
            try
            {
                main = new(); main.Show();
                Assert.Empty(main.OwnedWindows.OfType<CameraImagingWindow>());
                var ribbon = main.GetVisualDescendants().OfType<TabControl>().Single(t => t.Classes.Contains("ribbon-tabs"));
                ribbon.SelectedItem = ribbon.Items.Cast<TabItem>().Single(t => AutomationProperties.GetName(t) == "实验室");
                main.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var entry = main.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "ribbon-open-camera-imaging-lab");
                entry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); entry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var lab = Assert.Single(main.OwnedWindows.OfType<CameraImagingWindow>());
                Assert.Same(main, lab.Owner);
                lab.Close();
            }
            finally
            {
                if (main is not null) typeof(global::OpticalSystemDesign.App.MainWindow).GetField("_closeAfterPersistence", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(main, true);
                main?.Close(); Environment.SetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SETTINGS_DIRECTORY", previous);
                if (Directory.Exists(settings)) Directory.Delete(settings, true);
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CurrentLensAtDefaultResolutionUsesSharedOpticalStageAndExplainsSizeLimit()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(CameraImagingWindowTests));
        await session.Dispatch<bool>(async () =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
            using var app = WorkbenchApplication.Create("cooke");
            var before = app.Documents.GetSnapshot(); var window = new CameraImagingWindow(app);
            try
            {
                window.Show(); await window.RunAsync();
                Assert.NotNull(window.Result); Assert.NotEqual("Ideal optical control", window.Result.OpticalModel);
                Assert.Equal(before, app.Documents.GetSnapshot()); Capture(window, "Light-current-lens-stages");
                var controls = window.GetVisualDescendants().OfType<Control>().ToArray();
                controls.OfType<ComboBox>().Single(x => x.Name == "camera-size").SelectedIndex = 2;
                var run = controls.OfType<Button>().Single(x => x.Name == "camera-run");
                Assert.False(run.IsEnabled); Assert.Contains("256", AutomationProperties.GetHelpText(run));
                return true;
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public async Task ImportedProfilesAndResponsePlotsPreserveFrozenABAndRejectBadFiles(string theme)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(CameraImagingWindowTests));
        await session.Dispatch<bool>(async () =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            using var files = new CameraSensorFileTests.TemporaryFiles(); using var app = WorkbenchApplication.Create();
            var window = new CameraImagingWindow(app);
            try
            {
                window.Show(); await window.RunAsync(); window.PinComparison();
                var a = Assert.IsType<CameraImagingResult>(window.Comparison);
                var profile = new CameraSensorProfile(CameraSensorProfile.FormatIdentifier, 1, "Synthetic imported sensor", "Numerical control, not measured",
                    new(ExposureMilliseconds: 20, NoiseEnabled: false, DarkElectronsPerSecond: 0, RedQe: 0, GreenQe: 0, BlueQe: 0,
                        ReadNoiseElectrons: 2.125, Seed: -42));
                var path = files.Path("import.json"); await app.CameraImaging.SaveSensorAsync(profile, path);
                Assert.True(await window.ImportSensorFromPathAsync(path));
                Assert.Null(window.Result); Assert.Same(a, window.Comparison);
                Assert.True(await window.ImportResponseFromPathAsync(Path.Combine(CameraSensorFileTests.Examples, "example-qe.csv")));
                var controls = window.GetVisualDescendants().OfType<Control>().ToArray();
                Assert.Equal(20, controls.OfType<NumericUpDown>().Single(c => c.Name == "camera-exposure").Value);
                Assert.Equal(-42, controls.OfType<NumericUpDown>().Single(c => c.Name == "camera-seed").Value);
                Assert.Equal(2.125m, controls.OfType<NumericUpDown>().Single(c => c.Name == "camera-read").Value);
                var qe = controls.OfType<NumericUpDown>().Single(c => c.Name == "camera-qe-r");
                Assert.False(qe.IsEnabled); Assert.NotNull(AutomationProperties.GetHelpText(qe));
                await window.RunAsync(); var b = Assert.IsType<CameraImagingResult>(window.Result);
                Assert.True(b.MeanPhotoElectrons > 0); // Imported absolute response replaces zero scalar QE.
                Assert.Equal("Synthetic imported sensor", b.Request.SensorIdentity!.Name);
                Assert.Equal(-42, b.Request.Sensor!.Seed); Assert.Equal(2.125, b.Request.Sensor.ReadNoiseElectrons);
                Assert.Equal(10, a.Request.Sensor!.ExposureMilliseconds); Assert.Null(a.Request.SpectralResponse);
                Assert.Equal(4, b.Request.SpectralResponse!.Samples.Count);
                var tabs = controls.OfType<TabControl>().Single(t => t.Name == "CameraResults");
                tabs.SelectedIndex = 2; Capture(window, theme + "-imported-response");
                var plot = window.GetVisualDescendants().OfType<global::OpticalSystemDesign.App.Controls.AnalysisPlotControl>().Single();
                Assert.Equal(3, plot.Series.Count); Assert.All(plot.Series, s =>
                { Assert.Equal(AnalysisAxisQuantity.Wavelength, s.XQuantity); Assert.Equal(AnalysisAxisUnit.Nanometer, s.XUnit); });
                var options = plot.PlotOptions;
                var toggles = window.GetVisualDescendants().OfType<CheckBox>()
                    .Where(c => c.Name?.StartsWith("camera-response-visible-", StringComparison.Ordinal) == true).ToArray();
                Assert.Equal(3, toggles.Length);
                foreach (var toggle in toggles) toggle.IsChecked = false;
                Assert.Empty(plot.Series); Assert.Same(options, plot.PlotOptions); Assert.Same(b, window.Result);
                foreach (var toggle in toggles) toggle.IsChecked = true;
                Assert.Equal(3, plot.Series.Count);
                tabs.SelectedIndex = 1; Capture(window, theme + "-imported-AB");
                var bad = files.Path("bad.csv"); File.WriteAllText(bad, "wavelength_nm,qe_r,qe_g,qe_b\n450,10,0,0\n650,1,0,0");
                Assert.False(await window.ImportResponseFromPathAsync(bad)); Assert.Same(b, window.Result); Assert.Same(a, window.Comparison);
                var badJson = files.Path("bad.json"); File.WriteAllText(badJson, "{}");
                Assert.False(await window.ImportSensorFromPathAsync(badJson)); Assert.Same(b, window.Result);
                window.PinComparison(); window.ClearSpectralResponse();
                Assert.Null(window.Result); Assert.Null(window.SpectralResponse); Assert.Same(b, window.Comparison);
                Assert.Equal(4, window.Comparison!.Request.SpectralResponse!.Samples.Count); Assert.True(qe.IsEnabled);
                app.Documents.NewBlank(); Assert.Null(window.Comparison);
                return true;
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public async Task ContinuousSceneImportsAndModeSwitchKeepLegacyReferenceAndTypedSpectralPlots(string theme)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(CameraImagingWindowTests));
        await session.Dispatch<bool>(async () =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            using var app = WorkbenchApplication.Create(); using var files = new CameraSensorFileTests.TemporaryFiles();
            var window = new CameraImagingWindow(app);
            try
            {
                window.Show(); await window.RunAsync(); window.PinComparison();
                var a = Assert.IsType<CameraImagingResult>(window.Comparison); var originalRaw = a.RawDn.ToArray();
                var controls = window.GetVisualDescendants().OfType<Control>().ToArray();
                var mode = controls.OfType<ComboBox>().Single(c => c.Name == "camera-spectral-model");
                var run = controls.OfType<Button>().Single(c => c.Name == "camera-run");
                mode.SelectedIndex = 1; Assert.False(run.IsEnabled); Assert.NotNull(AutomationProperties.GetHelpText(run));
                Assert.True(await window.ImportResponseFromPathAsync(Path.Combine(CameraSensorFileTests.Examples, "example-qe.csv")));
                Assert.True(await window.ImportSceneSpectrumFromPathAsync(Path.Combine(CameraSensorFileTests.Examples, "illuminant-flat.csv"), true));
                Assert.True(await window.ImportSceneSpectrumFromPathAsync(Path.Combine(CameraSensorFileTests.Examples, "reflectance-red.csv"), false));
                Assert.True(run.IsEnabled);
                var start = controls.OfType<NumericUpDown>().Single(c => c.Name == "camera-spectrum-min");
                start.Value = null; Assert.False(run.IsEnabled); start.Value = 400; Assert.True(run.IsEnabled);
                Assert.False(controls.OfType<NumericUpDown>().Single(c => c.Name == "camera-irradiance").IsEnabled);
                await window.RunAsync(); var b = Assert.IsType<CameraImagingResult>(window.Result);
                Assert.Equal(CameraSpectralModel.Continuous, b.Request.SpectralModel); Assert.Equal(9, b.Integration!.Nodes.Count);
                Assert.Same(a, window.Comparison); Assert.Equal(originalRaw, a.RawDn); Assert.Null(a.Integration);
                var tabs = controls.OfType<TabControl>().Single(); tabs.SelectedIndex = 3;
                window.Height = 1180;
                Capture(window, theme + "-continuous-spectra");
                var plots = window.GetVisualDescendants().OfType<global::OpticalSystemDesign.App.Controls.AnalysisPlotControl>().ToArray();
                Assert.Equal(2, plots.Length);
                Assert.Contains(plots, p => p.Series.Single().YUnit == AnalysisAxisUnit.WattsPerSquareMeterPerNanometer);
                Assert.All(plots, p => Assert.Equal(AnalysisAxisUnit.Nanometer, p.Series.Single().XUnit));
                var options = plots.Select(p => p.PlotOptions).ToArray();
                var visibility = window.GetVisualDescendants().OfType<CheckBox>()
                    .Where(c => c.Name is "camera-illuminant-visible" or "camera-reflectance-visible").ToArray();
                Assert.Equal(2, visibility.Length);
                foreach (var check in visibility) check.IsChecked = false;
                Assert.All(plots, p => Assert.Empty(p.Series));
                Assert.Equal(options, plots.Select(p => p.PlotOptions)); Assert.Same(b, window.Result);
                foreach (var check in visibility) check.IsChecked = true;
                Assert.All(plots, p => Assert.Single(p.Series));
                window.Height = 900;
                tabs.SelectedIndex = 1; Capture(window, theme + "-continuous-AB");
                var invalid = files.Path("invalid-light.csv"); File.WriteAllText(invalid, "wavelength_nm,relative_power\n400,1\n700,1");
                Assert.False(await window.ImportSceneSpectrumFromPathAsync(invalid, true));
                Assert.Same(b, window.Result); Assert.Same(a, window.Comparison);
                window.PinComparison(); mode.SelectedIndex = 0;
                Assert.Same(b, window.Comparison); Assert.True(run.IsEnabled);
                await window.RunAsync(); Assert.Null(window.Result!.Integration); Assert.Same(b, window.Comparison);
                Assert.Equal(originalRaw, a.RawDn);
                app.Documents.NewBlank(); Assert.Null(window.Result); Assert.Null(window.Comparison);
                return true;
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("Light")] [InlineData("Dark")]
    public async Task MaterialScenesPreserveABAndAllowCurveAssignmentWithoutPhotoClassification(string theme)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(CameraImagingWindowTests));
        await session.Dispatch<bool>(async () =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            using var app = WorkbenchApplication.Create(); using var files = new CameraSensorFileTests.TemporaryFiles();
            var window = new CameraImagingWindow(app);
            try
            {
                window.Show(); await window.RunAsync(); window.PinComparison(); var legacy = window.Comparison;
                Assert.True(await window.ImportResponseFromPathAsync(Path.Combine(CameraSensorFileTests.Examples, "example-qe.csv")));
                Assert.True(await window.ImportSceneSpectrumFromPathAsync(Path.Combine(CameraSensorFileTests.Examples, "illuminant-flat.csv"), true));
                Assert.True(await window.ImportMaterialSceneFromPathAsync(CameraMaterialServiceTests.Example));
                var controls = window.GetVisualDescendants().OfType<Control>().ToArray();
                controls.OfType<ComboBox>().Single(c => c.Name == "camera-spectral-model").SelectedIndex = 1;
                var run = controls.OfType<Button>().Single(c => c.Name == "camera-run"); Assert.True(run.IsEnabled);
                await window.RunAsync(); var material = Assert.IsType<CameraImagingResult>(window.Result);
                Assert.Same(legacy, window.Comparison); Assert.Equal(CameraSceneMode.MaterialMap, material.Request.SceneMode);
                Assert.Null(material.Request.Reflectance); Assert.Equal(3, material.Integration!.Materials!.Count);
                var tabs = controls.OfType<TabControl>().Single(); tabs.SelectedIndex = 1; Capture(window, theme + "-materials-AB");
                tabs.SelectedIndex = 3; window.Height = 1440; Capture(window, theme + "-materials-scene");
                Assert.True(window.GetVisualDescendants().OfType<Control>().Single(c => c.Name == "camera-material-preview").IsVisible);
                var plot = window.GetVisualDescendants().OfType<global::OpticalSystemDesign.App.Controls.AnalysisPlotControl>()
                    .Single(p => p.Name == "camera-reflectance-plot");
                Assert.Equal(3, plot.Series.Count); var options = plot.PlotOptions;
                var checks = window.GetVisualDescendants().OfType<CheckBox>().Where(c => c.Name?.StartsWith("camera-material-visible-") == true).ToArray();
                Assert.Equal(3, checks.Length);
                foreach (var check in checks) check.IsChecked = false;
                Assert.Empty(plot.Series); Assert.Same(options, plot.PlotOptions); Assert.Same(material, window.Result);
                foreach (var check in checks) check.IsChecked = true;
                Assert.Equal(3, plot.Series.Count);
                window.Height = 900;
                var settings = controls.OfType<ScrollViewer>().Single(s => s.Name == "CameraSettings");
                settings.Offset = new Vector(0, 1100); Capture(window, theme + "-materials-settings"); settings.Offset = default;
                window.PinComparison();
                Assert.True(await window.ReplaceMaterialReflectanceFromPathAsync(Path.Combine(CameraSensorFileTests.Examples, "reflectance-neutral.csv"), 1));
                Assert.Null(window.Result); Assert.Same(material, window.Comparison);
                Assert.Equal(.7, material.Request.MaterialScene!.Materials.Single(m => m.Id == 1).Reflectance!.Samples[1].Value);
                await window.RunAsync(); var changed = window.Result;
                Assert.True(changed!.Request.MaterialScene!.Modified);
                controls.OfType<ComboBox>().Single(c => c.Name == "camera-material-choice").SelectedIndex = 2;
                Assert.Same(changed, window.Result); // choosing the assignment target is not a physical change
                var invalid = files.Path("invalid.json"); File.WriteAllText(invalid, "{}");
                Assert.False(await window.ImportMaterialSceneFromPathAsync(invalid)); Assert.Same(changed, window.Result); Assert.Same(material, window.Comparison);
                controls.OfType<ComboBox>().Single(c => c.Name == "camera-scene-mode").SelectedIndex = 0;
                Assert.False(run.IsEnabled); Assert.Same(material, window.Comparison);
                Assert.True(await window.ImportSceneSpectrumFromPathAsync(Path.Combine(CameraSensorFileTests.Examples, "reflectance-neutral.csv"), false));
                Assert.True(run.IsEnabled); await window.RunAsync(); Assert.Null(window.Result!.Integration!.Materials);
                Assert.Same(material, window.Comparison);
                app.Documents.NewBlank(); Assert.Null(window.Result); Assert.Null(window.Comparison);
                return true;
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private static void Capture(Window window, string name)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var path = Environment.GetEnvironmentVariable("OPTICAL_CAMERA_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(path)) return;
        Directory.CreateDirectory(path); using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame); frame.Save(Path.Combine(path, name + ".png"), PngBitmapEncoderOptions.Default);
    }
}

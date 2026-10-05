using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.App.Laboratories;
using OptilandWorkbench.App.Theming;

namespace OptilandWorkbench.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class OpticalAssemblyWindowTests
{
    public static AppBuilder BuildAvaloniaApp()
    {
        var capture = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPTICAL_ASSEMBLY_CAPTURE_DIR"));
        var builder = AppBuilder.Configure<global::OptilandWorkbench.App.App>();
        if (capture) builder.UseSkia();
        return builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = !capture });
    }

    [Fact]
    public async Task CurrentLensWindowProducesMetricReticleAndClearsStaleData()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(OpticalAssemblyWindowTests));
        await session.Dispatch<bool>(async () =>
        {
            ThemeApplicationService.Apply(global::Avalonia.Application.Current!, "Light");
            using var app = WorkbenchApplication.Create("cooke");
            var window = new OpticalAssemblyWindow(app);
            try
            {
                window.Show();
                await window.CalculateAsync();
                Assert.NotNull(window.Result);
                Assert.Equal(12, window.Result.Rows.Count);
                Capture(window, "cooke-initial");
                Assert.True(window.Preview is not null,
                    string.Join(" | ", window.GetVisualDescendants().OfType<TextBlock>().Select(x => x.Text)));
                Assert.True(window.Preview.RecordedRays > 0, window.Preview.Message);
                var firstLength = window.Preview.ImageLength!.Value;
                var controls = window.GetVisualDescendants().OfType<Control>().ToArray();
                var reticleLength = Assert.Single(controls.OfType<NumericUpDown>(), x => x.Name == "assembly-reticle-length");
                reticleLength.Value = 2;
                Assert.Null(window.Preview);
                Assert.NotNull(window.Result);
                await window.SimulateAsync();
                Assert.Equal(firstLength * 2, window.Preview!.ImageLength!.Value, 4);
                Assert.Equal(4, window.Preview.SensorWidth);
                Assert.All(controls.OfType<Button>().Where(b => b.Name is "assembly-calculate" or "assembly-simulate"),
                    b => Assert.Contains("accent", b.Classes));
                Capture(window, "cooke-vertex-reticle-2mm");

                var grid = Assert.Single(controls.OfType<DataGrid>(), x => x.Name == "assembly-conjugates");
                grid.SelectedIndex = 1;
                await window.SimulateAsync();
                Assert.NotNull(window.Preview);
                Capture(window, "cooke-center-reticle");
                window.Width = 920; window.Height = 680;
                Capture(window, "cooke-center-920x680");
                var simulate = controls.OfType<Button>().Single(x => x.Name == "assembly-simulate");
                var bottom = simulate.TranslatePoint(new Point(0, simulate.Bounds.Height), window);
                Assert.NotNull(bottom);
                Assert.InRange(bottom.Value.Y, 1, window.ClientSize.Height);
                window.Width = 1260; window.Height = 850;

                var row = app.Prescription.GetSurfaces()[1];
                app.Prescription.UpdateSurface(row with { Radius = row.Radius + 0.5 });
                Assert.Null(window.Result);
                Assert.Null(window.Preview);
                await window.CalculateAsync();
                Assert.Equal(app.Events.Revision, window.Result!.SourceRevision);
                app.Documents.NewBlank();
                Assert.Null(window.Result);
                Assert.Null(window.Preview);
                await window.CalculateAsync();
                Assert.Null(window.Result);
                app.Modes.SwitchTo(OpticalWorkbenchMode.NonSequential);
                Assert.False(controls.OfType<Button>().Single(x => x.Name == "assembly-calculate").IsEnabled);
                Capture(window, "empty-nonsequential-disabled");
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task InfinityModeEnablesInfiniteRowsAndModeChangesDiscardPendingImages()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(OpticalAssemblyWindowTests));
        await session.Dispatch<bool>(async () =>
        {
            ThemeApplicationService.Apply(global::Avalonia.Application.Current!, "Light");
            using var app = WorkbenchApplication.Create("cooke");
            var first = app.Prescription.GetSurfaces()[1];
            app.Prescription.UpdateSurface(first with { Radius = 0 });
            var window = new OpticalAssemblyWindow(app);
            try
            {
                window.Show();
                await window.CalculateAsync();
                var original = window.Result;
                Assert.Equal(AssemblyConjugateStatus.Infinity, original!.Rows[1].Status);
                var controls = window.GetVisualDescendants().OfType<Control>().ToArray();
                var grid = controls.OfType<DataGrid>().Single(x => x.Name == "assembly-conjugates");
                var mode = controls.OfType<ComboBox>().Single(x => x.Name == "assembly-measurement-mode");
                var simulate = controls.OfType<Button>().Single(x => x.Name == "assembly-simulate");
                grid.SelectedIndex = 1;
                await window.SimulateAsync();
                Assert.Null(window.Preview);
                Assert.False(simulate.IsEnabled);
                Assert.Contains("无穷远", ToolTip.GetTip(simulate)?.ToString());
                mode.SelectedIndex = 1;
                Assert.Same(original, window.Result);
                Assert.True(simulate.IsEnabled);
                Assert.Null(ToolTip.GetTip(simulate));
                await window.SimulateAsync();
                Assert.Equal(AssemblyMeasurementMode.Infinity, window.Preview!.Mode);
                Assert.True(window.Preview.MatchesSelectedConjugate);
                Assert.Null(window.Preview.HeadPosition);
                Assert.True(window.Preview.RecordedRays > 0);
                Assert.Equal(-100, window.Preview.CollimatorPosition, 6);
                Capture(window, "infinity-plane-reticle");

                var distance = window.GetVisualDescendants().OfType<NumericUpDown>().Single(x => x.Name == "assembly-instrument-distance");
                Assert.True(distance.IsEffectivelyVisible);
                distance.Value = 150;
                Assert.Null(window.Preview);
                Assert.Same(original, window.Result);
                await window.SimulateAsync();
                Assert.Equal(-150, window.Preview!.CollimatorPosition, 6);
                Assert.Equal(1, window.Preview.ImageLength!.Value, 6);
                window.Width = 920; window.Height = 680;
                Capture(window, "infinity-plane-920x680");
                var bottom = simulate.TranslatePoint(new Point(0, simulate.Bounds.Height), window);
                Assert.InRange(bottom!.Value.Y, 1, window.ClientSize.Height);

                var pending = window.SimulateAsync();
                mode.SelectedIndex = 0;
                await pending;
                Assert.Null(window.Preview);
                Assert.Same(original, window.Result);
                Assert.False(simulate.IsEnabled);
                Assert.False(distance.IsEffectivelyVisible);
                Assert.Contains("无穷远", Avalonia.Automation.AutomationProperties.GetHelpText(simulate));
                return true;
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task LaboratoryEntryReusesItsOwnedWindow()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(OpticalAssemblyWindowTests));
        await session.Dispatch(() =>
        {
            var previous = Environment.GetEnvironmentVariable("OPTILAND_SETTINGS_DIRECTORY");
            var settings = Path.Combine(Path.GetTempPath(), "assembly-window-" + Guid.NewGuid().ToString("N"));
            Environment.SetEnvironmentVariable("OPTILAND_SETTINGS_DIRECTORY", settings);
            global::OptilandWorkbench.App.MainWindow? main = null;
            try
            {
                main = new global::OptilandWorkbench.App.MainWindow();
                main.Show();
                var method = typeof(global::OptilandWorkbench.App.MainWindow).GetMethod("ShowOpticalAssembly",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                method.Invoke(main, null);
                var windows = main.OwnedWindows.OfType<OpticalAssemblyWindow>().ToArray();
                Assert.Single(windows);
                method.Invoke(main, null);
                Assert.Single(main.OwnedWindows.OfType<OpticalAssemblyWindow>());
                windows[0].Close();
            }
            finally
            {
                if (main is not null)
                    typeof(global::OptilandWorkbench.App.MainWindow).GetField("_closeAfterPersistence",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(main, true);
                main?.Close();
                Environment.SetEnvironmentVariable("OPTILAND_SETTINGS_DIRECTORY", previous);
                if (Directory.Exists(settings)) Directory.Delete(settings, recursive: true);
            }
        }, CancellationToken.None);
    }

    private static void Capture(Window window, string name)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var path = Environment.GetEnvironmentVariable("OPTICAL_ASSEMBLY_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(path)) return;
        Directory.CreateDirectory(path);
        using var bitmap = window.CaptureRenderedFrame();
        Assert.NotNull(bitmap);
        bitmap.Save(Path.Combine(path, name + ".png"), PngBitmapEncoderOptions.Default);
    }
}

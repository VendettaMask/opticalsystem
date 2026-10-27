using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Laboratories;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using GuideWindow = OpticalSystemDesign.App.MainWindow;
using InitialWindow = OpticalSystemDesign.App.Laboratories.InitialStructure.MainWindow;
using InitialSettings = OpticalSystemDesign.App.Laboratories.InitialStructure.LabDesktopSettings;
using CoatingWindow = OpticalSystemDesign.App.Laboratories.CoatingDesign.MainWindow;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class UserGuideCaptureTests
{
    [GuideCaptureTheory]
    [InlineData(UiLocalization.Chinese)]
    [InlineData(UiLocalization.English)]
    [InlineData(UiLocalization.Japanese)]
    public async Task CaptureCurrentProductWithoutDesktopOrUserProfileChanges(string language)
    {
        var output = Path.Combine(Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_GUIDE_CAPTURE_DIR")!, language);
        var drawingOnly = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_GUIDE_CAPTURE_SCOPE") == "drawing";
        Directory.CreateDirectory(output);
        var temporary = Path.Combine(Path.GetTempPath(), "guide-capture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        var captured = new List<string>();
        var settingsDirectory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SETTINGS_DIRECTORY");
        Environment.SetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SETTINGS_DIRECTORY", Path.Combine(temporary, "settings"));
        void Progress(string stage) => File.AppendAllText(Path.Combine(output, "progress.log"), DateTimeOffset.Now + " " + stage + Environment.NewLine);
        Progress("session start");
        try
        {
            using var session = SafeHeadlessUnitTestSession.StartNew(typeof(OpticalSystemDesign.App.App));
            await session.Dispatch(async () =>
            {
                Progress("dispatch start");
                UiLocalization.InstallGlobalHooks();
                UiLocalization.Apply(language);
                ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
                var app = WorkbenchApplication.Create("cooke", lensLibraryDirectory: Path.Combine(RepositoryRoot(), "src/OpticalSystemDesign.App/Assets/LensLibrary"));
                var main = new GuideWindow(new AppSettings { Language = language, Theme = "Light" }, app,
                    new WorkspaceSessionStore(Path.Combine(temporary, "sequential")))
                { Width = 1440, Height = 900 };
                var panels = Field<PanelManager>(main, "_panels");
                var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                main.StartupCompleted += (_, _) => ready.TrySetResult();
                main.Show(); await ready.Task.WaitAsync(TimeSpan.FromSeconds(20)); Tick(main);
                Progress("main ready");
                try
                {
                    if (drawingOnly)
                    {
                        Ribbon(main, 6);
                        Assert.Equal(new[] { "show-optical-drawing" },
                            Field<ActionManager>(main, "_actions").Actions
                                .Where(action => action.Id.StartsWith("show-optical-drawing", StringComparison.Ordinal))
                                .Select(action => action.Id).ToArray());
                        Assert.Equal(new[] { "ribbon-show-optical-drawing" },
                            main.GetVisualDescendants().OfType<Button>()
                                .Select(AutomationProperties.GetAutomationId)
                                .Where(id => id?.StartsWith("ribbon-show-optical-drawing", StringComparison.Ordinal) == true)
                                .ToArray());
                        panels.ShowLensLibrary(); Tick(main); Capture(main, "lens-library");
                        panels.ShowOpticalDrawing();
                        var drawingPanel = Assert.IsType<OpticalDrawingPanel>(panels.Factory.OpenDocuments().Single(item => item.Context is OpticalDrawingPanel).Context);
                        var standardPicker = Field<ComboBox>(drawingPanel, "_standardPicker");
                        Assert.Equal(2, standardPicker.ItemCount);
                        Field<ComboBox>(drawingPanel, "_elementPicker").SelectedIndex = 1;
                        Tick(main); Capture(main, "drawing");
                        standardPicker.SelectedIndex = 1;
                        Tick(main); Capture(main, "drawing-legacy");
                        return;
                    }
                    Capture(main, "workbench");
                    Ribbon(main, 0); Capture(main, "projects");
                    panels.ShowViewer(OpticSceneViewMode.TwoDimensional);
                    Ribbon(main, 2); await WaitScene(main); Capture(main, "layout-2d");
                    panels.ShowSolidModel(); await WaitScene(main); Capture(main, "solid-model");
                    Ribbon(main, 3);
                    await Analysis("Spot Diagram", "spot");
                    await Analysis("MTF", "mtf");
                    await Analysis("Wavefront Map", "wavefront");
                    app.Optimization.GenerateDefaultMeritFunction(MeritFunctionPreset.RmsSpot);
                    panels.Show(WorkspacePanelId.Optimization); Ribbon(main, 4); Tick(main); Capture(main, "optimization");
                    panels.Show(WorkspacePanelId.Tolerancing); Ribbon(main, 5); Tick(main); Capture(main, "tolerancing");
                    var tolerancePanel = Assert.IsType<TolerancingPanel>(panels.Factory.OpenDocuments().Single(d => d.Context is TolerancingPanel).Context);
                    var defaults = (TolerancingRunOptions)typeof(TolerancingPanel).GetMethod("CurrentRunOptions", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(tolerancePanel, null)!;
                    var toleranceDialog = new TolerancingRunWindow(defaults, app.Prescription.GetFields(), app.Prescription.GetSurfaces(), app.Prescription.GetWavelengths());
                    toleranceDialog.Show(main); Capture(toleranceDialog, "tolerance-settings"); toleranceDialog.Close();
                    panels.ShowLensLibrary(); Ribbon(main, 6); Tick(main); Capture(main, "lens-library");
                    panels.ShowOpticalDrawing();
                    Tick(main); Capture(main, "drawing");

                    var camera = new CameraImagingWindow(app) { Width = 1320, Height = 850 };
                    Field<ComboBox>(camera, "_optics").SelectedIndex = 1;
                    camera.Show(main); await camera.RunAsync().WaitAsync(TimeSpan.FromSeconds(30));
                    Assert.NotNull(camera.Result); Capture(camera, "camera"); camera.Close();
                    var coating = new CoatingWindow(); coating.Show(main);
                    await (Task)typeof(CoatingWindow).GetMethod("RunAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(coating, ["generate"])!;
                    Capture(coating, "coating");
                    typeof(CoatingWindow).GetMethod("CloseAfterHostApproval", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.Invoke(coating, null);
                    var initial = new InitialWindow(new InitialSettings { RunDirectory = Path.Combine(temporary, "initial") });
                    initial.Show(main); await initial.Ready.WaitAsync(TimeSpan.FromSeconds(30)); Capture(initial, "initial-structure"); initial.Close();
                    var assembly = new OpticalAssemblyWindow(app); assembly.Show(main); Capture(assembly, "assembly"); assembly.Close();
                }
                finally { main.Close(); }

                var nonSequential = WorkbenchApplication.Create("blank");
                nonSequential.Modes.SwitchTo(OpticalWorkbenchMode.NonSequential);
                var source = nonSequential.NonSequential.AddObject(NonSequentialObjectKind.SourcePoint);
                var detector = nonSequential.NonSequential.AddObject(NonSequentialObjectKind.DetectorRectangle);
                var rows = nonSequential.NonSequential.GetDocument().Objects;
                var row = rows.Single(r => r.Id == source);
                nonSequential.NonSequential.UpdateObject(new(source, true, true, row.Kind, row.Name, null, null,
                    0, 0, 0, 0, 0, 0, new SourcePointParameters(1, 1, 8, 24, 1000)));
                row = rows.Single(r => r.Id == detector);
                nonSequential.NonSequential.UpdateObject(new(detector, true, true, row.Kind, row.Name, null, null,
                    0, 0, 25, 0, 0, 0, new DetectorRectangleParameters(12, 12, 32, 32, true, true)));
                var nsWindow = new GuideWindow(new AppSettings { Language = language, WorkbenchMode = nameof(OpticalWorkbenchMode.NonSequential) },
                    nonSequential, new WorkspaceSessionStore(Path.Combine(temporary, "non-sequential")))
                { Width = 1440, Height = 900 };
                nsWindow.Show(); Tick(nsWindow);
                try
                {
                    var nsPanels = Field<PanelManager>(nsWindow, "_panels");
                    Capture(nsWindow, "non-sequential");
                    await nonSequential.NonSequentialAnalysis.TraceAsync(new());
                    nsPanels.ShowNonSequentialDetectorViewer(); Tick(nsWindow);
                    await WaitUntil(() => !nsWindow.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("正在计算") == true), nsWindow);
                    Capture(nsWindow, "detector");
                }
                finally { nsWindow.Close(); }

                async Task Analysis(string name, string key)
                {
                    Progress("analysis " + name);
                    panels.ShowAnalysis(name); Tick(main);
                    var active = Assert.IsType<AnalysisPanel>(panels.Factory.OpenDocuments().Single(d => d.Context is AnalysisPanel a && a.AnalysisKey == app.Analyses.CanonicalKey(name)).Context);
                    Assert.True(await active.RunForGuiCaptureAsync().WaitAsync(TimeSpan.FromSeconds(45))); Tick(main); Capture(main, key);
                }
                void Capture(Window window, string key)
                {
                    Tick(window);
                    var path = Path.Combine(output, key + ".png");
                    using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.ClientSize.Width, (int)window.ClientSize.Height), new Vector(96, 96));
                    bitmap.Render(window);
                    using (var stream = File.Create(path)) bitmap.Save(stream, new PngBitmapEncoderOptions());
                    Assert.True(new FileInfo(path).Length > 12000, key);
                    captured.Add(key);
                    Progress("captured " + key);
                }
            }, CancellationToken.None);
            await File.WriteAllTextAsync(Path.Combine(output, drawingOnly ? "drawing-capture.json" : "capture.json"), JsonSerializer.Serialize(new
            { language, date = "2026-10-27", renderer = "Actual product controls; Avalonia Headless + Skia; 96 DPI; no native desktop input", screens = captured }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SETTINGS_DIRECTORY", settingsDirectory);
            Directory.Delete(temporary, true);
        }
    }

    private static async Task WaitScene(Window window) => await WaitUntil(() =>
        window.GetVisualDescendants().OfType<OpticSceneControl>().Any(control => control.Scene is not null), window);
    private static async Task WaitUntil(Func<bool> ready, Window window)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (!ready() && DateTimeOffset.UtcNow < deadline) { await Task.Delay(20); Tick(window); }
        Assert.True(ready());
    }
    private static void Ribbon(Window window, int index)
    {
        window.GetVisualDescendants().OfType<TabControl>().First().SelectedIndex = index;
        Tick(window);
    }
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static void Tick(Window window)
    { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
    private static string RepositoryRoot() => ProductDependencyArchitectureTests.RepositoryRoot();

    private sealed class GuideCaptureTheoryAttribute : TheoryAttribute
    {
        public GuideCaptureTheoryAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_GUIDE_CAPTURE_DIR")))
                Skip = "Set OPTICAL_SYSTEM_DESIGN_GUIDE_CAPTURE_DIR to refresh versioned off-screen tutorial screenshots.";
        }
    }
}

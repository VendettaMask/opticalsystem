using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Model.Core;
using OpticalSystemDesign.App.Laboratories;
using OpticalSystemDesign.App.Manufacturing;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class BroadEnglishAudit
{
    const string Output = "/private/tmp/opticalsystem-english-ui-20261109/broad-audit";
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    static bool Cjk(string? s) => s is not null && Regex.IsMatch(s, "[\\u3400-\\u9fff]");
    static void Write(string name, object value) => File.WriteAllText(Path.Combine(Output, name + ".json"), JsonSerializer.Serialize(value, Json));
    static T Field<T>(object o, string name) => (T)o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(o)!;
    static object? Call(object o, string name, params object[] args) => o.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(o, args);
    static IEnumerable<Control> Nodes(Control c) => c.GetVisualDescendants().OfType<Control>().Concat(c.GetLogicalDescendants().OfType<Control>()).Append(c).Distinct();
    static void Tick(Window w) { w.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); w.UpdateLayout(); }
    static void Record(string name, Window window)
    {
        Tick(window);
        var entries = Nodes(window).SelectMany(c => new[] {
            (Property: "Text", Text: (c as TextBlock)?.Text),
            (Property: "Content", Text: (c as ContentControl)?.Content as string),
            (Property: "Header", Text: (c as HeaderedContentControl)?.Header as string),
            (Property: "Placeholder", Text: c is ComboBox box ? box.PlaceholderText : (c as TextBox)?.PlaceholderText),
            (Property: "EditorValue", Text: (c as TextBox)?.Text)
        }.Where(p => !string.IsNullOrEmpty(p.Text)).Select(p => new { Type = c.GetType().Name, c.Name, p.Property, p.Text, Chinese = Cjk(p.Text), Visible = c.IsEffectivelyVisible })).ToArray();
        Write(name, new { Language = UiLocalization.CurrentLanguage, Title = window.Title, Entries = entries });
    }
    static async Task Inspect(string name, Control content)
    {
        var window = content as Window ?? new Window { Width = 1500, Height = 1000, Content = content };
        try
        {
            window.Show(); Tick(window); await Task.Delay(100); Tick(window);
            foreach (var e in Nodes(window).OfType<Expander>().ToArray()) e.IsExpanded = true;
            await Task.Delay(100); Tick(window);
            Record(name, window);
            if (name == "coating") Assert.Contains(Nodes(window), c => c.Name == "CoatingLayerCard");
        }
        finally { window.Close(); if (content is IDisposable disposable) disposable.Dispose(); }
    }
    static IEnumerable<IDockable> Docks(IDockable node)
    {
        yield return node;
        if (node is IDock dock && dock.VisibleDockables is not null)
            foreach (var item in dock.VisibleDockables) foreach (var child in Docks(item)) yield return child;
    }

    [Fact]
    public async Task CaptureCurrentEnglishUiAndNativeExportsWithoutChangingUserSession()
    {
        Directory.CreateDirectory(Output);
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OpticalSystemDesign.App.App));
        await session.Dispatch(async () =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
            DisplayTypography.Configure(new AppSettings { Theme = "Light" });
            UiLocalization.InstallGlobalHooks(); UiLocalization.Apply(UiLocalization.English);
            using var app = WorkbenchApplication.Create("cooke");
            var before = app.Documents.GetRecoveryState();
            var settings = new AppSettings { Theme = "Light", Language = UiLocalization.English };
            var factory = new WorkspaceDockFactory(app, settings);
            var root = factory.CreateLayout();
            factory.InitLayout(root);
            Write("workspace-light", Docks(root).Select(d => new { d.Id, d.Title }));
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Dark");
            UiLocalization.Apply(UiLocalization.English);
            Write("workspace-dark", Docks(root).Select(d => new { d.Id, d.Title }));
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
            factory.DisposeContent();

            await Inspect("system-options", new SystemPropertiesPanel(app.Prescription, app.Materials, app.Events));
            await Inspect("optimization", new OptimizationPanel(app.Prescription, app.Optimization, app.Events));
            await Inspect("lens-library", new LensLibraryPanel(app.Lenses));
            await Inspect("manufacturability", new ManufacturabilityPanel(app.Prescription, app.Events));
            await Inspect("camera", new CameraImagingWindow(app));
            await Inspect("coating", new global::OpticalSystemDesign.App.Laboratories.CoatingDesign.MainWindow());

            using (var drawing = new OpticalDrawingPanel(app.Prescription, app.Materials, app.Events, app.Visualization))
            {
                var window = new Window { Width = 1500, Height = 1000, Content = drawing };
                try
                {
                    window.Show(); Tick(window); Record("drawing-settings", window);
                    Field<ComboBox>(drawing, "_elementPicker").SelectedIndex = 1; Tick(window);
                    var sheet = (OpticalDrawingSheet?)Call(drawing, "CreateSheet");
                    Assert.NotNull(sheet);
                    File.WriteAllBytes(Path.Combine(Output, "drawing.png"), OpticalDrawingRenderer.RenderPreview(sheet));
                    OpticalDrawingRenderer.ExportPdf(Path.Combine(Output, "drawing.pdf"), sheet);
                }
                finally { window.Close(); }
            }
            var reportWindow = new MarkdownReportWindow(app, settings);
            try
            {
                reportWindow.Show(); Record("report-settings", reportWindow);
                foreach (var key in new[] { "Single Ray Trace", "Spot Diagram", "Relative Illumination" })
                { Call(reportWindow, "EditParameters", key); Record("report-settings-" + key, reportWindow); }
            }
            finally { reportWindow.Close(); }

            foreach (var key in new[] { "Spot Diagram", "Field Curvature and Distortion", "Wavefront Map", "Interferogram", "PSF", "MTF", "RMS vs Field", "Encircled Energy", "Relative Illumination" })
            {
                using var panel = new AnalysisPanel(app.Analyses, app.Visualization, app.Documents, app.Events, settings, key);
                var window = new Window { Width = 1500, Height = 1050, Content = panel };
                try
                {
                    Assert.True(await panel.RunForGuiCaptureAsync()); window.Show(); Tick(window);
                    Record("analysis-" + key, window);
                    var view = Field<AnalysisViewDto>(panel, "_view");
                    Write("dto-" + key, view);
                    if (key is "Spot Diagram" or "Wavefront Map" or "Interferogram") { using var image = panel.CapturePlotImage(1); AnalysisPanel.SavePlotImage(image, Path.Combine(Output, key + ".png")); }
                }
                finally { window.Close(); }
            }

            using (var snapshot = app.CaptureReportSnapshot())
            {
                var result = await MarkdownReportExporter.ExportAsync(snapshot,
                    new() { Format = DesignReportFormat.Pdf, AnalysisKeys = ["MTF"], Include2DLayout = false, Include3DLayout = false },
                    Path.Combine(Output, "report"), async (request, directory, stem, token) => await Dispatcher.UIThread.InvokeAsync<IReadOnlyList<string>>(() =>
                    {
                        Assert.NotNull(request.Analysis);
                        using var panel = new AnalysisPanel(app.Analyses, app.Visualization, app.Documents, app.Events, settings, "MTF");
                        panel.PresentResult(request.Analysis.View, request.Document, request.CapturedAt);
                        var window = new Window { Width = 1500, Height = 1050, Content = panel };
                        try
                        {
                            window.Show(); Tick(window);
                            using var image = panel.CapturePlotImage(1);
                            var path = Path.Combine(directory, stem + ".png"); AnalysisPanel.SavePlotImage(image, path);
                            return [Path.GetFileName(path)];
                        }
                        finally { window.Close(); }
                    }));
                Write("report-result", result); Assert.Equal(0, result.Failed);
            }
            Assert.Equal(before, app.Documents.GetRecoveryState());
            using var nsc = WorkbenchApplication.Create(); nsc.Modes.SwitchTo(OpticalWorkbenchMode.NonSequential);
            if (nsc.NonSequential.GetDocument().Objects.Count == 0)
            { nsc.NonSequential.AddObject(NonSequentialObjectKind.SourcePoint); nsc.NonSequential.AddObject(NonSequentialObjectKind.DetectorRectangle); }
            Write("nsc-data", nsc.NonSequential.GetDocument());
            await Inspect("nsc-objects", new NonSequentialObjectEditorPanel(nsc.NonSequential, nsc.Events));
            await Inspect("nsc-settings", new NonSequentialModePanel(nsc.NonSequential, nsc.Events));
            Write("outcome", new { Completed = true, UIOnlyAudit = true, LocaleAcceptance = false, PrescriptionUnchanged = true });
        }, CancellationToken.None);
    }
}

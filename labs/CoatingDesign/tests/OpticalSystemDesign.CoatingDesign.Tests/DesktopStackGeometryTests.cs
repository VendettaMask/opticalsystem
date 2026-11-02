using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Laboratories.CoatingDesign;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.CoatingDesign.Engine;

namespace OpticalSystemDesign.CoatingDesign.Tests;

public sealed partial class DesktopTests
{
    [Theory]
    [InlineData("Light", 13, 20)]
    [InlineData("Dark", 13, 20)]
    [InlineData("Light", 18, 200)]
    public async Task VerticalStackAnalysisIsSeparateLiveAndExportsOneOriginalSheet(string theme, double fontSize, int count)
    {
        var directory = Path.Combine(Path.GetTempPath(), "coating-vertical-stack-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
        try
        {
            var original = ReferenceExamples.Create("demo-edge");
            var experiment = original with { Layers = Enumerable.Range(0, count).Select(i => original.Layers[i % original.Layers.Length] with { ThicknessNm = 30 + i * 4.75 }).ToArray(), Result = null };
            var path = Path.Combine(directory, "stack.coating.json"); await ExperimentStore.SaveAsync(experiment, path);
            using var host = new Host();
            await host.Run(async () =>
            {
                DisplayTypography.Configure(new AppSettings { Theme = theme, FontSize = fontSize });
                Assert.IsType<global::OpticalSystemDesign.App.App>(Avalonia.Application.Current).ApplyTheme(theme); UiLocalization.Apply(UiLocalization.Chinese);
                var window = new MainWindow(new LaboratoryFileDialogs(() => Task.FromResult<string?>(null), () => Task.FromResult<string?>(path), () => Task.FromResult<string?>(null)))
                    { Width = 1200, Height = 900, ChooseUnsavedChanges = DiscardChanges };
                try
                {
                    window.Show(); Tick(window); Click(window, "打开实验"); await window.PendingOperation; Tick(window);
                    var fingerprint = window.CurrentExperiment.Fingerprint(); Assert.False(window.HasUnsavedChanges);
                    Assert.DoesNotContain(WindowControls(window).OfType<Control>(), c => c.Name is "CoatingStackGeometry" or "CoatingStackFilms");
                    Click(window, "堆栈几何分析"); await window.PendingOperation;
                    var analysis = Assert.IsType<CoatingStackAnalysisView>(window.DockWorkspace.DockFactory.Pages[CoatingDockWorkspace.StackId].Context);
                    window.DockWorkspace.DockFactory.FloatDockable(window.DockWorkspace.DockFactory.Pages[CoatingDockWorkspace.StackId]); Tick(window);
                    var analysisHost = Assert.IsAssignableFrom<Window>(TopLevel.GetTopLevel(analysis));
                    analysisHost.Width = 1240; analysisHost.Height = 980; Tick(analysisHost);
                    Assert.Equal(fingerprint, window.CurrentExperiment.Fingerprint()); Assert.False(window.HasUnsavedChanges);
                    Assert.Equal(count, analysis.Plot.Bands.Count);
                    Assert.Equal(experiment.Layers.Select(l => l.ThicknessNm), analysis.Snapshot!.Layers.Select(l => l.ThicknessNm));
                    Assert.All(analysis.Plot.Bands.Zip(analysis.Plot.Bands.Skip(1)), pair =>
                    {
                        Assert.Equal(pair.First.Front.Left, pair.Second.Front.Left); Assert.True(pair.First.Front.Bottom <= pair.Second.Front.Top + 1e-8);
                    });
                    Assert.All(analysis.Plot.Bands, band => Assert.True(band.Front.Height >= 1.19));
                    analysis.Plot.Select(count); Assert.Equal(count, analysis.Plot.SelectedLayer);
                    var tabs = analysis.GetVisualDescendants().OfType<TabControl>().Single(c => c.Name == "CoatingStackAnalysisTabs");
                    tabs.SelectedIndex = 1; Tick(analysisHost);
                    Assert.Equal(count - 1, analysis.GetVisualDescendants().OfType<DataGrid>().Single(c => c.Name == "CoatingStackData").SelectedIndex);
                    tabs.SelectedIndex = 0; Tick(analysisHost);
                    var firstBand = analysis.Plot.Bands[0].Front;
                    var point = analysis.Plot.TranslatePoint(firstBand.Center, analysisHost)!.Value;
                    analysisHost.MouseDown(point, MouseButton.Left); analysisHost.MouseUp(point, MouseButton.Left); Tick(analysisHost);
                    Assert.Equal(1, analysis.Plot.SelectedLayer);
                    analysisHost.KeyPress(Key.End, RawInputModifiers.None, PhysicalKey.End, null); Tick(analysisHost);
                    Assert.Equal(count, analysis.Plot.SelectedLayer);
                    using (var png = analysis.CaptureImage())
                    {
                        Assert.Equal(new PixelSize(2404, 1804), png.PixelSize);
                        var evidence = Environment.GetEnvironmentVariable("COATING_UI_EVIDENCE");
                        if (!string.IsNullOrWhiteSpace(evidence)) { Directory.CreateDirectory(evidence); png.Save(Path.Combine(evidence, $"vertical-stack-{theme}-{count}.png"), PngBitmapEncoderOptions.Default); }
                    }
                    analysisHost.Width = 820; analysisHost.Height = 650; Tick(analysisHost);
                    using (var png = analysis.CaptureImage()) Assert.Equal(new PixelSize(2404, 1804), png.PixelSize);
                    Capture(analysisHost, $"vertical-stack-window-{theme}-{count}");
                    window.DockWorkspace.Close(CoatingDockWorkspace.StackId); Tick(window);
                    Assert.False(window.DockWorkspace.IsPageOpen(CoatingDockWorkspace.StackId));
                    Click(window, "堆栈几何分析"); await window.PendingOperation;
                    analysis = Assert.IsType<CoatingStackAnalysisView>(window.DockWorkspace.DockFactory.Pages[CoatingDockWorkspace.StackId].Context); Tick(window);
                    var rows = ((IEnumerable<LayerRow>)WindowControls(window).OfType<DataGrid>().Single(g => g.Name == "CoatingLayerTable").ItemsSource).ToArray();
                    rows[0].ThicknessText = "123.456789"; Tick(window);
                    Assert.Equal(123.456789, analysis.Snapshot!.Layers[0].ThicknessNm);
                    Find<TextBox>(window, "基板厚度 (mm)").Text = "invalid"; Tick(window);
                    Assert.Null(analysis.Snapshot); Assert.Empty(analysis.Plot.Bands);
                    Assert.Throws<InvalidOperationException>(() => analysis.CaptureImage());
                    Find<TextBox>(window, "基板厚度 (mm)").Text = "2.25"; Tick(window);
                    Assert.Equal(2.25, analysis.Snapshot!.SubstrateThicknessMm); Assert.Equal(count, analysis.Plot.Bands.Count);
                    using (var png = analysis.CaptureImage()) Assert.Equal(new PixelSize(2404, 1804), png.PixelSize);
                    Assert.Equal(experiment.Layers.Skip(1).Select(l => l.ThicknessNm), analysis.Snapshot.Layers.Skip(1).Select(l => l.ThicknessNm));
                    return;
                }
                finally { window.CloseAfterHostApproval(); }
            });
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task BareSubstrateAndLanguageChangesRemainViewOnly()
    {
        using var host = new Host();
        await host.Run(async () =>
        {
            DisplayTypography.Configure(new AppSettings { Theme = "Light", FontSize = 13 });
            var window = new MainWindow { ChooseUnsavedChanges = DiscardChanges };
            try
            {
                window.Show(); Tick(window); Click(window, "堆栈几何分析"); await window.PendingOperation;
                var analysis = Assert.IsType<CoatingStackAnalysisView>(window.DockWorkspace.DockFactory.Pages[CoatingDockWorkspace.StackId].Context); Tick(window);
                var original = analysis.Snapshot!; var before = window.CurrentExperiment.Fingerprint();
                analysis.SetSnapshot(original with { Layers = [] }); Tick(window); Assert.Empty(analysis.Plot.Bands);
                using (var png = analysis.CaptureImage()) Assert.Equal(new PixelSize(2404, 1804), png.PixelSize);
                UiLocalization.Apply(UiLocalization.English); Tick(window);
                Assert.Equal("Stack Geometry", UiLocalization.Text("堆栈几何")); Assert.Equal(before, window.CurrentExperiment.Fingerprint());
                Assert.Empty(analysis.Plot.Bands); UiLocalization.Apply(UiLocalization.Chinese);
            }
            finally { UiLocalization.Apply(UiLocalization.Chinese); window.CloseAfterHostApproval(); }
        });
    }
}

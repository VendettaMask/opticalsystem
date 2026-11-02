using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using OpticalSystemDesign.App.Laboratories.CoatingDesign;
using OpticalSystemDesign.CoatingDesign.Engine;
using OpticalSystemDesign.Core.Coatings;

namespace OpticalSystemDesign.CoatingDesign.Tests;

public sealed partial class DesktopTests
{
    [Fact]
    public async Task SpectrumSwitchesAreRealIndependentReadOnlyAndExportTheCurrentView()
    {
        var directory = Path.Combine(Path.GetTempPath(), "coating-spectrum-ui-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
        var previousSettings = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SETTINGS_DIRECTORY");
        Environment.SetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SETTINGS_DIRECTORY", directory);
        try
        {
            var d = Examples.Create(DesignKind.HighReflector);
            d = d with { Target = d.Target with { AngleDegrees = 45, Polarization = ThinFilmPolarization.S },
                Layers = [new(d.HighId, 65), new(d.LowId, 95), new(d.HighId, 70)] };
            d = new DesignService().Evaluate(d); var path = Path.Combine(directory, "input.coating.json");
            await ExperimentStore.SaveAsync(d, path);
            using var host = new Host();
            await host.Run(async () =>
            {
                var window = new MainWindow(new LaboratoryFileDialogs(() => Task.FromResult<string?>(null),
                    () => Task.FromResult<string?>(path), () => Task.FromResult<string?>(directory)))
                { Width = 1500, Height = 960, ChooseUnsavedChanges = DiscardChanges };
                try
                {
                    window.Show(); Tick(window); Click(window, "打开实验"); await window.PendingOperation; Tick(window);
                    var plot = WindowControls(window).OfType<SpectrumPlot>().Single();
                    await plot.PendingEvaluation; Tick(window);
                    Assert.Equal(400, plot.AnalysisOptions.MinimumNm); Assert.Equal(800, plot.AnalysisOptions.MaximumNm);
                    Assert.Equal(.5, plot.AnalysisOptions.StepNm); Assert.Equal(new double[] { 0 }, plot.AnalysisOptions.AnglesDegrees);
                    Assert.Equal(801, plot.SampleCount);
                    var originalData = plot.DisplayData; var originalCount = plot.DisplayComputations;
                    void SelectCurve(CoatingQuantity quantity, CoatingCurvePolarization pol, bool visible) =>
                        Find<ToggleButton>(window, $"{quantity} {pol switch { CoatingCurvePolarization.Average => "平均", CoatingCurvePolarization.S => "s", _ => "p" }}").IsChecked = visible;
                    foreach (var quantity in Enum.GetValues<CoatingQuantity>()) foreach (var pol in Enum.GetValues<CoatingCurvePolarization>())
                        SelectCurve(quantity, pol, false);
                    plot.SetTargets(false);
                    int backgroundRed;
                    using (var empty = SkiaSharp.SKBitmap.Decode(plot.CapturePng()))
                        backgroundRed = empty.Pixels.Count(color => color.Red > 180 && color.Green < 140 && color.Blue < 160);
                    SelectCurve(CoatingQuantity.R, CoatingCurvePolarization.P, true);
                    foreach (var scale in new[] { 1d, 1.5, 2 })
                    {
                        plot.SetCurveWidth(scale); Tick(window);
                        using var image = SkiaSharp.SKBitmap.Decode(plot.CapturePng());
                        Assert.True(image.Pixels.Count(color => color.Red > 180 && color.Green < 140 && color.Blue < 160) > backgroundRed + 200, "The p-only curve must add visible red pixels beyond the unchanged axes.");
                    }
                    Capture(window, "p-only-0deg-1500");
                    plot.SetCurveWidth(1.5);
                    foreach (var quantity in Enum.GetValues<CoatingQuantity>()) foreach (var pol in Enum.GetValues<CoatingCurvePolarization>())
                        SelectCurve(quantity, pol, pol == CoatingCurvePolarization.Average);
                    plot.SetTargets(true);
                    Assert.Same(originalData, plot.DisplayData); Assert.Equal(originalCount, plot.DisplayComputations);
                    Click(window, "光谱图设置"); Tick(window);
                    var options = Assert.IsType<Flyout>(Find<DropDownButton>(window, "光谱图设置").Flyout);
                    var content = Assert.IsType<StackPanel>(options.Content);
                    TextBox Box(string name) => content.GetLogicalDescendants().OfType<TextBox>().Single(c => AutomationProperties.GetName(c) == name);
                    Assert.Equal("400", Box("评估波长下限").Text); Assert.Equal("800", Box("评估波长上限").Text);
                    Assert.Equal("0.5", Box("评估步长").Text);
                    var restoreDefaults = content.GetLogicalDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "恢复评估默认");
                    Assert.False(restoreDefaults.IsEnabled);
                    Capture(window, "evaluation-settings-factory-1500");
                    Box("评估角度 1").Text = "45";
                    Tick(window); Assert.True(restoreDefaults.IsEnabled);
                    content.GetLogicalDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "另存为评估默认")
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await plot.PendingEvaluation; Tick(window);
                    Assert.Equal(new double[] { 45 }, CoatingPlotDefaults.Load(Path.Combine(directory, "coating-plot-defaults.json")).Sampling.AnglesDegrees);
                    Assert.True(plot.HasData); var fingerprint = window.CurrentExperiment.Fingerprint();
                    var computations = plot.DisplayComputations; var data = plot.DisplayData;
                    var initialRange = plot.Range; var initial = plot.CapturePng();
                    var s = WindowControls(window).OfType<ToggleButton>().Single(b => AutomationProperties.GetName(b) == "T s");
                    s.BringIntoView(); Tick(window);
                    var p = s.TranslatePoint(new Point(s.Bounds.Width / 2, s.Bounds.Height / 2), window)!.Value;
                    window.MouseDown(p, MouseButton.Left); window.MouseUp(p, MouseButton.Left); Tick(window);
                    Assert.True(plot.CurveVisible(CoatingQuantity.T, CoatingCurvePolarization.S));
                    Assert.True(plot.CurveVisible(CoatingQuantity.T, CoatingCurvePolarization.Average));
                    Assert.NotEqual(initial, plot.CapturePng());
                    plot.SetCurveVisible(CoatingQuantity.R, CoatingCurvePolarization.P, true);
                    plot.ApplyRange(new(525, 575, 0, 100)); plot.SetTargets(false); plot.SetGrid(false); plot.SetCurveWidth(2);
                    plot.SetOpticalDensity(true); Assert.True(plot.OpticalDensity);
                    Assert.True(plot.CurveVisible(CoatingQuantity.R, CoatingCurvePolarization.P));
                    plot.SetOpticalDensity(false);
                    Assert.Equal(525, plot.Range.MinimumNm);
                    Assert.Throws<ArgumentException>(() => plot.ApplyRange(new(600, 500, 0, 100)));
                    Assert.Equal(525, plot.Range.MinimumNm);
                    Assert.Same(data, plot.DisplayData); Assert.Equal(computations, plot.DisplayComputations);
                    Assert.Equal(fingerprint, window.CurrentExperiment.Fingerprint());
                    plot.ResetView(); plot.SetGrid(true); plot.SetTargets(true); plot.SetCurveWidth(1.5); Tick(window);
                    Assert.Equal(initialRange, plot.Range);
                    Click(window, "保存光谱 PNG"); await window.PendingOperation;
                    Assert.True(File.Exists(Path.Combine(directory, "coating-spectrum.png")));
                    using (var stream = File.OpenRead(Path.Combine(directory, "coating-spectrum.png")))
                    using (var bitmap = new Bitmap(stream)) Assert.Equal((int)Math.Ceiling(plot.Bounds.Width * 2), bitmap.PixelSize.Width);
                    var center = plot.TranslatePoint(new Point(plot.Bounds.Width / 2, plot.Bounds.Height / 2), window)!.Value;
                    window.MouseMove(center); Tick(window);
                    var tooltip = Assert.IsType<StackPanel>(ToolTip.GetTip(plot));
                    Assert.Contains(tooltip.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "T s");
                    Assert.Contains(tooltip.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "T 平均");
                    Capture(window, "polarization-controls-1500");
                    var results = plot.ResultsSnapshot(); Assert.Equal(801, results.Rows.Length);
                    Assert.Equal("wavelength_nm", results.ColumnIds[0]); Assert.Contains(results.ColumnIds, id => id.Contains("T_average_45deg_percent"));
                    Click(window, "导出评估结果"); Tick(window);
                    var resultMenu = Assert.IsType<MenuFlyout>(Find<DropDownButton>(window, "导出评估结果").Flyout);
                    resultMenu.Items.OfType<MenuItem>().Single(i => AutomationProperties.GetName(i) == "导出光谱 CSV")
                        .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); resultMenu.Hide();
                    await window.PendingOperation;
                    Assert.Equal(results.Delimited(','), File.ReadAllText(Path.Combine(directory, "coating-spectrum.csv")));
                    Click(window, "导出评估结果"); Tick(window);
                    resultMenu.Items.OfType<MenuItem>().Single(i => AutomationProperties.GetName(i) == "导出光谱 TSV")
                        .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); resultMenu.Hide(); await window.PendingOperation;
                    Assert.Equal(results.Delimited('\t'), File.ReadAllText(Path.Combine(directory, "coating-spectrum.tsv")));
                    plot.SetUnits(CoatingWavelengthUnit.Micrometers, CoatingValueUnit.Fraction);
                    var fractions = plot.ResultsSnapshot(); Assert.Equal(.4, fractions.Rows[0].Values[0]);
                    Assert.InRange(Math.Abs(fractions.Rows[0].Values[1] * 100 - results.Rows[0].Values[1]), 0, 1e-12);
                    Assert.Same(data, plot.DisplayData);
                    plot.SetUnits(CoatingWavelengthUnit.Nanometers, CoatingValueUnit.Percent);
                    var expander = WindowControls(window).OfType<Expander>().Single(e => e.Content is DataGrid g && g.Name == "CoatingSpectrumResults");
                    expander.IsExpanded = true; Tick(window); Capture(window, "polarization-results-1500");
                    Click(window, "光谱图设置"); Tick(window);
                    content.GetLogicalDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "恢复评估默认")
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await plot.PendingEvaluation; Tick(window);
                    Assert.Equal(new double[] { 0 }, plot.AnalysisOptions.AnglesDegrees); Assert.Equal(400, plot.AnalysisOptions.MinimumNm);
                    Assert.Equal(.5, CoatingPlotDefaults.Load(Path.Combine(directory, "coating-plot-defaults.json")).Sampling.StepNm);
                    plot.SetExperiment(window.CurrentExperiment); var pending = plot.PendingEvaluation;
                    plot.SetExperiment(null); await pending; Tick(window);
                    Assert.False(plot.HasData); Assert.False(plot.IsPreparing);
                    Assert.Equal(fingerprint, window.CurrentExperiment.Fingerprint());
                }
                finally { window.CloseAfterHostApproval(); }
            });
        }
        finally { Environment.SetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SETTINGS_DIRECTORY", previousSettings); Directory.Delete(directory, true); }
    }
}

using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.LogicalTree;
using OpticalSystemDesign.App.Laboratories.CoatingDesign;
using OpticalSystemDesign.CoatingDesign.Engine;

namespace OpticalSystemDesign.CoatingDesign.Tests;

public sealed partial class DesktopTests
{
    [Fact]
    public async Task FiveReferenceExamplesOpenWithOriginalLayersAndComputedResults()
    {
        var previous = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SETTINGS_DIRECTORY");
        var directory = Path.Combine(Path.GetTempPath(), "coating-reference-desktop-" + Guid.NewGuid());
        Environment.SetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SETTINGS_DIRECTORY", directory);
        try
        {
            using var host = new Host();
            await host.Run(async () =>
            {
                var window = new MainWindow { Width = 1500, Height = 960, ChooseUnsavedChanges = DiscardChanges };
                try
                {
                    window.Show(); Tick(window);
                    foreach (var preset in ReferenceExamples.Presets)
                    {
                        Click(window, "示例：" + preset.Id); await window.PendingOperation; Tick(window);
                        var actual = window.CurrentExperiment; var original = ReferenceExamples.Create(preset.Id);
                        Assert.Equal(original.Layers, actual.Layers); Assert.Equal(original.Fingerprint(), actual.Fingerprint());
                        Assert.NotNull(actual.Result);
                        var plot = WindowControls(window).OfType<SpectrumPlot>().Single();
                        Assert.True(plot.HasData, plot.DisplayError); Assert.Equal(801, plot.SampleCount);
                        Assert.True(actual.StackGeometry!.IncludeBackSurface);
                        Assert.DoesNotContain(WindowControls(window).OfType<Control>(), c => c.Name == "CoatingStackGeometry");
                        Assert.All(plot.ResultsSnapshot().ColumnIds.Skip(1), id => Assert.StartsWith("system_", id));
                        if (preset.Id is "demo-edge" or "demo-bbar-4")
                            Assert.Contains(WindowControls(window).OfType<TextBlock>(), t => t.Text?.Contains("域外按参考模型延用端点") == true);
                        Capture(window, preset.Id + "-1500");
                    }
                    Click(window, "示例：demo-edge"); await window.PendingOperation; Tick(window);
                    var originalFormal = window.CurrentExperiment.Result!.Metrics;
                    var inputChanges = new List<string>();
                    var layerTable = WindowControls(window).OfType<DataGrid>().Single(g => g.Name == "CoatingLayerTable");
                    layerTable.CellEditEnded += (_, e) => inputChanges.Add($"CellEditEnded: {e.EditAction}; {e.Column.Header}");
                    var computeClicks = 0;
                    Find<Button>(window, "计算 / 复验").Click += (_, _) => computeClicks++;
                    foreach (var input in WindowControls(window).Concat(window.SettingsContent.GetLogicalDescendants()).OfType<Control>().Distinct())
                        input.PropertyChanged += (_, e) =>
                        {
                            if (input is TextBox or AutoCompleteBox or ComboBox or CheckBox && e.Property.Name is "Text" or "SelectedItem" or "IsChecked")
                                inputChanges.Add($"{input.GetType().Name} {AutomationProperties.GetName(input)} {e.Property.Name}: {e.OldValue} -> {e.NewValue}");
                        };
                    var geometrySettings = WindowControls(window).OfType<Expander>().Single(e => e.Name == "CoatingGeometrySettings");
                    geometrySettings.IsExpanded = true; Tick(window);
                    Find<TextBox>(window, "基板厚度 (mm)").Text = "2";
                    Capture(window, "geometry-expanded-before-compute");
                    Click(window, "计算 / 复验"); await window.PendingOperation; Tick(window);
                    Assert.Equal(1, computeClicks);
                    var fullPlot = WindowControls(window).OfType<SpectrumPlot>().Single();
                    await fullPlot.PendingEvaluation; Tick(window);
                    Capture(window, "geometry-settings-1500");
                    Assert.True(fullPlot.HasData, fullPlot.DisplayError ?? string.Join(" | ", WindowControls(window).OfType<TextBlock>()
                        .Where(t => t.IsEffectivelyVisible).Select(t => t.Text)) + "\nInputs: " + string.Join(" | ", inputChanges.TakeLast(40)));
                    var whole = fullPlot.DisplayData!.Average.Select(s => s.Power.Transmittance).ToArray();
                    Assert.Equal(originalFormal, window.CurrentExperiment.Result!.Metrics);
                    Assert.Equal(2, window.CurrentExperiment.StackGeometry!.SubstrateThicknessMm);
                    Find<CheckBox>(window, "计入基板与背面").IsChecked = false;
                    Click(window, "计算 / 复验"); await window.PendingOperation; Tick(window);
                    Assert.True(fullPlot.DisplayData!.Average.Where((s, i) => Math.Abs(s.Power.Transmittance - whole[i]) > 1e-4).Any());
                    Assert.Equal(originalFormal, window.CurrentExperiment.Result!.Metrics);
                    geometrySettings.IsExpanded = false;
                    Click(window, "示例：demo-hr-qw"); await window.PendingOperation; Tick(window);
                    var grid = WindowControls(window).OfType<DataGrid>().Single(g => g.Name == "CoatingSpectrumResults");
                    WindowControls(window).OfType<Expander>().Single(e => e.Content == grid).IsExpanded = true;
                    Tick(window); Capture(window, "demo-hr-qw-results-1500");
                }
                finally { window.CloseAfterHostApproval(); }
            });
        }
        finally { Environment.SetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SETTINGS_DIRECTORY", previous); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}

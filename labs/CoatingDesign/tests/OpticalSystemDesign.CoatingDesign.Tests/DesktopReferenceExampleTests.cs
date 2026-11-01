using Avalonia.Controls;
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
                        var plot = window.GetLogicalDescendants().OfType<SpectrumPlot>().Single();
                        Assert.True(plot.HasData, plot.DisplayError); Assert.Equal(801, plot.SampleCount);
                        if (preset.Id is "demo-edge" or "demo-bbar-4")
                            Assert.Contains(window.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text?.Contains("域外按参考模型延用端点") == true);
                        Capture(window, preset.Id + "-1500");
                    }
                    Click(window, "示例：demo-hr-qw"); await window.PendingOperation; Tick(window);
                    var grid = window.GetLogicalDescendants().OfType<DataGrid>().Single(g => g.Name == "CoatingSpectrumResults");
                    window.GetLogicalDescendants().OfType<Expander>().Single(e => e.Content == grid).IsExpanded = true;
                    Tick(window); Capture(window, "demo-hr-qw-results-1500");
                }
                finally { window.CloseAfterHostApproval(); }
            });
        }
        finally { Environment.SetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SETTINGS_DIRECTORY", previous); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}

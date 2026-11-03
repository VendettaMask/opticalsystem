using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Laboratories.CoatingDesign;
using OpticalSystemDesign.CoatingDesign.Engine;
using OpticalSystemDesign.Core.Materials;

namespace OpticalSystemDesign.CoatingDesign.Tests;

public sealed partial class DesktopTests
{
    [Fact]
    public async Task PhysicalAndOpticalThicknessFollowReferenceAndMaterialWithoutChangingStoredPrescription()
    {
        var directory = Path.Combine(Path.GetTempPath(), "coating-thickness-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var constant = MaterialLibrary.Capture(new ConstantIndexMaterial("lossy constant", 2, .03), "analytical test", 400, 700);
            var cauchy = MaterialLibrary.Capture(new CauchyMaterial("dispersive", 1.5, .01), "analytical test", 400, 700);
            var example = Examples.Create(DesignKind.Antireflection);
            var experiment = example with
            {
                Target = example.Target with { CenterNm = 500 },
                Materials = [.. example.Materials, constant, cauchy],
                Layers = [new(constant.Id, 80.123456789), new(cauchy.Id, 100.25)]
            };
            var path = Path.Combine(directory, "thickness.coating.json");
            await ExperimentStore.SaveAsync(experiment, path);
            using var host = new Host();
            await host.Run(async () =>
            {
                var window = new MainWindow(new LaboratoryFileDialogs(() => Task.FromResult<string?>(path),
                    () => Task.FromResult<string?>(path), () => Task.FromResult<string?>(null)))
                { Width = 1500, Height = 960, ChooseUnsavedChanges = DiscardChanges };
                try
                {
                    window.Show(); Tick(window); Click(window, "打开实验"); await window.PendingOperation; Tick(window);
                    var table = WindowControls(window).OfType<DataGrid>().Single(g => g.Name == "CoatingLayerTable");
                    var rows = PhysicalRows(table);
                    Assert.Equal(160.246913578, rows[0].OpticalThicknessNm!.Value, 10);
                    Assert.Equal(154.385, rows[1].OpticalThicknessNm!.Value, 10);
                    Assert.Equal(experiment.Layers, rows.Select(r => r.ToLayer()));
                    Assert.Equal(experiment.Fingerprint(), window.CurrentExperiment.Fingerprint());
                    var originalText = rows[0].ThicknessText;
                    rows[0].ThicknessText = "120"; Tick(window);
                    Assert.Equal(240, rows[0].OpticalThicknessNm);
                    rows[0].ThicknessText = "invalid"; Tick(window);
                    Assert.Null(rows[0].OpticalThicknessNm); Assert.Equal("—", rows[0].OpticalThicknessText);
                    Assert.Throws<ArgumentException>(() => rows[0].ToLayer());
                    rows[0].ThicknessText = originalText;
                    rows[1].MaterialId = constant.Id; Tick(window);
                    Assert.Equal(200.5, rows[1].OpticalThicknessNm);
                    rows[1].MaterialId = cauchy.Id;
                    var reference = Find<TextBox>(window, "参考波长 (nm)");
                    reference.Text = "625"; Tick(window);
                    Assert.Equal(152.9414, rows[1].OpticalThicknessNm!.Value, 10);
                    Assert.Contains("625", rows[1].OpticalThicknessHelp);
                    reference.Text = "750"; Tick(window);
                    Assert.All(rows, r => Assert.Null(r.OpticalThicknessNm));
                    Assert.Contains("有效范围", rows[1].OpticalThicknessHelp);
                    reference.Text = "invalid"; Tick(window);
                    Assert.All(rows, r => Assert.Null(r.OpticalThicknessNm));
                    reference.Text = "500"; Tick(window);
                    Assert.Equal(154.385, rows[1].OpticalThicknessNm!.Value, 10);
                    Assert.Equal(experiment.Layers, rows.Select(r => r.ToLayer()));
                    Assert.Equal(experiment.Fingerprint(), window.CurrentExperiment.Fingerprint());
                    foreach (var row in rows)
                        Assert.Contains(table.GetVisualDescendants().OfType<TextBlock>(), c => ReferenceEquals(c.DataContext, row)
                            && c.Text == row.OpticalThicknessText);
                    Capture(window, "physical-optical-thickness-1500");
                    Click(window, "保存实验"); await window.PendingOperation;
                    var saved = await ExperimentStore.OpenAsync(path);
                    Assert.Equal(experiment.Layers, saved.Layers); Assert.Equal(experiment.Fingerprint(), saved.Fingerprint());
                    Assert.DoesNotContain("OpticalThickness", await File.ReadAllTextAsync(path));
                }
                finally { window.CloseAfterHostApproval(); }
            });
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}

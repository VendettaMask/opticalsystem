using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Headless;
using OpticalSystemDesign.App.Laboratories.CoatingDesign;
using OpticalSystemDesign.CoatingDesign.Engine;
using OpticalSystemDesign.Core.Materials;

namespace OpticalSystemDesign.CoatingDesign.Tests;

public sealed class LayerEditingTests
{
    [Fact]
    public void FourThicknessUnitsShareOneFullPrecisionPhysicalValueAndRejectInvalidConversion()
    {
        var row = new LayerRow(new("n2", 100.123456789));
        row.UpdateOpticalThickness(2, 500, null);
        Assert.Equal(200.246913578, row.OpticalThicknessNm!.Value, 11);
        var original = row.ToLayer(); row.QuarterWaveText = row.QuarterWaveText;
        Assert.Equal(original, row.ToLayer());
        row.OpticalThicknessText = "300"; row.UpdateOpticalThickness(2, 500, null);
        Assert.Equal(150, row.ToLayer().ThicknessNm); Assert.Equal(300, row.OpticalThicknessNm);
        row.QuarterWaveText = "1"; row.UpdateOpticalThickness(2, 500, null);
        Assert.Equal(62.5, row.ToLayer().ThicknessNm);
        row.FullWaveText = "0.5"; row.UpdateOpticalThickness(2, 500, null);
        Assert.Equal(125, row.ToLayer().ThicknessNm);
        row.FullWaveText = "invalid"; Assert.Throws<ArgumentException>(() => row.ToLayer());
        row.ThicknessText = "80"; row.UpdateOpticalThickness(null, 300, "outside data");
        row.QuarterWaveText = "1"; Assert.Throws<ArgumentException>(() => row.ToLayer());
        Assert.Equal("1", row.QuarterWaveText);
    }
}

public sealed partial class DesktopTests
{
    [Fact]
    public async Task InvalidTextAndNonContiguousMultiRowEditsHaveAtomicDesignHistory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "layer-history-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "history.coating.json");
        var original = DuplexCoatingTests.Design();
        var other = MaterialLibrary.Capture(new ConstantIndexMaterial("second film", 1.8), "analytic history control", 400, 800);
        original = original with
        {
            Materials = [.. original.Materials, other],
            Layers = [original.Layers[0] with { ThicknessNm = 50 },
            new(other.Id, 60), original.Layers[0] with { ThicknessNm = 70 }, new(other.Id, 80)]
        };
        await ExperimentStore.SaveAsync(original, path);
        try
        {
            using var host = new Host();
            await host.Run(async () =>
            {
                var window = new MainWindow(new LaboratoryFileDialogs(() => Task.FromResult<string?>(path), () => Task.FromResult<string?>(path), () => Task.FromResult<string?>(null)))
                { Width = 1650, Height = 1050, ChooseUnsavedChanges = DiscardChanges };
                try
                {
                    window.Show(); Tick(window); Click(window, "打开实验"); await window.PendingOperation; Tick(window);
                    var table = WindowControls(window).OfType<DataGrid>().Single(t => t.Name == "CoatingLayerTable");
                    LayerRow[] Rows() => PhysicalRows(table);
                    Rows()[0].ThicknessText = "invalid"; Tick(window); Assert.Throws<ArgumentException>(() => Rows()[0].ToLayer());
                    Click(window, "撤销"); await window.PendingOperation; Assert.Equal(50, Rows()[0].ToLayer().ThicknessNm);
                    Click(window, "重做"); await window.PendingOperation; Assert.Equal("invalid", Rows()[0].ThicknessText);
                    Rows()[0].ThicknessText = "55"; Tick(window); Assert.False(window.CanRedoEditor);
                    void Select(params int[] indices) { table.SelectedItems.Clear(); foreach (var i in indices) table.SelectedItems.Add(Rows()[i]); Tick(window); }
                    Select(0, 2); Click(window, "复制"); await window.PendingOperation; Assert.Equal(6, Rows().Length);
                    Click(window, "撤销"); await window.PendingOperation; Assert.Equal(4, Rows().Length);
                    Select(0, 2); Click(window, "删除"); await window.PendingOperation; Assert.Equal(new double[] { 60, 80 }, Rows().Select(r => r.ToLayer().ThicknessNm));
                    Click(window, "撤销"); await window.PendingOperation;
                    Select(0, 2); Click(window, "所选上移"); await window.PendingOperation;
                    Assert.Equal(new double[] { 60, 55, 80, 70 }, Rows().Select(r => r.ToLayer().ThicknessNm));
                    Click(window, "撤销"); await window.PendingOperation;
                    Select(1, 3); Click(window, "所选下移"); await window.PendingOperation;
                    Assert.Equal(new double[] { 60, 55, 80, 70 }, Rows().Select(r => r.ToLayer().ThicknessNm));
                    Click(window, "撤销"); await window.PendingOperation;
                    Select(0, 2); Click(window, "反转层序"); await window.PendingOperation;
                    Assert.Equal(new double[] { 70, 60, 55, 80 }, Rows().Select(r => r.ToLayer().ThicknessNm));
                    Click(window, "撤销"); await window.PendingOperation;
                    Select(0, 1); Find<TextBox>(window, "批量厚度值").Text = "1"; Find<ComboBox>(window, "批量厚度单位").SelectedIndex = 2;
                    Click(window, "设定所选厚度"); await window.PendingOperation;
                    Assert.Equal(550 / (4 * Math.Sqrt(1.5)), Rows()[0].ToLayer().ThicknessNm, 10);
                    Assert.Equal(550 / (4 * 1.8), Rows()[1].ToLayer().ThicknessNm, 10);
                    Click(window, "撤销"); await window.PendingOperation;
                    Assert.Equal(new double[] { 55, 60, 70, 80 }, Rows().Select(r => r.ToLayer().ThicknessNm));
                }
                finally { window.Close(); }
            });
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task MultiSelectionBatchAndHistoryRestoreWholeEditsAndNeverPartiallyApplyInvalidThickness()
    {
        using var host = new Host();
        await host.Run(async () =>
        {
            var window = new MainWindow { Width = 1600, Height = 1000, ChooseUnsavedChanges = DiscardChanges };
            try
            {
                window.Show(); Tick(window);
                Click(window, "添加层"); await window.PendingOperation; Tick(window);
                Click(window, "添加层"); await window.PendingOperation; Tick(window);
                var table = WindowControls(window).OfType<DataGrid>().Single(t => t.Name == "CoatingLayerTable");
                var rows = PhysicalRows(table);
                Assert.Equal(2, rows.Length);
                table.SelectedItems.Add(rows[0]); table.SelectedItems.Add(rows[1]); Tick(window);
                Find<TextBox>(window, "批量厚度值").Text = "2";
                Click(window, "所选厚度乘倍数"); await window.PendingOperation; Tick(window);
                Assert.All(rows, row => Assert.Equal(200, row.ToLayer().ThicknessNm));
                Click(window, "撤销"); await window.PendingOperation; Tick(window);
                rows = PhysicalRows(table);
                Assert.All(rows, row => Assert.Equal(100, row.ToLayer().ThicknessNm));
                Click(window, "重做"); await window.PendingOperation; Tick(window);
                rows = PhysicalRows(table);
                Assert.All(rows, row => Assert.Equal(200, row.ToLayer().ThicknessNm));
                Click(window, "固定所选"); await window.PendingOperation; Tick(window);
                Assert.All(rows, row => Assert.False(row.Variable));
                Click(window, "撤销"); await window.PendingOperation; Tick(window);
                rows = PhysicalRows(table); Assert.All(rows, row => Assert.True(row.Variable));
                Find<TextBox>(window, "批量厚度值").Text = "1000";
                Click(window, "所选厚度乘倍数"); await window.PendingOperation; Tick(window);
                Assert.All(rows, row => Assert.Equal(200, row.ToLayer().ThicknessNm));
                Assert.Null(window.CurrentExperiment.Result);
                Capture(window, "coating-linked-thickness-history");
            }
            finally { window.Close(); }
        });
    }
}

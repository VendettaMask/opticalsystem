using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using UnsavedChangesChoice = OpticalSystemDesign.App.UnsavedChangesChoice;
using OpticalSystemDesign.App.Laboratories.CoatingDesign;
using OpticalSystemDesign.CoatingDesign.Engine;

namespace OpticalSystemDesign.CoatingDesign.Tests;

public sealed partial class DesktopTests
{
    [Fact]
    public async Task ApprovedCloseDrainsAnInFlightRecoveryWriteWithoutBlockingTheUiThread()
    {
        using var host = new Host();
        await host.Run(async () =>
        {
            var window = new MainWindow { ChooseUnsavedChanges = DiscardChanges };
            window.Show(); Tick(window);
            Find<TextBox>(window, "实验名称").Text = new string('x', 60000);
            var write = window.FlushRecoveryAsync(); var path = window.RecoveryPath!;
            window.CloseAfterHostApproval();
            await write; Assert.False(File.Exists(path));
        });
    }
    private static void PointerClick(Control control)
    {
        var top = Assert.IsAssignableFrom<Window>(TopLevel.GetTopLevel(control)); control.BringIntoView(); Tick(top);
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), top)!.Value;
        top.MouseMove(point); Tick(top); top.MouseDown(point, MouseButton.Left); Tick(top); top.MouseUp(point, MouseButton.Left); Tick(top);
    }
    [Fact]
    public async Task FormulaPreviewClipboardMultiSelectionAndIndependentReferenceHaveAtomicHistory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "desktop-workflow-" + Guid.NewGuid()); Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "workflow.coating.json"); var original = DuplexCoatingTests.Design(); await ExperimentStore.SaveAsync(original, path);
        try
        {
            using var host = new Host();
            await host.Run(async () =>
            {
                var window = new MainWindow(new(() => Task.FromResult<string?>(path), () => Task.FromResult<string?>(path), () => Task.FromResult<string?>(null)))
                { Width = 1800, Height = 1100, ChooseUnsavedChanges = DiscardChanges };
                try
                {
                    window.Show(); Tick(window); Click(window, "打开实验"); await window.PendingOperation; Tick(window);
                    var table = WindowControls(window).OfType<DataGrid>().Single(t => t.Name == "CoatingLayerTable");
                    LayerRow[] Rows() => PhysicalRows(table);
                    Find<TextBox>(window, "参考波长 (nm)").Text = "625"; Tick(window);
                    Assert.Equal(550, window.CurrentExperiment.Target.CenterNm); Assert.Equal(625, window.CurrentExperiment.ReferenceWavelengthNm);
                    Assert.Equal(original.Layers, Rows().Select(r => r.ToLayer()));
                    Find<TextBox>(window, "中心波长 (nm)").Text = "555"; Tick(window);
                    Assert.Equal("625", Find<TextBox>(window, "参考波长 (nm)").Text);
                    Assert.Equal(original.Layers, Rows().Select(r => r.ToLayer()));
                    Find<TextBox>(window, "中心波长 (nm)").Text = "550";
                    Click(window, "堆栈公式"); Tick(window);
                    var dialog = window.OwnedWindows.Single(w => w.Name == "CoatingFormulaWindow"); Tick(dialog);
                    var formula = Find<TextBox>(dialog, "膜系公式"); formula.Focus(); formula.SelectAll(); dialog.KeyTextInput("(HL)^2");
                    Click(dialog, "预览膜系公式"); Tick(dialog); Capture(dialog, "formula-preview");
                    Click(dialog, "应用膜系公式"); await window.PendingOperation; Tick(window);
                    Assert.Equal(4, Rows().Length); var expected = CoatingStackFormula.Parse(window.CurrentExperiment with { ReferenceWavelengthNm = 625 }, false, "(HL)^2").Layers;
                    Assert.Equal(expected, Rows().Select(r => r.ToLayer()));
                    Click(window, "撤销"); await window.PendingOperation; Assert.Single(Rows());
                    Click(window, "重做"); await window.PendingOperation; Assert.Equal(4, Rows().Length);
                    table.SelectedItems.Clear(); table.SelectedItems.Add(Rows()[0]); table.SelectedItems.Add(Rows()[2]);
                    Click(window, "复制到剪贴板"); await window.PendingOperation;
                    Click(window, "粘贴膜层"); await window.PendingOperation; Tick(window); Assert.Equal(6, Rows().Length);
                    Assert.Equal(expected[0], Rows()[3].ToLayer()); Assert.Equal(expected[2], Rows()[4].ToLayer());
                    Click(window, "撤销"); await window.PendingOperation; Assert.Equal(4, Rows().Length);
                    table.SelectedItems.Clear(); table.SelectedItems.Add(Rows()[1]);
                    Click(window, "剪切膜层"); await window.PendingOperation; Assert.Equal(3, Rows().Length);
                    Click(window, "撤销"); await window.PendingOperation; Assert.Equal(expected, Rows().Select(r => r.ToLayer()));
                    Click(window, "保存实验"); await window.PendingOperation; var saved = await ExperimentStore.OpenAsync(path);
                    Assert.Equal(625, saved.ReferenceWavelengthNm); Assert.Equal(550, saved.Target.CenterNm); Assert.Equal(expected, saved.Layers);
                    Assert.Equal(original.BackLayers, saved.BackLayers); Capture(window, "workflow-editor");
                }
                finally { window.CloseAfterHostApproval(); }
            });
        }
        finally { Directory.Delete(dir, true); }
    }
    [Fact]
    public async Task RecoveryRestoresRawBothFaceEditsAndCancelCloseKeepsDraftWithoutOverwritingOriginal()
    {
        var dir = Path.Combine(Path.GetTempPath(), "desktop-recovery-" + Guid.NewGuid()); Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "original.coating.json"); var original = DuplexCoatingTests.Design(); await ExperimentStore.SaveAsync(original, path);
        var originalBytes = await File.ReadAllBytesAsync(path);
        try
        {
            using var host = new Host();
            await host.Run(async () =>
            {
                var dialogs = new LaboratoryFileDialogs(() => Task.FromResult<string?>(path), () => Task.FromResult<string?>(path), () => Task.FromResult<string?>(null));
                string draftPath, draft;
                var first = new MainWindow(dialogs) { Width = 1600, Height = 1050, ChooseUnsavedChanges = DiscardChanges };
                try
                {
                    first.Show(); Tick(first); Click(first, "打开实验"); await first.PendingOperation; Tick(first);
                    Find<TextBox>(first, "参考波长 (nm)").Text = "625";
                    var table = WindowControls(first).OfType<DataGrid>().Single(t => t.Name == "CoatingLayerTable");
                    ((IEnumerable<LayerRow>)table.ItemsSource).Single().ThicknessText = "unfinished.";
                    var tabs = WindowControls(first).OfType<TabControl>().Single(t => t.Name == "CoatingFilmSideTabs"); PointerClick((Control)tabs.Items[1]!); Tick(first);
                    ((IEnumerable<LayerRow>)table.ItemsSource).Single().QuarterWaveText = "partial";
                    await first.FlushRecoveryAsync(); draftPath = first.RecoveryPath!; draft = await File.ReadAllTextAsync(draftPath);
                }
                finally { first.CloseAfterHostApproval(); }
                // Reinstall the exact persisted crash draft. A separate process-kill test exercises actual process death.
                await File.WriteAllTextAsync(draftPath, draft);
                var choice = UnsavedChangesChoice.Cancel;
                var second = new MainWindow(dialogs) { Width = 1600, Height = 1050, ChooseUnsavedChanges = _ => Task.FromResult(choice) };
                try
                {
                    second.Show(); Tick(second);
                    var recovery = second.OwnedWindows.Single(w => w.Name == "CoatingRecoveryWindow"); Tick(recovery); Capture(recovery, "recovery-choice");
                    Click(recovery, "恢复草稿"); await second.PendingOperation; Tick(second);
                    Assert.False(File.Exists(draftPath)); Assert.True(File.Exists(second.RecoveryPath));
                    Assert.Equal("625", Find<TextBox>(second, "参考波长 (nm)").Text);
                    var table = WindowControls(second).OfType<DataGrid>().Single(t => t.Name == "CoatingLayerTable");
                    Assert.Equal("partial", ((IEnumerable<LayerRow>)table.ItemsSource).Single().QuarterWaveText);
                    var tabs = WindowControls(second).OfType<TabControl>().Single(t => t.Name == "CoatingFilmSideTabs"); PointerClick((Control)tabs.Items[0]!); Tick(second);
                    Assert.Equal("unfinished.", ((IEnumerable<LayerRow>)table.ItemsSource).Single().ThicknessText);
                    Assert.False(await second.PrepareForHostCloseAsync()); Assert.True(File.Exists(second.RecoveryPath));
                    Assert.True(second.HasUnsavedChanges); Assert.Equal(originalBytes, await File.ReadAllBytesAsync(path));
                    choice = UnsavedChangesChoice.Discard; Assert.True(await second.PrepareForHostCloseAsync());
                    Assert.False(File.Exists(second.RecoveryPath)); Assert.Equal(originalBytes, await File.ReadAllBytesAsync(path));
                }
                finally { second.CloseAfterHostApproval(); }
            });
        }
        finally { Directory.Delete(dir, true); }
    }
}

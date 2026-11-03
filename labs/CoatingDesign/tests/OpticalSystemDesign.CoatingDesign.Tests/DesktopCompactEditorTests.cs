using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Laboratories.CoatingDesign;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.CoatingDesign.Engine;

namespace OpticalSystemDesign.CoatingDesign.Tests;

public sealed partial class DesktopTests
{
    private static LayerRow[] DisplayRows(DataGrid table) => ((IEnumerable<LayerRow>)table.ItemsSource).ToArray();
    [Theory]
    [InlineData("Light", 1440, 22)]
    [InlineData("Dark", 1440, 22)]
    [InlineData("Light", 820, 18)]
    [InlineData("Light", 1200, 13)]
    public async Task ReferenceEditorKeepsInlineMaterialsRowToolsAndPhysicalPrescription(string theme, int width, double fontSize)
    {
        var dir = Path.Combine(Path.GetTempPath(), "coating-reference-ui-" + Guid.NewGuid()); Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "input.coating.json"); var original = ReferenceExamples.Create("demo-edge");
        await ExperimentStore.SaveAsync(original, path);
        try
        {
            using var host = new Host();
            await host.Run(async () =>
            {
                DisplayTypography.Configure(new AppSettings { Theme = theme, FontSize = fontSize });
                Assert.IsType<global::OpticalSystemDesign.App.App>(Avalonia.Application.Current).ApplyTheme(theme);
                var window = new MainWindow(new(() => Task.FromResult<string?>(path), () => Task.FromResult<string?>(path), () => Task.FromResult<string?>(null)))
                { Width = width, Height = 960, ChooseUnsavedChanges = DiscardChanges };
                try
                {
                    window.Show(); Tick(window); Click(window, "打开实验"); await window.PendingOperation; Tick(window);
                    var table = WindowControls(window).OfType<DataGrid>().Single(t => t.Name == "CoatingLayerTable");
                    var card = window.GetVisualDescendants().OfType<Border>().Single(c => c.Name == "CoatingLayerCard");
                    var header = card.GetVisualDescendants().OfType<StackPanel>().Single(c => c.Name == "CoatingCompactEditorHeader");
                    var toolbar = header.GetVisualDescendants().OfType<StackPanel>().Single(c => c.Name == "CoatingLayerToolbar");
                    Assert.Equal(5, toolbar.Children.OfType<Button>().Count());
                    Assert.True(table.Bounds.Height >= card.Bounds.Height * .55, $"{table.Bounds} / {card.Bounds}");
                    Assert.True(header.Bounds.Height <= Math.Max(160, fontSize * 8), header.Bounds.ToString());
                    Assert.Equal(original.Layers, PhysicalRows(table).Select(r => r.ToLayer()));
                    Assert.Equal(original.Layers.Reverse(), DisplayRows(table).Select(r => r.ToLayer()));
                    Assert.Equal(1, DisplayRows(table)[0].Number);
                    Assert.Equal(original.Fingerprint(), window.CurrentExperiment.Fingerprint());
                    Assert.True(Find<CheckBox>(window, "忽略另一面").IsChecked);
                    Assert.DoesNotContain(header.GetVisualDescendants().OfType<TextBlock>(), t => t.Text is "优化" or "评价" || t.Text?.Contains("解析导数") == true);
                    Capture(window, $"reference-{theme}-{width}-{fontSize}");
                    var reverse = toolbar.Children.OfType<Button>().Single(b => AutomationProperties.GetName(b) == "反转层序");
                    PointerClick(reverse); await window.PendingOperation; Assert.Equal(original.Layers.Reverse(), PhysicalRows(table).Select(r => r.ToLayer()));
                    Click(window, "撤销"); await window.PendingOperation;
                    Click(window, "全部锁定"); await window.PendingOperation; Assert.All(PhysicalRows(table), r => Assert.False(r.Variable));
                    Click(window, "撤销"); await window.PendingOperation; Assert.Equal(original.Layers, PhysicalRows(table).Select(r => r.ToLayer()));
                    Click(window, "复制到另一面"); await window.PendingOperation;
                    Find<CheckBox>(window, "忽略另一面").IsChecked = false; Tick(window);
                    Click(window, "保存实验"); await window.PendingOperation; var saved = await ExperimentStore.OpenAsync(path);
                    Assert.Equal(original.Layers, saved.Layers); Assert.Equal(original.Layers, saved.BackLayers);
                    Assert.Equal(CoatingEvaluationSurface.WholePlate, saved.Duplex!.Evaluate);
                    var mode = Find<ComboBox>(window, "表面模式"); mode.SelectedIndex = 3; Tick(window);
                    Assert.True(Find<CheckBox>(window, "对称双面").IsChecked);
                    Assert.False(Find<CheckBox>(window, "忽略另一面").IsEnabled);
                }
                finally { window.CloseAfterHostApproval(); DisplayTypography.Configure(new AppSettings()); }
            });
        }
        finally { Directory.Delete(dir, true); }
    }
    [Fact]
    public async Task ReferenceFloatingEditorUsesRealInlineMaterialLockMoveDeleteAndMenuHistory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "coating-row-tools-" + Guid.NewGuid()); Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "input.coating.json"); var original = ReferenceExamples.Create("demo-hr-qw");
        await ExperimentStore.SaveAsync(original, path);
        try
        {
            using var host = new Host();
            await host.Run(async () =>
            {
                DisplayTypography.Configure(new AppSettings { Theme = "Light", FontSize = 22 });
                Assert.IsType<global::OpticalSystemDesign.App.App>(Avalonia.Application.Current).ApplyTheme("Light");
                var window = new MainWindow(new(() => Task.FromResult<string?>(null), () => Task.FromResult<string?>(path), () => Task.FromResult<string?>(null)))
                { Width = 1440, Height = 1000, ChooseUnsavedChanges = DiscardChanges };
                try
                {
                    window.Show(); Tick(window); Click(window, "打开实验"); await window.PendingOperation;
                    var table = WindowControls(window).OfType<DataGrid>().Single(t => t.Name == "CoatingLayerTable");
                    window.DockWorkspace.DockFactory.FloatDockable(window.DockWorkspace.DockFactory.Pages[CoatingDockWorkspace.EditorId]); Tick(window);
                    var floating = Assert.IsAssignableFrom<Window>(TopLevel.GetTopLevel(table)); floating.Width = 1280; floating.Height = 900; Tick(floating);
                    var row = DisplayRows(table)[0];
                    var picker = table.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "CoatingRowMaterial" && ReferenceEquals(c.DataContext, row) && c.IsEffectivelyVisible && c.Bounds.Width > 0);
                    Assert.Equal(22, picker.FontSize); PointerClick(picker); Tick(floating);
                    var key = picker.SelectedIndex == 0 ? Key.End : Key.Home;
                    floating.KeyPress(key, RawInputModifiers.None, key == Key.End ? PhysicalKey.End : PhysicalKey.Home, null);
                    floating.KeyRelease(key, RawInputModifiers.None, key == Key.End ? PhysicalKey.End : PhysicalKey.Home, null);
                    floating.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); floating.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                    await window.PendingOperation; Tick(floating);
                    Assert.NotEqual(original.Layers[^1].MaterialId, row.MaterialId); Assert.Equal(original.Layers[^1].ThicknessNm, row.ToLayer().ThicknessNm);
                    floating.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, "z"); floating.KeyRelease(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, "z"); Tick(floating);
                    row = DisplayRows(table)[0];
                    var locked = table.GetVisualDescendants().OfType<ToggleButton>().Single(c => c.Name == "CoatingLayerLock" && ReferenceEquals(c.DataContext, row));
                    PointerClick(locked); Assert.False(row.Variable);
                    floating.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, "z"); floating.KeyRelease(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, "z"); Tick(floating);
                    row = DisplayRows(table)[1];
                    var up = table.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "膜层上移" && ReferenceEquals(b.DataContext, row));
                    PointerClick(up); await window.PendingOperation; Tick(floating); Assert.Same(row, DisplayRows(table)[0]);
                    floating.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, "z"); floating.KeyRelease(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, "z"); Tick(floating);
                    row = DisplayRows(table)[0]; var remove = table.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "删除膜层" && ReferenceEquals(b.DataContext, row));
                    PointerClick(remove); await window.PendingOperation; Assert.Equal(original.Layers.Length - 1, DisplayRows(table).Length);
                    floating.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, "z"); floating.KeyRelease(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, "z"); Tick(floating);
                    Assert.Equal(original.Layers, PhysicalRows(table).Select(r => r.ToLayer()));
                    Capture(floating, "reference-floating-22");
                    var tools = Find<DropDownButton>(window, "膜层工具"); PointerClick(tools); Tick(floating);
                    var menu = Assert.IsType<MenuFlyout>(tools.Flyout); Assert.True(menu.IsOpen); Capture(floating, "reference-floating-tools-22"); menu.Hide();
                }
                finally { window.CloseAfterHostApproval(); DisplayTypography.Configure(new AppSettings()); }
            });
        }
        finally { Directory.Delete(dir, true); }
    }
}

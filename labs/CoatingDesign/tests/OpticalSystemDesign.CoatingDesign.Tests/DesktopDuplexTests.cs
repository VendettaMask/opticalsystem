using Avalonia;
using Avalonia.Controls;
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
    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public async Task BackFilmKeyboardEditingFloatingUndoSymmetryAndPersistenceKeepOneEvaluationObject(string theme)
    {
        var directory = Path.Combine(Path.GetTempPath(), "desktop-duplex-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "two-sided.coating.json");
        var original = DuplexCoatingTests.Design(); await ExperimentStore.SaveAsync(original, path);
        try
        {
            using var host = new Host();
            await host.Run(async () =>
            {
                DisplayTypography.Configure(new AppSettings { Theme = theme });
                var window = new MainWindow(new LaboratoryFileDialogs(() => Task.FromResult<string?>(path), () => Task.FromResult<string?>(path), () => Task.FromResult<string?>(directory)))
                { Width = 1650, Height = 1050, ChooseUnsavedChanges = DiscardChanges };
                try
                {
                    window.Show(); Tick(window); Click(window, "打开实验"); await window.PendingOperation; Tick(window);
                    var table = WindowControls(window).OfType<DataGrid>().Single(t => t.Name == "CoatingLayerTable");
                    var tabs = WindowControls(window).OfType<TabControl>().Single(t => t.Name == "CoatingFilmSideTabs");
                    PointerClick((Control)tabs.Items[1]!); Tick(window); Assert.Equal(1, tabs.SelectedIndex);
                    var row = ((IEnumerable<LayerRow>)table.ItemsSource).Single(); Assert.Equal(170, row.ToLayer().ThicknessNm);
                    table.ScrollIntoView(row, table.Columns[4]); table.SelectedItem = row; table.CurrentColumn = table.Columns[4];
                    table.BeginEdit(); Tick(window);
                    var edit = table.GetVisualDescendants().OfType<TextBox>().Single(t => t.IsEffectivelyVisible && ReferenceEquals(t.DataContext, row));
                    edit.Focus(); edit.SelectAll(); window.KeyTextInput("1"); window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                    window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); table.CommitEdit(); Tick(window);
                    var expected = 550 / (4 * Math.Sqrt(1.5)); Assert.Equal(expected, row.ToLayer().ThicknessNm, 10);
                    window.DockWorkspace.DockFactory.FloatDockable(window.DockWorkspace.DockFactory.Pages[CoatingDockWorkspace.EditorId]); Tick(window);
                    var floating = Assert.IsAssignableFrom<Window>(TopLevel.GetTopLevel(table)); Tick(floating);
                    floating.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, "z"); floating.KeyRelease(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, "z"); Tick(floating);
                    row = ((IEnumerable<LayerRow>)table.ItemsSource).Single(); Assert.Equal(170, row.ToLayer().ThicknessNm);
                    floating.KeyPress(Key.Y, RawInputModifiers.Control, PhysicalKey.Y, "y"); floating.KeyRelease(Key.Y, RawInputModifiers.Control, PhysicalKey.Y, "y"); Tick(floating);
                    row = ((IEnumerable<LayerRow>)table.ItemsSource).Single(); Assert.Equal(expected, row.ToLayer().ThicknessNm, 10);
                    Click(window, "保存实验"); await window.PendingOperation;
                    var saved = await ExperimentStore.OpenAsync(path); Assert.Equal(original.Layers, saved.Layers); Assert.Equal(expected, saved.BackLayers![0].ThicknessNm, 10);
                    var symmetric = Find<CheckBox>(window, "对称双面"); PointerClick(symmetric); Tick(floating);
                    Assert.True(symmetric.IsChecked); Assert.Equal(0, tabs.SelectedIndex); Assert.False(((TabItem)tabs.Items[1]!).IsEnabled);
                    Click(window, "计算 / 复验"); await window.PendingOperation; Tick(window);
                    Assert.True(window.CurrentExperiment.Duplex!.Symmetric); Assert.Null(window.CurrentExperiment.BackLayers);
                    Assert.Equal(window.CurrentExperiment.Layers, window.CurrentExperiment.EffectiveBackLayers); Assert.NotNull(window.CurrentExperiment.Result);
                    Click(window, "堆栈几何分析"); await window.PendingOperation; Tick(window);
                    var stack = WindowControls(window).OfType<CoatingStackAnalysisView>().Single();
                    Assert.Equal(2, stack.Plot.Bands.Count); Assert.Contains(stack.Plot.Bands, b => b.Layer.Side == "背面");
                    using var image = stack.CaptureImage(); Assert.Equal(2404, image.PixelSize.Width); Assert.Equal(1804, image.PixelSize.Height);
                    window.DockWorkspace.ApplyLayout(CoatingPaneLayout.Columns); Tick(window);
                    Capture(window, "coating-duplex-" + theme);
                    window.DockWorkspace.Open(CoatingDockWorkspace.SpectrumId); Tick(window);
                    Capture(window, "coating-duplex-spectrum-" + theme);
                    Click(window, "打开实验"); await window.PendingOperation; Tick(window);
                    Assert.False(Find<Button>(window, "撤销").IsEnabled);
                    Assert.False(window.CurrentExperiment.Duplex!.Symmetric);
                    Assert.Equal(expected, window.CurrentExperiment.BackLayers![0].ThicknessNm, 10);
                }
                finally { window.Close(); DisplayTypography.Configure(new AppSettings()); }
                void PointerClick(Control control)
                {
                    var top = Assert.IsAssignableFrom<Window>(TopLevel.GetTopLevel(control));
                    control.BringIntoView(); Tick(top);
                    var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), top)!.Value;
                    top.MouseMove(point); Tick(top); top.MouseDown(point, MouseButton.Left); Tick(top); top.MouseUp(point, MouseButton.Left); Tick(top);
                }
            });
        }
        finally { Directory.Delete(directory, true); }
    }
}

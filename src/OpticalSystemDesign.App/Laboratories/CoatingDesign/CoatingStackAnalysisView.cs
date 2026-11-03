using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;

namespace OpticalSystemDesign.App.Laboratories.CoatingDesign;

/// <summary>Retained dockable analysis page. Never edits or recalculates the prescription.</summary>
internal sealed class CoatingStackAnalysisView : UserControl, IDisposable
{
    private readonly ResponsiveAnalysisPlotHost _drawing = new();
    private readonly CoatingStackGeometryPlot _plot = new();
    private readonly DataGrid _layers = new() { Name = "CoatingStackData", AutoGenerateColumns = false, IsReadOnly = true };
    private readonly TextBlock _error = new() { Name = "CoatingStackAnalysisStatus", TextWrapping = TextWrapping.Wrap };
    private readonly Button _png = new() { Content = "PNG", Name = "CoatingStackPng" };
    private readonly TextBlock _selection = new() { Name = "CoatingStackSelection", TextWrapping = TextWrapping.Wrap };
    private CoatingStackSnapshot? _snapshot;
    private bool _synchronizing;
    internal CoatingStackGeometryPlot Plot => _plot;
    internal CoatingStackSnapshot? Snapshot => _snapshot;

    internal CoatingStackAnalysisView()
    {
        Name = "CoatingStackAnalysisView";
        DisplayTypography.Apply(this);
        this.BindThemeResource(BackgroundProperty, ThemeResourceBindings.Workspace);
        this.BindThemeResource(ForegroundProperty, ThemeResourceBindings.TextPrimary);
        _layers.Columns.Add(new DataGridTextColumn { Header = "层号", Binding = new Binding(nameof(CoatingStackLayer.LayerLabel)), Width = new DataGridLength(110) });
        _layers.Columns.Add(new DataGridTextColumn { Header = "材料", Binding = new Binding(nameof(CoatingStackLayer.MaterialName)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _layers.Columns.Add(new DataGridTextColumn { Header = "材料 ID", Binding = new Binding(nameof(CoatingStackLayer.MaterialId)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _layers.Columns.Add(new DataGridTextColumn { Header = "物理厚度", Binding = new Binding(nameof(CoatingStackLayer.PhysicalThickness)), Width = new DataGridLength(200) });
        _layers.SelectionChanged += (_, _) =>
        {
            if (!_synchronizing && _layers.SelectedItem is CoatingStackLayer layer) _plot.Select(layer.Number);
        };
        _plot.SelectionChanged += number =>
        {
            if (_synchronizing) return;
            _synchronizing = true;
            try { _layers.SelectedIndex = number - 1; UpdateSelection(); }
            finally { _synchronizing = false; }
        };
        _png.Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6,
            Children = { new LocalIcon { IconName = "file-export-image", Width = 16, Height = 16 }, new TextBlock { Text = "PNG" } } };
        AutomationProperties.SetName(_png, "导出堆栈几何 PNG");
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(12, 8), Children = { _png, _selection } };
        var tabs = new TabControl { Name = "CoatingStackAnalysisTabs", ItemsSource = new[]
        {
            new TabItem { Header = "绘图", Content = _drawing },
            new TabItem { Header = "膜层数据", Content = _layers }
        }, SelectedIndex = 0 };
        var root = new Grid { RowDefinitions = new("Auto,*,Auto") };
        root.Children.Add(toolbar); Grid.SetRow(tabs, 1); root.Children.Add(tabs);
        _error.Margin = new Thickness(12, 8); Grid.SetRow(_error, 2); root.Children.Add(_error);
        Content = root;
        _png.Click += async (_, _) =>
        {
            try
            {
                using var image = CaptureImage();
                var storage = TopLevel.GetTopLevel(this)?.StorageProvider ?? throw new InvalidOperationException("请先打开堆栈几何面板。");
                var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "导出堆栈几何", SuggestedFileName = "coating-stack.png",
                    DefaultExtension = "png", ShowOverwritePrompt = true, FileTypeChoices = [new FilePickerFileType("PNG") { Patterns = ["*.png"] }] });
                if (file is not null) AnalysisPanel.SavePlotImage(image, file.Path.LocalPath);
            }
            catch (Exception error) { _error.Text = "导出失败：" + error.Message; }
        };
        UiLocalization.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, EventArgs args)
    { if (_snapshot is { } snapshot) SetSnapshot(snapshot); }

    public void Dispose() => UiLocalization.LanguageChanged -= OnLanguageChanged;

    internal void SetSnapshot(CoatingStackSnapshot snapshot)
    {
        _snapshot = snapshot; _synchronizing = true;
        try
        {
            _plot.SetSnapshot(snapshot); _layers.ItemsSource = snapshot.AllLayers.ToArray(); _layers.SelectedIndex = _plot.SelectedLayer - 1;
            var summary = new StackPanel { Spacing = 2, Margin = new Thickness(16, 8) };
            void Line(string value, bool title = false) => summary.Children.Add(new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap,
                FontSize = title ? DisplayTypography.CardTitle : DisplayTypography.Caption, FontWeight = title ? FontWeight.SemiBold : FontWeight.Normal });
            Line(UiLocalization.Text("堆栈几何 · 竖直剖面"), true); Line(DateTimeOffset.Now.ToString("yyyy/MM/dd HH:mm:ss"));
            Line($"{snapshot.Layers.Count + snapshot.BackLayers.Count} {UiLocalization.Text("层")} · {UiLocalization.Text("膜层总厚度")} {snapshot.AllLayers.Sum(l => l.ThicknessNm):G8} nm · {UiLocalization.Text("基板")} {snapshot.SubstrateThicknessMm:G7} mm");
            Line($"{UiLocalization.Text("参考波长")} {snapshot.ReferenceWavelengthNm:G7} nm · {UiLocalization.Text("入射角")} {snapshot.AngleDegrees:G6}° · {UiLocalization.Text("编辑修订")} {snapshot.Revision}");
            var caption = PlotCaption.Create(summary, snapshot.Name, UiLocalization.Text("镀膜实验"), "CoatingStackCompanyLogo");
            // Detach the existing drawing before reusing it in a refreshed fixed source sheet.
            if (_plot.Parent is Panel parent) parent.Children.Remove(_plot);
            _drawing.Child = new UniformDiagramPage(_plot, caption);
            _error.Text = ""; _error.IsVisible = false; UpdateSelection();
            ControlAvailability.Set(_png, true, "导出当前剖面、选中层与完整图签。");
        }
        finally { _synchronizing = false; }
    }
    internal void ShowUnavailable(string reason)
    {
        _snapshot = null; if (_plot.Parent is Panel parent) parent.Children.Remove(_plot);
        _plot.ClearSnapshot(); _drawing.Child = null; _layers.ItemsSource = null; _selection.Text = "";
        _error.Text = "堆栈几何不可用：" + reason; _error.IsVisible = true;
        ControlAvailability.Set(_png, false, "请先修正无效的材料或厚度。");
    }
    private void UpdateSelection()
    {
        var layer = _snapshot?.AllLayers.FirstOrDefault(l => l.Number == _plot.SelectedLayer);
        _selection.Text = layer is null ? "未镀膜基板" : $"第 {layer.LayerLabel} 层 · {layer.MaterialName} · {layer.PhysicalThickness}";
    }
    internal RenderTargetBitmap CaptureImage()
    {
        if (_snapshot is null || _drawing.Child is not UniformDiagramPage page) throw new InvalidOperationException("请先显示有效堆栈。");
        _drawing.UpdateLayout(); page.ValidateForExport();
        return ExportImageFrame.CaptureConfigured(page, this);
    }
}

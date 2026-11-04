using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Core.FileIO;
using DockSide = Avalonia.Controls.Dock;

namespace OpticalSystemDesign.App.Panels;

internal sealed class NonSequentialZemaxImportWindow : Window
{
    private readonly INonSequentialDocumentService _service;
    private readonly string _identity;
    private ZemaxNscImportResult? _result;
    private readonly TextBox _details = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly TextBlock _status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly Button _apply = new() { Content = "应用完整场景", IsEnabled = false };
    public NonSequentialZemaxImportWindow(INonSequentialDocumentService service)
    {
        _service = service; _identity = service.GetDesignSceneIdentity();
        Title = "Zemax NSC 导入检查"; Width = 950; Height = 700; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var open = new Button { Content = "选择 ZMX…" }; var export = new Button { Content = "保存完整导入记录…" };
        open.Click += async (_, _) =>
        {
            try
            {
                var files = await StorageProvider.OpenFilePickerAsync(new() { Title = "选择 Zemax NSC 文本文件", AllowMultiple = false });
                if (files.Count == 0) return; open.IsEnabled = false; _apply.IsEnabled = false; _result = null;
                _result = await _service.InspectZemaxNscAsync(files[0].Path.LocalPath);
                _status.Text = $"{_result.FileName} · {_result.Objects.Count} 个对象 · {_result.Diagnostics.Count(d => d.Blocking)} 个阻断项";
                _details.Text = string.Join(Environment.NewLine, _result.Objects.Select(o => $"对象 {o.Number} / Surface {o.Surface}：{o.Type}，{o.Records.Count} 条原记录，{(o.MappedObject is null ? "几何未映射" : "基本几何已映射")}"))
                    + "\n\n" + string.Join(Environment.NewLine, _result.Diagnostics.Select(d => $"行 {d.Line}，对象 {d.ObjectNumber?.ToString() ?? "全局"} [{d.Code}] {d.Message}"));
                _apply.IsEnabled = _result.CanApply;
            }
            catch (Exception e) { _status.Text = e.Message; }
            finally { open.IsEnabled = true; }
        };
        export.Click += async (_, _) =>
        {
            try
            {
                if (_result is null) throw new InvalidOperationException("请先检查文件。");
                var file = await StorageProvider.SaveFilePickerAsync(new() { Title = "保存完整导入记录", SuggestedFileName = "zemax-nsc-import.json", DefaultExtension = "json" });
                if (file is not null) await File.WriteAllTextAsync(file.Path.LocalPath, JsonSerializer.Serialize(_result, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception e) { _status.Text = e.Message; }
        };
        _apply.Click += (_, _) => { try { if (_result is not null) { _service.ApplyZemaxNscImport(_result, _identity); Close(); } } catch (Exception e) { _status.Text = e.Message; } };
        var root = new DockPanel { Margin = new Thickness(12) }; var commands = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { open, export, _apply } };
        DockPanel.SetDock(commands, DockSide.Top); root.Children.Add(commands);
        var note = new TextBlock { Margin = new Thickness(0, 8), Text = "保留原文件全部字节、对象、参数与外部资产引用。仅当全部语义和资产通过检查时才允许应用；不以占位几何替代未知对象。", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        DockPanel.SetDock(note, DockSide.Top); root.Children.Add(note); DockPanel.SetDock(_status, DockSide.Bottom); root.Children.Add(_status); root.Children.Add(_details);
        Content = root; ThemeChrome.ApplyDialogDecoration(this);
    }
}

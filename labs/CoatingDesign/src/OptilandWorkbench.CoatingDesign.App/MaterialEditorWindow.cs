using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using OptilandWorkbench.App.Services;
using OptilandWorkbench.App.Theming;
using OptilandWorkbench.CoatingDesign.Engine;

namespace OptilandWorkbench.CoatingDesign.App;

public sealed class MaterialEditorWindow : Window
{
    private readonly TextBox _name = new(), _source = new();
    private readonly TextBox _data = new() { AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, MinHeight = 220 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private string? _editingId;
    public MaterialEditorWindow(MaterialSnapshot[] materials, MaterialSnapshot? selected = null)
    {
        Title = "实验材料管理 · n / k 表"; Width = 760; Height = 680; MinWidth = 580; MinHeight = 500;
        FontSize = LaboratoryTypography.Body;
        this.BindThemeResource(BackgroundProperty, ThemeResourceBindings.Surface);
        this.BindThemeResource(ForegroundProperty, ThemeResourceBindings.TextPrimary);
        var selector = new ComboBox { ItemsSource = materials.Select(m => m.Name).ToArray(), HorizontalAlignment = HorizontalAlignment.Stretch };
        selector.SelectionChanged += (_, _) => { if (selector.SelectedIndex >= 0) Populate(materials[selector.SelectedIndex]); };
        var tools = new WrapPanel();
        Button Button(string text, Func<Task> action, bool primary = false)
        {
            var button = new Button { Content = text, Margin = new Thickness(0, 0, 8, 4) };
            AutomationProperties.SetName(button, text);
            if (primary) button.Classes.Add("accent");
            button.Click += async (_, _) => { try { await action(); } catch (Exception e) { _status.Text = e.Message; } };
            tools.Children.Add(button); return button;
        }
        Button("新建 / 示例格式", () => { _editingId = null; _name.Text = "用户材料"; _source.Text = "用户模型示例，非测量数据"; _data.Text = MaterialTable.Example; return Task.CompletedTask; });
        Button("复制材料", () => { _editingId = null; _name.Text += " 副本"; _status.Text = "保存时创建独立材料，不替换原引用。"; return Task.CompletedTask; });
        Button("导入 CSV", async () =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "导入 n/k 表（CSV / TSV）", AllowMultiple = false });
            if (files.FirstOrDefault()?.TryGetLocalPath() is { } path)
            {
                var data = await MaterialTable.ReadAsync(path);
                MaterialTable.Parse(Path.GetFileNameWithoutExtension(path), "用户导入：" + Path.GetFileName(path), data);
                _editingId = null; _name.Text = Path.GetFileNameWithoutExtension(path); _source.Text = "用户导入：" + Path.GetFileName(path); _data.Text = data;
                _status.Text = "数据已校验；请补充来源和适用条件，再保存到实验。";
            }
        });
        Button("导出 CSV", async () =>
        {
            var snapshot = Parse();
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "导出材料表", SuggestedFileName = snapshot.Name + ".csv" });
            if (file?.TryGetLocalPath() is { } path) { await MaterialTable.WriteAsync(snapshot, path); _status.Text = "已导出：" + path; }
        });
        Button("保存到实验", () => { Close(Parse()); return Task.CompletedTask; }, true);
        Button("取消", () => { Close(null); return Task.CompletedTask; });
        var form = new StackPanel
        {
            Spacing = 8,
            Children = {
            new TextBlock { Text = "选择实验材料（修改会使旧计算失效）" }, selector,
            new TextBlock { Text = "材料名称" }, _name, new TextBlock { Text = "数据来源 / 制备条件" }, _source,
            new TextBlock { Text = "表头 wavelength_nm,n,k 或 wavelength_um,n,k；波长严格递增，n > 0，k ≥ 0。解析色散材料只读，复制时须显式提供表格。", TextWrapping = TextWrapping.Wrap }, tools }
        };
        var root = new Grid { Margin = new Thickness(16), RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 8 };
        root.Children.Add(form); Grid.SetRow(_data, 1); root.Children.Add(_data); Grid.SetRow(_status, 2); root.Children.Add(_status);
        ScrollViewer.SetHorizontalScrollBarVisibility(_data, ScrollBarVisibility.Auto);
        AutomationProperties.SetName(_data, "n/k 表格"); AutomationProperties.SetName(_name, "材料名称"); AutomationProperties.SetName(_source, "数据来源");
        Content = root;
        if (selected is not null) selector.SelectedIndex = Array.FindIndex(materials, m => m.Id == selected.Id);
        else { _name.Text = "用户材料"; _source.Text = "用户模型示例，非测量数据"; _data.Text = MaterialTable.Example; }
    }
    private MaterialSnapshot Parse() => MaterialTable.Parse(_name.Text ?? "", _source.Text ?? "", _data.Text ?? "", _editingId);
    private void Populate(MaterialSnapshot material)
    {
        _editingId = material.Id; _name.Text = material.Name; _source.Text = material.Source;
        try { _data.Text = MaterialTable.Export(material); _status.Text = "显示完整原始表格。保存将更新本实验中的材料引用。"; }
        catch (ArgumentException e) { _data.Text = ""; _status.Text = e.Message; }
    }
}

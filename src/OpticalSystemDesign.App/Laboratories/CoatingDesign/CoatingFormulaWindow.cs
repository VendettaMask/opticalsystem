using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.CoatingDesign.Engine;

namespace OpticalSystemDesign.App.Laboratories.CoatingDesign;

internal sealed class CoatingFormulaWindow : Window
{
    internal CoatingFormulaWindow(Experiment basis, bool back)
    {
        Name = "CoatingFormulaWindow"; Title = "堆栈公式"; Width = 820; Height = 700; MinWidth = 620; MinHeight = 480;
        DisplayTypography.Apply(this);
        var formula = new TextBox { Text = "(HL)^4 H", AcceptsReturn = true, MaxLength = 8192, MinHeight = 70 };
        var aliases = new TextBox { PlaceholderText = "M=材料ID；默认H/L取当前高/低折射率材料", MaxLength = 8192 };
        AutomationProperties.SetName(formula, "膜系公式"); AutomationProperties.SetName(aliases, "公式材料符号");
        var summary = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var table = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, MinHeight = 120 };
        table.Columns.Add(new DataGridTextColumn { Header = "层序", Binding = new Binding("Number"), Width = new(65) });
        table.Columns.Add(new DataGridTextColumn { Header = "材料", Binding = new Binding("Material"), Width = new(1, DataGridLengthUnitType.Star) });
        table.Columns.Add(new DataGridTextColumn { Header = "物理厚度 (nm)", Binding = new Binding("ThicknessNm"), Width = new(180) });
        ParsedCoatingFormula? parsed = null;
        var apply = new Button { Content = "应用到当前面", IsEnabled = false }; AutomationProperties.SetName(apply, "应用膜系公式");
        var preview = new Button { Content = "预览展开" }; AutomationProperties.SetName(preview, "预览膜系公式");
        preview.Click += (_, _) =>
        {
            try
            {
                var map = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var entry in (aliases.Text ?? "").Split([';', '；', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var pair = entry.Split('=', 2, StringSplitOptions.TrimEntries);
                    if (pair.Length != 2 || !map.TryAdd(pair[0], pair[1])) throw new ArgumentException("材料符号格式为 M=材料ID，以分号分隔且不能重复。");
                }
                if (map.Count > 64) throw new ArgumentException("最多64个材料符号。");
                parsed = CoatingStackFormula.Parse(basis, back, formula.Text ?? "", map);
                table.ItemsSource = parsed.Layers.Select((l, i) => new { Number = i + 1, Material = basis.Materials.Single(m => m.Id == l.MaterialId).Name, l.ThicknessNm }).ToArray();
                summary.Text = $"{(back ? "背面" : "正面")}，外侧→基板；{parsed.Layers.Length}层，总厚度{parsed.TotalThicknessNm:G12} nm；λ₀={parsed.ReferenceWavelengthNm:G9} nm。"
                    + (parsed.Layers.Length > basis.Target.MaximumLayers ? "\n超过当前设计层数目标；原验收仍会判定该项。" : "")
                    + "\n应用会替换当前面的膜层，可一步撤销；不改变另一面、材料或设计目标。";
                apply.IsEnabled = true;
            }
            catch (Exception error) when (error is ArgumentException or InvalidDataException or NotSupportedException)
            { parsed = null; table.ItemsSource = null; summary.Text = error.Message; apply.IsEnabled = false; }
        };
        formula.TextChanged += (_, _) => { parsed = null; apply.IsEnabled = false; table.ItemsSource = null; summary.Text = "公式已修改，请重新预览。"; };
        aliases.TextChanged += (_, _) => { parsed = null; apply.IsEnabled = false; table.ItemsSource = null; summary.Text = "材料符号已修改，请重新预览。"; };
        apply.Click += (_, _) => { if (parsed is not null) Close(parsed.Layers); };
        var cancel = new Button { Content = "取消" }; cancel.Click += (_, _) => Close(null);
        var header = new StackPanel
        {
            Spacing = 8,
            Children =
        { new TextBlock { Text = "H/L表示当前膜料；数字为参考波长处QW系数。支持(HL)^4、0.5H、{材料ID}及Inc | … | Sub。", TextWrapping = Avalonia.Media.TextWrapping.Wrap }, formula, aliases, preview, summary }
        };
        var root = new Grid { Margin = new Thickness(16), RowDefinitions = new("Auto,*,Auto"), RowSpacing = 10 };
        root.Children.Add(new ScrollViewer { MaxHeight = 290, Content = header }); Grid.SetRow(table, 1); root.Children.Add(table);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { apply, cancel } };
        Grid.SetRow(buttons, 2); root.Children.Add(buttons); Content = root; LaboratoryPresentation.Apply(this);
    }
}

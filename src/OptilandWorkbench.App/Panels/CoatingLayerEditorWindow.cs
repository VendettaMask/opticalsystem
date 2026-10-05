using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Services;
using OptilandWorkbench.Application.Contracts;

namespace OptilandWorkbench.App.Panels;

public sealed class CoatingLayerEditorWindow : Window
{
    private readonly IPrescriptionService _prescription;
    private readonly int _surface;
    private readonly long _revision;
    private readonly StackPanel _layers = new() { Spacing = 12 };
    private readonly List<LayerEditor> _editors = [];
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap };

    public CoatingLayerEditorWindow(IPrescriptionService prescription, IWorkspaceEventStream events, int surface)
    {
        _prescription = prescription; _surface = surface; _revision = events.Revision;
        Title = $"表面 {surface} · 物理膜层";
        Width = 680; Height = 720; MinWidth = 560; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var add = new Button { Content = "添加膜层", Name = "AddCoatingLayer" };
        add.Click += (_, _) => Add(new("Air", 100));
        var apply = new Button { Content = "应用膜层", Name = "ApplyCoatingLayers" };
        apply.Classes.Add("accent");
        apply.Click += (_, _) => Apply();
        var cancel = new Button { Content = "取消" }; cancel.Click += (_, _) => Close();
        var footer = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(0, 12, 0, 0),
            Children =
        {
            _message,
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, apply } }
        }
        };
        var header = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(0, 0, 0, 12),
            Children =
        {
            new TextBlock { Text = "按入射介质 → 基底的顺序排列", FontSize = DisplayTypography.WindowTitle, FontWeight = FontWeight.SemiBold },
            new TextBlock { Text = "倍率调整基础厚度；n / k 偏移叠加到材料色散值。勾选“变量”后参与优化。倍率范围 0–10，调整后 n > 0、k ≥ 0。", TextWrapping = TextWrapping.Wrap },
            new TextBlock { Text = "应用将替换本表面的现有膜层；删除全部层表示无膜层。材料名称需存在于材料库。", TextWrapping = TextWrapping.Wrap },
            add
        }
        };
        var root = new DockPanel { Margin = new Thickness(20) };
        DockPanel.SetDock(header, Avalonia.Controls.Dock.Top); root.Children.Add(header);
        DockPanel.SetDock(footer, Avalonia.Controls.Dock.Bottom); root.Children.Add(footer);
        root.Children.Add(new ScrollViewer { Content = _layers, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        SettingsPanelChrome.ApplyInputStyles(root);
        this.BindThemeResource(BackgroundProperty, ThemeResourceBindings.Surface);
        Content = root;
        foreach (var layer in prescription.GetCoatingLayers(surface)) Add(layer);
    }

    private void Add(CoatingLayerEditDto layer)
    {
        var editor = new LayerEditor(layer);
        editor.Remove.Click += (_, _) => { _editors.Remove(editor); _layers.Children.Remove(editor.Root); Renumber(); };
        _editors.Add(editor); _layers.Children.Add(editor.Root); Renumber();
    }

    private void Renumber()
    {
        for (var index = 0; index < _editors.Count; index++) _editors[index].Title.Text = $"膜层 {index + 1}";
    }

    private void Apply()
    {
        try
        {
            var layers = _editors.Select(editor => editor.Read()).ToArray();
            _prescription.UpdateCoatingLayers(_surface, layers, _revision);
            Close();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            _message.Text = $"未应用：{exception.Message}";
        }
    }

    private sealed class LayerEditor
    {
        public TextBlock Title { get; } = new() { FontWeight = FontWeight.SemiBold };
        public Button Remove { get; } = new() { Content = "删除此层" };
        public Border Root { get; }
        private readonly int? _source;
        private readonly TextBox _material, _thickness, _multiplier, _index, _extinction;
        private readonly CheckBox _mv, _iv, _ev;

        public LayerEditor(CoatingLayerEditDto layer)
        {
            _source = layer.SourceLayer;
            _material = Input("膜层材料", layer.Material);
            _thickness = Input("基础厚度（nm）", Text(layer.ThicknessNanometers));
            _multiplier = Input("厚度倍率", Text(layer.Multiplier));
            _index = Input("折射率偏移", Text(layer.IndexOffset));
            _extinction = Input("消光系数偏移", Text(layer.ExtinctionOffset));
            _mv = Variable("厚度倍率变量", layer.MultiplierVariable);
            _iv = Variable("折射率偏移变量", layer.IndexVariable);
            _ev = Variable("消光系数偏移变量", layer.ExtinctionVariable);
            var heading = new DockPanel(); DockPanel.SetDock(Remove, Avalonia.Controls.Dock.Right); heading.Children.Add(Remove); heading.Children.Add(Title);
            Root = new Border
            {
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12),
                Child = new StackPanel
                {
                    Spacing = 8,
                    Children = { heading, Row("材料", _material), Row("基础厚度 / nm", _thickness),
                    Row("厚度倍率", _multiplier, _mv), Row("折射率偏移", _index, _iv), Row("消光系数偏移", _extinction, _ev) }
                }
            };
            SettingsPanelChrome.ApplySurfaceCardStyle(Root, shadow: false);
        }

        public CoatingLayerEditDto Read() => new(_material.Text?.Trim() ?? "", Number(_thickness), Number(_multiplier),
            Number(_index), Number(_extinction), _mv.IsChecked == true, _iv.IsChecked == true, _ev.IsChecked == true, _source);
        private static string Text(double value) => value.ToString("G17", CultureInfo.InvariantCulture);
        private static double Number(TextBox input) => double.TryParse(input.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            && double.IsFinite(value) ? value : throw new ArgumentException($"{AutomationProperties.GetName(input)}需要有限数值。");
        private static TextBox Input(string name, string value)
        {
            var input = new TextBox { Text = value, HorizontalAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(input, name); return input;
        }
        private static CheckBox Variable(string name, bool value)
        {
            var control = new CheckBox { Content = "变量", IsChecked = value };
            AutomationProperties.SetName(control, name); return control;
        }
        private static Grid Row(string label, Control input, Control? variable = null)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("120,*,80"), ColumnSpacing = 10 };
            grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(input, 1); grid.Children.Add(input);
            if (variable is not null) { Grid.SetColumn(variable, 2); grid.Children.Add(variable); }
            return grid;
        }
    }
}

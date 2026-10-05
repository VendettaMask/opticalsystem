using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Services;
using OptilandWorkbench.Application.Contracts;

namespace OptilandWorkbench.App.Panels;

public sealed class GradientIndexMaterialEditorWindow : Window
{
    private readonly IPrescriptionService _prescription;
    private readonly int _surface;
    private readonly long _revision;
    private readonly IReadOnlyList<GradientIndexProfileOptionDto> _profiles;
    private readonly TextBox _name = Input("GRIN 材料名称", "GRIN");
    private readonly ComboBox _profile = new() { Name = "GradientIndexProfile", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly StackPanel _coefficients = new() { Spacing = 8 };
    private readonly List<CoefficientEditor> _coefficientRows = [];
    private readonly Dictionary<string, TextBox> _integration = new();
    private readonly CheckBox _useDispersion = new() { Content = "使用 Gradient 5 色散", Name = "UseGradient5Dispersion" };
    private readonly StackPanel _dispersion = new() { Spacing = 8 };
    private readonly TextBox _reference = Input("参考波长（nm）", "550");
    private readonly TextBox _minimumWave = Input("最短波长（nm）", "400");
    private readonly TextBox _maximumWave = Input("最长波长（nm）", "700");
    private readonly TextBox[] _k = Enumerable.Range(1, 3).Select(i => Input($"K{i} 系数", "0")).ToArray();
    private readonly TextBox[] _l = Enumerable.Range(1, 3).Select(i => Input($"L{i} 系数", "0")).ToArray();
    private readonly TextBlock _message = new() { Name = "GradientIndexMessage", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _modelNote = new() { TextWrapping = TextWrapping.Wrap };
    private bool _loading;

    public GradientIndexMaterialEditorWindow(IPrescriptionService prescription, IWorkspaceEventStream events, int surface)
    {
        _prescription = prescription; _surface = surface; _revision = events.Revision;
        _profiles = prescription.GetGradientIndexProfiles();
        Title = $"表面 {surface} · GRIN 材料";
        Width = 840; Height = 780; MinWidth = 660; MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _profile.ItemsSource = _profiles.Select(p => p.Name).ToArray();
        AutomationProperties.SetName(_profile, "GRIN 分布类型");
        _profile.SelectionChanged += (_, _) => { if (!_loading) LoadCoefficients(null); UpdateDispersion(); };
        _useDispersion.IsCheckedChanged += (_, _) => UpdateDispersion();
        var basic = new StackPanel
        {
            Spacing = 12,
            Children = { Row("材料名称", _name), Row("分布类型", _profile), _modelNote,
                Note("系数采用镜头长度单位；n0Squared 表示 n²。切换分布会重置系数，点击应用前不改变工程。"),
                _coefficients, Note("勾选变量后填写优化上下限，当前值必须在范围内。仅优化当前配置的已选系数；色散和积分设置保持固定。") }
        };
        _dispersion.Children.Add(Row("参考波长 / nm", _reference));
        _dispersion.Children.Add(Row("最短波长 / nm", _minimumWave));
        _dispersion.Children.Add(Row("最长波长 / nm", _maximumWave));
        _dispersion.Children.Add(Note("每行按 n_ref 的升幂输入 1～8 个系数，以空格分隔。K 三行长度相同，L 三行长度相同；L 采用 μm² 约定。"));
        for (var i = 0; i < 3; i++) _dispersion.Children.Add(Row($"K{i + 1}", _k[i]));
        for (var i = 0; i < 3; i++) _dispersion.Children.Add(Row($"L{i + 1}", _l[i]));
        var dispersionPage = new StackPanel
        {
            Spacing = 12,
            Children = { _useDispersion,
            Note("只有 Gradient 5 可使用此色散模型。未启用时采用无色散分布；不自动读取 SGRIN.DAT。"), _dispersion }
        };
        var integration = BuildIntegration();
        var tabs = new TabControl
        {
            Items = { new TabItem { Header = "分布与变量", Content = Scroll(basic) },
                new TabItem { Header = "色散", Content = Scroll(dispersionPage) },
                new TabItem { Header = "积分精度", Content = Scroll(integration) } }
        };
        var apply = new Button { Content = "应用材料", Name = "ApplyGradientIndexMaterial" }; apply.Classes.Add("accent");
        apply.Click += (_, _) => Apply();
        var cancel = new Button { Content = "取消", Name = "CancelGradientIndexMaterial" }; cancel.Click += (_, _) => Close();
        var footer = new StackPanel
        {
            Margin = new Thickness(0, 12, 0, 0),
            Spacing = 8,
            Children = { _message, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, apply } } }
        };
        var header = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(0, 0, 0, 12),
            Children =
        {
            new TextBlock { Text = $"表面 {surface} 后的梯度材料", FontSize = DisplayTypography.WindowTitle, FontWeight = FontWeight.SemiBold },
            Note("应用会替换该表面后的材料，可撤销。当前桌面入口限共轴、旋转对称和轴上可微的分布；边界面形仍由镜头表设置。独立倾斜矢高项和 LPTD 尚未实现。")
        }
        };
        var root = new DockPanel { Margin = new Thickness(20) };
        DockPanel.SetDock(header, Avalonia.Controls.Dock.Top); root.Children.Add(header);
        DockPanel.SetDock(footer, Avalonia.Controls.Dock.Bottom); root.Children.Add(footer); root.Children.Add(tabs);
        SettingsPanelChrome.ApplyInputStyles(root);
        this.BindThemeResource(BackgroundProperty, ThemeResourceBindings.Surface);
        Content = root;
        Load(prescription.GetGradientIndexMaterial(surface));
    }

    private void Load(GradientIndexMaterialEditDto? material)
    {
        _loading = true;
        _name.Text = material?.Name ?? $"GRIN 表面 {_surface}";
        _profile.SelectedIndex = material is null ? 4 : _profiles.Select(p => p.Kind).ToList().IndexOf(material.Profile);
        if (_profile.SelectedIndex < 0) throw new InvalidOperationException("无法编辑此 GRIN 分布。");
        LoadCoefficients(material?.Coefficients);
        _useDispersion.IsChecked = material?.Dispersion is not null;
        if (material?.Dispersion is { } d)
        {
            _reference.Text = Text(d.ReferenceWavelengthNanometers); _minimumWave.Text = Text(d.MinimumWavelengthNanometers);
            _maximumWave.Text = Text(d.MaximumWavelengthNanometers);
            for (var i = 0; i < 3; i++) { _k[i].Text = string.Join(" ", d.K[i].Select(Text)); _l[i].Text = string.Join(" ", d.L[i].Select(Text)); }
        }
        var settings = material?.Integration ?? new();
        double[] values = [settings.MaximumPathLength, settings.MaximumStep, settings.MinimumStep, settings.PositionTolerance,
            settings.DirectionTolerance, settings.OpticalPathTolerance, settings.RelativeTolerance, settings.SurfaceTolerance, settings.MaximumAttempts];
        foreach (var pair in _integration.Values.Zip(values)) pair.First.Text = Text(pair.Second);
        _loading = false; UpdateDispersion();
    }

    private void LoadCoefficients(IReadOnlyList<GradientIndexCoefficientEditDto>? saved)
    {
        _coefficients.Children.Clear(); _coefficientRows.Clear();
        if (_profile.SelectedIndex < 0) return;
        var profile = _profiles[_profile.SelectedIndex];
        _modelNote.Text = profile.Kind switch
        {
            "gradient1" => "n = n0 + nr2·r² + nr1·r；非零 nr1 在轴上不可微，不能用于轴上追迹/近轴瞄准。",
            "gradient2" => "n² = n0Squared + nr2·r² + … + nr12·r¹²；基准值是折射率的平方。",
            "gradient3" => "n = n0 + nr2·r² + nr4·r⁴ + nr6·r⁶ + nz1·z + nz2·z² + nz3·z³。",
            "gradient4" => "n = n0 + nx1·x + nx2·x² + ny1·y + ny2·y² + nz1·z + nz2·z²；非旋转分布不能用于公共标量瞄准。",
            _ => "n_ref = n0 + nr2·r² + nr4·r⁴ + nz1·z + nz2·z² + nz3·z³ + nz4·z⁴；可选色散。"
        };
        foreach (var option in profile.Coefficients)
        {
            var initial = saved?.Single(c => c.Key == option.Key)
                ?? new(option.Key, option.DefaultValue, Minimum: option.Key is "n0" or "n0Squared" ? .1 : -1, Maximum: option.Key == "n0Squared" ? 16 : 4);
            var editor = new CoefficientEditor(initial, option.VariableDisabledReason); _coefficientRows.Add(editor); _coefficients.Children.Add(editor.Root);
        }
    }

    private Control BuildIntegration()
    {
        var stack = new StackPanel { Spacing = 10, Children = { Note("控制连续光线与近轴积分的计算预算和误差。路径长度与位置误差采用镜头长度单位；此处设置不作为优化变量。") } };
        foreach (var label in new[] { "最大路径长度", "最大步长", "最小步长", "位置绝对误差", "方向绝对误差", "光程绝对误差", "相对误差", "表面交点误差", "最大尝试次数" })
        {
            var input = Input(label, ""); _integration.Add(label, input); stack.Children.Add(Row(label, input));
        }
        return stack;
    }

    private void UpdateDispersion()
    {
        var allowed = _profile.SelectedIndex >= 0 && _profiles[_profile.SelectedIndex].Kind == "gradient5";
        _useDispersion.IsEnabled = allowed;
        var reason = allowed ? null : "当前分布不支持色散；请选择 Gradient 5。";
        ToolTip.SetTip(_useDispersion, reason); AutomationProperties.SetHelpText(_useDispersion, reason ?? "为 Gradient 5 设置显式色散系数。");
        _dispersion.IsVisible = allowed && _useDispersion.IsChecked == true;
    }

    private void Apply()
    {
        try
        {
            var kind = _profiles[_profile.SelectedIndex].Kind;
            var i = _integration.Values.Select(Number).ToArray();
            if (i[8] != Math.Truncate(i[8]) || i[8] < 1 || i[8] > 1000000) throw new ArgumentException("最大尝试次数须为 1～1000000 的整数。");
            GradientIndexDispersionEditDto? dispersion = kind == "gradient5" && _useDispersion.IsChecked == true
                ? new(Number(_reference), Number(_minimumWave), Number(_maximumWave), _k.Select(Coefficients).ToArray(), _l.Select(Coefficients).ToArray()) : null;
            var material = new GradientIndexMaterialEditDto(_name.Text?.Trim() ?? "", kind, _coefficientRows.Select(row => row.Read()).ToArray(), dispersion,
                new(i[0], i[1], i[2], i[3], i[4], i[5], i[6], i[7], (int)i[8]));
            _prescription.UpdateGradientIndexMaterial(_surface, material, _revision); Close();
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or NotSupportedException)
        { _message.Text = $"未应用：{error.Message}"; }
    }

    private static IReadOnlyList<double> Coefficients(TextBox input)
    {
        var parts = (input.Text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 1 or > 8) throw new ArgumentException($"{AutomationProperties.GetName(input)}需输入 1～8 个有限系数。");
        return parts.Select(part => double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)
            ? value : throw new ArgumentException($"{AutomationProperties.GetName(input)}包含非法数值。")).ToArray();
    }
    private static string Text(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static double Number(TextBox input) => double.TryParse(input.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)
        ? value : throw new ArgumentException($"{AutomationProperties.GetName(input)}需要有限数值。");
    private static TextBox Input(string name, string value)
    {
        var input = new TextBox { Text = value, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(input, name); return input;
    }
    private static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap };
    private static ScrollViewer Scroll(Control child)
    {
        // Leave room for overlay scrollbars so editor borders and dropdown arrows stay visible.
        child.Margin = new Thickness(0, 0, 20, 0);
        return new() { Content = child, Margin = new Thickness(0, 12, 0, 0), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
    private static Grid Row(string label, Control editor)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("145,*"), ColumnSpacing = 12 };
        grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }); Grid.SetColumn(editor, 1); grid.Children.Add(editor); return grid;
    }

    private sealed class CoefficientEditor
    {
        internal Border Root { get; }
        private readonly string _key;
        private readonly TextBox _value, _minimum, _maximum;
        private readonly CheckBox _variable;
        internal CoefficientEditor(GradientIndexCoefficientEditDto dto, string? variableDisabledReason)
        {
            _key = dto.Key; _value = Input(dto.Key, Text(dto.Value));
            _minimum = Input(dto.Key + " 下限", Text(dto.Minimum)); _maximum = Input(dto.Key + " 上限", Text(dto.Maximum));
            _variable = new CheckBox { Content = "变量", IsChecked = dto.Variable }; AutomationProperties.SetName(_variable, dto.Key + " 变量");
            _variable.IsEnabled = variableDisabledReason is null;
            ToolTip.SetTip(_variable, variableDisabledReason);
            AutomationProperties.SetHelpText(_variable, variableDisabledReason ?? "设置此系数为当前配置的优化变量。");
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("80,*,75"), ColumnSpacing = 8 };
            row.Children.Add(new TextBlock { Text = dto.Key, VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(_value, 1); row.Children.Add(_value); Grid.SetColumn(_variable, 2); row.Children.Add(_variable);
            var bounds = new Grid { ColumnDefinitions = new ColumnDefinitions("80,*,55,*"), ColumnSpacing = 8, IsVisible = dto.Variable };
            bounds.Children.Add(new TextBlock { Text = "下限", VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(_minimum, 1); bounds.Children.Add(_minimum);
            var upperLabel = new TextBlock { Text = "上限", VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(upperLabel, 2); bounds.Children.Add(upperLabel);
            Grid.SetColumn(_maximum, 3); bounds.Children.Add(_maximum);
            _variable.IsCheckedChanged += (_, _) => bounds.IsVisible = _variable.IsChecked == true;
            Root = new Border { Padding = new Thickness(10), BorderThickness = new Thickness(1), Child = new StackPanel { Spacing = 6, Children = { row, bounds } } };
            SettingsPanelChrome.ApplySurfaceCardStyle(Root, shadow: false);
        }
        internal GradientIndexCoefficientEditDto Read() => new(_key, Number(_value), _variable.IsChecked == true,
            _variable.IsChecked == true ? Number(_minimum) : -1, _variable.IsChecked == true ? Number(_maximum) : 1);
    }
}

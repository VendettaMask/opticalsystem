using System.Globalization;
using Avalonia.Controls;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.App.Panels;

public sealed partial class TolerancingRunWindow
{
    private readonly ComboBox _configurationScope = Picker(0, "当前配置", "选定配置", "全部配置");
    private readonly StackPanel _configurationRows = new() { Spacing = 8 };
    private readonly StackPanel _mceRows = new() { Spacing = 8 };
    private readonly List<ConfigurationControls> _configurationControls = [];
    private readonly List<MceControls> _mceControls = [];
    private IReadOnlyList<MultiConfigurationOperandRowDto> _thicknessRows = [];
    private Control? _jointDetails;

    private bool IsJointScope => _configurationScope.SelectedIndex > 0;

    private void InitializeJointControls(IReadOnlyList<MultiConfigurationRowDto>? configurations,
        IReadOnlyList<MultiConfigurationOperandRowDto>? operands)
    {
        _thicknessRows = (operands ?? []).Where(row => row.Kind == MultiConfigurationParameterKind.Thickness).ToArray();
        foreach (var config in configurations ?? [])
        {
            var select = Check($"配置 {config.Number}", config.Active);
            var limit = Number(0, 0, 1_000_000, .01m);
            var use = Check("覆盖配置验收限值", false);
            var fields = new TextBox { PlaceholderText = "1=0.25, 2=0.20" };
            _configurationRows.Children.Add(Section(config.Name, select, Row(use, limit),
                Row("逐视场下限（视场=数值）", fields)));
            _configurationControls.Add(new(config.Number, config.Active, select, use, limit, fields));
            use.IsCheckedChanged += (_, _) => UpdateJointControls();
        }

        _configurationScope.SelectionChanged += (_, _) => UpdateJointControls();
        _criterion.SelectionChanged += (_, _) => UpdateJointControls();
        _separateFields.IsCheckedChanged += (_, _) => UpdateJointControls();
    }

    private Control JointSection()
    {
        var addTolerance = Button("添加 TMCO");
        var addCompensator = Button("添加 CMCO");
        addTolerance.Click += (_, _) => AddDefaultMce(false);
        addCompensator.Click += (_, _) => AddDefaultMce(true);
        ControlAvailability.Set(addTolerance, _thicknessRows.Count > 0, "请先在多配置编辑器添加厚度行。");
        ControlAvailability.Set(addCompensator, _thicknessRows.Count > 0, "请先在多配置编辑器添加厚度行。");
        _jointDetails = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                _configurationRows, new WrapPanel { Children = { addTolerance, addCompensator } }, _mceRows
            }
        };
        var section = Section("多配置联合公差", Row("配置范围", _configurationScope),
            Note("同一次试验使用同一份共享制造误差。各配置同时达标才计入整机良率。"), _jointDetails);
        section.IsVisible = _configurationControls.Count > 1;
        UpdateJointControls();
        return section;
    }

    private void AddDefaultMce(bool compensator)
    {
        if (_thicknessRows.Count == 0) return;
        var row = _thicknessRows[0];
        var config = _configurationControls.FirstOrDefault(item => item.Select.IsChecked == true)?.Number ?? 1;
        AddMce(new(config, row.Number, row.SurfaceNumber, -.05, .05, compensator));
    }

    private void AddMce(ToleranceMceAdjustmentDto row)
    {
        var targetIndex = _thicknessRows.ToList().FindIndex(item => item.Number == row.Row && item.SurfaceNumber == row.SurfaceNumber);
        var target = Picker(targetIndex, _thicknessRows.Select(item => $"{item.Number} · 面 {item.SurfaceNumber}").ToArray());
        var configuration = Number(row.Configuration, 1, Math.Max(1, _configurationControls.Count), 1);
        var minimum = Number(ToDecimal(row.Minimum), -1_000_000, 1_000_000, .01m);
        var maximum = Number(ToDecimal(row.Maximum), -1_000_000, 1_000_000, .01m);
        var stage = Number(row.Stage, 1, 8, 1);
        var distribution = Picker((int)row.Distribution, "正态", "均匀");
        var remove = Button("删除");
        var extra = row.Compensator ? Row("补偿阶段", stage) : Row("统计模型", distribution);
        var card = Section(row.Compensator ? "CMCO" : "TMCO", Row("配置编号", configuration),
            Row("多配置厚度行", target), Row("最小偏差", minimum), Row("最大偏差", maximum), extra, remove);
        var controls = new MceControls(row.Compensator, configuration, target, minimum, maximum, stage, distribution);
        _mceControls.Add(controls);
        _mceRows.Children.Add(card);
        remove.Click += (_, _) =>
        {
            _mceControls.Remove(controls);
            _mceRows.Children.Remove(card);
        };
    }

    private void ApplyConfigurationSettings(ToleranceConfigurationSettingsDto? settings)
    {
        _configurationScope.SelectedIndex = (int)(settings?.Scope ?? ToleranceConfigurationScope.Current);
        foreach (var config in _configurationControls)
        {
            config.Select.IsChecked = settings?.Configurations?.Contains(config.Number) ?? config.Active;
            var limit = settings?.Limits?.FirstOrDefault(item => item.Configuration == config.Number);
            config.UseLimit.IsChecked = limit is not null;
            config.Limit.Value = ToDecimal(limit?.Limit ?? 0);
            config.Fields.Text = limit?.FieldLimits is { } fields
                ? string.Join(", ", fields.Select(field => FormattableString.Invariant($"{field.FieldNumber}={field.Minimum:R}"))) : "";
        }
        _mceControls.Clear();
        _mceRows.Children.Clear();
        foreach (var row in settings?.MceRows ?? []) AddMce(row);
        UpdateJointControls();
    }

    private ToleranceConfigurationSettingsDto? BuildConfigurationSettings()
    {
        if (!IsJointScope) return null;
        var scope = (ToleranceConfigurationScope)_configurationScope.SelectedIndex;
        var selected = _configurationControls.Where(config => scope == ToleranceConfigurationScope.All || config.Select.IsChecked == true).ToArray();
        if (selected.Length == 0) throw new ArgumentException("请选择至少一个参与公差的配置。");
        var limits = selected.Where(config => config.UseLimit.IsChecked == true).Select(config => new ToleranceConfigurationLimitDto(
            config.Number, DoubleValue(config.Limit, 0),
            CanSetJointFieldLimits ? ParseFieldLimits(config.Fields.Text) : null)).ToArray();
        var mce = _mceControls.Select(row =>
        {
            if (row.Target.SelectedIndex < 0 || row.Target.SelectedIndex >= _thicknessRows.Count)
                throw new ArgumentException("请选择有效的多配置厚度行。");
            var target = _thicknessRows[row.Target.SelectedIndex];
            return new ToleranceMceAdjustmentDto(IntValue(row.Configuration, 1), target.Number, target.SurfaceNumber,
                DoubleValue(row.Minimum, 0), DoubleValue(row.Maximum, 0), row.Compensator,
                IntValue(row.Stage, 1), (ToleranceDistribution)row.Distribution.SelectedIndex);
        }).ToArray();
        return new(scope, scope == ToleranceConfigurationScope.Selected ? selected.Select(row => row.Number).ToArray() : null, limits, mce);
    }

    private static IReadOnlyList<ToleranceFieldLimitDto>? ParseFieldLimits(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return text.Split(',', StringSplitOptions.TrimEntries).Select(part =>
        {
            var pair = part.Split('=', StringSplitOptions.TrimEntries);
            if (pair.Length != 2 || !int.TryParse(pair[0], out var field)
                || !double.TryParse(pair[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var limit)
                || field < 1 || !double.IsFinite(limit) || limit is < 0 or > 1)
                throw new ArgumentException("逐视场下限请使用 1=0.25, 2=0.20 格式。");
            return new ToleranceFieldLimitDto(field, limit);
        }).ToArray();
    }

    private bool CanSetJointFieldLimits =>
        _criterion.SelectedIndex == (int)ToleranceCriterion.Mtf && _separateFields.IsChecked == true;

    private void UpdateJointControls()
    {
        if (_jointDetails is not null) _jointDetails.IsVisible = IsJointScope;
        foreach (var config in _configurationControls)
        {
            config.Select.IsVisible = _configurationScope.SelectedIndex == (int)ToleranceConfigurationScope.Selected;
            config.Limit.IsEnabled = config.UseLimit.IsChecked == true;
            config.Limit.Maximum = _criterion.SelectedIndex == (int)ToleranceCriterion.Mtf ? 1 : 1_000_000;
            config.Fields.IsEnabled = config.Limit.IsEnabled && CanSetJointFieldLimits;
        }
        foreach (var row in _compControls) row.ConfigurationRow.IsVisible = IsJointScope;
    }

    private sealed record ConfigurationControls(int Number, bool Active, CheckBox Select, CheckBox UseLimit, NumericUpDown Limit, TextBox Fields);
    private sealed record MceControls(bool Compensator, NumericUpDown Configuration, ComboBox Target,
        NumericUpDown Minimum, NumericUpDown Maximum, NumericUpDown Stage, ComboBox Distribution);
}

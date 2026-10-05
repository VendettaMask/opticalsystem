using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.App.Controls;

namespace OptilandWorkbench.App.Panels;

public sealed partial class TolerancingRunWindow
{
    private readonly ComboBox _mtfMethod = Picker(0, "FFT 衍射 MTF", "几何 MTF（含衍射缩放）");
    private readonly ComboBox _mtfDirection = Picker(3, "T/S 平均", "切向 T", "弧矢 S", "T/S 较小值（两方向均达标）");
    private readonly NumericUpDown _mtfFrequency = Number(50, 0.000001m, 1_000_000, 5);
    private readonly ComboBox _mtfSampling = Picker(0, "32 × 32", "64 × 64", "128 × 128", "256 × 256", "512 × 512");
    private readonly NumericUpDown _mtfWave = Number(0, 0, 1, 1);
    private readonly CheckBox _separateFields = Check("逐视场验收（所有视场同时合格）", true);
    private readonly TextBlock _acceptanceLabel = new() { Text = "合格上限（0=不计算）", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly StackPanel _fieldLimitRows = new() { Spacing = 8 };
    private readonly StackPanel _compensatorRows = new() { Spacing = 10 };
    private readonly TextBlock _settingsError = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly List<FieldLimitControls> _fieldControls = new();
    private readonly List<CompensatorControls> _compControls = new();
    private IReadOnlyList<SurfaceRowDto> _contextSurfaces = Array.Empty<SurfaceRowDto>();
    private Border? _mtfCard;
    private Border? _fieldCard;

    private void InitializeMtfControls(IReadOnlyList<FieldRowDto>? fields,
        IReadOnlyList<SurfaceRowDto>? surfaces, IReadOnlyList<WavelengthRowDto>? wavelengths)
    {
        _contextSurfaces = surfaces ?? Array.Empty<SurfaceRowDto>();
        _mtfWave.Maximum = wavelengths?.Count ?? 1;
        foreach (var field in fields ?? Array.Empty<FieldRowDto>())
        {
            var use = Check($"视场 {field.Index + 1}  X={field.X:0.###}  Y={field.Y:0.###}", false);
            var limit = Number(0.2m, 0, 1, 0.01m);
            Avalonia.Automation.AutomationProperties.SetName(limit, $"视场 {field.Index + 1} MTF 下限");
            _fieldControls.Add(new(field.Index + 1, use, limit));
            use.IsCheckedChanged += (_, _) => UpdateMtfControls();
            _fieldLimitRows.Children.Add(Row(use, limit));
        }
        _separateFields.IsCheckedChanged += (_, _) => UpdateMtfControls();
        _settingsError.Classes.Add("error");
        Avalonia.Automation.AutomationProperties.SetName(_mtfFrequency, "MTF 空间频率");
        Avalonia.Automation.AutomationProperties.SetName(_mtfDirection, "MTF 方向");
    }

    private Control MtfSection() => _mtfCard = Section("指定频率 MTF",
        Row("计算方法", _mtfMethod), Row("方向", _mtfDirection), Row("空间频率 (cycles/mm)", _mtfFrequency),
        Row("瞳孔采样", _mtfSampling), Row("波长编号（0=多波长）", _mtfWave),
        Note("使用正式 MTF 方法评价。FFT 不自动回退为几何方法；计算失败计入不合格。最终评估应提高采样并检查结果是否收敛。"));

    private Control FieldLimitsSection() => _fieldCard = Section("分视场验收",
        _separateFields, Note("勾选某个视场以单独设置下限；未勾选的视场沿用统一下限。反向极限也使用这些单独下限；反向增量按各视场名义值减去允许下降量。"), _fieldLimitRows);

    private Control CompensationTab()
    {
        var add = Button("添加补偿器");
        add.Click += (_, _) => AddCompensator(new(
            _contextSurfaces.FirstOrDefault(surface => surface.Number > 0 && surface.Number < _contextSurfaces.Count - 1 && string.IsNullOrEmpty(surface.Material))?.Number ?? 1,
            ToleranceCompensatorKind.Thickness, -0.1, 0.1));
        return Page(Section("补偿方式", Row("方法", _compensation), Row("补偿循环", _cycles)),
            Section("附加可调量", Note("公差表中的 COMP 仍参与补偿。此处可增加空气间隔和单表面 X/Y 偏心、倾斜；范围是相对名义值的偏差，必须包含 0。偏心和间隔单位 mm，倾斜单位 °。"),
                add, _compensatorRows),
            Note("全部视场共用同一组补偿量。DLS 和坐标模式搜索为本软件算法；单表面调整并不自动移动整个镜片组。"));
    }

    private void AddCompensator(ToleranceCompensatorDto definition)
    {
        var surface = Number(definition.SurfaceNumber, 1, Math.Max(1, _contextSurfaces.Count - 2), 1);
        var kind = Picker((int)definition.Kind, "空气间隔 (mm)", "X 偏心 (mm)", "Y 偏心 (mm)", "X 倾斜 (°)", "Y 倾斜 (°)");
        var minimum = Number(ToDecimal(definition.Minimum), -1_000_000, 0, 0.01m);
        var maximum = Number(ToDecimal(definition.Maximum), 0, 1_000_000, 0.01m);
        var remove = Button("删除");
        var card = Section("附加补偿器", Row("表面编号", surface), Row("可调量", kind), Row("最小偏差", minimum), Row("最大偏差", maximum), remove);
        var controls = new CompensatorControls(surface, kind, minimum, maximum, card);
        _compControls.Add(controls); _compensatorRows.Children.Add(card);
        remove.Click += (_, _) => { _compControls.Remove(controls); _compensatorRows.Children.Remove(card); };
    }

    private void ApplyMtfSettings(ToleranceMtfSettingsDto? settings, IReadOnlyList<ToleranceCompensatorDto>? compensators)
    {
        settings ??= new();
        _mtfMethod.SelectedIndex = (int)settings.Method;
        _mtfDirection.SelectedIndex = (int)settings.Direction;
        _mtfFrequency.Value = ToDecimal(settings.Frequency);
        _mtfSampling.SelectedIndex = settings.Sampling - 1;
        _mtfWave.Value = settings.Wave;
        _separateFields.IsChecked = settings.SeparateFields;
        foreach (var field in _fieldControls)
        {
            var limit = settings.FieldLimits?.FirstOrDefault(item => item.FieldNumber == field.Number);
            field.Use.IsChecked = limit is not null;
            field.Limit.Value = ToDecimal(limit?.Minimum ?? 0.2);
        }
        _compControls.Clear(); _compensatorRows.Children.Clear();
        foreach (var compensator in compensators ?? Array.Empty<ToleranceCompensatorDto>()) AddCompensator(compensator);
    }

    private ToleranceMtfSettingsDto BuildMtfSettings() => new(DoubleValue(_mtfFrequency, 50), _mtfSampling.SelectedIndex + 1,
        IntValue(_mtfWave, 0), (ToleranceMtfMethod)_mtfMethod.SelectedIndex, (ToleranceMtfDirection)_mtfDirection.SelectedIndex,
        _separateFields.IsChecked == true, _separateFields.IsChecked == true
            ? _fieldControls.Where(field => field.Use.IsChecked == true).Select(field => new ToleranceFieldLimitDto(field.Number, DoubleValue(field.Limit, 0.2))).ToArray()
            : Array.Empty<ToleranceFieldLimitDto>());
    private IReadOnlyList<ToleranceCompensatorDto> BuildAdditionalCompensators() => _compControls.Select(row => new ToleranceCompensatorDto(
        IntValue(row.Surface, 1), (ToleranceCompensatorKind)row.Kind.SelectedIndex, DoubleValue(row.Minimum, 0), DoubleValue(row.Maximum, 0))).ToArray();

    private void UpdateMtfControls()
    {
        var mtf = _criterion.SelectedIndex == (int)ToleranceCriterion.Mtf;
        if (_mtfCard is not null) _mtfCard.IsVisible = mtf;
        if (_fieldCard is not null) _fieldCard.IsVisible = mtf;
        _acceptanceLabel.Text = mtf ? "MTF 合格下限（0=不计算）" : "合格上限（0=不计算）";
        _yieldLimit.Maximum = mtf ? 1 : 1_000_000;
        foreach (var field in _fieldControls)
        {
            ControlAvailability.Set(field.Use, mtf && _separateFields.IsChecked == true, "启用逐视场 MTF 验收后可设置单独下限。");
            ControlAvailability.Set(field.Limit, mtf && _separateFields.IsChecked == true && field.Use.IsChecked == true, "勾选此视场后可设置单独下限；否则沿用统一下限。");
        }
    }

    private void SubmitOptions()
    {
        var options = BuildOptions();
        if (options.AdditionalCompensators?.Any(row => row.Minimum >= row.Maximum) == true)
        { _settingsError.Text = "补偿器范围必须包含 0，且最小偏差小于最大偏差。"; return; }
        Close(options);
    }

    private sealed record FieldLimitControls(int Number, CheckBox Use, NumericUpDown Limit);
    private sealed record CompensatorControls(NumericUpDown Surface, ComboBox Kind, NumericUpDown Minimum, NumericUpDown Maximum, Border Card);
}

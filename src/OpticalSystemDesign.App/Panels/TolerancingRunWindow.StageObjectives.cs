using Avalonia.Controls;
using OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.App.Panels;

public sealed partial class TolerancingRunWindow
{
    private readonly StackPanel _stageObjectiveRows = new() { Spacing = 8 };
    private readonly List<StageObjectiveControls> _stageObjectiveControls = [];

    private Control StageObjectivesSection()
    {
        var add = Button("添加阶段评价");
        add.Click += (_, _) =>
        {
            var stage = Enumerable.Range(1, 9).FirstOrDefault(number =>
                _stageObjectiveControls.All(row => IntValue(row.Stage, 1) != number));
            if (stage > 0) AddStageObjective(new(stage, (ToleranceCriterion)_criterion.SelectedIndex));
        };
        return Section("阶段评价准则",
            Note("为有补偿器的阶段单独选准则。未设置的阶段沿用最终评价准则，良率始终按评价标准页判定。"),
            add, _stageObjectiveRows);
    }

    private void AddStageObjective(ToleranceStageObjectiveDto definition)
    {
        var settings = definition.MtfSettings ?? new();
        var stage = Number(definition.Stage, 1, 9, 1);
        var criterion = Picker((int)definition.Criterion, "RMS 点列半径", "RMS 波前误差", "指定频率 MTF", "系统评价函数");
        var method = Picker((int)settings.Method, "FFT 衍射 MTF", "几何 MTF（含衍射缩放）");
        var direction = Picker((int)settings.Direction, "T/S 平均", "切向 T", "弧矢 S", "T/S 较小值（两方向均达标）");
        var frequency = Number(ToDecimal(settings.Frequency), .000001m, 1_000_000, 5);
        var sampling = Picker(settings.Sampling - 1, "32 × 32", "64 × 64", "128 × 128", "256 × 256", "512 × 512");
        var wave = Number(settings.Wave, 0, _mtfWave.Maximum, 1);
        var includeZeroWeight = Check("阶段评价包含零权重视场", settings.SeparateFields);
        var mtf = new StackPanel
        {
            Spacing = 6,
            IsVisible = definition.Criterion == ToleranceCriterion.Mtf,
            Children =
            {
                Row("计算方法", method), Row("方向", direction), Row("空间频率 (cycles/mm)", frequency),
                Row("瞳孔采样", sampling), Row("波长编号（0=多波长）", wave), includeZeroWeight
            }
        };
        var merit = CreateMeritBinding($"阶段 {definition.Stage}", definition.MeritTable);
        void UpdateCriterion()
        {
            mtf.IsVisible = criterion.SelectedIndex == (int)ToleranceCriterion.Mtf;
            merit.View.IsVisible = criterion.SelectedIndex == (int)ToleranceCriterion.SystemMerit;
        }
        criterion.SelectionChanged += (_, _) => UpdateCriterion();
        UpdateCriterion();
        Avalonia.Automation.AutomationProperties.SetName(frequency, "阶段 MTF 空间频率");
        var remove = Button("删除");
        var card = Section("阶段评价", Row("阶段编号", stage), Row("阶段准则", criterion), mtf, merit.View, remove);
        var controls = new StageObjectiveControls(stage, criterion, method, direction, frequency, sampling, wave, includeZeroWeight, merit);
        _stageObjectiveControls.Add(controls);
        _stageObjectiveRows.Children.Add(card);
        remove.Click += (_, _) =>
        {
            _stageObjectiveControls.Remove(controls);
            _stageObjectiveRows.Children.Remove(card);
        };
    }

    private void ApplyStageObjectives(IReadOnlyList<ToleranceStageObjectiveDto>? objectives)
    {
        _stageObjectiveControls.Clear();
        _stageObjectiveRows.Children.Clear();
        foreach (var objective in objectives ?? []) AddStageObjective(objective);
    }

    private IReadOnlyList<ToleranceStageObjectiveDto>? BuildStageObjectives()
    {
        if (_stageObjectiveControls.Count == 0) return null;
        var objectives = _stageObjectiveControls.Select(row =>
        {
            var criterion = (ToleranceCriterion)row.Criterion.SelectedIndex;
            var settings = criterion == ToleranceCriterion.Mtf
                ? new ToleranceMtfSettingsDto(DoubleValue(row.Frequency, 50), row.Sampling.SelectedIndex + 1,
                    IntValue(row.Wave, 0), (ToleranceMtfMethod)row.Method.SelectedIndex,
                    (ToleranceMtfDirection)row.Direction.SelectedIndex, row.IncludeZeroWeight.IsChecked == true)
                : null;
            return new ToleranceStageObjectiveDto(IntValue(row.Stage, 1), criterion, settings,
                criterion == ToleranceCriterion.SystemMerit ? ReadMeritBinding(row.Merit) : null);
        }).ToArray();
        if (objectives.Select(row => row.Stage).Distinct().Count() != objectives.Length)
            throw new ArgumentException("同一阶段只能设置一个评价准则。");
        return objectives;
    }

    private sealed record StageObjectiveControls(NumericUpDown Stage, ComboBox Criterion, ComboBox Method,
        ComboBox Direction, NumericUpDown Frequency, ComboBox Sampling, NumericUpDown Wave, CheckBox IncludeZeroWeight, MeritBindingControls Merit);
}

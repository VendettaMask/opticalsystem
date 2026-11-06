using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.App.Panels;

internal sealed partial class ToleranceResultsPanel
{
    private static Control TrialDetail(TolerancingTrialRowDto trial, ToleranceCriterion finalCriterion)
    {
        var phases = new List<TabItem>
        {
            Tab("补偿后", DetailGrid(DetailRows(trial.CompensatedMerit, trial.Configurations, trial.Fields, trial.Compensators))),
            Tab("补偿前", DetailGrid(DetailRows(trial.Merit, trial.UncompensatedConfigurations, trial.UncompensatedFields, null)))
        };
        foreach (var stage in trial.Stages ?? [])
        {
            var criterion = stage.Objective?.Criterion ?? finalCriterion;
            var note = stage.Objective?.MtfSettings is { } settings
                ? UiLocalization.Format("MTF {0} cycles/mm", Format(settings.Frequency))
                : CriterionLabel(criterion);
            if (stage.MeritTableName is { } table)
                note += " · " + UiLocalization.Format("评价表 {0}", table);
            if (stage.Error.Length > 0) note += " · " + stage.Error;
            var rows = DetailRows(Format(stage.Criterion), stage.Configurations, stage.Fields, stage.Compensators, note);
            phases.Add(Tab(UiLocalization.Format("阶段 {0}", stage.Stage), DetailGrid(rows)));
        }
        var header = Text(UiLocalization.Format("试验 {0}    验收余量 {1}", trial.Trial, Format(trial.AcceptanceMargin)));
        return Page(header, Tabs(phases));
    }

    private static IReadOnlyList<DetailItem> DetailRows(string criterion,
        IReadOnlyList<ToleranceConfigurationResultDto>? configurations, IReadOnlyList<ToleranceFieldValueDto>? fields,
        IReadOnlyList<ToleranceCompensatorValueDto>? compensators, string note = "")
    {
        var rows = new List<DetailItem> { new(UiLocalization.Text("评价值"), criterion, note) };
        AddFields(fields, "");
        foreach (var configuration in configurations ?? [])
        {
            var name = UiLocalization.Format("配置 {0}", configuration.Configuration);
            var reason = string.IsNullOrWhiteSpace(configuration.Error)
                ? Status(configuration.Passed, !double.IsFinite(configuration.Criterion)
                    || configuration.AcceptanceMargin == double.NegativeInfinity)
                : configuration.Error;
            if (configuration.AcceptanceMargin.HasValue)
                reason += " · " + UiLocalization.Format("验收余量 {0}", Format(configuration.AcceptanceMargin));
            if (configuration.LimitingField is { } limiting)
                reason += " · " + UiLocalization.Format("限制视场 {0}", limiting);
            rows.Add(new(name, Format(configuration.Criterion), reason));
            AddFields(configuration.Fields, name);
            if (compensators is null)
                AddCompensators(configuration.Compensators, name);
        }
        AddCompensators(compensators, "");
        return rows;

        void AddFields(IReadOnlyList<ToleranceFieldValueDto>? values, string scope)
        {
            foreach (var value in values ?? [])
            {
                var name = UiLocalization.Format("视场 {0}", value.FieldNumber);
                rows.Add(new(Qualified(scope, name), Format(value.Value), value.Error));
            }
        }

        void AddCompensators(IReadOnlyList<ToleranceCompensatorValueDto>? values, string scope)
        {
            foreach (var value in values ?? [])
            {
                rows.Add(new(Qualified(scope, value.Name), $"{Format(value.Nominal)} → {Format(value.Value)}",
                    UiLocalization.Format("调整 {0} · 行程 [{1}, {2}]", Format(value.Value - value.Nominal),
                        Format(value.Minimum), Format(value.Maximum))));
            }
        }
    }

    private static string Qualified(string scope, string item) => scope.Length == 0 ? item : $"{scope} · {item}";

    private static DataGrid DetailGrid(IReadOnlyList<DetailItem> rows)
    {
        var grid = Grid(
            DetailColumn("项目", item => item.Name, new DataGridLength(1, DataGridLengthUnitType.Star)),
            DetailColumn("结果值", item => item.Value, new DataGridLength(112)),
            DetailColumn("状态／调整", item => item.Note, new DataGridLength(1.4, DataGridLengthUnitType.Star)));
        grid.CanUserSortColumns = false;
        grid.RowHeight = double.NaN;
        grid.ItemsSource = rows;
        return grid;
    }

    private static DataGridTemplateColumn DetailColumn(string header, Func<DetailItem, string> value, DataGridLength width) => new()
    {
        Header = UiLocalization.Text(header),
        Width = width,
        CellTemplate = new FuncDataTemplate<DetailItem>((item, _) => new TextBlock
        {
            Text = item is null ? "" : value(item),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Avalonia.Thickness(6, 4)
        })
    };

    private sealed record DetailItem(string Name, string Value, string Note);
}

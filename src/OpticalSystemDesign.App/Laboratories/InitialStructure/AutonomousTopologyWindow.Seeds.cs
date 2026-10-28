using Avalonia.Controls;
using Avalonia.Threading;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

namespace OpticalSystemDesign.App.Laboratories.InitialStructure;

public sealed partial class AutonomousTopologyWindow
{
    private CancellationTokenSource? _seedCancellation;
    private ParaxialSeedSearchResult? _seedResult;
    private string? _seedReportPath;
    private readonly TextBlock _seedSummary = Named(Text("一阶准备使用独立额度，不代表完整光学验收通过"), "AutonomousSeedSummary");

    private void MarkPlanEdited()
    {
        _planEdited = true;
        ResetSeedSummary();
    }

    private void ResetSeedSummary() { _seedResult = null; _seedReportPath = null; RefreshSeedSummary(); }

    private void RefreshSeedSummary()
    {
        _seedSummary.Text = UiLocalization.Text("一阶准备使用独立额度，不代表完整光学验收通过");
        if (_seedResult is not { } result) return;
        var options = result.Options;
        _seedSummary.Text += " · " + UiLocalization.Text(result.Termination switch
        {
            ParaxialSeedTermination.Completed => "一阶准备完成",
            ParaxialSeedTermination.BudgetExhausted => "一阶准备已到额度",
            ParaxialSeedTermination.Cancelled => "一阶准备已停止并保存",
            _ => "一阶准备已到时限"
        }) + $" · {result.Seeds.Length}/{options.RetainedSeeds}\n"
            + $"{UiLocalization.Text("评价")} {result.Work.FunctionEvaluations}/{options.MaximumFunctionEvaluations} · "
            + $"{UiLocalization.Text("矩阵")} {result.MatrixRequests} · Jacobian {result.Work.JacobianBuilds} · "
            + $"{UiLocalization.Text("几何查询")} {result.Work.GeometryProbes} · {UiLocalization.Text("追迹")} {result.Work.TracedRays}/{options.MaximumTracedRays}\n"
            + _seedReportPath;
    }

    private async Task PrepareSeedsAsync()
    {
        var plan = ReadPlan();
        var options = ReadSeedOptions(plan.Options.SeedPreparation ?? new());
        plan = plan with { Options = plan.Options with { SeedPreparation = options } };
        using var cancellation = new CancellationTokenSource();
        _seedCancellation = cancellation;
        var generation = ++_generation;
        Refresh();
        ParaxialSeedSearchResult result;
        try
        {
            result = await Task.Run(() => ParaxialSeedSearch.Run(plan, options, cancellation.Token,
                (start, functions) => Dispatcher.UIThread.Post(() =>
                {
                    if (!_closed && generation == _generation)
                        _status.Text = UiLocalization.Text("正在准备一阶起点") + $" · {start}/{options.MaximumStarts} · {functions}/{options.MaximumFunctionEvaluations}";
                })));
        }
        finally { _seedCancellation = null; _generation++; }
        var directory = Path.Combine(_settings.RunDirectory, "preparations");
        Directory.CreateDirectory(directory);
        var name = "paraxial-seeds-" + Guid.NewGuid().ToString("N");
        var reportPath = Path.Combine(directory, name + ".seed-search.json");
        var prepared = result.Seeds.IsEmpty ? plan : plan with { Seeds = result.Seeds };
        await ParaxialSeedSearchFiles.SaveAsync(result, reportPath);
        await AutonomousDesignFiles.SavePlanAsync(prepared, Path.Combine(directory, name + ".topology-plan.json"));
        _session.Reset(); _displayRevision = -1; _displayRun = null; _draft = prepared; ApplyPlan(prepared);
        _candidateRows.Clear(); _eventRows.Clear(); _prescriptionRows.Clear(); _fieldRows.Clear();
        _seedResult = result; _seedReportPath = reportPath; RefreshSeedSummary();
        _status.Text = UiLocalization.Text(result.Termination == ParaxialSeedTermination.Cancelled ? "一阶准备已停止并保存"
            : result.Seeds.IsEmpty ? "未找到满足一阶条件的起点，原起点保留" : "一阶起点已保存，尚未运行完整光学搜索");
    }

    private ParaxialSeedSearchOptions ReadSeedOptions(ParaxialSeedSearchOptions original)
    {
        var number = _numbers["AutonomousSeedSeconds"];
        if (number.Value is not { } seconds || !decimal.TryParse(number.Text, out var text) || text != seconds)
            throw new ArgumentException(UiLocalization.Text("请填写有效数字"));
        return original with
        {
            MaximumStarts = Whole("AutonomousSeedStarts"), RetainedSeeds = Whole("AutonomousSeedRetained"),
            MaximumFunctionEvaluations = Whole("AutonomousSeedFunctions"),
            TimeLimit = seconds == (decimal)original.TimeLimit.TotalSeconds ? original.TimeLimit : TimeSpan.FromSeconds((double)seconds)
        };
    }

    private ParaxialSeedSearchOptions? ReadSeedSettings()
    {
        var original = _draft.Options.SeedPreparation;
        var read = ReadSeedOptions(original ?? new());
        return original is null && read == new ParaxialSeedSearchOptions() ? null : read;
    }

    private int Whole(string id)
    {
        var number = _numbers[id];
        if (number.Value is not { } value || !decimal.TryParse(number.Text, out var text) || text != value || decimal.Truncate(value) != value)
            throw new ArgumentException(UiLocalization.Text("请填写有效整数"));
        return checked((int)value);
    }
}

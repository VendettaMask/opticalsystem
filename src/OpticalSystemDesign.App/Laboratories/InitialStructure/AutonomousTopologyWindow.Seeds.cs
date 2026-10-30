using Avalonia.Controls;
using System.Collections.Immutable;
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
        if (result.ImageScreens is { } screens)
            _seedSummary.Text += "\n" + UiLocalization.Text("真实像差筛选，仅用于起点排序")
                + $" · {screens.Count(screen => screen.Outcome == "Screened")}/{screens.Length}";
        if (result.Competition is { } competition)
        {
            _seedSummary.Text += "\n" + UiLocalization.Text("竞争候选 / 工作起点 / 原规格合格")
                + $" {competition.Candidates.Length} / {competition.SelectedIds.Length} / {competition.QualifiedIds.Length}"
                + " · " + UiLocalization.Text("阶段评价") + $" {competition.Rounds.Length} · {competition.StopReason}";
            if (competition.SearchEvidence is { } experiment)
                _seedSummary.Text += "\n" + UiLocalization.Text("实验策略：符号核验 / 快筛更新 / 挑战复核 / 终验预留")
                    + $" {competition.Candidates.Count(c => c.SearchEvidence?.SignsVerified == true)} / "
                    + $"{experiment.QuickEvaluations.Sum(q => q.AcceptedUpdates)} / {experiment.Challenges.Length} / {experiment.ReservedFinalRays}";
            foreach (var round in competition.Rounds.GroupBy(r => r.StageIndex))
            {
                var stage = round.First().Stage;
                _seedSummary.Text += $"\n{stage.Name} · {UiLocalization.Text("视场 / 孔径比例")} {stage.FieldFraction:P0} / {stage.PupilFraction:P0}"
                    + $" · {round.Count()} → {round.Count(r => r.Selected)}";
            }
        }
        if (result.Options.Generation?.MaterializationVersion == 3)
            _seedSummary.Text += "\n" + UiLocalization.Text("构造布局 / 可行布局") + " "
                + result.Attempts.Sum(a => a.Construction?.AttemptedLayouts ?? 0) + " / "
                + result.Attempts.Sum(a => a.Construction?.FeasibleLayouts ?? 0);
        if (result.Evolution is { } evolution)
            _seedSummary.Text += "\n" + UiLocalization.Text("结构演化：父方案合格 / 新方案合格 / 权衡方案 / 分支")
                + $" {evolution.QualifiedArchive.Count(q => q.ParentId is null)} / {evolution.QualifiedArchive.Count(q => q.ParentId is not null)}"
                + $" / {evolution.ParetoIds.Length} / {evolution.Trials.Length} · {evolution.StopReason}";
        if (result.Retrieval is { } retrieval)
        {
            _seedSummary.Text += "\n" + UiLocalization.Text("镜头库：条目 / 原规格合格 / 原输入复用")
                + $" {retrieval.Trials.Length} / {retrieval.QualifiedEntryIds.Length} / {retrieval.Trials.Count(t => t.MatchesOriginalInput && t.Assessment?.MeetsTargets == true)}"
                + $" · {retrieval.StopReason}\n" + UiLocalization.Text("来源获取成本未知的条目") + $" {retrieval.UnknownAcquisitionCosts}";
            foreach (var trial in retrieval.Trials)
                _seedSummary.Text += $"\n{trial.EntryId}: {RetrievalOutcomeText(trial.Outcome)} · {trial.Provenance.Title}"
                    + (trial.Transform is { } transform ? $" · ×{transform.UniformScale:G6}" : "");
        }
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
                        _status.Text = UiLocalization.Text(options.Retrieval is null ? "正在准备一阶起点" : "正在检索镜头库")
                            + $" · {start}/{options.Retrieval?.Library.Entries.Length ?? options.MaximumStarts} · {functions}/{options.MaximumFunctionEvaluations}";
                })));
        }
        finally { _seedCancellation = null; _generation++; }
        var directory = Path.Combine(_settings.RunDirectory, "preparations");
        Directory.CreateDirectory(directory);
        var name = "paraxial-seeds-" + Guid.NewGuid().ToString("N");
        var reportPath = Path.Combine(directory, name + ".seed-search.json");
        var prepared = result.Seeds.IsEmpty ? plan : plan with
        {
            Seeds = plan.Options.BoundedSearch is null ? result.Seeds
            : plan.Seeds.Concat(result.Seeds).DistinctBy(TopologySeedIdentity).Take(8).ToImmutableArray()
        };
        await ParaxialSeedSearchFiles.SaveAsync(result, reportPath);
        await AutonomousDesignFiles.SavePlanAsync(prepared, Path.Combine(directory, name + ".topology-plan.json"));
        _session.Reset(); _displayRevision = -1; _displayRun = null; _draft = prepared; ApplyPlan(prepared);
        _candidateRows.Clear(); _eventRows.Clear(); _prescriptionRows.Clear(); _fieldRows.Clear();
        _seedResult = result; _seedReportPath = reportPath; RefreshSeedSummary();
        _status.Text = UiLocalization.Text(result.Termination == ParaxialSeedTermination.Cancelled ? "一阶准备已停止并保存"
            : result.Retrieval is not null ? "检索已保存，已有镜头复用不计为新设计"
            : result.Competition is not null ? "竞争准备已保存，合格状态见原规格终验"
            : result.Seeds.IsEmpty ? result.ImageScreens is not null ? "像差筛选未得到完整有效结果，原起点保留" : "未找到满足一阶条件的起点，原起点保留" : "一阶起点已保存，尚未运行完整光学搜索");
    }

    private ParaxialSeedSearchOptions ReadSeedOptions(ParaxialSeedSearchOptions original)
    {
        var number = _numbers["AutonomousSeedSeconds"];
        if (number.Value is not { } seconds || !decimal.TryParse(number.Text, out var text) || text != seconds)
            throw new ArgumentException(UiLocalization.Text("请填写有效数字"));
        return original with
        {
            Retrieval = _choices["AutonomousRetrieval"].IsChecked == true ? (original.Retrieval ?? new() { Library = _lensLibrary }) with
            {
                Library = _lensLibrary,
                MaximumCandidates = Whole("AutonomousRetrievalCandidates"),
                MinimumScale = ReadLibraryNumber("AutonomousRetrievalMinScale"),
                MaximumScale = ReadLibraryNumber("AutonomousRetrievalMaxScale")
            } : null,
            Evolution = _choices["AutonomousEvolution"].IsChecked == true ? (original.Evolution ?? new()) with
            {
                Mode = _choices["AutonomousEvolutionCompetition"].IsChecked == true ? StructureEvolutionMode.RecoveryAndCompetition
                    : _choices["AutonomousEvolutionRecovery"].IsChecked == true ? StructureEvolutionMode.RecoveryAndCompensation : StructureEvolutionMode.DirectCompensation,
                MaximumParents = Whole("AutonomousEvolutionParents"),
                MaximumBranches = Whole("AutonomousEvolutionBranches"),
                RecoveryFunctionsPerBranch = Whole("AutonomousEvolutionRecoveryFunctions"),
                CompensationUpdatesPerBranch = Whole("AutonomousEvolutionUpdates"),
                PowerAllocation = _choices["AutonomousPowerAllocation"].IsChecked == true ? original.Evolution?.PowerAllocation ?? new() : null
            } : null,
            Competition = _choices["AutonomousCompetition"].IsChecked == true
                ? (original.Competition ?? new()) with
                {
                    UpdatesPerStage = Whole("AutonomousCompetitionUpdates"),
                    SearchStrategy = _choices["AutonomousPowerSigns"].IsChecked == true || _choices["AutonomousQuickAberration"].IsChecked == true || _choices["AutonomousEvolution"].IsChecked == true
                        ? (original.Competition?.SearchStrategy ?? new()) with
                        {
                            PowerSigns = _choices["AutonomousPowerSigns"].IsChecked == true ? original.Competition?.SearchStrategy?.PowerSigns ?? new() : null,
                            QuickEvaluation = _choices["AutonomousQuickAberration"].IsChecked == true ? original.Competition?.SearchStrategy?.QuickEvaluation ?? new() : null
                        } : null
                } : null,
            MaximumTracedRays = _choices["AutonomousRetrieval"].IsChecked == true ? Whole("AutonomousRetrievalRays") : _choices["AutonomousCompetition"].IsChecked == true
                ? Whole("AutonomousCompetitionRays") : original.MaximumTracedRays,
            MaximumStarts = Whole("AutonomousSeedStarts"),
            RetainedSeeds = Whole("AutonomousSeedRetained"),
            MaximumFunctionEvaluations = Whole("AutonomousSeedFunctions"),
            MaximumEvaluationsPerStart = Whole("AutonomousSeedPerStart"),
            Generation = ReadGeneration(original.Generation),
            FixedStopRecoveryVersion = _choices["AutonomousFixedStopRecovery"].IsChecked == true ? 2 : null,
            ImageScreening = _choices["AutonomousImageScreening"].IsChecked == true
                ? (original.ImageScreening ?? new() { BoundaryCheckArms = 8, PupilRings = 1, PupilArms = 4 }) with
                { MaximumTracedRaysPerCandidate = Whole("AutonomousScreenRays") } : null,
            TimeLimit = seconds == (decimal)original.TimeLimit.TotalSeconds ? original.TimeLimit : TimeSpan.FromSeconds((double)seconds)
        };
    }

    private static string TopologySeedIdentity(DesignTopology topology) => ContentFingerprint.Compute(topology with { Name = "seed" });

    private StructureGenerationOptions? ReadGeneration(StructureGenerationOptions? original)
    {
        if (_choices["AutonomousDescriptor"].IsChecked != true) return null;
        var count = Whole("AutonomousGeneratedElements");
        var source = _draft.Seeds[0];
        var elements = original?.Elements.Length == count ? original.Elements : Enumerable.Range(0, count).Select(i =>
        {
            var element = source.Elements[i % source.Elements.Length];
            return new StructureElementDescriptor
            {
                Profile = element.Front.Profile.Kind == TopologyProfileKind.EvenAsphere
                || element.Back.Profile.Kind == TopologyProfileKind.EvenAsphere ? TopologyProfileKind.EvenAsphere : TopologyProfileKind.Standard,
                Materials = [element.Material]
            };
        }).ToImmutableArray();
        double Number(string id, double? preserved)
        {
            var input = _numbers[id];
            if (input.Value is not { } value || !decimal.TryParse(input.Text, out var text) || text != value)
                throw new ArgumentException(UiLocalization.Text("请填写有效数字"));
            return preserved is { } exact && value == (decimal)exact ? exact : (double)value;
        }
        return (original ?? new()) with
        {
            Elements = elements,
            MaterializationVersion = _choices["AutonomousFeasibleThickDelano"].IsChecked == true ? 3
                : _choices["AutonomousThickDelano"].IsChecked == true ? 2 : null,
            FeasibleLayouts = _choices["AutonomousFeasibleThickDelano"].IsChecked == true ? new()
            {
                MaximumLayouts = Whole("AutonomousConstructionLayouts"),
                StopAfterElementCandidates = _choices["AutonomousStopPartitions"].IsChecked == true
                    ? original?.Elements.Length == count && original.StopAfterElement == Whole("AutonomousGeneratedStop")
                        && original.FeasibleLayouts is { StopAfterElementCandidates.IsEmpty: false } saved
                        ? saved.StopAfterElementCandidates
                        : new[] { Whole("AutonomousGeneratedStop"), 0, count / 2, count }.Distinct().ToImmutableArray() : []
            } : null,
            Method = _choices["AutonomousDelano"].IsChecked == true ? StructureGenerationMethod.Delano : StructureGenerationMethod.RandomPhysical,
            StopAfterElement = Whole("AutonomousGeneratedStop"),
            ClearSemiDiameterMillimeters = Number("AutonomousGeneratedRadius", original?.ClearSemiDiameterMillimeters),
            MinimumThicknessMillimeters = Number("AutonomousGeneratedMinThickness", original?.MinimumThicknessMillimeters),
            MaximumThicknessMillimeters = Number("AutonomousGeneratedMaxThickness", original?.MaximumThicknessMillimeters),
            MinimumBackFocusMillimeters = Number("AutonomousGeneratedBackFocus", original?.MinimumBackFocusMillimeters),
            AnnealingProposals = Whole("AutonomousAnnealing")
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

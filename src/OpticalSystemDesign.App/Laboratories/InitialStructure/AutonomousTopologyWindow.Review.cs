using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Persistence;

namespace OpticalSystemDesign.App.Laboratories.InitialStructure;

public sealed partial class AutonomousTopologyWindow
{
    internal AutonomousDesignReview? CurrentReview => _review;
    private AutonomousDesignPortfolio _portfolio = new();
    private AutonomousDesignReview? _review;
    private object? _reviewSource;
    private SeedPreparationCheckpoint? _preparedEvidence;
    private bool _reviewSelected, _viewingPortfolio, _settingPortfolio;
    private readonly TextBlock _reviewOverview = Named(Text("载入记录后查看准备过程、原规格验收和淘汰原因。"), "AutonomousReviewOverview");
    private readonly TextBox _reviewName = Named(new TextBox { PlaceholderText = "方案名称", MaxLength = 160 }, "AutonomousReviewName");
    private readonly ComboBox _portfolioEntries = Named(new ComboBox { MinWidth = 160, MaxWidth = 360 }, "AutonomousPortfolioEntries");
    private readonly TextBox _reviewDetails = Named(new TextBox { IsReadOnly = true, AcceptsReturn = true,
        TextWrapping = Avalonia.Media.TextWrapping.Wrap, MinHeight = 140, MaxHeight = 300 }, "AutonomousReviewDetails");
    private DataGrid? _reviewCandidates, _reviewDecisions;
    private TabControl? _autonomousTabs;
    private TabItem? _structureTab;
    private readonly Dictionary<Button, Func<bool>> _reviewActions = [];

    private Control BuildReviewView()
    {
        var root = new StackPanel { Spacing = 8, Margin = new Thickness(8) };
        root.Children.Add(_reviewOverview); root.Children.Add(_reviewName);
        var commands = new WrapPanel { Orientation = Orientation.Horizontal };
        root.Children.Add(commands);
        Add("AutonomousReviewCurrent", "显示当前记录", () => { _viewingPortfolio = false; _portfolioEntries.SelectedIndex = -1; RefreshReview(force: true); return Task.CompletedTask; }, HasCurrent);
        Add("AutonomousReviewReport", "导出设计报告", ExportReviewReportAsync, () => _review is not null);
        Add("AutonomousReviewData", "导出记录数据", ExportReviewDataAsync, () => _review is not null);
        Add("AutonomousReviewOpenData", "打开记录数据", OpenReviewDataAsync, () => true);
        Add("AutonomousReviewExport", "导出所选方案", ExportReviewCandidateAsync, () => SelectedReviewCandidate?.CanExport == true);
        Add("AutonomousReviewStructure", "显示所选结构", DisplayReviewStructureAsync, () => SelectedReviewCandidate is not null);
        Add("AutonomousPortfolioAdd", "方案库收录当前记录", AddCurrentStudyAsync, HasCurrent);
        Add("AutonomousPortfolioOpen", "打开方案库", OpenPortfolioAsync, () => true);
        Add("AutonomousPortfolioSave", "保存方案库", SavePortfolioAsync, () => _portfolio.Entries.Length > 0);
        _portfolioEntries.ItemTemplate = new FuncDataTemplate<AutonomousDesignStudy>((study, _) => Text(study?.Name ?? ""));
        _portfolioEntries.SelectionChanged += (_, _) =>
        {
            if (_settingPortfolio || IsBusy || _portfolioEntries.SelectedItem is not AutonomousDesignStudy study) return;
            _viewingPortfolio = true; PresentReview(AutonomousDesignReview.Build(study));
        };
        root.Children.Add(Text("方案库只读快照（不切换当前搜索任务）")); root.Children.Add(_portfolioEntries);
        _reviewCandidates = Table("AutonomousReviewCandidates", ("候选", "Id", 180), ("来源", "Origin", 150),
            ("验收", "Status", 230), ("结果", "Outcome", 240), ("片数", "Elements", 60));
        _reviewCandidates.Height = 210;
        _reviewCandidates.SelectionChanged += (_, _) => ShowReviewCandidate();
        root.Children.Add(_reviewCandidates);
        _reviewDecisions = Table("AutonomousReviewDecisions", ("试验", "Id", 180), ("父方案", "Parent", 130),
            ("阶段", "Stage", 160), ("结果", "Outcome", 250), ("评价 / 光线梯度 / 追迹", "Work", 200), ("原因", "Detail", 420));
        _reviewDecisions.Height = 210;
        _reviewDecisions.SelectionChanged += (_, _) =>
        {
            if (_reviewDecisions.SelectedItem is not ReviewDecisionRow row) return;
            var decision = row.Decision;
            _reviewDetails.Text = decision.Id + " · " + ReviewCode(decision.Stage) + "\n" + ReviewCode(decision.Outcome)
                + "\n[" + decision.Outcome + "]\n" + decision.Detail + "\n" + row.Work
                + (decision.Backend is null ? "" : "\n" + decision.Backend);
        };
        root.Children.Add(_reviewDecisions); root.Children.Add(_reviewDetails);
        return new ScrollViewer { Content = root, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };

        bool HasCurrent() => _session.Checkpoint is not null || _session.PreparationCheckpoint is not null || _seedResult is not null;
        void Add(string id, string label, Func<Task> action, Func<bool> enabled)
        {
            var button = Named(new Button { Content = label, Margin = new Thickness(0, 0, 6, 4) }, id);
            AutomationProperties.SetName(button, label);
            button.Click += (_, _) => { if (!IsBusy) PendingOperation = PerformAsync(action); };
            _reviewActions.Add(button, enabled); commands.Children.Add(button);
        }
    }

    private AutonomousDesignStudy CaptureCurrentStudy()
    {
        var name = string.IsNullOrWhiteSpace(_reviewName.Text) ? _session.Checkpoint?.RunId ?? _session.PreparationCheckpoint?.RunId ?? _preparedEvidence?.RunId ?? "Prepared design" : _reviewName.Text.Trim();
        return AutonomousDesignStudy.Capture(name, _session.Checkpoint,
            _session.PreparationCheckpoint ?? (_session.Checkpoint is null ? _preparedEvidence : null), _seedResult);
    }
    private void RefreshReview(bool force = false)
    {
        foreach (var (button, enabled) in _reviewActions) button.IsEnabled = !IsBusy && enabled();
        _portfolioEntries.IsEnabled = !IsBusy;
        if ((!_reviewSelected && !force) || _viewingPortfolio || _session.IsBusy || _uiOperation && !force) return;
        object? source = _session.Checkpoint ?? (object?)_session.PreparationCheckpoint ?? _preparedEvidence ?? (object?)_seedResult;
        if (source is null)
        {
            _review = null; _reviewSource = null;
            if (_reviewCandidates is not null) _reviewCandidates.ItemsSource = null;
            if (_reviewDecisions is not null) _reviewDecisions.ItemsSource = null;
            _reviewDetails.Text = ""; _reviewOverview.Text = UiLocalization.Text("载入记录后查看准备过程、原规格验收和淘汰原因。");
        }
        else if (force || !ReferenceEquals(source, _reviewSource))
        {
            var review = AutonomousDesignReview.Build(CaptureCurrentStudy());
            _reviewSource = source; PresentReview(review);
        }
    }
    private void PresentReview(AutonomousDesignReview review)
    {
        var selected = SelectedReviewCandidate?.Id; _review = review;
        _reviewOverview.Text = review.Study.Name + " · " + UiLocalization.Text(_viewingPortfolio ? "归档只读快照" : "当前记录只读展示")
            + "\n" + UiLocalization.Text("原规格合格记录 / 候选记录 / 阶段记录") + $" {review.Candidates.Count(c => c.OriginalQualified)} / {review.Candidates.Length} / {review.Decisions.Length}"
            + "\n" + UiLocalization.Text("一阶可行、工作起点和中间阶段不等于原规格合格；已有镜头复用不计为新设计。");
        if (review.Study.Preparation is not null || review.Study.Prepared is not null)
            _reviewOverview.Text += "\n" + UiLocalization.Text("准备累计费用") + " · " + ReviewWork(review.Study.Preparation?.Consumed ?? review.Study.Prepared!.Work);
        if (review.Study.Main is { } main) _reviewOverview.Text += "\n" + UiLocalization.Text("主搜索累计费用") + " · " + ReviewWork(main.Consumed);
        if (_reviewCandidates is not null)
        {
            var rows = review.Candidates.Select(c => new ReviewCandidateRow(c.Id, UiLocalization.Text(c.Origin), UiLocalization.Text(c.Classification),
                ReviewCode(c.Outcome), c.Topology.Elements.Length, c)).ToArray();
            _reviewCandidates.ItemsSource = rows;
            _reviewCandidates.SelectedItem = rows.FirstOrDefault(c => c.Id == selected) ?? rows.FirstOrDefault();
        }
        if (_reviewDecisions is not null) _reviewDecisions.ItemsSource = review.Decisions.Select(d => new ReviewDecisionRow(d.Id, d.Parent ?? "—",
            ReviewCode(d.Stage), ReviewCode(d.Outcome), d.Work is { } w ? $"{w.FunctionEvaluations} / {w.GradientEvaluations} / {w.TracedRays}"
                : $"{d.KnownFunctionEvaluations?.ToString() ?? UiLocalization.Text("未记录")} / {UiLocalization.Text("未记录")} / {UiLocalization.Text("未记录")}", d.Detail ?? "", d)).ToArray();
        ShowReviewCandidate();
    }
    private DesignReviewCandidate? SelectedReviewCandidate => (_reviewCandidates?.SelectedItem as ReviewCandidateRow)?.Candidate;
    private static string ReviewWork(AutonomousSearchWork work) => $"{UiLocalization.Text("评价")} {work.FunctionEvaluations} · "
        + $"{UiLocalization.Text("梯度")} {work.GradientEvaluations} · {UiLocalization.Text("追迹")} {work.TracedRays} · {work.Elapsed.TotalSeconds:F2} s";
    private void ShowReviewCandidate()
    {
        var text = new StringBuilder();
        if (SelectedReviewCandidate is { } item)
        {
            text.AppendLine(item.Id + " · " + UiLocalization.Text(item.Classification));
            text.AppendLine(UiLocalization.Text("结果代码") + ": " + ReviewCode(item.Outcome) + " [" + item.Outcome + "]");
            text.AppendLine(UiLocalization.Text("机械参考 / 光阑 / 像面位置") + $": {Value(item.Topology.Reference.ZMillimeters)} / {Value(item.Topology.Stop.ZMillimeters)} / {Value(item.Topology.Image.ZMillimeters)} mm");
            foreach (var e in item.Topology.Elements)
            {
                Face(e.FrontSurfaceId, e.FrontZMillimeters, e.Material, e.Front);
                Face(e.BackSurfaceId, e.BackZMillimeters, "Air", e.Back);
            }
            if (item.Assessment is { } a)
            {
                text.AppendLine(UiLocalization.Text("实际焦距 / F数") + $": {Value(a.EffectiveFocalLengthMillimeters)} mm / {Value(a.FNumber)}");
                for (var i = 0; i < a.Fields.Length; i++)
                {
                    var f = a.Fields[i];
                    text.AppendLine(UiLocalization.Text("视场编号") + $" {i + 1}: RMS {Value(f.RmsRadiusMillimeters * 1000)} µm; "
                        + UiLocalization.Text("最大半径 µm") + $" {Value(f.MaximumRadiusMillimeters * 1000)}; "
                        + UiLocalization.Text("有效 / 尝试") + $" {f.ValidRays}/{f.AttemptedRays}; "
                        + UiLocalization.Text("边界 / 中心") + $" {f.CheckValidRays}/{f.CheckAttemptedRays}");
                }
                foreach (var v in a.Violations) text.AppendLine($"{v.Code}: {Value(v.Actual)} / {Value(v.Limit)}");
            }
            void Face(string id, double z, string material, TopologyFace f) => text.AppendLine($"{id}: z={Value(z)} mm; {material}; "
                + UiLocalization.Text("净半口径 mm") + $"={Value(f.ClearSemiDiameterMillimeters)}; "
                + UiLocalization.Text("机械半口径 mm") + $"={Value(f.MechanicalSemiDiameterMillimeters)}; c={Value(f.Profile.CurvaturePerMillimeter)}; k={Value(f.Profile.Conic)}; "
                + string.Join("; ", f.Profile.Terms.Select(t => $"r{t.Power}={Value(t.Coefficient)}")));
        }
        _reviewDetails.Text = text.ToString();
        foreach (var (button, enabled) in _reviewActions) button.IsEnabled = !IsBusy && enabled();
    }
    private async Task ExportReviewReportAsync()
    {
        var review = _review ?? throw new InvalidOperationException(UiLocalization.Text("请先载入记录"));
        var path = await PickSave("design-report"); if (path is null) return;
        // UI localization is captured into a fixed dictionary before background formatting.
        var labels = DesignReviewLabels.All.ToDictionary(x => x, UiLocalization.Text, StringComparer.Ordinal);
        await Task.Run(() => AutonomousDesignReport.SaveMarkdownAsync(review, Extension(path, ".md"),
            key => labels.GetValueOrDefault(key, key), value => ReviewCodeForLanguage(value, labels)));
        _status.Text = UiLocalization.Text("设计报告已导出，原记录和验收结论保持");
    }
    private async Task ExportReviewDataAsync()
    {
        if (_review is not { } review) return;
        var path = await PickSave("design-study"); if (path is null) return;
        await Task.Run(() => AutonomousDesignStudyFiles.SaveStudyAsync(review.Study, Extension(path, ".design-study.json")));
        _status.Text = UiLocalization.Text("记录数据已导出，仅供查看，不替代原检查点");
    }
    private async Task OpenReviewDataAsync()
    {
        var path = await PickOpen("design-study"); if (path is null) return;
        var study = await Task.Run(() => AutonomousDesignStudyFiles.LoadStudyAsync(path));
        _viewingPortfolio = true; PresentReview(AutonomousDesignReview.Build(study));
        _status.Text = UiLocalization.Text("只读记录已载入，当前搜索任务保持不变");
    }
    private async Task ExportReviewCandidateAsync()
    {
        if (_review is not { } review || SelectedReviewCandidate is not { CanExport: true } candidate) return;
        var path = await PickSave("optic"); if (path is null) return;
        await AutonomousDesignFiles.ExportAsync(new(candidate.Id, candidate.Parent, candidate.Topology, candidate.Assessment!, 0), review.Study.Plan.Constraints, path);
        _status.Text = UiLocalization.Text(candidate.OriginalQualified ? "达标候选已导出" : "未达标候选已导出");
    }
    private async Task DisplayReviewStructureAsync()
    {
        if (_review is not { } review || SelectedReviewCandidate is not { } candidate) return;
        SetStructureSource(candidate.Topology, candidate.OriginalSpecification ? review.Study.Plan.Constraints
            : candidate.Assessment!.StructuralConstraints, candidate.Id, candidate.Classification, reviewed: true);
        StructurePendingLoad = DisplayStructureAsync(fromReview: true); await StructurePendingLoad;
        _autonomousTabs!.SelectedItem = _structureTab;
    }
    private Task AddCurrentStudyAsync()
    {
        var study = CaptureCurrentStudy();
        if (_portfolio.Entries.Any(e => e.SourceFingerprint == study.SourceFingerprint))
        { _status.Text = UiLocalization.Text("方案库已包含此记录版本"); return Task.CompletedTask; }
        var next = _portfolio with { Entries = _portfolio.Entries.Add(study) }; AutonomousDesignStudyFiles.Validate(next);
        _portfolio = next; RefreshPortfolioEntries(); _status.Text = UiLocalization.Text("当前记录已收录，搜索状态未改变");
        return Task.CompletedTask;
    }
    private async Task OpenPortfolioAsync()
    {
        var path = await PickOpen("design-portfolio"); if (path is null) return;
        var portfolio = await Task.Run(() => AutonomousDesignStudyFiles.LoadPortfolioAsync(path));
        _portfolio = portfolio; RefreshPortfolioEntries();
        _status.Text = UiLocalization.Text("方案库已载入，当前搜索任务保持不变");
    }
    private async Task SavePortfolioAsync()
    {
        var path = await PickSave("design-portfolio"); if (path is null) return;
        var portfolio = _portfolio;
        await Task.Run(() => AutonomousDesignStudyFiles.SavePortfolioAsync(portfolio, Extension(path, ".design-portfolio.json")));
        _status.Text = UiLocalization.Text("方案库已保存，包含原记录证据");
    }
    private void RefreshPortfolioEntries()
    {
        _settingPortfolio = true;
        try { _portfolioEntries.ItemsSource = _portfolio.Entries; _portfolioEntries.SelectedIndex = -1; }
        finally { _settingPortfolio = false; }
    }
    private static string Extension(string path, string suffix) => path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? path : path + suffix;
    private static string ReviewCode(string value) => ReviewCodeForLanguage(value, DesignReviewLabels.All.ToDictionary(x => x, UiLocalization.Text, StringComparer.Ordinal));
    private static string ReviewCodeForLanguage(string value, IReadOnlyDictionary<string, string> labels)
    {
        var key = DesignReviewLabels.CodeKeys.GetValueOrDefault(value, value);
        return labels.GetValueOrDefault(key, key);
    }
    private sealed record ReviewCandidateRow(string Id, string Origin, string Status, string Outcome, int Elements, DesignReviewCandidate Candidate);
    private sealed record ReviewDecisionRow(string Id, string Parent, string Stage, string Outcome, string Work, string Detail, DesignReviewDecision Decision);
}

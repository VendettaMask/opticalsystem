using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using OptilandWorkbench.InitialStructure.Contracts;
using OptilandWorkbench.InitialStructure.Engine;
using OptilandWorkbench.InitialStructure.Persistence;

namespace OptilandWorkbench.InitialStructure.App;

internal sealed record LabDesktopSettings
{
    public string RunDirectory { get; init; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpticalSystemDesign", "Labs", "InitialStructure", "flat-start", "runs");
    public InitialStructureSpecification? InitialSpecification { get; init; }
    public FlatStartSearchOptions? InitialOptions { get; init; }
    public Func<Window, string, Task<string?>>? SavePicker { get; init; }
    public Func<Window, Task<string?>>? OpenPicker { get; init; }
}

public sealed partial class MainWindow
{
    private FlatStartSearchCheckpoint? _current;
    private CandidateRow? _comparisonA;
    private CandidateRow? _comparisonB;
    private CancellationTokenSource? _runCancellation;
    private bool _busy, _running, _resumeLoading, _hasLastRun, _closed, _closeRequested, _allowClose;
    private int _runGeneration, _resumeRefreshGeneration;

    private void WireEvents()
    {
        Hook(_validateButton, ValidateAsync);
        Hook(_runButton, StartNewAsync);
        Hook(_resumeButton, ResumeAsync);
        Hook(_openButton, OpenRunAsync);
        Hook(_refineButton, RefineAsync);
        Hook(_exportButton, ExportAsync);
        _cancelButton.Click += (_, _) =>
        {
            _runCancellation?.Cancel();
            _cancelButton.IsEnabled = false;
            _status.Text = "正在结束当前批次并保存，请稍候。";
        };
        _compareAButton.Click += (_, _) => SetComparison(true);
        _compareBButton.Click += (_, _) => SetComparison(false);
        _clearComparisonButton.Click += (_, _) => { _comparisonA = _comparisonB = null; RefreshComparison(); UpdateCommands(); };
        _candidateGrid.SelectionChanged += (_, _) => SelectionChanged();
        _preview.Changed += () => _previewCaption.Text = _preview.IsLoading ? "正在读取实际布局与光线…"
            : _preview.Error is { } error ? $"布局未完成：{error}"
            : _preview.PrimaryScene is { } scene ? $"主波长 · 全部已定义视场 · 每视场 7 个瞳采样 · A 共 {scene.Rays.Count} 条轨迹 · mm"
            : "选择方案查看实际布局与光线。";
        SizeChanged += (_, args) => ApplyResponsiveLayout(args.NewSize.Width);
        foreach (var number in _form.GetLogicalDescendants().OfType<NumericUpDown>().Where(number => number != _refineBudget))
        {
            number.ValueChanged += (_, _) => ParametersChanged();
            number.PropertyChanged += (_, args) => { if (args.Property == NumericUpDown.TextProperty) ParametersChanged(); };
        }
        foreach (var input in new[] { _name, _glasses, _catalogs, _initialGlass })
            input.PropertyChanged += (_, args) => { if (args.Property == TextBox.TextProperty) ParametersChanged(); };
        _apertureMode.SelectionChanged += (_, _) => ParametersChanged();
        _fixedBack.IsCheckedChanged += (_, _) => ParametersChanged();
        Closing += (_, args) =>
        {
            if (_allowClose || !_busy) return;
            args.Cancel = true;
            if (_closeRequested) return;
            _closeRequested = true;
            _runCancellation?.Cancel();
            _status.Text = "正在停止并保存，完成后关闭窗口。";
            _ = CloseAfterOperationAsync();
        };
        Closed += (_, _) => { _closed = true; _runGeneration++; _resumeRefreshGeneration++; _preview.Clear(); _runCancellation?.Dispose(); };
    }
    private void Hook(Button button, Func<Task> action) => button.Click += (_, _) =>
    {
        if (_busy || _closed) return;
        PendingOperation = PerformAsync(action);
    };
    private async Task PerformAsync(Func<Task> action)
    {
        _busy = true; _resumeRefreshGeneration++; UpdateCommands();
        try { await action(); }
        catch (InitialStructureSpecificationException exception) { _status.Text = "参数需要调整：" + string.Join("；", exception.Errors); }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException
            or ArgumentException or ArithmeticException or NotSupportedException or KeyNotFoundException)
        {
            _status.Text = "未完成：" + exception.Message;
        }
        finally
        {
            _busy = _running = false;
            _runCancellation?.Dispose(); _runCancellation = null;
            if (!_closed) UpdateCommands();
        }
    }
    private async Task CloseAfterOperationAsync()
    {
        await PendingOperation;
        _allowClose = true;
        Close();
    }
    private void ParametersChanged()
    {
        if (_busy || _current is null) return;
        ClearResults();
        _status.Text = "参数已修改，将创建新的实验；原结果仍保存在实验记录中。";
    }
    private void UpdateCommands()
    {
        var selected = _candidateGrid.SelectedItem is CandidateRow;
        _form.IsEnabled = !_busy;
        _candidateGrid.IsEnabled = !_busy || _running;
        _validateButton.IsEnabled = _runButton.IsEnabled = _openButton.IsEnabled = !_busy;
        _resumeButton.Content = _current is null ? "上次实验" : "继续运行";
        _resumeButton.IsEnabled = !_busy && !_resumeLoading && (_current is null ? _hasLastRun : CanResume(_current));
        _cancelButton.IsEnabled = _running && _runCancellation?.IsCancellationRequested == false;
        _refineButton.IsEnabled = _exportButton.IsEnabled = _compareAButton.IsEnabled = _compareBButton.IsEnabled = !_busy && selected;
        _clearComparisonButton.IsEnabled = !_busy && (_comparisonA is not null || _comparisonB is not null);
    }
    private static bool CanResume(FlatStartSearchCheckpoint checkpoint) => checkpoint.UsableGlassNames.Count > 0
        && checkpoint.ChargedEvaluations < checkpoint.Specification.Budget.MaximumEvaluations
        && checkpoint.ElapsedComputeTicks < checkpoint.Specification.Budget.TimeLimit.Ticks && checkpoint.Trials.Count < 256;

    private async Task ValidateAsync()
    {
        var spec = BuildSpecification(); var options = BuildOptions();
        SpecificationValidator.Validate(spec);
        _status.Text = "正在检查设计目标和目录玻璃…";
        var result = await Task.Run(() => FlatStartSearchService.Preflight(spec, options));
        MaterialMessages(options, result.UsableGlassNames, result.Diagnostics);
        _status.Text = result.UsableGlassNames.Count == 0 ? "没有可用的目录玻璃，请调整材料或波长。" : "预检查通过，可以从平板开始生成。";
    }
    private async Task StartNewAsync()
    {
        var spec = BuildSpecification(); var options = BuildOptions();
        SpecificationValidator.Validate(spec);
        await RunSearchAsync(spec, options, null);
    }
    private async Task ResumeAsync()
    {
        if (_current is { } current)
        {
            if (CanResume(current)) await RunSearchAsync(current.Specification, current.Options, current);
            return;
        }
        _resumeLoading = true;
        try
        {
            ClearResults();
            var saved = await Task.Run(() => _library.LoadLastAsync());
            if (saved is null) { _status.Text = "没有上次实验记录。"; return; }
            ShowLoaded(saved);
        }
        finally { _resumeLoading = false; }
    }
    private async Task OpenRunAsync()
    {
        var path = _settings.OpenPicker is { } choose ? await choose(this) : await PickOpenAsync();
        if (string.IsNullOrWhiteSpace(path)) return;
        ClearResults();
        var saved = await Task.Run(() => new FlatStartSearchCheckpointStore().LoadAsync(path));
        ShowLoaded(saved);
    }
    private void ShowLoaded(FlatStartSearchCheckpoint checkpoint)
    {
        ApplySpecification(checkpoint.Specification, checkpoint.Options);
        Publish(checkpoint, _runGeneration);
        _status.Text = CanResume(checkpoint) ? "已打开实验记录。点击“继续运行”使用原有剩余预算。" : "已打开已结束的实验，可查看、导出或给选中方案追加细化。";
    }
    private async Task RefineAsync()
    {
        if (_current is not { } source || _candidateGrid.SelectedItem is not CandidateRow selected) return;
        var budget = Integer(_refineBudget);
        var child = await Task.Run(() => FlatStartSearchService.CreateRefinementCheckpoint(source, selected.Candidate.CandidateId, budget, source.Specification.Budget.TimeLimit));
        ApplySpecification(child.Specification, child.Options);
        await RunSearchAsync(child.Specification, child.Options, child);
    }

    private async Task RunSearchAsync(InitialStructureSpecification specification, FlatStartSearchOptions options, FlatStartSearchCheckpoint? checkpoint)
    {
        ClearResults();
        _running = true;
        _runCancellation = new();
        var cancellation = _runCancellation;
        var generation = _runGeneration;
        UpdateCommands();
        _status.Text = checkpoint?.Origin is not null ? "正在细化选中方案，原实验保持不变…" : checkpoint is null ? "正在准备零曲率平板结构…" : "正在恢复原有剩余预算…";
        var result = await Task.Run(() => new FlatStartSearchService().RunAsync(specification, options, cancellation.Token, checkpoint,
            async (saved, token) =>
            {
                await _library.SaveAsync(saved, token);
                await Dispatcher.UIThread.InvokeAsync(() => { if (!_closed) Publish(saved, generation); });
            }));
        Publish(result.Checkpoint, generation);
        _status.Text = result.Checkpoint.State switch
        {
            FlatStartSearchState.Cancelled => "已停止并保存；继续运行会保留已消耗的预算。",
            FlatStartSearchState.NoUsableGlass => "没有可用的目录玻璃，未生成替代材料。",
            FlatStartSearchState.TimeLimit => "已达到时间上限，已完成的方案和过程已保存。",
            _ => "本次搜索已结束，方案和过程已保存。"
        };
    }

    private void Publish(FlatStartSearchCheckpoint checkpoint, int generation)
    {
        if (_closed || generation != _runGeneration) return;
        _current = checkpoint; _hasLastRun = true;
        var selectedId = (_candidateGrid.SelectedItem as CandidateRow)?.Candidate.CandidateId;
        var candidates = FlatStartSearchService.SelectCandidates(checkpoint);
        if (!_rows.Select(row => row.Candidate.CandidateId).SequenceEqual(candidates.Select(candidate => candidate.CandidateId)))
        {
            _rows.Clear();
            for (var index = 0; index < candidates.Count; index++)
            {
                var candidate = candidates[index];
                var trial = checkpoint.Trials.FirstOrDefault(item => item.Candidate?.CandidateId == candidate.CandidateId) ?? checkpoint.Origin!.Source;
                _rows.Add(new CandidateRow(index + 1, trial, candidate.CandidateId == checkpoint.Origin?.Source.Candidate?.CandidateId));
            }
            _candidateGrid.SelectedItem = _rows.FirstOrDefault(row => row.Candidate.CandidateId == selectedId) ?? _rows.FirstOrDefault();
        }
        var accepted = candidates.Count(candidate => candidate.Status == CandidateStatus.LabAccepted);
        _summary.Text = $"{checkpoint.Specification.EffectiveFocalLengthMillimeters:0.###} mm · F/{checkpoint.Specification.FNumber:0.###} · 半视场 {checkpoint.Specification.MaximumFieldAngleDegrees:0.###}°\n"
            + $"当前保留 {_rows.Count} 个方案，{accepted} 个达标；其余方案的具体差距见详情。";
        _progress.Maximum = checkpoint.Specification.Budget.MaximumEvaluations;
        _progress.Value = checkpoint.Trials.Where(trial => trial.State != FamilyTrialState.Reserved).Sum(trial => trial.ChargedEvaluations);
        if (_running)
        {
            _status.Text = _runCancellation?.IsCancellationRequested == true ? "正在结束当前批次并保存…"
                : checkpoint.NextRootIndex < checkpoint.RootPlan.Count ? $"正在探索平板结构 {checkpoint.NextRootIndex}/{checkpoint.RootPlan.Count}，已找到 {accepted} 个达标方案。"
                : $"正在细化方案，已完成 {checkpoint.Trials.Count(trial => trial.State == FamilyTrialState.Completed)} 轮；已找到 {accepted} 个达标方案。";
        }
        MaterialMessages(checkpoint.Options, checkpoint.UsableGlassNames, checkpoint.Diagnostics);
        if (checkpoint.Trials.Any(trial => trial.State is FamilyTrialState.Failed or FamilyTrialState.Interrupted))
            _messages.Text += "\n有失败或中断批次，其已预留额度仍计入预算；详情见运行记录。";
        UpdateCommands();
    }
    private void MaterialMessages(FlatStartSearchOptions options, IReadOnlyList<string> usable, IReadOnlyList<SearchDiagnostic> diagnostics)
    {
        var excluded = options.AllowedGlassNames.Except(usable, StringComparer.OrdinalIgnoreCase).ToArray();
        _messages.Text = excluded.Length == 0 ? "" : $"未采用的玻璃：{string.Join("、", excluded)}。可能缺少材料、超出波长范围或与已选目录项重复；其余可用材料继续搜索。";
        if (diagnostics.Any(diagnostic => diagnostic.Code == "search.incomplete-coverage")) _messages.Text += "\n当前预算或结构数量不足以覆盖全部初始家族。";
    }

    private void ClearResults()
    {
        _runGeneration++;
        _current = null; _comparisonA = _comparisonB = null;
        _rows.Clear(); _candidateGrid.SelectedItem = null;
        _preview.Clear();
        _targetGrid.ItemsSource = _prescriptionGrid.ItemsSource = _fieldGrid.ItemsSource = null;
        _selectionDetails.Text = _messages.Text = "";
        _summary.Text = "尚未生成方案。"; _progress.Value = 0;
        _comparisonDetails.Text = "A 为蓝色，B 为橙色；共用毫米比例。";
        UpdateCommands();
    }
    private async Task RefreshResumeAvailabilityAsync()
    {
        var generation = ++_resumeRefreshGeneration;
        try
        {
            var last = await Task.Run(() => _library.LoadLastAsync());
            if (generation != _resumeRefreshGeneration || _closed) return;
            _hasLastRun = last is not null;
            UpdateCommands();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException
            or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            if (generation == _resumeRefreshGeneration && !_closed) _messages.Text = "上次实验记录无法读取：" + exception.Message;
        }
    }

    private async Task ExportAsync()
    {
        if (_candidateGrid.SelectedItem is not CandidateRow selected) return;
        var path = _settings.SavePicker is { } picker ? await picker(this, selected.Candidate.CandidateId) : await PickSaveAsync(selected.Candidate.CandidateId);
        if (string.IsNullOrWhiteSpace(path)) return;
        await new CandidateExportService().ExportStarOptAsync(selected.Candidate, path, CancellationToken.None);
        _status.Text = $"{selected.Name} 已导出并回读验证。";
    }
    private async Task<string?> PickSaveAsync(string name)
    {
        var target = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "导出初始结构",
            SuggestedFileName = name + ".staropt",
            DefaultExtension = "staropt",
            FileTypeChoices = [new("STAROPT 工程") { Patterns = ["*.staropt"] }]
        });
        return target?.TryGetLocalPath();
    }
    private async Task<string?> PickOpenAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "打开平板生成实验记录",
            AllowMultiple = false,
            FileTypeFilter = [new("实验记录") { Patterns = ["*.json"] }]
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    private void SelectionChanged()
    {
        if (_candidateGrid.SelectedItem is CandidateRow row && _current is { } checkpoint)
        {
            _targetGrid.ItemsSource = row.Targets(checkpoint.Specification);
            _prescriptionGrid.ItemsSource = row.Prescription();
            _fieldGrid.ItemsSource = row.Fields();
            _selectionDetails.Text = row.Details(checkpoint);
        }
        else
        {
            _targetGrid.ItemsSource = _prescriptionGrid.ItemsSource = _fieldGrid.ItemsSource = null;
            _selectionDetails.Text = "";
        }
        RefreshComparison(); UpdateCommands();
    }
    private void SetComparison(bool isPrimary)
    {
        if (_candidateGrid.SelectedItem is not CandidateRow selected) return;
        if (isPrimary) _comparisonA = selected; else _comparisonB = selected;
        RefreshComparison(); UpdateCommands();
    }
    private void RefreshComparison()
    {
        var primary = _comparisonA ?? _candidateGrid.SelectedItem as CandidateRow;
        _ = _preview.LoadAsync(primary?.Candidate, _comparisonB?.Candidate);
        _comparisonDetails.Text = _comparisonB is { } b ? $"A：{primary?.Name ?? "未选择"}，RMS {primary?.Rms ?? "—"} mm；B：{b.Name}，RMS {b.Rms} mm。蓝／橙色共用毫米比例。"
            : $"{primary?.Name ?? "未选择方案"} · 蓝色为当前或 A；可设置 B 比较。";
    }
}

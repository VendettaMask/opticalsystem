using System.Collections.Immutable;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

namespace OpticalSystemDesign.App.Laboratories.InitialStructure;

public sealed partial class AutonomousTopologyWindow
{
    private async Task PerformAsync(Func<Task> action)
    {
        _uiOperation = true; _operationFailed = false; _error.Text = ""; Refresh();
        try { await action(); }
        catch (OperationCanceledException) { _status.Text = UiLocalization.Text("操作已取消"); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException or NotSupportedException or ArithmeticException)
        { _operationFailed = true; _error.Text = error.Message; _status.Text = UiLocalization.Text("自主任务未完成"); }
        finally
        {
            _uiOperation = false;
            if (!_closed) Refresh();
            if (_closeRequested && !_session.IsBusy && !_operationFailed) { _allowClose = true; Close(); }
            else if (_operationFailed) _closeRequested = false;
        }
    }

    private Task NewDraft()
    {
        _session.Reset(); _generation++; _displayRevision = -1; _displayRun = null;
        _draft = AutonomousDesignExamples.ThreeElement(); ApplyPlan(_draft);
        _candidateRows.Clear(); _eventRows.Clear(); _prescriptionRows.Clear(); _fieldRows.Clear();
        _details.Text = UiLocalization.Text("选择候选查看独立验收结果。");
        _status.Text = UiLocalization.Text("新计划尚未运行");
        return Task.CompletedTask;
    }

    private async Task ImportAsync()
    {
        var path = await PickOpen("seed"); if (path is null) return;
        var seed = await AutonomousDesignFiles.ImportSeedAsync(path, _draft.Constraints);
        _draft = _draft with { Seeds = [seed] }; ApplyPlan(_draft);
        _status.Text = UiLocalization.Text("起点已载入，尚未运行");
    }
    private async Task OpenPlanAsync()
    {
        var path = await PickOpen("plan"); if (path is null) return;
        var plan = await AutonomousDesignFiles.LoadPlanAsync(path);
        ApplyPlan(plan); _session.Reset(); _generation++; _draft = plan;
        _candidateRows.Clear(); _eventRows.Clear(); _prescriptionRows.Clear(); _fieldRows.Clear();
        _status.Text = UiLocalization.Text(AutonomousSearchService.IsHistoricalReadOnlyPlan(plan)
            ? "此通光原型仅供查看和导出，请新建版本化计划" : "计划已载入，尚未运行");
    }
    private async Task SavePlanAsync()
    {
        var plan = _session.Checkpoint?.Plan ?? ReadPlan();
        var path = await PickSave("plan"); if (path is null) return;
        await AutonomousDesignFiles.SavePlanAsync(plan, path);
        _status.Text = UiLocalization.Text("设计计划已保存");
    }
    private async Task OpenRecordAsync()
    {
        var path = await PickOpen("record"); if (path is null) return;
        if (path.EndsWith(".family.json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(UiLocalization.Text("AutoLens 记录请从原入口打开"));
        await _session.LoadAsync(path, ApplyPlan);
        // An explicitly opened backup is authoritative even when this run was previously displayed at a later revision.
        _displayRevision = -1; _displayRun = null;
        _generation++; _draft = _session.Checkpoint!.Plan; ApplyPlan(_draft); Publish(_session.Checkpoint);
        _status.Text = UiLocalization.Text(AutonomousSearchService.IsHistoricalReadOnlyPlan(_draft)
            ? "此通光原型仅供查看和导出，请新建版本化计划" : "记录已载入，尚未继续运行");
    }
    private Task CheckAsync()
    {
        _draft = ReadPlan(); _ = AutonomousSearchService.Create("desktop-check", _draft);
        _status.Text = UiLocalization.Text(AutonomousSearchService.IsHistoricalReadOnlyPlan(_draft)
            ? "此通光原型仅供查看和导出，请新建版本化计划" : "输入有效，尚未进行光学验收");
        return Task.CompletedTask;
    }
    private async Task RunAsync(bool resume)
    {
        CancelStructureLoad();
        var plan = resume ? _session.Checkpoint!.Plan : ReadPlan();
        _draft = plan;
        var generation = ++_generation;
        _status.Text = UiLocalization.Text("自主设计正在运行");
        try
        {
            await _session.RunAsync(plan, resume,
                state => Dispatcher.UIThread.Post(() => { if (!_closed && generation == _generation) Publish(state); }),
                (pending, step) => Dispatcher.UIThread.Post(() =>
                {
                    if (!_closed && generation == _generation)
                        _status.Text = UiLocalization.Text("本阶段正在补偿") + $" · {pending.TrialId} · {step.Attempt}";
                }),
                (pending, step) => Dispatcher.UIThread.Post(() =>
                {
                    if (!_closed && generation == _generation)
                        _status.Text = UiLocalization.Text("本阶段正在恢复") + $" · {pending.TrialId} · "
                            + UiLocalization.Text("已接受步骤") + $" {step.AcceptedStep}";
                }));
        }
        finally { _generation++; }
        Publish(_session.Checkpoint!);
        var checkpoint = _session.Checkpoint!;
        _status.Text = UiLocalization.Text(checkpoint.Status switch
        {
            AutonomousSearchStatus.Cancelled => "自主任务已停止并保存",
            AutonomousSearchStatus.BudgetExhausted => "自主任务已达到预算",
            _ when checkpoint.AcceptedArchive.Length > 0 => "自主任务结束，已保存达标结果",
            _ => "自主任务结束，尚无达标结果"
        });
    }
    private void Cancel()
    {
        _seedCancellation?.Cancel();
        _session.Cancel(); _status.Text = UiLocalization.Text("正在停止并保存，完成后可继续。");
    }
    internal void RequestSafeClose()
    {
        if (IsBusy) { _closeRequested = true; Cancel(); }
        else Close();
    }
    private async Task CopyRecordAsync()
    {
        var source = _session.RecordPath ?? throw new InvalidOperationException("No saved autonomous record.");
        var path = await PickSave("record"); if (path is null) return;
        if (Path.GetFullPath(path).Equals(Path.GetFullPath(source), StringComparison.OrdinalIgnoreCase)) return;
        if (File.Exists(path)) throw new IOException(UiLocalization.Text("请为运行记录选择新文件名"));
        var bytes = await BoundedFile.ReadAllBytesAsync(source, InitialStructureLimits.MaximumManifestBytes, "autonomous record");
        var temporary = Path.GetFullPath(path) + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await BoundedFile.WriteAtomicAsync(temporary, InitialStructureLimits.MaximumManifestBytes, "autonomous record",
                (stream, token) => stream.WriteAsync(bytes, token).AsTask());
            File.Move(temporary, path, overwrite: false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        _status.Text = UiLocalization.Text("自主运行记录已复制");
    }
    private async Task ExportAsync()
    {
        if (_candidates.SelectedItem is not TopologyCandidateRow row || _session.Checkpoint is not { } state) return;
        var path = await PickSave("optic"); if (path is null) return;
        await AutonomousDesignFiles.ExportAsync(row.Candidate, state.Plan.Constraints, path);
        _status.Text = UiLocalization.Text(row.Candidate.Assessment.MeetsTargets ? "达标候选已导出" : "未达标候选已导出");
    }

    private void ApplyPlan(AutonomousSearchPlan plan)
    {
        SetStructureSource(plan.Seeds[0], plan.Constraints, "已载入起点", force: true);
        ResetSeedSummary();
        var values = new Dictionary<string, decimal>();
        var source = plan.Seeds[0]; var target = plan.Targets; var options = plan.Options;
        Put("AutonomousFocal", target.EffectiveFocalLengthMillimeters); Put("AutonomousFNumber", target.FNumber);
        var optic = TopologyCompiler.Compile(source, plan.Constraints).CreateOptic();
        Put("AutonomousField", FieldCoordinates.MaximumRadius(optic.Fields)); Put("AutonomousStop", source.Stop.ClearSemiDiameterMillimeters);
        Put("AutonomousRms", target.MaximumRmsRadiusMillimeters * 1000); Put("AutonomousMaximum", target.MaximumSpotRadiusMillimeters * 1000);
        Put("AutonomousMinElements", plan.Constraints.MinimumElementCount); Put("AutonomousMaxElements", plan.Constraints.MaximumElementCount);
        Put("AutonomousTrials", options.MaximumTrials); Put("AutonomousFunctions", options.MaximumFunctionEvaluations);
        Put("AutonomousGradients", options.MaximumGradientEvaluations); Put("AutonomousRays", options.MaximumTracedRays);
        Put("AutonomousReserve", options.FinalValidationRayReserve); Put("AutonomousMinutes", options.TimeLimit.TotalMinutes); Put("AutonomousSeed", options.Seed);
        Put("AutonomousAxialWindow", options.AxialCompensation?.MaximumDisplacementMillimeters ?? .25);
        var seedOptions = options.SeedPreparation ?? new();
        Put("AutonomousLayerLimit", options.BoundedSearch?.MaximumAttemptsPerElementCount ?? 3);
        Put("AutonomousStagnation", options.BoundedSearch?.MaximumUnsuccessfulTrials ?? 2);
        var generation = seedOptions.Generation;
        Put("AutonomousPoolSize", options.CandidatesPerElementCount);
        Put("AutonomousGeneratedElements", generation?.Elements.Length ?? Math.Max(2, source.Elements.Length));
        Put("AutonomousGeneratedStop", generation?.StopAfterElement ?? source.Elements.Count(element => element.BackZMillimeters < source.Stop.ZMillimeters));
        Put("AutonomousGeneratedRadius", generation?.ClearSemiDiameterMillimeters ?? source.Elements.Max(element => element.ClearSemiDiameterMillimeters));
        Put("AutonomousGeneratedMinThickness", generation?.MinimumThicknessMillimeters ?? Math.Max(1, plan.Constraints.MinimumCenterThicknessMillimeters));
        Put("AutonomousGeneratedMaxThickness", generation?.MaximumThicknessMillimeters ?? Math.Max(4, plan.Constraints.MinimumCenterThicknessMillimeters));
        Put("AutonomousGeneratedBackFocus", generation?.MinimumBackFocusMillimeters ?? Math.Max(5, plan.Constraints.MinimumBackFocusMillimeters));
        Put("AutonomousAnnealing", generation?.AnnealingProposals ?? 8);
        Put("AutonomousSeedPerStart", seedOptions.MaximumEvaluationsPerStart);
        Put("AutonomousSeedStarts", seedOptions.MaximumStarts); Put("AutonomousSeedRetained", seedOptions.RetainedSeeds);
        Put("AutonomousSeedFunctions", seedOptions.MaximumFunctionEvaluations); Put("AutonomousSeedSeconds", seedOptions.TimeLimit.TotalSeconds);
        Put("AutonomousLocalUpdates", options.Local.MaximumUpdates); Put("AutonomousFullUpdates", options.Full.MaximumUpdates);
        _applyingPlan = true;
        _choices["AutonomousImage"].IsChecked = !source.Image.Protected;
        _choices["AutonomousStructure"].IsChecked = options.FixedTopologyOnly != true;
        _choices["AutonomousReductionOnly"].IsChecked = options.QualifiedReductionOnly == true;
        _choices["AutonomousDiversity"].IsChecked = options.PoolDiversity is not null;
        _choices["AutonomousPopulation"].IsChecked = options.PopulationExploration is not null;
        _choices["AutonomousBounded"].IsChecked = options.BoundedSearch is not null;
        _choices["AutonomousGeometryScreen"].IsChecked = options.PopulationExploration?.Geometry is not null;
        _choices["AutonomousFixedStopRecovery"].IsChecked = seedOptions.FixedStopRecoveryVersion == 2;
        _choices["AutonomousDescriptor"].IsChecked = generation is not null;
        _choices["AutonomousDelano"].IsChecked = generation?.Method == StructureGenerationMethod.Delano;
        _choices["AutonomousThickDelano"].IsChecked = generation?.MaterializationVersion == 2;
        _choices["AutonomousGradual"].IsChecked = options.Expansion?.EnableGradualDeletion == true;
        _choices["AutonomousSplit"].IsChecked = options.Expansion?.EnableSplit == true;
        _choices["AutonomousMerge"].IsChecked = options.Expansion?.EnableMerge == true;
        _choices["AutonomousMaterial"].IsChecked = options.Expansion?.EnableMaterial == true;
        _choices["AutonomousProfile"].IsChecked = options.Expansion?.EnableProfile == true;
        _choices["AutonomousPrecision"].IsChecked = HighPrecision(options);
        _choices["AutonomousAxial"].IsChecked = options.AxialCompensation is not null;
        _choices["AutonomousMatrixDerivatives"].IsChecked = options.Recovery.UseAutomaticMatrixDerivatives == true;
        _choices["AutonomousGeometryRecovery"].IsChecked = options.Recovery.UseGeometryConstraintTangents == true;
        _choices["AutonomousPupilRecovery"].IsChecked = options.Recovery.RestorePupilMagnification == true;
        _choices["AutonomousRecoveryAxial"].IsChecked = options.UseAxialVariablesInRecovery == true;
        _choices["AutonomousConic"].IsChecked = options.CompensateConic == true;
        _choices["AutonomousStageChecks"].IsChecked = options.CheckStageFeasibility == true;
        _choices["AutonomousProtectLocalBudget"].IsChecked = options.ProtectPostRecoveryRayBudget == true;
        _choices["AutonomousProtectFullBudget"].IsChecked = options.ProtectCompensationRayBudget == true;
        foreach (var pair in values) _numbers[pair.Key].Value = pair.Value;
        _applyingPlan = false; _planEdited = false;
        _source.Text = UiLocalization.Text("无限远起点") + $" · {plan.Seeds.Length} · " + string.Join(", ", plan.Seeds.Select(seed => seed.Name + $" ({seed.Elements.Length})"));
        void Put(string id, double value)
        {
            var control = _numbers[id];
            if (!double.IsFinite(value) || value < (double)control.Minimum || value > (double)control.Maximum)
                throw new InvalidDataException("The loaded plan cannot be represented by this desktop editor: " + id);
            values.Add(id, (decimal)value);
        }
    }

    private AutonomousSearchPlan ReadPlan()
    {
        if (_session.Checkpoint is { } checkpoint) return checkpoint.Plan;
        if (!_planEdited) return _draft;
        double Read(string id)
        {
            var control = _numbers[id];
            if (control.Value is not { } value || !decimal.TryParse(control.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var text)
                || Math.Abs(text - value) > .00000001m) throw new ArgumentException(UiLocalization.Text("请填写有效数字"));
            if (id is "AutonomousMinElements" or "AutonomousMaxElements" or "AutonomousTrials" or "AutonomousFunctions" or "AutonomousGradients" or "AutonomousRays" or "AutonomousReserve" or "AutonomousSeed"
                or "AutonomousLocalUpdates" or "AutonomousFullUpdates"
                && decimal.Truncate(value) != value) throw new ArgumentException(UiLocalization.Text("请填写有效整数"));
            return (double)value;
        }
        double PreservedValue(string id, double original, double displayScale = 1)
        {
            var edited = Read(id);
            return _numbers[id].Value == (decimal)(original * displayScale) ? original : edited / displayScale;
        }
        var movable = _choices["AutonomousImage"].IsChecked == true;
        var seeds = _draft.Seeds.Select(seed =>
        {
            var optic = TopologyCompiler.Compile(seed, _draft.Constraints).CreateOptic();
            var maximum = FieldCoordinates.MaximumRadius(optic.Fields);
            var field = PreservedValue("AutonomousField", maximum);
            var fields = field == maximum ? seed.Fields : maximum > 0
                ? seed.Fields.Select(item => item with { XAngleDegrees = item.XAngleDegrees * field / maximum, YAngleDegrees = item.YAngleDegrees * field / maximum }).ToImmutableArray()
                : ImmutableArray.Create(new TopologyField("Axis", 0, 0), new TopologyField("Edge", 0, field));
            return seed with { Fields = fields, Stop = seed.Stop with { ClearSemiDiameterMillimeters = PreservedValue("AutonomousStop", seed.Stop.ClearSemiDiameterMillimeters) }, Image = seed.Image with { Protected = !movable } };
        }).ToImmutableArray();
        var structure = _choices["AutonomousStructure"].IsChecked == true;
        var totalRays = (long)Read("AutonomousRays");
        var minutes = Read("AutonomousMinutes");
        var options = _draft.Options with
        {
            Recovery = _draft.Options.Recovery with
            {
                UseAutomaticMatrixDerivatives = ReadFlag("AutonomousMatrixDerivatives", _draft.Options.Recovery.UseAutomaticMatrixDerivatives),
                RestorePupilMagnification = ReadFlag("AutonomousPupilRecovery", _draft.Options.Recovery.RestorePupilMagnification),
                UseGeometryConstraintTangents = ReadFlag("AutonomousGeometryRecovery", _draft.Options.Recovery.UseGeometryConstraintTangents)
            },
            UseAxialVariablesInRecovery = ReadFlag("AutonomousRecoveryAxial", _draft.Options.UseAxialVariablesInRecovery),
            CompensateConic = ReadFlag("AutonomousConic", _draft.Options.CompensateConic),
            CheckStageFeasibility = ReadFlag("AutonomousStageChecks", _draft.Options.CheckStageFeasibility),
            ProtectPostRecoveryRayBudget = ReadFlag("AutonomousProtectLocalBudget", _draft.Options.ProtectPostRecoveryRayBudget),
            ProtectCompensationRayBudget = ReadFlag("AutonomousProtectFullBudget", _draft.Options.ProtectCompensationRayBudget),
            QualifiedReductionOnly = ReadFlag("AutonomousReductionOnly", _draft.Options.QualifiedReductionOnly),
            PoolDiversity = _choices["AutonomousDiversity"].IsChecked == true ? _draft.Options.PoolDiversity ?? new TopologyDiversityOptions() : null,
            SeedPreparation = ReadSeedSettings(),
            PopulationExploration = _choices["AutonomousPopulation"].IsChecked == true
                ? (_draft.Options.PopulationExploration ?? new PopulationExplorationOptions()) with
                { Geometry = _choices["AutonomousGeometryScreen"].IsChecked == true ? _draft.Options.PopulationExploration?.Geometry ?? new() : null } : null,
            BoundedSearch = _choices["AutonomousBounded"].IsChecked == true
                ? (_draft.Options.BoundedSearch ?? new()) with
                {
                    MaximumAttemptsPerElementCount = Whole("AutonomousLayerLimit"),
                    MaximumUnsuccessfulTrials = Whole("AutonomousStagnation")
                } : null,
            CandidatesPerElementCount = Whole("AutonomousPoolSize"),
            Local = ReadStage("AutonomousLocalUpdates", _draft.Options.Local),
            Full = ReadStage("AutonomousFullUpdates", _draft.Options.Full),
            AxialCompensation = _choices["AutonomousAxial"].IsChecked == true
                ? (_draft.Options.AxialCompensation ?? new TopologyAxialCompensationOptions()) with
                { MaximumDisplacementMillimeters = PreservedValue("AutonomousAxialWindow", _draft.Options.AxialCompensation?.MaximumDisplacementMillimeters ?? .25) } : null,
            MaximumTrials = (int)Read("AutonomousTrials"),
            MaximumFunctionEvaluations = (int)Read("AutonomousFunctions"),
            MaximumGradientEvaluations = (int)Read("AutonomousGradients"),
            MaximumTracedRays = totalRays,
            MaximumStageTracedRays = Math.Min(_draft.Options.MaximumStageTracedRays, totalRays),
            FinalValidationRayReserve = (long)Read("AutonomousReserve"),
            TimeLimit = _numbers["AutonomousMinutes"].Value == (decimal)_draft.Options.TimeLimit.TotalMinutes
                ? _draft.Options.TimeLimit : TimeSpan.FromMinutes(minutes),
            Seed = (long)Read("AutonomousSeed"),
            FixedTopologyOnly = structure == (_draft.Options.FixedTopologyOnly != true) ? _draft.Options.FixedTopologyOnly : structure ? null : true,
            Expansion = ReadExpansion()
        };
        // Editing a budget must not silently replace an imported plan's custom training grid.
        if ((_choices["AutonomousPrecision"].IsChecked == true) != HighPrecision(_draft.Options))
        {
            var accurate = _choices["AutonomousPrecision"].IsChecked == true;
            FixedTopologyOptions Grid(FixedTopologyOptions stage) => stage with
            { Sampling = stage.Sampling with { PupilRings = accurate ? 4 : 3, PupilArms = accurate ? 16 : 12 } };
            options = options with { Local = Grid(options.Local), Full = Grid(options.Full) };
        }
        var plan = _draft with
        {
            Seeds = seeds,
            Constraints = _draft.Constraints with
            { MinimumElementCount = (int)Read("AutonomousMinElements"), MaximumElementCount = (int)Read("AutonomousMaxElements") },
            Options = options,
            Targets = _draft.Targets with
            {
                EffectiveFocalLengthMillimeters = PreservedValue("AutonomousFocal", _draft.Targets.EffectiveFocalLengthMillimeters),
                FNumber = PreservedValue("AutonomousFNumber", _draft.Targets.FNumber),
                MaximumRmsRadiusMillimeters = PreservedValue("AutonomousRms", _draft.Targets.MaximumRmsRadiusMillimeters, 1000),
                MaximumSpotRadiusMillimeters = PreservedValue("AutonomousMaximum", _draft.Targets.MaximumSpotRadiusMillimeters, 1000)
            }
        };
        _ = AutonomousSearchService.Create("desktop-check", plan);
        return plan;

        FixedTopologyOptions ReadStage(string id, FixedTopologyOptions original)
        {
            var updates = (int)Read(id);
            return updates == original.MaximumUpdates ? original : original with
            {
                MaximumUpdates = updates,
                MaximumFunctionEvaluations = Math.Max(original.MaximumFunctionEvaluations, updates + 1),
                MaximumGradientEvaluations = Math.Max(original.MaximumGradientEvaluations, updates + 1)
            };
        }
    }

    private bool? ReadFlag(string id, bool? original)
    {
        var selected = _choices[id].IsChecked == true;
        return selected == (original == true) ? original : selected;
    }

    private TopologyExpansionOptions? ReadExpansion()
    {
        var original = _draft.Options.Expansion;
        var gradual = _choices["AutonomousGradual"].IsChecked == true;
        var split = _choices["AutonomousSplit"].IsChecked == true;
        var merge = _choices["AutonomousMerge"].IsChecked == true;
        var material = _choices["AutonomousMaterial"].IsChecked == true;
        var profile = _choices["AutonomousProfile"].IsChecked == true;
        if (_choices["AutonomousReductionOnly"].IsChecked == true && (original is not null || gradual))
            return (original ?? new TopologyExpansionOptions()) with
            {
                EnableDeletion = true,
                EnableInsertion = false,
                EnableSplit = false,
                EnableMerge = false,
                EnableMaterial = false,
                EnableProfile = false,
                EnableGradualDeletion = gradual
            };
        if (gradual == (original?.EnableGradualDeletion == true) && split == (original?.EnableSplit == true) && merge == (original?.EnableMerge == true)
            && material == (original?.EnableMaterial == true) && profile == (original?.EnableProfile == true)) return original;
        // The fixed-topology switch controls dispatch, while inactive branch permissions remain editable and preserved.
        return (original ?? new TopologyExpansionOptions()) with
        {
            EnableGradualDeletion = gradual,
            EnableSplit = split,
            EnableMerge = merge,
            EnableMaterial = material,
            EnableProfile = profile
        };
    }

    private static bool HighPrecision(AutonomousSearchOptions options)
        => new[] { options.Local, options.Full }.All(stage => stage.Sampling.PupilRings == 4 && stage.Sampling.PupilArms == 16);

    private void Publish(AutonomousSearchCheckpoint state)
    {
        if (_displayRun == state.RunId && state.Revision < _displayRevision) return;
        _displayRun = state.RunId; _displayRevision = state.Revision;
        var selected = (_candidates.SelectedItem as TopologyCandidateRow)?.Candidate.Id;
        _candidateRows.Clear();
        foreach (var candidate in state.Candidates.OrderByDescending(item => item.Assessment.MeetsTargets).ThenBy(item => item.Score))
        {
            var assessment = candidate.Assessment;
            var rms = assessment.Fields.Length > 0 && assessment.Fields.All(item => item.RmsRadiusMillimeters is not null)
                ? assessment.Fields.Max(item => item.RmsRadiusMillimeters) * 1000 : null;
            var throughput = assessment.Fields.Length > 0 ? assessment.Fields.Min(item => (double)item.ValidRays / item.AttemptedRays) : (double?)null;
            _candidateRows.Add(new(candidate.Id, candidate.ParentId ?? "—", UiLocalization.Text(assessment.MeetsTargets ? "达标" : "未达标"),
                candidate.Topology.Elements.Length, Value(assessment.EffectiveFocalLengthMillimeters), Value(assessment.FNumber), Value(rms), Value(throughput, "P2"), candidate));
        }
        _eventRows.Clear();
        foreach (var entry in state.Events.TakeLast(1000))
            _eventRows.Add(new(entry.Sequence, entry.TrialId, entry.ParentId ?? "—", Operation(entry.Operation), Phase(entry.Phase), Outcome(entry.Outcome),
                $"{entry.Work.FunctionEvaluations} / {entry.Work.GradientEvaluations} / {entry.Work.TracedRays}",
                entry.Outcome == "InterruptedUnknownCharged" ? UiLocalization.Text("未记录")
                    : $"{entry.Work.MatrixEvaluations} / {entry.Work.JacobianBuilds} / {entry.Work.DifferenceEvaluations}",
                entry.Recovery?.Work.PupilEvaluations?.ToString(CultureInfo.CurrentCulture) ?? UiLocalization.Text("未记录"), entry.Detail ?? ""));
        _candidates.SelectedItem = _candidateRows.FirstOrDefault(row => row.Candidate.Id == selected) ?? _candidateRows.FirstOrDefault();
        if (_session.IsBusy && state.Pending is { Phase: AutonomousSearchPhase.Recovery } pending
            && state.Reservation?.Phase == AutonomousSearchPhase.Recovery)
            _status.Text = UiLocalization.Text("本阶段正在恢复") + $" · {pending.TrialId}";
        Refresh();
    }

    private void Refresh()
    {
        RefreshStructureText();
        var state = _session.Checkpoint; var editable = !IsBusy && state is null;
        var readOnly = AutonomousSearchService.IsHistoricalReadOnlyPlan(state?.Plan ?? _draft);
        foreach (var number in _numbers.Values) { number.IsEnabled = editable; ToolTip.SetTip(number, editable ? null : UiLocalization.Text("请新建计划以修改输入，继续任务保持原计划")); }
        foreach (var choice in _choices.Values)
        {
            choice.IsEnabled = editable;
            ToolTip.SetTip(choice, editable ? null : UiLocalization.Text("请新建计划以修改输入，继续任务保持原计划"));
        }
        foreach (var id in new[] { "AutonomousGradual", "AutonomousSplit", "AutonomousMerge", "AutonomousMaterial", "AutonomousProfile" })
            if (editable && _choices["AutonomousStructure"].IsChecked != true)
                ToolTip.SetTip(_choices[id], UiLocalization.Text("固定片数模式不派发结构分支，分支权限保留供后续使用"));
        Hint("AutonomousPupilRecovery", "启用瞳倍率恢复会同时启用矩阵自动求导；关闭自动求导会关闭瞳倍率恢复");
        Hint("AutonomousGeometryRecovery", "使用共享 Core 的几何导数联合求步，仍按原制造门槛严格复核；需要矩阵自动求导");
        Hint("AutonomousConic", "不新增面型分支时独立释放已有面的锥常数；面型分支已有的变量规则保持");
        Hint("AutonomousRecoveryAxial", "沿用本次试验的轴向窗口，不移动机械参考、光阑与固定像面");
        Hint("AutonomousProtectLocalBudget", "可选阶段验收挤占补偿额度时延后检查，仍保留最终独立验收");
        Hint("AutonomousProtectFullBudget", "可选阶段验收挤占补偿额度时延后检查，仍保留最终独立验收");
        Hint("AutonomousReductionOnly", "失败处方保留但不再派发结构分支；达到预算或限定尝试即停止");
        if (editable && _choices["AutonomousReductionOnly"].IsChecked == true)
            foreach (var id in new[] { "AutonomousStructure", "AutonomousSplit", "AutonomousMerge", "AutonomousMaterial", "AutonomousProfile", "AutonomousPopulation" })
            {
                _choices[id].IsEnabled = false;
                ToolTip.SetTip(_choices[id], UiLocalization.Text("减片模式只允许从达标处方删除镜片"));
            }
        if (editable && _choices["AutonomousAxial"].IsChecked != true)
        {
            _choices["AutonomousRecoveryAxial"].IsEnabled = false;
            ToolTip.SetTip(_choices["AutonomousRecoveryAxial"], UiLocalization.Text("请先启用镜面轴向微调，原恢复偏好保持"));
        }
        if (editable && _choices["AutonomousStageChecks"].IsChecked != true)
            foreach (var id in new[] { "AutonomousProtectLocalBudget", "AutonomousProtectFullBudget" })
            {
                _choices[id].IsEnabled = false;
                ToolTip.SetTip(_choices[id], UiLocalization.Text("请先启用阶段间独立验收，原预算保护偏好保持"));
            }
        foreach (var id in new[] { "AutonomousLocalUpdates", "AutonomousFullUpdates" })
            if (editable) ToolTip.SetTip(_numbers[id], UiLocalization.Text("修改更新上限会相应预留阶段评价与梯度尝试，总预算和追迹硬限不变"));
        foreach (var pair in _commands)
        {
            var enabled = !IsBusy;
            if (pair.Key == "AutonomousCancel") enabled = _session.IsBusy || _seedCancellation is not null;
            else if (pair.Key is "AutonomousImport" or "AutonomousCheck") enabled &= state is null;
            else if (pair.Key == "AutonomousPrepareSeeds") enabled &= state is null && !readOnly;
            else if (pair.Key == "AutonomousRun") enabled &= state is null && !readOnly;
            else if (pair.Key == "AutonomousResume") enabled &= !readOnly && state?.Status is AutonomousSearchStatus.Ready or AutonomousSearchStatus.Cancelled or AutonomousSearchStatus.Running;
            else if (pair.Key == "AutonomousCopy") enabled &= _session.RecordPath is not null;
            else if (pair.Key == "AutonomousExport") enabled &= _candidates.SelectedItem is TopologyCandidateRow row && row.Candidate.Assessment.Completed && !row.Candidate.Topology.HasTransitionElements;
            pair.Value.IsEnabled = enabled;
            ToolTip.SetTip(pair.Value, enabled ? null : UiLocalization.Text(IsBusy ? "请先停止并保存当前任务"
                : readOnly && pair.Key is "AutonomousRun" or "AutonomousResume" or "AutonomousPrepareSeeds" ? "此通光原型仅供查看和导出，请新建版本化计划" : "请先载入记录或选择可用候选"));
        }
        if (state is null) { _budget.Text = UiLocalization.Text("尚未消耗预算"); _record.Text = ""; return; }
        var used = state.Consumed; var cap = state.Plan.Options; var reserved = state.Reservation?.Maximum;
        var pupils = state.Events.Where(entry => entry.Recovery?.Work.PupilEvaluations is not null).Select(entry => entry.Recovery!.Work.PupilEvaluations!.Value).ToArray();
        var unknown = state.Events.Count(entry => entry.Outcome == "InterruptedUnknownCharged");
        _budget.Text = UiLocalization.Text("已计费 / 上限") + $" · {UiLocalization.Text("评价")} {used.FunctionEvaluations}/{cap.MaximumFunctionEvaluations} · "
            + $"{UiLocalization.Text("光线梯度")} {used.GradientEvaluations}/{cap.MaximumGradientEvaluations} · {UiLocalization.Text("追迹")} {used.TracedRays}/{cap.MaximumTracedRays}"
            + $" · {used.Elapsed.TotalSeconds:F1}/{cap.TimeLimit.TotalSeconds:G8}s\n"
            + UiLocalization.Text("已记录的工作明细") + $" · {UiLocalization.Text("矩阵")} {used.MatrixEvaluations} · Jacobian {used.JacobianBuilds} · "
            + $"{UiLocalization.Text("差分探测")} {used.DifferenceEvaluations} · {UiLocalization.Text("瞳查询")} "
            + (pupils.Length > 0 ? pupils.Sum(value => (long)value).ToString(CultureInfo.CurrentCulture) : UiLocalization.Text("未记录"))
            + $" · {UiLocalization.Text("几何查询")} {used.GeometryProbes} · {UiLocalization.Text("独立验收")} {used.ValidationEvaluations}\n"
            + UiLocalization.Text("在途预约") + $" · {UiLocalization.Text("评价")} {reserved?.FunctionEvaluations ?? 0} · {UiLocalization.Text("光线梯度")} {reserved?.GradientEvaluations ?? 0} · "
            + $"{UiLocalization.Text("追迹")} {reserved?.TracedRays ?? 0} · {(reserved?.Elapsed ?? TimeSpan.Zero).TotalSeconds:F1}s · {UiLocalization.Text("达标归档")} {state.AcceptedArchive.Length}"
            + (unknown > 0 ? $"\n{UiLocalization.Text("未知中断阶段按预约计费，辅助工作只显示已记录数量")} · {unknown}" : "");
        ToolTip.SetTip(_budget, UiLocalization.Text("光线梯度与恢复矩阵 Jacobian 分别记录，不能按相同成本相加；未记录不代表零工作"));
        if (state.Plan.Options.QualifiedReductionOnly == true) _budget.Text += "\n" + ReductionConclusionText(state);
        if (state.Plan.Options.BoundedSearch is not null) _budget.Text += "\n" + BoundedConclusionText(state);
        _record.Text = _session.RecordPath;

        void Hint(string id, string text)
        {
            if (editable) ToolTip.SetTip(_choices[id], UiLocalization.Text(text));
        }
    }

    internal static string ReductionConclusionText(AutonomousSearchCheckpoint checkpoint)
    {
        var summary = AutonomousSearchService.ReductionSummary(checkpoint);
        var reason = summary.Reason switch
        {
            AutonomousReductionStopReason.InProgress => "正在验证减片",
            AutonomousReductionStopReason.Cancelled => "减片任务已暂停",
            AutonomousReductionStopReason.BudgetExhausted => "已到预算，未完成的分支保留",
            AutonomousReductionStopReason.NoQualifiedSource => "尚无达标起点，停止减片",
            AutonomousReductionStopReason.MinimumAllowedCountReached => "已到设定片数下限",
            _ => "限定删减尝试已结束，保留达标处方"
        };
        return UiLocalization.Text(reason) + " · " + UiLocalization.Text("最少已验证片数") + ": "
            + (summary.MinimumVerifiedElementCount?.ToString(CultureInfo.CurrentCulture) ?? UiLocalization.Text("尚无达标处方"))
            + " · " + UiLocalization.Text("不代表全局最少片数");
    }

    internal static string BoundedConclusionText(AutonomousSearchCheckpoint checkpoint)
    {
        var summary = AutonomousSearchService.BoundedSummary(checkpoint);
        var reason = summary.StopReason switch
        {
            BoundedSearchStopReason.NoImprovement => "连续尝试无合格改进，停止探索",
            BoundedSearchStopReason.LayerLimit => "本层尝试已到上限，停止探索",
            BoundedSearchStopReason.InsufficientCompleteTrialBudget => "剩余预算不足完整试验与终验",
            BoundedSearchStopReason.TrialLimit => "结构尝试已到上限",
            BoundedSearchStopReason.Cancelled => "已停止并保留合格方案",
            BoundedSearchStopReason.TimeLimit => "已到运行时限",
            BoundedSearchStopReason.NoEligibleBranch => "可用候选分支已结束",
            _ => "正在筛检候选"
        };
        return UiLocalization.Text(reason) + " · " + UiLocalization.Text("最佳已验证结构") + ": "
            + (summary.Complexity is { } c ? $"{c.Elements} / {c.AsphericSurfaces} / {c.PolynomialTerms}" : UiLocalization.Text("尚无达标处方"))
            + $" · {UiLocalization.Text("筛检通过 / 拒绝")} {summary.ScreenedTrials - summary.RejectedScreens}/{summary.RejectedScreens}"
            + " · " + UiLocalization.Text("不代表全局最少片数");
    }

    private void ShowCandidate()
    {
        _prescriptionRows.Clear(); _fieldRows.Clear();
        if (_candidates.SelectedItem is not TopologyCandidateRow row) { Refresh(); return; }
        var candidate = row.Candidate; var state = _session.Checkpoint!;
        SetStructureSource(candidate.Topology, state.Plan.Constraints, candidate.Id, candidate.Assessment.MeetsTargets ? "达标" : "未达标");
        var optic = TopologyCompiler.Compile(candidate.Topology, state.Plan.Constraints).CreateOptic();
        foreach (var element in candidate.Topology.Elements)
        {
            Add(element.FrontSurfaceId, element.FrontZMillimeters, element.Material, element.Front, element.Protected);
            Add(element.BackSurfaceId, element.BackZMillimeters, "Air", element.Back, element.Protected);
        }
        foreach (var field in candidate.Assessment.Fields)
        {
            var angles = FieldCoordinates.Denormalize(optic.Fields, field.Field.X, field.Field.Y);
            _fieldRows.Add(new($"{angles.X:G6} / {angles.Y:G6}", Value(field.RmsRadiusMillimeters * 1000), Value(field.MaximumRadiusMillimeters * 1000),
                $"{field.ValidRays}/{field.AttemptedRays}", $"{field.CheckValidRays}/{field.CheckAttemptedRays}", Value(field.DistortionFraction * 100)));
        }
        var target = candidate.Assessment.Targets;
        _details.Text = UiLocalization.Text("原任务与独立验收") + $" · {target.EffectiveFocalLengthMillimeters:G8} mm · F/{target.FNumber:G6} · RMS ≤ {target.MaximumRmsRadiusMillimeters * 1000:G8} µm\n"
            + UiLocalization.Text("机械参考 / 光阑 / 像面位置") + $": {candidate.Topology.Reference.ZMillimeters:G8} / {candidate.Topology.Stop.ZMillimeters:G8} / {candidate.Topology.Image.ZMillimeters:G8} mm\n"
            + UiLocalization.Text("未通过门槛") + $": {candidate.Assessment.Violations.Length}\n" + string.Join("\n", candidate.Assessment.Violations.Take(6).Select(item => $"{item.Code}: {Value(item.Actual)} / {Value(item.Limit)}"));
        Refresh();
        void Add(string id, double z, string material, TopologyFace face, bool protect) => _prescriptionRows.Add(new(id, Value(z), material,
            UiLocalization.Text(face.Profile.Kind switch { TopologyProfileKind.Plane => "平面", TopologyProfileKind.Standard => "球面 / 二次曲面", _ => "偶次非球面" }),
            Value(face.ClearSemiDiameterMillimeters), UiLocalization.Text(protect ? "是" : "否"),
            face.Profile.CurvaturePerMillimeter == 0 ? "∞" : Value(1 / face.Profile.CurvaturePerMillimeter),
            Value(face.Profile.Conic), string.Join("; ", face.Profile.Terms.Select(term => $"r{term.Power}={term.Coefficient:G10}"))));
    }

    private static string Operation(string value) => UiLocalization.Text(value switch
    { "population-rebirth" => "候选重生", "population-difference" => "群体差分", "seed" => "起点验收", "optimize" => "连续优化", "insert" => "增片", "delete" => "删片", "gradual-delete" => "渐进删除", "split" => "拆片", "merge" => "并片", "material" => "替换材料", "profile" => "升级面型", _ => value });
    private static string Phase(AutonomousSearchPhase? value) => UiLocalization.Text(value switch
    { AutonomousSearchPhase.Screening => "候选几何筛检", AutonomousSearchPhase.Feasibility => "阶段独立验收", AutonomousSearchPhase.Recovery => "一阶恢复", AutonomousSearchPhase.Local => "局部补偿", AutonomousSearchPhase.Full => "全目标优化", AutonomousSearchPhase.Validation => "独立验收", AutonomousSearchPhase.TransitionValidation => "过渡验收", _ => "—" });
    private static string Outcome(string value) => UiLocalization.Text(value switch
    {
        "Accepted" => "达标",
        "ScreenPassed" => "筛检通过",
        "ScreenRejected" => "筛检拒绝",
        "ScreenCancelled" => "筛检已停止",
        "Rejected" or "UnmetTargets" => "未达标",
        "EditRejected" => "结构操作被拒绝",
        "Recovered" => "一阶恢复完成",
        "NoParaxialRecovery" => "一阶恢复未完成",
        "RecoverySkipped" => "按计划跳过恢复",
        "UpdateLimit" => "阶段更新完成",
        "Converged" => "阶段已收敛",
        "Stalled" => "阶段无改进",
        "TransitionRejected" => "过渡结构未通过",
        "TransitionFeasible" => "过渡结构可继续",
        "ValidationUnavailable" => "独立验收不可用",
        "PreflightRejected" => "阶段输入被拒绝",
        "DomainFailure" => "光学定义域失败",
        "TimeLimit" => "达到阶段时限",
        "Cancelled" => "已停止",
        "BudgetExhausted" or "RayBudgetExhausted" => "预算耗尽",
        "InterruptedUnknownCharged" => "未知中断已按预约计费",
        "NotDispatched" => "未派发，已退还预约",
        _ => value
    });

    private async Task<string?> PickOpen(string kind)
    {
        if (_settings.OpenPicker is { } picker) return await picker(this, kind);
        var patterns = kind switch { "plan" => new[] { "*.topology-plan.json" }, "record" => ["*.autonomous.json"], _ => ["*.staropt", "*.topology.json", "*.optic.json", "*.json"] };
        var selected = await StorageProvider.OpenFilePickerAsync(new()
        {
            AllowMultiple = false,
            Title = UiLocalization.Text("选择文件"),
            FileTypeFilter = [new FilePickerFileType(UiLocalization.Text("自主设计文件")) { Patterns = patterns }]
        });
        return selected.FirstOrDefault()?.TryGetLocalPath();
    }
    private async Task<string?> PickSave(string kind)
    {
        if (_settings.SavePicker is { } picker) return await picker(this, kind);
        var extension = kind switch { "plan" => ".topology-plan.json", "record" => ".autonomous.json", _ => ".staropt" };
        var selected = await StorageProvider.SaveFilePickerAsync(new()
        {
            Title = UiLocalization.Text("保存设计文件"),
            SuggestedFileName = "autonomous" + extension,
            FileTypeChoices = [new FilePickerFileType(UiLocalization.Text("自主设计文件")) { Patterns = ["*" + extension] }]
        });
        return selected?.TryGetLocalPath();
    }
}

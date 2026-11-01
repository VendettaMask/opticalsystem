using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;

namespace OpticalSystemDesign.InitialStructure.Persistence;

/// <summary>A portable, read-only evidence snapshot. Never a resumable checkpoint or a new acceptance protocol.</summary>
public sealed record AutonomousDesignStudy
{
    public int Version { get; init; } = 1;
    public string Name { get; init; } = "";
    public string SourceFingerprint { get; init; } = "";
    public AutonomousSearchCheckpoint? Main { get; init; }
    public SeedPreparationCheckpoint? Preparation { get; init; }
    public ParaxialSeedSearchResult? Prepared { get; init; }
    [JsonIgnore] public AutonomousSearchPlan Plan => Main?.Plan ?? Preparation?.Plan ?? Prepared!.SourcePlan;
    [JsonIgnore] public string RunId => Main?.RunId ?? Preparation?.RunId ?? Prepared!.ProtocolFingerprint;

    public static AutonomousDesignStudy Capture(string name, AutonomousSearchCheckpoint? main = null,
        SeedPreparationCheckpoint? preparation = null, ParaxialSeedSearchResult? prepared = null)
    {
        var result = new AutonomousDesignStudy { Name = name, Main = main, Preparation = preparation,
            Prepared = main is null && preparation is null ? prepared : null };
        result = result with { SourceFingerprint = Fingerprint(result) };
        Validate(result);
        return result;
    }

    internal static string Fingerprint(AutonomousDesignStudy study) => ContentFingerprint.Compute(new { study.Main, study.Preparation, study.Prepared });
    public static void Validate(AutonomousDesignStudy study)
    {
        if (study.Version != 1 || string.IsNullOrWhiteSpace(study.Name) || study.Name.Length > 160
            || study.Main is null && study.Preparation is null && study.Prepared is null
            || study.Prepared is not null && (study.Main is not null || study.Preparation is not null)
            || study.SourceFingerprint != Fingerprint(study)) throw new InvalidDataException("Invalid design evidence snapshot.");
        if (study.Main is { } main) AutonomousSearchService.Validate(main);
        if (study.Preparation is { } preparation)
        {
            SeedPreparationSearch.Validate(preparation);
            if (study.Main is { } parent && (parent.RunId != preparation.RunId
                || ContentFingerprint.Compute(parent.Plan.Targets) != ContentFingerprint.Compute(preparation.Plan.Targets)
                || ContentFingerprint.Compute(parent.Plan.Constraints) != ContentFingerprint.Compute(preparation.Plan.Constraints)))
                throw new InvalidDataException("Preparation evidence belongs to another task.");
        }
    }
}

public sealed record AutonomousDesignPortfolio
{
    public int Version { get; init; } = 1;
    public string Algorithm { get; init; } = "AutonomousDesignPortfolioV1";
    public ImmutableArray<AutonomousDesignStudy> Entries { get; init; } = [];
}

public sealed record DesignReviewCandidate(string Id, string? Parent, string Origin, string Outcome,
    DesignTopology Topology, TopologyAssessment? Assessment, bool WorkingSeed = false, bool OriginalSpecification = true)
{
    public bool OriginalQualified => OriginalSpecification && !Topology.HasTransitionElements && Assessment is { Completed: true, MeetsTargets: true };
    public bool CanExport => OriginalSpecification && !Topology.HasTransitionElements && Assessment is { Completed: true };
    public string Classification => Topology.HasTransitionElements ? "过渡结构，仅供研究"
        : !OriginalSpecification ? "中间阶段验收，不代表原规格合格"
        : OriginalQualified ? "原规格合格" : Assessment is { Completed: true } ? "原规格未达标"
        : Assessment is not null ? "验收未完成" : WorkingSeed ? "工作起点，未通过完整验收" : "尚无完整验收";
}

public sealed record DesignReviewDecision(string Id, string? Parent, string Stage, string Outcome, string? Detail,
    AutonomousSearchWork? Work, string? CandidateId = null, string? Backend = null, int? KnownFunctionEvaluations = null);

/// <summary>Projection of saved evidence only. No tracing, first-order computation or acceptance replay.</summary>
public sealed record AutonomousDesignReview(AutonomousDesignStudy Study, ImmutableArray<DesignReviewCandidate> Candidates,
    ImmutableArray<DesignReviewDecision> Decisions)
{
    public static AutonomousDesignReview Build(AutonomousDesignStudy study)
    {
        AutonomousDesignStudy.Validate(study);
        var candidates = new List<DesignReviewCandidate>(); var decisions = new List<DesignReviewDecision>();
        if (study.Main is { } main)
        {
            foreach (var item in main.Candidates.Concat(main.AcceptedArchive).DistinctBy(c => c.Id))
                Add(new("main:" + item.Id, item.ParentId, "自主搜索", main.Pool.Contains(item.Id) ? "保留候选" : "历史候选",
                    item.Topology, item.Assessment));
            foreach (var entry in main.Events)
            {
                var id = "event:" + entry.Sequence;
                if (entry.Endpoint is { } endpoint && entry.Assessment is { } assessment)
                    Add(new(id, entry.ParentId, "阶段端点", entry.Outcome, endpoint, assessment), allowIntermediate: true);
                var backend = entry.Optimization is { } optimization
                    ? $"{optimization.EndpointEvaluation?.Execution?.Backend ?? "unrecorded"}; device={optimization.EndpointEvaluation?.Execution?.DeviceName ?? "unrecorded"}; "
                        + $"fallback={optimization.EndpointEvaluation?.Execution?.FallbackReason ?? "unrecorded"}; "
                        + $"GPU derivative rays={optimization.Work.GpuDerivativeRays?.ToString() ?? "unrecorded"}; batches={optimization.Work.GpuBatches?.ToString() ?? "unrecorded"}"
                    : null;
                decisions.Add(new(entry.TrialId, entry.ParentId, entry.Phase?.ToString() ?? entry.Operation,
                    entry.Outcome, entry.Detail, entry.Work, entry.Endpoint is not null && entry.Assessment is not null ? id : null, backend));
            }
        }
        var prepared = study.Prepared ?? study.Preparation?.Result;
        if (prepared is null && study.Preparation is { Spc: { } partialSpc } spcCheckpoint)
            prepared = new() { SourcePlan = spcCheckpoint.Plan, Options = spcCheckpoint.Options, ProtocolFingerprint = spcCheckpoint.ProtocolFingerprint,
                Termination = ParaxialSeedTermination.Cancelled, Work = spcCheckpoint.Consumed,
                Spc = new() { Algorithm = "initial-structure-spc-preparation-v1", ProtocolFingerprint = spcCheckpoint.ProtocolFingerprint,
                    StopReason = spcCheckpoint.Status.ToString(), Work = spcCheckpoint.Consumed, Parents = partialSpc.Parents, Branches = partialSpc.Branches } };
        if (prepared is null && study.Preparation is { Mode: not "Cold", EngineState: { } saved } partial)
        {
            // Decode only completed evidence already present in this schema-1 snapshot.
            // Never call Run/Result/final validation to make an unfinished record look complete.
            prepared = new() { SourcePlan = partial.Plan, Options = partial.Options, ProtocolFingerprint = partial.ProtocolFingerprint,
                Termination = ParaxialSeedTermination.Cancelled, Work = partial.Consumed, Attempts = Array<ParaxialSeedAttempt>("attempts") };
            if (partial.Mode == "Retrieval") prepared = prepared with { Retrieval = new()
            { LibraryFingerprint = ContentFingerprint.Compute(partial.Options.Retrieval!.Library), StopReason = partial.Status.ToString(), Trials = Array<LensRetrievalTrial>("trials") } };
            if (partial.Mode == "Competition")
            {
                var items = saved.TryGetProperty("candidates", out var data) ? data.EnumerateArray().Select(item =>
                    new StructureCompetitionCandidate(item.GetProperty("id").GetString()!,
                        item.GetProperty("family").Deserialize<StructureRayPathFamily>(AutonomousDesignStudyFiles.Json),
                        item.GetProperty("partition").GetInt32(), item.GetProperty("outcome").GetString()!,
                        item.GetProperty("topology").Deserialize<DesignTopology>(AutonomousDesignStudyFiles.Json),
                        item.GetProperty("completedStages").GetInt32(), item.GetProperty("assessment").Deserialize<TopologyAssessment>(AutonomousDesignStudyFiles.Json),
                        item.GetProperty("work").Deserialize<AutonomousSearchWork>(AutonomousDesignStudyFiles.Json)!)).ToImmutableArray() : [];
                prepared = prepared with { Competition = new() { ProtocolFingerprint = partial.ProtocolFingerprint, StopReason = partial.Status.ToString(),
                    Candidates = items, Rounds = Array<StructureCompetitionRound>("rounds"), SelectedIds = Array<string>("activeIds") } };
            }
            if (partial.Mode == "Evolution") prepared = prepared with { Evolution = new("initial-structure-evolution-v1", partial.ProtocolFingerprint,
                partial.Status.ToString(), Array<StructureEvolutionParent>("parents"), Array<StructureEvolutionTrial>("trials"),
                Array<QualifiedEvolutionDesign>("archive"), [], Array<StructurePhaseCost>("costs")) };
            ImmutableArray<T> Array<T>(string name) => saved.TryGetProperty(name, out var value)
                ? value.Deserialize<ImmutableArray<T>>(AutonomousDesignStudyFiles.Json) : [];
        }
        var cold = prepared?.ColdStart;
        var constructions = cold?.Construction ?? study.Preparation?.Candidates ?? [];
        var phases = cold?.Phases ?? study.Preparation?.Phases ?? [];
        foreach (var item in constructions)
        {
            var id = "construction:" + item.Index;
            if (item.Topology is { } topology) Add(new(id, null, "构造起点", item.Outcome, topology, item.Assessment));
            decisions.Add(new(id, null, "Construction", item.Outcome, null, item.Work, item.Topology is null ? null : id));
        }
        for (var i = 0; i < phases.Length; i++)
        {
            var item = phases[i]; var id = "compensation:" + i;
            Add(new(id, null, "补偿优化", item.Outcome, item.Topology, item.Assessment));
            decisions.Add(new(id, null, item.Name, item.Outcome, string.Join("; ", item.Diagnostics), item.Work, id));
        }
        if (prepared?.Retrieval is { } retrieval) foreach (var item in retrieval.Trials)
        {
            var id = "retrieval:" + item.EntryId;
            if (item.Topology is { } topology) Add(new(id, item.EntryId, "已有镜头复用", item.Outcome, topology, item.Assessment,
                retrieval.RetainedEntryIds.Contains(item.EntryId)));
            var detail = $"{item.Provenance.Title}; {item.Provenance.Origin}; {item.Provenance.UsageAuthority}; SHA256={item.Provenance.SourceDocumentSha256}; "
                + $"scale={item.Transform?.UniformScale.ToString("R", System.Globalization.CultureInfo.InvariantCulture) ?? "unrecorded"}; original-input={item.MatchesOriginalInput}; " + item.Detail;
            decisions.Add(new(id, item.EntryId, "Retrieval", item.Outcome, detail, item.Work, item.Topology is null ? null : id));
        }
        if (prepared?.Competition is { } competition)
        {
            foreach (var item in competition.Candidates)
                if (item.Topology is { } topology) Add(new("competition:" + item.Id, null, "多候选竞争", item.Outcome,
                    topology, item.OriginalAssessment, competition.SelectedIds.Contains(item.Id)));
            foreach (var round in competition.Rounds)
                decisions.Add(new(round.CandidateId, null, round.Stage.Name, round.Screen.Outcome,
                    $"field={round.Stage.FieldFraction:R}; pupil={round.Stage.PupilFraction:R}; selected={round.Selected}; "
                    + string.Join("; ", round.Optimization?.Diagnostics ?? []), round.Work, "competition:" + round.CandidateId));
        }
        if (prepared?.Evolution is { } evolution)
        {
            foreach (var parent in evolution.Parents) Add(new("parent:" + parent.Id, null, "已有父方案", "父方案验收", parent.Topology, parent.Assessment));
            foreach (var item in evolution.Trials)
            {
                var id = "evolution:" + item.Id; var topology = item.Endpoint ?? item.EditedTopology;
                if (topology is not null) Add(new(id, item.ParentId, "结构分支", item.Outcome, topology, item.OriginalAssessment));
                decisions.Add(new(id, item.ParentId, item.Operation, item.Outcome, item.Detail, item.Work, topology is null ? null : id));
            }
            foreach (var item in evolution.QualifiedArchive)
                if (!candidates.Any(c => c.Id == "evolution:" + item.Id || c.Id == "parent:" + item.Id))
                    Add(new("archive:" + item.Id, item.ParentId, item.ParentId is null ? "已有父方案" : "结构分支", "合格归档", item.Topology, item.Assessment));
        }
        if (prepared?.Spc is { } spc)
        {
            foreach (var parent in spc.Parents)
            {
                Add(new("spc:" + parent.Id + ":original", null, "已有父方案", parent.Outcome, parent.Original, parent.OriginalAssessment));
                if (parent.StationaryParent is { } topology)
                    Add(new("spc:" + parent.Id + ":stationary", parent.Id, "SPC父结构优化", parent.Outcome, topology, parent.StationaryAssessment));
                decisions.Add(new("spc:" + parent.Id, null, "SPC鞍点核查", parent.Outcome,
                    parent.Saddle is { } saddle ? $"scope={saddle.Scope}; negative={saddle.StableNegativeDirections}; uncertainty={saddle.CurvatureUncertainty:R}; eigenvalues={string.Join(",", saddle.Fine.Eigenvalues)}" : null, null));
            }
            foreach (var branch in spc.Branches)
            {
                foreach (var (stage, index) in branch.Stages.Select((stage, index) => (stage, index))) SpcStage(branch.Id, branch.ParentId, stage, index);
                decisions.Add(new("spc:" + branch.Id, branch.ParentId, "SPC正负分支优化", branch.Outcome,
                    $"sign={branch.Sign}; null-stop={branch.NullOptimizationStopReason}; stationary={branch.NullInteriorStationary}; positive-curvature={branch.NullMinimum?.Certified}", null));
            }
            if (study.Preparation?.Spc is { } pendingSpc && pendingSpc.ParentCursor < pendingSpc.Order.Length)
            {
                var parentId = "parent-" + pendingSpc.Order[pendingSpc.ParentCursor];
                if (!spc.Parents.Any(p => p.Id == parentId) && pendingSpc.StationaryParent is { } current && pendingSpc.ParentAssessment is { } measured)
                    Add(new("spc:" + parentId + ":stationary", parentId, "SPC父结构优化", "Suspended", current, measured));
                var branchId = parentId + (pendingSpc.Sign < 0 ? ":minus" : ":plus");
                foreach (var (stage, index) in pendingSpc.Stages.Select((stage, index) => (stage, index))) SpcStage(branchId, parentId, stage, index);
            }
            void SpcStage(string branch, string parent, SpcPhysicalStage stage, int index)
            {
                var id = "spc:" + branch + ":stage-" + index;
                if (stage.Topology is { } topology) Add(new(id, parent, "SPC实体补偿", stage.Outcome, topology, stage.Assessment));
                decisions.Add(new(id, parent, stage.Name, stage.Outcome, $"glass={stage.Material}; center-mm={stage.ThicknessMillimeters:R}", stage.Work, id));
            }
        }
        if (prepared is not null && cold is null && prepared.Spc is null && prepared.Retrieval is null && prepared.Competition is null && prepared.Evolution is null)
            foreach (var item in prepared.Attempts)
            {
                var id = "paraxial:" + item.Index;
                if (item.Endpoint is { } topology) Add(new(id, null, "一阶起点", item.Outcome, topology, null,
                    prepared.Seeds.Any(s => ContentFingerprint.Compute(s) == ContentFingerprint.Compute(topology))));
                decisions.Add(new(id, null, "Paraxial", item.Outcome, null,
                    null, item.Endpoint is null ? null : id, KnownFunctionEvaluations: item.FunctionEvaluations));
            }
        if (cold?.Endpoint is { } final) Add(new("cold:final", null, "冷启动终点", cold.StopReason, final, cold.Assessment));
        else if (study.Preparation?.Endpoint is { } point)
            Add(new("preparation:current", null, "准备阶段当前处方", study.Preparation.StopReason ?? study.Preparation.Stage,
                point, study.Preparation.Assessment));
        if (study.Preparation is { } prep)
            decisions.Add(new("preparation:status", null, prep.Stage, prep.StopReason ?? prep.Status.ToString(),
                "kernel=" + prep.KernelFingerprint, prep.Consumed));
        else if (prepared is not null)
            decisions.Add(new("preparation:status", null, "Preparation", cold?.StopReason ?? prepared.Spc?.StopReason ?? prepared.Retrieval?.StopReason
                ?? prepared.Competition?.StopReason ?? prepared.Evolution?.StopReason ?? prepared.Termination.ToString(), null, prepared.Work));
        return new(study, candidates.ToImmutableArray(), decisions.ToImmutableArray());

        void Add(DesignReviewCandidate candidate, bool allowIntermediate = false)
        {
            if (candidate.Assessment is { } assessment)
            {
                var original = !candidate.Topology.HasTransitionElements
                    && ContentFingerprint.Compute(assessment.Targets) == ContentFingerprint.Compute(study.Plan.Targets)
                    && ContentFingerprint.Compute(assessment.StructuralConstraints) == ContentFingerprint.Compute(study.Plan.Constraints);
                // A recorded transition/phase assessment keeps its own original exploratory permissions.
                // They are used for read-only identity checks, never substituted into the task or formal acceptance.
                var compiled = TopologyCompiler.Compile(candidate.Topology, allowIntermediate ? assessment.StructuralConstraints : study.Plan.Constraints);
                if (assessment.OpticFingerprint != compiled.OpticFingerprint
                    || !allowIntermediate && !original
                    || assessment.MeetsTargets && (!assessment.Completed || !assessment.Violations.IsEmpty || assessment.Fields.IsEmpty)
                    || !TopologyValidator.MatchesProtocol(compiled, allowIntermediate ? assessment.Targets : study.Plan.Targets, assessment.ProtocolFingerprint))
                    throw new InvalidDataException("Saved assessment does not describe this original specification and prescription: " + candidate.Id);
                candidate = candidate with { OriginalSpecification = original };
            }
            candidates.Add(candidate);
        }
    }
}

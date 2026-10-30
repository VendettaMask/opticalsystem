using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;

if (args.Length != 1) throw new ArgumentException("<evidence-root>");
var root = Path.GetFullPath(args[0]);
var json = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
};
using var protocol = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "experiment-protocol.json")));
var seeds = protocol.RootElement.GetProperty("seeds").EnumerateArray().Select(x => x.GetInt32()).ToArray();
var arms = protocol.RootElement.GetProperty("arms").EnumerateArray().Select(x => x.GetString()!).ToArray();
var cases = protocol.RootElement.GetProperty("cases").EnumerateArray().Select(x => x.GetString()!).ToArray();
var planIdentities = new Dictionary<string, string>(); var checks = new List<object>(); var finals = new List<object>();
var trials = new List<object>(); var archives = new List<object>(); var metrics = new List<object>();
var lossDiagnostics = new List<object>();
foreach (var seed in seeds)
    foreach (var arm in arms)
        foreach (var name in cases)
        {
            var directory = Path.Combine(root, "formal", arm + "-seed" + seed);
            var path = Path.Combine(directory, name + ".seed-search.json");
            using var saved = JsonDocument.Parse(File.ReadAllBytes(path));
            var result = saved.RootElement.GetProperty("result").Deserialize<ParaxialSeedSearchResult>(json)!;
            Check(ContentFingerprint.Compute(result) == saved.RootElement.GetProperty("sha256").GetString(), "Saved content hash");
            var evolution = result.Evolution!; var plan = result.SourcePlan;
            var key = name + "/" + seed; var fp = ContentFingerprint.Compute(plan);
            if (planIdentities.TryGetValue(key, out var previous)) Check(previous == fp, "Shared input/target/permission identity");
            else planIdentities[key] = fp;
            using var input = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, name + ".input.json")));
            var acquired = input.RootElement.GetProperty("acquisition").Deserialize<AutonomousSearchWork>(json)!;
            var allWork = evolution.Parents.Select(p => p.Work).Concat(evolution.Trials.Select(t => t.Work)).ToArray();
            foreach (var property in typeof(AutonomousSearchWork).GetProperties().Where(p => p.PropertyType == typeof(int) || p.PropertyType == typeof(long)))
            {
                var sum = allWork.Sum(w => Convert.ToInt64(property.GetValue(w)));
                Check(sum == Convert.ToInt64(property.GetValue(result.Work)), "Settled " + property.Name);
            }
            Check(result.Work.FunctionEvaluations + acquired.FunctionEvaluations <= 600, "Unified function cap");
            Check(result.Work.GradientEvaluations + acquired.GradientEvaluations <= 200, "Unified gradient cap");
            Check(result.Work.TracedRays + acquired.TracedRays <= 2000000, "Unified ray cap");
            Check(evolution.Trials.All(t => t.Work.FunctionEvaluations <= result.Options.MaximumEvaluationsPerStart), "Candidate lifetime cap");
            foreach (var parent in evolution.Parents) ValidateFinal(parent.Id, null, parent.Topology, parent.Assessment, true);
            foreach (var trial in evolution.Trials)
            {
                var parent = evolution.Parents.Single(p => p.Id == trial.ParentId);
                if (trial.EditedTopology is { } edited)
                {
                    Check(TopologyCompiler.Compile(edited, plan.Constraints).BindingFingerprint == trial.EditBindingFingerprint, "Fresh edited binding");
                    if (trial.Compensation is not null)
                        Check(TopologyCompiler.Compile(trial.Recovery?.Topology ?? edited, plan.Constraints).BindingFingerprint == trial.CompensationBindingFingerprint, "Fresh compensation binding");
                }
                if (trial.Endpoint is { } endpoint)
                {
                    Check(endpoint.Reference == parent.Topology.Reference && endpoint.Stop == parent.Topology.Stop && endpoint.Image == parent.Topology.Image, "Fixed mechanical planes/aperture");
                    Check(endpoint.Fields.SequenceEqual(parent.Topology.Fields) && endpoint.Wavelengths.SequenceEqual(parent.Topology.Wavelengths), "Original spectrum/fields");
                    var originalFaces = parent.Topology.Elements.SelectMany(e => new[] { (e.FrontSurfaceId, e.Front), (e.BackSurfaceId, e.Back) }).ToDictionary(x => x.Item1, x => x.Item2);
                    foreach (var (id, face) in endpoint.Elements.SelectMany(e => new[] { (e.FrontSurfaceId, e.Front), (e.BackSurfaceId, e.Back) }))
                        if (originalFaces.TryGetValue(id, out var old))
                            Check(face.ClearSemiDiameterMillimeters == old.ClearSemiDiameterMillimeters && face.MechanicalSemiDiameterMillimeters == old.MechanicalSemiDiameterMillimeters, "Retained physical aperture");
                    foreach (var lens in parent.Topology.Elements.Where(e => e.Protected)) Check(endpoint.Elements.Contains(lens), "Protected lens");
                    if (trial.OriginalAssessment is { } a) ValidateFinal(trial.Id, trial.ParentId, endpoint, a, false);
                }
                if (trial.PowerAllocation is { AllocatedPowersPreserved: true } allocation)
                    Check(allocation.RequestedPowersPerMillimeter.Length == allocation.EndpointPowersPerMillimeter.Length
                        && allocation.RequestedPowersPerMillimeter.Zip(allocation.EndpointPowersPerMillimeter).All(p => Math.Abs(p.First - p.Second) < 1e-10), "Assigned front powers preserved through closure");
                trials.Add(new
                {
                    Case = name,
                    Arm = arm,
                    Seed = seed,
                    trial.Id,
                    trial.ParentId,
                    trial.Operation,
                    trial.Outcome,
                    trial.Detail,
                    Recovery = trial.Recovery?.Termination.ToString(),
                    trial.Work,
                    Power = trial.PowerAllocation,
                    CompletedFinal = trial.OriginalAssessment?.Completed,
                    QualifiedFinal = trial.OriginalAssessment?.MeetsTargets,
                    EndpointCount = trial.Endpoint?.Elements.Length
                });
            }
            foreach (var q in evolution.QualifiedArchive)
            {
                Check(q.Assessment.Completed && q.Assessment.MeetsTargets, "Qualified archive hard gate");
                Check(q.Complexity == StructureEvolutionSearch.Complexity(q.Topology), "Actual complexity");
                if (q.ParentId is null) Check(evolution.Parents.Any(p => p.Id == q.Id && ContentFingerprint.Compute(p.Topology) == ContentFingerprint.Compute(q.Topology)), "Immutable inherited parent");
                else Check(evolution.Trials.Any(t => t.Id == q.Id && t.OriginalAssessment is { Completed: true, MeetsTargets: true }), "New qualification has original validation");
                archives.Add(new
                {
                    Case = name,
                    Arm = arm,
                    Seed = seed,
                    q.Id,
                    q.ParentId,
                    q.Complexity,
                    q.WorstRmsMillimeters,
                    q.WorstMaximumMillimeters,
                    Pareto = evolution.ParetoIds.Contains(q.Id)
                });
            }
            foreach (var p in evolution.Parents.Where(p => p.Assessment.Completed && p.Assessment.MeetsTargets))
                Check(evolution.QualifiedArchive.Any(q => q.Id == p.Id && q.ParentId is null), "Qualified parent never overwritten");
            Check(evolution.ParetoIds.SequenceEqual(StructureEvolutionSearch.Pareto(evolution.QualifiedArchive)), "Recomputed Pareto frontier");
            foreach (var working in result.Seeds)
            {
                var workingId = ContentFingerprint.Compute(working);
                var assessment = evolution.QualifiedArchive.FirstOrDefault(q => ContentFingerprint.Compute(q.Topology) == workingId)?.Assessment
                    ?? evolution.Trials.First(t => t.Endpoint is not null && ContentFingerprint.Compute(t.Endpoint) == workingId && t.OriginalAssessment is not null).OriginalAssessment;
                Check(StructureEvolutionSearch.IsWorkingSeedAssessment(assessment), "Working seeds pass original first-order and geometry gates");
            }
            if (arm == "combined")
            {
                var observations = evolution.Parents.Select(p => (p.Id, Topology: p.Topology, Kind: "Parent"))
                    .Concat(evolution.Trials.Where(t => t.Endpoint is not null && t.OriginalAssessment is not null)
                        .Select(t => (t.Id, Topology: t.Endpoint!, Kind: "OriginalFinalist")));
                foreach (var observation in observations)
                {
                    var source = TopologyCompiler.Compile(observation.Topology, plan.Constraints);
                    var before = source.OpticFingerprint;
                    var diagnostic = StructureSearchDiagnostics.Analyze(source);
                    Check(before == source.OpticFingerprint, "Posthoc diagnosis preserves snapshot");
                    lossDiagnostics.Add(new
                    {
                        Case = name,
                        Arm = arm,
                        Seed = seed,
                        observation.Id,
                        observation.Kind,
                        DiagnosticOnly = true,
                        SearchBudgetInput = false,
                        diagnostic
                    });
                }
            }
            var inherited = evolution.QualifiedArchive.Count(q => q.ParentId is null); var added = evolution.QualifiedArchive.Where(q => q.ParentId is not null).ToArray();
            var changedCount = added.Count(q => q.Complexity.Elements != evolution.Parents.Single(p => p.Id == q.ParentId).Topology.Elements.Length);
            metrics.Add(new
            {
                Case = name,
                Arm = arm,
                Seed = seed,
                result.Termination,
                evolution.StopReason,
                InheritedQualified = inherited,
                NewQualified = added.Length,
                NewStructuralQualified = changedCount,
                evolution.FirstNewQualifiedElapsed,
                MinimumInheritedCount = evolution.QualifiedArchive.Where(q => q.ParentId is null).Select(q => (int?)q.Complexity.Elements).Min(),
                MinimumNewCount = added.Select(q => (int?)q.Complexity.Elements).Min(),
                Pareto = evolution.ParetoIds.Length,
                Current = result.Work,
                Acquisition = acquired,
                EndToEnd = new
                {
                    Functions = result.Work.FunctionEvaluations + acquired.FunctionEvaluations,
                    Gradients = result.Work.GradientEvaluations + acquired.GradientEvaluations,
                    Rays = result.Work.TracedRays + acquired.TracedRays,
                    Elapsed = result.Work.Elapsed + acquired.Elapsed
                },
                Bytes = new FileInfo(path).Length
            });
            checks.Add(new
            {
                Case = name,
                Arm = arm,
                Seed = seed,
                ContentHash = true,
                SharedInput = true,
                OriginalProtocol = true,
                Gates = true,
                UnifiedBudgets = true,
                LifetimeBudgets = true,
                SettledAllCounters = true,
                PreservedParents = true,
                ParetoRecomputed = true
            });

            void Check(bool value, string label) { if (!value) throw new InvalidDataException($"{name}/{arm}/{seed}: {label}"); }
            void ValidateFinal(string id, string? parentId, DesignTopology topology, TopologyAssessment assessment, bool inheritedParent)
            {
                var compiled = TopologyCompiler.Compile(topology, plan.Constraints);
                Check(ContentFingerprint.Compute(assessment.Targets) == ContentFingerprint.Compute(plan.Targets), "Original targets");
                Check(ContentFingerprint.Compute(assessment.StructuralConstraints) == ContentFingerprint.Compute(plan.Constraints), "Original constraints");
                Check(compiled.OpticFingerprint == assessment.OpticFingerprint, "Same-snapshot final result");
                Check(TopologyValidator.GetProtocolFingerprint(compiled, plan.Targets) == assessment.ProtocolFingerprint, "Unchanged original validation protocol");
                var available = assessment.Completed && assessment.Fields.All(f => f.RmsRadiusMillimeters is { } rms && double.IsFinite(rms)
                    && f.MaximumRadiusMillimeters is { } max && double.IsFinite(max));
                finals.Add(new
                {
                    Case = name,
                    Arm = arm,
                    Seed = seed,
                    Id = id,
                    ParentId = parentId,
                    InheritedParent = inheritedParent,
                    assessment.Completed,
                    assessment.MeetsTargets,
                    assessment.TracedRayCount,
                    assessment.Failure,
                    Elements = topology.Elements.Length,
                    Fields = assessment.Fields.Length,
                    MinimumValidFraction = assessment.Fields.IsEmpty ? (double?)null : assessment.Fields.Min(f => (double)f.ValidRays / f.AttemptedRays),
                    BoundaryLost = assessment.Fields.Sum(f => f.CheckAttemptedRays - f.CheckValidRays),
                    AllFieldsHaveMetrics = available,
                    WorstRmsMillimeters = available ? assessment.Fields.Max(f => f.RmsRadiusMillimeters) : null,
                    WorstMaximumMillimeters = available ? assessment.Fields.Max(f => f.MaximumRadiusMillimeters) : null,
                    assessment.Violations,
                    assessment.ProtocolFingerprint,
                    assessment.OpticFingerprint
                });
            }
        }
void Save(string name, object value) => File.WriteAllText(Path.Combine(root, name), JsonSerializer.Serialize(value, json));
Save("formal-verification.json", checks); Save("formal-finalists.json", finals); Save("formal-trials.json", trials);
Save("formal-qualified-archive.json", archives); Save("formal-runs.json", metrics);
Save("posthoc-loss-diagnostics.json", lossDiagnostics);
Console.WriteLine($"Verified {checks.Count} runs, {finals.Count} original assessments, all saved hashes, counters, budgets, source settings and qualified/Pareto archives.");

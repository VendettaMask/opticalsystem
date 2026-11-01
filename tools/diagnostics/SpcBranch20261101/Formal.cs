using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

internal static class Formal
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true,
        WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals, Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
    public static async Task Run(string[] args, DesignTopology model)
    {
        using var serial = ComputationParallelism.SuppressNestedParallelism();
        var root = Path.GetFullPath(args[1]);
        if (args[0] == "freeze")
        {
            if (Directory.Exists(root)) throw new IOException("Frozen input directory already exists."); Directory.CreateDirectory(root);
            var budget = new ParaxialSeedSearchOptions { MaximumFunctionEvaluations = 600, MaximumTracedRays = 2_000_000, TimeLimit = TimeSpan.FromSeconds(120) };
            var policy = new SpcBranchOptions();
            foreach (var seed in Enumerable.Range(42, 5)) foreach (var control in new[] { true, false })
            {
                var plan = new AutonomousSearchPlan { Seeds = [model], Options = new() { Seed = seed, InsertionMaterials = ["N-BK7", "N-F2"],
                    Expansion = new() { EnableInsertion = true, EnableMaterial = true }, Local = new() { ComputeMode = DesignComputeMode.Cpu, GateCompensationVersion = 4, PerformFinalAssessment = false,
                        Sampling = new() { FieldsPerDirection = 3, PupilRings = 2, PupilArms = 8, IncludeBoundaryClearance = true, IncludeBoundaryMaximum = true } } } };
                await Write(Path.Combine(root, $"{(control ? "direct" : "spc")}-seed{seed}.json"), new Input(plan, budget, policy, control));
            }
            await Write(Path.Combine(root, "protocol.json"), new { Seeds = Enumerable.Range(42, 5), Budget = budget, Policy = policy,
                Control = "two direct positive-thickness common-bending starts; same parent optimization and physical stages/materials; no saddle or null branch optimization",
                Scope = "existing authored M3.1 singlet; one site, deterministic optimizer, seeds may be duplicates; no original F8/F4 success-rate claim",
                Research = "CPU signed null continuation; declared curvature subspace, true AD-gradient FD Hessian at h and h/2; original full TopologyValidator for positive structures",
                FailuresRetained = true, NoGateChanges = true, FrozenBeforeFormalRuns = true });
            return;
        }
        if (args[0] == "case")
        {
            var input = JsonSerializer.Deserialize<Input>(await File.ReadAllBytesAsync(args[2]), Json)!;
            if (Directory.Exists(root)) throw new IOException("Case evidence already exists."); Directory.CreateDirectory(root);
            File.Copy(args[2], Path.Combine(root, "input.json"));
            var fingerprint = ContentFingerprint.Compute(input.Plan);
            var result = SpcBranchSearch.Run(input.Plan, input.Budget, input.Policy, directGrowthControl: input.Control);
            Require(fingerprint == ContentFingerprint.Compute(input.Plan), "original plan mutated");
            Require(result.Work.FunctionEvaluations <= input.Budget.MaximumFunctionEvaluations && result.Work.GradientEvaluations <= input.Policy.MaximumGradientEvaluations
                && result.Work.TracedRays <= input.Budget.MaximumTracedRays && result.Work.Elapsed <= input.Budget.TimeLimit, "total budget");
            var study = new SpcBranchStudy(input.Plan, input.Budget, input.Policy, result, input.Control);
            await SpcBranchStudyFiles.SaveAsync(study, Path.Combine(root, "study.spc-study.json"));
            var audits = new List<object>();
            foreach (var branch in result.Branches.Where(b => result.QualifiedBranchIds.Contains(b.Id)))
            {
                var compiled = TopologyCompiler.Compile(branch.Endpoint!, input.Plan.Constraints);
                var qa = TopologyValidator.Validate(compiled, input.Plan.Targets);
                Require(ContentFingerprint.Compute(qa) == ContentFingerprint.Compute(branch.Assessment), "independent exact original QA");
                var path = await AutonomousDesignFiles.ExportAsync(new(branch.Id, branch.ParentId, branch.Endpoint!, qa, 0), input.Plan.Constraints,
                    Path.Combine(root, branch.Sign < 0 ? "minus.staropt" : "plus.staropt"));
                var native = await StarOptProjectStore.LoadAsync(path);
                var imported = TopologyCompiler.Import(native.Configurations[native.ActiveConfigurationIndex].ToSnapshot(), input.Plan.Constraints);
                CheckFrame(imported.Topology, branch.Endpoint!);
                var readback = TopologyValidator.Validate(imported, input.Plan.Targets);
                Require(readback.MeetsTargets, "native readback original QA");
                audits.Add(new { branch.Id, Independent = qa, NativeReadback = readback, Exported = Path.GetFileName(path) });
            }
            await Write(Path.Combine(root, "audit.json"), new { SearchBudgetInput = false, Audits = audits });
            await Write(Path.Combine(root, "summary.json"), new { result.Algorithm, input.Plan.Options.Seed, result.StopReason, result.Work,
                Qualified = result.QualifiedBranchIds.Length, OriginalParentQualified = result.Parents.FirstOrDefault()?.OriginalAssessment?.MeetsTargets,
                StationaryParentQualified = result.Parents.FirstOrDefault()?.StationaryAssessment?.MeetsTargets,
                Parents = result.Parents.Select(p => new { p.Outcome, p.ParentStationarity, p.Saddle }),
                Branches = result.Branches.Select(b => new { b.Id, b.Sign, b.Outcome, b.InitialObjective, b.OptimizedObjective, b.NullMinimum,
                    EndpointFingerprint = b.Endpoint is null ? null : TopologyCompiler.Compile(b.Endpoint, input.Plan.Constraints).OpticFingerprint,
                    WorstRms = b.Assessment?.Fields.Max(f => f.RmsRadiusMillimeters), WorstMaximum = b.Assessment?.Fields.Max(f => f.MaximumRadiusMillimeters),
                    Stages = b.Stages.Select(s => new { s.Name, s.Material, s.ThicknessMillimeters, s.Outcome, s.Work }) }) });
            Console.WriteLine($"{result.Algorithm} seed{input.Plan.Options.Seed}: {result.StopReason}, qualified={result.QualifiedBranchIds.Length}, functions={result.Work.FunctionEvaluations}, rays={result.Work.TracedRays}");
            return;
        }
        if (args[0] == "reopen")
        {
            using var rays = SequentialTraceMeasurement.Begin(); var count = 0;
            foreach (var path in Directory.GetFiles(root, "*.spc-study.json", SearchOption.AllDirectories))
            {
                var study = await SpcBranchStudyFiles.LoadAsync(path);
                Require(study.Result.ProtocolFingerprint == SpcBranchSearch.GetProtocolFingerprint(study.Plan, study.Budget, study.Policy, study.DirectGrowthControl), "reopen protocol"); count++;
            }
            Require(count == 10 && rays.RayCount == 0, "ten portable read-only studies");
            await Write(args[2], new { Studies = count, Rays = rays.RayCount, NewProcessReadback = true }); return;
        }
        if (args[0] == "originals")
        {
            if (Directory.Exists(root)) throw new IOException("Original audit already exists."); Directory.CreateDirectory(root);
            var rows = new List<object>();
            foreach (var seed in Enumerable.Range(42, 5))
            {
                foreach (var name in new[] { "F8", "F4", "half21" })
                {
                    var source = name == "half21" ? $"validation/rechecks/2026-10-30/initial-structure-m2/formal/fixed-seed{seed}/original-half21.seed-search.json"
                        : $"validation/rechecks/2026-11-01/initial-performance/inputs/competition-8-{name}-full25.input.json";
                    using var document = JsonDocument.Parse(await File.ReadAllBytesAsync(source));
                    var item = name == "half21" ? document.RootElement.GetProperty("result").GetProperty("sourcePlan") : document.RootElement.GetProperty("plan");
                    var plan = item.Deserialize<AutonomousSearchPlan>(Json)!; plan = plan with { Options = plan.Options with { Seed = seed } };
                    var before = ContentFingerprint.Compute(plan);
                    var result = SpcBranchSearch.Run(plan, new() { MaximumFunctionEvaluations = 600, MaximumTracedRays = 2_000_000, TimeLimit = TimeSpan.FromSeconds(120) }, new());
                    Require(result.Work.TracedRays == 0 && result.Work.FunctionEvaluations == 0 && result.Branches.IsEmpty && before == ContentFingerprint.Compute(plan), "original guard zero work");
                    Require(name == "F8" ? result.Parents.Single().Outcome == "OriginalElementLimit" : result.StopReason == (name == "F4" ? "OriginalExternalStopApertureConflict" : "OriginalInsertionPermissionConflict"), "original stop classification");
                    await Write(Path.Combine(root, $"{name}-seed{seed}.json"), new { Source = source, SourceSha256 = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(source))), Plan = plan, Result = result });
                    rows.Add(new { name, seed, result.StopReason, Parents = result.Parents.Select(p => p.Outcome), result.Work });
                }
                var old = await new SeedPreparationCheckpointStore().LoadAsync($"validation/rechecks/2026-11-01/initial-performance/candidate/seed{seed}/run.preparation.json");
                var cold = SeedPreparationSearch.Result(old).ColdStart!;
                var qa = TopologyValidator.Validate(TopologyCompiler.Compile(cold.Endpoint!, cold.ColdPlan.Constraints), cold.ColdPlan.Targets);
                Require(qa.MeetsTargets && ContentFingerprint.Compute(qa) == ContentFingerprint.Compute(cold.Assessment), "original F8 exact unchanged full QA");
                await Write(Path.Combine(root, $"old-F8-qualified-seed{seed}.json"), new { Original = cold.Assessment, Current = qa, ExactSameAssessment = true });
            }
            await Write(Path.Combine(root, "summary.json"), new { OriginalGuards = rows, OriginalF8QualifiedEndpointsRecheckedExactly = 5 });
        }
    }
    private static void CheckFrame(DesignTopology actual, DesignTopology expected)
    {
        Require(actual.Stop.ZMillimeters == expected.Stop.ZMillimeters && actual.Stop.ClearSemiDiameterMillimeters == expected.Stop.ClearSemiDiameterMillimeters
            && actual.Stop.MechanicalSemiDiameterMillimeters == expected.Stop.MechanicalSemiDiameterMillimeters && actual.Image.ZMillimeters == expected.Image.ZMillimeters
            && actual.Reference.ZMillimeters == expected.Reference.ZMillimeters && actual.Elements.Length == expected.Elements.Length, "native fixed frame");
        foreach (var pair in actual.Elements.Zip(expected.Elements)) foreach (var faces in new[] { (pair.First.Front, pair.Second.Front), (pair.First.Back, pair.Second.Back) })
            Require(faces.Item1.ClearSemiDiameterMillimeters == faces.Item2.ClearSemiDiameterMillimeters && faces.Item1.MechanicalSemiDiameterMillimeters == faces.Item2.MechanicalSemiDiameterMillimeters, "native original clear/mechanical radius");
    }
    private static void Require(bool condition, string text) { if (!condition) throw new InvalidDataException(text); }
    private static async Task Write(string path, object data)
    {
        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write); await JsonSerializer.SerializeAsync(file, data, Json);
    }
    private sealed record Input(AutonomousSearchPlan Plan, ParaxialSeedSearchOptions Budget, SpcBranchOptions Policy, bool Control);
}

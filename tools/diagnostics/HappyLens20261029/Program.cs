using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

var json = new JsonSerializerOptions { WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    PropertyNameCaseInsensitive = true,
    Converters = { new JsonStringEnumConverter() } };
if (args.Length == 3 && args[0] == "--replay")
{
    var source = JsonSerializer.Deserialize<AutonomousSearchCheckpoint>(await File.ReadAllTextAsync(args[1]), json)!;
    var repeated = AutonomousSearchService.Run(AutonomousSearchService.Create("post-format-replay", source.Plan));
    if (!source.Candidates.Select(candidate => ContentFingerprint.Compute(candidate.Topology)).SequenceEqual(
        repeated.Candidates.Select(candidate => ContentFingerprint.Compute(candidate.Topology)))
        || source.Consumed with { Elapsed = TimeSpan.Zero } != repeated.Consumed with { Elapsed = TimeSpan.Zero })
        throw new InvalidOperationException("The current build does not reproduce the recorded prescriptions and numerical ledger.");
    await File.WriteAllTextAsync(args[2], JsonSerializer.Serialize(repeated, json));
    Console.WriteLine("Current-build replay: identical candidate prescriptions and numerical work ledger.");
    return;
}
if (args.Length == 4 && args[0] == "--export-plan")
{
    var state = JsonSerializer.Deserialize<AutonomousSearchCheckpoint>(await File.ReadAllTextAsync(args[1]), json)!;
    AutonomousSearchService.Validate(state);
    using var preparation = JsonDocument.Parse(await File.ReadAllBytesAsync(args[2]));
    var options = preparation.RootElement.GetProperty("result").GetProperty("options").Deserialize<ParaxialSeedSearchOptions>(json)!;
    await AutonomousDesignFiles.SavePlanAsync(state.Plan with { Options = state.Plan.Options with { SeedPreparation = options } }, args[3]);
    return;
}
var directory = Path.GetFullPath(args.Single()); Directory.CreateDirectory(directory);
var original = AutonomousDesignExamples.ThreeElement();
var stage = new FixedTopologyOptions { ComputeMode = DesignComputeMode.Cpu, PerformFinalAssessment = false,
    RequireDescent = true, MaximumUpdates = 4, MaximumFunctionEvaluations = 24, MaximumGradientEvaluations = 6,
    LearningRate = .002, Sampling = new() { PupilRings = 3, PupilArms = 12, ProfileRadialSamples = 16 } };
var plan = original with { Options = original.Options with { AxialCompensation = null, Expansion = null,
    CheckStageFeasibility = null, FixedTopologyOnly = true, CandidatesPerElementCount = 4,
    MaximumTrials = 4, MaximumFunctionEvaluations = 300, MaximumGradientEvaluations = 80,
    MaximumTracedRays = 2_000_000, MaximumStageTracedRays = 200_000, FinalValidationRayReserve = 100_000,
    TimeLimit = TimeSpan.FromSeconds(45), Local = stage, Full = stage } };
var records = new List<object>();
foreach (var seed in new[] { 42L, 73L, 101L })
{
    foreach (var mode in new[] { "Template", "RandomPhysical", "Delano" })
    {
        var input = plan with { Options = plan.Options with { Seed = seed } };
        StructureGenerationOptions? descriptor = mode == "Template" ? null : new()
        {
            Method = Enum.Parse<StructureGenerationMethod>(mode),
            Elements = [new() { Materials = ["N-BK7"] }, new() { Materials = ["N-F2"] }, new() { Materials = ["N-BK7"] }],
            ClearSemiDiameterMillimeters = 5, MinimumThicknessMillimeters = 2, MaximumThicknessMillimeters = 3,
            MinimumBackFocusMillimeters = 10, AnnealingProposals = 8
        };
        var preparation = ParaxialSeedSearch.Run(input, new() { Generation = descriptor, MaximumStarts = 4, RetainedSeeds = 4,
            MaximumFunctionEvaluations = 400, MaximumEvaluationsPerStart = 100, TimeLimit = TimeSpan.FromSeconds(30) });
        var id = mode + "-" + seed;
        await ParaxialSeedSearchFiles.SaveAsync(preparation, Path.Combine(directory, id + ".seed-search.json"));
        if (preparation.Seeds.IsEmpty)
        {
            records.Add(new { id, preparation.Work, Retained = 0 });
            continue;
        }
        foreach (var population in new[] { false, true })
        {
            var searchPlan = input with { Seeds = preparation.Seeds, Options = input.Options with
                { PoolDiversity = new(), PopulationExploration = population ? new() : null,
                    // Match four permitted gradient updates per decision in both arms.
                    Local = population ? stage with { MaximumUpdates = 2 } : stage,
                    Full = population ? stage with { MaximumUpdates = 2 } : stage } };
            var state = AutonomousSearchService.Run(AutonomousSearchService.Create(id + (population ? "-population" : "-local"), searchPlan));
            await File.WriteAllTextAsync(Path.Combine(directory, state.RunId + ".json"), JsonSerializer.Serialize(state, json));
            var best = state.Candidates.OrderByDescending(candidate => candidate.Assessment.MeetsTargets).ThenBy(candidate => candidate.Score).FirstOrDefault();
            if (best is not null) await AutonomousDesignFiles.ExportAsync(best, searchPlan.Constraints, Path.Combine(directory, state.RunId + ".staropt"));
            var record = new { id, Population = population, Retained = preparation.Seeds.Length,
                PreparationWork = preparation.Work, SearchWork = state.Consumed, state.Status, state.TrialCursor,
                Qualified = state.AcceptedArchive.Length, Best = best is null ? null : new { best.Id, best.Score,
                    best.Assessment.EffectiveFocalLengthMillimeters, best.Assessment.FNumber,
                    RmsMicrometers = best.Assessment.Fields.Max(field => field.RmsRadiusMillimeters) * 1000,
                    MaximumMicrometers = best.Assessment.Fields.Max(field => field.MaximumRadiusMillimeters) * 1000,
                    best.Assessment.Violations } };
            records.Add(record);
            Console.WriteLine(JsonSerializer.Serialize(record, json));
            await File.WriteAllTextAsync(Path.Combine(directory, "summary.json"), JsonSerializer.Serialize(records, json));
        }
    }
}

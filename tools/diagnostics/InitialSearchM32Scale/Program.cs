using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

if (args.Length != 3) throw new ArgumentException("<repository-root> <frozen-main-inputs> <new-output>");
var root = Path.GetFullPath(args[0]); var inputs = Path.GetFullPath(args[1]); var output = Path.GetFullPath(args[2]);
if (Directory.Exists(output)) throw new IOException("Existing evidence is never overwritten.");
Directory.CreateDirectory(output);
try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch (InvalidOperationException) { }
using var serial = ComputationParallelism.SuppressNestedParallelism();
var json = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = true,
    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
};
var library = await LensLibraryFiles.LoadAsync(Path.Combine(inputs, "sources.lens-library.json"));
var entry = library.Entries.Single(entry => entry.Id == "existing-010");
using var historical = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, entry.Provenance.Origin)));
var prior = historical.RootElement.Deserialize<ParaxialSeedSearchResult>(json)!;
var prototype = TopologyCompiler.Compile(entry.Topology, entry.Constraints);
var rows = new List<object>();
for (var seed = 42; seed <= 46; seed++)
    foreach (var factor in new[] { .5, 2d })
    {
        // These are two explicitly new size queries. Historic tasks remain unchanged.
        var bounds = entry.Constraints with { MaximumMechanicalTrackMillimeters = entry.Constraints.MaximumMechanicalTrackMillimeters * Math.Max(1, factor) };
        var scaled = TopologyCompiler.Import(CenteredPrescriptionScaling.Scale(prototype.Snapshot, factor).ToSnapshot(), bounds,
            entry.Topology.Reference.ZMillimeters * factor);
        var targets = prior.SourcePlan.Targets with
        {
            EffectiveFocalLengthMillimeters = entry.Descriptor.EffectiveFocalLengthMillimeters * factor,
            MaximumOpticalTrackMillimeters = prior.SourcePlan.Targets.MaximumOpticalTrackMillimeters * Math.Max(1, factor)
        };
        var plan = prior.SourcePlan with
        {
            Seeds = [scaled.Topology],
            Constraints = bounds,
            Targets = targets,
            Options = prior.SourcePlan.Options with { Seed = seed, SeedPreparation = null }
        };
        var options = new ParaxialSeedSearchOptions
        {
            MaximumStarts = 1,
            RetainedSeeds = 1,
            MaximumEvaluationsPerStart = 8,
            MaximumFunctionEvaluations = 8,
            MaximumTracedRays = 1000000,
            TimeLimit = TimeSpan.FromSeconds(30),
            Retrieval = new() { Library = new() { Name = "Existing source 010 scale check", Entries = [entry] }, MaximumCandidates = 1, MinimumScale = .5, MaximumScale = 2 }
        };
        var direct = TopologyValidator.Validate(scaled, targets);
        var result = ParaxialSeedSearch.Run(plan, options);
        var trial = result.Retrieval!.Trials.Single();
        Require(trial.Transform?.UniformScale == factor && trial.Assessment is { Completed: true }, "Actual non-unit transfer and complete validation are required.");
        Require(direct.Completed && direct.MeetsTargets == trial.Assessment!.MeetsTargets, "Direct original gate and retrieved original gate disagree.");
        Require(ContentFingerprint.Compute(direct) == ContentFingerprint.Compute(trial.Assessment), "Direct and retrieved full assessments differ.");
        Require(targets.MaximumRmsRadiusMillimeters == prior.SourcePlan.Targets.MaximumRmsRadiusMillimeters
            && targets.MaximumSpotRadiusMillimeters == prior.SourcePlan.Targets.MaximumSpotRadiusMillimeters
            && bounds.MinimumCenterThicknessMillimeters == entry.Constraints.MinimumCenterThicknessMillimeters
            && bounds.MinimumAirGapMillimeters == entry.Constraints.MinimumAirGapMillimeters
            && bounds.MinimumBackFocusMillimeters == entry.Constraints.MinimumBackFocusMillimeters, "Absolute image/manufacturing minima changed.");
        var name = $"seed{seed}-scale{factor.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        await ParaxialSeedSearchFiles.SaveAsync(result, Path.Combine(output, name + ".seed-search.json"));
        File.WriteAllText(Path.Combine(output, name + ".direct.json"), JsonSerializer.Serialize(new { plan, options, scaled.TopologyFingerprint, direct }, json));
        rows.Add(new
        {
            seed,
            factor,
            direct.Completed,
            direct.MeetsTargets,
            direct.TracedRayCount,
            retrievalRays = result.Work.TracedRays,
            worstRmsMillimeters = direct.Fields.Max(field => field.RmsRadiusMillimeters),
            worstMaximumMillimeters = direct.Fields.Max(field => field.MaximumRadiusMillimeters),
            violationCodes = direct.Violations.Select(violation => violation.Code),
            assessmentHash = ContentFingerprint.Compute(direct)
        });
        Console.WriteLine($"seed {seed}, scale {factor}: direct/retrieved complete and identical; qualified {direct.MeetsTargets}.");
    }
File.WriteAllText(Path.Combine(output, "verification.json"), JsonSerializer.Serialize(new
{
    identity = "initial-structure-m32-scale-gates-v1",
    sourceEntry = entry.Id,
    originalImageTargets = prior.SourcePlan.Targets,
    cases = rows
}, json));
static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }

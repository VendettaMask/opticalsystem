using System.Collections.Immutable;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

if (args.Length < 3) throw new ArgumentException("prepare <root> <new-inputs> | run <root> <inputs> <new-output> <seed> <fixed|retrieval> [case] | verify <root> <formal> <new-output>");
var mode = args[0]; var root = Path.GetFullPath(args[1]);
var json = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = true,
    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
};
try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch (InvalidOperationException) { }
using var serial = ComputationParallelism.SuppressNestedParallelism();
if (mode == "prepare")
{
    var output = NewDirectory(args[2]); var entries = new List<LensLibraryEntry>(); var identities = new HashSet<string>();
    AutonomousSearchPlan? sanity = null; ParaxialSeedSearchOptions? prototype = null;
    for (var seed = 42; seed <= 46; seed++)
    {
        foreach (var name in new[] { "competition-8-F8-full25", "competition-8-F4-full25", "original-half21" })
        {
            var prefix = $"validation/rechecks/2026-10-30/initial-structure-m2/formal/fixed-seed{seed}/{name}";
            var previous = ReadResult(prefix + ".seed-search.json"); prototype ??= previous.Options;
            using var sourceInput = JsonDocument.Parse(File.ReadAllBytes(At(prefix + ".input.json")));
            var acquisition = sourceInput.RootElement.GetProperty("acquisition").Deserialize<AutonomousSearchWork>(json)!;
            var parent = previous.Evolution!.Parents.Single();
            Add(parent.Topology, previous.SourcePlan.Constraints, prefix + ".seed-search.json",
                name == "original-half21" ? null : acquisition, name == "original-half21" ? LensSourceKind.UserProvidedLocalUse : LensSourceKind.ProjectAuthored);
            var off = FixedOptions(previous.Options, acquisition);
            var plan = previous.SourcePlan with { Options = previous.SourcePlan.Options with { Seed = seed, SeedPreparation = null } };
            Write(output, name + $"-seed{seed}.input.json", new Input(plan, off, acquisition, prefix + ".input.json", Sha(At(prefix + ".input.json"))));
        }
        foreach (var arm in new[] { "off", "combined" })
        {
            var path = $"validation/rechecks/2026-10-30/initial-structure-m0-m1/formal/{arm}-seed{seed}/competition-3-F8-full10.json";
            var previous = ReadResult(path);
            foreach (var candidate in previous.Competition!.Candidates.Where(c => c.OriginalAssessment is { Completed: true, MeetsTargets: true }))
            {
                Add(candidate.Topology!, previous.SourcePlan.Constraints, path, previous.Work, LensSourceKind.ProjectAuthored);
                sanity ??= previous.SourcePlan with { Seeds = [candidate.Topology!] };
            }
        }
    }
    if (sanity is null) throw new InvalidDataException("No previously qualified authored small-field prescription available for the controlled retrieval case.");
    var first = sanity.Seeds[0].Elements[0];
    var query = sanity.Seeds[0] with
    {
        Name = "Controlled retrieval query: known source with front-curvature perturbation",
        Elements = sanity.Seeds[0].Elements.SetItem(0, first with
        {
            Front = first.Front with
            {
                Profile = first.Front.Profile with
                { CurvaturePerMillimeter = first.Front.Profile.CurvaturePerMillimeter + .0005 }
            }
        })
    };
    for (var seed = 42; seed <= 46; seed++)
        Write(output, $"controlled-3-F8-full10-seed{seed}.input.json", new Input(sanity with { Seeds = [query], Options = sanity.Options with { Seed = seed, SeedPreparation = null } },
            FixedOptions(prototype!, new()), new(), "Controlled fixture from a recorded qualified source; acquisition charged separately in library", ContentFingerprint.Compute(query)));
    var library = new LensLibraryDocument { Name = "Frozen project-authored and user-provided historical prescriptions", Entries = entries.ToImmutableArray() };
    await LensLibraryFiles.SaveAsync(library, Path.Combine(output, "sources.lens-library.json"));
    Write(output, "library-index.json", entries.Select(entry => new { entry.Id, entry.Provenance, entry.SourceOpticFingerprint, entry.Descriptor, entry.IndexingWork, entry.SourceAcquisitionWork }));
    Console.WriteLine($"Frozen {entries.Count} existing prescriptions and 20 queries. Controlled case is a retrieval sanity check, not a new-design benchmark.");

    void Add(DesignTopology topology, TopologyConstraints constraints, string origin, AutonomousSearchWork? work, LensSourceKind kind)
    {
        if (!identities.Add(ContentFingerprint.Compute(topology with { Name = "library" }))) return;
        var entry = LensLibraryService.Index("existing-" + entries.Count.ToString("D3"), new(kind, topology.Name, origin,
            kind == LensSourceKind.ProjectAuthored ? "Repository-authored construction; local product verification use."
                : "Previously user-provided prescription; local design use only, original creation cost unknown; no redistribution grant asserted.", Sha(At(origin))),
            TopologyCompiler.Compile(topology, constraints), work);
        entries.Add(entry);
    }
}
else if (mode == "run")
{
    if (args.Length is < 6 or > 7) throw new ArgumentException("run <root> <inputs> <new-output> <seed> <fixed|retrieval> [case]");
    var inputs = Path.GetFullPath(args[2]); var output = NewDirectory(args[3]); var seed = int.Parse(args[4]); var arm = args[5];
    if (arm is not ("fixed" or "retrieval")) throw new ArgumentException("Unknown arm.");
    var library = await LensLibraryFiles.LoadAsync(Path.Combine(inputs, "sources.lens-library.json"));
    foreach (var name in new[] { "controlled-3-F8-full10", "competition-8-F8-full25", "competition-8-F4-full25", "original-half21" })
    {
        if (args.Length == 7 && args[6] != name) continue;
        var input = JsonSerializer.Deserialize<Input>(File.ReadAllBytes(Path.Combine(inputs, name + $"-seed{seed}.input.json")), json)!;
        var options = arm == "fixed" ? input.Options : input.Options with
        {
            Evolution = null,
            Competition = null,
            Generation = null,
            FixedStopRecoveryVersion = null,
            ImageScreening = null,
            MaximumStarts = 16,
            Retrieval = new() { Library = library, MaximumCandidates = 2, MinimumScale = .5, MaximumScale = 2, FinalRayRecoveryFactor = 12 }
        };
        var plan = input.Plan with { Options = input.Plan.Options with { SeedPreparation = options } };
        Write(output, name + ".input.json", new
        {
            plan,
            options,
            acquisition = input.Acquisition,
            input.Origin,
            input.OriginSha256,
            unifiedBudget = new { functions = 600, gradients = 200, rays = 2000000, seconds = 110 },
            libraryFingerprint = ContentFingerprint.Compute(library)
        });
        var result = ParaxialSeedSearch.Run(plan, options);
        await ParaxialSeedSearchFiles.SaveAsync(result, Path.Combine(output, name + ".seed-search.json"));
        Write(output, name + ".summary.json", new
        {
            name,
            seed,
            arm,
            result.Termination,
            result.Work,
            acquisition = input.Acquisition,
            retained = result.Seeds.Length,
            qualifiedExisting = result.Retrieval?.QualifiedEntryIds.Length ?? result.Evolution!.QualifiedArchive.Count(q => q.ParentId is null),
            qualifiedChanged = result.Evolution?.QualifiedArchive.Count(q => q.ParentId is not null) ?? 0,
            originalInputReused = result.Retrieval?.Trials.Count(t => t.MatchesOriginalInput && t.Assessment?.MeetsTargets == true) ?? 0,
            endToEnd = AddWork(result.Work, input.Acquisition),
            retrieval = result.Retrieval
        });
        Console.WriteLine($"{arm}, seed {seed}, {name}: {result.Termination}; online functions {result.Work.FunctionEvaluations}, rays {result.Work.TracedRays}, retained {result.Seeds.Length}.");
    }
}
else if (mode == "verify")
{
    var formal = Path.GetFullPath(args[2]); var output = NewDirectory(args[3]); var checks = new List<object>(); var count = 0; long replayRays = 0;
    foreach (var path in Directory.GetFiles(formal, "*.seed-search.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
    {
        using var saved = JsonDocument.Parse(File.ReadAllBytes(path)); var result = saved.RootElement.GetProperty("result").Deserialize<ParaxialSeedSearchResult>(json)!;
        Require(saved.RootElement.GetProperty("sha256").GetString() == ContentFingerprint.Compute(result), "Saved result hash mismatch.");
        Require(result.Work.FunctionEvaluations <= result.Options.MaximumFunctionEvaluations && result.Work.TracedRays <= result.Options.MaximumTracedRays, "Online hard budget exceeded.");
        var plan = result.SourcePlan; var assessments = new List<(DesignTopology Topology, TopologyAssessment Assessment)>();
        if (result.Retrieval is { } retrieval)
        {
            Require(retrieval.LibraryFingerprint == ContentFingerprint.Compute(result.Options.Retrieval!.Library), "Library identity mismatch.");
            Require(result.Work.FunctionEvaluations == retrieval.Trials.Sum(t => t.Work.FunctionEvaluations)
                && result.Work.ValidationEvaluations == retrieval.Trials.Sum(t => t.Work.ValidationEvaluations)
                && result.Work.TracedRays == retrieval.Trials.Sum(t => t.Work.TracedRays)
                && result.Work.GeometryProbes == retrieval.Trials.Sum(t => t.Work.GeometryProbes), "Retrieval work does not sum.");
            foreach (var trial in retrieval.Trials)
            {
                Require(trial.Work.FunctionEvaluations <= result.Options.MaximumEvaluationsPerStart, "Per-entry budget exceeded.");
                if (trial.Topology is { } topology)
                {
                    Require(ContentFingerprint.Compute(topology with { Elements = plan.Seeds[0].Elements }) == ContentFingerprint.Compute(plan.Seeds[0]), "Task fields, wavelengths, environment or fixed metadata changed.");
                    Require(topology.Stop == plan.Seeds[0].Stop && topology.Image == plan.Seeds[0].Image && topology.Reference == plan.Seeds[0].Reference, "Fixed planes changed.");
                    Require(topology.Elements.Length == plan.Seeds[0].Elements.Length, "Fixed aperture mapping changed element count.");
                    foreach (var (a, b) in topology.Elements.Zip(plan.Seeds[0].Elements))
                    {
                        Require(a.Front.ClearSemiDiameterMillimeters == b.Front.ClearSemiDiameterMillimeters && a.Back.ClearSemiDiameterMillimeters == b.Back.ClearSemiDiameterMillimeters
                            && a.Front.MechanicalSemiDiameterMillimeters == b.Front.MechanicalSemiDiameterMillimeters && a.Back.MechanicalSemiDiameterMillimeters == b.Back.MechanicalSemiDiameterMillimeters, "Original apertures changed.");
                        Require(!b.Protected || ContentFingerprint.Compute(a) == ContentFingerprint.Compute(b), "Protected lens changed.");
                        var window = plan.Options.AxialCompensation is { } axial && (axial.ElementIds.IsEmpty || axial.ElementIds.Contains(b.Id)) ? axial.MaximumDisplacementMillimeters : 0;
                        Require(Math.Abs(a.FrontZMillimeters - b.FrontZMillimeters) <= window && Math.Abs(a.BackZMillimeters - b.BackZMillimeters) <= window, "Original axial displacement permission exceeded.");
                    }
                }
                if (trial.Assessment is { } assessment)
                {
                    Require(retrieval.QualifiedEntryIds.Contains(trial.EntryId) == (assessment.Completed && assessment.MeetsTargets), "Intermediate state qualified.");
                    assessments.Add((trial.Topology!, assessment));
                }
            }
            foreach (var source in result.Options.Retrieval!.Library.Entries)
                Require(Sha(At(source.Provenance.Origin)) == source.Provenance.SourceDocumentSha256, "Frozen source receipt changed.");
        }
        else
        {
            assessments.AddRange(result.Evolution!.Parents.Select(p => (p.Topology, p.Assessment)));
            assessments.AddRange(result.Evolution.Trials.Where(t => t.OriginalAssessment is not null).Select(t => (t.Endpoint!, t.OriginalAssessment!)));
            if (result.Competition is { } competition) assessments.AddRange(competition.Candidates.Where(c => c.OriginalAssessment is not null).Select(c => (c.Topology!, c.OriginalAssessment!)));
        }
        foreach (var (topology, old) in assessments.DistinctBy(pair => ContentFingerprint.Compute(new { pair.Topology, pair.Assessment })))
        {
            var source = TopologyCompiler.Compile(topology, plan.Constraints);
            Require(old.Targets == plan.Targets && ContentFingerprint.Compute(old.StructuralConstraints) == ContentFingerprint.Compute(plan.Constraints) && source.OpticFingerprint == old.OpticFingerprint,
                "Original targets, constraints or prescription changed.");
            var fresh = TopologyValidator.Validate(source, plan.Targets); replayRays += fresh.TracedRayCount;
            Require(old.Completed && ContentFingerprint.Compute(old) == ContentFingerprint.Compute(fresh), "Independent original dense assessment does not reproduce exactly."); count++;
        }
        foreach (var topology in result.Seeds)
        {
            var fp = TopologyCompiler.Compile(topology, plan.Constraints).OpticFingerprint;
            Require(assessments.Any(pair => pair.Assessment.Completed && pair.Assessment.OpticFingerprint == fp
                && pair.Assessment.Violations.All(v => !v.Code.StartsWith("geometry:", StringComparison.Ordinal) && v.Code is not ("focal-length" or "f-number" or "transition-element"))), "Invalid working seed escaped.");
        }
        checks.Add(new { path = Path.GetRelativePath(formal, path).Replace('\\', '/'), result.Algorithm, result.Work, denseAssessments = assessments.Count, result.Termination });
    }
    Write(output, "verification.json", new { resultCount = checks.Count, independentlyReplayedAssessments = count, replayTracedRays = replayRays, checks });
    Console.WriteLine($"Verified {checks.Count} results and replayed {count} complete original dense assessments ({replayRays} separate verifier rays).");
}
else throw new ArgumentException("Unknown mode.");

string At(string relative)
{
    var full = Path.GetFullPath(Path.Combine(root, relative));
    if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The evidence source must stay inside this repository.");
    return full;
}
ParaxialSeedSearchResult ReadResult(string relative)
{
    using var document = JsonDocument.Parse(File.ReadAllBytes(At(relative)));
    var node = document.RootElement.TryGetProperty("result", out var wrapped) ? wrapped : document.RootElement;
    var result = node.Deserialize<ParaxialSeedSearchResult>(json)!;
    if (document.RootElement.TryGetProperty("sha256", out var checksum)) Require(checksum.GetString() == ContentFingerprint.Compute(result), "Source result hash mismatch.");
    return result;
}
ParaxialSeedSearchOptions FixedOptions(ParaxialSeedSearchOptions prototype, AutonomousSearchWork acquisition) => prototype with
{
    Retrieval = null,
    Generation = null,
    MaximumStarts = 4,
    RetainedSeeds = 2,
    MaximumFunctionEvaluations = 600 - acquisition.FunctionEvaluations,
    MaximumEvaluationsPerStart = 160,
    MaximumTracedRays = 2000000 - acquisition.TracedRays,
    TimeLimit = TimeSpan.FromSeconds(110 - acquisition.Elapsed.TotalSeconds),
    Evolution = new()
    {
        MaximumParents = 1,
        MaximumBranches = 1,
        Operations = [],
        Mode = StructureEvolutionMode.RecoveryAndCompetition,
        MaximumPhaseTime = TimeSpan.FromSeconds(4),
        RecoveryFunctionsPerBranch = 24,
        CompensationUpdatesPerBranch = 3
    },
    Competition = prototype.Competition! with { MaximumGradientEvaluations = 200 - acquisition.GradientEvaluations }
};
void Write(string directory, string name, object value) => File.WriteAllText(Path.Combine(directory, name), JsonSerializer.Serialize(value, json));
static string NewDirectory(string path) { path = Path.GetFullPath(path); if (Directory.Exists(path)) throw new IOException("Existing evidence is never overwritten."); Directory.CreateDirectory(path); return path; }
static string Sha(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
static AutonomousSearchWork AddWork(AutonomousSearchWork a, AutonomousSearchWork b) => new()
{
    FunctionEvaluations = a.FunctionEvaluations + b.FunctionEvaluations,
    GradientEvaluations = a.GradientEvaluations + b.GradientEvaluations,
    TracedRays = a.TracedRays + b.TracedRays,
    Elapsed = a.Elapsed + b.Elapsed
};
internal sealed record Input(AutonomousSearchPlan Plan, ParaxialSeedSearchOptions Options, AutonomousSearchWork Acquisition, string Origin, string OriginSha256);

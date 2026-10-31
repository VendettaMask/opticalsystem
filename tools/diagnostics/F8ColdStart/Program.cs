using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

if (args.Length != 4) throw new ArgumentException("<original-input> <new-output> <seed> <control|candidate>");
var inputPath = Path.GetFullPath(args[0]); var output = Path.GetFullPath(args[1]); var seed = long.Parse(args[2]); var arm = args[3];
if (arm is not ("control" or "candidate")) throw new ArgumentException("Unknown arm.");
if (Directory.Exists(output)) throw new IOException("Existing evidence is never overwritten.");
Directory.CreateDirectory(output);
try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Normal; } catch (InvalidOperationException) { }
var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true,
    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals, Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
var bytes = await File.ReadAllBytesAsync(inputPath); using var input = JsonDocument.Parse(bytes);
var originalPlan = input.RootElement.GetProperty("plan").Deserialize<AutonomousSearchPlan>(json)!;
var originalPreparation = input.RootElement.GetProperty("options").Deserialize<ParaxialSeedSearchOptions>(json)!;
var plan = originalPlan with { Options = originalPlan.Options with { Seed = seed } };
var preparation = originalPreparation with { Retrieval = null, Evolution = null, Competition = null,
    MaximumFunctionEvaluations = 600, MaximumEvaluationsPerStart = 600, MaximumTracedRays = 10_000_000, TimeLimit = TimeSpan.FromSeconds(600) };
var policy = new ColdStartDesignOptions { ExploreBackFocus = arm == "candidate", MaximumGradientEvaluations = 600 };
await File.WriteAllTextAsync(Path.Combine(output, "input.json"), JsonSerializer.Serialize(new
{
    inputPath, SourceSha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)), seed, arm,
    ColdStart = true, ImportedEndpoint = false, ImportedCheckpoint = false, HistoricalAcquisitionCost = "Not reused: fresh construction is charged in this run",
    plan = ColdStartDesignSearch.ColdPlan(plan), preparation, policy,
    SpecFingerprint = ContentFingerprint.Compute(new { originalPlan.Targets, originalPlan.Constraints, originalPreparation.Generation,
        OriginalPermissions = originalPlan.Options with { Seed = 0, SeedPreparation = null },
        originalPlan.Seeds[0].Reference, originalPlan.Seeds[0].Stop, originalPlan.Seeds[0].Image,
        originalPlan.Seeds[0].Fields, originalPlan.Seeds[0].Wavelengths })
}, json));
var cpuStart = Process.GetCurrentProcess().TotalProcessorTime;
var result = ColdStartDesignSearch.Run(plan, preparation, policy, progress: text =>
{
    File.AppendAllText(Path.Combine(output, "progress.log"), DateTimeOffset.Now.ToString("O") + " " + text + Environment.NewLine);
    Console.WriteLine($"{arm}/{seed} {text}");
});
await File.WriteAllTextAsync(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new
{ Sha256 = ContentFingerprint.Compute(result), result }, json));
await File.WriteAllTextAsync(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new
{
    seed, arm, result.StopReason, result.Qualified, result.Work, Cpu = Process.GetCurrentProcess().TotalProcessorTime - cpuStart,
    InitialLayouts = result.Construction.Length, result.Phases.Length,
    Fields = result.Assessment?.Fields.Length,
    MinimumValid = result.Assessment is { Fields.IsEmpty: false } a ? a.Fields.Min(f => (double)f.ValidRays / f.AttemptedRays) : (double?)null,
    LostBoundary = result.Assessment?.Fields.Sum(f => f.CheckAttemptedRays - f.CheckValidRays),
    WorstRms = result.Assessment is { Completed: true } b && b.Fields.All(f => f.RmsRadiusMillimeters is not null) ? b.Fields.Max(f => f.RmsRadiusMillimeters) : null,
    WorstMaximum = result.Assessment is { Completed: true } c && c.Fields.All(f => f.MaximumRadiusMillimeters is not null) ? c.Fields.Max(f => f.MaximumRadiusMillimeters) : null,
    result.Assessment?.Violations
}, json));
Console.WriteLine($"DONE {arm}/{seed}: qualified={result.Qualified}, {result.StopReason}, functions={result.Work.FunctionEvaluations}, rays={result.Work.TracedRays}");

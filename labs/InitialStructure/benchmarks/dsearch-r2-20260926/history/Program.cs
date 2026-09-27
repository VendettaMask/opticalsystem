using System.Security.Cryptography;
using System.Text.Json;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.InitialStructure.Contracts;
using OptilandWorkbench.InitialStructure.Engine;
using OptilandWorkbench.InitialStructure.Persistence;

var store = new FlatStartSearchCheckpointStore();
var hash = Hash(args[1]);
var source = await store.LoadAsync(args[1]);
if (args[0] == "verify")
{
    if (source.Algorithm.Version != "9" || FlatStartAlgorithm.Version != "10")
        throw new InvalidOperationException("Expected a real v9 source and v10 runtime.");
    try
    {
        await new FlatStartSearchService().RunAsync(source.Specification, source.Options, checkpoint: source);
        throw new InvalidOperationException("Historical resume was not rejected.");
    }
    catch (InvalidDataException) { }
}
else if (args[0] != "capture" || FlatStartAlgorithm.Version != "9")
    throw new InvalidOperationException("Capture must use frozen v9 binaries.");
var selected = FlatStartSearchService.SelectCandidates(source)[0];
var child = FlatStartSearchService.CreateRefinementCheckpoint(source, selected.CandidateId, 120, TimeSpan.FromMinutes(1));
var result = await new FlatStartSearchService().RunAsync(child.Specification, child.Options, checkpoint: child);
FlatStartCheckpointValidation.Validate(result.Checkpoint);
await store.SaveAsync(result.Checkpoint, args[2]);
var loaded = await store.LoadAsync(args[2]);
if (ContentFingerprint.Compute(loaded) != ContentFingerprint.Compute(result.Checkpoint) || hash != Hash(args[1]))
    throw new InvalidOperationException("Source changed or current checkpoint failed round trip.");
var best = result.Candidates[0];
var export = Path.ChangeExtension(args[2], ".staropt");
await new CandidateExportService().ExportStarOptAsync(best, export);
var document = await StarOptProjectStore.LoadAsync(export);
if (ContentFingerprint.Compute(document.Configurations[document.ActiveConfigurationIndex].ToSnapshot()) != best.OpticFingerprint)
    throw new InvalidOperationException("Export differs from candidate.");
File.WriteAllText(args[3], JsonSerializer.Serialize(new
{
    SourceVersion = source.Algorithm.Version,
    CurrentVersion = FlatStartAlgorithm.Version,
    SourceSha256 = hash,
    SourceUnchanged = hash == Hash(args[1]),
    CheckpointSha256 = Hash(args[2]),
    result.Checkpoint.ChargedEvaluations,
    best.Status,
    best.Evaluation.FlatStartObjective,
    ExportReloadIdentical = true,
    EngineSha256 = Hash(typeof(FlatStartSearchService).Assembly.Location)
}, new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine($"Historical {source.Algorithm.Version} -> current {FlatStartAlgorithm.Version}; {best.Status}; source unchanged.");

static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

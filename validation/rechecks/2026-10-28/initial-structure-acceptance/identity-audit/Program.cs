using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

// Read-only diagnostic: never supplies a state for resume and never rewrites its identity.
var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
if (args[0] == "--prepare-native")
{
    var source = AutonomousDesignExamples.ThreeElement();
    var stage = source.Options.Full with { TransmissionFirst = null, TransmissionPolicyVersion = null, ActiveParameterLimit = null,
        MaximumUpdates = 2, MaximumFunctionEvaluations = 3, MaximumGradientEvaluations = 3,
        Sampling = new() { FieldsPerDirection = 3, PupilRings = 1, PupilArms = 8, ProfileRadialSamples = 8 } };
    var plan = source with { Seeds = [source.Seeds[0] with { Fields = [new("Axis", 0, 0)] }],
        Options = source.Options with { AxialCompensation = null, MaximumTrials = 1, MaximumFunctionEvaluations = 800,
            MaximumGradientEvaluations = 240, MaximumTracedRays = 2_000_000, TimeLimit = TimeSpan.FromSeconds(30),
            Expansion = null, Local = stage, Full = stage } };
    await AutonomousDesignFiles.SavePlanAsync(plan, args[1]);
    return;
}
using var document = JsonDocument.Parse(await File.ReadAllBytesAsync(args[0]));
var state = document.RootElement.GetProperty("checkpoint").Deserialize<AutonomousSearchCheckpoint>(options)!;
if (document.RootElement.GetProperty("sha256").GetString() != ContentFingerprint.Compute(state))
    throw new InvalidDataException("Audit input checksum mismatch.");
var evaluationPlan = typeof(TopologyValidator).Assembly.GetType("OpticalSystemDesign.InitialStructure.Engine.TopologyEvaluationPlan")!;
var checks = evaluationPlan.GetMethod("Checks", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null);
var fieldsMethod = evaluationPlan.GetMethod("Fields", BindingFlags.NonPublic | BindingFlags.Static)!;
var rows = state.Candidates.Select(candidate => {
    var compiled = TopologyCompiler.Compile(candidate.Topology, state.Plan.Constraints);
    var protocol = TopologyValidator.GetProtocolFingerprint(compiled, state.Plan.Targets);
    return new { candidate.Id, OpticStored = candidate.Assessment.OpticFingerprint, OpticCurrent = compiled.OpticFingerprint,
        ProtocolStored = candidate.Assessment.ProtocolFingerprint, ProtocolCurrent = protocol,
        Fields = fieldsMethod.Invoke(null, [compiled, 21]), compiled.Snapshot,
        TargetsMatch = candidate.Assessment.Targets == state.Plan.Targets,
        ConstraintsMatch = ContentFingerprint.Compute(candidate.Assessment.StructuralConstraints) == ContentFingerprint.Compute(state.Plan.Constraints) };
}).ToArray();
var historical = new List<object>();
if (args.Length > 2)
{
    using var retained = JsonDocument.Parse(await File.ReadAllBytesAsync(args[2]));
    var insensitive = new JsonSerializerOptions(options) { PropertyNameCaseInsensitive = true };
    var source = TopologyCompiler.Compile(state.Candidates[0].Topology, state.Plan.Constraints);
    var fields = (IEnumerable<TopologyFieldCoordinate>)fieldsMethod.Invoke(null, [source, 21])!;
    foreach (var item in retained.RootElement.EnumerateArray())
    {
        var assessment = item.GetProperty("Assessment").Deserialize<TopologyAssessment>(insensitive)!;
        var sameTargets = assessment.Targets == state.Plan.Targets;
        var sameConstraints = ContentFingerprint.Compute(assessment.StructuralConstraints) == ContentFingerprint.Compute(state.Plan.Constraints);
        var sameFields = assessment.Fields.Select(row => row.Field).SequenceEqual(fields);
        if (!sameTargets || !sameConstraints || !sameFields) throw new InvalidDataException("Historical protocol descriptor differs.");
        historical.Add(new { assessment.ProtocolFingerprint, TargetsMatch = sameTargets, ConstraintsMatch = sameConstraints,
            FieldsMatch = sameFields, Portable = TopologyValidator.GetProtocolFingerprint(source, state.Plan.Targets) });
    }
}
var report = new { RuntimeInformation.OSDescription, RuntimeInformation.FrameworkDescription,
    state.RunId, InputSha256 = document.RootElement.GetProperty("sha256").GetString(),
    Pupils = ApertureSampler.GenerateGaussianQuadrature(10, 32), Checks = checks, Candidates = rows, HistoricalEvidence = historical };
await File.WriteAllTextAsync(args[1], JsonSerializer.Serialize(report, new JsonSerializerOptions(options) { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(rows.Select(row => new { row.Id, OpticMatch = row.OpticStored == row.OpticCurrent,
    ProtocolMatch = row.ProtocolStored == row.ProtocolCurrent, row.TargetsMatch, row.ConstraintsMatch }), options));

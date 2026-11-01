using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

if (args.Length == 3 && args[0] == "--validate")
{
    var path = Path.GetFullPath(args[2]); if (Directory.Exists(path)) throw new IOException("Existing validation evidence.");
    Directory.CreateDirectory(path);
    var settings = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals, Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
    using var saved = JsonDocument.Parse(await File.ReadAllBytesAsync(args[1]));
    var value = saved.RootElement.GetProperty("result").Deserialize<FixedTopologyResult>(settings)!;
    using var old = JsonDocument.Parse(await File.ReadAllBytesAsync(saved.RootElement.GetProperty("source").GetString()!));
    var specification = old.RootElement.GetProperty("result").Deserialize<ColdStartDesignResult>(settings)!.ColdPlan;
    var check = TopologyValidator.Validate(TopologyCompiler.Compile(value.Topology, specification.Constraints), specification.Targets);
    await File.WriteAllTextAsync(Path.Combine(path,"validation.json"),JsonSerializer.Serialize(new { DevelopmentOnly=true, SearchBudgetInput=false,check },settings));
    Console.WriteLine($"Independent development check: qualified={check.MeetsTargets}, RMS={check.Fields.Max(f=>f.RmsRadiusMillimeters)*1000:R} um, rim={check.Fields.Sum(f=>f.CheckAttemptedRays-f.CheckValidRays)}");
    if(check.MeetsTargets) await AutonomousDesignFiles.ExportAsync(new("development-only",null,value.Topology,check,0),specification.Constraints,Path.Combine(path,"qualified.staropt"));
    return;
}
if (args.Length is not (2 or 3)) throw new ArgumentException("<saved-failed-cold-result> <new-diagnostic-output> [gate-version]");
var directory = Path.GetFullPath(args[1]);
if (Directory.Exists(directory)) throw new IOException("Existing diagnostics are never overwritten.");
Directory.CreateDirectory(directory);
var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true,
    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals, Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
using var input = JsonDocument.Parse(await File.ReadAllBytesAsync(args[0]));
var previous = input.RootElement.GetProperty("result").Deserialize<ColdStartDesignResult>(json)!;
if (ContentFingerprint.Compute(previous) != input.RootElement.GetProperty("sha256").GetString()) throw new InvalidDataException("Input checksum mismatch.");
var plan = previous.ColdPlan; var source = TopologyCompiler.Compile(previous.Endpoint!, plan.Constraints);
var variables = previous.Phases[^1].AnchoredVariables;
var binding = TopologyParameterBinder.Bind(source, variables, exactMechanicalFrame: true);
using var serial = ComputationParallelism.SuppressNestedParallelism();
using var rays = SequentialTraceMeasurement.BeginCpuBudget(3000000);
var result = FixedTopologyOptimizer.Run(source, binding, plan.Targets, new()
{
    GateCompensationVersion = args.Length == 3 ? int.Parse(args[2]) : 3, MaximumUpdates = 28, MaximumFunctionEvaluations = 30, MaximumGradientEvaluations = 29,
    TimeLimit = TimeSpan.FromSeconds(90), ComputeMode = DesignComputeMode.Cpu,
    Sampling = new() { FieldsPerDirection = 3, PupilRings = 10, PupilArms = 32, ProfileRadialSamples = 64,
        IncludeBoundaryClearance = true, IncludeBoundaryMaximum = true }
});
await File.WriteAllTextAsync(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(new { DevelopmentOnly = true,
    ColdStart = false, SearchBudgetInput = false, Source = args[0], ResultSha256 = ContentFingerprint.Compute(result), result }, json));
var assessment = result.FinalAssessment;
Console.WriteLine($"Development continuation qualified={assessment?.MeetsTargets}, {result.Termination}, fn={result.Work.FunctionEvaluations}");
if (assessment is { Completed: true })
{
    Console.WriteLine($"RMS={assessment.Fields.Max(f => f.RmsRadiusMillimeters)*1000:R} um, maximum={assessment.Fields.Max(f=>f.MaximumRadiusMillimeters)*1000:R} um, rim={assessment.Fields.Sum(f=>f.CheckAttemptedRays-f.CheckValidRays)}");
    if (assessment.MeetsTargets) await AutonomousDesignFiles.ExportAsync(new("development-only", null, result.Topology, assessment, 0), plan.Constraints, Path.Combine(directory,"qualified.staropt"));
}

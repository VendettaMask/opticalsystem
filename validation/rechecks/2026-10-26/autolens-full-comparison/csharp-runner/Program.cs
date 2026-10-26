// Development-only runner over the existing product optimizer. No replacement optical engine.
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

var json = new JsonSerializerOptions { WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
if (args.Length == 3 && args[0] is "profile" or "profile-refine") return PerformanceProbe.Run(args[1], args[2], args[0] == "profile-refine");
if (args.Length == 4 && args[0] == "compare") return EndpointComparison.Run(args[1], args[2], args[3]);
if (args.Length != 2 || args[0] != "train") throw new ArgumentException("train <output-directory>");
var directory = Path.GetFullPath(args[1]);
Directory.CreateDirectory(directory);
var preset = AutoLensReferenceExamples.Mobile();
// Only resource caps differ from the desktop preset. Training configuration/sampling/seed are unchanged.
var fullSpec = preset.Specification with
{
    FlatStart = preset.Specification.FlatStart! with { AutoLens = preset.Specification.FlatStart.AutoLens! with { ComputeMode = DesignComputeMode.Gpu } },
    Budget = preset.Specification.Budget with { MaximumEvaluations = 100_000, TimeLimit = TimeSpan.FromHours(24) }
};
SpecificationValidator.Validate(fullSpec);
Save("input.json", new { Specification = fullSpec, preset.Options, Sampling = fullSpec.FlatStart!.AutoLens!.TrainingSampling ?? new(),
    ResourceOnlyChanges = new { MaximumEvaluations = 100_000, TimeLimitHours = 24, PerCheckpointEvaluationIncrement = 3 },
    Purpose = "Complete 2001+3001 update protocol with unchanged product engine; one-update resumable checkpoints do not compress the training schedule." });
Save("binaries.json", new { StartedUtc = DateTimeOffset.UtcNow, ProcessId = Environment.ProcessId, Runtime = Environment.Version.ToString(),
    Device = DesignGpu.Status, Binaries = new[] { typeof(Optic).Assembly, typeof(FlatStartDesignService).Assembly, Assembly.GetExecutingAssembly() }
        .ToDictionary(a => Path.GetFileName(a.Location), a => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(a.Location))).ToLowerInvariant()) });
var service = new FlatStartDesignService();
FlatStartBootstrapResult? root = Load<FlatStartBootstrapResult>("root-proof.json");
AutoLensManagedState? state = Load<AutoLensManagedState>("state.json");
var timer = Stopwatch.StartNew();
var previousElapsed = state?.ElapsedComputeTicks ?? 0;
var started = DateTimeOffset.UtcNow;
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
FlatStartDesignResult? result = null;
try
{
    while (state?.Phase is not (AutoLensManagedPhase.Completed or AutoLensManagedPhase.DomainFailure))
    {
        if (File.Exists(Path.Combine(directory, "stop.request"))) break;
        var previousCount = state?.EvaluationCount ?? 0;
        var spec = fullSpec with { Budget = fullSpec.Budget with { MaximumEvaluations = previousCount + 3 } };
        WriteProgress("running");
        var lap = Stopwatch.StartNew();
        result = state is null ? service.Solve(spec, 6, cancellation.Token, allowedGlassNames: preset.Options.AllowedGlassNames)
            : service.ResumeAutoLens(spec, root!, state, preset.Options.AllowedGlassNames, cancellationToken: cancellation.Token);
        root ??= result.Bootstrap;
        state = result.AutoLensState ?? throw new InvalidDataException("No AutoLens state returned.");
        AutoLensManagedStateValidation.Validate(state);
        Save("root-proof.json", root);
        Save("state.json", state);
        Save("latest-result.json", result);
        Save("latest.optic.json", state.Optic);
        var steps = state.TrainingSteps;
        var curriculum = steps.Count(s => s.Phase == AutoLensManagedPhase.Curriculum);
        var refinement = steps.Count(s => s.Phase == AutoLensManagedPhase.CatalogRefinement);
        Console.WriteLine($"{DateTimeOffset.UtcNow:O} {state.Phase} curriculum={curriculum}/2001 refinement={refinement}/3001 evals={state.EvaluationCount} lap={lap.Elapsed.TotalSeconds:F3}s loss={state.LastLoss:R}");
        WriteProgress(state.Phase is AutoLensManagedPhase.Completed ? "completed" : state.Phase is AutoLensManagedPhase.DomainFailure ? "domain-failure" : "running");
        SaveLosses();
        if (state.EvaluationCount == previousCount || cancellation.IsCancellationRequested) break;
    }
    if (state?.Phase == AutoLensManagedPhase.Completed)
    {
        Save("final.optic.json", state.Optic);
        Save("training-start.optic.json", state.TrainingStartOptic);
        Save("final-result.json", result);
        var optic = Optic.FromSnapshot(state.Optic);
        Save("final-prescription.json", Describe(optic));
        if (result?.Candidate is { } candidate)
            await new CandidateExportService().ExportStarOptAsync(candidate, Path.Combine(directory, "final.staropt"));
    }
    WriteProgress(state?.Phase == AutoLensManagedPhase.Completed ? "completed"
        : state?.Phase == AutoLensManagedPhase.DomainFailure ? "domain-failure" : "incomplete");
    return state?.Phase == AutoLensManagedPhase.Completed ? 0 : 2;
}
catch (Exception error)
{
    Save("exception.json", new { Error = error.ToString(), LastSavedState = state?.Phase.ToString(), state?.EvaluationCount,
        DurationSeconds = timer.Elapsed.TotalSeconds, UpdatedUtc = DateTimeOffset.UtcNow });
    WriteProgress("failed");
    Console.Error.WriteLine(error);
    return 1;
}

object Describe(Optic optic) => new
{
    Source = "Existing C# AutoLens UpstreamE58 completed endpoint", ObjectDistanceMm = 20000,
    FocalLengthMm = optic.Paraxial.EstimateEffectiveFocalLength(), EntrancePupilDiameterMm = optic.Paraxial.EstimateEntrancePupilDiameter(),
    Surfaces = optic.SurfaceGroup.Items.Skip(1).SkipLast(1).Select(s => new
    {
        Index = s.Number - 1, IsStop = s.IsStop, PositionMm = s.CoordinateSystem.Origin.Z - 20000, RadiusMm = s.SemiDiameter,
        Curvature = OpticalDesignParameters.Profile(s.Geometry).Curvature, Conic = OpticalDesignParameters.Profile(s.Geometry).Conic,
        Coefficients = OpticalDesignParameters.Profile(s.Geometry).Coefficients, Material = s.MaterialAfter.Name
    }), SensorPositionMm = optic.SurfaceGroup.Items[^1].CoordinateSystem.Origin.Z - 20000
};
void WriteProgress(string status) => Save("status.json", new
{
    State = status, ProcessId = Environment.ProcessId, StartedUtc = started, UpdatedUtc = DateTimeOffset.UtcNow,
    Phase = state?.Phase.ToString() ?? "Initialization", PhaseIteration = state?.Adam.Iteration ?? 0,
    CurriculumUpdates = state?.TrainingSteps.Count(s => s.Phase == AutoLensManagedPhase.Curriculum) ?? 0,
    RefinementUpdates = state?.TrainingSteps.Count(s => s.Phase == AutoLensManagedPhase.CatalogRefinement) ?? 0,
    ExpectedCurriculumUpdates = 2001, ExpectedRefinementUpdates = 3001, Evaluations = state?.EvaluationCount ?? 0,
    state?.LastLoss, SecondsThisProcess = timer.Elapsed.TotalSeconds, PreviousElapsedComputeSeconds = previousElapsed / 1e7,
    state?.GpuBatches, state?.GpuRays, state?.LastComputeExecution, state?.PropagationExecution,
    Scope = "Same default sampling and seed; complete existing implementation with one-update exact resumes; resource caps do not shorten stages"
});
void SaveLosses()
{
    var text = "phase\titeration\tpupilFraction\tpreUpdateLoss\tevaluation\n" + string.Join("\n", state!.TrainingSteps.Select(s =>
        FormattableString.Invariant($"{s.Phase}\t{s.Iteration}\t{s.PupilFraction:R}\t{s.BeforeLoss:R}\t{s.ChargedEvaluation}"))) + "\n";
    File.WriteAllText(Path.Combine(directory, "loss-history.tsv"), text);
}
T? Load<T>(string name) => File.Exists(Path.Combine(directory, name))
    ? JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(directory, name)), json) : default;
void Save<T>(string name, T value)
{
    var path = Path.Combine(directory, name);
    var temporary = path + ".tmp";
    File.WriteAllText(temporary, JsonSerializer.Serialize(value, json) + Environment.NewLine);
    File.Move(temporary, path, overwrite: true);
}

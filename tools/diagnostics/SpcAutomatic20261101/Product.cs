using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.App.Laboratories.InitialStructure;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

internal static class Product
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals, Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
    internal static async Task Run(string mode, string output, string? input)
    {
        var session = new AutonomousDesktopSession(output);
        if (mode == "product-start")
        {
            if (Directory.Exists(output)) throw new IOException("Product evidence exists."); Directory.CreateDirectory(output);
            var plan = JsonSerializer.Deserialize<AutonomousSearchPlan>(await File.ReadAllBytesAsync(input!), Json)!;
            plan = plan with { Options = plan.Options with { MaximumTrials = 1, MaximumFunctionEvaluations = 400, MaximumGradientEvaluations = 100,
                MaximumTracedRays = 600000, MaximumStageTracedRays = 200000, FinalValidationRayReserve = 100000, TimeLimit = TimeSpan.FromSeconds(30) } };
            await Write(Path.Combine(output, "input.json"), plan);
            var paused = false;
            await session.RunAsync(plan, false, _ => { }, preparationProgress: (_, _) =>
            {
                if (!paused && session.PreparationCheckpoint?.Spc is { Phase: SpcPreparationPhase.BranchOptimization, Sign: -1, BranchMinimum.Iterations: >= 1 })
                { paused = true; session.Cancel(); }
            });
            Require(paused && session.Checkpoint is null && session.PreparationCheckpoint?.Status == SeedPreparationStatus.Suspended, "Stopped before automatic handoff");
            await Write(Path.Combine(output, "stopped.json"), new { session.PreparationCheckpointPath, State = session.PreparationCheckpoint }); return;
        }
        var path = Directory.GetFiles(output, "*.preparation.json", SearchOption.AllDirectories).Single();
        long loadRays;
        using (var reads = SequentialTraceMeasurement.Begin()) { await session.LoadAsync(path); loadRays = reads.RayCount; }
        Require(loadRays == 0, "Loading never computes");
        var before = session.PreparationCheckpoint!.Consumed;
        if (mode == "product-resume")
        {
            await session.RunAsync(session.PreparationCheckpoint.Plan, true, _ => { });
            Require(session.PreparationCheckpoint?.Status == SeedPreparationStatus.Completed && session.Checkpoint is not null, "Automatic main handoff");
            Require(Directory.GetFiles(output, "*.autonomous.json").Length == 1, "Exactly one main run");
            var main = session.Checkpoint!; var preparation = session.PreparationCheckpoint!;
            var audits = new List<object>(); var i = 0;
            foreach (var seed in main.Plan.Seeds)
            {
                var compiled = TopologyCompiler.Compile(seed, main.Plan.Constraints); var qa = TopologyValidator.Validate(compiled, main.Plan.Targets);
                Require(qa.MeetsTargets, "Only original-qualified seeds handed off");
                var destination = await AutonomousDesignFiles.ExportAsync(new("seed-" + i, null, seed, qa, 0), main.Plan.Constraints, Path.Combine(output, "handoff-" + i++ + ".staropt"));
                var loaded = await StarOptProjectStore.LoadAsync(destination);
                var imported = TopologyCompiler.Import(loaded.Configurations[loaded.ActiveConfigurationIndex].ToSnapshot(), main.Plan.Constraints);
                var readback = TopologyValidator.Validate(imported, main.Plan.Targets); Require(readback.MeetsTargets, "Native handoff readback");
                audits.Add(new { Independent = qa, NativeReadback = readback });
            }
            long reportRays;
            using (var reads = SequentialTraceMeasurement.Begin())
            {
                var study = AutonomousDesignStudy.Capture("SPC automatic preparation and continuation", main, preparation);
                await AutonomousDesignStudyFiles.SaveStudyAsync(study, Path.Combine(output, "product.design-study.json"));
                await AutonomousDesignReport.SaveMarkdownAsync(AutonomousDesignReview.Build(study), Path.Combine(output, "product-report.md")); reportRays = reads.RayCount;
            }
            Require(reportRays == 0, "Report never computes");
            await Write(Path.Combine(output, "completed.json"), new { LoadRays = loadRays, ReportRays = reportRays, PreviousPreparation = before,
                Preparation = preparation.Consumed, Main = main.Consumed, MainStatus = main.Status, Original = preparation.Plan,
                Selected = main.Plan.Seeds.Length, Audits = audits, ExactlyOneHandoff = true, NoGateChanges = true }); return;
        }
        if (mode == "product-reopen")
        {
            long workRays;
            using (var reads = SequentialTraceMeasurement.Begin()) { await session.RunAsync(session.PreparationCheckpoint.Plan, true, _ => { }); workRays = reads.RayCount; }
            Require(workRays == 0 && Directory.GetFiles(output, "*.autonomous.json").Length == 1, "Repeated completed handoff is idempotent");
            using var reports = SequentialTraceMeasurement.Begin();
            var study = await AutonomousDesignStudyFiles.LoadStudyAsync(Path.Combine(output, "product.design-study.json"));
            _ = AutonomousDesignReview.Build(study); Require(reports.RayCount == 0, "Fresh process report readback");
            await Write(Path.Combine(output, "reopened.json"), new { NewProcess = true, LoadRays = loadRays, RepeatedHandoffRays = workRays, ReportRays = reports.RayCount, MainCount = 1 }); return;
        }
        throw new ArgumentException(mode);
    }
    private static void Require(bool condition, string text) { if (!condition) throw new InvalidDataException(text); }
    private static async Task Write(string path, object value) { await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write); await JsonSerializer.SerializeAsync(file, value, Json); }
}

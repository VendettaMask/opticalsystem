using System.Collections.Immutable;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

// A separately charged development experiment. Raising a sampling lower bound
// cannot loosen the original validator's unchanged manufacturing acceptance.
internal static class BackFocusProbe
{
    internal static async Task Run(string inputPath, string output)
    {
        if (Directory.Exists(output)) throw new IOException("Existing evidence.");
        Directory.CreateDirectory(output);
        var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }, WriteIndented = true };
        var bytes = await File.ReadAllBytesAsync(inputPath);
        using var input = JsonDocument.Parse(bytes);
        var plan = input.RootElement.GetProperty("plan").Deserialize<AutonomousSearchPlan>(json)!;
        var preparation = input.RootElement.GetProperty("options").Deserialize<ParaxialSeedSearchOptions>(json)!;
        var acquisition = input.RootElement.GetProperty("acquisition").Deserialize<AutonomousSearchWork>(json)!;
        var descriptor = preparation.Generation ?? throw new ArgumentException("Original generation permission is required.");
        var original = plan.Seeds[0];
        if (original.Elements.Any(e => e.Protected) || descriptor.StopAfterElement != 0 || plan.Options.FixedTopologyOnly == true)
            throw new ArgumentException("This probe requires the original free external-stop layout permission.");
        var margin = Math.Max(.05, plan.Constraints.MinimumAirGapMillimeters);
        var maximum = original.Image.ZMillimeters - original.Stop.ZMillimeters - margin
            - descriptor.Elements.Length * (descriptor.MinimumThicknessMillimeters + margin);
        var minimum = Math.Max(descriptor.MinimumBackFocusMillimeters, plan.Constraints.MinimumBackFocusMillimeters);
        var watch = Stopwatch.StartNew(); var cpuStart = Process.GetCurrentProcess().TotalProcessorTime;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(300) - acquisition.Elapsed);
        using var serial = ComputationParallelism.SuppressNestedParallelism();
        using var rays = SequentialTraceMeasurement.BeginCpuBudget(10_000_000 - acquisition.TracedRays);
        var token = deadline.Token; var functions = 0; var gradients = 0;
        var proposals = new List<object>(); var phases = new List<FixedTopologyResult>();
        DesignTopology? best = null; TopologyAssessment? assessment = null;
        var bestRank = (Loss: double.PositiveInfinity, Quality: double.PositiveInfinity);
        var random = new DeterministicRandom(plan.Options.Seed);
        string? failure = null;
        try
        {
            foreach (var fraction in new[] { .75, .875, .9375 })
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    token.ThrowIfCancellationRequested();
                    var work = new StructureGenerationWork(); var before = functions;
                    var samplingMinimum = minimum + fraction * Math.Max(0, maximum - minimum);
                    try
                    {
                        var candidate = ThickStructureSeedGenerator.Create(plan, descriptor with
                        {
                            MinimumBackFocusMillimeters = samplingMinimum, AnnealingProposals = Math.Min(16, descriptor.AnnealingProposals)
                        }, ref random, Charge, work, token, requireFeasibleGeometry: true,
                            initialShape: StructureCompetitionSearch.InitialShape(StructureRayPathFamily.Balanced, descriptor.Elements.Length));
                        Charge();
                        var measured = TopologyValidator.Validate(TopologyCompiler.Compile(candidate, plan.Constraints), plan.Targets, token);
                        var loss = measured.Completed && !measured.Fields.IsEmpty
                            ? measured.Fields.Max(f => Math.Max(1 - (double)f.ValidRays / f.AttemptedRays,
                                (double)(f.CheckAttemptedRays - f.CheckValidRays) / f.CheckAttemptedRays)) : double.PositiveInfinity;
                        if (candidate.Reference != original.Reference || candidate.Stop != original.Stop || candidate.Image != original.Image)
                            throw new InvalidDataException("Original mechanical frame changed.");
                        var oldFaces = original.Elements.SelectMany(e => new[] { (e.FrontSurfaceId, e.Front), (e.BackSurfaceId, e.Back) })
                            .ToDictionary(f => f.Item1, f => f.Item2);
                        foreach (var (id, face) in candidate.Elements.SelectMany(e => new[] { (e.FrontSurfaceId, e.Front), (e.BackSurfaceId, e.Back) }))
                            if (oldFaces.TryGetValue(id, out var old) && (face.ClearSemiDiameterMillimeters != old.ClearSemiDiameterMillimeters
                                || face.MechanicalSemiDiameterMillimeters != old.MechanicalSemiDiameterMillimeters))
                                throw new InvalidDataException("Original physical aperture changed.");
                        var quality = !measured.Fields.IsEmpty && measured.Fields.All(f => f.RmsRadiusMillimeters is not null && f.MaximumRadiusMillimeters is not null)
                            ? measured.Fields.Max(f => Math.Max(f.RmsRadiusMillimeters!.Value / plan.Targets.MaximumRmsRadiusMillimeters,
                                f.MaximumRadiusMillimeters!.Value / plan.Targets.MaximumSpotRadiusMillimeters)) : double.PositiveInfinity;
                        proposals.Add(new { fraction, attempt, samplingMinimum, functions = functions - before, work.MatrixRequests,
                            work.GeometryProbes, topology = candidate, measured });
                        if (loss < bestRank.Loss || loss == bestRank.Loss && quality < bestRank.Quality)
                        { bestRank = (loss, quality); best = candidate; assessment = measured; }
                        Console.WriteLine($"Back focus {samplingMinimum:F3}, loss={loss:G5}, quality={quality:G5}, qualified={measured.MeetsTargets}");
                    }
                    catch (Exception error) when (error is ArgumentException or ArithmeticException)
                    { proposals.Add(new { fraction, attempt, samplingMinimum, functions = functions - before, error.Message }); }
                }
            if (best is not null && assessment?.MeetsTargets != true)
            {
                var variables = StructureEvolutionSearch.EvolutionVariables(best, plan.Options);
                foreach (var dense in new[] { false, true })
                {
                    var compiled = TopologyCompiler.Compile(best, plan.Constraints);
                    var binding = TopologyParameterBinder.Bind(compiled, variables, exactMechanicalFrame: true);
                    var remainingFunctions = 600 - acquisition.FunctionEvaluations - functions;
                    var remainingGradients = 600 - acquisition.GradientEvaluations - gradients;
                    var time = TimeSpan.FromSeconds(300) - acquisition.Elapsed - watch.Elapsed;
                    if (remainingFunctions < 3 || remainingGradients < 1 || time <= TimeSpan.FromSeconds(5)) break;
                    var budget = Math.Min(dense ? 82 : 102, remainingFunctions);
                    var result = FixedTopologyOptimizer.Run(compiled, binding, plan.Targets, new()
                    {
                        GateCompensationVersion = 2, MaximumUpdates = budget - 2, MaximumFunctionEvaluations = budget,
                        MaximumGradientEvaluations = Math.Min(budget - 1, remainingGradients),
                        TimeLimit = dense ? time : time * .45, ComputeMode = DesignComputeMode.Cpu,
                        Sampling = new() { FieldsPerDirection = 3, PupilRings = dense ? 10 : 2, PupilArms = dense ? 32 : 8,
                            IncludeBoundaryClearance = true, IncludeBoundaryMaximum = true, ProfileRadialSamples = 64 }
                    }, token);
                    functions += result.Work.FunctionEvaluations; gradients += result.Work.GradientEvaluations;
                    phases.Add(result); best = result.Topology; assessment = result.FinalAssessment;
                    if (assessment?.MeetsTargets == true) break;
                }
            }
        }
        catch (OperationCanceledException) { failure = "Global wall deadline reached."; }
        catch (Exception error) when (error is ArgumentException or ArithmeticException or InvalidOperationException)
        { failure = error.Message; }
        await File.WriteAllTextAsync(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new
        {
            DiagnosticOnly = true, inputPath, sourceSha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
            OriginalTargets = plan.Targets, OriginalConstraints = plan.Constraints, OriginalDescriptor = descriptor,
            OriginalPermissions = plan.Options, acquisition, functions, gradients, tracedRays = rays.RayCount,
            elapsed = watch.Elapsed, cpu = Process.GetCurrentProcess().TotalProcessorTime - cpuStart,
            endToEndFunctions = functions + acquisition.FunctionEvaluations,
            endToEndRays = rays.RayCount + acquisition.TracedRays, proposals, phases, endpoint = best, assessment, failure
        }, json));
        if (best is not null && assessment is { Completed: true, MeetsTargets: true })
        {
            // Separate cold replay/export; not fed back into search or its budget.
            var cold = TopologyValidator.Validate(TopologyCompiler.Compile(best, plan.Constraints), plan.Targets);
            if (!cold.Completed || !cold.MeetsTargets) throw new InvalidDataException("Cold original replay failed.");
            var exported = await AutonomousDesignFiles.ExportAsync(new("back-focus", null, best, cold, 0), plan.Constraints,
                Path.Combine(output, "qualified.staropt"));
            await File.WriteAllTextAsync(Path.Combine(output, "cold-export.json"), JsonSerializer.Serialize(new { SearchBudgetInput = false, exported, cold }, json));
        }
        Console.WriteLine($"Back-focus experiment: qualified={assessment?.MeetsTargets}, functions={functions}, rays={rays.RayCount}, {failure}");
        void Charge()
        {
            token.ThrowIfCancellationRequested();
            if (functions >= 200) throw new InvalidOperationException("Bounded construction/screen function allocation exhausted.");
            functions++;
        }
    }
}

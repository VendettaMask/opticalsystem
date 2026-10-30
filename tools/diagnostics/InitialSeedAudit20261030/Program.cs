using System.Security.Cryptography;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;

if (args.Length is < 2 or > 3 || args.Length == 3 && args[2] != "--f4")
    throw new ArgumentException("<repository-root> <new-evidence-directory> [--f4]");
var root = Path.GetFullPath(args[0]); var output = Path.GetFullPath(args[1]);
if (Directory.Exists(output)) throw new IOException("Existing evidence is never overwritten.");
Directory.CreateDirectory(output);
var json = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true,
    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals, Converters = { new JsonStringEnumConverter() } };
var engine = "labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Engine/";
string[] sources = [engine + "ThickStructureSeedGenerator.cs", engine + "FeasibleThickSeedGenerator.cs",
    engine + "ParaxialSeedSearch.cs", engine + "SeedImageQualityScreening.cs", engine + "TopologyEvaluationPlan.cs",
    engine + "TopologyCompiler.cs", engine + "TopologyValidator.cs",
    "src/OpticalSystemDesign.Core/Services/RayEnvelopeApertureSizing.cs",
    "src/OpticalSystemDesign.Core/Raytrace/SequentialRayTracer.ApertureDiagnostics.cs"];
Dictionary<string, string> Hashes() => sources.ToDictionary(p => p, p => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, p)))));
void Save(string name, object value) => File.WriteAllText(Path.Combine(output, name), JsonSerializer.Serialize(value, json));
var hashes = Hashes(); Save("source-sha256.json", hashes);
var frozen = Path.Combine(root, "validation/rechecks/2026-10-29/feasible-thick-v3");
ParaxialSeedSearchResult Read(string name) => JsonSerializer.Deserialize<ParaxialSeedSearchResult>(File.ReadAllText(Path.Combine(frozen, name + ".json")), json)!;
var comparisons = new List<object>();
foreach (var count in new[] { 3, 8 })
{
    var narrow = Read($"feasible-{count}-8-5-42"); var wide = Read($"feasible-{count}-8-12.5-42");
    comparisons.Add(new { Elements = count, IdenticalLensCoordinatesAndProfiles = narrow.Seeds.Zip(wide.Seeds).All(pair =>
        ContentFingerprint.Compute(pair.First.Elements) == ContentFingerprint.Compute(pair.Second.Elements)),
        NarrowCount = narrow.Seeds.Length, WideCount = wide.Seeds.Length, NarrowHalfField = 5, WideHalfField = 12.5 });
}
Save("field-sensitivity.json", comparisons);
var results = new List<object>();
foreach (var name in args.Length == 3 ? ["feasible-8-4-12.5-42"]
    : new[] { "feasible-8-8-12.5-42", "feasible-8-8-5-42", "feasible-3-8-5-42", "feasible-8-4-12.5-42" })
{
    var preparation = Read(name); var plan = preparation.SourcePlan;
    var count = name == "feasible-8-8-12.5-42" ? preparation.Seeds.Length : 1;
    for (var candidate = 0; candidate < count; candidate++)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40)); var token = deadline.Token;
        using var rays = SequentialTraceMeasurement.BeginCpuBudget(500_000);
        using var cpu = new SequentialGpuSession(DesignComputeMode.Cpu, token);
        var topology = preparation.Seeds[candidate]; var source = TopologyCompiler.Compile(topology, plan.Constraints);
        var optic = source.CreateOptic();
        var defaultScreen = SeedImageQualityScreening.Evaluate(source, plan.Targets, new(), token);
        var generousScreen = SeedImageQualityScreening.Evaluate(source, plan.Targets,
            new() { FieldsPerDirection = 5, MaximumTracedRaysPerCandidate = 25_000 }, token);
        var physical = TopologyValidator.Validate(source, plan.Targets, token);
        var diagnostics = new Dictionary<string, int>();
        var unclipped = source.CreateOptic();
        foreach (var map in source.Elements)
            foreach (var index in new[] { map.FrontIndex, map.BackIndex })
            {
                unclipped.SurfaceGroup.Items[index].PhysicalAperture = null;
                unclipped.SurfaceGroup.Items[index].SemiDiameterDefinesPhysicalAperture = false;
            }
        unclipped.InvalidateRayTraceCache();
        var pupils = ApertureSampler.GenerateGaussianQuadrature(2, 8).Concat(TopologyEvaluationPlan.Checks()).ToArray();
        foreach (var wave in optic.Wavelengths.Where(w => w.Weight > 0))
        {
            var bundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(0, 1, wave.Micrometers,
                pupils, aimAtStop: optic.RayAimingEnabled, applyVignettingFactors: false);
            using var nominal = optic.SequentialRayTracer.Trace(bundle, TraceRequest.FullHistory());
            using var openTrace = unclipped.SequentialRayTracer.Trace(bundle, TraceRequest.FullHistory());
            for (var ray = 0; ray < bundle.Rays.Count; ray++)
            {
                var continuation = optic.SequentialRayTracer.DiagnoseCircularApertures(bundle.Rays[ray], token);
                var first = optic.SequentialRayTracer.DiagnoseApertures(bundle.Rays[ray], token);
                var reached = continuation.UnclippedImage is not null;
                var key = !bundle.Rays[ray].IsAlive ? "invalid-launch-or-aim" : reached
                    ? continuation.MinimumClearanceMillimeters < 0 ? "circular-aperture-clipping" : "unclipped-complete"
                    : "propagation-failure-requires-interaction-diagnosis";
                diagnostics[key] = diagnostics.GetValueOrDefault(key) + 1;
                for (var surfaceIndex = 0; surfaceIndex < unclipped.SurfaceGroup.Items.Count; surfaceIndex++)
                {
                    if (!openTrace.TryGetSample(ray, surfaceIndex, out var sample)) break;
                    if (sample.InteractionKind == OpticalSystemDesign.Core.Interactions.RayInteractionKind.TotalInternalReflection)
                    {
                        var reason = "unclipped-first-TIR:" + source.SurfaceIds.GetValueOrDefault(surfaceIndex, surfaceIndex.ToString());
                        diagnostics[reason] = diagnostics.GetValueOrDefault(reason) + 1;
                        break;
                    }
                    if (!sample.Vignetted && sample.Intensity > 0) continue;
                    var failure = $"unclipped-first-invalid:{source.SurfaceIds.GetValueOrDefault(surfaceIndex, surfaceIndex.ToString())}:{sample.InteractionKind?.ToString() ?? "no-interaction"}";
                    diagnostics[failure] = diagnostics.GetValueOrDefault(failure) + 1;
                    break;
                }
                if (nominal.TryGetSample(ray, optic.SurfaceGroup.Items.Count - 1, out var final)
                    && !final.Vignetted && final.Intensity > 0)
                    diagnostics["nominal-physically-complete"] = diagnostics.GetValueOrDefault("nominal-physically-complete") + 1;
                else
                {
                    for (var surfaceIndex = 0; surfaceIndex < optic.SurfaceGroup.Items.Count; surfaceIndex++)
                    {
                        if (!nominal.TryGetSample(ray, surfaceIndex, out var sample)) break;
                        if (!sample.Vignetted && sample.Intensity > 0) continue;
                        var reason = $"nominal-first-invalid:{source.SurfaceIds.GetValueOrDefault(surfaceIndex, surfaceIndex.ToString())}:{sample.InteractionKind?.ToString() ?? "no-interaction"}";
                        diagnostics[reason] = diagnostics.GetValueOrDefault(reason) + 1;
                        break;
                    }
                }
                if (first.FirstBlockedSurfaceIndex is { } surface)
                {
                    var label = "first-blocked:" + source.SurfaceIds.GetValueOrDefault(surface, surface.ToString());
                    diagnostics[label] = diagnostics.GetValueOrDefault(label) + 1;
                }
            }
        }
        var groups = source.Elements.Select(e => (IReadOnlyList<int>)new[] { e.FrontIndex, e.BackIndex }).ToArray();
        var envelope = AutomaticSemiDiameterSolver.UpdateFromRayEnvelope(optic, groups,
            [(0, 0), (0, .25), (0, .5), (0, .75), (0, 1)], pupils,
            marginFactor: 1.01, cancellationToken: token, preserveStopAperture: true);
        var failures = new List<object>();
        object? expandedPhysical = null;
        if (envelope.Applied)
        {
            var sizes = envelope.Surfaces.ToDictionary(s => s.SurfaceIndex);
            var expanded = topology with { Elements = topology.Elements.Select(e =>
            {
                var map = source.Elements.Single(m => m.ElementId == e.Id);
                return e with
                {
                    Front = e.Front with { ClearSemiDiameterMillimeters = sizes[map.FrontIndex].SemiDiameterMillimeters,
                        MechanicalSemiDiameterMillimeters = sizes[map.FrontIndex].MechanicalSemiDiameterMillimeters },
                    Back = e.Back with { ClearSemiDiameterMillimeters = sizes[map.BackIndex].SemiDiameterMillimeters,
                        MechanicalSemiDiameterMillimeters = sizes[map.BackIndex].MechanicalSemiDiameterMillimeters }
                };
            }).ToImmutableArray() };
            var compiled = TopologyCompiler.Compile(expanded, plan.Constraints);
            foreach (var row in TopologyEvaluationPlan.Geometry(compiled, plan.Targets, 64))
            {
                double? value;
                try { value = TopologyEvaluationPlan.FormalMeasure(optic, row); }
                catch (Exception error) when (TopologyEvaluationPlan.DomainError(error)) { value = null; }
                if (value is null || !double.IsFinite(value.Value) || (row.Upper ? value > row.Limit : value < row.Limit))
                    failures.Add(new { row.Id, Value = value, row.Limit, row.Upper });
            }
            expandedPhysical = TopologyValidator.Validate(compiled, plan.Targets, token);
        }
        var result = new { Case = name, Candidate = candidate, ShapeFingerprint = ContentFingerprint.Compute(topology.Elements),
            Scope = "Read-only C#/Core audit; expanded apertures are diagnostic counterfactuals, never accepted prescriptions",
            DefaultScreen = defaultScreen, GenerousFiveFieldScreen = generousScreen, OriginalPhysical = physical,
            EdgeDiagnostics = diagnostics, Envelope = envelope, ExpandedGeometryFailures = failures,
            ExpandedPhysical = expandedPhysical, AdditionalTracedRays = rays.RayCount };
        Save(name + "-" + candidate + ".json", result); results.Add(result);
        Console.WriteLine($"{name}/{candidate}: physical={physical.MeetsTargets}, default-screen={defaultScreen.Outcome}, "
            + $"envelope={envelope.Applied}, expanded-geometry-failures={failures.Count}, rays={rays.RayCount}");
    }
}
Save("results.json", results);
Save("completion.json", new { Complete = true, SourceUnchanged = hashes.OrderBy(p => p.Key).SequenceEqual(Hashes().OrderBy(p => p.Key)) });

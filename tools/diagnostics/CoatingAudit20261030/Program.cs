using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpticalSystemDesign.CoatingDesign.Engine;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Materials;

if (args[0] == "deep") { await CoatingDeepAudit.Run(args.Skip(1).ToArray()); return; }
if (args[0] == "competition") { await CompetitionAudit.Run(args.Skip(1).ToArray()); return; }
if (args[0] == "general-matrix") { await GeneralAudit.Run(args.Skip(1).ToArray()); return; }
if (args[0] == "spectrum-edges") { await SpectrumEdgeAudit.Run(args.Skip(1).ToArray()); return; }
if (args[0] == "quality-freeze") { await ReflectanceQualityAudit.Freeze(args[1]); return; }
if (args[0] == "quality-run") { await ReflectanceQualityAudit.Run(args[1], args[2]); return; }
if (args[0] == "quality-resume") { await ReflectanceQualityAudit.Run(args[1], args[2], resume: true); return; }
if (args[0] == "infeasible-export") { await InfeasibleRepairAudit.Export(args[1], args[2]); return; }
if (args[0] == "infeasible-reference") { await InfeasibleRepairAudit.ValidateReference(args[1], args[2], args[3]); return; }
if (args[0] == "target-freeze") { await TargetProtectionAudit.Freeze(args[1]); return; }
if (args[0] == "target-scale") { await TargetProtectionAudit.Scale(args[1]); return; }
if (args[0] == "protected-probe") { await BalancedTargetAudit.Probe(args[1], args[2]); return; }
if (args[0] == "balanced-freeze") { await BalancedTargetAudit.Freeze(args[1]); return; }
if (args[0] == "continuation-freeze") { await ContinuationAudit.Freeze(args[1]); return; }
if (args[0] == "allocation-recheck") { await AllocationAudit.Recheck(args[1], args[2], args[3]); return; }
if (args[0] == "allocation-physics") { await AllocationAudit.ExportPhysics(args[1], args[2]); return; }
if (args[0] == "margin-freeze") { await MarginAudit.Freeze(args[1], args[2]); return; }
if (args[0] == "sampling-trace") { await AllocationAudit.SamplingTrace(args[1], args[2]); return; }
if (args[0] == "legacy-repair-guide") { await RepairGuideAudit.Run(args[1], args[2], args[3]); return; }

var protocol = args[0]; var directory = Path.GetFullPath(args[1]); Directory.CreateDirectory(directory);
var summaries = new List<object>(); var referenceCases = new List<object>();
var service = new DesignService();
foreach (var (name, kind, cavities) in new[] { ("AR", DesignKind.Antireflection, 1), ("HR", DesignKind.HighReflector, 1),
    ("NBF1", DesignKind.NarrowBand, 1), ("NBF2", DesignKind.NarrowBand, 2) })
{
    if (args.Length > 3 && args[3] != name) continue;
    var example = Examples.Create(kind);
    example = example with
    {
        Settings = example.Settings with { MaximumIterations = 24, Search = new(4, 12, true) },
        Target = example.Target with { Structure = cavities == 1 ? null : new(cavities) }
    };
    var json = JsonNode.Parse(JsonSerializer.Serialize(example, Experiment.JsonOptions))!;
    if (protocol != "default") json["Settings"]!["DesignProtocolVersion"] = protocol == "legacy" ? null : JsonValue.Create(2);
    var input = json.Deserialize<Experiment>(Experiment.JsonOptions)!;
    using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
    var watch = Stopwatch.StartNew();
    var searching = args.Length > 2 && args[2] == "search";
    var generated = searching ? input : service.Generate(input, token: deadline.Token);
    var optimized = searching ? service.Search(input, token: deadline.Token) : service.Optimize(generated, token: deadline.Token);
    await ExperimentStore.SaveAsync(optimized, Path.Combine(directory, name + ".coating.json"));
    var result = optimized.Result!;
    summaries.Add(new
    {
        Name = name,
        Protocol = protocol,
        Workflow = searching ? "Search" : "Generate+Optimize",
        Input = input,
        Layers = optimized.Layers.Length,
        GeneratedLayers = generated.Layers.Length,
        GeneratedMerit = generated.Result?.Merit,
        GenerationRun = generated.Result?.Run,
        result.Merit,
        result.Metrics,
        result.Checks,
        result.Passed,
        result.SamplingConverged,
        result.Run,
        Seconds = watch.Elapsed.TotalSeconds
    });
    var materials = optimized.Materials.ToDictionary(m => m.Id, m => m.Resolve(new MaterialRegistry()));
    var solver = new CoherentThinFilmSolver(materials[optimized.IncidentId], materials[optimized.SubstrateId],
        optimized.Layers.Select(l => new CoherentFilm(materials[l.MaterialId], l.ThicknessNm)));
    foreach (var point in result.Spectrum.Where((_, i) => i % Math.Max(1, result.Spectrum.Length / 12) == 0))
        foreach (var pol in new[] { ThinFilmPolarization.S, ThinFilmPolarization.P, ThinFilmPolarization.Unpolarized })
        {
            var wave = point.WavelengthNanometers;
            double[] Index(string id) => [materials[id].RefractiveIndex(wave), materials[id].ExtinctionCoefficient(wave)];
            referenceCases.Add(new
            {
                Name = name,
                Lambda = wave,
                Angle = input.Target.AngleDegrees,
                Pol = pol.ToString(),
                N0 = Index(optimized.IncidentId),
                Ns = Index(optimized.SubstrateId),
                Layers = optimized.Layers.Select(l => new { N = Index(l.MaterialId), D = l.ThicknessNm }),
                Core = solver.Evaluate(wave, input.Target.AngleDegrees, pol)
            });
        }
    Console.WriteLine($"{protocol} {name}: {optimized.Layers.Length} layers, passed={result.Passed}, peak={result.Metrics.PeakTransmittance:G6}, FWHM={result.Metrics.FwhmNanometers:G6}, {watch.Elapsed.TotalSeconds:F2}s");
}
await File.WriteAllTextAsync(Path.Combine(directory, "summary.json"), JsonSerializer.Serialize(summaries, Experiment.JsonOptions));
await File.WriteAllTextAsync(Path.Combine(directory, "core-cases.json"), JsonSerializer.Serialize(referenceCases, Experiment.JsonOptions));

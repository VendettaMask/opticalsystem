using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using OpticalSystemDesign.CoatingDesign.Engine;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Optimization;
using OpticalSystemDesign.Core.Propagation;

internal static class CoatingDeepAudit
{
    internal static async Task Run(string[] args)
    {
        var directory = Path.GetFullPath(args[0]); Directory.CreateDirectory(directory);
        var sourceDirectory = Path.GetFullPath("validation/rechecks/2026-10-30/coating-reference-review/defaults");
        var sources = new Dictionary<string, Experiment>();
        foreach (var name in new[] { "HR", "NBF1", "NBF2" })
            sources[name] = await ExperimentStore.OpenAsync(Path.Combine(sourceDirectory, name + ".coating.json"));
        if (args.Length > 1 && args[1] == "validate-reference")
        {
            using var referenceJson = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(args[2], "reference-results.json")));
            using var rtaJson = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(args[2], "reference-rta.json")));
            var validated = new List<object>(); var solvers = new Dictionary<string, CoherentThinFilmSolver>();
            foreach (var candidate in referenceJson.RootElement.GetProperty("candidates").EnumerateArray())
            {
                var source = sources[candidate.GetProperty("Source").GetString()!]; var name = candidate.GetProperty("Name").GetString()!;
                var layers = candidate.GetProperty("Layers").Deserialize<FilmLayer[]>(Experiment.JsonOptions)!;
                var lossless = candidate.GetProperty("Lossless").GetBoolean(); var embedded = candidate.GetProperty("Embedded").GetBoolean();
                var materials = source.Materials.ToDictionary(m => m.Id, m => m.Resolve(new MaterialRegistry()));
                CoherentThinFilmSolver Solver(bool idealized, bool inside)
                {
                    IMaterial Media(string id) => idealized ? new WithoutFilmAbsorption(materials[id]) : materials[id];
                    return new(Media(inside ? source.SubstrateId : source.IncidentId), Media(source.SubstrateId),
                        layers.Select(l => new CoherentFilm(Media(l.MaterialId), l.ThicknessNm)));
                }
                var intended = Solver(lossless, embedded); solvers[name] = intended;
                var grid = ThinFilmSpectrum.Grid(source.Target.MinimumNm, source.Target.MaximumNm, source.Target.Kind == DesignKind.HighReflector ? 1200 : 5600)
                    .Append(source.Target.CenterNm).Distinct().Order().ToArray();
                object Screen(CoherentThinFilmSolver solver)
                {
                    var samples = ThinFilmSpectrum.Sample(solver, grid, 0, source.Target.Polarization);
                    var metrics = ThinFilmSpectrum.Measure(source.Target.Kind == DesignKind.HighReflector ? samples
                        : samples.Where(s => Math.Abs(s.WavelengthNanometers - source.Target.CenterNm) <= 2 * source.Target.FwhmNm).ToArray());
                    return new
                    {
                        Metrics = metrics,
                        CenterPower = solver.Evaluate(550, 0, source.Target.Polarization),
                        MinimumOd = (source.Target.StopBands ?? []).Select(b => samples.Where(s => s.WavelengthNanometers >= b.MinimumNm && s.WavelengthNanometers <= b.MaximumNm).Min(s => s.Power.OpticalDensity))
                    };
                }
                TopologyFreeAssessment? actual = null;
                if (!lossless && !embedded)
                {
                    var original = new DesignService().Evaluate(source with { Layers = layers, Result = null, Candidates = null, Tolerance = null });
                    actual = new(original.Result!.Passed, original.Result.SamplingConverged, original.Result.Metrics, original.Result.Checks);
                    await ExperimentStore.SaveAsync(original, Path.Combine(directory, name + ".coating.json"));
                }
                validated.Add(new
                {
                    Name = name,
                    Source = candidate.GetProperty("Source").GetString(),
                    Counterfactual = lossless || embedded,
                    LayerCount = layers.Length,
                    WithinOriginalLayerBudget = layers.Length <= source.Target.MaximumLayers,
                    WithinOriginalThicknessBounds = layers.All(l => l.ThicknessNm >= source.Settings.MinimumThicknessNm && l.ThicknessNm <= source.Settings.MaximumThicknessNm),
                    IntendedScreen = Screen(intended),
                    OriginalAirAndRealMaterialsScreen = Screen(Solver(false, false)),
                    OriginalAssessment = actual
                });
            }
            var errors = new List<object>(); double maxR = 0, maxT = 0, maxA = 0;
            foreach (var point in rtaJson.RootElement.EnumerateArray())
            {
                var name = point.GetProperty("Name").GetString()!; var wave = point.GetProperty("Lambda").GetDouble();
                var core = solvers[name].Evaluate(wave, 0, ThinFilmPolarization.S); var expected = point.GetProperty("Reference");
                var r = Math.Abs(core.Reflectance - expected.GetProperty("R").GetDouble());
                var t = Math.Abs(core.Transmittance - expected.GetProperty("T").GetDouble());
                var a = Math.Abs(core.Absorptance - expected.GetProperty("A").GetDouble());
                maxR = Math.Max(maxR, r); maxT = Math.Max(maxT, t); maxA = Math.Max(maxA, a);
                errors.Add(new { Name = name, Wave = wave, R = r, T = t, A = a });
            }
            await Write(Path.Combine(directory, "reference-candidates-native.json"), validated);
            await Write(Path.Combine(directory, "reference-candidates-rta-check.json"), new { Cases = errors.Count, MaximumR = maxR, MaximumT = maxT, MaximumA = maxA, Errors = errors });
            if (maxR > 2e-11 || maxT > 2e-11 || maxA > 2e-11) throw new ArithmeticException("Independent reference geometry does not agree with native Core.");
            Console.WriteLine($"Validated {validated.Count} reference geometries; R/T/A maximum errors {maxR:G6}, {maxT:G6}, {maxA:G6}"); return;
        }
        if (args.Length > 1 && args[1] == "matched-hr")
        {
            var input = sources["HR"]; var materials = input.Materials.ToDictionary(m => m.Id, m => m.Resolve(new MaterialRegistry()));
            var values = input.Layers.Select(l => l.ThicknessNm).ToArray(); var grid = ThinFilmSpectrum.Grid(520, 580, 120);
            var problem = new OptimizationProblem(); problem.DisableBatching();
            for (var i = 0; i < values.Length; i++)
            { var index = i; problem.AddVariable(new DelegateVariable("thickness-" + i, () => values[index], v => values[index] = v, 2, 2000, stepHint: .01)); }
            double[]? cached = null, residuals = null;
            double[] Evaluate()
            {
                if (cached is not null && cached.SequenceEqual(values)) return residuals!;
                var solver = new CoherentThinFilmSolver(materials[input.IncidentId], materials[input.SubstrateId], input.Layers.Select((l, i) => new CoherentFilm(materials[l.MaterialId], values[i])));
                residuals = grid.Select(w => solver.Evaluate(w, 0, ThinFilmPolarization.Unpolarized).Reflectance - 1).ToArray();
                cached = values.ToArray(); return residuals;
            }
            for (var i = 0; i < grid.Length; i++) { var index = i; problem.AddOperand(new Operand("R-1 at " + grid[i], 0, 1, () => Evaluate()[index])); }
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            using var cancellation = OpticalSystemDesign.Core.Services.ComputationCancellation.Push(timeout.Token);
            var watch = Stopwatch.StartNew(); var report = OptimizerCatalog.Create("Damped Least Squares").Optimize(problem, 80);
            var candidate = new DesignService().Evaluate(input with { Layers = input.Layers.Select((l, i) => l with { ThicknessNm = values[i] }).ToArray(), Result = null, Candidates = null });
            await ExperimentStore.SaveAsync(candidate, Path.Combine(directory, "matched-native-HR.coating.json"));
            await Write(Path.Combine(directory, "matched-native-HR-summary.json"), new
            {
                Scope = "Diagnostic matched 121-point R=1 squared objective, not the product v2 objective",
                SourceFingerprint = input.Fingerprint(),
                Report = report,
                OriginalTargetAssessment = candidate.Result,
                Layers = candidate.Layers,
                Seconds = watch.Elapsed.TotalSeconds
            });
            Console.WriteLine($"Matched native HR: Rmin={candidate.Result!.Metrics.MinimumReflectance:G10}, {report.StopReason}"); return;
        }
        if (args.Length > 1 && args[1].StartsWith("refine-", StringComparison.Ordinal))
        {
            var name = args[1][7..]; var input = sources[name];
            var candidate = input with
            {
                Result = null,
                Candidates = null,
                Tolerance = null,
                Settings = input.Settings with { MaximumIterations = 80 }
            };
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var watch = Stopwatch.StartNew(); DesignProgress? lastReported = null;
            try
            {
                var output = new DesignService().Optimize(candidate,
                    new Immediate(p => lastReported = p), deadline.Token);
                await ExperimentStore.SaveAsync(output, Path.Combine(directory, name + ".coating.json"));
                await Write(Path.Combine(directory, name + "-summary.json"), new
                {
                    Scope = "Additional bounded 80-iteration native optimization from previous endpoint",
                    SourceFingerprint = input.Fingerprint(),
                    Before = input.Result,
                    After = output.Result,
                    Layers = output.Layers,
                    Seconds = watch.Elapsed.TotalSeconds
                });
                Console.WriteLine($"{name} reopt80: {output.Result!.Run.StopReason}, Rmin={output.Result.Metrics.MinimumReflectance:G8}, Tpeak={output.Result.Metrics.PeakTransmittance:G8}, width={output.Result.Metrics.FwhmNanometers:G8}, passed={output.Result.Passed}");
            }
            catch (OperationCanceledException)
            {
                await Write(Path.Combine(directory, name + "-summary.json"), new
                {
                    Scope = "Interrupted extra optimization, no endpoint or qualification claim",
                    SourceFingerprint = input.Fingerprint(),
                    Seconds = watch.Elapsed.TotalSeconds,
                    LastReportedProgress = lastReported
                });
                Console.WriteLine(name + " reopt80 incomplete");
            }
            return;
        }
        var rows = new List<object>(); var reference = new List<object>(); var referenceInputs = new List<object>();
        foreach (var (name, source) in sources)
        {
            var materials = source.Materials.ToDictionary(m => m.Id, m => m.Resolve(new MaterialRegistry()));
            var grid = ThinFilmSpectrum.Grid(source.Target.MinimumNm, source.Target.MaximumNm, name == "HR" ? 600 : 1400)
                .Append(source.Target.CenterNm).Distinct().Order().ToArray();
            var lookupGrid = grid.Concat(Enumerable.Range(-8, 17).Select(i => 550 + i * 15 / 16.0))
                .Concat(new[] { 470d, 502, 506, 510, 550, 590, 594, 598, 630 }).Distinct().Order().ToArray();
            referenceInputs.Add(new
            {
                Name = name,
                SourceFingerprint = source.Fingerprint(),
                source.Target,
                source.Settings,
                source.IncidentId,
                source.SubstrateId,
                source.LowId,
                source.HighId,
                source.Layers,
                Materials = source.Materials.Select(m => new
                {
                    m.Id,
                    m.Name,
                    CenterN = materials[m.Id].RefractiveIndex(550),
                    CenterK = materials[m.Id].ExtinctionCoefficient(550),
                    Values = lookupGrid.Select(w => new[] { w,
                        materials[m.Id].RefractiveIndex(w), materials[m.Id].ExtinctionCoefficient(w) }).ToArray()
                })
            });
            foreach (var loss in new[] { "real", "no-high-k", "no-low-k", "no-film-k" })
                Add(name + ":endpoint", source.Layers, loss, false);
            if (name != "HR")
            {
                foreach (var loss in new[] { "real", "no-film-k" }) Add(name + ":endpoint-embedded", source.Layers, loss, true);
                var cavities = name == "NBF1" ? 1 : 2;
                for (var pairs = 1; pairs <= 9; pairs++)
                    foreach (var order in new[] { 1, 2, 3 })
                    {
                        var mirrors = Enumerable.Range(0, 2 * pairs + 1).Select(i => Layer(i % 2 == 0 ? source.HighId : source.LowId)).ToArray();
                        var layers = new List<FilmLayer>(mirrors);
                        for (var cavity = 0; cavity < cavities; cavity++) { layers.Add(Layer(source.LowId, 2 * order)); layers.AddRange(mirrors); }
                        if (layers.Count > source.Target.MaximumLayers || layers.Any(l => l.ThicknessNm > source.Settings.MaximumThicknessNm)) continue;
                        foreach (var loss in new[] { "real", "no-film-k" }) Add($"{name}:pairs{pairs}:order{order}", layers.ToArray(), loss, false);
                    }
            }
            else
            {
                foreach (var pairs in new[] { 4, 8, 12, 15, 16, 24, 32, 48, 64 })
                    foreach (var reverse in new[] { false, true })
                    {
                        var layers = Enumerable.Range(0, pairs * 2).Select(i => Layer((i % 2 == 0) != reverse ? source.HighId : source.LowId)).ToArray();
                        foreach (var loss in new[] { "real", "no-film-k" }) Add($"HR:quarter:pairs{pairs}:reverse{reverse}", layers, loss, false);
                        if (pairs <= 15)
                            foreach (var cap in new[] { .5, 1.0, 2.0 })
                                Add($"HR:lowcap{cap}:pairs{pairs}:reverse{reverse}", new[] { Layer(source.LowId, cap) }.Concat(layers).ToArray(), "real", false);
                    }
            }
            if (name != "HR")
            {
                var flags = BindingFlags.NonPublic | BindingFlags.Static;
                var objectiveGrid = (double[])typeof(DesignService).GetMethod("ObjectiveGrid", flags)!.Invoke(null, [source])!;
                var solver = new CoherentThinFilmSolver(materials[source.IncidentId], materials[source.SubstrateId],
                    source.Layers.Select(l => new CoherentFilm(materials[l.MaterialId], l.ThicknessNm)));
                var residuals = (double[])typeof(DesignService).GetMethod("Objective", flags)!.Invoke(null, [source, solver, objectiveGrid])!;
                var groups = new List<(string Name, double Value)> { ("center-minimum", residuals[0]), ("left-half-height", residuals[1]), ("right-half-height", residuals[2]) };
                var index = 3;
                foreach (var wave in objectiveGrid)
                {
                    var bandIndex = Array.FindIndex(source.Target.StopBands!, b => wave >= b.MinimumNm && wave <= b.MaximumNm);
                    if (bandIndex >= 0) groups.Add(("stopband-" + bandIndex, residuals[index++]));
                    else if (Math.Abs(wave - source.Target.CenterNm) < source.Target.FwhmNm / 4) groups.Add(("center-shape", residuals[index++]));
                }
                if (index != residuals.Length) throw new InvalidOperationException("Legacy residual layout changed; audit must not silently reinterpret it.");
                await Write(Path.Combine(directory, name + "-objective-groups.json"), new
                {
                    Scope = "Actual current private objective rows, no alternative optical model",
                    SourceFingerprint = source.Fingerprint(),
                    Rows = residuals.Length,
                    MeanSquares = residuals.Sum(v => v * v) / residuals.Length,
                    Groups = groups.GroupBy(g => g.Name).Select(g => new
                    {
                        Name = g.Key,
                        Count = g.Count(),
                        SumSquares = g.Sum(x => x.Value * x.Value),
                        FractionOfTotal = g.Sum(x => x.Value * x.Value) / residuals.Sum(v => v * v)
                    })
                });
            }
            await Write(Path.Combine(directory, "sweeps.json"), rows);
            await Write(Path.Combine(directory, "reference-input.json"), referenceInputs);

            FilmLayer Layer(string id, double quarters = 1) => new(id, quarters * CoherentThinFilmSolver.QuarterWaveThickness(
                materials[id], materials[source.IncidentId], source.Target.CenterNm, source.Target.AngleDegrees));

            void Add(string label, FilmLayer[] layers, string loss, bool embedded)
            {
                IMaterial Film(string id) => loss == "no-film-k" || loss == "no-high-k" && id == source.HighId || loss == "no-low-k" && id == source.LowId
                    ? new WithoutFilmAbsorption(materials[id]) : materials[id];
                IMaterial incident = embedded ? new WithoutFilmAbsorption(materials[source.SubstrateId]) : materials[source.IncidentId];
                IMaterial substrate = embedded ? new WithoutFilmAbsorption(materials[source.SubstrateId]) : materials[source.SubstrateId];
                var solver = new CoherentThinFilmSolver(incident, substrate, layers.Select(l => new CoherentFilm(Film(l.MaterialId), l.ThicknessNm)));
                var samples = ThinFilmSpectrum.Sample(solver, grid, source.Target.AngleDegrees, source.Target.Polarization);
                var metrics = ThinFilmSpectrum.Measure(name == "HR" ? samples
                    : samples.Where(s => Math.Abs(s.WavelengthNanometers - source.Target.CenterNm) <= 2 * source.Target.FwhmNm).ToArray());
                var worst = samples.MinBy(s => s.Power.Reflectance)!;
                var center = solver.Evaluate(source.Target.CenterNm, source.Target.AngleDegrees, source.Target.Polarization);
                rows.Add(new
                {
                    Name = name,
                    Label = label,
                    Loss = loss,
                    Embedded = embedded,
                    ExteriorModel = embedded ? "Counterfactual: substrate dispersion on both sides, exterior k=0 for a valid transparent incident medium" : "Original incident and substrate",
                    Counterfactual = loss != "real" || embedded,
                    ScreenOnly = true,
                    LayerCount = layers.Length,
                    WithinOriginalLayerBudget = layers.Length <= source.Target.MaximumLayers,
                    WithinOriginalThicknessBounds = layers.All(l => l.ThicknessNm >= source.Settings.MinimumThicknessNm && l.ThicknessNm <= source.Settings.MaximumThicknessNm),
                    Metrics = metrics,
                    CenterPower = center,
                    WorstReflectanceWavelength = worst.WavelengthNanometers,
                    WorstReflectancePower = worst.Power,
                    BlockingOd = (source.Target.StopBands ?? []).Select(b => new
                    {
                        Band = b,
                        MinimumOd = samples.Where(s => s.WavelengthNanometers >= b.MinimumNm && s.WavelengthNanometers <= b.MaximumNm).Min(s => s.Power.OpticalDensity)
                    })
                });
                foreach (var sample in samples.Where((_, i) => i % Math.Max(1, samples.Length / 5) == 0))
                    reference.Add(new
                    {
                        Name = label + ":" + loss + ":embedded" + embedded,
                        Lambda = sample.WavelengthNanometers,
                        Angle = source.Target.AngleDegrees,
                        Pol = source.Target.Polarization.ToString(),
                        N0 = new[] { incident.RefractiveIndex(sample.WavelengthNanometers), incident.ExtinctionCoefficient(sample.WavelengthNanometers) },
                        Ns = new[] { substrate.RefractiveIndex(sample.WavelengthNanometers), substrate.ExtinctionCoefficient(sample.WavelengthNanometers) },
                        Layers = layers.Select(l => new { N = new[] { Film(l.MaterialId).RefractiveIndex(sample.WavelengthNanometers), Film(l.MaterialId).ExtinctionCoefficient(sample.WavelengthNanometers) }, D = l.ThicknessNm }),
                        Core = sample.Power
                    });
            }
        }
        await Write(Path.Combine(directory, "sweeps.json"), rows);
        await Write(Path.Combine(directory, "reference-input.json"), referenceInputs);
        await Write(Path.Combine(directory, "core-cases.json"), reference);
        Console.WriteLine($"Deep audit: {rows.Count} physical screens, {reference.Count} independent reference points; no acceptance or product-input changes.");
    }

    private static Task Write(string path, object value) => File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, Experiment.JsonOptions));
    private sealed record TopologyFreeAssessment(bool Passed, bool SamplingConverged, ThinFilmBandMetrics Metrics, RequirementCheck[] Checks);
    private sealed class Immediate(Action<DesignProgress> callback) : IProgress<DesignProgress>
    { public void Report(DesignProgress value) => callback(value); }
    private sealed class WithoutFilmAbsorption(IMaterial original) : IMaterial
    {
        public string Name => original.Name + " [diagnostic k=0 counterfactual]";
        public IPropagationModel PropagationModel => original.PropagationModel;
        public double RefractiveIndex(double wavelengthNanometers) => original.RefractiveIndex(wavelengthNanometers);
        public double ExtinctionCoefficient(double wavelengthNanometers) => 0;
        public IMaterial Clone() => new WithoutFilmAbsorption(original.Clone());
    }
}

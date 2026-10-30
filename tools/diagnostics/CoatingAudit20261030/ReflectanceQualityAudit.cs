using System.Diagnostics;
using System.Text.Json;
using OpticalSystemDesign.CoatingDesign.Engine;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Materials;

internal static class ReflectanceQualityAudit
{
    internal static async Task Freeze(string directory)
    {
        Directory.CreateDirectory(directory);
        var specs = new[]
        {
            ("ar-blue",450d,1.38,2.3,0d,0d,ThinFilmPolarization.S,DesignKind.Antireflection,.05,.1),
            ("ar-nearir",800d,1.32,2.15,.002,20d,ThinFilmPolarization.P,DesignKind.Antireflection,.05,.1),
            ("ar-ir",1300d,1.4,2.25,.008,35d,ThinFilmPolarization.Unpolarized,DesignKind.Antireflection,.05,.1),
            ("hr-green",520d,1.4,2.3,0d,0d,ThinFilmPolarization.S,DesignKind.HighReflector,.05,.8),
            ("hr-nearir",940d,1.45,2.15,.002,35d,ThinFilmPolarization.P,DesignKind.HighReflector,.05,.8),
            ("hr-ir",1550d,1.4,2.25,.01,25d,ThinFilmPolarization.Unpolarized,DesignKind.HighReflector,.05,.8),
            ("heldout-ar-red",610d,1.43,2.1,.004,12d,ThinFilmPolarization.P,DesignKind.Antireflection,.12,.08),
            ("heldout-ar-oblique",1064d,1.46,2.2,.012,45d,ThinFilmPolarization.S,DesignKind.Antireflection,.03,.15),
            ("heldout-ar-wide",700d,1.25,1.95,.02,30d,ThinFilmPolarization.Unpolarized,DesignKind.Antireflection,.2,.04),
            ("heldout-hr-red",633d,1.52,2.05,.004,10d,ThinFilmPolarization.S,DesignKind.HighReflector,.08,.75),
            ("heldout-hr-oblique",1100d,1.4,2.35,.01,42d,ThinFilmPolarization.P,DesignKind.HighReflector,.02,.85),
            ("heldout-hr-wide",1800d,1.3,2.05,.015,30d,ThinFilmPolarization.Unpolarized,DesignKind.HighReflector,.1,.7)
        };
        foreach (var (name, wave, nl, nh, k, angle, pol, kind, band, requirement) in specs)
        {
            var d = Examples.Create(kind);
            MaterialSnapshot Capture(string label, double n, double extinction = 0) => MaterialLibrary.Capture(
                new ConstantIndexMaterial(label, n, extinction), "Frozen synthetic comparison; not measured", wave / 2, wave * 2);
            var inc = Capture("incident", 1); var sub = Capture("substrate", 1.52);
            var low = Capture("low", nl, k / 5); var high = Capture("high", nh, k);
            double Quarter(MaterialSnapshot m) => CoherentThinFilmSolver.QuarterWaveThickness(m.Resolve(new()), inc.Resolve(new()), wave, angle);
            var layers = kind == DesignKind.Antireflection ? new[] { new FilmLayer(low.Id, .9 * Quarter(low)) }
                : Enumerable.Range(0, 16).Select(i => new FilmLayer(i % 2 == 0 ? high.Id : low.Id, Quarter(i % 2 == 0 ? high : low) * (i % 2 == 0 ? .94 : 1.04))).ToArray();
            d = d with
            {
                Name = name,
                Materials = [inc, sub, low, high],
                IncidentId = inc.Id,
                SubstrateId = sub.Id,
                LowId = low.Id,
                HighId = high.Id,
                Layers = layers,
                Target = d.Target with
                {
                    CenterNm = wave,
                    MinimumNm = wave * (1 - band),
                    MaximumNm = wave * (1 + band),
                    AngleDegrees = angle,
                    Polarization = pol,
                    Reflectance = requirement,
                    MaximumLayers = 32
                },
                Settings = d.Settings with
                {
                    DesignProtocolVersion = 4,
                    MaximumIterations = 24,
                    PreviewIntervals = 100,
                    OptimizationIntervals = 40,
                    MinimumThicknessNm = wave * .001,
                    MaximumThicknessNm = wave * 2
                }
            };
            await ExperimentStore.SaveAsync(d, Path.Combine(directory, name + ".coating.json"));
        }
        foreach (var (name, path) in new[]
        {
            ("real-ar-threshold","real/ar/general"), ("real-ar-polished","real/ar/modern"),
            ("real-hr-original-target","real/hr/general"), ("real-narrowband-control","real/real1/general")
        })
        {
            var source = await ExperimentStore.OpenAsync(Path.Combine("validation/rechecks/2026-10-30/coating-general-fixes/final-runtime", path, "result.coating.json"));
            var d = source with
            {
                Name = name,
                Settings = source.Settings with { DesignProtocolVersion = 4, MaximumIterations = 24 },
                Result = null,
                Candidates = null,
                Tolerance = null
            };
            await ExperimentStore.SaveAsync(d, Path.Combine(directory, name + ".coating.json"));
        }
        var fixedSource = await ExperimentStore.OpenAsync(Path.Combine(directory, "hr-nearir.coating.json"));
        await ExperimentStore.SaveAsync(fixedSource with { Name = "fixed-films", Layers = fixedSource.Layers.Select((l, i) => l with { Variable = i % 3 != 0 }).ToArray() },
            Path.Combine(directory, "fixed-films.coating.json"));
        Console.WriteLine("Frozen 17 specifications; no result-dependent target or material changes.");
    }

    internal static async Task Run(string inputDirectory, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory); var summaries = new List<object>(); var cases = new List<object>();
        var service = new DesignService();
        foreach (var path in Directory.GetFiles(inputDirectory, "*.coating.json").Order(StringComparer.Ordinal))
        {
            var input = await ExperimentStore.OpenAsync(path); using var limit = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var before = service.Evaluate(input, token: limit.Token); var watch = Stopwatch.StartNew();
            var after = service.Optimize(input, token: limit.Token); watch.Stop(); var result = after.Result!;
            await ExperimentStore.SaveAsync(after, Path.Combine(outputDirectory, Path.GetFileName(path)), limit.Token);
            double Loss(Experiment d) => d.Target.Kind == DesignKind.Antireflection ? d.Result!.Metrics.MaximumReflectance
                : d.Target.Kind == DesignKind.HighReflector ? 1 - d.Result!.Metrics.MinimumReflectance : d.Result!.Merit;
            var protectedLayers = Enumerable.Range(0, input.Layers.Length).Where(i => !input.Layers[i].Variable).All(i => input.Layers[i] == after.Layers[i]);
            var identicalMaterials = JsonSerializer.Serialize(input.Materials, Experiment.JsonOptions) == JsonSerializer.Serialize(after.Materials, Experiment.JsonOptions);
            summaries.Add(new
            {
                input.Name,
                InputFingerprint = input.Fingerprint(),
                input.Target,
                input.Settings,
                BeforeLoss = Loss(before),
                AfterLoss = Loss(after),
                BeforePassed = before.Result!.Passed,
                AfterPassed = result.Passed,
                BeforeMetrics = before.Result.Metrics,
                AfterMetrics = result.Metrics,
                BeforeBlockingOd = (input.Target.StopBands ?? []).Select(b => before.Result.Spectrum.Where(s => s.WavelengthNanometers >= b.MinimumNm && s.WavelengthNanometers <= b.MaximumNm).Min(s => s.Power.OpticalDensity)).ToArray(),
                AfterBlockingOd = (input.Target.StopBands ?? []).Select(b => result.Spectrum.Where(s => s.WavelengthNanometers >= b.MinimumNm && s.WavelengthNanometers <= b.MaximumNm).Min(s => s.Power.OpticalDensity)).ToArray(),
                Checks = result.Checks,
                ProtectedLayers = protectedLayers,
                IdenticalMaterials = identicalMaterials,
                LayersChanged = !input.Layers.SequenceEqual(after.Layers),
                result.Run,
                Seconds = watch.Elapsed.TotalSeconds
            });
            if (before.Result.Passed && (!result.Passed || Loss(after) > Loss(before)) || !protectedLayers || !identicalMaterials || result.Run.Iterations > input.Settings.MaximumIterations)
                throw new InvalidOperationException("Feasibility, quality, fixed-layer, material or iteration guard failed: " + input.Name);
            var materials = after.Materials.ToDictionary(m => m.Id, m => m.Resolve(new MaterialRegistry()));
            var solver = new CoherentThinFilmSolver(materials[after.IncidentId], materials[after.SubstrateId], after.Layers.Select(l => new CoherentFilm(materials[l.MaterialId], l.ThicknessNm)));
            foreach (var wave in ThinFilmSpectrum.Grid(input.Target.MinimumNm, input.Target.MaximumNm, 8)) foreach (var pol in Enum.GetValues<ThinFilmPolarization>())
            {
                double[] Index(string id) => [materials[id].RefractiveIndex(wave), materials[id].ExtinctionCoefficient(wave)];
                cases.Add(new
                {
                    Name = input.Name,
                    Lambda = wave,
                    Angle = input.Target.AngleDegrees,
                    Pol = pol.ToString(),
                    N0 = Index(after.IncidentId),
                    Ns = Index(after.SubstrateId),
                    Layers = after.Layers.Select(l => new { N = Index(l.MaterialId), D = l.ThicknessNm }),
                    Core = solver.Evaluate(wave, input.Target.AngleDegrees, pol)
                });
            }
            Console.WriteLine($"{input.Name}: loss {Loss(before):G10} -> {Loss(after):G10}; passed={result.Passed}; iterations={result.Run.Iterations}; {watch.Elapsed.TotalSeconds:F3}s");
        }
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "summary.json"), JsonSerializer.Serialize(summaries, Experiment.JsonOptions));
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "core-cases.json"), JsonSerializer.Serialize(cases, Experiment.JsonOptions));
    }
}

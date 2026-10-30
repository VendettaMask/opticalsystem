using System.Text.Json;
using OpticalSystemDesign.CoatingDesign.Engine;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Materials;

internal static class SpectrumEdgeAudit
{
    internal static async Task Run(string[] args)
    {
        var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
        var summaries = new List<object>(); var cases = new List<object>(); var service = new DesignService();
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        foreach (var relative in new[] { "real/ar/modern", "real/hr/modern", "real/real1/modern", "real/real2/modern",
            "matrix/visible-low-loss/general", "matrix/red-low-contrast/general", "matrix/near-ir-oblique-p/general", "matrix/ir-absorbing-s/general" })
        {
            var original = await ExperimentStore.OpenAsync(Path.Combine("validation/rechecks/2026-10-30/coating-general-fixes/final-runtime", relative, "result.coating.json"));
            var recalculated = service.Evaluate(original, token: deadline.Token);
            var old = original.Result!; var result = recalculated.Result!;
            var name = relative.Replace('/', '-');
            await ExperimentStore.SaveAsync(recalculated, Path.Combine(output, name + ".coating.json"), deadline.Token);
            var materials = original.Materials.ToDictionary(m => m.Id, m => m.Resolve(new MaterialRegistry()));
            var solver = new CoherentThinFilmSolver(materials[original.IncidentId], materials[original.SubstrateId],
                original.Layers.Select(l => new CoherentFilm(materials[l.MaterialId], l.ThicknessNm)));
            var waves = ThinFilmSpectrum.Grid(original.Target.MinimumNm, original.Target.MaximumNm, 14).Append(original.Target.CenterNm).Distinct().Order().ToArray();
            foreach (var w in waves) foreach (var pol in Enum.GetValues<ThinFilmPolarization>())
                AddCase(name, w, original.Target.AngleDegrees, pol, materials[original.IncidentId], materials[original.SubstrateId],
                    original.Layers.Select(l => new CoherentFilm(materials[l.MaterialId], l.ThicknessNm)).ToArray(), solver);
            var sameChecks = old.Checks.Select(c => c.Passed).SequenceEqual(result.Checks.Select(c => c.Passed));
            summaries.Add(new
            {
                name,
                InputFingerprintUnchanged = original.Fingerprint() == recalculated.Fingerprint(),
                OriginalPassed = old.Passed,
                RecalculatedPassed = result.Passed,
                SameChecks = sameChecks,
                OriginalMetrics = old.Metrics,
                RecalculatedMetrics = result.Metrics,
                WidthDifferenceNm = result.Metrics.FwhmNanometers - old.Metrics.FwhmNanometers,
                result.SpectrumMetricsVersion,
                result.SamplingConverged,
                result.VerificationSamples
            });
            if (old.Passed != result.Passed || !sameChecks) throw new InvalidOperationException("Frozen prescription changed acceptance: " + relative);
            Console.WriteLine($"recalculated {name}: passed={result.Passed}; width delta={result.Metrics.FwhmNanometers - old.Metrics.FwhmNanometers:G9} nm");
        }

        foreach (var tir in new[] { false, true })
        {
            IMaterial incident = new ConstantIndexMaterial("incident", tir ? 1.7 : 1);
            IMaterial substrate = new ConstantIndexMaterial("substrate", tir ? 1 : 1.52);
            var layers = new[] { new CoherentFilm(new ConstantIndexMaterial("film", tir ? 1.6 : .3, tir ? 0 : 4), tir ? 100 : 100000) };
            var solver = new CoherentThinFilmSolver(incident, substrate, layers);
            foreach (var w in new[] { 450d, 550, 940, 1300 }) foreach (var pol in Enum.GetValues<ThinFilmPolarization>())
                AddCase(tir ? "evanescent-exit" : "opaque-film", w, tir ? 65 : 15, pol, incident, substrate, layers, solver);
        }
        var feasible = new List<object>();
        foreach (var (wave, nl, nh, angle, pol) in new[] { (450d, 1.38, 2.3, 0d, ThinFilmPolarization.S),
            (800d, 1.32, 2.15, 20d, ThinFilmPolarization.P), (1300d, 1.4, 2.25, 35d, ThinFilmPolarization.Unpolarized) })
        {
            var original = Examples.Create(DesignKind.Antireflection);
            MaterialSnapshot Capture(string name, double n) => MaterialLibrary.Capture(new ConstantIndexMaterial(name, n), "Synthetic feasible-interior control, not measured", wave / 2, 2 * wave);
            var low = Capture("low", nl); var high = Capture("high", nh); var sub = Capture("substrate", 1.52);
            var inc = MaterialLibrary.Capture(new AirMaterial(), "Synthetic control", wave / 2, 2 * wave);
            var thickness = .9 * CoherentThinFilmSolver.QuarterWaveThickness(low.Resolve(new()), inc.Resolve(new()), wave, angle);
            original = original with
            {
                Materials = [inc, sub, low, high],
                IncidentId = inc.Id,
                SubstrateId = sub.Id,
                LowId = low.Id,
                HighId = high.Id,
                Layers = [new(low.Id, thickness)],
                Target = original.Target with
                {
                    CenterNm = wave,
                    MinimumNm = .9 * wave,
                    MaximumNm = 1.1 * wave,
                    AngleDegrees = angle,
                    Polarization = pol,
                    Reflectance = .1
                },
                Settings = original.Settings with { MaximumIterations = 24, MinimumThicknessNm = .001 * wave, MaximumThicknessNm = 2 * wave }
            };
            var before = service.Evaluate(original);
            var modern = service.Optimize(original with { Settings = original.Settings with { DesignProtocolVersion = 2 } });
            var general = service.Optimize(original with { Settings = original.Settings with { DesignProtocolVersion = 4 } });
            await ExperimentStore.SaveAsync(general, Path.Combine(output, $"feasible-{wave:G}-general.coating.json"));
            feasible.Add(new
            {
                Wave = wave,
                LowIndex = nl,
                HighIndex = nh,
                Angle = angle,
                Polarization = pol,
                InitialWorstR = before.Result!.Metrics.MaximumReflectance,
                ModernWorstR = modern.Result!.Metrics.MaximumReflectance,
                GeneralWorstR = general.Result!.Metrics.MaximumReflectance,
                InitialPassed = before.Result.Passed,
                ModernPassed = modern.Result.Passed,
                GeneralPassed = general.Result.Passed,
                GeneralThicknessUnchanged = original.Layers.SequenceEqual(general.Layers),
                GeneralStopReason = general.Result.Run.StopReason
            });
        }
        await File.WriteAllTextAsync(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new { Recalculated = summaries, FeasibleInteriorControls = feasible }, Experiment.JsonOptions));
        await File.WriteAllTextAsync(Path.Combine(output, "core-cases.json"), JsonSerializer.Serialize(cases, Experiment.JsonOptions));

        void AddCase(string name, double wave, double angle, ThinFilmPolarization pol, IMaterial n0, IMaterial ns, CoherentFilm[] layers, CoherentThinFilmSolver solver)
        {
            static double[] Index(IMaterial m, double w) => [m.RefractiveIndex(w), m.ExtinctionCoefficient(w)];
            cases.Add(new
            {
                Name = name,
                Lambda = wave,
                Angle = angle,
                Pol = pol.ToString(),
                N0 = Index(n0, wave),
                Ns = Index(ns, wave),
                Layers = layers.Select(l => new { N = Index(l.Material, wave), D = l.ThicknessNanometers }),
                Core = solver.Evaluate(wave, angle, pol)
            });
        }
    }
}

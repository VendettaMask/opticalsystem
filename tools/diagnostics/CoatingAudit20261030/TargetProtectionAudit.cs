using OpticalSystemDesign.CoatingDesign.Engine;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Materials;

internal static class TargetProtectionAudit
{
    internal static async Task Scale(string directory)
    {
        Directory.CreateDirectory(directory); var service = new DesignService();
        foreach (var wave in new[] { 450d, 1350 })
        {
            var d = Examples.Create(DesignKind.NarrowBand);
            var low = MaterialLibrary.Capture(new ConstantIndexMaterial("generic low", 1.43, .0008), "synthetic scale control", wave / 2, wave * 2);
            var high = MaterialLibrary.Capture(new ConstantIndexMaterial("generic high", 2.15, .004), "synthetic scale control", wave / 2, wave * 2);
            var sub = MaterialLibrary.Capture(new ConstantIndexMaterial("generic substrate", 1.52), "synthetic scale control", wave / 2, wave * 2);
            d = d with
            {
                LowId = low.Id, HighId = high.Id, SubstrateId = sub.Id, Materials = [d.Materials.Single(m => m.Id == d.IncidentId), low, high, sub],
                Target = d.Target with { CenterNm = wave, MinimumNm = wave * .85, MaximumNm = wave * 1.15, FwhmNm = wave * .03,
                    CenterToleranceNm = wave * .001, FwhmToleranceNm = wave * .003, AngleDegrees = 20, Polarization = ThinFilmPolarization.Unpolarized,
                    StopBands = [new(wave * .88, wave * .92), new(wave * 1.08, wave * 1.12)] },
                Settings = d.Settings with { DesignProtocolVersion = 4, MinimumThicknessNm = wave * .002, MaximumThicknessNm = wave * 2, MaximumIterations = 12 }
            };
            d = d with { Layers = service.BuildNarrowBandPrototype(d, new([4, 6], [2])) };
            await ExperimentStore.SaveAsync(d, Path.Combine(directory, $"input-{wave}.coating.json"));
            var before = service.Evaluate(d); var after = service.Optimize(d);
            var registry = new MaterialRegistry(); var materials = d.Materials.ToDictionary(m => m.Id, m => m.Resolve(registry));
            var solver = new CoherentThinFilmSolver(materials[d.IncidentId], materials[d.SubstrateId], d.Layers.Select(l => new CoherentFilm(materials[l.MaterialId], l.ThicknessNm)));
            var peak = ThinFilmSpectrum.RefineTransmissionPeak(solver, before.Result!.Spectrum.Where(s => Math.Abs(s.WavelengthNanometers - wave) <= 2 * d.Target.FwhmNm).ToArray(), 20, ThinFilmPolarization.Unpolarized);
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
            var initial = (double[])typeof(DesignService).GetMethod("ObjectiveGrid", flags)!.Invoke(null, [d])!;
            var anchors = initial.Min(w => Math.Abs(w - peak.Peak.WavelengthNanometers)) > d.Target.FwhmNm * Math.Sqrt(Math.BitIncrement(1.0) - 1) ? new[] { peak.Peak.WavelengthNanometers } : Array.Empty<double>();
            var grid = (double[])typeof(DesignService).GetMethod("GeneralGrid", flags)!.Invoke(null, [d, initial.Concat(anchors)])!;
            var system = typeof(DesignService).GetMethod("GeneralConstraintSystem", flags)!.Invoke(null, [d, solver, grid, true, null])!;
            var values = (double[])system.GetType().GetProperty("Values")!.GetValue(system)!;
            var matrix = (double[,])system.GetType().GetProperty("Derivatives")!.GetValue(system)!;
            var scales = d.Layers.Select(l => CoherentThinFilmSolver.CharacteristicThickness(materials[l.MaterialId], materials[d.IncidentId], wave, 20)).ToArray();
            await File.WriteAllTextAsync(Path.Combine(directory, $"init-{wave}.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                Peak = peak.Peak.WavelengthNanometers / wave, peak.Peak.Power, Grid = grid.Select(w => w / wave), Values = values,
                Jacobian = Enumerable.Range(0, values.Length).Select(i => Enumerable.Range(0, scales.Length).Select(j => matrix[i, j] * scales[j]))
            }, Experiment.JsonOptions));
            await ExperimentStore.SaveAsync(before, Path.Combine(directory, $"before-{wave}.coating.json"));
            await ExperimentStore.SaveAsync(after, Path.Combine(directory, $"after-{wave}.coating.json"));
            Console.WriteLine($"{wave}: {after.Result!.Run.StopReason}; merit={after.Result.Merit:G17}; widths {before.Result!.Metrics.FwhmNanometers:G17} -> {after.Result.Metrics.FwhmNanometers:G17}");
        }
    }

    internal static async Task Freeze(string root)
    {
        var directory = Path.Combine(root, "inputs"); Directory.CreateDirectory(directory);
        var previous = "validation/rechecks/2026-10-30/coating-infeasible-repair/final-matrix";
        foreach (var (name, arm, file) in new[]
        {
            ("prior-single-24", "before", "real-narrowband-control"), ("prior-double-96", "before", "real-nbf2-96"),
            ("continued-single-24", "after", "real-narrowband-control"), ("continued-double-96", "after", "real-nbf2-96"),
            ("continued-hr-96", "after", "real-hr-96")
        })
        {
            var source = await ExperimentStore.OpenAsync(Path.Combine(previous, arm, file + ".coating.json"));
            await ExperimentStore.SaveAsync(source with { Name = name, Result = null, Candidates = null, Tolerance = null }, Path.Combine(directory, name + ".coating.json"));
        }
        var specs = new[]
        {
            ("new-blue-single",450d,1.38,2.3,0d,0d,ThinFilmPolarization.S,1),
            ("new-red-double",633d,1.5,2.0,.003,17d,ThinFilmPolarization.P,2),
            ("new-nearir-single",1064d,1.4,2.25,.006,35d,ThinFilmPolarization.P,1),
            ("new-ir-double",1550d,1.35,2.2,.01,30d,ThinFilmPolarization.Unpolarized,2),
            ("new-oblique-single",940d,1.45,2.15,.002,45d,ThinFilmPolarization.S,1),
            ("new-fixed-double",780d,1.43,2.15,.004,23d,ThinFilmPolarization.Unpolarized,2)
        };
        var service = new DesignService();
        foreach (var (name, wave, nl, nh, k, angle, pol, cavities) in specs)
        {
            var d = Examples.Create(DesignKind.NarrowBand);
            MaterialSnapshot Material(string label, double n, double loss = 0) => MaterialLibrary.Capture(new ConstantIndexMaterial(label, n, loss),
                "Frozen synthetic cross-specification control, not measured material data", wave / 2, wave * 2);
            var inc = Material("incident", 1); var sub = Material("substrate", 1.52); var low = Material("low", nl, k / 5); var high = Material("high", nh, k);
            d = d with
            {
                Name = name, Materials = [inc, sub, low, high], IncidentId = inc.Id, SubstrateId = sub.Id, LowId = low.Id, HighId = high.Id,
                Target = d.Target with
                {
                    CenterNm = wave, MinimumNm = wave * .85, MaximumNm = wave * 1.15, FwhmNm = wave * .035,
                    FwhmToleranceNm = wave * .003, CenterToleranceNm = wave * .001, AngleDegrees = angle, Polarization = pol,
                    Structure = new(cavities), StopBands = [new(wave * .88, wave * .92), new(wave * 1.08, wave * 1.12)]
                },
                Settings = d.Settings with { DesignProtocolVersion = 4, MaximumIterations = 48, MinimumThicknessNm = wave * .002, MaximumThicknessNm = wave * 2 }
            };
            var prototype = cavities == 1 ? new NarrowBandPrototype([5, 5], [1]) : new NarrowBandPrototype([3, 7, 3], [1, 1]);
            d = d with { Layers = service.BuildNarrowBandPrototype(d, prototype) };
            if (name == "new-fixed-double") d = d with { Layers = d.Layers.Select((l, i) => l with { Variable = i % 3 != 0 }).ToArray() };
            await ExperimentStore.SaveAsync(d, Path.Combine(directory, name + ".coating.json"));
        }
        Console.WriteLine("Frozen 11 additional starting profiles/specifications before paired evaluation; no target or n/k edits after running.");
    }
}

using System.Reflection;
using System.Text.Json;
using OpticalSystemDesign.CoatingDesign.Engine;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Coatings;

internal static class BalancedTargetAudit
{
    internal static async Task Freeze(string root)
    {
        var directory = Path.Combine(root, "inputs");
        if (Directory.Exists(directory) && Directory.GetFiles(directory).Length > 0) throw new InvalidOperationException("Inputs already frozen.");
        Directory.CreateDirectory(directory);
        foreach (var path in Directory.GetFiles("validation/rechecks/2026-10-30/coating-target-preservation/inputs", "*.coating.json"))
            File.Copy(path, Path.Combine(directory, Path.GetFileName(path)));
        var service = new DesignService();
        foreach (var (name, wave, nl, nh, loss, angle, pol, cavities, fixedFilms) in new[]
        {
            ("holdout-violet",405d,1.46,2.1,.0012,27d,ThinFilmPolarization.S,1,false),
            ("holdout-red-fixed",680d,1.37,2.32,.007,41d,ThinFilmPolarization.P,2,true),
            ("holdout-telecom",1550d,1.44,2.05,.0033,12d,ThinFilmPolarization.Unpolarized,2,false),
            ("holdout-ir",2000d,1.5,2.35,.02,32d,ThinFilmPolarization.S,1,false)
        })
        {
            MaterialSnapshot Material(string label, double n, double k = 0) => MaterialLibrary.Capture(new ConstantIndexMaterial(label, n, k),
                "Synthetic held-out control frozen before paired verification; not measured", wave / 2, wave * 2);
            var inc = Material("incident", 1); var sub = Material("substrate", 1.52);
            var low = Material("low", nl, loss / 5); var high = Material("high", nh, loss);
            var d = Examples.Create(DesignKind.NarrowBand);
            d = d with
            {
                Name = name, Materials = [inc, sub, low, high], IncidentId = inc.Id, SubstrateId = sub.Id, LowId = low.Id, HighId = high.Id,
                Target = d.Target with { CenterNm = wave, MinimumNm = wave * .85, MaximumNm = wave * 1.15, FwhmNm = wave * .04,
                    FwhmToleranceNm = wave * .004, CenterToleranceNm = wave * .001, AngleDegrees = angle, Polarization = pol,
                    Structure = new(cavities), StopBands = [new(wave * .87, wave * .91), new(wave * 1.09, wave * 1.13)] },
                Settings = d.Settings with { DesignProtocolVersion = 4, MaximumIterations = 48, MinimumThicknessNm = wave * .002, MaximumThicknessNm = wave * 2 }
            };
            d = d with { Layers = service.BuildNarrowBandPrototype(d, cavities == 1 ? new([4, 6], [1]) : new([3, 5, 3], [1, 1])) };
            if (fixedFilms) d = d with { Layers = d.Layers.Select((l, i) => l with { Variable = i % 4 != 0 }).ToArray() };
            await ExperimentStore.SaveAsync(d, Path.Combine(directory, name + ".coating.json"));
        }
        foreach (var (name, wave, angle, pol) in new[] { ("holdout-real-hr-red",633d,23d,ThinFilmPolarization.Unpolarized),
            ("holdout-real-hr-nearir",780d,38d,ThinFilmPolarization.P) })
        {
            var source = await ExperimentStore.OpenAsync(Path.Combine(directory, "real-hr-96.coating.json"));
            var d = source with { Name = name, Layers = source.Layers.Select(l => l with { ThicknessNm = l.ThicknessNm * wave / source.Target.CenterNm }).ToArray(),
                Target = source.Target with { CenterNm = wave, MinimumNm = wave * .94, MaximumNm = wave * 1.06, AngleDegrees = angle, Polarization = pol },
                Settings = source.Settings with { MaximumIterations = 48 } };
            await ExperimentStore.SaveAsync(d, Path.Combine(directory, name + ".coating.json"));
        }
        Console.WriteLine("Frozen 35 unchanged starting profiles and six new held-out specifications before paired verification.");
    }

    internal static async Task Probe(string inputPath, string directory)
    {
        Directory.CreateDirectory(directory);
        var d = await ExperimentStore.OpenAsync(inputPath);
        var service = new DesignService(); var start = service.Evaluate(d);
        var materials = d.Materials.ToDictionary(m => m.Id, m => m.Resolve(new MaterialRegistry()));
        var grid = (double[])typeof(DesignService).GetMethod("ObjectiveGrid", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [d])!;
        var refined = ((FilmLayer[] Layers, double Merit, RunRecord Run))typeof(DesignService)
            .GetMethod("RefineGeneralThickness", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(service, [d, materials, grid, d.Settings.MaximumIterations, null, CancellationToken.None, start])!;
        var candidate = service.Evaluate(d with { Layers = refined.Layers, Result = null }, refined.Run);
        await ExperimentStore.SaveAsync(start, Path.Combine(directory, "start.coating.json"));
        await ExperimentStore.SaveAsync(candidate, Path.Combine(directory, "candidate.coating.json"));
        Console.WriteLine(JsonSerializer.Serialize(new { start.Result!.Metrics, Candidate = candidate.Result!.Metrics,
            StartOd = start.Target.StopBands!.Select(b => start.Result.Spectrum.Where(s => s.WavelengthNanometers >= b.MinimumNm && s.WavelengthNanometers <= b.MaximumNm).Min(s => s.Power.OpticalDensity)),
            CandidateOd = d.Target.StopBands!.Select(b => candidate.Result.Spectrum.Where(s => s.WavelengthNanometers >= b.MinimumNm && s.WavelengthNanometers <= b.MaximumNm).Min(s => s.Power.OpticalDensity)),
            candidate.Result.Run }, Experiment.JsonOptions));
    }
}

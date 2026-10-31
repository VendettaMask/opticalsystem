using OpticalSystemDesign.CoatingDesign.Engine;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Materials;

internal static class ContinuationAudit
{
    internal static async Task Freeze(string root)
    {
        var directory = Path.Combine(root, "holdout-inputs");
        if (Directory.Exists(directory) && Directory.GetFiles(directory).Length > 0) throw new InvalidOperationException("Holdouts already frozen.");
        Directory.CreateDirectory(directory); var service = new DesignService();
        foreach (var (name, wave, nl, nh, loss, angle, pol, cavities, fixedFilms) in new[]
        {
            ("fresh-blue-fixed",460d,1.39,2.18,.0045,22d,ThinFilmPolarization.P,1,true),
            ("fresh-nearir-double",860d,1.47,2.24,.0064,34d,ThinFilmPolarization.Unpolarized,2,true),
            ("fresh-telecom-single",1200d,1.43,2.08,.0018,7d,ThinFilmPolarization.S,1,false),
            ("fresh-ir-double",1750d,1.52,2.27,.0097,39d,ThinFilmPolarization.P,2,false)
        })
        {
            MaterialSnapshot Capture(string label, double n, double k = 0) => MaterialLibrary.Capture(new ConstantIndexMaterial(label, n, k),
                "Synthetic control frozen before new-algorithm cross-verification; not measured", wave / 2, wave * 2);
            var inc = Capture("incident", 1); var sub = Capture("substrate", 1.52); var low = Capture("low", nl, loss / 5); var high = Capture("high", nh, loss);
            var d = Examples.Create(DesignKind.NarrowBand);
            d = d with
            {
                Name = name, Materials = [inc, sub, low, high], IncidentId = inc.Id, SubstrateId = sub.Id, LowId = low.Id, HighId = high.Id,
                Target = d.Target with { CenterNm = wave, MinimumNm = wave * .84, MaximumNm = wave * 1.16,
                    FwhmNm = wave * .025, FwhmToleranceNm = wave * .003, CenterToleranceNm = wave * .001,
                    AngleDegrees = angle, Polarization = pol, MaximumLayers = 64, Structure = new(cavities),
                    StopBands = [new(wave * .86, wave * .91), new(wave * 1.08, wave * 1.14)] },
                Settings = d.Settings with { DesignProtocolVersion = 4, MaximumIterations = 48, MinimumThicknessNm = wave * .002, MaximumThicknessNm = wave * 2 }
            };
            d = d with { Layers = service.BuildNarrowBandPrototype(d, cavities == 1 ? new([4, 5], [2]) : new([3, 4, 4], [1, 1])) };
            if (fixedFilms) d = d with { Layers = d.Layers.Select((l, i) => l with { Variable = i % 4 != 0 }).ToArray() };
            await ExperimentStore.SaveAsync(d, Path.Combine(directory, name + ".coating.json"));
        }
        var source = await ExperimentStore.OpenAsync(Path.Combine(root, "inputs", "real-hr-96.coating.json"));
        foreach (var (name, wave, angle, pol) in new[] { ("fresh-real-hr-red",720d,31d,ThinFilmPolarization.S), ("fresh-real-hr-nearir",850d,16d,ThinFilmPolarization.Unpolarized) })
        {
            var d = source with { Name = name, Layers = source.Layers.Select(l => l with { ThicknessNm = l.ThicknessNm * wave / source.Target.CenterNm }).ToArray(),
                Target = source.Target with { CenterNm = wave, MinimumNm = wave * .94, MaximumNm = wave * 1.06, AngleDegrees = angle, Polarization = pol },
                Settings = source.Settings with { MaximumIterations = 48 }, Result = null, Candidates = null, Tolerance = null };
            await ExperimentStore.SaveAsync(d, Path.Combine(directory, name + ".coating.json"));
        }
        Console.WriteLine("Frozen six new physical inputs before their result inspection; original acceptance values retained.");
    }
}

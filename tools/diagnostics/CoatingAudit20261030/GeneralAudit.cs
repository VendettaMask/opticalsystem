using OpticalSystemDesign.CoatingDesign.Engine;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Materials;

internal static class GeneralAudit
{
    internal static async Task Run(string[] args)
    {
        var root = Path.GetFullPath(args[0]); Directory.CreateDirectory(root);
        var selected = args.Length > 1 ? args[1] : "all";
        // Predeclared paired controls: no result-dependent specification or acceptance changes.
        var controls = new[] {
            ("visible-low-loss", 450.0, 1.38, 2.3, 0.0, 0.0, ThinFilmPolarization.S, 1, .03),
            ("red-low-contrast", 680.0, 1.5, 1.95, 0.0, 0.0, ThinFilmPolarization.Unpolarized, 2, .04),
            ("near-ir-oblique-p", 940.0, 1.43, 2.15, .002, 35.0, ThinFilmPolarization.P, 1, .025),
            ("ir-absorbing-s", 1300.0, 1.4, 2.25, .01, 25.0, ThinFilmPolarization.S, 2, .035)
        };
        foreach (var c in controls)
        {
            if (selected != "all" && selected != c.Item1) continue;
            var d = Examples.Create(DesignKind.NarrowBand); var wave = c.Item2;
            var lo = MaterialLibrary.Capture(new ConstantIndexMaterial("control L", c.Item3, c.Item5 / 5), "synthetic holdout control", wave / 2, wave * 2);
            var hi = MaterialLibrary.Capture(new ConstantIndexMaterial("control H", c.Item4, c.Item5), "synthetic holdout control", wave / 2, wave * 2);
            var sub = MaterialLibrary.Capture(new ConstantIndexMaterial("control S", 1.52), "synthetic holdout control", wave / 2, wave * 2);
            d = d with
            {
                LowId = lo.Id,
                HighId = hi.Id,
                SubstrateId = sub.Id,
                Materials = [d.Materials.Single(m => m.Id == d.IncidentId), lo, hi, sub],
                Target = d.Target with
                {
                    CenterNm = wave,
                    MinimumNm = wave * .85,
                    MaximumNm = wave * 1.15,
                    FwhmNm = wave * c.Item9,
                    CenterToleranceNm = wave * .001,
                    FwhmToleranceNm = wave * .003,
                    AngleDegrees = c.Item6,
                    Polarization = c.Item7,
                    Structure = new(c.Item8),
                    StopBands = [new(wave * .88, wave * .92), new(wave * 1.08, wave * 1.12)]
                },
                Settings = d.Settings with { MinimumThicknessNm = wave * .002, MaximumThicknessNm = wave * 2 }
            };
            var source = Path.Combine(root, c.Item1 + ".coating.json"); await ExperimentStore.SaveAsync(d, source);
            foreach (var protocol in new[] { "modern", "general" })
                await CompetitionAudit.Run([Path.Combine(root, c.Item1, protocol), c.Item1, "12", protocol, "generate", source]);
        }
    }
}

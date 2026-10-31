using OpticalSystemDesign.CoatingDesign.Engine;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Materials;

internal static class MarginAudit
{
    internal static async Task Freeze(string sourceDirectory, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var specs = new[]
        {
            ("real-nbf2-96", "new-real-nbf-green", 500.0, 5.0, ThinFilmPolarization.S, false),
            ("real-nbf1-96", "new-real-nbf-red", 650.0, 12.0, ThinFilmPolarization.P, false),
            ("real-nbf2-96", "new-real-nbf-nearir-fixed", 900.0, 23.0, ThinFilmPolarization.Unpolarized, true),
            ("real-nbf1-96", "new-real-nbf-ir", 1300.0, 31.0, ThinFilmPolarization.S, false),
            ("real-hr-96", "new-real-hr-green", 520.0, 10.0, ThinFilmPolarization.S, false),
            ("holdout-real-hr-red", "new-real-hr-ir-fixed", 1100.0, 27.0, ThinFilmPolarization.P, true),
            ("fresh-blue-fixed", "new-synthetic-green", 530.0, 19.0, ThinFilmPolarization.Unpolarized, false),
            ("fresh-nearir-double", "new-synthetic-ir-fixed", 1480.0, 33.0, ThinFilmPolarization.P, true)
        };
        foreach (var (file, name, wavelength, angle, polarization, fixedFilms) in specs)
        {
            var d = await ExperimentStore.OpenAsync(Path.Combine(sourceDirectory, file + ".coating.json"));
            var materials = d.Materials.ToDictionary(m => m.Id, m => m.Resolve(new MaterialRegistry()));
            var ratio = wavelength / d.Target.CenterNm;
            var thicknessRatios = d.Layers.Select(l =>
                CoherentThinFilmSolver.CharacteristicThickness(materials[l.MaterialId], materials[d.IncidentId], wavelength, angle)
                / CoherentThinFilmSolver.CharacteristicThickness(materials[l.MaterialId], materials[d.IncidentId], d.Target.CenterNm, d.Target.AngleDegrees)).ToArray();
            var target = d.Target with { CenterNm = wavelength, MinimumNm = d.Target.MinimumNm * ratio, MaximumNm = d.Target.MaximumNm * ratio,
                FwhmNm = d.Target.FwhmNm * ratio, FwhmToleranceNm = d.Target.FwhmToleranceNm * ratio,
                CenterToleranceNm = d.Target.CenterToleranceNm * ratio, AngleDegrees = angle, Polarization = polarization,
                StopBands = d.Target.StopBands?.Select(b => new SpectralBand(b.MinimumNm * ratio, b.MaximumNm * ratio)).ToArray() };
            var input = d with { Id = Guid.NewGuid(), Name = name, Target = target,
                Settings = d.Settings with { MaximumIterations = 48, MinimumThicknessNm = d.Settings.MinimumThicknessNm * thicknessRatios.Min(),
                    MaximumThicknessNm = d.Settings.MaximumThicknessNm * thicknessRatios.Max() },
                Layers = d.Layers.Select((l, i) => l with { ThicknessNm = l.ThicknessNm * thicknessRatios[i], Variable = l.Variable && (!fixedFilms || i % 3 != 0) }).ToArray(),
                Result = null, Candidates = null, Tolerance = null };
            await ExperimentStore.SaveAsync(input, Path.Combine(outputDirectory, name + ".coating.json"));
        }
        Console.WriteLine($"Frozen {specs.Length} new specifications without changing any n/k snapshot. Shared Core phase units generate the initial layers.");
    }
}

using System.Reflection;
using System.Text.Json;
using OpticalSystemDesign.CoatingDesign.Engine;
using OpticalSystemDesign.Core.Materials;

namespace OpticalSystemDesign.CoatingDesign.Tests;

public sealed class StructureFeedbackTests
{
    private static FilmLayer[]? Transfer(Experiment d, FilmLayer[] fitted, NarrowBandPrototype oldOrigin, NarrowBandPrototype next, bool prefix)
    {
        var materials = d.Materials.ToDictionary(m => m.Id, m => m.Resolve(new MaterialRegistry()));
        return (FilmLayer[]?)typeof(DesignService).GetMethod("TransferGeneralPrototype", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [d, materials, fitted, oldOrigin, next, prefix]);
    }

    [Theory]
    [InlineData(550, 2.1, 1)]
    [InlineData(550, 2.6, 2)]
    [InlineData(940, 3.4, 1)]
    [InlineData(940, 2.4, 2)]
    public void PhysicalRolesRetainFittedPhaseAndActualPrefix(double wavelength, double highIndex, int cavities)
    {
        var d = GeneralCoatingTests.Fixture(wavelength, 1.45, highIndex, 0, 0, Core.Coatings.ThinFilmPolarization.Unpolarized);
        d = d with { Target = d.Target with { MaximumLayers = 39, Structure = new(cavities) } };
        var oldOrigin = new NarrowBandPrototype(Enumerable.Repeat(3, cavities + 1).ToArray(), Enumerable.Repeat(1, cavities).ToArray(), true);
        var next = oldOrigin with { MirrorLayers = Enumerable.Repeat(5, cavities + 1).ToArray(), CavityOrders = Enumerable.Repeat(2, cavities).ToArray() };
        var service = new DesignService(); var basis = service.BuildNarrowBandPrototype(d, oldOrigin);
        var actualPrefix = new FilmLayer(d.LowId, 17.123456789);
        var fitted = new[] { actualPrefix }.Concat(basis.Select(l => l with { ThicknessNm = l.ThicknessNm * 1.125 })).ToArray();
        var original = JsonSerializer.Serialize(fitted, Experiment.JsonOptions);
        var transferred = Transfer(d, fitted, oldOrigin, next, true)!;
        Assert.Equal(actualPrefix, transferred[0]);
        var nextBasis = service.BuildNarrowBandPrototype(d, next);
        Assert.Equal(nextBasis.Length + 1, transferred.Length);
        for (var i = 0; i < nextBasis.Length; i++)
        {
            Assert.Equal(nextBasis[i].MaterialId, transferred[i + 1].MaterialId);
            Assert.InRange(Math.Abs(transferred[i + 1].ThicknessNm / nextBasis[i].ThicknessNm - 1.125), 0, 1e-14);
        }
        Assert.Equal(fitted, Transfer(d, fitted, oldOrigin, oldOrigin, true));
        Assert.Equal(fitted.Skip(1), Transfer(d, fitted, oldOrigin, oldOrigin, false));
        Assert.Equal(original, JsonSerializer.Serialize(fitted, Experiment.JsonOptions));
    }

    [Fact]
    public void UnverifiedRolesAndFixedLayersCannotBeTransferred()
    {
        var d = GeneralCoatingTests.Fixture(550, 1.45, 2.3, 0, 0, Core.Coatings.ThinFilmPolarization.Unpolarized);
        d = d with { Target = d.Target with { MaximumLayers = 15, Structure = new(2) } };
        var origin = new NarrowBandPrototype([3, 3, 3], [1, 1], true);
        var layers = new DesignService().BuildNarrowBandPrototype(d, origin);
        Assert.Null(Transfer(d, layers.Select((l, i) => i == 0 ? l with { Variable = false } : l).ToArray(), origin, origin, true));
        Assert.Null(Transfer(d, layers.Select((l, i) => i == 0 ? l with { MaterialId = d.LowId } : l).ToArray(), origin, origin, true));
        Assert.Null(Transfer(d, layers, origin, origin with { CavityOrders = [1] }, true));
        Assert.Null(Transfer(d, layers, origin, origin with { MirrorLayers = [20, 20, 20] }, true));
    }

    [Fact]
    public async Task FormalSearchRecordsBoundedFeedbackAndReopensItsEvidence()
    {
        var d = GeneralCoatingTests.Fixture(550, 1.45, 2.3, 0, 0, Core.Coatings.ThinFilmPolarization.Unpolarized);
        d = d with { Target = d.Target with { MaximumLayers = 9, Structure = new(2) },
            Settings = d.Settings with { MaximumIterations = 2, Search = new(1, 12, true) } };
        var origin = new NarrowBandPrototype([1, 1, 1], [1, 1], true); var service = new DesignService();
        var layers = service.BuildNarrowBandPrototype(d, origin).Select(l => l with { ThicknessNm = l.ThicknessNm * 1.01 }).ToArray();
        d = d with { Layers = layers, Candidates = [new("saved actual fitted prescription", layers, 0, false) { OriginPrototype = origin }] };
        var fingerprint = d.Fingerprint(); var value = service.SearchMatchedFeedback(d); var feedback = value.Result!.Run.StructureFeedback!;
        Assert.Equal(2, value.Result.Run.StructureFeedbackWindows!.Length);
        Assert.Equal(new[] { 1, 2 }, value.Result.Run.StructureFeedbackWindows.Select(w => w.Round));
        var ordinary = service.Search(d);
        Assert.Null(ordinary.Result!.Run.StructureFeedback);
        Assert.Null(ordinary.Result.Run.StructureFeedbackWindows);
        Assert.Equal(ordinary.Result.Metrics, value.Result.Run.StructureFeedbackWindows[0].BaselineMetrics);
        Assert.Equal(ordinary.Layers, value.Result.Run.StructureFeedbackWindows[0].BaselineLayers);
        Assert.Equal(DesignService.StructureFeedbackProtocol, feedback.Protocol);
        Assert.InRange(feedback.CandidateEvaluations, 1, feedback.CandidateLimit);
        Assert.InRange(feedback.Screening.Length, 1, 1);
        Assert.All(feedback.Screening, s => Assert.InRange(s.Run.Iterations, 0, feedback.ShortIterationLimit));
        Assert.All(feedback.Matching, m => Assert.InRange(m.Run.Iterations, 0, Math.Min(32, d.Settings.MaximumIterations)));
        Assert.InRange(feedback.RefinementRun!.Iterations, 0, d.Settings.MaximumIterations);
        Assert.True(value.Result.Metrics.PeakTransmittance >= feedback.BaselineMetrics!.PeakTransmittance);
        Assert.True(Math.Abs(value.Result.Metrics.FwhmNanometers!.Value - d.Target.FwhmNm)
            <= Math.Abs(feedback.BaselineMetrics.FwhmNanometers!.Value - d.Target.FwhmNm));
        var od = d.Target.StopBands!.Select(b => value.Result.Spectrum.Where(s => s.WavelengthNanometers >= b.MinimumNm && s.WavelengthNanometers <= b.MaximumNm).Min(s => s.Power.OpticalDensity));
        Assert.All(od.Zip(feedback.BaselineBlockingOd!), pair => Assert.True(pair.First >= pair.Second));
        Assert.Equal(fingerprint, d.Fingerprint());
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".coating.json");
        try
        {
            await ExperimentStore.SaveAsync(value, path); var reopened = await ExperimentStore.OpenAsync(path);
            Assert.Equal(JsonSerializer.Serialize(feedback, Experiment.JsonOptions), JsonSerializer.Serialize(reopened.Result!.Run.StructureFeedback, Experiment.JsonOptions));
            Assert.Equal(value.Result.Metrics, reopened.Result.Metrics);
        }
        finally { File.Delete(path); }
    }
}

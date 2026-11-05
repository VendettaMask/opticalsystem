using OpticalSystemDesign.Validation;
using System.Text.Json;
using OpticalSystemDesign.CoatingDesign.Engine;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Materials;

namespace OpticalSystemDesign.CoatingDesign.Tests;

public sealed class DuplexCoatingTests
{
    [Fact]
    public void BothCoherentFacesMatch162IndependentMixedCoherenceReferences()
    {
        using var json = JsonDocument.Parse(EvidenceFiles.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/duplex-substrate-20261102.json")));
        var cases = json.RootElement.GetProperty("cases").EnumerateArray().ToArray(); Assert.Equal(162, cases.Length);
        foreach (var sample in cases)
        {
            static IMaterial M(JsonElement n) => new ConstantIndexMaterial("independent input", n[0].GetDouble(), n[1].GetDouble());
            CoherentFilm[] Films(string face) => sample.GetProperty(face).EnumerateArray().Select(l => new CoherentFilm(M(l.GetProperty("n")), l.GetProperty("d").GetDouble())).ToArray();
            var front = new CoherentThinFilmSolver(M(sample.GetProperty("n0")), M(sample.GetProperty("ns")), Films("front"));
            var back = new CoherentThinFilmSolver(M(sample.GetProperty("ne")), M(sample.GetProperty("ns")), Films("back"));
            var actual = front.EvaluateIncoherentSubstrate(back, sample.GetProperty("thickness").GetDouble(), sample.GetProperty("wavelength").GetDouble(),
                sample.GetProperty("angle").GetDouble(), sample.GetProperty("polarization").GetString() == "s" ? ThinFilmPolarization.S : ThinFilmPolarization.P);
            Assert.InRange(Math.Abs(actual.Reflectance - sample.GetProperty("r").GetDouble()), 0, 2e-11);
            Assert.InRange(Math.Abs(actual.Transmittance - sample.GetProperty("t").GetDouble()), 0, 2e-11);
            Assert.InRange(Math.Abs(actual.Absorptance - sample.GetProperty("a").GetDouble()), 0, 2e-11);
        }
    }

    internal static Experiment Design(CoatingDesignSurface optimize = CoatingDesignSurface.Both,
        CoatingEvaluationSurface evaluate = CoatingEvaluationSurface.WholePlate, bool symmetric = false)
    {
        var d = Examples.Create(DesignKind.Antireflection);
        var film = MaterialLibrary.Capture(new ConstantIndexMaterial("analytic AR film", Math.Sqrt(1.5)), "independent analytic control", 400, 800);
        var substrate = MaterialLibrary.Capture(new ConstantIndexMaterial("analytic glass", 1.5), "independent analytic control", 400, 800);
        return d with
        {
            SchemaVersion = Experiment.DuplexSchema,
            Duplex = new(optimize, evaluate, symmetric),
            Target = d.Target with { MinimumNm = 540, MaximumNm = 560, CenterNm = 550, Reflectance = .01, MaximumLayers = 4 },
            Settings = d.Settings with
            {
                PreviewIntervals = 40,
                OptimizationIntervals = 20,
                MaximumIterations = 15,
                DesignProtocolVersion = null,
                MinimumThicknessNm = 2,
                MaximumThicknessNm = 300,
                ToleranceTrials = 3,
                Search = new(2, 10, true)
            },
            SubstrateId = substrate.Id,
            LowId = film.Id,
            HighId = film.Id,
            Materials = [.. d.Materials, film, substrate],
            Layers = [new(film.Id, 60)],
            BackLayers = symmetric ? null : [new(film.Id, 170)],
            StackGeometry = new(d.IncidentId, 1)
        };
    }

    [Fact]
    public void DuplexReciprocityTotalReflectionAndOpaqueLogTransmissionRemainPhysical()
    {
        var air = new ConstantIndexMaterial("air", 1); var exit = new ConstantIndexMaterial("exit", 1.33);
        var glass = new ConstantIndexMaterial("glass", 1.52);
        var front = new CoherentThinFilmSolver(air, glass, [new(new ConstantIndexMaterial("high", 2.1), 75)]);
        var rear = new CoherentThinFilmSolver(exit, glass, [new(new ConstantIndexMaterial("low", 1.4), 65), new(new ConstantIndexMaterial("middle", 1.7), 95)]);
        var reverseAngle = Math.Asin(Math.Sin(35 * Math.PI / 180) / 1.33) * 180 / Math.PI;
        foreach (var pol in new[] { ThinFilmPolarization.S, ThinFilmPolarization.P, ThinFilmPolarization.Unpolarized })
        {
            var a = front.EvaluateIncoherentSubstrate(rear, 1e6, 550, 35, pol);
            var b = rear.EvaluateIncoherentSubstrate(front, 1e6, 550, reverseAngle, pol);
            Assert.Equal(a.Transmittance, b.Transmittance, 12); Assert.InRange(Math.Abs(a.Absorptance), 0, 2e-11);
        }
        var denseIncident = new ConstantIndexMaterial("dense", 1.5); var denseGlass = new ConstantIndexMaterial("dense glass", 1.7);
        var tirFront = new CoherentThinFilmSolver(denseIncident, denseGlass, []);
        var tirRear = new CoherentThinFilmSolver(air, denseGlass, [new(new ConstantIndexMaterial("film", 1.8), 70)]);
        var tir = tirFront.EvaluateIncoherentSubstrate(tirRear, 1e6, 550, 57, ThinFilmPolarization.P);
        Assert.Equal(1, tir.Reflectance, 12); Assert.Equal(0, tir.Transmittance); Assert.True(double.IsNegativeInfinity(tir.LogTransmittance));
        var lossy = new ConstantIndexMaterial("lossy glass", 1.5, .1);
        var opaque = new CoherentThinFilmSolver(air, lossy, []).EvaluateIncoherentSubstrate(
            new CoherentThinFilmSolver(exit, lossy, [new(new ConstantIndexMaterial("absorbing film", 1.8, .03), 90)]), 1e6, 550, 35, ThinFilmPolarization.S);
        Assert.Equal(0, opaque.Transmittance); Assert.True(double.IsFinite(opaque.LogTransmittance)); Assert.True(opaque.LogTransmittance < -1000);
        var films = Enumerable.Range(0, 200).Select(i =>
        {
            var index = i % 2 == 0 ? 3.0 : 1.4;
            return new CoherentFilm(new ConstantIndexMaterial("lossless alternating material", index), 550 / (4 * index));
        }).ToArray();
        var mirror = new CoherentThinFilmSolver(air, glass, films);
        var single = mirror.Evaluate(550, 0, ThinFilmPolarization.S);
        var doubleMirror = mirror.EvaluateIncoherentSubstrate(mirror, 1e6, 550, 0, ThinFilmPolarization.S);
        Assert.Equal(single.LogTransmittance - Math.Log(2 - single.Transmittance), doubleMirror.LogTransmittance, 10);
        Assert.Equal(1, doubleMirror.Reflectance, 12);
    }

    [Theory]
    [InlineData(CoatingDesignSurface.Front, CoatingEvaluationSurface.Front)]
    [InlineData(CoatingDesignSurface.Back, CoatingEvaluationSurface.Back)]
    [InlineData(CoatingDesignSurface.Both, CoatingEvaluationSurface.WholePlate)]
    public void FormalDisplayAndPolarizationSamplingUseExactlyTheSelectedPhysicalObject(CoatingDesignSurface optimize, CoatingEvaluationSurface evaluate)
    {
        var d = Design(optimize, evaluate); var service = new DesignService();
        var result = service.Evaluate(d); var polarization = service.EvaluateDisplayPolarizations(result);
        Assert.Equal(result.Result!.Spectrum, polarization.Average);
        var materials = d.Materials.ToDictionary(m => m.Id, m => m.Resolve(new MaterialRegistry()));
        var front = new CoherentThinFilmSolver(materials[d.IncidentId], materials[d.SubstrateId], d.Layers.Select(l => new CoherentFilm(materials[l.MaterialId], l.ThicknessNm)));
        var rear = new CoherentThinFilmSolver(materials[d.StackGeometry!.ExitMediumId], materials[d.SubstrateId], d.EffectiveBackLayers.Select(l => new CoherentFilm(materials[l.MaterialId], l.ThicknessNm)));
        foreach (var spectrum in service.EvaluateAnalysisDisplay(d, new(540, 560, 5, [0, 35])))
            foreach (var sample in spectrum.Spectrum.P)
            {
                var expected = evaluate == CoatingEvaluationSurface.Front ? front.Evaluate(sample.WavelengthNanometers, spectrum.AngleDegrees, ThinFilmPolarization.P)
                    : evaluate == CoatingEvaluationSurface.Back ? rear.Evaluate(sample.WavelengthNanometers, spectrum.AngleDegrees, ThinFilmPolarization.P)
                    : front.EvaluateIncoherentSubstrate(rear, 1e6, sample.WavelengthNanometers, spectrum.AngleDegrees, ThinFilmPolarization.P);
                Assert.Equal(expected, sample.Power);
            }
        Assert.Equal(d.Fingerprint(), result.Result.Fingerprint);
    }

    [Fact]
    public void JointOptimizationMovesBothFacesAndPreservesFixedAndInactiveLayers()
    {
        var service = new DesignService(); var d = Design(); var initial = service.Evaluate(d);
        var optimized = service.Optimize(d);
        Assert.NotEqual(d.Layers[0].ThicknessNm, optimized.Layers[0].ThicknessNm);
        Assert.NotEqual(d.BackLayers![0].ThicknessNm, optimized.BackLayers![0].ThicknessNm);
        Assert.True(optimized.Result!.Metrics.MaximumReflectance < initial.Result!.Metrics.MaximumReflectance / 3);
        Assert.Equal(2, optimized.Result.Run.Duplex!.IndependentVariables);
        var backOnly = service.Optimize(d with { Duplex = new(CoatingDesignSurface.Back, CoatingEvaluationSurface.WholePlate) });
        Assert.Equal(d.Layers, backOnly.Layers); Assert.NotEqual(d.BackLayers[0].ThicknessNm, backOnly.BackLayers![0].ThicknessNm);
        var fixedFront = d with { Layers = [d.Layers[0] with { Variable = false }] };
        var fixedResult = service.Optimize(fixedFront);
        Assert.Equal(fixedFront.Layers, fixedResult.Layers); Assert.Equal(1, fixedResult.Result!.Run.Duplex!.IndependentVariables);
    }

    [Fact]
    public void SymmetricFacesUseOneVariableAndManufacturingCorrelationRemainsExplicit()
    {
        var service = new DesignService(); var d = Design(symmetric: true); var optimized = service.Optimize(d);
        Assert.Null(optimized.BackLayers); Assert.Equal(optimized.Layers, optimized.EffectiveBackLayers);
        Assert.Equal(1, optimized.Result!.Run.Duplex!.IndependentVariables);
        var independent = service.Tolerance(optimized);
        Assert.Contains(independent.Samples!, trial => !trial.ThicknessNm.SequenceEqual(trial.BackThicknessNm!));
        var common = service.Tolerance(optimized with { Settings = optimized.Settings with { Tolerancing = new(Correlation: ThicknessCorrelation.Common) } });
        Assert.All(common.Samples!, trial => Assert.Equal(trial.ThicknessNm, trial.BackThicknessNm));
    }

    [Fact]
    public async Task DuplexVersionFingerprintsAndStoredCandidatesRetainBothFacesAndRejectStaleResults()
    {
        var service = new DesignService(); var result = service.Optimize(Design());
        var path = Path.Combine(Path.GetTempPath(), "duplex-" + Guid.NewGuid() + ".coating.json");
        var exportDirectory = Path.Combine(Path.GetTempPath(), "duplex-export-" + Guid.NewGuid());
        try
        {
            await ExperimentStore.SaveAsync(result, path); var opened = await ExperimentStore.OpenAsync(path);
            Assert.Equal(result.Fingerprint(), opened.Fingerprint()); Assert.Equal(result.BackLayers, opened.BackLayers);
            Assert.Equal(result.Result!.Spectrum, opened.Result!.Spectrum);
            Assert.Equal(result.Candidates![^1].BackLayers, opened.Candidates![^1].BackLayers);
            var changed = result with { BackLayers = [result.BackLayers![0] with { ThicknessNm = 70 }] };
            Assert.NotEqual(result.Fingerprint(), changed.Fingerprint());
            await Assert.ThrowsAsync<InvalidDataException>(() => ExperimentStore.SaveAsync(changed, path));
            Assert.NotEqual(result.Fingerprint(), (result with { StackGeometry = result.StackGeometry! with { SubstrateThicknessMm = 2 } }).Fingerprint());
            Assert.Throws<InvalidDataException>(() => (result with { SchemaVersion = 1 }).Validate());
            Assert.Throws<InvalidDataException>(() => (result with { Duplex = result.Duplex! with { Version = 99 } }).Validate());
            Assert.Throws<InvalidDataException>(() => (result with { Duplex = new(CoatingDesignSurface.Both, CoatingEvaluationSurface.Front) }).Validate());
            await ExperimentStore.ExportAsync(result, exportDirectory);
            Assert.Equal(2, EvidenceFiles.ReadAllLines(Path.Combine(exportDirectory, "back-layers.csv")).Length);
            Assert.Contains("WholePlate", EvidenceFiles.ReadAllText(Path.Combine(exportDirectory, "evaluation.json")));
        }
        finally { File.Delete(path); if (EvidenceFiles.DirectoryExists(exportDirectory)) Directory.Delete(exportDirectory, true); }
    }

    [Fact]
    public void DuplexGenerationAndSearchKeepFixedFaceAndAttachBothFacesToEveryCandidate()
    {
        var service = new DesignService(); var d = Design() with { Settings = Design().Settings with { MaximumIterations = 2 } };
        d = d with { Layers = [d.Layers[0] with { Variable = false }] };
        foreach (var result in new[] { service.Generate(d), service.Search(d) })
        {
            Assert.Equal(d.Layers, result.Layers); Assert.NotNull(result.Result);
            Assert.All(result.Candidates!, candidate => { Assert.Equal(d.Layers, candidate.Layers); Assert.NotNull(candidate.BackLayers); });
        }
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => service.Optimize(d, token: cancellation.Token));
    }
}

using System.Text.Json;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Optimization;

namespace OpticalSystemDesign.CoatingDesign.Tests;

public sealed class GeneralCoatingDerivativeTests
{
    [Fact]
    public void StableAdjointAgreesWithTheIndependentPinnedAnalyticReferenceAcrossWavesAnglesAndLosses()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/tmmcore-sensitivity-0.4.2.json")));
        foreach (var c in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            static IMaterial Material(JsonElement v) => new ConstantIndexMaterial("independent index", v[0].GetDouble(), v[1].GetDouble());
            var films = c.GetProperty("layers").EnumerateArray().Select(l => new CoherentFilm(Material(l.GetProperty("n")), l.GetProperty("d").GetDouble())).ToArray();
            var solver = new CoherentThinFilmSolver(Material(c.GetProperty("n0")), Material(c.GetProperty("ns")), films);
            var wave = c.GetProperty("wave").GetDouble(); var angle = c.GetProperty("angle").GetDouble();
            var pol = c.GetProperty("pol").GetString() == "s" ? ThinFilmPolarization.S : ThinFilmPolarization.P;
            var actual = solver.EvaluateThicknessSensitivity(wave, angle, pol); var expected = c.GetProperty("reference");
            Assert.Equal(solver.Evaluate(wave, angle, pol), actual.Power);
            for (var i = 0; i < films.Length; i++)
            {
                Assert.InRange(Math.Abs(actual.Layers[i].Reflectance - expected.GetProperty("dRdd")[i].GetDouble()), 0, 2e-11);
                Assert.InRange(Math.Abs(actual.Layers[i].Transmittance - expected.GetProperty("dTdd")[i].GetDouble()), 0, 2e-11);
                Assert.InRange(Math.Abs(actual.Layers[i].Absorptance - expected.GetProperty("dAdd")[i].GetDouble()), 0, 2e-11);
            }
        }
    }

    [Theory]
    [InlineData(450, 0, ThinFilmPolarization.Unpolarized)]
    [InlineData(900, 33, ThinFilmPolarization.S)]
    [InlineData(1500, 57, ThinFilmPolarization.P)]
    [InlineData(700, 42, ThinFilmPolarization.Unpolarized)]
    public void AdjointIncludesPhysicalThicknessMultiplierAndMatchesIndependentCentralDifferences(double wave, double angle, ThinFilmPolarization pol)
    {
        var films = new[] { new CoherentFilm(new ConstantIndexMaterial("A", 2.2, .02), wave * .17, new(Multiplier: 1.3)),
            new CoherentFilm(new ConstantIndexMaterial("B", 1.45, .003), wave * .23) };
        CoherentThinFilmSolver Solver(CoherentFilm[] layers) => new(new AirMaterial(), new ConstantIndexMaterial("substrate", 1.52), layers);
        var response = Solver(films).EvaluateThicknessSensitivity(wave, angle, pol); var h = wave * 1e-6;
        for (var i = 0; i < films.Length; i++)
        {
            var lo = films.ToArray(); var hi = films.ToArray();
            lo[i] = lo[i] with { ThicknessNanometers = lo[i].ThicknessNanometers - h };
            hi[i] = hi[i] with { ThicknessNanometers = hi[i].ThicknessNanometers + h };
            var a = Solver(lo).Evaluate(wave, angle, pol); var b = Solver(hi).Evaluate(wave, angle, pol);
            Assert.InRange(Math.Abs((b.Reflectance - a.Reflectance) / (2 * h) - response.Layers[i].Reflectance), 0, 1e-8);
            Assert.InRange(Math.Abs((b.Transmittance - a.Transmittance) / (2 * h) - response.Layers[i].Transmittance), 0, 1e-8);
            Assert.InRange(Math.Abs((b.LogTransmittance - a.LogTransmittance) / (2 * h) - response.Layers[i].LogTransmittance), 0, 1e-8);
        }
    }

    [Fact]
    public void OpaqueFilmRetainsFiniteLogDerivativeAndEvanescentExitReportsItsUnsupportedLogDerivative()
    {
        var opaque = new CoherentThinFilmSolver(new AirMaterial(), new ConstantIndexMaterial("S", 1.5),
            [new(new ConstantIndexMaterial("absorber", .3, 4), 100000)]).EvaluateThicknessSensitivity(600, 0, ThinFilmPolarization.Unpolarized);
        Assert.Equal(0, opaque.Power.Transmittance); Assert.True(opaque.HasLogTransmissionDerivative);
        Assert.Equal(-4 * Math.PI * 4 / 600, opaque.Layers[0].LogTransmittance, 11);
        var tir = new CoherentThinFilmSolver(new ConstantIndexMaterial("incident", 1.7), new AirMaterial(),
            [new(new ConstantIndexMaterial("film", 1.6), 100)]).EvaluateThicknessSensitivity(600, 65, ThinFilmPolarization.S);
        Assert.False(tir.HasLogTransmissionDerivative); Assert.Equal(0, tir.Power.Transmittance);
        Assert.InRange(Math.Abs(tir.Layers[0].Reflectance), 0, 1e-12);
    }

    [Fact]
    public void AdaptiveGuidePreservesAnchorsAndReportsAnUnfinishedPointBudget()
    {
        var solver = new CoherentThinFilmSolver(new AirMaterial(), new ConstantIndexMaterial("S", 1.5),
            [new(new ConstantIndexMaterial("H", 2.2), 300), new(new ConstantIndexMaterial("L", 1.4), 200)]);
        var limited = AdaptiveThinFilmSpectrum.Sample(solver, [450, 550, 700], 15, ThinFilmPolarization.P, 1e-8, 3);
        Assert.True(limited.LimitReached); Assert.Equal(3, limited.Evaluations);
        var sampled = AdaptiveThinFilmSpectrum.Sample(solver, [450, 550, 700], 15, ThinFilmPolarization.P, 1e-8, 25);
        Assert.Equal(sampled.Samples.Length, sampled.Evaluations); Assert.InRange(sampled.Evaluations, 4, 25);
        foreach (var wave in new[] { 450, 550, 700 }) Assert.Equal(solver.Evaluate(wave, 15, ThinFilmPolarization.P), sampled.Samples.Single(s => s.WavelengthNanometers == wave).Power);
        Assert.True(sampled.MaximumObservedChordError > 0);
    }

    [Fact]
    public void BoundActiveSetKeepsAnOutwardVariableAtItsBoundWhileImprovingFreeVariables()
    {
        var x = 0.0; var y = 0.0; var problem = new OptimizationProblem(); problem.DisableBatching(); problem.EnableBoundActiveSet();
        problem.AddVariable(new DelegateVariable("bounded", () => x, v => x = v, 0, 5));
        problem.AddVariable(new DelegateVariable("free", () => y, v => y = v, -5, 5));
        problem.AddOperand(new Operand("outside target", -1, 1, () => x));
        problem.AddOperand(new Operand("reachable target", 2, 1, () => y));
        problem.SetValueJacobianEvaluator(_ => new[,] { { 1.0, 0 }, { 0, 1.0 } });
        OptimizerCatalog.Create("Damped Least Squares").Optimize(problem, 12);
        Assert.Equal(0, x); Assert.InRange(Math.Abs(y - 2), 0, 1e-6);
        var scale = CoherentThinFilmSolver.CharacteristicThickness(new ConstantIndexMaterial("evanescent", 1.1), new ConstantIndexMaterial("incident", 1.7), 600, 65);
        Assert.True(double.IsFinite(scale) && scale > 0);
    }

    [Fact]
    public void OptionalAnalyticValueJacobianRespectsWeightsFixedVariablesAndTheLegacyFallback()
    {
        var x = 2.0; var y = 7.0; var problem = new OptimizationProblem(); problem.DisableBatching();
        problem.AddVariable(new DelegateVariable("free", () => x, v => x = v, 0, 10));
        problem.AddVariable(new DelegateVariable("fixed", () => y, v => y = v, 7, 7));
        problem.AddOperand(new Operand("quadratic", 9, 2, () => x * x));
        problem.SetValueJacobianEvaluator(v => new[,] { { 2 * v[0], 100.0 } });
        var report = OptimizerCatalog.Create("Damped Least Squares").Optimize(problem, 24);
        Assert.InRange(Math.Abs(x - 3), 0, 1e-6); Assert.Equal(7, y); Assert.True(problem.AnalyticJacobianEvaluations > 0);
        x = 2; problem.SetValueJacobianEvaluator(_ => null);
        var count = problem.AnalyticJacobianEvaluations;
        OptimizerCatalog.Create("Damped Least Squares").Optimize(problem, 24);
        Assert.InRange(Math.Abs(x - 3), 0, 1e-6); Assert.Equal(count, problem.AnalyticJacobianEvaluations);
        problem.SetValueJacobianEvaluator(_ => new double[2, 1]);
        Assert.Throws<InvalidOperationException>(() => OptimizerCatalog.Create("Damped Least Squares").Optimize(problem, 2));
    }
}

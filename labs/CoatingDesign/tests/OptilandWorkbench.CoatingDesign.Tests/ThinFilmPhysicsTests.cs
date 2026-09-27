using System.Text.Json;
using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Materials;

namespace OptilandWorkbench.CoatingDesign.Tests;

public sealed class ThinFilmPhysicsTests
{
    private static ConstantIndexMaterial N(double n, double k = 0) => new("test", n, k);
    [Fact]
    public void FixedUpstreamReferenceMatchesAllCases()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "tmmcore-0.4.2.json")));
        foreach (var c in json.RootElement.GetProperty("cases").EnumerateArray())
        {
            static IMaterial Material(JsonElement a) => N(a[0].GetDouble(), a[1].GetDouble());
            var solver = new CoherentThinFilmSolver(Material(c.GetProperty("n0")), Material(c.GetProperty("ns")),
                c.GetProperty("layers").EnumerateArray().Select(l => new CoherentFilm(Material(l.GetProperty("n")), l.GetProperty("d").GetDouble())));
            var result = solver.Evaluate(c.GetProperty("lambda").GetDouble(), c.GetProperty("angle").GetDouble(),
                c.GetProperty("pol").GetString() == "s" ? ThinFilmPolarization.S : ThinFilmPolarization.P);
            var expected = c.GetProperty("expected");
            Assert.True(Math.Abs(result.Reflectance - expected.GetProperty("R").GetDouble()) < 2e-11, c.GetProperty("name").GetString());
            Assert.InRange(Math.Abs(result.Transmittance - expected.GetProperty("T").GetDouble()), 0, 2e-11);
            Assert.InRange(Math.Abs(result.Absorptance - expected.GetProperty("A").GetDouble()), 0, 2e-11);
        }
    }

    [Fact]
    public void BareFresnelAndBrewsterAnalyticChecks()
    {
        var solver = new CoherentThinFilmSolver(N(1), N(1.5), []);
        Assert.Equal(0.04, solver.Evaluate(550, 0, ThinFilmPolarization.S).Reflectance, 13);
        Assert.InRange(solver.Evaluate(550, Math.Atan(1.5) * 180 / Math.PI, ThinFilmPolarization.P).Reflectance, 0, 1e-28);
        var theta = 40 * Math.PI / 180;
        var theta2 = Math.Asin(Math.Sin(theta) / 1.5);
        var rs = Math.Pow((Math.Cos(theta) - 1.5 * Math.Cos(theta2)) / (Math.Cos(theta) + 1.5 * Math.Cos(theta2)), 2);
        Assert.Equal(rs, solver.Evaluate(550, 40, ThinFilmPolarization.S).Reflectance, 13);
    }

    [Fact]
    public void SingleQuarterWaveCancelsAndZeroThicknessIsIdentity()
    {
        var quarter = new CoherentThinFilmSolver(N(1), N(1.52), [new(N(Math.Sqrt(1.52)), 550 / (4 * Math.Sqrt(1.52)))]);
        var power = quarter.Evaluate(550, 0, ThinFilmPolarization.Unpolarized);
        Assert.InRange(power.Reflectance, 0, 1e-28);
        Assert.Equal(1, power.Transmittance, 13);
        var zero = new CoherentThinFilmSolver(N(1), N(1.5), [new(N(3, 2), 0)]);
        Assert.Equal(0.04, zero.Evaluate(550, 0, ThinFilmPolarization.S).Reflectance, 13);
    }

    [Theory]
    [InlineData(0)] [InlineData(35)] [InlineData(70)]
    public void LosslessPowerAndUnpolarizedAverage(double angle)
    {
        var solver = new CoherentThinFilmSolver(N(1), N(1.5), Enumerable.Range(0, 120).Select(i => new CoherentFilm(N(i % 2 == 0 ? 2.3 : 1.45), 63 + i % 9)));
        var s = solver.Evaluate(670, angle, ThinFilmPolarization.S);
        var p = solver.Evaluate(670, angle, ThinFilmPolarization.P);
        var u = solver.Evaluate(670, angle, ThinFilmPolarization.Unpolarized);
        Assert.InRange(Math.Abs(1 - s.Reflectance - s.Transmittance), 0, 2e-12);
        Assert.InRange(Math.Abs(1 - p.Reflectance - p.Transmittance), 0, 2e-12);
        Assert.Equal((s.Reflectance + p.Reflectance) / 2, u.Reflectance, 13);
    }

    [Fact]
    public void HighReflectorMatchesQuarterWaveAnalyticSolution()
    {
        const int pairs = 12;
        var solver = new CoherentThinFilmSolver(N(1), N(1.52), Enumerable.Range(0, 2 * pairs)
            .Select(i => new CoherentFilm(N(i % 2 == 0 ? 2.2 : 1.45), 550 / (4 * (i % 2 == 0 ? 2.2 : 1.45)))));
        var admittance = 1.52 * Math.Pow(2.2 / 1.45, 2 * pairs);
        Assert.Equal(Math.Pow((1 - admittance) / (1 + admittance), 2), solver.Evaluate(550, 0, ThinFilmPolarization.S).Reflectance, 12);
    }

    [Fact]
    public void ThickAbsorberAndThousandLayersRemainFiniteWithoutClippingTransmission()
    {
        var metal = new CoherentThinFilmSolver(N(1), N(1.5), [new(N(0.2, 4), 100000)]).Evaluate(550, 10, ThinFilmPolarization.P);
        Assert.True(double.IsFinite(metal.LogTransmittance));
        Assert.True(metal.OpticalDensity > 1000);
        Assert.True(metal.Absorptance > 0);
        var many = new CoherentThinFilmSolver(N(1), N(1.5), Enumerable.Range(0, 2000).Select(i => new CoherentFilm(N(i % 2 == 0 ? 2.2 : 1.45), 70)))
            .Evaluate(550, 20, ThinFilmPolarization.S);
        Assert.True(double.IsFinite(many.LogTransmittance));
        Assert.True(many.Reflectance > 0.999);
    }

    [Fact]
    public void TotalInternalReflectionAndPassiveInputValidation()
    {
        var tir = new CoherentThinFilmSolver(N(1.5), N(1), []).Evaluate(550, 60, ThinFilmPolarization.P);
        Assert.Equal(1, tir.Reflectance, 13);
        Assert.Equal(0, tir.Transmittance);
        Assert.Throws<ArgumentException>(() => new CoherentThinFilmSolver(N(1), N(1.5), [new(N(2), -1)]));
        Assert.Throws<ArgumentException>(() => new CoherentThinFilmSolver(N(1), N(2, -1), []).Evaluate(550, 0, ThinFilmPolarization.S));
        Assert.Throws<NotSupportedException>(() => new CoherentThinFilmSolver(N(1, 0.1), N(1.5), []).Evaluate(550, 0, ThinFilmPolarization.S));
    }
}

using OptilandWorkbench.InitialStructure.Engine;
using OptilandWorkbench.InitialStructure.Engine.Optimization;

namespace OptilandWorkbench.InitialStructure.Tests;

public sealed class FlatStartSolverCoordinatesTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndependentBoxCornersRespectMinimumThicknessAndCoupledTrack(bool fixedBack)
    {
        var spec = FlatStartDesignTests.LoadSpecification(fixedBack ? "02-35mm-fixed-back.json" : "03-50mm-monochrome.json");
        var coordinates = new FlatStartSolverCoordinates(spec, 3, 2);
        foreach (var fraction in new[] { 0.0, .1, .7, 1.0 })
        {
            var point = coordinates.Lower.Select((lower, index) => lower + fraction * (coordinates.Upper[index] - lower)).ToArray();
            var physical = coordinates.Decode(point);
            var total = physical.Skip(6).Sum() * spec.EffectiveFocalLengthMillimeters + (spec.FlatStart!.FixedBackFocusMillimeters ?? 0);
            Assert.InRange(total, 0, spec.MaximumTrackLengthMillimeters + 1e-12);
            for (var index = 6; index < physical.Length; index++)
            {
                var minimum = index == 11 ? spec.MinimumBackFocusMillimeters
                    : index % 2 == 0 ? spec.MinimumCenterThicknessMillimeters : spec.MinimumAirGapMillimeters;
                Assert.True(physical[index] * spec.EffectiveFocalLengthMillimeters >= minimum - 1e-12);
            }
            var roundTrip = coordinates.Decode(coordinates.Encode(physical));
            Assert.All(physical.Zip(roundTrip), pair => Assert.Equal(pair.First, pair.Second, 12));
        }
    }

    [Fact]
    public void DifferentialIncludesTrackCouplingInsteadOfSilentlyProjectingOtherCoordinates()
    {
        var spec = FlatStartDesignTests.LoadSpecification("03-50mm-monochrome.json");
        var map = new FlatStartSolverCoordinates(spec, 3, 2);
        var point = new double[12];
        point[6] = .2;
        point[7] = .5;
        point[11] = 1;
        var before = map.Decode(point);
        point[6] += 1e-5;
        var after = map.Decode(point);
        Assert.True(after[6] > before[6]);
        Assert.True(after[7] < before[7]);
        Assert.Equal(spec.MaximumTrackLengthMillimeters, after.Skip(6).Sum() * spec.EffectiveFocalLengthMillimeters, 10);
        // This is a real coordinate mapping: perturbing column 6 consistently
        // changes all affected residuals, instead of dividing by a projected x6.
        var solved = BoundedTrustRegionLeastSquares.Solve(new[] { .2 }, new[] { 0.0 }, new[] { 1.0 },
            (x, _) => { point[6] = x[0]; return new(new[] { map.Decode(point)[7] - before[7] }); });
        Assert.Equal(.2, solved.Variables[0], 8);
    }

    [Fact]
    public void AcceptedObserverNeverPublishesFiniteDifferenceOrRejectedTrials()
    {
        var observed = new List<double>();
        var result = BoundedTrustRegionLeastSquares.Solve(new[] { .01 }, new[] { -10.0 }, new[] { 10.0 },
            (x, _) => new(new[] { x[0] * x[0] - 1 }), new() { MaximumEvaluations = 100 },
            acceptedStep: (x, _) => observed.Add(x[0]));
        Assert.Equal(result.Trials.Where(trial => trial.Accepted).Select(trial => trial.TrialPoint[0]), observed);
        Assert.Equal(result.Variables[0], observed[^1]);
    }
}

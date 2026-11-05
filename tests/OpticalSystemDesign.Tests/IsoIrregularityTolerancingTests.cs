using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Tolerancing;

namespace OpticalSystemDesign.Tests;

public sealed class IsoIrregularityTolerancingTests
{
    [Fact]
    public void ContinuousPeakToValleyIncludesInteriorExtremaAndRepeatedRoots()
    {
        // Q = (t - 0.4)^4 has a flat interior minimum.
        Assert.Equal(Math.Pow(.6, 4), IsoSurfaceFigure.RadialPeakToValley([.0256, -.256, .96, -1.6, 1], 0), 13);
        // With m=2 the envelope is t(1-t); with m=1 it is sqrt(t)(1-t).
        Assert.Equal(.5, IsoSurfaceFigure.RadialPeakToValley([1, -1], 2), 14);
        Assert.Equal(4 / (3 * Math.Sqrt(3)), IsoSurfaceFigure.RadialPeakToValley([1, -1], 1), 14);
    }

    [Theory]
    [InlineData(IsoSurfaceFormUnit.Nanometers, 632.8)]
    [InlineData(IsoSurfaceFormUnit.Micrometers, .6328)]
    [InlineData(IsoSurfaceFormUnit.Fringes, 2)]
    public void IsocMatchesIndependentRadialShapeAndSignedPeakToValley(IsoSurfaceFormUnit unit, double maximum)
    {
        var (optic, surface) = Plane();
        var nominal = surface.Geometry;
        var operand = Make(SurfaceFormMode.IsoRotationalIrregularityPv, maximum, unit);
        const double pv = .0006328;
        operand.ApplyMaximum(optic);
        var positive = surface.Geometry;
        Assert.Equal(0, positive.Sag(0, 0), 14);
        Assert.InRange(DensePeakToValley(positive, 0), pv * (1 - 1e-6), pv * (1 + 1e-11));

        // Endpoint shape is proportional to the four public Standard radial terms.
        var t = .38;
        var r = 6 * Math.Sqrt(t);
        var predictedRatio = IsocEndpointProfile(t) / IsocEndpointProfile(1);
        Assert.Equal(predictedRatio, positive.Sag(r, 0) / positive.Sag(6, 0), 12);
        Assert.Equal(positive.Sag(r, 0), positive.Sag(0, r), 14);
        var normal = positive.SurfaceNormal(new Vector3D(r, 0, positive.Sag(r, 0)));
        const double step = 1e-5;
        Assert.Equal(-(positive.Sag(r + step, 0) - positive.Sag(r - step, 0)) / (2 * step),
            normal.X / normal.Z, 11);
        var hit = positive.DistanceToIntersection(new Vector3D(r, 0, -1), new Vector3D(0, 0, 1));
        Assert.True(hit.IsHit);
        Assert.Equal(1 + positive.Sag(r, 0), hit.Distance, 12);
        operand.Revert(optic);
        Assert.Same(nominal, surface.Geometry);
        operand.ApplyScaledMinimum(optic, .35);
        Assert.Equal(-.35 * positive.Sag(r, 0), surface.Geometry.Sag(r, 0), 14);
        operand.Revert(optic);
        Assert.Same(nominal, surface.Geometry);
    }

    [Fact]
    public void IsocMonteCarloAtMaximumUsesDistinctRadialShapes()
    {
        var (optic, surface) = Plane();
        var operand = Make(SurfaceFormMode.IsoRotationalIrregularityPv, 1200);
        var random = new Random(43);
        var sag = new HashSet<double>();
        for (var trial = 0; trial < 12; trial++)
        {
            operand.Apply(optic, random);
            Assert.InRange(DensePeakToValley(surface.Geometry, 0), .0012 * (1 - 1e-6), .0012 * (1 + 1e-11));
            Assert.Equal(0, surface.Geometry.Sag(0, 0), 14);
            Assert.True(sag.Add(surface.Geometry.Sag(3, 0)));
            operand.Revert(optic);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void IsobEachAngularFamilyMatchesContinuousDiskPeakToValley(int order)
    {
        var (optic, surface) = Plane();
        var operand = Make(SurfaceFormMode.IsoIrregularityPv, 1600);
        var random = new AngularFamilyRandom(order);
        operand.Apply(optic, random);
        const double expected = .0016;
        Assert.InRange(DensePeakToValley(surface.Geometry, order), expected * (1 - 1e-6), expected * (1 + 1e-11));
        Assert.Equal(0, surface.Geometry.Sag(0, 0), 14);
        if (order > 0)
        {
            var theta = Math.PI / order;
            Assert.Equal(-surface.Geometry.Sag(3, 0),
                surface.Geometry.Sag(3 * Math.Cos(theta), 3 * Math.Sin(theta)), 14);
        }
    }

    [Fact]
    public void IsodSeparatesCenteredRmsFromVertexDatumAndUsesPositiveTruncatedGaussian()
    {
        var (optic, surface) = Plane();
        const double expected = .0012;
        var exact = Make(SurfaceFormMode.IsoIrregularityRms, 1.2, IsoSurfaceFormUnit.Micrometers);
        exact.ApplyMaximum(optic);
        Assert.Equal(0, surface.Geometry.Sag(0, 0), 14);
        var (mean, rms, raw) = DiskStatistics(surface.Geometry);
        Assert.Equal(expected, rms, 14);
        Assert.True(raw > rms);
        Assert.Equal(mean * mean + rms * rms, raw * raw, 15);
        exact.Revert(optic);

        var fixedRandom = new Random(19);
        for (var trial = 0; trial < 12; trial++)
        {
            exact.Apply(optic, fixedRandom);
            Assert.Equal(expected, DiskStatistics(surface.Geometry).Rms, 14);
            exact.Revert(optic);
        }

        var sampled = Make(SurfaceFormMode.IsoIrregularityRms, 1.2, IsoSurfaceFormUnit.Micrometers,
            IsoSurfaceFormStatistics.TruncatedGaussian);
        var random = new Random(32910);
        var fractions = new double[384];
        for (var trial = 0; trial < fractions.Length; trial++)
        {
            sampled.Apply(optic, random);
            fractions[trial] = DiskStatistics(surface.Geometry).Rms / expected;
            Assert.InRange(fractions[trial], double.Epsilon, 1 - 1e-14);
            sampled.Revert(optic);
        }
        var center = fractions.Average();
        Assert.InRange(center, .475, .525);
        Assert.InRange(Math.Sqrt(fractions.Average(value => Math.Pow(value - center, 2))), .105, .145);
        Assert.Equal(fractions.Length, fractions.Distinct().Count());
    }

    [Fact]
    public async Task IsoIrregularitiesUseTheRealMtfWorkersAndPreserveTheNominalPrescription()
    {
        using var app = WorkbenchApplication.Create("cooke");
        var before = app.Prescription.GetSurfaces();
        var revision = app.Events.Revision;
        var request = new TolerancingRequestDto(1, 0, 0, 4, 82, 0,
            [new(1, true, ToleranceOperandKind.IsoIrregularityPv, 1, -1500, 1500),
             new(2, true, ToleranceOperandKind.IsoRotationalIrregularityPv, 2, -2, 2,
                 IsoUnit: IsoSurfaceFormUnit.Fringes, TestWavelengthMicrometers: .5461,
                 IsoStatistics: IsoSurfaceFormStatistics.TruncatedGaussian),
             new(3, true, ToleranceOperandKind.IsoIrregularityRms, 3, -1.5, 1.5, IsoUnit: IsoSurfaceFormUnit.Micrometers)],
            ToleranceCriterion.Mtf, MaxDegreeOfParallelism: 1,
            MtfSettings: new(20, Method: ToleranceMtfMethod.Geometric));

        Assert.True(app.Tolerancing.ValidateStudy(request).IsValid);
        var single = await app.Tolerancing.RunAsync(request);
        var multi = await app.Tolerancing.RunAsync(request with { MaxDegreeOfParallelism = 2 });
        Assert.Equal(3, single.SensitivityRows.Count);
        Assert.All(single.SensitivityRows, row =>
        {
            Assert.NotEqual("0", row.DeltaMerit);
            Assert.All(row.NegativeFields!.Concat(row.PositiveFields!), field => Assert.Empty(field.Error));
        });
        Assert.Equal(single.TrialRows.Select(trial => trial.CriterionValue), multi.TrialRows.Select(trial => trial.CriterionValue));
        Assert.Equal(4, single.TrialRows.Select(trial => trial.CriterionValue).Distinct().Count());

        var inverse = await app.Tolerancing.RunAsync(request with
        {
            Criterion = ToleranceCriterion.RmsSpotRadius, Mode = ToleranceAnalysisMode.InverseIncrement,
            InverseValue = .00005, Trials = 1
        });
        Assert.Equal(3, inverse.InverseRows!.Count);
        Assert.All(inverse.AdjustedOperands!, row => Assert.Equal(-row.Maximum, row.Minimum));
        Assert.Equal(IsoSurfaceFormStatistics.TruncatedGaussian, inverse.AdjustedOperands![1].IsoStatistics);
        Assert.Single(inverse.TrialRows);
        Assert.Equal(revision, app.Events.Revision);
        Assert.Equal(before, app.Prescription.GetSurfaces());
    }

    [Fact]
    public void ValidationRejectsCompetingIrregularitiesAndInvalidIsoMetadata()
    {
        using var app = WorkbenchApplication.Create("cooke");
        var b = new ToleranceOperandDto(1, true, ToleranceOperandKind.IsoIrregularityPv, 1, -1, 1);
        var c = b with { Index = 2, Kind = ToleranceOperandKind.IsoRotationalIrregularityPv };
        var d = b with { Index = 3, Kind = ToleranceOperandKind.IsoIrregularityRms };
        Assert.True(app.Tolerancing.ValidateOperands([b, c with { SurfaceNumber = 2 }, d with { SurfaceNumber = 3 }]).IsValid);
        Assert.True(app.Tolerancing.ValidateOperands([c, b with { Kind = ToleranceOperandKind.IsoPower }]).IsValid);
        Assert.False(app.Tolerancing.ValidateOperands([b, c]).IsValid);
        Assert.False(app.Tolerancing.ValidateOperands([b, d]).IsValid);
        Assert.False(app.Tolerancing.ValidateOperands([c, d]).IsValid);
        Assert.False(app.Tolerancing.ValidateOperands([d, d with { Index = 9 }]).IsValid);
        Assert.False(app.Tolerancing.ValidateOperands([d, b with { Kind = ToleranceOperandKind.FringeIrregularity }]).IsValid);
        Assert.False(app.Tolerancing.ValidateOperands([b with { Minimum = -.5 }]).IsValid);
        Assert.False(app.Tolerancing.ValidateOperands([b with { IsoUnit = (IsoSurfaceFormUnit)20 }]).IsValid);
        Assert.False(app.Tolerancing.ValidateOperands([b with { IsoStatistics = (IsoSurfaceFormStatistics)20 }]).IsValid);
        Assert.False(app.Tolerancing.ValidateOperands([c with { IsoUnit = IsoSurfaceFormUnit.Fringes, TestWavelengthMicrometers = 0 }]).IsValid);
    }

    private static (Optic Optic, OpticalSurface Surface) Plane()
    {
        var optic = Optic.CreateCookeTriplet();
        var surface = optic.SurfaceGroup.Items[1];
        surface.Radius = 0;
        surface.PhysicalAperture = new CircularAperture(6);
        return (optic, surface);
    }

    private static SurfaceFormPerturbation Make(SurfaceFormMode mode, double maximum,
        IsoSurfaceFormUnit unit = IsoSurfaceFormUnit.Nanometers,
        IsoSurfaceFormStatistics statistics = IsoSurfaceFormStatistics.AtMaximum) =>
        new(mode.ToString(), 1, mode, maximum, false, 0, 0, unit, .6328, statistics);

    private static double IsocEndpointProfile(double t)
    {
        var x = 2 * t - 1;
        return Math.Sqrt(5) * ((3 * x * x - 1) / 2 - 1)
            + Math.Sqrt(7) * ((5 * Math.Pow(x, 3) - 3 * x) / 2 + 1)
            + 3 * ((35 * Math.Pow(x, 4) - 30 * x * x + 3) / 8 - 1)
            + Math.Sqrt(11) * ((63 * Math.Pow(x, 5) - 70 * Math.Pow(x, 3) + 15 * x) / 8 + 1);
    }

    private static double DensePeakToValley(IGeometry geometry, int angularOrder)
    {
        // Independently scan the traced sag on a dense radial grid. Orthogonal
        // angular phases recover the envelope for the single-frequency B family.
        var minimum = 0.0;
        var maximum = 0.0;
        var (sine, cosine) = Math.SinCos(Math.PI / (2 * Math.Max(1, angularOrder)));
        for (var i = 0; i <= 8192; i++)
        {
            var r = 6 * Math.Sqrt(i / 8192.0);
            var x = geometry.Sag(r, 0);
            if (angularOrder == 0)
            {
                minimum = Math.Min(minimum, x);
                maximum = Math.Max(maximum, x);
                continue;
            }
            var y = geometry.Sag(r * cosine, r * sine);
            maximum = Math.Max(maximum, Math.Sqrt(x * x + y * y));
        }
        return angularOrder == 0 ? maximum - minimum : 2 * maximum;
    }

    private static (double Mean, double Rms, double RawRms) DiskStatistics(IGeometry geometry)
    {
        double[] nodes = [-.906179845938664, -.538469310105683, 0, .538469310105683, .906179845938664];
        double[] weights = [.236926885056189, .478628670499366, .568888888888889, .478628670499366, .236926885056189];
        var mean = 0.0;
        var sumSquares = 0.0;
        for (var i = 0; i < nodes.Length; i++)
        {
            var r = 6 * Math.Sqrt((nodes[i] + 1) / 2);
            for (var azimuth = 0; azimuth < 32; azimuth++)
            {
                var (sine, cosine) = Math.SinCos(2 * Math.PI * azimuth / 32);
                var value = geometry.Sag(r * cosine, r * sine);
                var weight = weights[i] / 64;
                mean += weight * value;
                sumSquares += weight * value * value;
            }
        }
        return (mean, Math.Sqrt(sumSquares - mean * mean), Math.Sqrt(sumSquares));
    }

    private sealed class AngularFamilyRandom(int order) : Random(192)
    {
        public override int Next(int maxValue) => order;
    }
}

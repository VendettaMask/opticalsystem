using OptilandWorkbench.App.ViewModels;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Apodization;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Tests;

public sealed class GeometricEnergyOperandTests
{
    public static TheoryData<string> Codes => new("GENC", "GENF", "ERFP");
    private static Optic Plate(double field = 0)
    {
        var optic = Optic.CreateBlank();
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Thickness = double.PositiveInfinity, Geometry = new PlaneGeometry(), SemiDiameter = 50 },
            new OpticalSurface { Thickness = 10, Geometry = new PlaneGeometry(), MaterialAfter = new AirMaterial(), SemiDiameter = 50, IsStop = true },
            new OpticalSurface { Thickness = 0, Geometry = new PlaneGeometry(), MaterialAfter = new AirMaterial(), SemiDiameter = 50 }
        ]);
        optic.Aperture.Kind = ApertureKind.EntrancePupilDiameter; optic.Aperture.Value = 10;
        optic.Fields.Clear(); optic.Fields.Add(new FieldPoint { Y = field });
        optic.Wavelengths.Clear(); optic.Wavelengths.Add(new Wavelength { Nanometers = 550, IsPrimary = true });
        return optic;
    }
    private static MeritOperandDefinition Row(string code, int type = 1, int reference = 1, double value = .5, int noDiffraction = 1) => new()
    {
        Type = code,
        Wavelength = 1,
        Field = 1,
        ZemaxIntegerParameters = [1, 1],
        ZemaxDataParameters = code == "ERFP" ? [1, type, value, 0] : [1, type, reference, value, noDiffraction]
    };
    private static double Value(Optic optic, MeritOperandDefinition row)
    {
        var result = MeritFunctionCatalog.Evaluate(optic, row); Assert.Empty(result.Error); return result.Value;
    }
    private static void Invalid(Optic optic, MeritOperandDefinition row)
    {
        var result = MeritFunctionCatalog.Evaluate(optic, row); Assert.NotEmpty(result.Error);
        Assert.True(double.IsNaN(result.Value)); Assert.True(double.IsPositiveInfinity(result.Contribution));
        Assert.Throws<OptimizationEvaluationException>(() => MeritFunctionCatalog.EvaluateOptimizationValues(optic, [row]));
    }
    private static double[] PupilDistances(int type, double radius)
    {
        var points = new List<double>();
        for (var j = -16; j <= 16; j++) for (var i = -16; i <= 16; i++)
            if (i * i + j * j <= 256)
                points.Add(radius / 16 * (type switch { 2 => Math.Abs(i), 3 => Math.Abs(j), 4 => Math.Max(Math.Abs(i), Math.Abs(j)), _ => Math.Sqrt(i * i + j * j) }));
        return points.Order().ToArray();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void UnfocusedPlaneBundleMatchesIndependentPupilEnergy(int type)
    {
        var optic = Plate(); var distances = PupilDistances(type, 5000);
        var radius = distances[(int)Math.Ceiling(.8 * distances.Length) - 1];
        Assert.Equal(radius, Value(optic, Row("GENC", type, value: .8)), 8);
        Assert.Equal(distances.Count(d => d <= 2777) / (double)distances.Length, Value(optic, Row("GENF", type, value: 2777)), 12);
        Assert.Equal(1, Value(optic, Row("GENF", type, value: 10000)));
        Assert.Equal(distances[^1], Value(optic, Row("GENC", type, value: 1)), 8);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void EdgePositionUsesCorrectAxisReferenceAndMillimeters(int type)
    {
        var optic = Plate(5); var coordinates = new List<double>();
        for (var j = -16; j <= 16; j++) for (var i = -16; i <= 16; i++) if (i * i + j * j <= 256)
            coordinates.Add(5.0 / 16 * (type % 2 == 0 ? i : j));
        coordinates.Sort(); var expected = coordinates[(int)Math.Ceiling(.8 * coordinates.Count) - 1];
        if (type == 3) expected += 10 * Math.Tan(5 * Math.PI / 180);
        Assert.Equal(expected, Value(optic, Row("ERFP", type, value: .8)), 10);
        var reverse = Value(optic, Row("ERFP", type, value: .2));
        var center = type == 3 ? 10 * Math.Tan(5 * Math.PI / 180) : 0;
        Assert.Equal(2 * center, expected + reverse, 10);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void GeometricReferencesMatchShiftedCircularBundle(int reference)
    {
        var optic = Plate(5); var row = Row("GENC", 3, reference, 1);
        var expected = 5000 + (reference == 2 ? 10000 * Math.Tan(5 * Math.PI / 180) : 0);
        Assert.Equal(expected, Value(optic, row), 8);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void DistributionCombinesEqualCoordinatesAndWeightsBeforeQuantiles(int type)
    {
        WeightedEnergyPoint[] samples = [new(-3, 4, 1), new(-3, 4, 2), new(6, 8, 1), new(0, 0, 0)];
        var energy = new GeometricEnergyDistribution(samples, GeometricEnergyReference.Vertex, (EnergyRegion)type);
        var first = type switch { 2 => 3, 3 or 4 => 4, _ => 5 };
        Assert.Equal(.75, energy.FractionAtDistance(first)); Assert.Equal(first, energy.DistanceAtFraction(.75));
        Assert.Equal(2 * first, energy.DistanceAtFraction(.751)); Assert.Equal(0, energy.DistanceAtFraction(0));
        var huge = samples.Select(p => p with { Weight = p.Weight * 1e307 }).ToArray();
        var again = new GeometricEnergyDistribution(huge, GeometricEnergyReference.Vertex, (EnergyRegion)type);
        Assert.Equal(.75, again.FractionAtDistance(first), 14);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MiddleIsMinimumCircleRatherThanBoundingBoxOrCentroid(bool translated)
    {
        var offset = translated ? 1e8 : 0;
        WeightedEnergyPoint[] points = [new(offset - 1, 0, 1), new(offset + 1, 0, 1), new(offset, Math.Sqrt(3), 10), new(offset, .5, 100)];
        var energy = new GeometricEnergyDistribution(points, GeometricEnergyReference.Middle, EnergyRegion.Circle);
        Assert.Equal(offset, energy.Center.X, 7); Assert.Equal(1 / Math.Sqrt(3), energy.Center.Y, 12);
        Assert.Equal(2 / Math.Sqrt(3), energy.DistanceAtFraction(1), 10);
    }

    [Fact]
    public void CircleHandlesCollinearRepeatedAndSinglePoints()
    {
        foreach (var points in new[] { new[] { (1d, 2d) }, new[] { (-7d, 2d), (1d, 2d), (1d, 2d), (3d, 2d) } })
        {
            var actual = MinimumEnclosingCircle.Center(points);
            Assert.Equal((points.Min(p => p.Item1) + points.Max(p => p.Item1)) / 2, actual.X, 12); Assert.Equal(2, actual.Y);
        }
    }

    [Fact]
    public void RandomCirclesMatchExhaustiveIndependentBoundaryCandidates()
    {
        var random = new Random(84);
        for (var trial = 0; trial < 40; trial++)
        {
            var p = Enumerable.Range(0, 8).Select(_ => (X: random.NextDouble() * 4 - 2, Y: random.NextDouble() * 4 - 2)).ToArray();
            var candidates = new List<(double X, double Y)>();
            for (var i = 0; i < p.Length; i++) for (var j = i + 1; j < p.Length; j++)
            {
                candidates.Add(((p[i].X + p[j].X) / 2, (p[i].Y + p[j].Y) / 2));
                for (var k = j + 1; k < p.Length; k++)
                {
                    // Solve the two perpendicular-bisector linear equations independently.
                    var a = 2 * (p[j].X - p[i].X); var b = 2 * (p[j].Y - p[i].Y);
                    var c = 2 * (p[k].X - p[i].X); var d = 2 * (p[k].Y - p[i].Y);
                    var e = p[j].X * p[j].X + p[j].Y * p[j].Y - p[i].X * p[i].X - p[i].Y * p[i].Y;
                    var f = p[k].X * p[k].X + p[k].Y * p[k].Y - p[i].X * p[i].X - p[i].Y * p[i].Y;
                    if (Math.Abs(a * d - b * c) > 1e-10) candidates.Add(((e * d - b * f) / (a * d - b * c), (a * f - e * c) / (a * d - b * c)));
                }
            }
            double Radius((double X, double Y) center) => p.Max(q => Math.Sqrt(Math.Pow(center.X - q.X, 2) + Math.Pow(center.Y - q.Y, 2)));
            Assert.Equal(candidates.Min(Radius), Radius(MinimumEnclosingCircle.Center(p)), 11);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void AiryScaleAndInverseAreConsistentAndRegionAware(int type)
    {
        var optic = Plate(); optic.SurfaceGroup.Items[1].InteractionModel = new ThinLensInteractionModel(50);
        optic.SurfaceGroup.Items[1].Thickness = 50; optic.SurfaceGroup.Renumber();
        var distribution = GeometricEnergyMetrics.Create(optic, 1, 1, 1, (EnergyRegion)type, GeometricEnergyReference.ChiefRay, true);
        var radius = distribution.DistanceAtFraction(.8); Assert.InRange(radius, .1, 20);
        Assert.Equal(.8, distribution.FractionAtDistance(radius), 6);
        var unscaled = GeometricEnergyMetrics.Create(optic, 1, 1, 1, (EnergyRegion)type, GeometricEnergyReference.ChiefRay, false);
        Assert.InRange(unscaled.DistanceAtFraction(.8), 0, 1e-8);
        var circular = GeometricEnergyMetrics.Create(optic, 1, 1, 1, EnergyRegion.Circle, GeometricEnergyReference.ChiefRay, true);
        Assert.True(distribution.FractionAtDistance(3) + 1e-7 >= circular.FractionAtDistance(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => distribution.DistanceAtFraction(1));
    }

    [Fact]
    public void AiryCircularIntegralMatchesIndependentBesselSeriesAndSlitBounds()
    {
        static double J(int order, double x)
        {
            var term = order == 0 ? 1.0 : x / 2; var sum = term;
            for (var k = 1; k < 80; k++) { term *= -x * x / (4 * k * (k + order)); sum += term; }
            return sum;
        }
        var a = new AiryEnergyComponent(.55, 1, 5); var d = 2.0; var u = Math.PI * d / (.55 * 5);
        var circle = GeometricEnergyDistribution.AiryFraction(d, a, EnergyRegion.Circle);
        Assert.InRange(Math.Abs(circle - (1 - J(0, u) * J(0, u) - J(1, u) * J(1, u))), 0, 2e-8);
        var square = GeometricEnergyDistribution.AiryFraction(d, a, EnergyRegion.Square);
        var slit = GeometricEnergyDistribution.AiryFraction(d, a, EnergyRegion.XSlit);
        Assert.True(circle < square && square < slit && slit < 1);
        Assert.Equal(slit, GeometricEnergyDistribution.AiryFraction(d, a, EnergyRegion.YSlit));
    }

    [Fact]
    public void PolyWeightsPrimaryReferenceAndSingleZeroWeightAreIndependent()
    {
        var optic = Plate(5); optic.SurfaceGroup.Items[1].MaterialAfter = new CatalogGlassMaterial("TEST:dispersion", "TEST", "tabulated n", 400, 800,
            refractiveIndexWavelengthsNanometers: [400, 800], refractiveIndices: [1.4, 1.8]);
        optic.Wavelengths[0].Nanometers = 400; optic.Wavelengths[0].Weight = 0;
        optic.Wavelengths.Add(new Wavelength { Nanometers = 800, Weight = double.MaxValue, IsPrimary = false });
        var mono = Row("ERFP", 3, value: .5); mono.ZemaxIntegerParameters[1] = 2;
        var poly = Row("ERFP", 3, value: .5); poly.ZemaxIntegerParameters[1] = 0;
        Assert.Equal(Value(optic, mono), Value(optic, poly), 12);
        var primary = Row("ERFP", 3, value: .5); var primaryValue = Value(optic, primary);
        poly.ZemaxDataParameters[1] = 1;
        Assert.Equal(Value(optic, mono) - primaryValue, Value(optic, poly), 10);
        optic.Wavelengths[0].Weight = double.MaxValue;
        Assert.True(double.IsFinite(Value(optic, poly)));
    }

    [Fact]
    public void VignettedChiefIsNotReplacedByCentroid()
    {
        var optic = Plate(); optic.SurfaceGroup.Items[1].PhysicalAperture = new AnnularAperture(5, 1);
        Assert.True(Value(optic, Row("GENC", reference: 1)) > 0);
        Invalid(optic, Row("GENC", reference: 0)); Invalid(optic, Row("ERFP", 0));
    }

    [Theory]
    [InlineData("GENC", "afocal")]
    [InlineData("GENF", "sampling")]
    [InlineData("ERFP", "sampling")]
    [InlineData("GENC", "wave")]
    [InlineData("GENF", "field")]
    [InlineData("ERFP", "field")]
    [InlineData("GENC", "type")]
    [InlineData("GENF", "reference")]
    [InlineData("ERFP", "type")]
    [InlineData("GENC", "fraction")]
    [InlineData("GENF", "distance")]
    [InlineData("ERFP", "fraction")]
    [InlineData("ERFP", "window")]
    [InlineData("GENF", "zeroWeight")]
    [InlineData("GENC", "aperture")]
    [InlineData("GENC", "diffraction")]
    public void UnsupportedAndInvalidRequestsFailExplicitly(string code, string fault)
    {
        var optic = Plate(); var row = Row(code);
        if (fault == "afocal") optic.ImageSpaceAfocal = true;
        if (fault == "sampling") row.ZemaxIntegerParameters[0] = 6;
        if (fault == "wave") row.ZemaxIntegerParameters[1] = 99;
        if (fault == "field") row.ZemaxDataParameters[0] = 0;
        if (fault == "type") row.ZemaxDataParameters[1] = 99;
        if (fault == "reference") row.ZemaxDataParameters[2] = 4;
        if (fault == "fraction") row.ZemaxDataParameters[code == "ERFP" ? 2 : 3] = 1.01;
        if (fault == "distance") row.ZemaxDataParameters[3] = -1;
        if (fault == "window") row.ZemaxDataParameters[3] = 100;
        if (fault == "zeroWeight") { row.ZemaxIntegerParameters[1] = 0; optic.Wavelengths[0].Weight = 0; }
        if (fault == "aperture") optic.SurfaceGroup.Items[2].PhysicalAperture = new OffsetRadialAperture(1, 0, 200, 200);
        if (fault == "diffraction") row.ZemaxDataParameters[4] = 0;
        Invalid(optic, row);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void AsymmetricApertureAndApodizationMatchIndependentWeightedCdf(int type)
    {
        var optic = Plate(); optic.SurfaceGroup.Items[1].PhysicalAperture = new RectangularAperture(2.5, 5, 2.5, 0);
        optic.Apodization = new ZemaxApodization(ZemaxApodizationType.Gaussian, .7);
        var samples = new List<(double X, double Y, double Weight)>();
        for (var j = -16; j <= 16; j++) for (var i = 0; i <= 16; i++) if (i * i + j * j <= 256)
            samples.Add((5000.0 * i / 16, 5000.0 * j / 16, Math.Exp(-1.4 * (i * i + j * j) / 256)));
        var total = samples.Sum(p => p.Weight); var cx = samples.Sum(p => p.X * p.Weight) / total; var cy = samples.Sum(p => p.Y * p.Weight) / total;
        double Distance((double X, double Y, double Weight) p) => type switch
        { 2 => Math.Abs(p.X - cx), 3 => Math.Abs(p.Y - cy), 4 => Math.Max(Math.Abs(p.X - cx), Math.Abs(p.Y - cy)), _ => Math.Sqrt(Math.Pow(p.X - cx, 2) + Math.Pow(p.Y - cy, 2)) };
        Assert.Equal(samples.Where(p => Distance(p) <= 2333).Sum(p => p.Weight) / total, Value(optic, Row("GENF", type, value: 2333)), 11);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedCoreEnergyMatchesDirectAnalysisSamplesWithoutPlotInterpolation(bool scaled)
    {
        var optic = Optic.CreateCookeTriplet();
        var analysis = new EncircledEnergyAnalysis(optic, numRays: 32, distribution: "uniform-intervals", numPoints: 37,
            wavelengthNumber: 0, reference: "centroid", multiplyByDiffractionLimit: scaled).GenerateData();
        for (var field = 1; field <= optic.Fields.Count; field++)
        {
            var distribution = GeometricEnergyMetrics.Create(optic, 1, 0, field, EnergyRegion.Circle, GeometricEnergyReference.Centroid, scaled);
            foreach (var point in analysis.PlotSeries[field - 1].Points)
                Assert.Equal(point.Y, distribution.FractionAtDistance(point.X), 8);
        }
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("nan")]
    [InlineData("negative")]
    [InlineData("zero")]
    [InlineData("overflow")]
    public void InvalidDistributionsDoNotReturnSuccessfulZero(string fault)
    {
        WeightedEnergyPoint[] points = fault switch
        {
            "empty" => [],
            "nan" => [new(double.NaN, 0, 1)],
            "negative" => [new(0, 0, -1)],
            "zero" => [new(0, 0, 0)],
            _ => [new(double.MaxValue, double.MaxValue, 1)]
        };
        Assert.Throws<InvalidOperationException>(() => new GeometricEnergyDistribution(points, GeometricEnergyReference.Vertex, EnergyRegion.Circle));
    }

    [Fact]
    public void MultiWavelengthRayBudgetFailsBeforeTracing()
    {
        var optic = Plate();
        for (var i = 0; i < 5; i++) optic.Wavelengths.Add(new Wavelength { Nanometers = 600 + i, IsPrimary = false });
        var row = Row("GENC"); row.ZemaxIntegerParameters = [5, 0]; Invalid(optic, row);
    }

    [Fact]
    public void TransmissionOptionDoesNotAlterTracedPositionsOrDefaultWeights()
    {
        var optic = Plate(5); optic.SurfaceGroup.Items[1].MaterialAfter = new ConstantIndexMaterial("Test", 1.5, extinctionCoefficient: 1e-6);
        var pupils = new[] { new PupilSample(0, 0, 1), new PupilSample(.5, 0, .5) };
        var geometric = RayBundleMetrics.Trace(optic, 2, 1, 0, 1, pupils, includeSurfaceTransmission: false);
        var transmitted = RayBundleMetrics.Trace(optic, 2, 1, 0, 1, pupils);
        for (var i = 0; i < pupils.Length; i++)
        {
            Assert.Equal(transmitted[i].Position, geometric[i].Position);
            Assert.Equal(pupils[i].Weight, geometric[i].Weight, 12);
            Assert.True(transmitted[i].Weight < geometric[i].Weight);
        }
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void CancellationAndEditsAreObserved(string code)
    {
        var optic = Plate(); var row = Row(code, value: code == "GENF" ? 2500 : .8);
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel(); using var scope = ComputationCancellation.Push(cancellation.Token);
            Assert.ThrowsAny<OperationCanceledException>(() => MeritFunctionCatalog.Evaluate(optic, row));
        }
        var before = Value(optic, row); optic.Aperture.Value = 8; Assert.NotEqual(before, Value(optic, row));
        row.Target = Value(optic, row) + .2; row.Weight = 3;
        Assert.Equal(.12, MeritFunctionCatalog.Evaluate(optic, row).Contribution, 9);
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public async Task LocalEditorAndProjectRoundTripsButUnverifiedNativeLayoutStaysReadOnly(string code)
    {
        var optic = Plate(); var row = Row(code); optic.MeritFunctionOperands.Add(row);
        var expected = Value(optic, row); var path = Path.Combine(Path.GetTempPath(), $"energy-{Guid.NewGuid():N}.staropt");
        try
        {
            await StarOptProjectStore.SaveAsync(new StarOptProjectDocument([optic], 0), path);
            var copy = (await StarOptProjectStore.LoadAsync(path)).Configurations[0];
            Assert.Equal(expected, Value(copy, Assert.Single(copy.MeritFunctionOperands)), 12);
            Assert.Equal(row.ZemaxDataParameters, copy.MeritFunctionOperands[0].ZemaxDataParameters);
        }
        finally { File.Delete(path); }
        using var app = WorkbenchApplication.Create("cooke");
        app.Optimization.SetMeritFunction([new(1, false, code, 0, 1, 1, 0, 0, 0, 0, .25, 2, 0, 0, "energy",
            ZemaxInt1: 1, ZemaxInt2: 1, ZemaxData1: 1, ZemaxData2: 1, ZemaxData3: code == "ERFP" ? .5 : 1,
            ZemaxData4: code == "ERFP" ? 0 : .8, ZemaxData5: code == "ERFP" ? null : 1)]);
        var type = app.Optimization.GetMeritOperandTypes().Single(t => t.Code == code);
        var editor = new MeritOperandEditorRow(Assert.Single(app.Optimization.GetMeritFunction()), type);
        if (code != "ERFP") editor.Parameter7 = -1;
        app.Optimization.SetMeritFunction([editor.ToDto()]); var saved = Assert.Single(app.Optimization.GetMeritFunction());
        Assert.False(saved.CompatibilityOnly); Assert.Equal(2, saved.Weight); Assert.Equal(.25, saved.Target);
        if (code != "ERFP") Assert.Equal(-1, saved.ZemaxData5);
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "zemax-123456.ZMX"));
        var imported = OpticalFormatCatalog.Import(text + $"\n{code} 1 1 1 1 1 .8 .25 2 0 0\n", ".zmx");
        var native = imported.MeritFunctionOperands.Last(); Assert.False(native.Enabled); Assert.True(native.CompatibilityOnly);
        Assert.True(Optic.FromSnapshot(imported.ToSnapshot()).MeritFunctionOperands.Last().CompatibilityOnly);
    }
}

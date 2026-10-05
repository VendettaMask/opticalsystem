using OptilandWorkbench.App.ViewModels;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Tests;

public sealed class DiffractionEnergyOperandTests
{
    public static TheoryData<string> Codes => new("DENC", "DENF");
    private static MeritOperandDefinition Row(string code, int reference = 0, int type = 1, double value = .5) => new()
    {
        Type = code,
        Wavelength = 1,
        Field = 1,
        ZemaxIntegerParameters = [1, 1],
        ZemaxDataParameters = [1, type, reference, value, 1, reference >= 3 ? 5 : 0]
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
    private static WeightedEnergyPoint[] UniformPixels() => [new(-.5, -.5, 1), new(.5, -.5, 1), new(-.5, .5, 1), new(.5, .5, 1)];

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void UniformPixelAreaIntegralsMatchAnalyticShapes(int type)
    {
        var distribution = new DiffractionEnergyDistribution(UniformPixels(), (0, 0), (EnergyRegion)type);
        foreach (var r in new[] { .1, .3, .5, .8, 1.0 })
        {
            var expected = type == 1 ? Math.PI * r * r / 4 : type == 4 ? r * r : r;
            Assert.InRange(Math.Abs(distribution.FractionAtDistance(r) - expected), 0, 2e-5);
            if (expected < .99) Assert.InRange(Math.Abs(distribution.DistanceAtFraction(expected) - r), 0, 5e-5);
        }
        Assert.Equal(0, distribution.FractionAtDistance(0)); Assert.Equal(0, distribution.DistanceAtFraction(0));
    }

    [Fact]
    public void ShiftedReferenceAndUnequalIntensitiesUsePixelOverlap()
    {
        var points = UniformPixels().Select(p => p with { Weight = p.X < 0 ? 1 : 3 }).ToArray();
        var distribution = new DiffractionEnergyDistribution(points, (.25, 0), EnergyRegion.XSlit);
        Assert.Equal(.625, distribution.FractionAtDistance(.5), 12);
        Assert.Equal(.5, distribution.DistanceAtFraction(.625), 12);
        var large = new DiffractionEnergyDistribution(points.Select(p => p with { Weight = p.Weight * 1e307 }).ToArray(), (.25, 0), EnergyRegion.XSlit);
        Assert.Equal(.625, large.FractionAtDistance(.5), 12);
    }

    [Theory]
    [InlineData(.3)]
    [InlineData(1.3)]
    [InlineData(1.7)]
    public void CircleCrossingManyPixelBoundariesRetainsExactArea(double radius)
    {
        var points = new List<WeightedEnergyPoint>();
        for (var y = 0; y < 6; y++) for (var x = 0; x < 4; x++) points.Add(new(x - 1.5, y - 2.5, 1));
        var distribution = new DiffractionEnergyDistribution(points, (.123, -.24), EnergyRegion.Circle);
        var expected = Math.PI * radius * radius / 24;
        Assert.Equal(expected, distribution.FractionAtDistance(radius), 12);
        Assert.Equal(radius, distribution.DistanceAtFraction(expected), 11);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("nan")]
    [InlineData("negative")]
    [InlineData("zero")]
    [InlineData("hole")]
    [InlineData("duplicate")]
    [InlineData("pitch")]
    public void InvalidPsfGridsFailWithoutSyntheticFractions(string fault)
    {
        var points = UniformPixels();
        if (fault == "empty") points = [];
        if (fault == "nan") points[0] = points[0] with { X = double.NaN };
        if (fault == "negative") points[0] = points[0] with { Weight = -1 };
        if (fault == "zero") points = points.Select(p => p with { Weight = 0 }).ToArray();
        if (fault == "hole") points = points[..3];
        if (fault == "duplicate") points[3] = points[0];
        if (fault == "pitch") points[0] = points[0] with { X = -.25 };
        Assert.Throws<InvalidOperationException>(() => new DiffractionEnergyDistribution(points, (0, 0), EnergyRegion.Circle));
    }

    [Fact]
    public void FiniteWindowDoesNotReturnUnityForUncoveredRegions()
    {
        var distribution = new DiffractionEnergyDistribution(UniformPixels(), (0, 0), EnergyRegion.Circle);
        Assert.Throws<InvalidOperationException>(() => distribution.FractionAtDistance(1.001));
        Assert.Throws<InvalidOperationException>(() => distribution.DistanceAtFraction(.9));
        Assert.Throws<ArgumentOutOfRangeException>(() => distribution.DistanceAtFraction(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => distribution.FractionAtDistance(-1));
        Assert.Throws<InvalidOperationException>(() => new DiffractionEnergyDistribution(UniformPixels(), (10, 0), EnergyRegion.Circle));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel(); using var scope = ComputationCancellation.Push(cancellation.Token);
        Assert.ThrowsAny<OperationCanceledException>(() => distribution.FractionAtDistance(.5));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(0, 2)]
    [InlineData(0, 3)]
    [InlineData(0, 4)]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 1)]
    [InlineData(3, 2)]
    [InlineData(3, 3)]
    [InlineData(3, 4)]
    [InlineData(4, 1)]
    [InlineData(5, 1)]
    public void FormalPsfModesProduceConsistentDistanceAndFraction(int reference, int type)
    {
        var optic = Optic.CreateCookeTriplet(); var row = Row("DENC", reference, type);
        var distance = Value(optic, row); Assert.InRange(distance, 1e-8, 1000);
        row.Type = "DENF"; row.ZemaxDataParameters[3] = distance;
        Assert.Equal(.5, Value(optic, row), 8);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void MonochromaticFftMatchesSharedEnergyAnalysisAtActualCoordinates(int wave)
    {
        var optic = Optic.CreateCookeTriplet();
        var distribution = DiffractionEnergyMetrics.Create(optic, 1, wave, 1, EnergyRegion.Circle, 1);
        var data = new DiffractionEncircledEnergyAnalysis(optic, pupilSampling: 32, imageSampling: 64, numPoints: 23,
            wavelengthNumber: wave, fieldNumber: 1, reference: "centroid").GenerateData();
        var compared = 0;
        foreach (var point in data.PlotSeries[^1].Points.Where(p => p.X <= distribution.MaximumCoveredDistanceMicrometers))
        {
            Assert.Equal(point.Y, distribution.FractionAtDistance(point.X), 10); compared++;
        }
        Assert.True(compared >= 3);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void ZeroWeightMonochromaticSelectionAndHugePolyWeightsRemainValid(int reference)
    {
        var optic = Optic.CreateCookeTriplet(); var row = Row("DENF", reference, value: 1);
        var before = Value(optic, row); optic.Wavelengths[0].Weight = 0; Assert.Equal(before, Value(optic, row), 12);
        foreach (var wave in optic.Wavelengths) wave.Weight = double.MaxValue;
        row.ZemaxIntegerParameters[1] = 0; Assert.InRange(Value(optic, row), 0, 1);
        foreach (var wave in optic.Wavelengths) wave.Weight = 0; Invalid(optic, row);
    }

    [Fact]
    public void DefaultHuygensDeltaAndLargerImageWindowUseFormalSampling()
    {
        var optic = Optic.CreateCookeTriplet();
        var small = DiffractionEnergyMetrics.Create(optic, 1, 1, 1, EnergyRegion.Circle, 3, 1, 0);
        var large = DiffractionEnergyMetrics.Create(optic, 1, 1, 1, EnergyRegion.Circle, 3, 2, 0);
        Assert.True(large.MaximumCoveredDistanceMicrometers > small.MaximumCoveredDistanceMicrometers);
        Assert.InRange(small.FractionAtDistance(small.MaximumCoveredDistanceMicrometers / 2), 0, 1);
        Assert.InRange(large.FractionAtDistance(small.MaximumCoveredDistanceMicrometers / 2), 0, 1);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    public void VertexReferenceOutsideSampledWindowCannotReturnFalseEnergy(int reference)
    {
        var optic = Optic.CreateCookeTriplet(); var row = Row("DENC", reference);
        row.ZemaxDataParameters[0] = 3; Invalid(optic, row);
    }

    [Theory]
    [InlineData("afocal")]
    [InlineData("sampling")]
    [InlineData("imageSampling")]
    [InlineData("budget")]
    [InlineData("wave")]
    [InlineData("field")]
    [InlineData("type")]
    [InlineData("reference")]
    [InlineData("delta")]
    [InlineData("fraction")]
    [InlineData("distance")]
    public void UnsupportedAndInvalidRequestsAreErrors(string fault)
    {
        var optic = Optic.CreateCookeTriplet(); var row = Row("DENC", 3);
        if (fault == "afocal") optic.ImageSpaceAfocal = true;
        if (fault == "sampling") row.ZemaxIntegerParameters[0] = 5;
        if (fault == "imageSampling") row.ZemaxDataParameters[4] = 5;
        if (fault == "budget") { row.ZemaxIntegerParameters[0] = 4; row.ZemaxDataParameters[4] = 4; }
        if (fault == "wave") row.ZemaxIntegerParameters[1] = 99;
        if (fault == "field") row.ZemaxDataParameters[0] = 99;
        if (fault == "type") row.ZemaxDataParameters[1] = 0;
        if (fault == "reference") row.ZemaxDataParameters[2] = 6;
        if (fault == "delta") row.ZemaxDataParameters[5] = -1;
        if (fault == "fraction") row.ZemaxDataParameters[3] = 1;
        if (fault == "distance") { row.Type = "DENF"; row.ZemaxDataParameters[3] = 1e8; }
        Invalid(optic, row);
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void CancellationSourceEditsAndContributionRemainObservable(string code)
    {
        var optic = Optic.CreateCookeTriplet(); var row = Row(code);
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel(); using var scope = ComputationCancellation.Push(cancellation.Token);
            Assert.ThrowsAny<OperationCanceledException>(() => MeritFunctionCatalog.Evaluate(optic, row));
        }
        var before = Value(optic, row); optic.Wavelengths[0].Nanometers *= 1.1;
        Assert.NotEqual(before, Value(optic, row)); row.Target = Value(optic, row) + .2; row.Weight = 3;
        Assert.Equal(.12, MeritFunctionCatalog.Evaluate(optic, row).Contribution, 10);
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public async Task EightSlotsRoundTripAndUnverifiedNativeRowsRemainReadOnly(string code)
    {
        var optic = Optic.CreateCookeTriplet(); var row = Row(code, 3); optic.MeritFunctionOperands.Add(row);
        var expected = Value(optic, row); var path = Path.Combine(Path.GetTempPath(), $"diffraction-energy-{Guid.NewGuid():N}.staropt");
        try
        {
            await StarOptProjectStore.SaveAsync(new StarOptProjectDocument([optic], 0), path);
            var copy = (await StarOptProjectStore.LoadAsync(path)).Configurations[0]; var saved = Assert.Single(copy.MeritFunctionOperands);
            Assert.Equal(row.ZemaxDataParameters, saved.ZemaxDataParameters); Assert.Equal(expected, Value(copy, saved), 10);
        }
        finally { File.Delete(path); }
        using var app = WorkbenchApplication.Create("cooke");
        app.Optimization.SetMeritFunction([new(1, false, code, 0, 1, 1, 0, 0, 0, 0, .25, 2, 0, 0, "energy",
            ZemaxInt1: 1, ZemaxInt2: 1, ZemaxData1: 1, ZemaxData2: 1, ZemaxData3: 3, ZemaxData4: .5, ZemaxData5: 1, ZemaxData6: 5)]);
        var type = app.Optimization.GetMeritOperandTypes().Single(t => t.Code == code);
        var editor = new MeritOperandEditorRow(Assert.Single(app.Optimization.GetMeritFunction()), type);
        Assert.True(editor.IsParameterEditable(7)); editor.Parameter8 = 4;
        app.Optimization.SetMeritFunction([editor.ToDto()]); var dto = Assert.Single(app.Optimization.GetMeritFunction());
        Assert.Equal(4, dto.ZemaxData6); Assert.Equal(.25, dto.Target); Assert.Equal(2, dto.Weight);
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "zemax-123456.ZMX"));
        var imported = OpticalFormatCatalog.Import(text + $"\n{code} 1 1 1 1 1 .5 .25 2 0 0\n", ".zmx");
        var native = imported.MeritFunctionOperands.Last(); Assert.True(native.CompatibilityOnly); Assert.False(native.Enabled);
        Assert.True(Optic.FromSnapshot(imported.ToSnapshot()).MeritFunctionOperands.Last().CompatibilityOnly);
    }
}

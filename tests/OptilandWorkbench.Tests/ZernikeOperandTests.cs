using System.Numerics;
using OptilandWorkbench.App.ViewModels;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Tests;

public sealed class ZernikeOperandTests
{
    private static MeritOperandDefinition Row(int term = 4, int type = 0, int field = 1, double epsilon = 0) => new()
    {
        Type = "ZERN",
        Field = field,
        Wavelength = 1,
        ZemaxIntegerParameters = [term, 1],
        ZemaxDataParameters = [1, field, type, epsilon, 0]
    };
    private static double Value(Optic optic, MeritOperandDefinition row)
    { var r = MeritFunctionCatalog.Evaluate(optic, row); Assert.Empty(r.Error); return r.Value; }
    private static WavefrontSample Sample(double x, double y, double value, double intensity = 1) => new(x, y, x, y, 0, value, intensity);
    private static WavefrontSample[] Grid(Func<double, double, double> function, int size = 33) =>
        (from i in Enumerable.Range(0, size)
         from j in Enumerable.Range(0, size)
         let x = -1 + 2.0 * i / (size - 1)
         let y = -1 + 2.0 * j / (size - 1)
         where x * x + y * y <= 1
         select Sample(x, y, function(x, y))).ToArray();

    [Fact]
    public void StandardNumberingMatchesOfficialFirstTwentyEightTerms()
    {
        (int, int)[] expected = [(0,0),(1,1),(1,-1),(2,0),(2,-2),(2,2),(3,-1),(3,1),(3,-3),(3,3),
            (4,0),(4,2),(4,-2),(4,4),(4,-4),(5,1),(5,-1),(5,3),(5,-3),(5,5),(5,-5),
            (6,0),(6,-2),(6,2),(6,-4),(6,4),(6,-6),(6,6)];
        var coefficients = ZernikeFitEngine.FitStandard([], 28);
        Assert.Equal(expected, coefficients.Select(c => (c.RadialOrder, c.AzimuthalOrder)));
        var samples = Grid((x, y) => .3 + .2 * Math.Sqrt(6) * 2 * x * y
            - .1 * Math.Sqrt(8) * (3 * (x * x + y * y) - 2) * y);
        var fit = ZernikeMetrics.Fit(samples, ZernikeBasisKind.Standard, 28);
        for (var i = 1; i <= 28; i++) Assert.Equal(i switch { 1 => .3, 5 => .2, 7 => -.1, _ => 0 }, fit.Value(i), 11);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(.5)]
    [InlineData(.95)]
    public void AnnularLowTermsMatchIndependentAnalyticPolynomials(double epsilon)
    {
        var e2 = epsilon * epsilon;
        var samples = Grid((x, y) => .4 + .2 * 2 * x / Math.Sqrt(1 + e2)
            + .3 * Math.Sqrt(3) * (2 * (x * x + y * y) - 1 - e2) / (1 - e2)
            - .1 * Math.Sqrt(6) * 2 * x * y / Math.Sqrt(1 + e2 + e2 * e2), 65);
        var fit = ZernikeMetrics.Fit(samples, ZernikeBasisKind.Annular, 11, epsilon);
        for (var i = 1; i <= 11; i++) Assert.Equal(i switch { 1 => .4, 2 => .2, 4 => .3, 5 => -.1, _ => 0 }, fit.Value(i), 10);
        Assert.InRange(fit.MaximumFitError, 0, 1e-11);
    }

    // Independent tabulated 16-point Gauss-Legendre rule, not the runtime sampler.
    private static readonly double[] Nodes = [.09501250983763744, .2816035507792589, .4580167776572274, .6178762444026438,
        .755404408355003, .8656312023878318, .9445750230732326, .9894009349916499];
    private static readonly double[] Weights = [.1894506104550685, .1826034150449236, .1691565193950025, .1495959888165767,
        .1246289712555339, .09515851168249278, .06225352393864789, .02715245941175409];

    [Theory]
    [InlineData(0)]
    [InlineData(.5)]
    [InlineData(.95)]
    public void All231AnnularTermsAreOrthonormalIncludingThinAnnuli(double epsilon)
    {
        var terms = ZernikeFitEngine.FitStandard([], 231).Where(c => c.AzimuthalOrder >= 0).ToArray();
        foreach (var a in terms)
            foreach (var b in terms.Where(b => b.AzimuthalOrder == a.AzimuthalOrder && b.Number >= a.Number))
            {
                var dot = 0.0;
                for (var i = 0; i < Nodes.Length; i++) foreach (var sign in new[] { -1, 1 })
                {
                    var r = Math.Sqrt(epsilon * epsilon + (1 - epsilon * epsilon) * (1 + sign * Nodes[i]) / 2);
                    double At(ZernikeCoefficient c) => ZernikeFitEngine.EvaluateAnnular([c with { Value = 1 }], r, 0, epsilon);
                    dot += Weights[i] / 2 * At(a) * At(b) * (a.AzimuthalOrder == 0 ? 1 : .5);
                }
                Assert.True(Math.Abs(dot - (a.Number == b.Number ? 1 : 0)) < 2e-10, $"eps={epsilon}, {a.Number}/{b.Number}: {dot:R}");
            }
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(2, .5)]
    [InlineData(2, .95)]
    public void Full231TermFitRecoversIndependentTwentiethOrderSine(int kind, double epsilon)
    {
        var samples = new List<WavefrontSample>();
        var normalization = Math.Sqrt(42 / Enumerable.Range(0, 21).Sum(k => Math.Pow(epsilon, 2 * k)));
        for (var i = 0; i < Nodes.Length; i++) foreach (var sign in new[] { -1, 1 })
        {
            var r = Math.Sqrt(epsilon * epsilon + (1 - epsilon * epsilon) * (1 + sign * Nodes[i]) / 2);
            for (var a = 0; a < 48; a++)
            {
                var x = r * Math.Cos(2 * Math.PI * a / 48); var y = r * Math.Sin(2 * Math.PI * a / 48);
                samples.Add(Sample(x, y, .4 + .07 * normalization * Complex.Pow(new(x, y), 20).Imaginary));
            }
        }
        var fit = ZernikeMetrics.Fit(samples, (ZernikeBasisKind)kind, 231, epsilon);
        for (var i = 1; i <= 231; i++) Assert.Equal(i switch { 1 => .4, 231 => .07, _ => 0 }, fit.Value(i), 8);
        Assert.InRange(fit.MaximumFitError, 0, 1e-8);
    }

    [Fact]
    public void FringeAndNineDiagnosticsUseRawSamplesAndBestFitTilt()
    {
        var samples = Grid((x, y) => 2 + 3 * x - 4 * y);
        samples = samples.Select((s, i) => s with { Intensity = i % 2 == 0 ? .1 : 10 }).ToArray();
        var fit = ZernikeMetrics.Fit(samples, ZernikeBasisKind.Fringe, 11);
        Assert.Equal(2, fit.Value(1), 12); Assert.Equal(3, fit.Value(2), 12); Assert.Equal(-4, fit.Value(3), 12);
        var variance = samples.Average(s => Math.Pow(s.OpdWaves - 2, 2));
        Assert.Equal(Math.Sqrt(4 + variance), fit.Value(-6), 12);
        Assert.Equal(Math.Sqrt(variance), fit.Value(-5), 12);
        Assert.Equal(samples.Max(s => s.OpdWaves) - samples.Min(s => s.OpdWaves), fit.Value(-7), 12);
        foreach (var term in new[] { -8, -4, -3, -1, 0 }) Assert.InRange(fit.Value(term), 0, 1e-12);
        Assert.Equal(1, fit.Value(-2), 12);
        Assert.Throws<ArgumentOutOfRangeException>(() => fit.Value(12));
    }

    [Fact]
    public void MaskedPupilStatisticsDoNotSubtractCoefficientsOfUnrelatedFullBasisFit()
    {
        var samples = Grid((x, y) => .1 * (x * x + y * y) + .2 * x).Where(s => s.NormalizedPupilX > -.2).ToArray();
        var fit = ZernikeMetrics.Fit(samples, ZernikeBasisKind.Fringe, 11);
        var mean = samples.Average(s => s.OpdWaves);
        Assert.Equal(Math.Sqrt(samples.Average(s => Math.Pow(s.OpdWaves - mean, 2))), fit.Chief.Rms, 12);
        Assert.True(Math.Abs(mean - fit.Value(1)) > 1e-3);
        Assert.True(fit.Centroid.Rms < fit.Chief.Rms);
        Assert.Equal(Math.Exp(-Math.Pow(2 * Math.PI * fit.Centroid.Rms, 2)), fit.Value(-2), 13);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void AdjacentRowsShareHighestTermFitAndDiagnostics(int type)
    {
        var optic = Optic.CreateCookeTriplet();
        var rows = new[] { Row(1, type), Row(22, type), Row(-4, type), Row(0, type) };
        var results = MeritFunctionCatalog.EvaluateAll(optic, rows);
        var fit = ZernikeMetrics.Evaluate(optic, 1, 1, 1, (ZernikeBasisKind)type, 22);
        for (var i = 0; i < rows.Length; i++) { Assert.Empty(results[i].Error); Assert.Equal(fit.Value(rows[i].ZemaxIntegerParameters[0]), results[i].Value, 12); }
        rows[0].Weight = 2; rows[0].Target = results[0].Value + .25;
        Assert.Equal(.125, MeritFunctionCatalog.EvaluateAll(optic, rows)[0].Contribution, 12);
        var standalone = Value(optic, rows[0]);
        var split = MeritFunctionCatalog.EvaluateAll(optic, [rows[0], new() { Type = "BLNK" }, rows[1]]);
        Assert.Equal(standalone, split[0].Value, 12);
        Assert.True(Math.Abs(standalone - results[0].Value) > 1e-10);
        rows[1].Enabled = false;
        Assert.Equal(standalone, MeritFunctionCatalog.EvaluateAll(optic, rows)[0].Value, 12);
    }

    [Fact]
    public void AdjacentFitReusesTraceRequestsButSeparateBlockDoesNotReuseFit()
    {
        var optic = Optic.CreateCookeTriplet(); var cache = new RayTraceCache(4096, 100_000); optic.ConfigureRayTraceCache(cache, 7);
        MeritFunctionCatalog.EvaluateAll(optic, [Row(22), Row(4), Row(-4)]);
        var initial = cache.Statistics;
        var copy = Optic.CreateCookeTriplet(); var other = new RayTraceCache(4096, 100_000); copy.ConfigureRayTraceCache(other, 7);
        Value(copy, Row(22));
        Assert.Equal(other.Statistics.Hits, initial.Hits); Assert.Equal(other.Statistics.Misses, initial.Misses);
        MeritFunctionCatalog.EvaluateAll(optic, [Row(22), new() { Type = "BLNK" }, Row(4)]);
        Assert.True(cache.Statistics.Hits > initial.Hits);
    }

    [Theory]
    [InlineData("wave")]
    [InlineData("field")]
    [InlineData("type")]
    [InlineData("sampling")]
    [InlineData("epsilon")]
    public void DifferentRequestsEndAdjacentFitGroups(string difference)
    {
        var optic = Optic.CreateCookeTriplet(); var first = Row(1, 2, 1, .2); var second = Row(22, 2, 1, .2);
        switch (difference)
        {
            case "wave": second.ZemaxIntegerParameters[1] = 2; break;
            case "field": second.ZemaxDataParameters[1] = 2; break;
            case "type": second.ZemaxDataParameters[2] = 1; break;
            case "sampling": second.ZemaxDataParameters[0] = 2; break;
            case "epsilon": second.ZemaxDataParameters[3] = .5; break;
        }
        var results = MeritFunctionCatalog.EvaluateAll(optic, [first, second]);
        Assert.All(results, r => Assert.Empty(r.Error));
        Assert.Equal(Value(optic, first), results[0].Value, 12); Assert.Equal(Value(optic, second), results[1].Value, 12);
    }

    [Fact]
    public void IntermediateImageChangesInvalidateFitAndRestoreSource()
    {
        var optic = Optic.CreateCookeTriplet(); var row = Row(-4);
        var initial = Value(optic, row);
        var results = MeritFunctionCatalog.EvaluateAll(optic, [row,new(){Type="IMSF",ZemaxIntegerParameters=[5,0]},row,
            new(){Type="IMSF",ZemaxIntegerParameters=[0,0]},row]);
        Assert.All(results, r => Assert.Empty(r.Error));
        Assert.Equal(initial, results[4].Value, 12); Assert.Equal(initial, Value(optic, row), 12);
        var selected = IntermediateImageSystem.Create(optic, 5, false);
        Assert.Equal(Value(selected, row), results[2].Value, 12); Assert.NotEqual(initial, results[2].Value);
    }

    [Fact]
    public async Task SeparateConcurrentEvaluationsKeepTheirOwnFits()
    {
        var optics = new[] { Optic.CreateCookeTriplet(), Optic.CreateCookeTriplet() };
        optics[1].SurfaceGroup.Items[1].Radius *= 1.01;
        var expected = optics.Select(o => Value(o, Row(22, 1))).ToArray();
        var values = await Task.WhenAll(Enumerable.Range(0, 4).Select(i => Task.Run(() =>
            MeritFunctionCatalog.EvaluateAll(optics[i % 2], [Row(1, 1), Row(22, 1)])[1])));
        for (var i = 0; i < values.Length; i++) { Assert.Empty(values[i].Error); Assert.Equal(expected[i % 2], values[i].Value, 12); }
        Assert.NotEqual(expected[0], expected[1]);
    }

    [Theory]
    [InlineData("wave")]
    [InlineData("field")]
    [InlineData("type")]
    [InlineData("sampling")]
    [InlineData("fractional")]
    [InlineData("epsilon")]
    [InlineData("vertex")]
    [InlineData("term")]
    [InlineData("budget")]
    [InlineData("nan")]
    public void InvalidRequestsReturnExplicitErrorsInsteadOfPlausibleNumbers(string fault)
    {
        var row = Row();
        switch (fault)
        {
            case "wave": row.ZemaxIntegerParameters[1] = 0; break;
            case "field": row.ZemaxDataParameters[1] = 99; break;
            case "type": row.ZemaxDataParameters[2] = 3; break;
            case "sampling": row.ZemaxDataParameters[0] = 6; break;
            case "fractional": row.ZemaxDataParameters[0] = 1.5; break;
            case "epsilon": row.ZemaxDataParameters[2] = 2; row.ZemaxDataParameters[3] = 1; break;
            case "vertex": row.ZemaxDataParameters[4] = 1; break;
            case "term": row.ZemaxIntegerParameters[0] = -9; break;
            case "budget": row.ZemaxDataParameters[2] = 1; row.ZemaxIntegerParameters[0] = 231; row.ZemaxDataParameters[0] = 5; break;
            case "nan": row.ZemaxDataParameters[3] = double.NaN; break;
        }
        var optic = Optic.CreateCookeTriplet(); var result = MeritFunctionCatalog.Evaluate(optic, row);
        Assert.NotEmpty(result.Error); Assert.True(double.IsNaN(result.Value)); Assert.True(double.IsPositiveInfinity(result.Contribution));
        Assert.Throws<OptimizationEvaluationException>(() => MeritFunctionCatalog.EvaluateOptimizationValues(optic, [row]));
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("insufficient")]
    [InlineData("rank")]
    [InlineData("nan")]
    [InlineData("intensity")]
    public void InvalidPupilsCannotProduceSuccessfulFits(string fault)
    {
        var samples = fault switch
        {
            "empty" => Array.Empty<WavefrontSample>(),
            "insufficient" => new[] { Sample(0, 0, 1) },
            "rank" => Enumerable.Repeat(Sample(0, 0, 1), 20).ToArray(),
            "nan" => Grid((x, y) => double.NaN),
            _ => Grid((x, y) => 0).Select(s => s with { Intensity = -1 }).ToArray()
        };
        Assert.Throws<InvalidOperationException>(() => ZernikeMetrics.Fit(samples, ZernikeBasisKind.Standard, 11));
    }

    [Fact]
    public void EditsAndCancellationDoNotLeaveStaleBatchResults()
    {
        var optic = Optic.CreateCookeTriplet(); var row = Row(); var old = Value(optic, row);
        optic.SurfaceGroup.Items[^2].Thickness += .1; optic.SurfaceGroup.Renumber(); Assert.NotEqual(old, Value(optic, row));
        using (var cts = new CancellationTokenSource())
        { cts.Cancel(); using var scope = ComputationCancellation.Push(cts.Token); Assert.ThrowsAny<OperationCanceledException>(() => Value(optic, row)); }
        Assert.True(double.IsFinite(Value(optic, row)));
    }

    [Fact]
    public async Task SevenSlotsNegativeTermsAndFieldInData2SurviveProjectAndEditor()
    {
        var optic = Optic.CreateCookeTriplet(); var row = Row(-4, 2, 2, .5); optic.MeritFunctionOperands.Add(row);
        var path = Path.Combine(Path.GetTempPath(), $"zernike-{Guid.NewGuid():N}.staropt");
        try
        {
            await StarOptProjectStore.SaveAsync(new StarOptProjectDocument([optic], 0), path);
            var copy = (await StarOptProjectStore.LoadAsync(path)).Configurations[0]; var saved = Assert.Single(copy.MeritFunctionOperands);
            Assert.False(saved.CompatibilityOnly); Assert.Equal(row.ZemaxDataParameters, saved.ZemaxDataParameters);
            Assert.Equal(Value(optic, row), Value(copy, saved), 12);
        }
        finally { File.Delete(path); }
        using var app = WorkbenchApplication.Create("cooke");
        app.Optimization.SetMeritFunction([new(1,false,"ZERN",0,2,1,0,0,0,0,.25,2,0,0,"fit",
            ZemaxInt1:-4,ZemaxInt2:1,ZemaxData1:1,ZemaxData2:2,ZemaxData3:2,ZemaxData4:.5,ZemaxData5:0)]);
        var type = app.Optimization.GetMeritOperandTypes().Single(t => t.Code == "ZERN");
        var editor = new MeritOperandEditorRow(Assert.Single(app.Optimization.GetMeritFunction()), type);
        Assert.Equal(7, type.Parameters!.Count); Assert.True(editor.IsParameterEditable(6)); Assert.False(editor.IsParameterEditable(7));
        editor.Parameter4 = 1; app.Optimization.SetMeritFunction([editor.ToDto()]);
        var dto = Assert.Single(app.Optimization.GetMeritFunction()); Assert.Equal(1, dto.Field); Assert.Equal(1, dto.ZemaxData2); Assert.Equal(-4, dto.ZemaxInt1);
        Assert.Equal(.25, dto.Target); Assert.Equal(2, dto.Weight);
    }

    [Theory]
    [InlineData("field")]
    [InlineData("missing")]
    public void SnapshotValidatesRawFieldSlotAndCompleteExtendedParameters(string fault)
    {
        var optic = Optic.CreateCookeTriplet(); var row = Row();
        if (fault == "field") row.ZemaxDataParameters[1] = 99; else row.ZemaxDataParameters = row.ZemaxDataParameters[..4];
        optic.MeritFunctionOperands.Add(row);
        Assert.ThrowsAny<Exception>(() => Optic.FromSnapshot(optic.ToSnapshot()));
    }

    [Fact]
    public void UnverifiedNativeRowsStayReadOnlyAndPreserveOriginalParameters()
    {
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "zemax-123456.ZMX"));
        var optic = OpticalFormatCatalog.Import(text + "\nZERN 4 1 1 2 1 0 0 .25 2 0 0\n", ".zmx");
        var row = optic.MeritFunctionOperands.Last(); Assert.True(row.CompatibilityOnly); Assert.False(row.Enabled);
        var copy = Optic.FromSnapshot(optic.ToSnapshot()).MeritFunctionOperands.Last();
        Assert.True(copy.CompatibilityOnly); Assert.Equal(row.ZemaxDataParameters, copy.ZemaxDataParameters);
    }

    [Fact]
    public void UniformFringeAnalysisUsesSameFormalFitAndRawStatistics()
    {
        var optic = Optic.CreateCookeTriplet();
        var data = new ZernikeAnalysis(optic, ZernikeAnalysisKind.ZemaxFringe, numRings: 32, numTerms: 22, mapSize: 17, wavelengthNumber: 1, fieldNumber: 2).GenerateData();
        var fit = ZernikeMetrics.Evaluate(optic, 1, 2, 1, ZernikeBasisKind.Fringe, 22);
        Assert.Equal(fit.Chief.Rms, (double)data.Values["RmsChiefWaves"], 11);
        Assert.Equal(fit.Centroid.Rms, (double)data.Values["RmsCenterWaves"], 11);
        var coefficients = (double[])data.Values["CoefficientsWaves"];
        for (var i = 0; i < coefficients.Length; i++) Assert.Equal(fit.Coefficients[i].Value, coefficients[i], 11);
    }
}

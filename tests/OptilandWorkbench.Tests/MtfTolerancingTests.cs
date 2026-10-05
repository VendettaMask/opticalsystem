using System.Globalization;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.App.Panels;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Services;
using OptilandWorkbench.Core.Tolerancing;
using ContractDirection = OptilandWorkbench.Application.Contracts.ToleranceMtfDirection;
using CoreDirection = OptilandWorkbench.Core.Tolerancing.ToleranceMtfDirection;

namespace OptilandWorkbench.Tests;

public sealed class MtfTolerancingTests
{
    [Theory]
    [InlineData(MtfMetricKind.Fourier, CoreDirection.Average)]
    [InlineData(MtfMetricKind.Fourier, CoreDirection.Tangential)]
    [InlineData(MtfMetricKind.Fourier, CoreDirection.Sagittal)]
    [InlineData(MtfMetricKind.Fourier, CoreDirection.Minimum)]
    [InlineData(MtfMetricKind.Geometric, CoreDirection.Average)]
    [InlineData(MtfMetricKind.Geometric, CoreDirection.Tangential)]
    [InlineData(MtfMetricKind.Geometric, CoreDirection.Sagittal)]
    [InlineData(MtfMetricKind.Geometric, CoreDirection.Minimum)]
    public void PhysicalCriterionMatchesFormalMtfForEveryField(MtfMetricKind kind, CoreDirection direction)
    {
        var optic = ExampleOptic();
        var metric = new MtfToleranceMetric(optic, kind, direction, 30, wave: 1);
        var values = metric.EvaluateFields();
        Assert.Equal(optic.Fields.Count, values.Count);
        foreach (var field in values)
        {
            var formal = MtfMetrics.EvaluateGrid(optic, kind, 1, 1, field.FieldNumber, 30);
            var expected = direction switch
            {
                CoreDirection.Tangential => formal.Tangential,
                CoreDirection.Sagittal => formal.Sagittal,
                CoreDirection.Minimum => Math.Min(formal.Tangential, formal.Sagittal),
                _ => (formal.Tangential + formal.Sagittal) / 2
            };
            Assert.Equal(expected, field.Value, 12); Assert.Empty(field.Error);
        }
        var totalWeight = optic.Fields.Sum(field => field.Weight);
        var expectedAverage = values.Sum(field => field.Value * optic.Fields[field.FieldNumber - 1].Weight) / totalWeight;
        Assert.Equal(expectedAverage, metric.Evaluate(), 12);
    }

    [Fact]
    public void HigherBetterSensitivityAndInverseUseMinimumAndDecrease()
    {
        var optic = ExampleOptic();
        var amount = 0.0;
        var tolerancing = new Tolerancing { HigherIsBetter = true };
        tolerancing.AddPerturbation(new VariableRangePerturbation("defect", new DelegateVariable("defect", () => amount, value => amount = value, -2, 2), -.6, .8, false));
        tolerancing.SetCriterionEvaluator(() => .9 - Math.Abs(amount));
        tolerancing.SetFieldEvaluator(() => new[] { new ToleranceFieldValue(1, .9 - Math.Abs(amount)), new ToleranceFieldValue(2, .8 - 2 * Math.Abs(amount)) });
        var analysis = new SensitivityAnalysis(optic, tolerancing);
        var sensitivity = Assert.Single(analysis.Run());
        Assert.Equal(.1, sensitivity.WorstCriterion, 12); Assert.Equal(-.8, sensitivity.DeltaCriterion, 12);
        var inverse = Assert.Single(analysis.RunInverse(.6, fieldTargets: new Dictionary<int, double> { [1] = .6, [2] = .5 }));
        Assert.InRange(Math.Abs(inverse.Minimum.AdjustedTolerance), .14999, .150000000000001);
        Assert.InRange(inverse.Maximum.AdjustedTolerance, .14999, .150000000000001);
        Assert.Equal(0, amount); // inverse and sensitivity restore the same nominal specimen
        Assert.Throws<ArgumentOutOfRangeException>(() => analysis.RunInverse(.95));
    }

    [Theory]
    [InlineData(ToleranceCompensationAlgorithm.DampedLeastSquares)]
    [InlineData(ToleranceCompensationAlgorithm.CoordinatePatternSearch)]
    public async Task BoundedCompensationReportsRealChangesAndLeavesDocumentUnchanged(ToleranceCompensationAlgorithm algorithm)
    {
        using var app = WorkbenchApplication.Create("cooke");
        var surfaces = app.Prescription.GetSurfaces(); var gap = surfaces.Count - 2;
        var before = surfaces[gap]; var revision = app.Events.Revision;
        var request = Request() with
        {
            Operands = [new(1, true, ToleranceOperandKind.Thickness, gap, -.05, .05)],
            Criterion = ToleranceCriterion.RmsSpotRadius,
            CompensationIterations = 4,
            CompensationAlgorithm = algorithm,
            AdditionalCompensators = [new(gap, ToleranceCompensatorKind.Thickness, -.04, .04)]
        };
        var result = await app.Tolerancing.RunAsync(request);
        Assert.Equal(revision, app.Events.Revision); Assert.Equal(before.Thickness, app.Prescription.GetSurfaces()[gap].Thickness);
        var negative = Assert.Single(result.SensitivityRows).NegativeCompensators!;
        var compensation = Assert.Single(negative);
        Assert.Equal(before.Thickness, compensation.Nominal, 12);
        Assert.Equal(before.Thickness - .04, compensation.Minimum, 12);
        Assert.Equal(before.Thickness + .04, compensation.Maximum, 12);
        Assert.InRange(compensation.Value, compensation.Minimum, compensation.Maximum);
        Assert.NotEqual(compensation.Nominal - .05, compensation.Value);
    }

    [Fact]
    public async Task JointFieldAcceptanceRejectsGoodAverageWhenOneFieldFails()
    {
        using var app = WorkbenchApplication.Create("cooke");
        var request = Request() with
        {
            Trials = 3,
            Mode = ToleranceAnalysisMode.SkipSensitivity,
            YieldLimit = .000001,
            MtfSettings = new(10, Method: ToleranceMtfMethod.Geometric, FieldLimits: [new(2, 1)])
        };
        var result = await app.Tolerancing.RunAsync(request);
        Assert.Equal("0.0%", result.Statistics!.Yield);
        Assert.All(result.TrialRows, row => { Assert.False(row.Passed); Assert.True(row.AcceptanceMargin < 0); Assert.True(row.CriterionValue > request.YieldLimit); });
        Assert.Equal(0, result.FieldStatistics!.Single(row => row.FieldNumber == 2).Yield);
        var average = await app.Tolerancing.RunAsync(request with { MtfSettings = request.MtfSettings! with { SeparateFields = false, FieldLimits = null } });
        Assert.Equal("100.0%", average.Statistics!.Yield);
        var chart = ToleranceChartBuilder.Yield(result, ToleranceCriterion.Mtf, request.YieldLimit);
        Assert.Contains("0 / 3", chart.Summary); Assert.Contains("最差视场", chart.Series[0].XAxisLabel);
        Assert.Equal(AnalysisAxisQuantity.Modulation, chart.Series[0].XQuantity);
        Assert.Equal(AnalysisAxisUnit.Dimensionless, chart.Series[0].XUnit);
        Assert.Equal(AnalysisAxisQuantity.Probability, chart.Series[0].YQuantity);
    }

    [Fact]
    public async Task MtfSerialAndParallelWorkersAreDeterministic()
    {
        using var app = WorkbenchApplication.Create("cooke");
        var request = Request() with
        {
            Trials = 4,
            Mode = ToleranceAnalysisMode.SkipSensitivity,
            MtfSettings = new(20, Method: ToleranceMtfMethod.Geometric),
            YieldLimit = .2
        };
        var serial = await app.Tolerancing.RunAsync(request);
        var parallel = await app.Tolerancing.RunAsync(request with { MaxDegreeOfParallelism = 2 });
        Assert.Equal(serial.TrialRows.Select(row => row.CriterionValue), parallel.TrialRows.Select(row => row.CriterionValue));
        Assert.Equal(serial.TrialRows.Select(row => row.Passed), parallel.TrialRows.Select(row => row.Passed));
        for (var i = 0; i < serial.TrialRows.Count; i++) Assert.Equal(serial.TrialRows[i].Fields, parallel.TrialRows[i].Fields);
    }

    [Fact]
    public async Task MtfInverseIncrementEnforcesEachFieldNominalAndRssPredictsDecrease()
    {
        using var app = WorkbenchApplication.Create("cooke");
        var request = Request() with
        {
            Mode = ToleranceAnalysisMode.InverseIncrement,
            InverseValue = .001,
            MtfSettings = new(10, Method: ToleranceMtfMethod.Geometric),
            Operands = [new(1, true, ToleranceOperandKind.Thickness, 2, -1, 1)]
        };
        var result = await app.Tolerancing.RunAsync(request);
        var nominal = result.NominalFields!.ToDictionary(field => field.FieldNumber, field => field.Value);
        var row = Assert.Single(result.SensitivityRows);
        foreach (var field in row.NegativeFields!.Concat(row.PositiveFields!)) Assert.True(field.Value >= nominal[field.FieldNumber] - .001 - 1e-12);
        Assert.True(Parse(result.SensitivityStatistics!.EstimatedCriterion) <= Parse(result.SensitivityStatistics.Nominal));
        Assert.True(result.AdjustedOperands![0].Maximum <= 1); Assert.True(result.AdjustedOperands[0].Minimum >= -1);
    }

    [Theory]
    [InlineData("frequency")]
    [InlineData("sampling")]
    [InlineData("wave")]
    [InlineData("direction")]
    [InlineData("method")]
    [InlineData("duplicate-field")]
    [InlineData("missing-field")]
    [InlineData("bad-limit")]
    [InlineData("average-field-limit")]
    [InlineData("comp-bounds")]
    [InlineData("comp-glass")]
    [InlineData("comp-duplicate")]
    [InlineData("algorithm")]
    public void InvalidStudyIsRejectedBeforeRunning(string fault)
    {
        using var app = WorkbenchApplication.Create("cooke"); var request = Request(); var settings = new ToleranceMtfSettingsDto();
        request = fault switch
        {
            "frequency" => request with { MtfSettings = settings with { Frequency = double.NaN } },
            "sampling" => request with { MtfSettings = settings with { Sampling = 6 } },
            "wave" => request with { MtfSettings = settings with { Wave = 100 } },
            "direction" => request with { MtfSettings = settings with { Direction = (ContractDirection)100 } },
            "method" => request with { MtfSettings = settings with { Method = (ToleranceMtfMethod)100 } },
            "duplicate-field" => request with { MtfSettings = settings with { FieldLimits = [new(1, .1), new(1, .2)] } },
            "missing-field" => request with { MtfSettings = settings with { FieldLimits = [new(100, .2)] } },
            "bad-limit" => request with { MtfSettings = settings with { FieldLimits = [new(1, 1.1)] } },
            "average-field-limit" => request with { MtfSettings = settings with { SeparateFields = false, FieldLimits = [new(1, .2)] } },
            "comp-bounds" => request with { AdditionalCompensators = [new(2, ToleranceCompensatorKind.DecenterX, .1, .2)] },
            "comp-glass" => request with { AdditionalCompensators = [new(1, ToleranceCompensatorKind.Thickness, -.01, .01)] },
            "comp-duplicate" => request with { AdditionalCompensators = [new(2, ToleranceCompensatorKind.DecenterX, -.1, .1), new(2, ToleranceCompensatorKind.DecenterX, -.1, .1)] },
            "algorithm" => request with { CompensationAlgorithm = (ToleranceCompensationAlgorithm)100 },
            _ => throw new Exception()
        };
        var revision = app.Events.Revision;
        Assert.False(app.Tolerancing.ValidateStudy(request).IsValid);
        Assert.ThrowsAny<ArgumentException>(() => { _ = app.Tolerancing.RunAsync(request); });
        Assert.Equal(revision, app.Events.Revision);
    }

    [Fact]
    public void YieldChartCountsFailedTrialsAndUsesRawPrecision()
    {
        var result = new TolerancingResultDto("MTF", [], [
            new(1, ".5", ".5", CriterionValue: .50000001, Passed: true),
            new(2, ".5", ".5", CriterionValue: .49999999, Passed: false),
            new(3, "失效", "失效", CriterionValue: double.NegativeInfinity, Passed: false)], "");
        var chart = ToleranceChartBuilder.Yield(result, ToleranceCriterion.Mtf, .5);
        Assert.Contains("1 / 3", chart.Summary); Assert.Contains("33.333", chart.Summary);
        Assert.Equal(.49999999, chart.Series[0].Points[0].X);
        Assert.Equal(AnalysisAxisUnit.Dimensionless, chart.Series[0].XUnit);
        Assert.Equal(AnalysisAxisQuantity.Probability, chart.Series[0].YQuantity);
        Assert.True(chart.Series[0].Points[^1].Y <= chart.Series[0].Points[0].Y);
    }

    [Theory]
    [InlineData(MtfMetricKind.Geometric)]
    [InlineData(MtfMetricKind.Fourier)]
    public void MissingRayDataFailsButAboveCutoffZeroRemainsValid(MtfMetricKind kind)
    {
        var optic = ExampleOptic();
        var metric = new MtfToleranceMetric(optic, kind, CoreDirection.Minimum, 1_000_000);
        Assert.All(metric.EvaluateFields(), field => { Assert.Equal(0, field.Value); Assert.Empty(field.Error); });
        var first = optic.SurfaceGroup.Items[1];
        first.SemiDiameter = .00001;
        first.CoordinateSystem = first.CoordinateSystem with { Origin = first.CoordinateSystem.Origin with { X = 100_000 } };
        Assert.All(metric.EvaluateFields(), field => { Assert.False(double.IsFinite(field.Value)); Assert.NotEmpty(field.Error); });
        Assert.Equal(double.NegativeInfinity, metric.Evaluate());
    }

    [Fact]
    public void AfocalMtfToleranceRequiresASeparateUnitContract()
    {
        var optic = ExampleOptic(); optic.ImageSpaceAfocal = true;
        Assert.Throws<NotSupportedException>(() => new MtfToleranceMetric(optic, MtfMetricKind.Fourier, CoreDirection.Minimum, 30));
    }

    [Fact]
    public void FftMtfCompensationUsesTheFormalObjectiveAndAbsoluteNominalBounds()
    {
        var optic = ExampleOptic(); var gap = optic.SurfaceGroup.Items[^2].Number;
        var nominal = optic.SurfaceGroup.Items[^2].Thickness;
        var tolerancing = new Tolerancing();
        new MtfToleranceMetric(optic, MtfMetricKind.Fourier, CoreDirection.Minimum, 20).Configure(tolerancing);
        tolerancing.AddCompensator(new DelegateVariable("focus", () => optic.SurfaceGroup.Items[gap].Thickness,
            value => { optic.SurfaceGroup.Items[gap].Thickness = value; optic.SurfaceGroup.Items[^1].CoordinateSystem = optic.SurfaceGroup.Items[^1].CoordinateSystem with { Origin = optic.SurfaceGroup.Items[^1].CoordinateSystem.Origin with { Z = optic.SurfaceGroup.Items[gap].CoordinateSystem.Origin.Z + value } }; },
            nominal - .2, nominal + .2, .04));
        var before = tolerancing.Evaluate();
        var after = tolerancing.EvaluateCompensated(3, CancellationToken.None);
        Assert.True(after.Merit <= before.Merit + 1e-12);
        Assert.InRange(Assert.Single(after.Compensators!).Value, nominal - .2, nominal + .2);
        foreach (var field in after.Fields!)
        {
            var formal = MtfMetrics.EvaluateGrid(optic, MtfMetricKind.Fourier, 1, 0, field.FieldNumber, 20, requireValidData: true);
            Assert.Equal(Math.Min(formal.Tangential, formal.Sagittal), field.Value, 12);
        }
    }

    [Fact]
    public void SeparateFieldCompensationIncludesZeroWeightAcceptanceFields()
    {
        var optic = ExampleOptic(); optic.Fields[1].Weight = 0;
        var metric = new MtfToleranceMetric(optic, MtfMetricKind.Geometric, CoreDirection.Minimum, 20);
        var average = new Tolerancing(); metric.Configure(average);
        Assert.Equal(optic.Fields.Count - 1, average.Operands.Count);
        var separate = new Tolerancing(); metric.Configure(separate, includeZeroWeightFields: true);
        Assert.Equal(optic.Fields.Count, separate.Operands.Count);
        Assert.All(separate.Operands, operand => Assert.True(operand.Weight > 0));
        Assert.Equal(optic.Fields.Count, metric.EvaluateFields().Count);
    }

    private static double Parse(string value) => double.Parse(value, CultureInfo.InvariantCulture);
    private static Optic ExampleOptic() => Optic.CreateCookeTriplet();
    private static TolerancingRequestDto Request() => new(2, 0, 0, 0, 42, 0,
        [new(1, true, ToleranceOperandKind.Thickness, 2, -.01, .01)], ToleranceCriterion.Mtf,
        MaxDegreeOfParallelism: 1, MtfSettings: new(30));
}

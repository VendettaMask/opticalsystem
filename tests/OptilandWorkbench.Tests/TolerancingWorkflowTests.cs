using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.App.Panels;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Tolerancing;
using System.Globalization;

namespace OptilandWorkbench.Tests;

public sealed class TolerancingWorkflowTests
{
    [Fact]
    public void ToleranceHistogramUsesMonteCarloValuesToBuildRealBars()
    {
        var result = CreateChartResult("1", "2", "2", "3", "4", "5");

        var view = ToleranceChartBuilder.Histogram(result, ToleranceCriterion.RmsSpotRadius);

        var series = Assert.Single(view.Series);
        Assert.Equal(AnalysisSeriesKind.Bar, series.Kind);
        Assert.Equal("RMS 点列半径 (mm)", series.XAxisLabel);
        Assert.Equal(6, series.Points.Sum(point => point.Y));
        Assert.True(series.Points.Count >= 5);
        Assert.Equal(0, view.PlotOptions.YMinimum);
        Assert.Contains("样本数：6", view.Summary, StringComparison.Ordinal);
        Assert.Empty(view.EmptyMessage);
    }

    [Fact]
    public void ToleranceYieldUsesCumulativeDistributionAndLimitLine()
    {
        var result = CreateChartResult("4", "1", "3", "2");

        var view = ToleranceChartBuilder.Yield(result, ToleranceCriterion.RmsWavefront, 2.5);

        Assert.Equal(2, view.Series.Count);
        var cumulative = view.Series[0];
        Assert.Equal(AnalysisSeriesKind.Line, cumulative.Kind);
        Assert.Equal("RMS 波前误差 (waves)", cumulative.XAxisLabel);
        Assert.Equal(0, cumulative.Points[0].Y);
        Assert.Equal(100, cumulative.Points[^1].Y);
        Assert.True(cumulative.Points.Zip(cumulative.Points.Skip(1), (first, second) => first.Y <= second.Y).All(value => value));

        var limit = view.Series[1];
        Assert.Equal(new[] { 2.5, 2.5 }, limit.Points.Select(point => point.X));
        Assert.Equal(new[] { 0.0, 100.0 }, limit.Points.Select(point => point.Y));
        Assert.Contains("2 / 4", view.Summary, StringComparison.Ordinal);
        Assert.Contains("50%", view.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void ToleranceWizardGeneratesEditableZemaxStyleOperands()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var surfaces = application.Prescription.GetSurfaces();

        var rows = application.Tolerancing.GenerateWizard(new ToleranceWizardSettingsDto(
            1,
            surfaces.Count - 2,
            IncludeRadius: true,
            RadiusToleranceMode.Fixed,
            RadiusTolerance: 0.05,
            IncludeThickness: true,
            ThicknessTolerance: 0.03,
            IncludeDecenter: true,
            DecenterTolerance: 0.01,
            IncludeTilt: true,
            TiltToleranceDegrees: 0.02,
            IncludeRefractiveIndex: true,
            RefractiveIndexTolerance: 0.0002,
            IncludeAbbeNumber: true,
            AbbeNumberTolerance: 0.2,
            IncludeImageCompensator: true,
            CompensatorMinimum: -2,
            CompensatorMaximum: 2,
            IncludeConic: true,
            ConicTolerance: 0.01));

        Assert.NotEmpty(rows);
        Assert.Equal(Enumerable.Range(1, rows.Count), rows.Select(row => row.Index));
        Assert.Contains(rows, row => row.Kind == ToleranceOperandKind.Radius);
        Assert.Contains(rows, row => row.Kind == ToleranceOperandKind.Conic);
        Assert.Contains(rows, row => row.Kind == ToleranceOperandKind.Thickness);
        Assert.Contains(rows, row => row.Kind == ToleranceOperandKind.DecenterX);
        Assert.Contains(rows, row => row.Kind == ToleranceOperandKind.DecenterY);
        Assert.Contains(rows, row => row.Kind == ToleranceOperandKind.TiltX);
        Assert.Contains(rows, row => row.Kind == ToleranceOperandKind.TiltY);
        Assert.Contains(rows, row => row.Kind == ToleranceOperandKind.RefractiveIndex);
        Assert.Contains(rows, row => row.Kind == ToleranceOperandKind.AbbeNumber);
        Assert.Equal(ToleranceOperandKind.Compensator, rows[^1].Kind);
        Assert.Equal(surfaces.Count - 2, rows[^1].SurfaceNumber);
        Assert.Contains("像面前间隔", rows[^1].Comment, StringComparison.Ordinal);
        Assert.All(
            rows.Where(row => row.Kind is ToleranceOperandKind.DecenterX
                or ToleranceOperandKind.DecenterY
                or ToleranceOperandKind.TiltX
                or ToleranceOperandKind.TiltY),
            row => Assert.Contains("表面", row.Comment, StringComparison.Ordinal));
        Assert.True(application.Tolerancing.ValidateOperands(rows).IsValid);
    }

    private static TolerancingResultDto CreateChartResult(params string[] values) => new(
        "完成",
        Array.Empty<TolerancingSensitivityRowDto>(),
        values.Select((value, index) => new TolerancingTrialRowDto(index + 1, value, value)).ToArray(),
        "");

    [Fact]
    public void EditableToleranceOperandRowExportsCurrentGridValues()
    {
        var row = new ToleranceOperandEditorRow(new ToleranceOperandDto(
            1,
            true,
            ToleranceOperandKind.Thickness,
            1,
            -0.05,
            0.05,
            ToleranceDistribution.Normal,
            "initial"));

        row.Enabled = false;
        row.Code = "TRAD";
        row.SurfaceNumber = 3;
        row.Minimum = -0.2;
        row.Maximum = 0.3;
        row.DistributionText = "均匀";
        row.Comment = "edited in grid";

        var dto = row.ToDto();

        Assert.False(dto.Enabled);
        Assert.Equal(ToleranceOperandKind.Radius, dto.Kind);
        Assert.Equal("TRAD", row.Code);
        Assert.Equal(3, dto.SurfaceNumber);
        Assert.Equal(-0.2, dto.Minimum);
        Assert.Equal(0.3, dto.Maximum);
        Assert.Equal(ToleranceDistribution.Uniform, dto.Distribution);
        Assert.Equal("均匀", row.DistributionText);
        Assert.Equal("edited in grid", dto.Comment);
    }

    [Fact]
    public async Task ConfiguredToleranceAnalysisReturnsSensitivityTrialsAndStatistics()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var rows = application.Tolerancing.GenerateWizard(new ToleranceWizardSettingsDto(
                1,
                2,
                IncludeRadius: true,
                RadiusToleranceMode.Fixed,
                RadiusTolerance: 0.01,
                IncludeThickness: true,
                ThicknessTolerance: 0.01,
                IncludeDecenter: false,
                DecenterTolerance: 0,
                IncludeTilt: false,
                TiltToleranceDegrees: 0,
                IncludeRefractiveIndex: false,
                RefractiveIndexTolerance: 0,
                IncludeAbbeNumber: false,
                AbbeNumberTolerance: 0,
                IncludeImageCompensator: true,
                CompensatorMinimum: -1,
                CompensatorMaximum: 1))
            .ToArray();

        var result = await application.Tolerancing.RunAsync(new TolerancingRequestDto(
            1,
            0,
            0,
            Trials: 6,
            Seed: 42,
            CompensationIterations: 3,
            Operands: rows,
            Criterion: ToleranceCriterion.RmsSpotRadius,
            YieldLimit: 1));

        Assert.Equal(rows.Count(row => row.Kind != ToleranceOperandKind.Compensator), result.SensitivityRows.Count);
        Assert.All(result.SensitivityRows, row =>
        {
            Assert.False(string.IsNullOrWhiteSpace(row.NegativeMerit));
            Assert.False(string.IsNullOrWhiteSpace(row.PositiveMerit));
            Assert.False(string.IsNullOrWhiteSpace(row.WorstMerit));
        });
        Assert.Equal(6, result.TrialRows.Count);
        Assert.All(result.TrialRows, row => Assert.False(string.IsNullOrWhiteSpace(row.Degradation)));
        Assert.NotNull(result.Statistics);
        Assert.EndsWith("%", result.Statistics!.Yield, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SensitivityCanRunWithoutCreatingMonteCarloTrials()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var operand = new ToleranceOperandDto(
            1,
            true,
            ToleranceOperandKind.Thickness,
            2,
            -0.01,
            0.01);

        var result = await application.Tolerancing.RunAsync(new TolerancingRequestDto(
            2,
            0,
            0,
            Trials: 0,
            Seed: 42,
            CompensationIterations: 0,
            Operands: new[] { operand },
            Mode: ToleranceAnalysisMode.Sensitivity));

        Assert.Single(result.SensitivityRows);
        Assert.Empty(result.TrialRows);
        Assert.Null(result.Statistics);
        Assert.NotNull(result.SensitivityStatistics);
        Assert.False(string.IsNullOrWhiteSpace(result.SensitivityStatistics!.Nominal));
        Assert.False(string.IsNullOrWhiteSpace(result.SensitivityStatistics.RssEstimatedChange));
        Assert.False(string.IsNullOrWhiteSpace(result.SensitivityStatistics.EstimatedCriterion));
    }

    [Fact]
    public async Task HighNaZemaxLensKeepsValidRaysForToleranceCriterion()
    {
        using var application = WorkbenchApplication.Create();
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "zemax-ms-l7-high-na.ZMX");
        await application.Documents.OpenAsync(path);
        var operand = new ToleranceOperandDto(
            1,
            true,
            ToleranceOperandKind.Thickness,
            1,
            -0.01,
            0.01);

        var result = await application.Tolerancing.RunAsync(new TolerancingRequestDto(
            1,
            0,
            0,
            Trials: 0,
            Seed: 42,
            CompensationIterations: 0,
            Operands: new[] { operand },
            Mode: ToleranceAnalysisMode.Sensitivity));

        Assert.Single(result.SensitivityRows);
        Assert.NotNull(result.SensitivityStatistics);
    }

    [Fact]
    public async Task SkipSensitivityRunsOnlyMonteCarlo()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var operand = new ToleranceOperandDto(
            1,
            true,
            ToleranceOperandKind.Thickness,
            2,
            -0.01,
            0.01);

        var result = await application.Tolerancing.RunAsync(new TolerancingRequestDto(
            2,
            0,
            0,
            Trials: 2,
            Seed: 42,
            CompensationIterations: 0,
            Operands: new[] { operand },
            Mode: ToleranceAnalysisMode.SkipSensitivity));

        Assert.Empty(result.SensitivityRows);
        Assert.Equal(2, result.TrialRows.Count);
        Assert.NotNull(result.Statistics);
        Assert.Null(result.SensitivityStatistics);
    }

    [Fact]
    public void RangeSensitivityEvaluatesBothLimitsAndRestoresVariable()
    {
        var optic = new Optic();
        var value = 10.0;
        var variable = new DelegateVariable("x", () => value, next => value = next, -100, 100);
        var tolerancing = optic.CreateTolerancing();
        tolerancing.AddOperand(new Operand("target", 10, 1, () => value));
        tolerancing.SetCriterionEvaluator(() => value);
        tolerancing.AddPerturbation(new VariableRangePerturbation(
            "TTHI surface 1",
            variable,
            -2,
            3,
            normalDistribution: true));

        var result = new SensitivityAnalysis(optic, tolerancing).Run().Single();

        Assert.Equal(4, result.NegativeMerit, precision: 12);
        Assert.Equal(9, result.PositiveMerit, precision: 12);
        Assert.Equal(9, result.WorstMerit, precision: 12);
        Assert.Equal(9, result.DeltaMerit, precision: 12);
        Assert.Equal(8, result.NegativeCriterion, precision: 12);
        Assert.Equal(13, result.PositiveCriterion, precision: 12);
        Assert.Equal(13, result.WorstCriterion, precision: 12);
        Assert.Equal(3, result.DeltaCriterion, precision: 12);
        Assert.Equal(10, value, precision: 12);
    }

    [Fact]
    public void ModifiedGaussianUsesToleranceMidpointWithoutEndpointClamping()
    {
        var optic = new Optic();
        var value = 0.0;
        var variable = new DelegateVariable("x", () => value, next => value = next, -100, 100);
        var perturbation = new VariableRangePerturbation(
            "asymmetric",
            variable,
            -1,
            3,
            normalDistribution: true);
        var random = new Random(12345);
        var samples = new double[20_000];

        for (var index = 0; index < samples.Length; index++)
        {
            perturbation.Apply(optic, random);
            samples[index] = value;
            perturbation.Revert(optic);
        }

        Assert.All(samples, sample => Assert.InRange(sample, -1.0, 3.0));
        Assert.DoesNotContain(samples, sample => sample == -1.0 || sample == 3.0);
        Assert.InRange(samples.Average(), 0.98, 1.02);
        var standardDeviation = Math.Sqrt(samples.Select(sample => Math.Pow(sample - samples.Average(), 2)).Average());
        Assert.InRange(standardDeviation, 0.85, 0.91);
    }

    [Fact]
    public async Task MonteCarloRowsAndStatisticsUseActualCriterion()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var operand = new ToleranceOperandDto(
            1,
            true,
            ToleranceOperandKind.Thickness,
            2,
            0.01,
            0.01,
            ToleranceDistribution.Uniform,
            "fixed spacing change");

        var result = await application.Tolerancing.RunAsync(new TolerancingRequestDto(
            2,
            0,
            0,
            Trials: 1,
            Seed: 42,
            CompensationIterations: 0,
            Operands: new[] { operand },
            Criterion: ToleranceCriterion.RmsSpotRadius,
            YieldLimit: 0));

        var nominal = Parse(result.Statistics!.Nominal);
        var criterion = Parse(result.TrialRows.Single().CompensatedMerit);
        var degradation = Parse(result.TrialRows.Single().Degradation);
        var mean = Parse(result.Statistics.Mean);

        Assert.InRange(Math.Abs((criterion - nominal) - degradation), 0, 2e-6);
        Assert.Equal(criterion, mean, precision: 10);
        Assert.True(criterion > 0);
    }

    [Fact]
    public async Task ImageSpacingCompensatorChangesRealOpticalCriterion()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var surfaces = application.Prescription.GetSurfaces();
        var operands = new[]
        {
            new ToleranceOperandDto(
                1,
                true,
                ToleranceOperandKind.Radius,
                2,
                0.5,
                0.5,
                ToleranceDistribution.Uniform,
                "fixed radius change"),
            new ToleranceOperandDto(
                2,
                true,
                ToleranceOperandKind.Compensator,
                surfaces.Count - 2,
                -5,
                5,
                ToleranceDistribution.Uniform,
                "image spacing")
        };

        var result = await application.Tolerancing.RunAsync(new TolerancingRequestDto(
            2,
            0,
            0,
            Trials: 1,
            Seed: 7,
            CompensationIterations: 20,
            Operands: operands,
            Criterion: ToleranceCriterion.RmsSpotRadius,
            YieldLimit: 0));

        var before = Parse(result.TrialRows.Single().Merit);
        var after = Parse(result.TrialRows.Single().CompensatedMerit);

        Assert.True(after <= before + 1e-9);
        Assert.True(Math.Abs(after - before) > 1e-8);
    }

    [Fact]
    public void InvalidCriterionMarksMonteCarloTrialAsInvalid()
    {
        var optic = new Optic();
        var value = 0.0;
        var variable = new DelegateVariable("x", () => value, next => value = next, -10, 10);
        var tolerancing = optic.CreateTolerancing();
        tolerancing.AddOperand(new Operand("target", 0, 1, () => value));
        tolerancing.AddPerturbation(new VariablePerturbation(
            "constant",
            variable,
            new ConstantSampler(1)));
        tolerancing.SetCriterionEvaluator(() => double.PositiveInfinity);

        var result = new MonteCarlo(optic, tolerancing).RunDetailed(1).Single();

        Assert.False(result.IsValid);
        Assert.True(double.IsPositiveInfinity(result.Criterion));
        Assert.True(double.IsPositiveInfinity(result.CompensatedCriterion));
    }

    [Fact]
    public void ValidationRejectsNegativePhysicalSpacingAndImageSurfaceCompensator()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var surfaces = application.Prescription.GetSurfaces();
        var spacingSurface = surfaces.Count - 2;
        var spacing = surfaces[spacingSurface].Thickness;
        var operands = new[]
        {
            new ToleranceOperandDto(
                1,
                true,
                ToleranceOperandKind.Thickness,
                spacingSurface,
                -spacing - 0.1,
                0.1),
            new ToleranceOperandDto(
                2,
                true,
                ToleranceOperandKind.Compensator,
                surfaces.Count - 1,
                0,
                1)
        };

        var result = application.Tolerancing.ValidateOperands(operands);

        Assert.False(result.IsValid);
        Assert.Contains(result.Messages, message => message.Contains("负的厚度/间隔", StringComparison.Ordinal));
        Assert.Contains(result.Messages, message => message.Contains("像面之前的间隔", StringComparison.Ordinal));
    }

    private static double Parse(string value) =>
        double.Parse(value, NumberStyles.Float, CultureInfo.CurrentCulture);
}

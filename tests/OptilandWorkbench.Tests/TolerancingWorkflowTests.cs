using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Tolerancing;

namespace OptilandWorkbench.Tests;

public sealed class TolerancingWorkflowTests
{
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
            CompensatorMaximum: 2));

        Assert.NotEmpty(rows);
        Assert.Equal(Enumerable.Range(1, rows.Count), rows.Select(row => row.Index));
        Assert.Contains(rows, row => row.Kind == ToleranceOperandKind.Radius);
        Assert.Contains(rows, row => row.Kind == ToleranceOperandKind.Thickness);
        Assert.Contains(rows, row => row.Kind == ToleranceOperandKind.DecenterX);
        Assert.Contains(rows, row => row.Kind == ToleranceOperandKind.DecenterY);
        Assert.Contains(rows, row => row.Kind == ToleranceOperandKind.TiltX);
        Assert.Contains(rows, row => row.Kind == ToleranceOperandKind.TiltY);
        Assert.Contains(rows, row => row.Kind == ToleranceOperandKind.RefractiveIndex);
        Assert.Contains(rows, row => row.Kind == ToleranceOperandKind.AbbeNumber);
        Assert.Equal(ToleranceOperandKind.Compensator, rows[^1].Kind);
        Assert.True(application.Tolerancing.ValidateOperands(rows).IsValid);
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
    public void RangeSensitivityEvaluatesBothLimitsAndRestoresVariable()
    {
        var optic = new Optic();
        var value = 10.0;
        var variable = new DelegateVariable("x", () => value, next => value = next, -100, 100);
        var tolerancing = optic.CreateTolerancing();
        tolerancing.AddOperand(new Operand("target", 10, 1, () => value));
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
        Assert.Equal(10, value, precision: 12);
    }
}

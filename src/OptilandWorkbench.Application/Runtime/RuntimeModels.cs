using System.Collections.ObjectModel;
using System.Globalization;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Formatting;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apodization;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Multiconfig;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Phase;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Services;
using OptilandWorkbench.Core.Tolerancing;
using ContractMeritFunctionPreset = OptilandWorkbench.Application.Contracts.MeritFunctionPreset;

namespace OptilandWorkbench.Application.Runtime;

public sealed record AnalysisView(
    string Name,
    IReadOnlyList<AnalysisRow> Rows,
    string ReportText,
    AnalysisSeries? Series,
    IReadOnlyList<AnalysisSeries> SeriesList,
    AnalysisPlotOptions PlotOptions,
    IReadOnlyList<AnalysisPlotPane> PlotPanes,
    int PlotPaneColumns,
    AnalysisTable? Table = null,
    InterferogramSummaryDto? InterferogramSummary = null,
    Core.Analysis.AnalysisOutcome Outcome = Core.Analysis.AnalysisOutcome.Success,
    string? OutcomeReason = null);

public sealed record AnalysisRow(string Metric, string Value);

public enum AnalysisParameterKind
{
    Integer,
    Double,
    Choice,
    Boolean,
    File
}

public sealed record AnalysisParameterDescriptor(
    string Key,
    string DisplayName,
    AnalysisParameterKind Kind,
    string DefaultValue,
    double Minimum = 0,
    double Maximum = 1,
    double Increment = 1,
    IReadOnlyList<string>? Choices = null);

public sealed record TolerancingView(
    string Summary,
    IReadOnlyList<TolerancingSensitivityRow> SensitivityRows,
    IReadOnlyList<TolerancingTrialRow> TrialRows,
    string Details,
    TolerancingStatistics? Statistics = null,
    TolerancingSensitivityStatistics? SensitivityStatistics = null,
    IReadOnlyList<TolerancingInverseRow>? InverseRows = null,
    IReadOnlyList<ToleranceOperandDto>? AdjustedOperands = null,
    string InverseTarget = "",
    ToleranceMtfSettingsDto? MtfSettings = null,
    IReadOnlyList<ToleranceFieldValueDto>? NominalFields = null,
    IReadOnlyList<ToleranceFieldStatisticsDto>? FieldStatistics = null,
    IReadOnlyList<ToleranceCompensatorValueDto>? NominalCompensators = null)
{
    public static TolerancingView Empty(string message)
    {
        return new TolerancingView(message, Array.Empty<TolerancingSensitivityRow>(), Array.Empty<TolerancingTrialRow>(), message);
    }
}

public sealed record TolerancingSensitivityRow(
    string Perturbation,
    string DeltaMerit,
    string NegativeMerit = "",
    string PositiveMerit = "",
    string WorstMerit = "",
    IReadOnlyList<ToleranceFieldValueDto>? NegativeFields = null,
    IReadOnlyList<ToleranceFieldValueDto>? PositiveFields = null,
    IReadOnlyList<ToleranceCompensatorValueDto>? NegativeCompensators = null,
    IReadOnlyList<ToleranceCompensatorValueDto>? PositiveCompensators = null);

public sealed record TolerancingTrialRow(
    int Trial,
    string Merit,
    string CompensatedMerit,
    string Degradation = "",
    IReadOnlyList<ToleranceFieldValueDto>? Fields = null,
    IReadOnlyList<ToleranceFieldValueDto>? UncompensatedFields = null,
    IReadOnlyList<ToleranceCompensatorValueDto>? Compensators = null,
    bool? Passed = null,
    double? AcceptanceMargin = null,
    double? CriterionValue = null);

public sealed record TolerancingStatistics(
    string Nominal,
    string Mean,
    string StandardDeviation,
    string Minimum,
    string Maximum,
    string Percentile50,
    string Percentile90,
    string Percentile95,
    string Yield,
    string Percentile05 = "");

public sealed record TolerancingSensitivityStatistics(
    string Nominal,
    string RssEstimatedChange,
    string EstimatedCriterion);

public sealed record TolerancingInverseEndpoint(
    string OriginalTolerance,
    string AdjustedTolerance,
    string Criterion,
    ToleranceInverseEndpointStatus Status,
    int Iterations);

public sealed record TolerancingInverseRow(
    string Perturbation,
    TolerancingInverseEndpoint Minimum,
    TolerancingInverseEndpoint Maximum);

public sealed record MultiConfigurationRow(int Index, string Name, bool Active, int SurfaceCount, string TotalTrack, string EffectiveFocalLength);

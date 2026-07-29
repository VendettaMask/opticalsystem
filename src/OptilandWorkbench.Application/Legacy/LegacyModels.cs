using System.Collections.ObjectModel;
using System.Globalization;
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

namespace OptilandWorkbench.Application.Legacy;

public sealed record AnalysisView(
    string Name,
    IReadOnlyList<AnalysisRow> Rows,
    string ReportText,
    AnalysisSeries? Series,
    IReadOnlyList<AnalysisSeries> SeriesList,
    AnalysisPlotOptions PlotOptions,
    IReadOnlyList<AnalysisPlotPane> PlotPanes,
    int PlotPaneColumns,
    AnalysisTable? Table = null);

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
    TolerancingStatistics? Statistics = null)
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
    string WorstMerit = "");

public sealed record TolerancingTrialRow(
    int Trial,
    string Merit,
    string CompensatedMerit,
    string Degradation = "");

public sealed record TolerancingStatistics(
    string Nominal,
    string Mean,
    string StandardDeviation,
    string Minimum,
    string Maximum,
    string Percentile50,
    string Percentile90,
    string Percentile95,
    string Yield);

public sealed record MultiConfigurationRow(int Index, string Name, bool Active, int SurfaceCount, string TotalTrack, string EffectiveFocalLength);

using OpticalSystemDesign.Core.Optimization;
using OpticalSystemDesign.Core.Serialization;

namespace OpticalSystemDesign.Core.NonSequential;

public enum NonSequentialDesignRunKind { Optimization, Tolerance }
public sealed record NonSequentialOptimizationRecord(string Algorithm, string SourceSceneSha256,
    IReadOnlyList<double> InitialValues, IReadOnlyList<double> BestValues,
    IReadOnlyList<NonSequentialMetricValue> InitialMetrics, IReadOnlyList<NonSequentialMetricValue> TrainingMetrics,
    IReadOnlyList<NonSequentialMetricValue> ValidationMetrics, bool Passed, OptimizerResult Optimizer, NonSequentialDesignCost Cost)
{
    public static NonSequentialOptimizationRecord From(NonSequentialDesignResult r) => new(r.Algorithm, r.SourceSceneSha256,
        r.InitialValues, r.BestValues, r.InitialMetrics, r.TrainingMetrics, r.ValidationMetrics, r.Passed, r.Optimizer, r.Cost);
}
public sealed record NonSequentialDesignRecordedRun(NonSequentialOptimizationRecord? Optimization = null,
    NonSequentialToleranceResult? Tolerance = null);
public sealed record NonSequentialDesignMaterial(string Alias, ComponentSnapshot Model);
public sealed record NonSequentialArchiveEntry(string Name, long Bytes, string Sha256);
public sealed record NonSequentialDesignArchiveManifest(string Format, int Version, NonSequentialDesignRunKind Kind,
    string OptimizationProtocol, string ToleranceProtocol, string SourceSceneSha256, IReadOnlyList<NonSequentialArchiveEntry> Entries);
public sealed record NonSequentialDesignArchiveInfo(NonSequentialDesignArchiveManifest Manifest,
    NonSequentialDesignExperiment Plan, NonSequentialDesignRecordedRun? Recorded);
public sealed record NonSequentialDesignReplayResult(NonSequentialDesignArchiveInfo Archive,
    NonSequentialDesignResult? Optimization, NonSequentialToleranceResult? Tolerance, bool? MatchesRecorded,
    string Comparison = "Exact canonical JSON of deterministic numeric results, costs and optimizer diagnostics; no external engine");

using System.Collections.Immutable;
using OpticalSystemDesign.Core.Optimization;

namespace OpticalSystemDesign.InitialStructure.Contracts;

public enum SpcPreparationPhase
{
    SelectParent, OriginalAssessment, ParentOptimization, ParentAssessment, ParentCurvature, NullMerit, SaddleCurvature,
    StartBranch, BranchOptimization, BranchCurvature, PhysicalSetup, PhysicalOptimization, PhysicalAssessment, FinishBranch, Finished
}

/// <summary>Complete numerical continuation. Original plan, quotas and kernel identity belong to its preparation checkpoint.</summary>
public sealed record SpcPreparationState
{
    public int Version { get; init; } = 1;
    public SpcPreparationPhase Phase { get; init; }
    public ImmutableArray<int> Order { get; init; } = [];
    public int ParentCursor { get; init; }
    public string? SiteId { get; init; }
    public DesignTopology? StationaryParent { get; init; }
    public TopologyAssessment? OriginalAssessment { get; init; }
    public TopologyAssessment? ParentAssessment { get; init; }
    public ImmutableArray<TopologyVariableRequest> Variables { get; init; } = [];
    public StationaryContinuation? Optimizer { get; init; }
    public StationaryCurvatureContinuation? Curvature { get; init; }
    public StationarySpectrum? Coarse { get; init; }
    public SpcStationarityEvidence? ParentMinimum { get; init; }
    public SpcStationarityEvidence? Saddle { get; init; }
    public StationaryValue? NullMerit { get; init; }
    public int Sign { get; init; } = -1;
    public ImmutableArray<double> InitialCoordinates { get; init; } = [];
    public StationaryValue? InitialValue { get; init; }
    public StationaryContinuation? BranchMinimum { get; init; }
    public SpcStationarityEvidence? BranchCertificate { get; init; }
    public int ThicknessCursor { get; init; }
    public int MaterialCursor { get; init; }
    public DesignTopology? PhysicalSource { get; init; }
    public FixedTopologyOptions? PhysicalOptions { get; init; }
    public FixedTopologyContinuation? PhysicalContinuation { get; init; }
    public DesignTopology? PhysicalOutput { get; init; }
    public AutonomousSearchWork PhysicalBase { get; init; } = new();
    public AutonomousSearchWork StageBase { get; init; } = new();
    public string? PhysicalStageName { get; init; }
    public DesignTopology? MaterialBase { get; init; }
    public DesignTopology? BestPhysical { get; init; }
    public TopologyAssessment? BestAssessment { get; init; }
    public ImmutableArray<SpcPhysicalStage> Stages { get; init; } = [];
    public ImmutableArray<SpcParentEvidence> Parents { get; init; } = [];
    public ImmutableArray<SpcOptimizedBranch> Branches { get; init; } = [];
    public ImmutableArray<AutonomousSearchCandidate> Pool { get; init; } = [];
}

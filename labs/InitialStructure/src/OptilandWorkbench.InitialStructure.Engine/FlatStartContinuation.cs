using OptilandWorkbench.InitialStructure.Contracts;

namespace OptilandWorkbench.InitialStructure.Engine;

/// <summary>Deterministic continuation driven by feasibility and solver progress, not a stage budget split.</summary>
internal sealed class FlatStartContinuation
{
    // New area-integrated searches reserve a local-model window at reduced difficulty
    // before spending the remaining budget at the full target. Legacy replay stays explicit.
    public static IReadOnlyList<DesignStage> ProgressiveStages { get; } =
        [new(.3, 0, false), new(.65, .5, true), FlatStartDesignProblem.FullStage];

    public static int ProgressiveWindow(int remaining, int dimension, int stageIndex)
    {
        if (stageIndex == ProgressiveStages.Count - 1) return remaining;
        var modelWindow = 2 * dimension + 3;
        var fullReserve = Math.Min(modelWindow, remaining);
        return Math.Min(Math.Max(0, remaining - fullReserve),
            Math.Max(modelWindow, remaining / (ProgressiveStages.Count - stageIndex)));
    }

    public DesignStage Established { get; private set; } = new(.3, 0, false);
    public DesignStage Proposed { get; private set; } = FlatStartDesignProblem.FullStage;
    public int Backoffs { get; private set; }
    public bool Exhausted => Backoffs >= 8;

    public void Accept()
    {
        Established = Proposed;
        Proposed = FlatStartDesignProblem.FullStage;
        Backoffs = 0;
    }

    public void Backoff()
    {
        Proposed = new((Established.PupilFraction + Proposed.PupilFraction) / 2,
            (Established.FieldFraction + Proposed.FieldFraction) / 2,
            Proposed.AllWavelengths && (Established.AllWavelengths || Backoffs < 3));
        Backoffs++;
    }

    public static double Violation(DesignEvaluation evaluation)
    {
        return !evaluation.GeometryFeasible ? evaluation.GeometryResiduals.Sum(value => value * value)
            : evaluation.HasContinuousSearchResiduals ? evaluation.Merit : double.PositiveInfinity;
    }

    public static int FeasibilityRank(DesignEvaluation evaluation) => !evaluation.GeometryFeasible ? 0
        : !evaluation.HasContinuousSearchResiduals ? 1 : 2;
}

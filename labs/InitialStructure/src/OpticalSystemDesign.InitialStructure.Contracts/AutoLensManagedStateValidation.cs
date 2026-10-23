using OpticalSystemDesign.Core.Serialization;

namespace OpticalSystemDesign.InitialStructure.Contracts;

public static class AutoLensManagedStateValidation
{
    public static void Validate(AutoLensManagedState state)
    {
        if (state.PropagationExecution is { } gpu && (gpu.DeviceName is not {Length:>0 and <=512}
            || gpu.AimRays<0 || gpu.AimTraces<0 || gpu.PropagatedRays<0 || gpu.Batches<0 || gpu.CpuRecoveryRays<0
            || gpu.CpuRecoveryRays>gpu.AimRays || !double.IsFinite(gpu.Milliseconds) || gpu.Milliseconds<0
            || gpu.FallbackReason is {Length:>4096}
            || gpu.Batches==0 && (gpu.AimRays!=0 || gpu.PropagatedRays!=0 || gpu.AimTraces!=0 || gpu.Milliseconds!=0)))
            throw new InvalidDataException("Invalid AutoLens CUDA aiming/physical propagation evidence.");
        if (state.GpuBatches < 0 || state.GpuRays < 0 || !double.IsFinite(state.GpuMilliseconds) || state.GpuMilliseconds < 0
            || state.LastComputeExecution is { } execution && (execution.Backend is not ("cpu-forward-v1" or "cuda-reverse-tape-v1")
                || execution.DeviceName is not { Length: > 0 and <= 512 } || execution.GpuBatches < 0 || execution.GpuRays < 0
                || !double.IsFinite(execution.GpuMilliseconds) || execution.GpuMilliseconds < 0
                || execution.Backend == "cpu-forward-v1" && (execution.GpuBatches != 0 || execution.GpuRays != 0 || execution.GpuMilliseconds != 0)
                || execution.Backend == "cuda-reverse-tape-v1" && (execution.GpuBatches == 0 || execution.GpuRays == 0 || execution.FallbackReason is not null)
                || state.GpuBatches < execution.GpuBatches || state.GpuRays < execution.GpuRays
                || execution.FallbackReason is { Length: > 4096 }))
            throw new InvalidDataException("Invalid AutoLens compute-device evidence.");
        if (state.SchemaVersion != 1 || !Enum.IsDefined(state.Phase)
            || !Hash(state.SpecificationFingerprint) || !Hash(state.AllowedGlassFingerprint) || !Hash(state.RootFingerprint)
            || state.PlannedCurriculumIterations is < 0 or > 10000 || state.PlannedRefinementIterations is < 0 or > 10000
            || state.EvaluationCount is < 0 or > 1000000 || state.TracedRayCount < 0
            || state.ParentCandidateId is { } parent && (string.IsNullOrWhiteSpace(parent) || parent.Length > 256)
            || state.ElapsedComputeTicks < 0 || state.ElapsedComputeTicks > 2 * InitialStructureLimits.MaximumTimeLimit.Ticks
            || state.TargetPolishEvaluationCount < 0 || state.TargetPolishEvaluationCount > state.EvaluationCount
            || state.LastLoss is { } loss && (!double.IsFinite(loss) || loss < 0)
            || state.Parameters is not { Count: > 0 and <= 512 } || state.Adam is null
            || state.Adam.Iteration is < 0 or > 10000 || state.Adam.Coordinates?.Count != state.Parameters.Count
            || state.Adam.MomentIteration < 0 || state.Adam.MomentIteration > state.Adam.Iteration
            || state.Adam.FirstMoment?.Count != state.Parameters.Count || state.Adam.SecondMoment?.Count != state.Parameters.Count
            || state.Adam.Coordinates.Any(value => !double.IsFinite(value))
            || state.Adam.FirstMoment.Any(value => !double.IsFinite(value))
            || state.Adam.SecondMoment.Any(value => !double.IsFinite(value) || value < 0)
            || state.Adam.MomentIteration == 0 && (state.Adam.FirstMoment.Any(value => value != 0) || state.Adam.SecondMoment.Any(value => value != 0))
            || state.GlassMatches is not { Count: <= 8 } || state.TrainingSteps is not { Count: <= 20000 } || state.Optic is null)
            throw new InvalidDataException("Invalid native AutoLens design state.");
        OpticSnapshotValidator.Validate(state.Optic);
        if (state.FullTargetIncumbent is not null) OpticSnapshotValidator.Validate(state.FullTargetIncumbent);
        if (state.TrainingSteps.Any(step => step is null || step.Phase is not (AutoLensManagedPhase.Curriculum or AutoLensManagedPhase.CatalogRefinement)
            || step.Iteration is < 1 or > 10000 || !double.IsFinite(step.PupilFraction) || step.PupilFraction is < .5 or > 1
            || !double.IsFinite(step.BeforeLoss) || !double.IsFinite(step.AfterLoss) || step.AfterLoss < 0 || step.AfterLoss >= step.BeforeLoss
            || !double.IsFinite(step.StepFraction) || step.StepFraction is <= 0 or > 1 || step.ChargedEvaluation < 1 || step.ChargedEvaluation > state.EvaluationCount))
            throw new InvalidDataException("Native AutoLens step evidence does not show a finite measured decrease.");
        if (state.Phase == AutoLensManagedPhase.CatalogProjection
            || state.Phase == AutoLensManagedPhase.Curriculum && state.Adam.Iteration > state.PlannedCurriculumIterations
            || state.Phase == AutoLensManagedPhase.CatalogRefinement && state.Adam.Iteration > state.PlannedRefinementIterations
            || state.Phase == AutoLensManagedPhase.Completed && (state.Adam.Iteration != state.PlannedRefinementIterations
                || state.Parameters.Any(parameter => parameter.Kind is Core.Differentiation.OpticalDesignParameterKind.MaterialIndex or Core.Differentiation.OpticalDesignParameterKind.MaterialAbbe)))
            throw new InvalidDataException("Native AutoLens phase and optimizer state disagree.");
        if (state.Parameters.Any(parameter => parameter is null || !Enum.IsDefined(parameter.Kind)
                || parameter.SurfaceIndex <= 0 || parameter.SurfaceIndex >= state.Optic.Surfaces.Count
                || parameter.LinkedSurfaceIndex is { } linked && (linked <= 0 || linked >= state.Optic.Surfaces.Count || linked == parameter.SurfaceIndex)
                || !double.IsFinite(parameter.Scale) || parameter.Scale <= 0 || parameter.CoefficientIndex < 0)
            || state.Parameters.Select(parameter => (parameter.SurfaceIndex, parameter.Kind, parameter.CoefficientIndex)).Distinct().Count() != state.Parameters.Count
            || state.GlassMatches.Any(match => match is null || string.IsNullOrWhiteSpace(match.SourceName) || string.IsNullOrWhiteSpace(match.CatalogName)
                || !double.IsFinite(match.Distance) || match.Distance < 0
                || !double.IsFinite(match.RelaxedIndex) || !double.IsFinite(match.RelaxedAbbe)
                || !double.IsFinite(match.CatalogIndex) || !double.IsFinite(match.CatalogAbbe)))
            throw new InvalidDataException("Invalid native AutoLens parameter or material mapping.");
    }

    private static bool Hash(string value) => value is { Length: 64 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}

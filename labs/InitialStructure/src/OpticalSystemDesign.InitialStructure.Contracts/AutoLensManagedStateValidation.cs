using OpticalSystemDesign.Core.Serialization;

namespace OpticalSystemDesign.InitialStructure.Contracts;

public static class AutoLensManagedStateValidation
{
    public static void Validate(AutoLensManagedState state)
    {
        var reference = state.SchemaVersion == 2;
        if (state.PropagationExecution is { } gpu && (gpu.DeviceName is not {Length:>0 and <=512}
            || gpu.AimRays<0 || gpu.AimTraces<0 || gpu.PropagatedRays<0 || gpu.Batches<0 || gpu.CpuRecoveryRays<0 || gpu.CpuAimingRays < 0
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
        if (state.SchemaVersion is not (1 or 2) || !Enum.IsDefined(state.Phase) || !reference && state.Phase is AutoLensManagedPhase.Finalization or AutoLensManagedPhase.PhysicalValidation
            || !Hash(state.SpecificationFingerprint) || !Hash(state.AllowedGlassFingerprint) || !Hash(state.RootFingerprint)
            || state.PlannedCurriculumIterations is < 0 or > 10000 || state.PlannedRefinementIterations is < 0 or > 10000
            || state.EvaluationCount is < 0 or > 1000000 || state.TracedRayCount < 0
            || state.ParentCandidateId is { } parent && (string.IsNullOrWhiteSpace(parent) || parent.Length > 256)
            || state.ElapsedComputeTicks < 0 || state.ElapsedComputeTicks > 2 * InitialStructureLimits.MaximumTimeLimit.Ticks
            || state.TargetPolishEvaluationCount < 0 || state.TargetPolishEvaluationCount > state.EvaluationCount
            || state.LastLoss is { } loss && (!double.IsFinite(loss) || loss < 0)
            || state.Parameters is not { Count: > 0 and <= 512 } || state.Adam is null
            || state.Adam.Iteration < 0 || state.Adam.Iteration > (reference ? 10001 : 10000) || state.Adam.Coordinates?.Count != state.Parameters.Count
            || state.Adam.MomentIteration < 0 || state.Adam.MomentIteration > state.Adam.Iteration
            || state.Adam.FirstMoment?.Count != state.Parameters.Count || state.Adam.SecondMoment?.Count != state.Parameters.Count
            || state.Adam.Coordinates.Any(value => !double.IsFinite(value))
            || state.Adam.FirstMoment.Any(value => !double.IsFinite(value))
            || state.Adam.SecondMoment.Any(value => !double.IsFinite(value) || value < 0)
            || state.Adam.MomentIteration == 0 && (state.Adam.FirstMoment.Any(value => value != 0) || state.Adam.SecondMoment.Any(value => value != 0))
            || state.GlassMatches is not { Count: <= 8 } || state.TrainingSteps is null || state.TrainingSteps.Count > (reference ? 20002 : 20000) || state.Optic is null)
            throw new InvalidDataException("Invalid native AutoLens design state.");
        OpticSnapshotValidator.Validate(state.Optic);
        if (state.FullTargetIncumbent is not null) OpticSnapshotValidator.Validate(state.FullTargetIncumbent);
        if (state.TrainingSteps.Any(step => step is null || step.Phase is not (AutoLensManagedPhase.Curriculum or AutoLensManagedPhase.CatalogRefinement)
            || step.Iteration < 1 || step.Iteration > (reference ? 10001 : 10000) || !double.IsFinite(step.PupilFraction) || step.PupilFraction is < .5 or > 1
            || !double.IsFinite(step.BeforeLoss) || !double.IsFinite(step.AfterLoss) || step.AfterLoss < 0
            || (reference ? InvalidReferenceStep(step) : step.IsPreUpdateObservation || step.AfterLoss >= step.BeforeLoss)
            || !double.IsFinite(step.StepFraction) || step.StepFraction is <= 0 or > 1 || step.ChargedEvaluation < 1 || step.ChargedEvaluation > state.EvaluationCount))
            throw new InvalidDataException("Native AutoLens step evidence does not show a finite measured decrease.");
        if (state.Phase == AutoLensManagedPhase.CatalogProjection
            || state.Phase == AutoLensManagedPhase.Curriculum && state.Adam.Iteration > state.PlannedCurriculumIterations + (reference ? 1 : 0)
            || state.Phase == AutoLensManagedPhase.CatalogRefinement && state.Adam.Iteration > state.PlannedRefinementIterations + (reference ? 1 : 0)
            || state.Phase is AutoLensManagedPhase.Completed or AutoLensManagedPhase.Finalization or AutoLensManagedPhase.PhysicalValidation && (state.Adam.Iteration != state.PlannedRefinementIterations + (reference ? 1 : 0)
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
        if (reference)
        {
            if (state.TrainingStartOptic is null || !double.IsFinite(state.ReferenceFocalLength) || state.ReferenceFocalLength <= 0
                || !double.IsFinite(state.ReferenceHalfFieldRadians) || state.ReferenceHalfFieldRadians is < 0 or >= (Math.PI / 2)
                || !double.IsFinite(state.ReferenceEffectiveHalfFieldRadians) || state.ReferenceEffectiveHalfFieldRadians is < 0 or >= (Math.PI / 2)
                || state.ReferenceLearningRates?.Count != state.Parameters.Count || state.ReferenceLearningRates.Any(rate => !double.IsFinite(rate) || rate < 0)
                || state.TargetPolishEvaluationCount != 0 || state.FullTargetIncumbent is not null
                || state.Adam.MomentIteration != state.Adam.Iteration || !state.Parameters.Any(parameter => parameter.Kind == Core.Differentiation.OpticalDesignParameterKind.AxialPosition)
                || state.Parameters.Any(parameter => parameter.Kind == Core.Differentiation.OpticalDesignParameterKind.Thickness || !double.IsFinite(parameter.OriginOffset)))
                throw new InvalidDataException("Incomplete reference protocol, sampling, parameter rates or optimizer state.");
            OpticSnapshotValidator.Validate(state.TrainingStartOptic);
            if (state.ReferenceSampling is { } sampling)
            {
                if (sampling.Iteration < 0 || sampling.Iteration > state.Adam.Iteration || !double.IsFinite(sampling.PupilFraction)
                    || sampling.PupilFraction is < .5 or > 1) throw new InvalidDataException("Invalid fixed-input sampling checkpoint.");
                OpticSnapshotValidator.Validate(sampling.Optic);
            }
        }
    }

    private static bool InvalidReferenceStep(AutoLensManagedStep step) =>
        !step.IsPreUpdateObservation || step.BeforeLoss != step.AfterLoss || step.StepFraction != 1;

    private static bool Hash(string value) => value is { Length: 64 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}

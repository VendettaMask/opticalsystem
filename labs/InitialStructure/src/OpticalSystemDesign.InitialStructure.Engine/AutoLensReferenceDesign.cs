using System.Diagnostics;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine.Optimization;

namespace OpticalSystemDesign.InitialStructure.Engine;

/// <summary>The e58 default mobile design protocol, orchestrated entirely in C# over the shared Core.</summary>
internal static class AutoLensReferenceDesign
{
    public static FlatStartDesignResult Run(InitialStructureSpecification spec, int elements, FlatStartBootstrapResult bootstrap,
        Stopwatch clock, FlatStartFamily? family, IReadOnlyList<string> allowed, CancellationToken token,
        AutoLensManagedState? resume = null, OpticSnapshot? restart = null, string? parent = null)
    {
        var settings = spec.FlatStart!.AutoLens!;
        var samplingSettings = settings.TrainingSampling ?? new();
        using var ownedCompute = SequentialGpuSession.Current is null ? new SequentialGpuSession(settings.ComputeMode, token) : null;
        var diagnostics = new List<SearchDiagnostic>
        {
            new("design.autolens-upstream-e58-v2", "Pinned e58 mobile protocol: random weak-curvature finite-object start, independent axial positions, cached un-aimed RGB input rays, detached green reference/weights, linear constraints, direct Adam, upstream schedules and full phase lengths. Shared double-precision Core and an explicit portable random stream remain numerical backend differences."),
            new("design.acceptance", "Training loss never certifies throughput or target compliance. Only the completed endpoint receives separate unchanged dense physical gates; no incumbent replacement or target polish.")
        };
        var state = resume;
        if (state is not null)
        {
            AutoLensManagedStateValidation.Validate(state);
            if (state.SchemaVersion != 2 || state.SpecificationFingerprint != AutoLensReferenceStart.TrainingFingerprint(spec)
                || state.AllowedGlassFingerprint != ContentFingerprint.Compute(allowed)
                || state.RootFingerprint != ContentFingerprint.Compute(bootstrap.Steps[0].Optic))
                throw new InvalidDataException("Reference Adam state belongs to another training protocol, root, seed or material set.");
            var physical = OpticalDesignParameters.Read(Optic.FromSnapshot(state.Optic), state.Parameters);
            if (physical.Where((value, index) => Math.Abs(value - state.Adam.Coordinates[index]) > 1e-10 * Math.Max(1, Math.Abs(value))).Any())
                throw new InvalidDataException("Reference Adam coordinates do not describe the saved optic.");
        }
        var count = state?.EvaluationCount ?? 0;
        var rays = state?.TracedRayCount ?? 0;
        var previousTicks = state?.ElapsedComputeTicks ?? 0;
        var steps = new List<AutoLensManagedStep>(state?.TrainingSteps ?? []);
        var imageRadius = RotationalDesignGeometry.ImageRadius(spec.EffectiveFocalLengthMillimeters, spec.MaximumFieldAngleDegrees * Math.PI / 180);
        var compact = imageRadius < 12;
        var referenceRows = AutoLensReferenceStart.ReferenceRows(spec, elements);
        var initial = state?.TrainingStartOptic ?? (restart ?? AutoLensReferenceStart.Create(spec, elements, family, allowed).ToSnapshot());
        if (state is null)
        {
            var optic = Optic.FromSnapshot(initial);
            // Refining an explicitly selected endpoint starts fixed-glass refinement, rather than replaying material search.
            state = Initialize(optic, restart is null ? AutoLensManagedPhase.Curriculum : AutoLensManagedPhase.CatalogRefinement,
                restart is null && settings.OptimizeMaterials, [], restart is null ? (spec.MaximumFieldAngleDegrees > 0
                    ? RotationalDesignGeometry.FocalLengthForField(imageRadius, spec.MaximumFieldAngleDegrees / 57.3) : spec.EffectiveFocalLengthMillimeters)
                    : optic.Paraxial.EstimateEffectiveFocalLength(), restart is null ? spec.MaximumFieldAngleDegrees / 57.3
                    : RotationalDesignGeometry.HalfFieldRadians(imageRadius, optic.Paraxial.EstimateEffectiveFocalLength()));
        }
        FixedInputRaySet? cached = null;
        try
        {
            if (state.Phase == AutoLensManagedPhase.Curriculum) Optimize(curriculum: true);
            if (state.Phase == AutoLensManagedPhase.Curriculum && state.Adam.Iteration == settings.CurriculumIterations + 1)
            {
                var optic = Optic.FromSnapshot(state.Optic);
                AutoLensReferenceMaterials.Register(optic.Materials);
                var matches = new List<DesignGlassMatch>();
                for (var index = 2; index + 1 < optic.SurfaceGroup.Items.Count; index++)
                {
                    if (referenceRows.Contains(index)) continue;
                    var surface = optic.SurfaceGroup.Items[index];
                    if (surface.MaterialAfter is AirMaterial or CatalogGlassMaterial) continue;
                    var match = AutoLensReferenceMaterials.Match(optic.Materials, surface.MaterialAfter, allowed,
                        spec.Wavelengths.Where(wave => wave.Weight > 0).Select(wave => wave.Nanometers).ToArray());
                    surface.MaterialAfter = match.Material;
                    matches.Add(match.Match);
                }
                AutoLensManagedTopology.Apply(optic, spec, elements);
                OpticalDesignParameters.SynchronizeIncidentMedia(optic);
                // e58 set_fnum() uses the cached target focal length before the JSON reload.
                RotationalDesignGeometry.SetRadius(optic, 1, state.ReferenceFocalLength / spec.FNumber / 2);
                AutoLensReferenceHandoff.Apply(optic, AutoLensReferenceStart.ObjectDistance);
                var focal = optic.Paraxial.EstimateEffectiveFocalLength();
                if (!double.IsFinite(focal) || focal <= 0) throw new ArithmeticException("The catalog handoff has no positive finite focal length.");
                var effective = RotationalDesignGeometry.HalfFieldRadians(imageRadius, focal);
                double real;
                count++;
                using (var measurement = SequentialTraceMeasurement.Begin())
                {
                    try
                    {
                        real = RotationalDesignGeometry.SensorEdgeHalfField(optic, imageRadius, focal, Seed(0, 8), token,
                        samplingSettings.FieldScanRings, samplingSettings.FieldScanRays);
                    }
                    finally { rays += measurement.RayCount; }
                }
                state = Initialize(optic, AutoLensManagedPhase.CatalogRefinement, false, matches, focal,
                    effective) with
                { ReferenceHalfFieldRadians = real };
                cached = null;
            }
            if (state.Phase == AutoLensManagedPhase.CatalogRefinement) Optimize(curriculum: false);
            if (state.Phase == AutoLensManagedPhase.CatalogRefinement && state.Adam.Iteration == settings.RefinementIterations + 1)
                state = state with { Phase = AutoLensManagedPhase.Finalization };
            if (state.Phase == AutoLensManagedPhase.Finalization && CanWork())
            {
                var optic = Optic.FromSnapshot(state.Optic);
                count++;
                using var measurement = SequentialTraceMeasurement.Begin();
                try
                {
                    RotationalDesignGeometry.PruneClearApertures(optic, state.ReferenceHalfFieldRadians, state.ReferenceFocalLength, Seed(0, 9), token, referenceRows,
                    samplingSettings.ShapeRings, samplingSettings.ShapeRays);
                }
                finally { rays += measurement.RayCount; }
                state = state with { Optic = optic.ToSnapshot(), Phase = AutoLensManagedPhase.PhysicalValidation };
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            diagnostics.Add(new("design.autolens-paused", "Cancelled at an update boundary; cached ray inputs, exact phase progress and moments are retained. In-flight work is charged and no update is invented."));
        }
        catch (ArithmeticException error)
        {
            state = state with { Phase = AutoLensManagedPhase.DomainFailure };
            diagnostics.Add(new("design.autolens-domain", error.Message));
        }
        DesignEvaluation? validation = null;
        CandidateSnapshot? candidate = null;
        if (state.Phase is AutoLensManagedPhase.PhysicalValidation or AutoLensManagedPhase.Completed && CanWork())
        {
            var optic = Optic.FromSnapshot(state.Optic);
            AutoLensReferenceMaterials.Register(optic.Materials);
            foreach (var surface in optic.SurfaceGroup.Items)
                foreach (var material in new[] { surface.MaterialBefore, surface.MaterialAfter })
                    if (material is not AirMaterial && (material is not CatalogGlassMaterial
                        || !allowed.Any(name => optic.Materials.Resolve(name).Name.Equals(material.Name, StringComparison.OrdinalIgnoreCase))))
                        throw new InvalidDataException("Reference publication requires permitted real catalog media on both sides of every surface.");
            count++;
            var problem = new FlatStartDesignProblem(spec, elements, bootstrap.Steps[0].Optic, family);
            validation = problem.Evaluate(optic, FlatStartDesignProblem.FullStage, true, CancellationToken.None, independentValidation: true);
            state = state with { Phase = AutoLensManagedPhase.Completed };
            rays += problem.TracedRayCount;
            var snapshot = validation.EvaluatedOptic!;
            var fingerprint = ContentFingerprint.Compute(snapshot);
            var attempted = validation.Fields.Sum(field => field.AttemptedRays + field.CheckAttemptedRays);
            var valid = validation.Fields.Sum(field => field.ValidRays + field.CheckValidRays);
            candidate = new()
            {
                CandidateId = "autolens-e58-" + fingerprint[..24],
                OpticFingerprint = fingerprint,
                Optic = snapshot,
                FlatRootOptic = bootstrap.Steps[0].Optic,
                Status = validation.MeetsTargets ? CandidateStatus.LabAccepted : validation.IsFeasible ? CandidateStatus.TraceValid : CandidateStatus.Rejected,
                Lineage = new()
                {
                    RootFingerprint = ContentFingerprint.Compute(bootstrap.Steps[0].Optic),
                    Operation = FlatStartAlgorithm.Name("reference-random-start", spec),
                    ParentCandidateId = parent ?? state.ParentCandidateId,
                    ElementCount = elements,
                    StopVariant = 1,
                    SeedIndex = family?.SeedIndex ?? 0,
                    Generation = steps.Count,
                    InitialForm = family?.BinaryStart?.Signs
                },
                Evaluation = new()
                {
                    FlatStartObjective = validation.Objective,
                    EffectiveFocalLengthMillimeters = validation.EffectiveFocalLengthMillimeters,
                    FNumber = validation.FNumber,
                    RmsSpotRadiusMillimeters = validation.Fields.Max(field => field.RmsRadiusMillimeters),
                    MaximumSpotRadiusMillimeters = validation.Fields.Max(field => field.MaximumRadiusMillimeters),
                    EvaluatedRayCount = attempted,
                    ValidRayCount = valid,
                    ValidRayFraction = attempted == 0 ? 0 : (double)valid / attempted
                },
                Violations = validation.Violations
            };
        }
        state = state with
        {
            EvaluationCount = count,
            TracedRayCount = rays,
            ElapsedComputeTicks = previousTicks + clock.Elapsed.Ticks,
            TrainingSteps = steps.ToArray(),
            FullTargetIncumbent = null,
            PropagationExecution = SequentialGpuSession.Current is { } active ? SequentialGpuExecution.Merge(resume?.PropagationExecution, active.Execution) : resume?.PropagationExecution
        };
        return new()
        {
            Algorithm = new(FlatStartAlgorithm.Name("strict-flat-family-design", spec), FlatStartAlgorithm.Version, "Managed CPU", true),
            Specification = spec,
            SpecificationFingerprint = ContentFingerprint.Compute(spec),
            Bootstrap = bootstrap,
            EvaluationCount = count,
            TracedRayCount = rays,
            AutoLensState = state,
            Candidate = candidate,
            FinalValidation = validation,
            State = validation?.MeetsTargets == true ? FlatStartDesignState.Accepted
                : previousTicks + clock.Elapsed.Ticks >= spec.Budget.TimeLimit.Ticks ? FlatStartDesignState.TimeLimit
                : count >= spec.Budget.MaximumEvaluations - 2 ? FlatStartDesignState.BudgetExhausted : FlatStartDesignState.TargetsNotMet,
            Steps = validation is null ? [] : [new(count, "full-target-dense-validation", FlatStartDesignProblem.FullStage, validation.EvaluatedOptic!, validation)],
            Diagnostics = diagnostics
        };

        void Optimize(bool curriculum)
        {
            var planned = curriculum ? settings.CurriculumIterations : settings.RefinementIterations;
            var interval = Math.Max(planned / 50, 1);
            var parameters = state!.Parameters;
            while (state.Adam.Iteration <= planned && CanWork(reserve: 2))
            {
                var iteration = state.Adam.Iteration;
                var optic = Optic.FromSnapshot(state.Optic);
                var checkpoint = iteration / interval * interval;
                if (state.ReferenceSampling?.Iteration != checkpoint)
                {
                    var fraction = curriculum ? .5 + .25 * (1 - Math.Cos(Math.PI * iteration / planned)) : 1;
                    if (curriculum) RotationalDesignGeometry.SetRadius(optic, 1, state.ReferenceFocalLength / spec.FNumber / 2 * fraction);
                    if (iteration > 0)
                    {
                        count++;
                        using var measurement = SequentialTraceMeasurement.Begin();
                        try
                        {
                            RotationalDesignGeometry.AnchorFrontStop(optic);
                            RotationalDesignGeometry.PruneClearApertures(optic, state.ReferenceHalfFieldRadians, state.ReferenceFocalLength, Seed(iteration, 1), token, referenceRows,
                                samplingSettings.ShapeRings, samplingSettings.ShapeRays);
                        }
                        finally { rays += measurement.RayCount; }
                    }
                    state = state with
                    {
                        Optic = optic.ToSnapshot(),
                        Adam = state.Adam with { Coordinates = OpticalDesignParameters.Read(optic, parameters) },
                        ReferenceSampling = new(optic.ToSnapshot(), checkpoint, Seed(checkpoint, 2), fraction)
                    };
                    cached = null;
                }
                if (!CanWork(reserve: 2)) break;
                var sampling = state.ReferenceSampling!;
                cached ??= FixedInputOpticalMetrics.SampleRingArm(Optic.FromSnapshot(sampling.Optic), state.ReferenceHalfFieldRadians,
                    state.ReferenceFocalLength, curriculum ? samplingSettings.CurriculumRings : samplingSettings.RefinementRings,
                    curriculum ? samplingSettings.CurriculumArms : samplingSettings.RefinementArms,
                    curriculum ? samplingSettings.CurriculumRays : samplingSettings.RefinementRays, 1.05, sampling.Seed);
                count++;
                DifferentialScalar objective;
                using (var measurement = SequentialTraceMeasurement.Begin())
                {
                    try
                    {
                        var bendGate = Math.Cos((compact ? 20 : 30) * Math.PI / 180);
                        var images = Evaluate(cached, bendGate);
                        // Regularization inputs are resampled from the CURRENT optic on every iteration, under no-grad.
                        var craInputs = FixedInputOpticalMetrics.SampleRingArm(optic, state.ReferenceHalfFieldRadians, state.ReferenceFocalLength,
                            samplingSettings.RegularizationRings, 1, samplingSettings.RegularizationRays, .1, Seed(iteration, 3), primaryOnly: true);
                        var bendInputs = FixedInputOpticalMetrics.SampleRingArm(optic, state.ReferenceHalfFieldRadians, state.ReferenceFocalLength,
                            samplingSettings.RegularizationRings, 1, samplingSettings.RegularizationRays, 1, Seed(iteration, 4), primaryOnly: true);
                        var cra = Evaluate(craInputs, bendGate);
                        var bend = Evaluate(bendInputs, bendGate);
                        var craCosine = Math.Cos(Math.Min(state.ReferenceEffectiveHalfFieldRadians, (compact ? 40 : 20) * Math.PI / 180));
                        objective = AutoLensReferenceLoss.Image(images, state.ReferenceCentroid)
                            + .1 * (AutoLensReferenceLoss.Geometry(optic, parameters, compact, curriculum && settings.OptimizeMaterials, referenceRows)
                                + AutoLensReferenceLoss.RayRegularization(cra, bend, craCosine));
                    }
                    finally { rays += measurement.RayCount; }
                }
                if (!double.IsFinite(objective.Value)) throw new ArithmeticException("Reference objective is outside the finite Core domain.");
                var factor = settings.LearningRateFactor * (curriculum ? 1 : .5) * DifferentiableAdam.ReferenceSchedule(iteration, planned, curriculum);
                var adam = DifferentiableAdam.ReferenceStep(state.Adam, objective, state.ReferenceLearningRates!.Select(rate => rate * factor).ToArray());
                var updated = OpticalDesignParameters.Apply(state.Optic, parameters, adam.Coordinates);
                state = state with { Adam = adam, Optic = updated.ToSnapshot(), LastLoss = objective.Value };
                steps.Add(new(state.Phase, adam.Iteration, sampling.PupilFraction, objective.Value, objective.Value, 1, count) { IsPreUpdateObservation = true });

                FixedInputOpticalResult Evaluate(FixedInputRaySet inputs, double bendGate)
                {
                    var result = FixedInputOpticalMetrics.Evaluate(optic, parameters, inputs, bendGate, settings.ComputeMode, token);
                    state = state with
                    {
                        LastComputeExecution = result.Execution,
                        GpuBatches = state.GpuBatches + result.Execution.GpuBatches,
                        GpuRays = state.GpuRays + result.Execution.GpuRays,
                        GpuMilliseconds = state.GpuMilliseconds + result.Execution.GpuMilliseconds
                    };
                    return result;
                }
            }
            diagnostics.Add(new(curriculum ? "design.reference-curriculum" : "design.reference-refinement",
                $"{state.Adam.Iteration}/{planned + 1} unconditional Adam updates; configured iterations={planned}, inclusive upstream loop; {state.Parameters.Count} independent coordinates. Sampling interval={interval}; no schedule compression or loss backtracking."));
        }

        AutoLensManagedState Initialize(Optic optic, AutoLensManagedPhase phase, bool material,
            IReadOnlyList<DesignGlassMatch> matches, double focal, double halfField)
        {
            var parameters = AutoLensReferenceStart.Parameters(optic, spec, material);
            return new()
            {
                SchemaVersion = 2,
                SpecificationFingerprint = AutoLensReferenceStart.TrainingFingerprint(spec),
                AllowedGlassFingerprint = ContentFingerprint.Compute(allowed),
                RootFingerprint = ContentFingerprint.Compute(bootstrap.Steps[0].Optic),
                ParentCandidateId = parent ?? state?.ParentCandidateId,
                Phase = phase,
                PlannedCurriculumIterations = settings.CurriculumIterations,
                PlannedRefinementIterations = settings.RefinementIterations,
                Optic = optic.ToSnapshot(),
                TrainingStartOptic = initial,
                Parameters = parameters,
                Adam = DifferentiableAdam.Initialize(OpticalDesignParameters.Read(optic, parameters)),
                ReferenceLearningRates = parameters.Select(parameter => AutoLensReferenceStart.LearningRate(optic, parameter)).ToArray(),
                ReferenceFocalLength = focal,
                ReferenceHalfFieldRadians = halfField,
                ReferenceEffectiveHalfFieldRadians = halfField,
                ReferenceCentroid = phase == AutoLensManagedPhase.Curriculum || spec.MaximumFieldAngleDegrees / 57.3 > .52,
                GlassMatches = matches,
                LastComputeExecution = state?.LastComputeExecution,
                GpuBatches = state?.GpuBatches ?? 0,
                GpuRays = state?.GpuRays ?? 0,
                GpuMilliseconds = state?.GpuMilliseconds ?? 0
            };
        }
        bool CanWork(int reserve = 0) => !token.IsCancellationRequested && count < spec.Budget.MaximumEvaluations - reserve
            && previousTicks + clock.Elapsed.Ticks < spec.Budget.TimeLimit.Ticks;
        ulong Seed(int iteration, int purpose) => unchecked((ulong)spec.Budget.RandomSeed + (ulong)(family?.SeedIndex ?? 0) * 0x9e3779b97f4a7c15UL
            + (ulong)iteration * 0xbf58476d1ce4e5b9UL + (ulong)purpose * 0x94d049bb133111ebUL
            + (state!.Phase == AutoLensManagedPhase.Curriculum ? 0UL : 0xd1b54a32d192ed03UL));
    }
}

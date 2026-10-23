using System.Diagnostics;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine.Optimization;

namespace OpticalSystemDesign.InitialStructure.Engine;

/// <summary>In-process two-phase native design orchestration; every physical calculation belongs to Core.</summary>
internal static class AutoLensManagedDesign
{
    public static FlatStartDesignResult Run(InitialStructureSpecification specification, int elementCount,
        FlatStartBootstrapResult bootstrap, Stopwatch stopwatch, FlatStartFamily? family, OpticSnapshot? restart,
        string? parentCandidateId, IReadOnlyList<string> allowedGlassNames, CancellationToken cancellationToken,
        AutoLensManagedState? resume = null, bool screenOnly = false)
    {
        var settings = specification.FlatStart!.AutoLens ?? new();
        using var ownedCompute = SequentialGpuSession.Current is null ? new SequentialGpuSession(settings.ComputeMode,cancellationToken) : null;
        var propagation = SequentialGpuSession.Current!;
        var budget = specification.Budget.MaximumEvaluations;
        var count = restart is null ? bootstrap.EvaluationCount : 0;
        long rays = restart is null ? bootstrap.TracedRayCount : 0;
        long previousTicks = 0;
        var diagnostics = new List<SearchDiagnostic>
        {
            new("design.autolens-managed-v1", "Native Core automatic differentiation with implicit stop aiming, bounded Adam, cosine aperture curriculum, permitted-catalog material projection and fixed-catalog refinement. Shared C# Core on CPU or CUDA; no foreign runtime or upstream numerical-equivalence claim."),
            new("design.acceptance", "Training uses unclipped Core coordinates only as diagnostics. A new full-aperture dense independent physical evaluation is mandatory; relaxed glass is never published as a candidate.")
        };
        var steps = new List<DesignStep>();
        var trainingSteps = new List<AutoLensManagedStep>(resume?.TrainingSteps ?? []);
        var gpuBatches = resume?.GpuBatches ?? 0;
        var gpuRays = resume?.GpuRays ?? 0;
        var gpuMilliseconds = resume?.GpuMilliseconds ?? 0;
        var lastComputeExecution = resume?.LastComputeExecution;
        var source = restart ?? bootstrap.Steps[^1].Optic;
        var geometry = new FlatStartDesignProblem(specification, elementCount, source, family);
        var available = Math.Max(0, budget - count - 3);
        available -= Math.Min(settings.TargetPolishEvaluations, available / 4);
        var initialIterations = Math.Min(settings.CurriculumIterations, Math.Max(0, available / 5));
        var refinementIterations = Math.Min(settings.RefinementIterations, Math.Max(0, (available - 2 * initialIterations) / 2));
        if (screenOnly) { initialIterations = Math.Min(2, initialIterations); refinementIterations = Math.Min(2, refinementIterations); }
        AutoLensManagedState? state = null;
        var catalogOptic = Optic.FromSnapshot(source);
        if (resume is not null)
        {
            AutoLensManagedStateValidation.Validate(resume);
            if (resume.SpecificationFingerprint != ContentFingerprint.Compute(specification)
                || resume.AllowedGlassFingerprint != ContentFingerprint.Compute(allowedGlassNames)
                || resume.RootFingerprint != ContentFingerprint.Compute(bootstrap.Steps[0].Optic))
                throw new InvalidDataException("The automatic differentiation state belongs to different settings or permitted materials.");
            count = resume.EvaluationCount;
            rays = resume.TracedRayCount;
            initialIterations = resume.PlannedCurriculumIterations;
            refinementIterations = resume.PlannedRefinementIterations;
            state = resume;
            previousTicks = resume.ElapsedComputeTicks;
            catalogOptic = Optic.FromSnapshot(resume.Optic);
            var coordinates = OpticalDesignParameters.Read(catalogOptic, resume.Parameters);
            if (coordinates.Where((value, index) => Math.Abs(value - resume.Adam.Coordinates[index]) > 1e-10 * Math.Max(1, Math.Abs(value))).Any())
                throw new InvalidDataException("Saved Adam coordinates do not describe the saved optical system.");
        }
        DesignEvaluation? incumbent = null;
        var incumbentOptic = Optic.FromSnapshot(resume?.FullTargetIncumbent ?? (resume is null ? source : bootstrap.Steps[^1].Optic));
        EnsureCatalog(incumbentOptic);
        if (resume is null)
            for (var index = 1; index <= 2 * elementCount; index++)
                if (settings.EvenAsphereOrder > 0 && incumbentOptic.SurfaceGroup.Items[index].Geometry is not EvenAsphereGeometry)
                    incumbentOptic.SurfaceGroup.Items[index].Geometry = new EvenAsphereGeometry(incumbentOptic.SurfaceGroup.Items[index].Radius,
                        incumbentOptic.SurfaceGroup.Items[index].Conic, new double[settings.EvenAsphereOrder]);
        if (CanEvaluate(true))
        {
            incumbent = ValidateFull(incumbentOptic);
            steps.Add(new(count, "autolens-full-target-incumbent", FlatStartDesignProblem.FullStage, incumbent.EvaluatedOptic!, incumbent));
            if (resume is null) catalogOptic = Optic.FromSnapshot(incumbent.EvaluatedOptic!);
        }
        var remainingTrainingTicks = Math.Max(0, specification.Budget.TimeLimit.Ticks - previousTicks - stopwatch.Elapsed.Ticks);
        var trainingDeadline = specification.Budget.TimeLimit.Ticks
            - (settings.TargetPolishEvaluations > 0 ? (long)(remainingTrainingTicks * .35) : 0);
        if (state?.Phase is not (AutoLensManagedPhase.Completed or AutoLensManagedPhase.DomainFailure))
        {
            if (state is null || state.Phase == AutoLensManagedPhase.Curriculum)
            {
                if (state is null)
                {
                    for (var index = 1; index <= 2 * elementCount; index++)
                    {
                        var surface = catalogOptic.SurfaceGroup.Items[index];
                        if (settings.EvenAsphereOrder > 0 && surface.Geometry is not EvenAsphereGeometry)
                            surface.Geometry = new EvenAsphereGeometry(surface.Radius, surface.Conic, new double[settings.EvenAsphereOrder]);
                        if (settings.OptimizeMaterials && index % 2 == 1)
                        {
                            var coordinates = OpticalDesignParameters.RelaxedGlassCoordinates(surface.MaterialAfter);
                            surface.MaterialAfter = OpticalDesignParameters.CreateRelaxedGlass($"DesignRelaxed-{index}", coordinates.Nd, coordinates.Vd);
                        }
                    }
                    AutoLensManagedTopology.Apply(catalogOptic, specification, elementCount);
                    OpticalDesignParameters.SynchronizeIncidentMedia(catalogOptic);
                    catalogOptic.SurfaceGroup.Renumber();
                    state = Initialize(catalogOptic, AutoLensManagedPhase.Curriculum, settings.OptimizeMaterials, []);
                }
                Optimize(initialIterations, curriculum: true);
                catalogOptic = Optic.FromSnapshot(state!.Optic);
                // Projection is mandatory even if a short budget ended the curriculum.
                var matches = ProjectMaterials(catalogOptic);
                diagnostics.Add(new("design.autolens-catalog-projection", $"{matches.Count} relaxed media replaced by permitted real catalog glasses; Adam moments reset for the changed physical model."));
                if (state.Phase != AutoLensManagedPhase.DomainFailure && state.Adam.Iteration >= initialIterations)
                    state = Initialize(catalogOptic, AutoLensManagedPhase.CatalogRefinement, false, matches);
            }
            if (state!.Phase == AutoLensManagedPhase.CatalogRefinement) Optimize(refinementIterations, curriculum: false);
            if (state.Phase == AutoLensManagedPhase.CatalogRefinement)
            {
                catalogOptic = Optic.FromSnapshot(state.Optic);
                if (state.Adam.Iteration >= refinementIterations) state = state with { Phase = AutoLensManagedPhase.Completed };
            }
        }
        // A paused/failed relaxed state is retained for audit/resume, while its
        // separately projected catalog copy alone is eligible for final evaluation.
        catalogOptic = Optic.FromSnapshot(state!.Optic);
        ProjectMaterials(catalogOptic);
        // This check is independent of training state/labels: never export an approximate material.
        EnsureCatalog(catalogOptic);
        var polishEvaluations = resume?.TargetPolishEvaluationCount ?? 0;
        if (CanEvaluate(true) && settings.TargetPolishEvaluations > 0
            && (resume is null || trainingSteps.Count > resume.TrainingSteps.Count))
        {
            var before = count;
            var remainingPolishTicks = Math.Max(0, specification.Budget.TimeLimit.Ticks - previousTicks - stopwatch.Elapsed.Ticks);
            var polishDeadline = specification.Budget.TimeLimit.Ticks - Math.Max(TimeSpan.TicksPerSecond, remainingPolishTicks / 10);
            catalogOptic = Optic.FromSnapshot(AutoLensTargetPolisher.Polish(specification, elementCount, family,
                catalogOptic.ToSnapshot(), Math.Min(settings.TargetPolishEvaluations, budget - count - 1),
                () => !CanEvaluate(true) || previousTicks + stopwatch.Elapsed.Ticks >= polishDeadline,
                () => count++, cost => rays += cost, diagnostics.Add, cancellationToken));
            polishEvaluations += count - before;
        }
        catalogOptic.Aperture.Value = specification.EffectiveFocalLengthMillimeters / specification.FNumber;
        DesignEvaluation? validation = null;
        CandidateSnapshot? candidate = null;
        if (CanEvaluate(false))
        {
            validation = ValidateFull(catalogOptic);
        }
        if (incumbent is not null && (validation is null || Better(incumbent, validation)))
        {
            validation = incumbent;
            diagnostics.Add(new("design.autolens-incumbent-preserved", "The separately verified full-target incumbent is physically better; the training endpoint did not replace it."));
        }
        if (validation is not null)
        {
            EnsureCatalog(Optic.FromSnapshot(validation.EvaluatedOptic!));
            var snapshot = validation.EvaluatedOptic!;
            steps.Add(new(count, "full-target-dense-validation", FlatStartDesignProblem.FullStage, snapshot, validation));
            var fingerprint = ContentFingerprint.Compute(snapshot);
            candidate = new()
            {
                CandidateId = "flat-design-" + fingerprint[..24],
                OpticFingerprint = fingerprint,
                FlatRootOptic = bootstrap.Steps[0].Optic,
                Optic = snapshot,
                Status = validation.MeetsTargets ? CandidateStatus.LabAccepted : validation.IsFeasible ? CandidateStatus.TraceValid : CandidateStatus.Rejected,
                Lineage = new()
                {
                    RootFingerprint = ContentFingerprint.Compute(bootstrap.Steps[0].Optic),
                    Operation = FlatStartAlgorithm.Name(family is null ? $"strict-flat-design-v{FlatStartAlgorithm.Version}" : $"strict-flat-family-design-v{FlatStartAlgorithm.Version}", specification),
                    ParentCandidateId = parentCandidateId,
                    Generation = bootstrap.Steps.Count + (state?.Adam.Iteration ?? 0),
                    ElementCount = elementCount,
                    StopVariant = family?.StopSurfaceIndex ?? 0,
                    SeedIndex = family?.SeedIndex ?? 0,
                    InitialForm = family?.BinaryStart?.Signs
                },
                Evaluation = new()
                {
                    FlatStartObjective = validation.Objective,
                    EffectiveFocalLengthMillimeters = validation.EffectiveFocalLengthMillimeters,
                    FNumber = validation.FNumber,
                    RmsSpotRadiusMillimeters = validation.Fields.Max(field => field.RmsRadiusMillimeters),
                    MaximumSpotRadiusMillimeters = validation.Fields.Max(field => field.MaximumRadiusMillimeters),
                    EvaluatedRayCount = validation.Fields.Sum(field => field.AttemptedRays + field.CheckAttemptedRays),
                    ValidRayCount = validation.Fields.Sum(field => field.ValidRays + field.CheckValidRays),
                    ValidRayFraction = (double)validation.Fields.Sum(field => field.ValidRays + field.CheckValidRays)
                        / validation.Fields.Sum(field => field.AttemptedRays + field.CheckAttemptedRays)
                },
                Violations = validation.Violations
            };
        }
        else diagnostics.Add(new("design.validation-not-run", "Budget or elapsed-time limit prevented independent final validation; no candidate is published."));
        if (state is not null) state = state with
        {
            EvaluationCount = count,
            TracedRayCount = rays,
            ElapsedComputeTicks = previousTicks + stopwatch.Elapsed.Ticks,
            FullTargetIncumbent = validation?.EvaluatedOptic ?? state.FullTargetIncumbent
        };
        if (state is not null) state = state with
        {
            TrainingSteps = trainingSteps.ToArray(), TargetPolishEvaluationCount = polishEvaluations,
            LastComputeExecution = lastComputeExecution, GpuBatches = gpuBatches, GpuRays = gpuRays, GpuMilliseconds = gpuMilliseconds,
            PropagationExecution = settings.ComputeMode == DesignComputeMode.Cpu ? resume?.PropagationExecution
                : MergePropagation(resume?.PropagationExecution,propagation.Execution)
        };
        if (lastComputeExecution is { } execution)
            diagnostics.Add(new("design.autolens-compute", $"{execution.Backend}; {execution.DeviceName}; {gpuBatches} CUDA batches, {gpuRays} differentiated rays; CPU formal tracing and independent physical validation retained."
                + (execution.FallbackReason is { } reason ? " CPU fallback: " + reason : "")));
        return new()
        {
            Algorithm = new(FlatStartAlgorithm.Name(family is null ? "strict-flat-design" : "strict-flat-family-design", specification), FlatStartAlgorithm.Version, "Managed CPU", true),
            Specification = specification,
            SpecificationFingerprint = ContentFingerprint.Compute(specification),
            Bootstrap = bootstrap,
            State = validation?.MeetsTargets == true ? FlatStartDesignState.Accepted
                : previousTicks + stopwatch.Elapsed.Ticks >= specification.Budget.TimeLimit.Ticks ? FlatStartDesignState.TimeLimit
                : count >= budget - 1 ? FlatStartDesignState.BudgetExhausted : FlatStartDesignState.TargetsNotMet,
            EvaluationCount = count,
            TracedRayCount = rays,
            FinalValidation = validation,
            Candidate = candidate,
            Steps = steps,
            Diagnostics = diagnostics,
            AutoLensState = state
        };

        bool CanEvaluate(bool reserveFinal)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return count < budget - (reserveFinal ? 1 : 0) && previousTicks + stopwatch.Elapsed.Ticks < specification.Budget.TimeLimit.Ticks;
        }

        DesignEvaluation ValidateFull(Optic optic)
        {
            count++;
            OpticalDesignParameters.SynchronizeIncidentMedia(optic);
            optic.Aperture.Value = specification.EffectiveFocalLengthMillimeters / specification.FNumber;
            var before = geometry.TracedRayCount;
            var evaluation = geometry.Evaluate(optic, FlatStartDesignProblem.FullStage, true, cancellationToken, independentValidation: true);
            rays += geometry.TracedRayCount - before;
            return evaluation;
        }

        void EnsureCatalog(Optic optic)
        {
            foreach (var surface in optic.SurfaceGroup.Items)
                foreach (var material in new[] { surface.MaterialBefore, surface.MaterialAfter })
                    if (material is not AirMaterial && (material is not CatalogGlassMaterial || !allowedGlassNames.Any(name =>
                            optic.Materials.Resolve(name).Name.Equals(material.Name, StringComparison.OrdinalIgnoreCase))))
                        throw new InvalidDataException("A published design/incumbent must use only air and explicitly permitted real catalog glasses, including incident-medium metadata.");
        }

        static bool Better(DesignEvaluation first, DesignEvaluation second)
        {
            static int Rank(DesignEvaluation evaluation) => evaluation.MeetsTargets ? 0 : evaluation.IsFeasible ? 1 : 2;
            return Rank(first) < Rank(second) || Rank(first) == Rank(second)
                && FlatStartCandidateArchive.Score(first.Objective) <= FlatStartCandidateArchive.Score(second.Objective);
        }

        AutoLensManagedState Initialize(Optic optic, AutoLensManagedPhase phase, bool material, IReadOnlyList<DesignGlassMatch> matches)
        {
            if (phase == AutoLensManagedPhase.CatalogRefinement && specification.FlatStart.AutomaticLensDiameters && CanEvaluate(true))
            {
                count++;
                using var measurement = SequentialTraceMeasurement.Begin();
                try
                {
                    optic.Aperture.Value = specification.EffectiveFocalLengthMillimeters / specification.FNumber;
                    PhysicalStopCalibration.Apply(optic);
                    var envelope = AutomaticSemiDiameterSolver.UpdateFromRayEnvelope(optic, FlatStartProblem.LensGroups(elementCount),
                        [(0, 0), (0, -.7), (0, .7), (0, -1), (0, 1)],
                        FlatStartDesignProblem.IntegrationPupils(FlatStartSamplingPolicy.UniformAreaGaussianV1, false)
                            .Concat(FlatStartDesignProblem.CheckPupils(false)).ToArray(), specification.SemiDiameterMarginFactor,
                        cancellationToken: cancellationToken, preserveStopAperture: true);
                    diagnostics.Add(new("design.autolens-body-projection", envelope.Applied
                        ? "Real-ray body sizing applied outside differentiation; body radii remain fixed during this Adam phase and are independently resized/validated at final acceptance."
                        : "Real-ray body sizing was unavailable; no estimated body size substituted. Final physical sizing/throughput gates remain mandatory."));
                }
                finally { rays += measurement.RayCount; }
            }
            var parameters = Parameters(optic, specification, elementCount, material);
            return new()
            {
                SpecificationFingerprint = ContentFingerprint.Compute(specification),
                AllowedGlassFingerprint = ContentFingerprint.Compute(allowedGlassNames),
                RootFingerprint = ContentFingerprint.Compute(bootstrap.Steps[0].Optic),
                ParentCandidateId = parentCandidateId,
                Phase = phase,
                PlannedCurriculumIterations = initialIterations,
                PlannedRefinementIterations = refinementIterations,
                Optic = optic.ToSnapshot(),
                Parameters = parameters,
                Adam = DifferentiableAdam.Initialize(OpticalDesignParameters.Read(optic, parameters)),
                GlassMatches = matches,
                EvaluationCount = count,
                TracedRayCount = rays
            };
        }

        IReadOnlyList<DesignGlassMatch> ProjectMaterials(Optic optic)
        {
            var matches = new List<DesignGlassMatch>();
            for (var index = 1; index <= 2 * elementCount; index += 2)
            {
                if (optic.SurfaceGroup.Items[index].MaterialAfter is CatalogGlassMaterial) continue;
                var match = DesignGlassMatcher.Match(optic.Materials, optic.SurfaceGroup.Items[index].MaterialAfter,
                    allowedGlassNames, specification.Wavelengths.Where(wave => wave.Weight > 0).Select(wave => wave.Nanometers).ToArray());
                optic.SurfaceGroup.Items[index].MaterialAfter = match.Material;
                matches.Add(match.Match);
            }
            optic.SurfaceGroup.Renumber();
            AutoLensManagedTopology.Apply(optic, specification, elementCount);
            OpticalDesignParameters.SynchronizeIncidentMedia(optic);
            return matches;
        }

        void Optimize(int plannedIterations, bool curriculum)
        {
            var parameters = state!.Parameters;
            var template = state.Optic;
            var fields = specification.MaximumFieldAngleDegrees == 0 ? new[] { 0.0 }
                : new[] { 0.0 }.Concat(Enumerable.Range(1, settings.FieldRings - 1)
                    .SelectMany(index => new[] { Math.Sqrt((double)index / (settings.FieldRings - 1)), -Math.Sqrt((double)index / (settings.FieldRings - 1)) })).ToArray();
            IReadOnlyList<PupilSample> pupils = [];
            var accepted = 0;
            for (var iteration = state.Adam.Iteration; iteration < plannedIterations && CanTrain(); iteration++)
            {
                pupils = ApertureSampler.GenerateSeededUniformDisk(settings.PupilRings * settings.PupilArms,
                    unchecked((ulong)specification.Budget.RandomSeed ^ (curriculum ? 0x31f6a8b9UL : 0xf5e272c1UL) ^ ((ulong)(iteration / settings.ResampleInterval) * 0x9e3779b97f4a7c15UL)));
                var fraction = curriculum ? AutoLensCpuCurriculum.ApertureFraction(plannedIterations <= 1 ? 1 : (double)iteration / (plannedIterations - 1)) : 1;
                (Optic Optic, DifferentialScalar Loss)? current = Evaluate(state.Adam.Coordinates, fraction);
                if (current is null) { state = state with { Phase = AutoLensManagedPhase.DomainFailure }; break; }
                state = state with { Optic = current.Value.Optic.ToSnapshot(), LastLoss = current.Value.Loss.Value, EvaluationCount = count, TracedRayCount = rays };
                var rates = parameters.Select(parameter => LearningRate(parameter) * settings.LearningRateFactor * (curriculum ? 1 : .5)
                    * DifferentiableAdam.CosineWarmRestart(iteration, plannedIterations)).ToArray();
                var proposal = DifferentiableAdam.Propose(state.Adam, current.Value.Loss, rates);
                var projectedProposal = Project(proposal.Coordinates.ToArray(), parameters);
                var dot = projectedProposal.Select((value, index) => (value - state.Adam.Coordinates[index]) * current.Value.Loss.Derivative(index)).Sum();
                if (dot >= 0)
                {
                    // Restart only momentum age, never the phase/curriculum counter.
                    var reset = DifferentiableAdam.Initialize(state.Adam.Coordinates) with { Iteration = state.Adam.Iteration };
                    proposal = DifferentiableAdam.Propose(reset, current.Value.Loss, rates);
                    diagnostics.Add(new("design.autolens-momentum-restart", $"Non-descent momentum restarted at {state.Phase} iteration {state.Adam.Iteration}; phase progress and charged work are retained."));
                }
                var acceptedProposal = false;
                for (var backoff = 0; backoff < 12 && CanTrain(); backoff++)
                {
                    var factor = Math.Pow(.5, backoff);
                    var point = Project(proposal.Coordinates.Select((value, index) => state.Adam.Coordinates[index] + factor * (value - state.Adam.Coordinates[index])).ToArray(), parameters);
                    var next = Evaluate(point, fraction);
                    if (next is null) continue;
                    if (next.Value.Loss.Value >= current.Value.Loss.Value) continue;
                    state = state with
                    {
                        Adam = proposal with { Coordinates = point },
                        Optic = next.Value.Optic.ToSnapshot(),
                        LastLoss = next.Value.Loss.Value,
                        EvaluationCount = count,
                        TracedRayCount = rays
                    };
                    acceptedProposal = true;
                    accepted++;
                    trainingSteps.Add(new(state.Phase, state.Adam.Iteration, fraction, current.Value.Loss.Value,
                        next.Value.Loss.Value, factor, count));
                    break;
                }
                if (!acceptedProposal) break;
            }
            diagnostics.Add(new(curriculum ? "design.autolens-curriculum" : "design.autolens-catalog-refinement",
                $"{accepted} finite automatic-gradient Adam steps; {parameters.Count} parameters; {state.Adam.Iteration}/{plannedIterations} iterations. Every objective/domain probe is charged; no parameter finite differences."));

            (Optic Optic, DifferentialScalar Loss)? Evaluate(IReadOnlyList<double> point, double pupilFraction)
            {
                if (!CanTrain()) return null;
                count++;
                using var measurement = SequentialTraceMeasurement.Begin();
                try
                {
                    var optic = OpticalDesignParameters.Apply(template, parameters, point);
                    optic.Aperture.Value = specification.EffectiveFocalLengthMillimeters / specification.FNumber * pupilFraction;
                    PhysicalStopCalibration.Apply(optic);
                    var result = DifferentiableOpticalMetrics.Evaluate(optic, parameters, fields, pupils, cancellationToken, settings.ComputeMode);
                    if (settings.ComputeMode != DesignComputeMode.Cpu)
                    {
                        lastComputeExecution = result.Execution;
                        gpuBatches += result.Execution.GpuBatches;
                        gpuRays += result.Execution.GpuRays;
                        gpuMilliseconds += result.Execution.GpuMilliseconds;
                    }
                    var loss = AutoLensManagedLoss.Evaluate(optic, parameters, result, specification, elementCount);
                    if (!loss.IsFinite || loss.Dimension != parameters.Count) throw new ArithmeticException("Incomplete automatic-gradient objective.");
                    return (optic, loss);
                }
                catch (ArithmeticException error)
                {
                    if (!diagnostics.Any(diagnostic => diagnostic.Code == "design.autolens-domain"))
                        diagnostics.Add(new("design.autolens-domain", error.Message));
                    return null;
                }
                finally { rays += measurement.RayCount; }
            }

            bool CanTrain() => CanEvaluate(true) && previousTicks + stopwatch.Elapsed.Ticks < trainingDeadline;
        }

        double[] Project(double[] point, IReadOnlyList<OpticalDesignParameter> parameters)
        {
            var surfaceCount = 2 * elementCount;
            var native = geometry.Vector(state!.Optic);
            for (var index = 0; index < parameters.Count; index++)
            {
                var parameter = parameters[index];
                var slot = parameter.Kind == OpticalDesignParameterKind.Curvature ? parameter.SurfaceIndex - 1
                    : parameter.Kind == OpticalDesignParameterKind.Thickness ? surfaceCount + parameter.SurfaceIndex - 1 : -1;
                if (slot >= 0) native[slot] = point[index];
                if (parameter.Kind == OpticalDesignParameterKind.Curvature && parameter.LinkedSurfaceIndex is { } linked)
                    native[linked - 1] = point[index];
            }
            native = geometry.Project(native);
            for (var index = 0; index < point.Length; index++)
                point[index] = parameters[index].Kind switch
                {
                    OpticalDesignParameterKind.Curvature => native[parameters[index].SurfaceIndex - 1],
                    OpticalDesignParameterKind.Thickness => native[surfaceCount + parameters[index].SurfaceIndex - 1],
                    OpticalDesignParameterKind.Conic => Math.Clamp(point[index], -5, 1),
                    OpticalDesignParameterKind.EvenAsphereCoefficient => Math.Clamp(point[index], -.1, .1),
                    OpticalDesignParameterKind.MaterialIndex => Math.Clamp(point[index], 1.3 / parameters[index].Scale, 2.2 / parameters[index].Scale),
                    OpticalDesignParameterKind.MaterialAbbe => Math.Clamp(point[index], 10 / parameters[index].Scale, 100 / parameters[index].Scale),
                    _ => point[index]
                };
            return point;
        }
    }

    private static SequentialGpuExecution MergePropagation(SequentialGpuExecution? previous,SequentialGpuExecution current) =>
        previous is null ? current : current with
        {
            AimRays=checked(previous.AimRays+current.AimRays),AimTraces=checked(previous.AimTraces+current.AimTraces),
            PropagatedRays=checked(previous.PropagatedRays+current.PropagatedRays),Batches=checked(previous.Batches+current.Batches),
            Milliseconds=previous.Milliseconds+current.Milliseconds,CpuRecoveryRays=checked(previous.CpuRecoveryRays+current.CpuRecoveryRays),
            FallbackReason=current.FallbackReason ?? previous.FallbackReason
        };

    private static IReadOnlyList<OpticalDesignParameter> Parameters(Optic optic, InitialStructureSpecification specification, int elements, bool materials)
    {
        var focal = specification.EffectiveFocalLengthMillimeters;
        var surfaces = 2 * elements;
        var parameters = Enumerable.Range(1, surfaces).Where(index => !LinkedFront(index))
            .Select(index => new OpticalDesignParameter(index, OpticalDesignParameterKind.Curvature, 1 / focal, LinkedSurfaceIndex: LinkedBack(index) ? index + 1 : null)).ToList();
        parameters.AddRange(Enumerable.Range(1, surfaces - (specification.FlatStart!.FixedBackFocusMillimeters.HasValue ? 1 : 0))
            .Where(index => !LinkedBack(index))
            .Select(index => new OpticalDesignParameter(index, OpticalDesignParameterKind.Thickness, focal)));
        for (var index = 1; index <= surfaces; index++)
        {
            if (!LinkedFront(index) && optic.SurfaceGroup.Items[index].Geometry is EvenAsphereGeometry asphere)
            {
                parameters.Add(new(index, OpticalDesignParameterKind.Conic, LinkedSurfaceIndex: LinkedBack(index) ? index + 1 : null));
                var radius = Math.Max(1, optic.SurfaceGroup.Items[index].MechanicalSemiDiameter);
                for (var coefficient = 1; coefficient < asphere.Coefficients.Count; coefficient++)
                    parameters.Add(new(index, OpticalDesignParameterKind.EvenAsphereCoefficient, 1 / Math.Pow(radius, 2 * coefficient + 1), coefficient, LinkedBack(index) ? index + 1 : null));
            }
            if (materials && index % 2 == 1) parameters.AddRange([
                new(index, OpticalDesignParameterKind.MaterialIndex, LinkedSurfaceIndex: LinkedFront(index) ? index - 1 : null),
                new(index, OpticalDesignParameterKind.MaterialAbbe, 50, LinkedSurfaceIndex: LinkedFront(index) ? index - 1 : null)]);
        }
        return parameters;

        bool LinkedBack(int index) => index % 2 == 0 && index < surfaces && AutoLensManagedTopology.IsCemented(specification, index / 2 - 1);
        bool LinkedFront(int index) => index > 1 && index % 2 == 1 && AutoLensManagedTopology.IsCemented(specification, (index - 3) / 2);
    }

    private static double LearningRate(OpticalDesignParameter parameter) => parameter.Kind switch
    {
        OpticalDesignParameterKind.Curvature => 1e-4 / parameter.Scale,
        OpticalDesignParameterKind.Thickness => 1e-2 / parameter.Scale,
        OpticalDesignParameterKind.Conic => .01,
        OpticalDesignParameterKind.EvenAsphereCoefficient => .00001,
        OpticalDesignParameterKind.MaterialIndex => 1e-4,
        OpticalDesignParameterKind.MaterialAbbe => .01 / parameter.Scale,
        _ => throw new ArgumentOutOfRangeException(nameof(parameter))
    };
}

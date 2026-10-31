using System.Collections.Immutable;
using System.Diagnostics;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine.Optimization;

namespace OpticalSystemDesign.InitialStructure.Engine;

/// <summary>Bounded multi-start first-order preparation using actual Core thick-lens matrices and geometry.
/// This is not Delano replication or physical image-quality certification.</summary>
public static class ParaxialSeedSearch
{
    private sealed class LimitReached : Exception { }
    private sealed class StartLimitReached : Exception { }
    private sealed record Sample(CompiledTopology Source, double[] Coordinates, double[] Residuals,
        double[,] Jacobian, double FocusA, double Power, double InverseFNumber,
        (double Offset, double[] Normal)[] Geometry);

    public static ParaxialSeedSearchResult Run(AutonomousSearchPlan plan, ParaxialSeedSearchOptions? options = null,
        CancellationToken cancellationToken = default, Action<int, int>? progress = null)
    {
        options ??= plan.Options.SeedPreparation ?? new();
        ValidateOptions(options);
        if (options.Retrieval is not null)
            return LensRetrievalSearch.Run(plan, options, cancellationToken, progress);
        if (options.Evolution is not null)
            return StructureEvolutionSearch.Run(plan, options, cancellationToken, progress);
        if (options.Competition is not null)
            return StructureCompetitionSearch.Run(plan, options, cancellationToken, progress);
        _ = AutonomousSearchService.Create("seed-preparation-check", plan);
        if (AutonomousSearchService.IsHistoricalReadOnlyPlan(plan))
            throw new NotSupportedException("Historical read-only optimizer plans cannot prepare new seeds. Create a new versioned plan.");
        if (plan.Seeds.Any(seed => seed.HasTransitionElements))
            throw new ArgumentException("First-order preparation requires complete physical seed templates, not deletion transition states.");
        var protocol = ContentFingerprint.Compute(new
        {
            Identity = options.ImageScreening is not null ? "initial-structure-real-ray-ranked-seeds-v1"
                : options.Generation?.MaterializationVersion == 3 ? "initial-structure-feasible-thick-seeds-v3"
                : options.Generation?.MaterializationVersion == 2 ? "initial-structure-thick-delano-seeds-v2"
                : options.Generation is null ? "initial-structure-paraxial-seeds-v1" : "initial-structure-descriptor-seeds-v1",
            plan,
            options,
            Objective = options.FixedStopRecoveryVersion == 2 ? "Core/A,power,independent-pupil-v2,geometry/joint-projection"
                : "Core/A,whole-system-power,fixed-stop-inverse-F,geometry",
            SearchRadialSamples = options.FixedStopRecoveryVersion == 2 ? 64 : 16,
            FinalRadialSamples = 64
        });
        var timer = Stopwatch.StartNew();
        using var deadline = new CancellationTokenSource(options.TimeLimit);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var token = linked.Token;
        using var rays = SequentialTraceMeasurement.BeginCpuBudget(options.MaximumTracedRays);
        var random = new DeterministicRandom(plan.Options.Seed);
        var functions = 0; var matrices = 0; var jacobians = 0; var diagrams = 0; long probes = 0;
        var attempts = new List<ParaxialSeedAttempt>();
        var retained = new List<(DesignTopology Topology, double Score)>();
        var termination = ParaxialSeedTermination.Completed;
        var screeningPhase = false;
        var screeningReserve = options.ImageScreening is { } imageOptions
            ? Math.Min(Math.Min(imageOptions.MaximumCandidates, options.MaximumStarts), options.MaximumFunctionEvaluations - 2) : 0;
        try
        {
            using var cpu = new SequentialGpuSession(DesignComputeMode.Cpu, token);
            for (var start = 0; start < options.MaximumStarts; start++)
            {
                CheckLimit();
                if (options.MaximumFunctionEvaluations - functions < 2) throw new LimitReached();
                var template = plan.Seeds[start % plan.Seeds.Length];
                var before = functions;
                var outcome = "NoFirstOrderSeed";
                Sample? last = null, accepted = null;
                StructureGenerationWork? constructionWork = null;
                try
                {
                    if (options.Generation is { } generation)
                    {
                        var generationWork = new StructureGenerationWork();
                        constructionWork = generationWork;
                        try
                        {
                            template = StructureSeedGenerator.Create(plan, generation, ref random, () =>
                            {
                                if (functions - before >= options.MaximumEvaluationsPerStart - 2) throw new StartLimitReached();
                                Charge(); diagrams++;
                            }, token, work: generationWork);
                        }
                        finally { matrices += generationWork.MatrixRequests; probes += generationWork.GeometryProbes; }
                    }
                    var source = TopologyCompiler.Compile(template, plan.Constraints);
                    var binding = TopologyParameterBinder.Bind(source, Variables(template, plan.Options, options.FixedStopRecoveryVersion == 2));
                    var initial = binding.InitialCoordinates.ToArray();
                    if (options.Generation is null && start >= plan.Seeds.Length)
                        for (var index = 0; index < initial.Length; index++)
                        {
                            var slot = binding.Slots[index];
                            var width = .25 * (slot.UpperCoordinate - slot.LowerCoordinate);
                            initial[index] = Math.Clamp(initial[index] + width * (2 * random.NextUnitDouble() - 1), slot.LowerCoordinate, slot.UpperCoordinate);
                        }
                    var geometry = TopologyEvaluationPlan.Geometry(source, plan.Targets, options.FixedStopRecoveryVersion == 2 ? 64 : 16);
                    var solution = BoundedTrustRegionLeastSquares.Solve(initial,
                        binding.Slots.Select(slot => slot.LowerCoordinate).ToArray(), binding.Slots.Select(slot => slot.UpperCoordinate).ToArray(),
                        Evaluate, new()
                        {
                            MaximumEvaluations = Math.Min(options.MaximumEvaluationsPerStart - (functions - before) - 1, options.MaximumFunctionEvaluations - functions - 1),
                            StepMethod = LeastSquaresStepMethod.Regularized,
                            ScaleByJacobian = true
                        }, token,
                        acceptedStep: (_, _) => accepted = last,
                        jacobianBuilt: () => jacobians++,
                        analyticJacobian: coordinates => last is not null && coordinates.SequenceEqual(last.Coordinates)
                            ? last.Jacobian : throw new InvalidOperationException("Seed derivatives must use the same Core evaluation."),
                        trialProjector: options.FixedStopRecoveryVersion == 2 ? Project : null);
                    if (solution.Evaluation?.IsValid == true && accepted is { } endpoint)
                    {
                        var score = endpoint.Residuals.Take(3).Sum(value => value * value);
                        var optic = endpoint.Source.CreateOptic();
                        Charge();
                        var valid = true;
                        foreach (var row in TopologyEvaluationPlan.Geometry(endpoint.Source, plan.Targets, 64))
                        {
                            token.ThrowIfCancellationRequested(); probes++;
                            var value = TopologyEvaluationPlan.FormalMeasure(optic, row);
                            valid &= double.IsFinite(value) && (row.Upper ? value <= row.Limit : value >= row.Limit);
                        }
                        matrices++; var focal = optic.Paraxial.EstimateEffectiveFocalLength();
                        matrices++; var fNumber = optic.Paraxial.EstimateFNumber();
                        outcome = valid ? "FirstOrderTargetRejected" : "DenseGeometryRejected";
                        valid &= double.IsFinite(focal) && focal > 0 && double.IsFinite(fNumber) && fNumber > 0
                            && Math.Abs(focal / plan.Targets.EffectiveFocalLengthMillimeters - 1) <= plan.Targets.FocalLengthRelativeTolerance
                            && Math.Abs(fNumber / plan.Targets.FNumber - 1) <= plan.Targets.FNumberRelativeTolerance
                            && Math.Abs(endpoint.FocusA) <= options.FocusATolerance;
                        if (valid)
                        {
                            outcome = "FirstOrderFeasibleOnly";
                            retained.Add((endpoint.Source.Topology, score));
                        }
                    }

                    LeastSquaresEvaluation Evaluate(IReadOnlyList<double> coordinates, CancellationToken evaluationToken)
                    {
                        Charge(); evaluationToken.ThrowIfCancellationRequested();
                        try
                        {
                            var value = TopologyParameterBinder.Apply(source, binding, coordinates);
                            var optic = value.CreateOptic();
                            matrices++;
                            var differential = DifferentiableParaxialMetrics.TransferMatrix(optic, binding.CoreParameters,
                                value.SurfaceIndices[value.Topology.Reference.Id], value.SurfaceIndices[value.Topology.Image.Id], includeInverseFNumber: true);
                            var residuals = new List<DifferentialScalar>
                            {
                                differential.A / options.FocusATolerance,
                                (differential.OpticalPowerPerMillimeter!.Value * plan.Targets.EffectiveFocalLengthMillimeters - 1) / plan.Targets.FocalLengthRelativeTolerance,
                                (differential.InverseFNumber!.Value * plan.Targets.FNumber - 1) / plan.Targets.FNumberRelativeTolerance
                            };
                            if (options.FixedStopRecoveryVersion == 2)
                            {
                                // Target pupil diameter is specification algebra. The actual pupil and
                                // every derivative come from the same unchanged shared-Core evaluation.
                                var pupilTarget = plan.Targets.EffectiveFocalLengthMillimeters / plan.Targets.FNumber;
                                residuals = [differential.A / options.FocusATolerance,
                                    (differential.OpticalPowerPerMillimeter!.Value * plan.Targets.EffectiveFocalLengthMillimeters - 1)
                                        / plan.Targets.FocalLengthRelativeTolerance,
                                    (differential.EntrancePupilDiameterMillimeters!.Value / pupilTarget - 1)
                                        / plan.Targets.FNumberRelativeTolerance];
                            }
                            var planes = new List<(double, double[])>();
                            foreach (var row in geometry)
                            {
                                evaluationToken.ThrowIfCancellationRequested(); probes++;
                                var measured = TopologyEvaluationPlan.Measure(optic, binding.CoreParameters, row);
                                var error = row.Upper ? measured - row.Limit : row.Limit - measured;
                                if (options.FixedStopRecoveryVersion == 2) error += 1e-6;
                                if (options.FixedStopRecoveryVersion == 2)
                                    planes.Add((error.Value, Enumerable.Range(0, coordinates.Count).Select(error.Derivative).ToArray()));
                                residuals.Add(error.Value > 0 ? error / .01 : 0);
                            }
                            var scalars = residuals.Select(row => row.Value).ToArray();
                            var jacobian = new double[scalars.Length, binding.Slots.Length];
                            for (var row = 0; row < scalars.Length; row++)
                                for (var column = 0; column < binding.Slots.Length; column++) jacobian[row, column] = residuals[row].Derivative(column);
                            if (scalars.Any(value => !double.IsFinite(value)) || jacobian.Cast<double>().Any(value => !double.IsFinite(value)))
                                return new([], false);
                            last = new(value, coordinates.ToArray(), scalars, jacobian, differential.A.Value,
                                differential.OpticalPowerPerMillimeter.Value.Value, differential.InverseFNumber.Value.Value, planes.ToArray());
                            accepted ??= last; // First valid sample is the solver's initial accepted point.
                            return new(scalars);
                        }
                        catch (Exception error) when (TopologyEvaluationPlan.DomainError(error) && !SequentialTraceBudgetExceededException.IsBudgetException(error))
                        { return new([], false); }
                    }

                    double[]? Project(IReadOnlyList<double> point, IReadOnlyList<double> proposed, double radius)
                    {
                        var sample = accepted ?? throw new InvalidOperationException("Missing accepted recovery point.");
                        if (!point.SequenceEqual(sample.Coordinates)) throw new InvalidOperationException("Stale recovery projection.");
                        var step = proposed.Select((value, index) => value - point[index]).ToArray();
                        var structural = new List<(double Offset, double[] Normal)>();
                        var topology = sample.Source.Topology;
                        foreach (var element in topology.Elements)
                            AddAxial(element.FrontSurfaceId, element.BackSurfaceId, element.FrontZMillimeters,
                                element.BackZMillimeters, plan.Constraints.MinimumCenterThicknessMillimeters);
                        foreach (var air in TopologyValidation.AirIntervals(topology, plan.Constraints))
                            AddAxial(air.LeftSurfaceId, air.RightSurfaceId, air.LeftZMillimeters, air.RightZMillimeters,
                                air.RightSurfaceId == topology.Image.Id ? plan.Constraints.MinimumBackFocusMillimeters
                                    : air.LeftSurfaceId == topology.Reference.Id && air.RightSurfaceId != topology.Stop.Id ? 0
                                    : air.LengthMillimeters == 0 && air.LeftSurfaceId == topology.Reference.Id ? 0 : plan.Constraints.MinimumAirGapMillimeters);
                        var planes = sample.Geometry.Concat(structural).ToList();
                        for (var column = 0; column < point.Count; column++)
                        {
                            var positive = new double[point.Count]; positive[column] = 1;
                            var negative = new double[point.Count]; negative[column] = -1;
                            planes.Add((-Math.Min(radius, binding.Slots[column].UpperCoordinate - point[column]), positive));
                            planes.Add((-Math.Min(radius, point[column] - binding.Slots[column].LowerCoordinate), negative));
                            structural.Add(planes[^2]); structural.Add(planes[^1]);
                        }
                        foreach (var fraction in new[] { 1, .5, .25, .1 })
                        {
                            var joint = planes.ToList();
                            for (var row = 0; row < 3; row++)
                            {
                                var normal = Enumerable.Range(0, point.Count).Select(column => sample.Jacobian[row, column]).ToArray();
                                joint.Add((fraction * sample.Residuals[row] - .1, normal));
                                joint.Add((-fraction * sample.Residuals[row] - .1, normal.Select(value => -value).ToArray()));
                            }
                            var projected = LinearFeasibilityProjection.Solve(joint, point.Count, 512, token, step);
                            if (projected is not null) return projected.Select((value, index) => point[index] + value).ToArray();
                        }
                        var legal = LinearFeasibilityProjection.Solve(structural, point.Count, 512, token, step);
                        return legal?.Select((value, index) => point[index] + value).ToArray();

                        void AddAxial(string left, string right, double leftZ, double rightZ, double minimum)
                        {
                            var normal = binding.Slots.Select(slot => slot.Request.Kind != OpticalDesignParameterKind.AxialPosition ? 0
                                : slot.Request.SurfaceId == left ? slot.Request.Scale : slot.Request.SurfaceId == right ? -slot.Request.Scale : 0).ToArray();
                            structural.Add((leftZ - rightZ + minimum + (normal.Any(value => value != 0) ? 1e-9 : 0), normal));
                        }
                    }
                }
                catch (StartLimitReached) { outcome = "PreparationBudgetExhausted"; }
                catch (Exception error) when (TopologyEvaluationPlan.DomainError(error) && !SequentialTraceBudgetExceededException.IsBudgetException(error))
                { outcome = "Rejected:" + error.GetType().Name; }
                finally
                {
                    attempts.Add(new(start, ContentFingerprint.Compute(template), outcome, functions - before,
                        accepted is null ? null : accepted.Residuals.Take(3).Sum(value => value * value), accepted?.Source.Topology)
                    {
                        Construction = options.Generation?.MaterializationVersion == 3 && constructionWork is { } work
                            ? new(work.AttemptedLayouts, work.FeasibleLayouts, work.RejectedGeometryProposals,
                                work.SelectedStopAfterElement, work.MinimumClearanceMarginMillimeters) : null
                    });
                    progress?.Invoke(start + 1, functions);
                }
            }
        }
        catch (OperationCanceledException) { termination = cancellationToken.IsCancellationRequested ? ParaxialSeedTermination.Cancelled : ParaxialSeedTermination.TimeLimit; }
        catch (LimitReached) { termination = ParaxialSeedTermination.BudgetExhausted; }
        catch (Exception error) when (SequentialTraceBudgetExceededException.IsBudgetException(error)) { termination = ParaxialSeedTermination.BudgetExhausted; }
        var pool = retained.OrderBy(item => item.Score).ThenBy(item => ContentFingerprint.Compute(item.Topology), StringComparer.Ordinal)
            .DistinctBy(item => TopologyDiversitySelection.PrescriptionIdentity(item.Topology)).ToList();
        var seeds = new List<DesignTopology>();
        List<SeedImageScreeningResult>? screens = null;
        if (options.ImageScreening is { } screening)
        {
            screeningPhase = true;
            screens = [];
            try
            {
                foreach (var candidate in pool.Take(screening.MaximumCandidates))
                {
                    Charge();
                    var assessed = SeedImageQualityScreening.Evaluate(TopologyCompiler.Compile(candidate.Topology, plan.Constraints),
                        plan.Targets, screening, token);
                    screens.Add(assessed);
                    progress?.Invoke(attempts.Count, functions);
                    if (token.IsCancellationRequested) token.ThrowIfCancellationRequested();
                    if (rays.LimitReached) throw new LimitReached();
                }
            }
            catch (OperationCanceledException) { termination = cancellationToken.IsCancellationRequested ? ParaxialSeedTermination.Cancelled : ParaxialSeedTermination.TimeLimit; }
            catch (LimitReached) { termination = ParaxialSeedTermination.BudgetExhausted; }
            seeds = SeedImageQualityScreening.Select(pool, screens, options.RetainedSeeds, screening, plan.Targets);
        }
        else
        {
            while (pool.Count > 0 && seeds.Count < options.RetainedSeeds)
            {
                var best = seeds.Count == 0 ? pool[0] : pool.OrderByDescending(item => seeds.Min(chosen =>
                    TopologyDiversitySelection.Distance(item.Topology, chosen, plan.Targets))).ThenBy(item => item.Score).First();
                seeds.Add(best.Topology); pool.Remove(best);
            }
        }
        return new()
        {
            Algorithm = options.ImageScreening is not null ? "initial-structure-real-ray-ranked-seeds-v1"
                : options.Generation?.MaterializationVersion == 3 ? "initial-structure-feasible-thick-seeds-v3"
                : options.Generation?.MaterializationVersion == 2 ? "initial-structure-thick-delano-seeds-v2"
                : options.FixedStopRecoveryVersion == 2 ? "initial-structure-fixed-stop-seeds-v2"
                : options.Generation is null ? "initial-structure-paraxial-seeds-v1" : "initial-structure-descriptor-seeds-v1",
            DiagramEvaluations = options.Generation?.Method == StructureGenerationMethod.Delano ? diagrams : null,
            ProtocolFingerprint = protocol,
            SourcePlan = plan,
            Options = options,
            Termination = termination,
            Seeds = seeds.ToImmutableArray(),
            Attempts = attempts.ToImmutableArray(),
            ImageScreens = screens?.ToImmutableArray(),
            MatrixRequests = matrices,
            Work = new()
            {
                FunctionEvaluations = functions,
                MatrixEvaluations = matrices,
                JacobianBuilds = jacobians,
                GeometryProbes = probes,
                TracedRays = rays.RayCount,
                Elapsed = timer.Elapsed
            }
        };

        void CheckLimit()
        {
            token.ThrowIfCancellationRequested();
            if (functions >= options.MaximumFunctionEvaluations - (screeningPhase ? 0 : screeningReserve)) throw new LimitReached();
        }
        void Charge() { CheckLimit(); functions++; }
    }

    internal static ImmutableArray<TopologyVariableRequest> Variables(DesignTopology topology, AutonomousSearchOptions policy, bool includePermittedConics = false)
    {
        var result = ImmutableArray.CreateBuilder<TopologyVariableRequest>();
        foreach (var element in topology.Elements.Where(element => !element.Protected))
        {
            foreach (var (id, face, z) in new[] { (element.FrontSurfaceId, element.Front, element.FrontZMillimeters),
                (element.BackSurfaceId, element.Back, element.BackZMillimeters) })
            {
                if (face.Profile.Kind != TopologyProfileKind.Plane)
                    result.Add(new(id, OpticalDesignParameterKind.Curvature, -policy.CurvatureBoundPerMillimeter, policy.CurvatureBoundPerMillimeter, .01));
                if (policy.AxialCompensation is { } axial && (axial.ElementIds.IsEmpty || axial.ElementIds.Contains(element.Id)))
                    result.Add(new(id, OpticalDesignParameterKind.AxialPosition, z - axial.MaximumDisplacementMillimeters,
                        z + axial.MaximumDisplacementMillimeters, axial.CoordinateScaleMillimeters));
                if (includePermittedConics && policy.CompensateConic == true && face.Profile.Kind != TopologyProfileKind.Plane)
                {
                    var bound = Math.Max(policy.Expansion?.MaximumAbsoluteConic ?? 2, Math.Abs(face.Profile.Conic) * 2);
                    result.Add(new(id, OpticalDesignParameterKind.Conic, -bound, bound));
                }
            }
        }
        if (result.Count is < 1 or > 64) throw new ArgumentException("Seed preparation requires 1–64 permitted curvature/axial variables.");
        return result.ToImmutable();
    }

    public static void ValidateOptions(ParaxialSeedSearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Retrieval is not null) LensRetrievalSearch.ValidateOptions(options);
        if (options.Evolution is not null) StructureEvolutionSearch.ValidateOptions(options);
        if (options.Competition is not null) StructureCompetitionSearch.Validate(options);
        if (options.Generation is { } generation) StructureSeedGenerator.Validate(generation);
        if (options.ImageScreening is { } screening)
        {
            SeedImageQualityScreening.Validate(screening);
            if (options.MaximumFunctionEvaluations < 3) throw new ArgumentException("Real-ray screening requires a separate evaluation within the preparation budget.");
        }
        if (options.FixedStopRecoveryVersion is not (null or 2) || options.Version != 1 || options.MaximumStarts is < 1 or > 64 || options.RetainedSeeds is < 1 or > 8
            || options.MaximumFunctionEvaluations is < 2 or > 10_000 || options.MaximumEvaluationsPerStart is < 2 or > 1000
            || options.TimeLimit <= TimeSpan.Zero || options.TimeLimit > TimeSpan.FromMinutes(options.Evolution is null && options.Retrieval is null ? 2 : 10)
            || options.MaximumTracedRays < 1 || options.MaximumTracedRays > (options.Evolution is null && options.Retrieval is null ? 1_000_000 : 10_000_000)
            || !double.IsFinite(options.FocusATolerance) || options.FocusATolerance is <= 0 or > .1)
            throw new ArgumentException("Invalid bounded first-order seed preparation protocol.");
    }

}

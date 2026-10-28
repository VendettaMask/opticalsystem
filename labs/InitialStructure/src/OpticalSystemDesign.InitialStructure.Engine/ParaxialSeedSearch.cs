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
    private sealed record Sample(CompiledTopology Source, double[] Coordinates, double[] Residuals,
        double[,] Jacobian, double FocusA, double Power, double InverseFNumber);

    public static ParaxialSeedSearchResult Run(AutonomousSearchPlan plan, ParaxialSeedSearchOptions? options = null,
        CancellationToken cancellationToken = default, Action<int, int>? progress = null)
    {
        options ??= plan.Options.SeedPreparation ?? new();
        ValidateOptions(options);
        _ = AutonomousSearchService.Create("seed-preparation-check", plan);
        if (AutonomousSearchService.IsHistoricalReadOnlyPlan(plan))
            throw new NotSupportedException("Historical read-only optimizer plans cannot prepare new seeds. Create a new versioned plan.");
        if (plan.Seeds.Any(seed => seed.HasTransitionElements))
            throw new ArgumentException("First-order preparation requires complete physical seed templates, not deletion transition states.");
        var protocol = ContentFingerprint.Compute(new { Identity = "initial-structure-paraxial-seeds-v1", plan, options,
            Objective = "Core/A,whole-system-power,fixed-stop-inverse-F,geometry", SearchRadialSamples = 16, FinalRadialSamples = 64 });
        var timer = Stopwatch.StartNew();
        using var deadline = new CancellationTokenSource(options.TimeLimit);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var token = linked.Token;
        using var rays = SequentialTraceMeasurement.BeginCpuBudget(options.MaximumTracedRays);
        var random = new DeterministicRandom(plan.Options.Seed);
        var functions = 0; var matrices = 0; var jacobians = 0; long probes = 0;
        var attempts = new List<ParaxialSeedAttempt>();
        var retained = new List<(DesignTopology Topology, double Score)>();
        var termination = ParaxialSeedTermination.Completed;
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
                try
                {
                    var source = TopologyCompiler.Compile(template, plan.Constraints);
                    var binding = TopologyParameterBinder.Bind(source, Variables(template, plan.Options));
                    var initial = binding.InitialCoordinates.ToArray();
                    if (start >= plan.Seeds.Length)
                        for (var index = 0; index < initial.Length; index++)
                        {
                            var slot = binding.Slots[index];
                            var width = .25 * (slot.UpperCoordinate - slot.LowerCoordinate);
                            initial[index] = Math.Clamp(initial[index] + width * (2 * random.NextUnitDouble() - 1), slot.LowerCoordinate, slot.UpperCoordinate);
                        }
                    var geometry = TopologyEvaluationPlan.Geometry(source, plan.Targets, 16);
                    var solution = BoundedTrustRegionLeastSquares.Solve(initial,
                        binding.Slots.Select(slot => slot.LowerCoordinate).ToArray(), binding.Slots.Select(slot => slot.UpperCoordinate).ToArray(),
                        Evaluate, new() { MaximumEvaluations = Math.Min(options.MaximumEvaluationsPerStart - 1, options.MaximumFunctionEvaluations - functions - 1),
                            StepMethod = LeastSquaresStepMethod.Regularized, ScaleByJacobian = true }, token,
                        acceptedStep: (_, _) => accepted = last,
                        jacobianBuilt: () => jacobians++,
                        analyticJacobian: coordinates => last is not null && coordinates.SequenceEqual(last.Coordinates)
                            ? last.Jacobian : throw new InvalidOperationException("Seed derivatives must use the same Core evaluation."));
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
                            foreach (var row in geometry)
                            {
                                evaluationToken.ThrowIfCancellationRequested(); probes++;
                                var measured = TopologyEvaluationPlan.Measure(optic, binding.CoreParameters, row);
                                var error = row.Upper ? measured - row.Limit : row.Limit - measured;
                                residuals.Add(error.Value > 0 ? error / .01 : 0);
                            }
                            var scalars = residuals.Select(row => row.Value).ToArray();
                            var jacobian = new double[scalars.Length, binding.Slots.Length];
                            for (var row = 0; row < scalars.Length; row++)
                                for (var column = 0; column < binding.Slots.Length; column++) jacobian[row, column] = residuals[row].Derivative(column);
                            if (scalars.Any(value => !double.IsFinite(value)) || jacobian.Cast<double>().Any(value => !double.IsFinite(value)))
                                return new([], false);
                            last = new(value, coordinates.ToArray(), scalars, jacobian, differential.A.Value,
                                differential.OpticalPowerPerMillimeter.Value.Value, differential.InverseFNumber.Value.Value);
                            accepted ??= last; // First valid sample is the solver's initial accepted point.
                            return new(scalars);
                        }
                        catch (Exception error) when (TopologyEvaluationPlan.DomainError(error) && !SequentialTraceBudgetExceededException.IsBudgetException(error))
                        { return new([], false); }
                    }
                }
                catch (Exception error) when (TopologyEvaluationPlan.DomainError(error) && !SequentialTraceBudgetExceededException.IsBudgetException(error))
                { outcome = "Rejected:" + error.GetType().Name; }
                finally
                {
                    attempts.Add(new(start, ContentFingerprint.Compute(template), outcome, functions - before,
                        accepted is null ? null : accepted.Residuals.Take(3).Sum(value => value * value), accepted?.Source.Topology));
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
        while (pool.Count > 0 && seeds.Count < options.RetainedSeeds)
        {
            var best = seeds.Count == 0 ? pool[0] : pool.OrderByDescending(item => seeds.Min(chosen =>
                TopologyDiversitySelection.Distance(item.Topology, chosen, plan.Targets))).ThenBy(item => item.Score).First();
            seeds.Add(best.Topology); pool.Remove(best);
        }
        return new() { ProtocolFingerprint = protocol, SourcePlan = plan, Options = options, Termination = termination,
            Seeds = seeds.ToImmutableArray(), Attempts = attempts.ToImmutableArray(), MatrixRequests = matrices,
            Work = new() { FunctionEvaluations = functions, MatrixEvaluations = matrices, JacobianBuilds = jacobians,
                GeometryProbes = probes, TracedRays = rays.RayCount, Elapsed = timer.Elapsed } };

        void CheckLimit() { token.ThrowIfCancellationRequested(); if (functions >= options.MaximumFunctionEvaluations) throw new LimitReached(); }
        void Charge() { CheckLimit(); functions++; }
    }

    internal static ImmutableArray<TopologyVariableRequest> Variables(DesignTopology topology, AutonomousSearchOptions policy)
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
            }
        }
        if (result.Count is < 1 or > 64) throw new ArgumentException("Seed preparation requires 1–64 permitted curvature/axial variables.");
        return result.ToImmutable();
    }

    public static void ValidateOptions(ParaxialSeedSearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Version != 1 || options.MaximumStarts is < 1 or > 64 || options.RetainedSeeds is < 1 or > 8
            || options.MaximumFunctionEvaluations is < 2 or > 10_000 || options.MaximumEvaluationsPerStart is < 2 or > 200
            || options.TimeLimit <= TimeSpan.Zero || options.TimeLimit > TimeSpan.FromMinutes(2)
            || options.MaximumTracedRays is < 1 or > 1_000_000 || !double.IsFinite(options.FocusATolerance) || options.FocusATolerance is <= 0 or > .1)
            throw new ArgumentException("Invalid bounded first-order seed preparation protocol.");
    }
}

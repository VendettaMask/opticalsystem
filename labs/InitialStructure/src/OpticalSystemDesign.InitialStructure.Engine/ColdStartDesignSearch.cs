using System.Collections.Immutable;
using System.Diagnostics;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.InitialStructure.Contracts;

namespace OpticalSystemDesign.InitialStructure.Engine;

/// <summary>Fresh fixed-stop layouts, original-spec ranking and bounded sphere/A4/A6 compensation.
/// All optical construction, differentiation and qualification use shared Core.</summary>
public static class ColdStartDesignSearch
{
    private sealed class FunctionLimit : Exception;

    public static ColdStartDesignResult Run(AutonomousSearchPlan input, ParaxialSeedSearchOptions preparation,
        ColdStartDesignOptions policy, CancellationToken cancellationToken = default, Action<string>? progress = null)
    {
        Validate(input, preparation, policy);
        var plan = ColdPlan(input);
        var descriptor = preparation.Generation!;
        var identity = ContentFingerprint.Compute(new { Identity = "cold-back-focus-design-v1", plan, preparation, policy,
            Final = "original-TopologyValidator-v2", Coordinates = "anchored-on-generated-layout; append-original-permitted-terms" });
        var clock = Stopwatch.StartNew();
        using var deadline = new CancellationTokenSource(preparation.TimeLimit);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, cancellationToken);
        var token = linked.Token;
        using var cpu = new SequentialGpuSession(DesignComputeMode.Cpu, token);
        using var serial = ComputationParallelism.SuppressNestedParallelism();
        using var rays = SequentialTraceMeasurement.BeginCpuBudget(preparation.MaximumTracedRays);
        var constructions = new List<ColdStartConstruction>(); var phases = new List<ColdStartDesignPhase>();
        var functions = 0; var gradients = 0; var validations = 0; var matrices = 0; long probes = 0;
        var reason = "Completed"; DesignTopology? endpoint = null; TopologyAssessment? assessment = null;
        var rank = (Loss: double.PositiveInfinity, Quality: double.PositiveInfinity);
        var random = new DeterministicRandom(plan.Options.Seed);
        var margin = Math.Max(.05, plan.Constraints.MinimumAirGapMillimeters);
        var minimum = Math.Max(descriptor.MinimumBackFocusMillimeters, plan.Constraints.MinimumBackFocusMillimeters);
        var maximum = plan.Seeds[0].Image.ZMillimeters - plan.Seeds[0].Stop.ZMillimeters - margin
            - descriptor.Elements.Length * (descriptor.MinimumThicknessMillimeters + margin);
        try
        {
            token.ThrowIfCancellationRequested();
            if (StructureSearchDiagnostics.ExternalStopAperturePrecheck(plan.Seeds[0]).ProvenInfeasible)
            { reason = "OriginalExternalStopApertureConflict"; return Finish(); }
            foreach (var fraction in policy.BackFocusSlackFractions)
                for (var attempt = 0; attempt < policy.AttemptsPerFraction; attempt++)
                {
                    Check();
                    var beforeFunctions = functions; var beforeRays = rays.RayCount; var started = Stopwatch.StartNew();
                    var construction = new StructureGenerationWork(); DesignTopology? proposal = null; TopologyAssessment? measured = null;
                    var outcome = "NoFeasibleConstruction";
                    var samplingMinimum = policy.ExploreBackFocus ? minimum + fraction * Math.Max(0, maximum - minimum) : minimum;
                    try
                    {
                        proposal = ThickStructureSeedGenerator.Create(plan, descriptor with
                        {
                            MinimumBackFocusMillimeters = samplingMinimum,
                            AnnealingProposals = Math.Min(16, descriptor.AnnealingProposals)
                        }, ref random, () => { if (functions >= 200) throw new FunctionLimit(); Charge(); }, construction, token,
                            requireFeasibleGeometry: true,
                            initialShape: StructureCompetitionSearch.InitialShape(StructureRayPathFamily.Balanced, descriptor.Elements.Length));
                        CheckPermissions(proposal, plan, descriptor);
                        measured = Assess(proposal); outcome = measured.Completed ? "OriginalAssessed" : "OriginalAssessmentIncomplete";
                        var loss = measured.Completed && !measured.Fields.IsEmpty ? measured.Fields.Max(f => Math.Max(
                            1 - (double)f.ValidRays / f.AttemptedRays, (double)(f.CheckAttemptedRays - f.CheckValidRays) / f.CheckAttemptedRays)) : double.PositiveInfinity;
                        var quality = !measured.Fields.IsEmpty && measured.Fields.All(f => f.RmsRadiusMillimeters is not null && f.MaximumRadiusMillimeters is not null)
                            ? measured.Fields.Max(f => Math.Max(f.RmsRadiusMillimeters!.Value / plan.Targets.MaximumRmsRadiusMillimeters,
                                f.MaximumRadiusMillimeters!.Value / plan.Targets.MaximumSpotRadiusMillimeters)) : double.PositiveInfinity;
                        if (loss < rank.Loss || loss == rank.Loss && quality < rank.Quality)
                        { endpoint = proposal; assessment = measured; rank = (loss, quality); }
                    }
                    catch (Exception error) when (TopologyEvaluationPlan.DomainError(error) && !SequentialTraceBudgetExceededException.IsBudgetException(error))
                    { outcome = "Rejected:" + error.Message; }
                    finally
                    {
                        matrices += construction.MatrixRequests; probes += construction.GeometryProbes;
                        constructions.Add(new(constructions.Count + 1, samplingMinimum, outcome,
                            new() { FunctionEvaluations = functions - beforeFunctions, MatrixEvaluations = construction.MatrixRequests,
                                GeometryProbes = construction.GeometryProbes + (measured?.GeometryProbes ?? 0),
                                ValidationEvaluations = measured is null ? 0 : 1, TracedRays = rays.RayCount - beforeRays, Elapsed = started.Elapsed }, proposal, measured));
                    }
                    progress?.Invoke($"construction {constructions.Count}: {outcome}");
                }
            if (endpoint is null) { reason = "NoEligibleConstruction"; return Finish(); }
            if (assessment?.MeetsTargets == true) return Finish();
            var variables = StructureEvolutionSearch.EvolutionVariables(endpoint, plan.Options);
            Optimize("SphereSparse", 102, 90, 2, 8, 180);
            if (assessment?.MeetsTargets == true) return Finish();
            Optimize("SphereDense", 82, 180, 10, 32, 150);
            if (assessment?.MeetsTargets == true) return Finish();
            foreach (var power in new[] { 4, 6 })
            {
                Check();
                if (plan.Options.Expansion is not { EnableProfile: true } expansion || expansion.MaximumEvenPower < power) break;
                var changed = endpoint;
                foreach (var face in endpoint.Elements.Where(e => !e.Protected).SelectMany(e => new[] { (e.FrontSurfaceId, e.Front), (e.BackSurfaceId, e.Back) })
                    .Where(f => f.Item2.Profile.MaximumEvenPower < power).Select(f => f.Item1).ToArray())
                {
                    var edit = TopologyOperators.UpgradeProfile(changed, face, power, plan.Constraints);
                    if (!edit.Succeeded) throw new ArgumentException(edit.Failure!.Message);
                    changed = edit.Topology!;
                }
                endpoint = changed;
                assessment = null;
                variables = variables.AddRange(StructureEvolutionSearch.EvolutionVariables(endpoint, plan.Options)
                    .Where(v => v.Kind == OpticalDesignParameterKind.EvenAsphereCoefficient
                        && !variables.Any(old => old.SurfaceId == v.SurfaceId && old.Kind == v.Kind && old.Power == v.Power)));
                if (variables.Length > 64) throw new ArgumentException("Original permitted release exceeds the shared Core 64-variable workspace.");
                Optimize("Even" + power, power == 4 ? 200 : 120, power == 4 ? 300 : 270, 10, 32, power == 4 ? 75 : 0);
                if (assessment?.MeetsTargets == true) break;
            }
            if (assessment?.MeetsTargets != true) reason = "NoQualifiedEndpointWithinBudget";

            void Optimize(string name, int maximumFunctions, double seconds, int rings, int arms, double laterReserve)
            {
                Check();
                var remaining = preparation.TimeLimit.TotalSeconds - clock.Elapsed.TotalSeconds - 5 - laterReserve;
                var quota = Math.Min(maximumFunctions, preparation.MaximumFunctionEvaluations - functions - 1);
                var gradientQuota = Math.Min(quota - 1, policy.MaximumGradientEvaluations - gradients);
                if (remaining <= 0 || quota < 2 || gradientQuota < 1) { reason = "InsufficientCompletePhaseBudget"; return; }
                var beforeRays = rays.RayCount; var timer = Stopwatch.StartNew();
                var source = TopologyCompiler.Compile(endpoint!, plan.Constraints);
                var binding = TopologyParameterBinder.Bind(source, variables, exactMechanicalFrame: true);
                var result = FixedTopologyOptimizer.Run(source, binding, plan.Targets, new()
                {
                    GateCompensationVersion = 2, MaximumUpdates = quota - 2, MaximumFunctionEvaluations = quota,
                    MaximumGradientEvaluations = gradientQuota, TimeLimit = TimeSpan.FromSeconds(Math.Min(seconds, remaining)),
                    ComputeMode = DesignComputeMode.Cpu, PerformFinalAssessment = false,
                    Sampling = new() { FieldsPerDirection = 3, PupilRings = rings, PupilArms = arms, ProfileRadialSamples = 64,
                        IncludeBoundaryClearance = true, IncludeBoundaryMaximum = true }
                }, token);
                functions += result.Work.FunctionEvaluations; gradients += result.Work.GradientEvaluations;
                probes += result.Work.GeometryProbes; endpoint = result.Topology;
                assessment = null;
                if (!token.IsCancellationRequested && functions < preparation.MaximumFunctionEvaluations) assessment = Assess(endpoint);
                phases.Add(new(name, result.Termination.ToString(), new()
                {
                    FunctionEvaluations = result.Work.FunctionEvaluations + (assessment is null ? 0 : 1),
                    GradientEvaluations = result.Work.GradientEvaluations, ValidationEvaluations = assessment is null ? 0 : 1,
                    GeometryProbes = result.Work.GeometryProbes + (assessment?.GeometryProbes ?? 0),
                    TracedRays = rays.RayCount - beforeRays, Elapsed = timer.Elapsed
                }, variables, endpoint, assessment, result.Diagnostics));
                progress?.Invoke(name + ": " + result.Termination + ", qualified=" + (assessment?.MeetsTargets == true));
            }
        }
        catch (FunctionLimit) { reason = "FunctionBudget"; }
        catch (OperationCanceledException) { reason = cancellationToken.IsCancellationRequested ? "Cancelled" : "TimeLimit"; }
        catch (Exception error) when (SequentialTraceBudgetExceededException.IsBudgetException(error)) { reason = "RayBudget"; }
        catch (Exception error) when (TopologyEvaluationPlan.DomainError(error)) { reason = "Domain:" + error.Message; }
        return Finish();

        void Check() { token.ThrowIfCancellationRequested(); if (functions >= preparation.MaximumFunctionEvaluations) throw new FunctionLimit(); }
        void Charge() { Check(); functions++; }
        TopologyAssessment Assess(DesignTopology topology)
        {
            Charge(); validations++;
            var value = TopologyValidator.Validate(TopologyCompiler.Compile(topology, plan.Constraints), plan.Targets, token);
            probes += value.GeometryProbes; return value;
        }
        ColdStartDesignResult Finish() => new()
        {
            ProtocolFingerprint = identity, ColdPlan = plan, Preparation = preparation, Policy = policy, StopReason = reason,
            Work = new() { FunctionEvaluations = functions, GradientEvaluations = gradients, ValidationEvaluations = validations,
                MatrixEvaluations = matrices, GeometryProbes = probes, TracedRays = rays.RayCount, Elapsed = clock.Elapsed },
            Construction = constructions.ToImmutableArray(), Phases = phases.ToImmutableArray(), Endpoint = endpoint, Assessment = assessment
        };
    }

    public static AutonomousSearchPlan ColdPlan(AutonomousSearchPlan plan)
    {
        var origin = plan.Seeds[0];
        TopologyFace Plane(TopologyFace face) => face with { Profile = new() { Kind = TopologyProfileKind.Standard } };
        return plan with { Seeds = [origin with { Name = "Cold specification envelope", Elements = origin.Elements.Select(e => e with
            { Front = Plane(e.Front), Back = Plane(e.Back) }).ToImmutableArray() }] };
    }

    public static void Validate(AutonomousSearchPlan plan, ParaxialSeedSearchOptions preparation, ColdStartDesignOptions policy)
    {
        ArgumentNullException.ThrowIfNull(plan); ArgumentNullException.ThrowIfNull(preparation); ArgumentNullException.ThrowIfNull(policy);
        if (plan.Seeds.IsDefaultOrEmpty || plan.Options.FixedTopologyOnly == true || plan.Options.QualifiedReductionOnly == true
            || plan.Seeds[0].Elements.Any(e => e.Protected || e.Transition) || plan.Seeds[0].Conjugate != ObjectConjugateMode.Infinite
            || preparation.Generation is not { Method: StructureGenerationMethod.Delano, StopAfterElement: 0 }
            || policy.Version != 1 || policy.AttemptsPerFraction is < 1 or > 8 || policy.BackFocusSlackFractions.IsDefaultOrEmpty
            || policy.BackFocusSlackFractions.Length > 8 || policy.BackFocusSlackFractions.Any(v => !double.IsFinite(v) || v is < 0 or >= 1)
            || policy.MaximumGradientEvaluations is < 1 or > 10000 || preparation.MaximumFunctionEvaluations is < 10 or > 10000
            || preparation.MaximumTracedRays < 1 || preparation.TimeLimit <= TimeSpan.Zero || preparation.TimeLimit > TimeSpan.FromHours(24))
            throw new ArgumentException("Cold design requires original free external-stop generation and bounded explicit budgets.");
        StructureSeedGenerator.Validate(preparation.Generation);
        if (preparation.Generation.Elements.Length != plan.Seeds[0].Elements.Length)
            throw new ArgumentException("Cold fixed-aperture construction preserves the original element count.");
        for (var i = 0; i < preparation.Generation.Elements.Length; i++)
        {
            var declared = preparation.Generation.Elements[i]; var actual = plan.Seeds[0].Elements[i];
            if (actual.Front.ClearSemiDiameterMillimeters != (declared.FrontAperture?.ClearSemiDiameterMillimeters ?? preparation.Generation.ClearSemiDiameterMillimeters)
                || actual.Back.ClearSemiDiameterMillimeters != (declared.BackAperture?.ClearSemiDiameterMillimeters ?? preparation.Generation.ClearSemiDiameterMillimeters))
                throw new ArgumentException("Cold construction cannot change original physical aperture caps.");
        }
        if (plan.Seeds[0].Stop.ZMillimeters < plan.Seeds[0].Reference.ZMillimeters)
            throw new ArgumentException("The fixed reference cannot follow the original external stop.");
    }

    private static void CheckPermissions(DesignTopology proposal, AutonomousSearchPlan plan, StructureGenerationOptions descriptor)
    {
        var origin = plan.Seeds[0];
        if (proposal.Reference != origin.Reference || proposal.Stop != origin.Stop || proposal.Image != origin.Image
            || !proposal.Fields.SequenceEqual(origin.Fields) || !proposal.Wavelengths.SequenceEqual(origin.Wavelengths))
            throw new ArgumentException("Original fixed frame, fields or spectrum changed.");
        for (var i = 0; i < proposal.Elements.Length; i++)
        {
            var e = proposal.Elements[i]; var declared = descriptor.Elements[i];
            if (!declared.Materials.Contains(e.Material) || Math.Abs(e.Front.Profile.CurvaturePerMillimeter) > plan.Options.CurvatureBoundPerMillimeter
                || Math.Abs(e.Back.Profile.CurvaturePerMillimeter) > plan.Options.CurvatureBoundPerMillimeter)
                throw new ArgumentException("Original material or curvature permission changed.");
            if (e.Front.ClearSemiDiameterMillimeters != (declared.FrontAperture?.ClearSemiDiameterMillimeters ?? descriptor.ClearSemiDiameterMillimeters)
                || e.Back.ClearSemiDiameterMillimeters != (declared.BackAperture?.ClearSemiDiameterMillimeters ?? descriptor.ClearSemiDiameterMillimeters))
                throw new ArgumentException("Original clear aperture changed.");
        }
    }
}

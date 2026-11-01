using System.Collections.Immutable;
using System.Diagnostics;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Optimization;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.InitialStructure.Contracts;

namespace OpticalSystemDesign.InitialStructure.Engine;

/// <summary>One-level, explicitly bounded SPC research search. Core owns every optical value and derivative.
/// Null states never enter ordinary acceptance. Only restored positive-thickness states use the original full validator.</summary>
public static partial class SpcBranchSearch
{
    private sealed class Limit(string reason) : Exception(reason);
    public static string GetProtocolFingerprint(AutonomousSearchPlan plan, ParaxialSeedSearchOptions budget, SpcBranchOptions policy, bool directGrowthControl = false)
    {
        Validate(plan, budget, policy);
        return ContentFingerprint.Compute(new { Algorithm = directGrowthControl ? "initial-structure-direct-growth-control-v1" : "initial-structure-spc-branches-v1",
            plan, budget, policy, Coordinates = "declared-unprotected-curvatures-only", OpticalMerit = "Core-power/inverseF/spot-energy/distortion/clearance-v1",
            ResearchTrace = "cpu-null-element-continuation/1", Curvature = "central-exact-AD-gradient/h-and-half-h; symmetric-eigenvalue-index", Final = "original-TopologyValidator" });
    }
    public static SpcBranchSearchResult Run(AutonomousSearchPlan plan, ParaxialSeedSearchOptions budget, SpcBranchOptions policy,
        CancellationToken cancellationToken = default, Action<string, int>? progress = null, bool directGrowthControl = false)
    {
        var identity = GetProtocolFingerprint(plan, budget, policy, directGrowthControl);
        var parents = new List<SpcParentEvidence>(); var branches = new List<SpcOptimizedBranch>();
        var functions = 0; var gradients = 0; var validations = 0; long probes = 0;
        var reason = "Completed"; var timer = Stopwatch.StartNew();
        using var deadline = new CancellationTokenSource(budget.TimeLimit);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token); var token = linked.Token;
        using var serial = ComputationParallelism.SuppressNestedParallelism();
        using var rays = SequentialTraceMeasurement.BeginCpuBudget(budget.MaximumTracedRays);
        try
        {
            token.ThrowIfCancellationRequested();
            if (plan.Seeds.Any(s => StructureSearchDiagnostics.ExternalStopAperturePrecheck(s).ProvenInfeasible)) return Finish("OriginalExternalStopApertureConflict");
            if (plan.Options.FixedTopologyOnly == true || plan.Options.QualifiedReductionOnly == true || plan.Options.Expansion?.EnableInsertion == false)
                return Finish("OriginalInsertionPermissionConflict");
            using var cpu = new SequentialGpuSession(DesignComputeMode.Cpu, token);
            var random = new DeterministicRandom(plan.Options.Seed);
            var ordered = plan.Seeds.Select((s, i) => (Topology: s, Id: "parent-" + i, Order: random.NextUnitDouble())).OrderBy(x => x.Order).Take(policy.MaximumParents);
            foreach (var input in ordered)
            {
                Check(); var original = input.Topology; var id = input.Id;
                if (original.Elements.Length >= plan.Constraints.MaximumElementCount) { ParentStop("OriginalElementLimit"); continue; }
                if (original.CementedInterfaces is not null || original.HasTransitionElements) { ParentStop("UnsupportedParentStructure"); continue; }
                var sites = original.Elements.Where(e => !e.Protected && e.Back.Profile.Kind == TopologyProfileKind.Standard && e.Back.Profile.Conic == 0
                    && e.BackZMillimeters > original.Stop.ZMillimeters).ToArray();
                if (sites.Length == 0) { ParentStop("NoPermittedSphericalPostStopInterface"); continue; }
                var site = sites[random.NextInt32(sites.Length)];
                var source = TopologyCompiler.Compile(original, plan.Constraints); var requests = Variables(original, plan.Options.CurvatureBoundPerMillimeter);
                if (requests.Length is < 1 or > 30) { ParentStop("StationarityDimensionUnsupported"); continue; }
                var materials = plan.Options.InsertionMaterials.Concat(plan.Options.Expansion is { EnableMaterial: true } expansion ? expansion.MaterialAlternatives : [])
                    .Where(m => plan.Constraints.AllowedGlassNames.IsEmpty || plan.Constraints.AllowedGlassNames.Contains(m)).Distinct(StringComparer.Ordinal)
                    .Take(policy.MaximumMaterialAlternatives).ToArray();
                if (materials.Length == 0) { ParentStop("NoPermittedInsertionGlass"); continue; }
                var initialGlass = materials.Contains(site.Material) ? site.Material : materials[0];
                var originalAssessment = Assess(original);
                if (!originalAssessment.Completed || originalAssessment.Violations.Any(v => v.Code.StartsWith("geometry:", StringComparison.Ordinal)))
                { parents.Add(new(id,"ParentManufacturingRejected",original,null,requests,null,null,site.Id){OriginalAssessment=originalAssessment});continue; }
                var binding = TopologyParameterBinder.Bind(source, requests, exactMechanicalFrame: true);
                var parameters = binding.CoreParameters.ToArray(); var lower = binding.Slots.Select(s => s.LowerCoordinate).ToArray(); var upper = binding.Slots.Select(s => s.UpperCoordinate).ToArray();
                var fields = TopologyEvaluationPlan.Fields(source, plan.Options.Local.Sampling.FieldsPerDirection).Select(f => (f.X, f.Y)).ToArray();
                var pupils = ApertureSampler.GenerateGaussianQuadrature(plan.Options.Local.Sampling.PupilRings, plan.Options.Local.Sampling.PupilArms).ToArray();
                var parentOptic = source.CreateOptic();
                StationaryValue ParentObjective(double[] x) => Optical(OpticalDesignParameters.Apply(parentOptic.ToSnapshot(), parameters, x), parameters, fields, pupils);
                var minimum = StationaryPointSearch.Minimize(ParentObjective, binding.InitialCoordinates.ToArray(), lower, upper,
                    policy.ParentIterations, policy.GradientTolerance, token);
                var optimizedParent = TopologyParameterBinder.Apply(source, binding, minimum.Coordinates);
                var parentAssessment = Assess(optimizedParent.Topology);
                if (!parentAssessment.Completed || parentAssessment.Violations.Any(v => v.Code.StartsWith("geometry:", StringComparison.Ordinal)))
                { parents.Add(new(id,"StationaryParentManufacturingRejected",original,optimizedParent.Topology,requests,null,null,site.Id)
                    {OriginalAssessment=originalAssessment,StationaryAssessment=parentAssessment});continue; }
                if (!directGrowthControl && !minimum.InteriorStationary)
                { parents.Add(new(id, "ParentNotInteriorStationary:" + minimum.StopReason, original, optimizedParent.Topology, requests, null, null, site.Id)); continue; }
                SpcStationarityEvidence? parentCertificate = null;
                if (!directGrowthControl)
                {
                    parentCertificate = Certify("parent-declared-curvature-subspace", ParentObjective, minimum.Coordinates, lower, upper, minimum.Evaluation, 0);
                    if (!parentCertificate.Certified) { parents.Add(new(id, "ParentMinimumNotCertified", original, optimizedParent.Topology, requests, parentCertificate, null, site.Id)); continue; }
                }
                var transition = SpcTransitionCompiler.Compile(SpcTransitionCompiler.Create(optimizedParent, site.Id, id + "-spc"));
                var nullOptic = transition.CreateOptic();
                var expanded = parameters.Select(p => p with { SurfaceIndex = p.SurfaceIndex > transition.OriginalBackIndex ? p.SurfaceIndex + 2 : p.SurfaceIndex }).ToList();
                expanded.Add(new(transition.NullFrontIndex, OpticalDesignParameterKind.Curvature, .01)); expanded.Add(new(transition.NullBackIndex, OpticalDesignParameterKind.Curvature, .01));
                var expandedParameters = expanded.ToArray(); var center = OpticalDesignParameters.Read(nullOptic, expandedParameters);
                var lo = lower.Concat(Enumerable.Repeat(-plan.Options.CurvatureBoundPerMillimeter / .01, 2)).ToArray();
                var hi = upper.Concat(Enumerable.Repeat(plan.Options.CurvatureBoundPerMillimeter / .01, 2)).ToArray();
                StationaryValue Research(double[] x) => Optical(OpticalDesignParameters.Apply(nullOptic.ToSnapshot(), expandedParameters, x), expandedParameters, fields, pupils,
                    transition.NullFrontIndex, transition.NullBackIndex);
                SpcStationarityEvidence? saddle = null; double[]? direction = null;
                if (!directGrowthControl)
                {
                    var at = Research(center);
                    if (Math.Abs(at.Value - minimum.Evaluation.Value) > 1e-8 * Math.Max(1, Math.Abs(at.Value))) throw new ArithmeticException("Null insertion changed the declared optical merit.");
                    saddle = Certify("expanded-declared-curvature-subspace", Research, center, lo, hi, at, 1);
                    if (!saddle.Certified) { parents.Add(new(id, "IndexOneSaddleNotCertified", original, optimizedParent.Topology, requests, parentCertificate, saddle, site.Id)); continue; }
                    direction = saddle.Fine.Eigenvectors[0].ToArray();
                    var pivot = Enumerable.Range(0, direction.Length).MaxBy(j => Math.Abs(direction[j]));
                    if (direction[pivot] < 0) direction = direction.Select(x => -x).ToArray();
                }
                parents.Add(new(id, directGrowthControl ? "DirectGrowthControl" : "IndexOneSaddleCertified", original, optimizedParent.Topology, requests, parentCertificate, saddle, site.Id)
                    { OriginalAssessment=originalAssessment,StationaryAssessment=parentAssessment });
                foreach (var sign in new[] { -1, 1 })
                {
                    var branchId = id + (sign < 0 ? ":minus" : ":plus"); var stages = new List<SpcPhysicalStage>();
                    var start = center.ToArray(); var endpoint = center.ToArray(); double? startMerit = null, endMerit = null;
                    DesignTopology? physical = null; TopologyAssessment? assessment = null; var outcome = "NotDispatched";
                    StationarySearchResult? nullMinimum = null;
                    SpcStationarityEvidence? branchCertificate = null;
                    try
                    {
                        Check();
                        if (!directGrowthControl)
                        {
                            var step = policy.BranchCoordinateStep;
                            for (var j = 0; j < center.Length; j++) if (direction![j] != 0)
                                step = Math.Min(step, .25 * Math.Min(center[j] - lo[j], hi[j] - center[j]) / Math.Abs(direction[j]));
                            start = center.Select((x, j) => x + sign * step * direction![j]).ToArray();
                            startMerit = Research(start).Value;
                            var spectrum = saddle!.Fine;
                            var inverseScale = Enumerable.Range(0, center.Length).Select(i => Enumerable.Range(0, center.Length).Select(j =>
                                Enumerable.Range(0, center.Length).Sum(k => spectrum.Eigenvectors[k][i] * spectrum.Eigenvectors[k][j]
                                    / Math.Max(Math.Abs(spectrum.Eigenvalues[k]), 8 * saddle.CurvatureUncertainty))).ToArray()).ToArray();
                            var descended = StationaryPointSearch.Minimize(Research, start, lo, hi, policy.BranchIterations, policy.GradientTolerance, token, inverseScale);
                            nullMinimum = descended;
                            endpoint = descended.Coordinates; endMerit = descended.Evaluation.Value; outcome = "NullBranch:" + descended.StopReason;
                            if (!descended.InteriorStationary) { AddBranch(); continue; }
                            branchCertificate = Certify("null-branch-declared-curvature-subspace", Research, endpoint, lo, hi, descended.Evaluation, 0);
                            if (!branchCertificate.Certified) { outcome = "NullBranchMinimumNotCertified"; AddBranch(); continue; }
                        }
                        else
                        {
                            // A genuine direct-growth comparison: two small common-bending physical starts,
                            // with no saddle certificate, no zero-state optimization and the same declared total caps.
                            var step = Math.Min(policy.BranchCoordinateStep, .25 * Math.Min(center[^1] - lo[^1], hi[^1] - center[^1]));
                            start[^2] += sign * step; start[^1] += sign * step; endpoint = start.ToArray();
                        }
                        var parentCoordinates = endpoint.Take(parameters.Length).ToArray();
                        var changedParent = TopologyParameterBinder.Apply(optimizedParent, TopologyParameterBinder.Bind(optimizedParent, requests, exactMechanicalFrame: true), parentCoordinates);
                        var insertedId = id + "-spc";
                        foreach (var multiplier in policy.ThicknessMultipliers)
                        {
                            Check(); var before = Work();
                            if (physical is null)
                            {
                                physical = Materialize(changedParent.Topology, site.Id, insertedId,
                                    endpoint[^2] * .01, endpoint[^1] * .01, plan.Constraints.MinimumCenterThicknessMillimeters * multiplier,
                                    plan.Constraints.MinimumAirGapMillimeters, initialGlass, plan.Constraints);
                            }
                            else physical = physical with { Elements = physical.Elements.Select(e => e.Id == insertedId
                                ? e with { ThicknessMillimeters = PositiveThickness(e.FrontZMillimeters, plan.Constraints.MinimumCenterThicknessMillimeters * multiplier) } : e).ToImmutableArray() };
                            physical = Polish(physical);
                            assessment = Assess(physical);
                            stages.Add(new("Thickness:" + multiplier, initialGlass, physical.Elements.Single(e => e.Id == insertedId).ThicknessMillimeters,
                                assessment.MeetsTargets ? "QualifiedOriginalSpecification" : "OriginalSpecificationFailed", physical, assessment, Difference(Work(), before)));
                        }
                        var materialBase = physical!; var best = physical!; var bestAssessment = assessment!;
                        foreach (var material in materials)
                        {
                            Check(); if (material == materialBase.Elements.Single(e => e.Id == insertedId).Material) continue;
                            var before = Work(); var trial = materialBase with { Elements = materialBase.Elements.Select(e => e.Id == insertedId ? e with { Material = material } : e).ToImmutableArray() };
                            trial = Polish(trial); var measured = Assess(trial);
                            stages.Add(new("CatalogGlass", material, trial.Elements.Single(e => e.Id == insertedId).ThicknessMillimeters,
                                measured.MeetsTargets ? "QualifiedOriginalSpecification" : "OriginalSpecificationFailed", trial, measured, Difference(Work(), before)));
                            if (Rank(measured) < Rank(bestAssessment)) { best = trial; bestAssessment = measured; }
                        }
                        physical = best; assessment = bestAssessment; outcome = assessment.MeetsTargets ? "QualifiedOriginalSpecification" : "OriginalSpecificationFailed";
                    }
                    catch (Exception error) when (error is Limit or OperationCanceledException || TopologyEvaluationPlan.DomainError(error))
                    {
                        outcome = error is Limit or OperationCanceledException ? "Interrupted:" + error.Message : "Domain:" + error.Message;
                        AddBranch();
                        if (error is Limit or OperationCanceledException || SequentialTraceBudgetExceededException.IsBudgetException(error)) throw;
                        continue;
                    }
                    AddBranch();
                    void AddBranch() => branches.Add(new(branchId, id, sign, outcome, start.ToImmutableArray(), endpoint.ToImmutableArray(), startMerit, endMerit, stages.ToImmutableArray(), physical, assessment)
                        { NullOptimizationStopReason=nullMinimum?.StopReason,NullGradient=nullMinimum?.Evaluation.Gradient.ToImmutableArray()??[],
                            NullInteriorStationary=nullMinimum?.InteriorStationary??false, NullMinimum=branchCertificate });
                }
                void ParentStop(string message) => parents.Add(new(id, message, original, null, [], null, null, null));
            }
        }
        catch (OperationCanceledException) { reason = cancellationToken.IsCancellationRequested ? "Cancelled" : "TimeLimit"; }
        catch (Limit error) { reason = error.Message; }
        catch (Exception error) when (SequentialTraceBudgetExceededException.IsBudgetException(error)) { reason = "RayBudget"; }
        catch (Exception error) when (TopologyEvaluationPlan.DomainError(error)) { reason = "Domain:" + error.Message; }
        return Finish(reason);

        void Check()
        {
            token.ThrowIfCancellationRequested();
            if (functions >= budget.MaximumFunctionEvaluations) throw new Limit("FunctionBudget");
            if (gradients >= policy.MaximumGradientEvaluations) throw new Limit("GradientBudget");
            progress?.Invoke(parents.Count == 0 ? "Parent" : "Branches", functions);
        }
        AutonomousSearchWork Work() => new() { FunctionEvaluations = functions, GradientEvaluations = gradients,
            ValidationEvaluations = validations, TracedRays = rays.RayCount, GeometryProbes = probes, Elapsed = timer.Elapsed };
        SpcBranchSearchResult Finish(string stop) => new() { Algorithm = directGrowthControl ? "initial-structure-direct-growth-control-v1" : "initial-structure-spc-branches-v1",
            ProtocolFingerprint = identity, StopReason = stop, Work = Work(), Parents = parents.ToImmutableArray(), Branches = branches.ToImmutableArray(),
            QualifiedBranchIds = branches.Where(b => b.Outcome == "QualifiedOriginalSpecification" && b.Assessment is { Completed: true, MeetsTargets: true }
                && b.Endpoint is { HasTransitionElements: false }).Select(b => b.Id).ToImmutableArray() };
        StationaryValue Optical(Optic optic, OpticalDesignParameter[] parameters, (double, double)[] fields, PupilSample[] pupils, int first = -1, int last = -1)
        {
            Check(); functions++; gradients++;
            var measured = first < 0 ? DifferentiableOpticalMetrics.EvaluateAtFields(optic, parameters, fields, pupils, token, DesignComputeMode.Cpu, true)
                : DifferentiableOpticalMetrics.EvaluateNullElementContinuation(optic, parameters, fields, pupils, first, last, token);
            var targets = plan.Targets;
            var value = DifferentialScalar.Square((measured.OpticalPowerPerMillimeter * targets.EffectiveFocalLengthMillimeters - 1) / targets.FocalLengthRelativeTolerance)
                + DifferentialScalar.Square((measured.InverseFNumber * targets.FNumber - 1) / targets.FNumberRelativeTolerance);
            foreach (var field in measured.Fields)
            {
                value += DifferentialScalar.Square(field.RmsRadiusMillimeters / targets.MaximumRmsRadiusMillimeters) / measured.Fields.Count;
                value += field.DistortionSquaredFraction / (targets.MaximumDistortionFraction * targets.MaximumDistortionFraction) / measured.Fields.Count;
                foreach (var ray in field.Rays) value += DifferentialScalar.Square(DifferentialScalar.PositivePart(-ray.MinimumClearanceMillimeters) / .01) / (field.Rays.Count * measured.Fields.Count);
            }
            return new(value.Value, value.Gradient());
        }
        SpcStationarityEvidence Certify(string scope, Func<double[], StationaryValue> evaluate, double[] point, double[] lo, double[] hi, StationaryValue at, int expectedNegative)
        {
            var a = StationaryPointSearch.Curvature(evaluate, point, lo, hi, policy.CurvatureDifferenceStep, token);
            var b = StationaryPointSearch.Curvature(evaluate, point, lo, hi, policy.CurvatureDifferenceStep / 2, token);
            var discrepancy = a.Hessian.Select((row, i) => row.Select((v, j) => Math.Abs(v - b.Hessian[i][j])).Sum()).Max();
            var uncertainty = Math.Max(8 * Math.Max(discrepancy, point.Length * Math.Max(a.SymmetryError, b.SymmetryError)), 1e-10 * Math.Max(1, b.Eigenvalues.Max(Math.Abs)));
            var count = b.Eigenvalues.Count(v => v < -uncertainty);
            var stationary = StationaryPointSearch.Norm(at.Gradient) <= policy.GradientTolerance * Math.Max(1, Math.Abs(at.Value))
                && point.Select((v, j) => v > lo[j] + 1e-7 && v < hi[j] - 1e-7).All(v => v);
            var stable = a.Eigenvalues.Count(v => v < -uncertainty) == count
                && a.Eigenvalues.Concat(b.Eigenvalues).All(v => Math.Abs(v) > uncertainty);
            return new(scope, at.Value, point.ToImmutableArray(), at.Gradient.ToImmutableArray(), stationary, a, b, uncertainty, count, stationary && stable && count == expectedNegative);
        }
        TopologyAssessment Assess(DesignTopology topology)
        {
            Check(); functions++; validations++;
            var result = TopologyValidator.Validate(TopologyCompiler.Compile(topology, plan.Constraints), plan.Targets, token); probes += result.GeometryProbes;
            token.ThrowIfCancellationRequested();
            if (SequentialTraceMeasurement.CpuBudgetReached) throw new Limit("RayBudget");
            return result;
        }
        DesignTopology Polish(DesignTopology topology)
        {
            Check(); var compiled = TopologyCompiler.Compile(topology, plan.Constraints); var variables = Variables(topology, plan.Options.CurvatureBoundPerMillimeter);
            var remaining = Math.Min(budget.MaximumFunctionEvaluations - functions - 1, policy.MaximumGradientEvaluations - gradients);
            if (remaining < 1) throw new Limit("PhysicalCompensationBudget");
            var options = plan.Options.Local with { MaximumUpdates = Math.Min(policy.PhysicalUpdatesPerStage, remaining - 1),
                MaximumFunctionEvaluations = Math.Min(remaining, policy.PhysicalUpdatesPerStage + 1), MaximumGradientEvaluations = Math.Min(remaining, policy.PhysicalUpdatesPerStage + 1),
                TimeLimit = budget.TimeLimit - timer.Elapsed, ComputeMode = DesignComputeMode.Cpu, PerformFinalAssessment = false };
            var result = FixedTopologyOptimizer.Run(compiled, TopologyParameterBinder.Bind(compiled, variables, exactMechanicalFrame: true), plan.Targets, options, token);
            functions += result.Work.FunctionEvaluations; gradients += result.Work.GradientEvaluations; probes += result.Work.GeometryProbes;
            token.ThrowIfCancellationRequested();
            if (SequentialTraceMeasurement.CpuBudgetReached) throw new Limit("RayBudget");
            return result.Topology;
        }
    }
    private static ImmutableArray<TopologyVariableRequest> Variables(DesignTopology topology, double bound) => topology.Elements.Where(e => !e.Protected)
        .SelectMany(e => new[] { (Id: e.FrontSurfaceId, Face: e.Front), (Id: e.BackSurfaceId, Face: e.Back) })
        .Where(f => f.Face.Profile.Kind != TopologyProfileKind.Plane).Select(f => new TopologyVariableRequest(f.Id, OpticalDesignParameterKind.Curvature, -bound, bound, .01)).ToImmutableArray();
    private static DesignTopology Materialize(DesignTopology parent, string siteId, string id, double front, double back, double thickness, double gap, string material, TopologyConstraints constraints)
    {
        var index = parent.Elements.ToList().FindIndex(e => e.Id == siteId); var site = parent.Elements[index];
        var z = site.BackZMillimeters + gap;
        while (z - site.BackZMillimeters < gap) z = Math.BitIncrement(z);
        var added = new LensElement { Id = id, FrontSurfaceId = id + ":front", BackSurfaceId = id + ":back", FrontZMillimeters = z,
            ThicknessMillimeters = PositiveThickness(z, thickness), Material = material, Front = site.Back with { Profile = site.Back.Profile with { CurvaturePerMillimeter = front } },
            Back = site.Back with { Profile = site.Back.Profile with { CurvaturePerMillimeter = back } } };
        return TopologyCompiler.Compile(parent with { Elements = parent.Elements.Insert(index + 1, added) }, constraints).Topology;
    }
    private static double PositiveThickness(double front, double minimum)
    { var back = front + minimum; while (back - front < minimum) back = Math.BitIncrement(back); return back - front; }
    private static double Rank(TopologyAssessment assessment) => (assessment.MeetsTargets ? 0 : 1e9) + assessment.Violations.Length * 1e6
        + (assessment.Fields.IsEmpty ? 1e5 : assessment.Fields.Max(f => f.RmsRadiusMillimeters ?? 1e5));
    private static AutonomousSearchWork Difference(AutonomousSearchWork a, AutonomousSearchWork b) => new() { FunctionEvaluations = a.FunctionEvaluations - b.FunctionEvaluations,
        GradientEvaluations = a.GradientEvaluations - b.GradientEvaluations, ValidationEvaluations = a.ValidationEvaluations - b.ValidationEvaluations,
        TracedRays = a.TracedRays - b.TracedRays, GeometryProbes = a.GeometryProbes - b.GeometryProbes, Elapsed = a.Elapsed - b.Elapsed };
    private static void Validate(AutonomousSearchPlan plan, ParaxialSeedSearchOptions budget, SpcBranchOptions policy)
    {
        _ = AutonomousSearchService.Create("spc-branch-preflight", plan);
        ValidatePolicy(budget, policy);
    }
    internal static void ValidatePolicy(ParaxialSeedSearchOptions budget, SpcBranchOptions policy)
    {
        if (budget.Version != 1 || policy.Version != 1 || policy.MaximumParents is < 1 or > 4 || policy.MaximumGradientEvaluations is < 1 or > 10000
            || policy.ParentIterations is < 1 or > 200 || policy.BranchIterations is < 1 or > 200 || policy.PhysicalUpdatesPerStage is < 0 or > 100
            || policy.MaximumMaterialAlternatives is < 1 or > 8 || !double.IsFinite(policy.GradientTolerance) || policy.GradientTolerance is < 1e-8 or > .001
            || !double.IsFinite(policy.CurvatureDifferenceStep) || policy.CurvatureDifferenceStep is < 1e-5 or > .01
            || !double.IsFinite(policy.BranchCoordinateStep) || policy.BranchCoordinateStep is <= 0 or > .5
            || policy.ThicknessMultipliers.IsDefaultOrEmpty || policy.ThicknessMultipliers.Length > 8
            || policy.ThicknessMultipliers.Any(v => !double.IsFinite(v) || v < 1) || !policy.ThicknessMultipliers.SequenceEqual(policy.ThicknessMultipliers.Order())
            || budget.MaximumFunctionEvaluations is < 1 or > 10000 || budget.MaximumTracedRays is < 1 or > 10000000 || budget.TimeLimit <= TimeSpan.Zero || budget.TimeLimit > TimeSpan.FromMinutes(10))
            throw new ArgumentException("Invalid bounded SPC research policy.");
    }
}

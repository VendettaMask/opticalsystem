using System.Collections.Immutable;
using System.Diagnostics;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Optimization;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.InitialStructure.Contracts;

namespace OpticalSystemDesign.InitialStructure.Engine;

public static partial class SpcBranchSearch
{
    private sealed class SpcPause : Exception;

    internal static SeedPreparationCheckpoint RunPreparation(SeedPreparationCheckpoint input, Action<SeedPreparationCheckpoint> persist,
        CancellationToken pause, Action<int, int>? progress)
    {
        var state = input; var s = state.Spc!; var plan = state.Plan; var options = state.Options; var policy = options.Spc!;
        if (state.Status == SeedPreparationStatus.Running && state.Pending is { InFlight: true } lost)
        {
            state = state with { Consumed = SeedPreparationSearch.Add(state.Consumed, lost.Reservation), Pending = null,
                LostReservations = state.LostReservations + 1, StopReason = "UnsettledReservationCharged", Stage = "Finished",
                Status = SeedPreparationStatus.Interrupted, Spc = s with { Phase = SpcPreparationPhase.Finished }, Revision = state.Revision + 1 };
            state = state with { Result = PreparationResult(state) }; persist(state); return state;
        }
        var beforeRays = state.Consumed.TracedRays; var beforeElapsed = state.Consumed.Elapsed;
        var timer = Stopwatch.StartNew(); var persistence = state.PersistenceElapsed;
        using var deadline = new CancellationTokenSource(options.TimeLimit > beforeElapsed ? options.TimeLimit - beforeElapsed : TimeSpan.Zero);
        var token = deadline.Token;
        using var serial = ComputationParallelism.SuppressNestedParallelism();
        using var rays = SequentialTraceMeasurement.BeginCpuBudget(Math.Max(0, options.MaximumTracedRays - beforeRays));
        var random = DeterministicRandom.Restore(state.RandomState);
        try
        {
            CheckPause(); Check(0, 0);
            if (s.Order.IsEmpty)
            {
                if (plan.Seeds.Any(t => StructureSearchDiagnostics.ExternalStopAperturePrecheck(t).ProvenInfeasible)) return Finish("OriginalExternalStopApertureConflict");
                if (plan.Options.FixedTopologyOnly == true || plan.Options.QualifiedReductionOnly == true || plan.Options.Expansion?.EnableInsertion == false)
                    return Finish("OriginalInsertionPermissionConflict");
                s = s with { Order = plan.Seeds.Select((_, i) => (i, rank: random.NextUnitDouble())).OrderBy(x => x.rank)
                    .Take(policy.MaximumParents).Select(x => x.i).ToImmutableArray() };
                Save();
            }
            using var cpu = new SequentialGpuSession(DesignComputeMode.Cpu, token);
            while (s.ParentCursor < s.Order.Length)
            {
                Check(0, 0); CheckPause();
                var original = plan.Seeds[s.Order[s.ParentCursor]]; var id = "parent-" + s.Order[s.ParentCursor];
                var materials = plan.Options.InsertionMaterials.Concat(plan.Options.Expansion is { EnableMaterial: true } expansion ? expansion.MaterialAlternatives : [])
                    .Where(m => plan.Constraints.AllowedGlassNames.IsEmpty || plan.Constraints.AllowedGlassNames.Contains(m)).Distinct(StringComparer.Ordinal)
                    .Take(policy.MaximumMaterialAlternatives).ToArray();
                if (s.Phase == SpcPreparationPhase.SelectParent)
                {
                    if (original.Elements.Length >= plan.Constraints.MaximumElementCount) { RejectParent("OriginalElementLimit"); continue; }
                    if (original.CementedInterfaces is not null || original.HasTransitionElements) { RejectParent("UnsupportedParentStructure"); continue; }
                    var sites = original.Elements.Where(e => !e.Protected && e.Back.Profile.Kind == TopologyProfileKind.Standard && e.Back.Profile.Conic == 0
                        && e.BackZMillimeters > original.Stop.ZMillimeters).ToArray();
                    if (sites.Length == 0) { RejectParent("NoPermittedSphericalPostStopInterface"); continue; }
                    var variables = Variables(original, plan.Options.CurvatureBoundPerMillimeter);
                    if (variables.Length is < 1 or > 30) { RejectParent("StationarityDimensionUnsupported"); continue; }
                    if (materials.Length == 0) { RejectParent("NoPermittedInsertionGlass"); continue; }
                    s = s with { SiteId = sites[random.NextInt32(sites.Length)].Id, Variables = variables, Phase = SpcPreparationPhase.OriginalAssessment };
                    Save(); continue;
                }
                var site = original.Elements.Single(e => e.Id == s.SiteId);
                var initialGlass = materials.Contains(site.Material) ? site.Material : materials[0];
                if (s.Phase == SpcPreparationPhase.OriginalAssessment)
                {
                    var assessment = Assess(original); s = s with { OriginalAssessment = assessment };
                    if (!Manufacturing(assessment)) { RejectParent("ParentManufacturingRejected"); continue; }
                    Advance(SpcPreparationPhase.ParentOptimization); continue;
                }
                var source = TopologyCompiler.Compile(original, plan.Constraints);
                var binding = TopologyParameterBinder.Bind(source, s.Variables, exactMechanicalFrame: true);
                var parameters = binding.CoreParameters.ToArray(); var lo = binding.Slots.Select(x => x.LowerCoordinate).ToArray(); var hi = binding.Slots.Select(x => x.UpperCoordinate).ToArray();
                var fields = TopologyEvaluationPlan.Fields(source, plan.Options.Local.Sampling.FieldsPerDirection).Select(f => (f.X, f.Y)).ToArray();
                var pupils = ApertureSampler.GenerateGaussianQuadrature(plan.Options.Local.Sampling.PupilRings, plan.Options.Local.Sampling.PupilArms).ToArray();
                var parentOptic = source.CreateOptic();
                StationaryValue Parent(double[] x) => Optical(OpticalDesignParameters.Apply(parentOptic.ToSnapshot(), parameters, x), parameters, fields, pupils);
                if (s.Phase == SpcPreparationPhase.ParentOptimization)
                {
                    var minimum = StationaryPointSearch.Minimize(Parent, binding.InitialCoordinates.ToArray(), lo, hi, policy.ParentIterations, policy.GradientTolerance,
                        token, continuation: s.Optimizer, checkpoint: c => { s = s with { Optimizer = c }; Save(); });
                    s = s with { StationaryParent = TopologyParameterBinder.Apply(source, binding, minimum.Coordinates).Topology };
                    if (!minimum.InteriorStationary) { RejectParent("ParentNotInteriorStationary:" + minimum.StopReason); continue; }
                    Advance(SpcPreparationPhase.ParentAssessment); continue;
                }
                if (s.Phase == SpcPreparationPhase.ParentAssessment)
                {
                    var measured = Assess(s.StationaryParent!); s = s with { ParentAssessment = measured };
                    if (!Manufacturing(measured)) { RejectParent("StationaryParentManufacturingRejected"); continue; }
                    AddPool(id + ":stationary-parent", s.StationaryParent!, measured);
                    Advance(SpcPreparationPhase.ParentCurvature); continue;
                }
                if (s.Phase == SpcPreparationPhase.ParentCurvature)
                {
                    var certificate = Certificate("parent-declared-curvature-subspace", Parent, s.Optimizer!.Coordinates, lo, hi, s.Optimizer.Evaluation, 0);
                    s = s with { ParentMinimum = certificate };
                    if (!certificate.Certified) { RejectParent("ParentMinimumNotCertified"); continue; }
                    Advance(SpcPreparationPhase.NullMerit); continue;
                }
                var stationary = TopologyCompiler.Compile(s.StationaryParent!, plan.Constraints);
                var transition = SpcTransitionCompiler.Compile(SpcTransitionCompiler.Create(stationary, site.Id, id + "-spc"));
                var optic = transition.CreateOptic();
                var expanded = parameters.Select(p => p with { SurfaceIndex = p.SurfaceIndex > transition.OriginalBackIndex ? p.SurfaceIndex + 2 : p.SurfaceIndex }).ToList();
                expanded.Add(new(transition.NullFrontIndex, OpticalDesignParameterKind.Curvature, .01)); expanded.Add(new(transition.NullBackIndex, OpticalDesignParameterKind.Curvature, .01));
                var expandedParameters = expanded.ToArray(); var center = OpticalDesignParameters.Read(optic, expandedParameters);
                var lower = lo.Concat(Enumerable.Repeat(-plan.Options.CurvatureBoundPerMillimeter / .01, 2)).ToArray();
                var upper = hi.Concat(Enumerable.Repeat(plan.Options.CurvatureBoundPerMillimeter / .01, 2)).ToArray();
                StationaryValue Null(double[] x) => Optical(OpticalDesignParameters.Apply(optic.ToSnapshot(), expandedParameters, x), expandedParameters, fields, pupils,
                    transition.NullFrontIndex, transition.NullBackIndex);
                if (s.Phase == SpcPreparationPhase.NullMerit)
                {
                    var at = Null(center);
                    if (Math.Abs(at.Value - s.Optimizer!.Evaluation.Value) > 1e-8 * Math.Max(1, Math.Abs(at.Value))) throw new ArithmeticException("Null insertion changed the declared optical merit.");
                    s = s with { NullMerit = at }; Advance(SpcPreparationPhase.SaddleCurvature); continue;
                }
                if (s.Phase == SpcPreparationPhase.SaddleCurvature)
                {
                    var certificate = Certificate("expanded-declared-curvature-subspace", Null, center, lower, upper, s.NullMerit!, 1);
                    s = s with { Saddle = certificate };
                    if (!certificate.Certified) { RejectParent("IndexOneSaddleNotCertified"); continue; }
                    s = s with { Parents = s.Parents.Add(ParentEvidence("IndexOneSaddleCertified")) };
                    Advance(SpcPreparationPhase.StartBranch); continue;
                }
                var branchId = id + (s.Sign < 0 ? ":minus" : ":plus");
                if (s.Phase == SpcPreparationPhase.StartBranch)
                {
                    var direction = s.Saddle!.Fine.Eigenvectors[0].ToArray();
                    var pivot = Enumerable.Range(0, direction.Length).MaxBy(j => Math.Abs(direction[j]));
                    if (direction[pivot] < 0) direction = direction.Select(v => -v).ToArray();
                    var step = policy.BranchCoordinateStep;
                    for (var j = 0; j < center.Length; j++) if (direction[j] != 0) step = Math.Min(step,
                        .25 * Math.Min(center[j] - lower[j], upper[j] - center[j]) / Math.Abs(direction[j]));
                    var start = center.Select((x, j) => x + s.Sign * step * direction[j]).ToArray();
                    var at = Null(start);
                    s = s with { InitialCoordinates = start.ToImmutableArray(), InitialValue = at };
                    Advance(SpcPreparationPhase.BranchOptimization); continue;
                }
                if (s.Phase == SpcPreparationPhase.BranchOptimization)
                {
                    var spectrum = s.Saddle!.Fine;
                    var inverse = Enumerable.Range(0, center.Length).Select(i => Enumerable.Range(0, center.Length).Select(j =>
                        Enumerable.Range(0, center.Length).Sum(k => spectrum.Eigenvectors[k][i] * spectrum.Eigenvectors[k][j]
                            / Math.Max(Math.Abs(spectrum.Eigenvalues[k]), 8 * s.Saddle.CurvatureUncertainty))).ToArray()).ToArray();
                    var minimum = StationaryPointSearch.Minimize(Null, s.InitialCoordinates.ToArray(), lower, upper, policy.BranchIterations,
                        policy.GradientTolerance, token, inverse, s.BranchMinimum, c => { s = s with { BranchMinimum = c }; Save(); });
                    if (!minimum.InteriorStationary) { FinishBranch("NullBranch:" + minimum.StopReason); continue; }
                    Advance(SpcPreparationPhase.BranchCurvature); continue;
                }
                if (s.Phase == SpcPreparationPhase.BranchCurvature)
                {
                    var certificate = Certificate("null-branch-declared-curvature-subspace", Null, s.BranchMinimum!.Coordinates, lower, upper, s.BranchMinimum.Evaluation, 0);
                    s = s with { BranchCertificate = certificate };
                    if (!certificate.Certified) { FinishBranch("NullBranchMinimumNotCertified"); continue; }
                    Advance(SpcPreparationPhase.PhysicalSetup); continue;
                }
                if (s.Phase == SpcPreparationPhase.PhysicalSetup)
                {
                    if (s.ThicknessCursor >= policy.ThicknessMultipliers.Length && s.MaterialCursor >= materials.Length)
                    { FinishBranch(s.BestAssessment?.MeetsTargets == true ? "QualifiedOriginalSpecification" : "OriginalSpecificationFailed"); continue; }
                    DesignTopology physical; string stageName;
                    if (s.ThicknessCursor < policy.ThicknessMultipliers.Length)
                    {
                        var thickness = plan.Constraints.MinimumCenterThicknessMillimeters * policy.ThicknessMultipliers[s.ThicknessCursor];
                        if (s.PhysicalOutput is null)
                        {
                            var x = s.BranchMinimum!.Coordinates;
                            var parent = TopologyParameterBinder.Apply(stationary, TopologyParameterBinder.Bind(stationary, s.Variables, exactMechanicalFrame: true), x.Take(parameters.Length).ToArray());
                            physical = Materialize(parent.Topology, site.Id, id + "-spc", x[^2] * .01, x[^1] * .01, thickness,
                                plan.Constraints.MinimumAirGapMillimeters, initialGlass, plan.Constraints);
                        }
                        else physical = s.PhysicalOutput with { Elements = s.PhysicalOutput.Elements.Select(e => e.Id == id + "-spc"
                            ? e with { ThicknessMillimeters = PositiveThickness(e.FrontZMillimeters, thickness) } : e).ToImmutableArray() };
                        stageName = "Thickness:" + policy.ThicknessMultipliers[s.ThicknessCursor];
                    }
                    else
                    {
                        var material = materials[s.MaterialCursor];
                        if (material == s.MaterialBase!.Elements.Single(e => e.Id == id + "-spc").Material)
                        { s = s with { MaterialCursor = s.MaterialCursor + 1 }; Save(); continue; }
                        physical = s.MaterialBase with { Elements = s.MaterialBase.Elements.Select(e => e.Id == id + "-spc" ? e with { Material = material } : e).ToImmutableArray() };
                        stageName = "CatalogGlass";
                    }
                    var remaining = Math.Min(options.MaximumFunctionEvaluations - state.Consumed.FunctionEvaluations - 1,
                        policy.MaximumGradientEvaluations - state.Consumed.GradientEvaluations);
                    if (remaining < 1) throw new Limit("PhysicalCompensationBudget");
                    s = s with { PhysicalSource = physical, PhysicalOutput = null, PhysicalStageName = stageName, PhysicalBase = Work(), StageBase = Work(), PhysicalContinuation = null,
                        PhysicalOptions = plan.Options.Local with { MaximumUpdates = Math.Min(policy.PhysicalUpdatesPerStage, remaining - 1),
                            MaximumFunctionEvaluations = Math.Min(remaining, policy.PhysicalUpdatesPerStage + 1), MaximumGradientEvaluations = Math.Min(remaining, policy.PhysicalUpdatesPerStage + 1),
                            ComputeMode = DesignComputeMode.Cpu, PerformFinalAssessment = false, TimeLimit = options.TimeLimit } };
                    Advance(SpcPreparationPhase.PhysicalOptimization); continue;
                }
                if (s.Phase == SpcPreparationPhase.PhysicalOptimization)
                {
                    Reserve(Math.Min(options.MaximumFunctionEvaluations - state.Consumed.FunctionEvaluations, s.PhysicalOptions!.MaximumFunctionEvaluations),
                        Math.Min(policy.MaximumGradientEvaluations - state.Consumed.GradientEvaluations, s.PhysicalOptions.MaximumGradientEvaluations));
                    var compiled = TopologyCompiler.Compile(s.PhysicalSource!, plan.Constraints);
                    var result = FixedTopologyOptimizer.Run(compiled, TopologyParameterBinder.Bind(compiled, Variables(compiled.Topology, plan.Options.CurvatureBoundPerMillimeter), exactMechanicalFrame: true),
                        plan.Targets, s.PhysicalOptions, token, continuation: s.PhysicalContinuation, checkpoint: c =>
                        {
                            Account(c.Work); s = s with { PhysicalContinuation = c }; Save();
                            if (c.Work.FunctionEvaluations < s.PhysicalOptions.MaximumFunctionEvaluations && c.Work.GradientEvaluations < s.PhysicalOptions.MaximumGradientEvaluations)
                                Reserve(s.PhysicalOptions.MaximumFunctionEvaluations - c.Work.FunctionEvaluations, s.PhysicalOptions.MaximumGradientEvaluations - c.Work.GradientEvaluations);
                            return false;
                        });
                    Account(result.Work); token.ThrowIfCancellationRequested();
                    if (SequentialTraceMeasurement.CpuBudgetReached) throw new Limit("RayBudget");
                    s = s with { PhysicalOutput = result.Topology, PhysicalContinuation = result.Continuation };
                    Advance(SpcPreparationPhase.PhysicalAssessment); continue;
                }
                if (s.Phase == SpcPreparationPhase.PhysicalAssessment)
                {
                    var assessment = Assess(s.PhysicalOutput!); var added = s.PhysicalOutput!.Elements.Single(e => e.Id == id + "-spc");
                    var stage = new SpcPhysicalStage(s.PhysicalStageName!, added.Material, added.ThicknessMillimeters,
                        assessment.MeetsTargets ? "QualifiedOriginalSpecification" : "OriginalSpecificationFailed", s.PhysicalOutput, assessment, Difference(Work(), s.StageBase));
                    var thickness = s.ThicknessCursor < policy.ThicknessMultipliers.Length;
                    var lastThickness = thickness && s.ThicknessCursor + 1 == policy.ThicknessMultipliers.Length;
                    var better = thickness || s.BestAssessment is null || Rank(assessment) < Rank(s.BestAssessment);
                    s = s with { Stages = s.Stages.Add(stage), ThicknessCursor = s.ThicknessCursor + (thickness ? 1 : 0),
                        MaterialCursor = s.MaterialCursor + (thickness ? 0 : 1), MaterialBase = lastThickness ? s.PhysicalOutput : s.MaterialBase,
                        BestPhysical = better ? s.PhysicalOutput : s.BestPhysical, BestAssessment = better ? assessment : s.BestAssessment };
                    AddPool(branchId + ":stage-" + s.Stages.Length, s.PhysicalOutput, assessment);
                    Advance(SpcPreparationPhase.PhysicalSetup); continue;
                }
                throw new InvalidDataException("Unsupported SPC phase.");

                SpcParentEvidence ParentEvidence(string outcome) => new(id, outcome, original, s.StationaryParent, s.Variables,
                    s.ParentMinimum, s.Saddle, s.SiteId) { OriginalAssessment = s.OriginalAssessment, StationaryAssessment = s.ParentAssessment };
                void RejectParent(string outcome)
                {
                    var parents = s.Parents.Add(ParentEvidence(outcome)); var branches = s.Branches; var pool = s.Pool; var order = s.Order; var next = s.ParentCursor + 1;
                    s = new() { Order = order, ParentCursor = next, Parents = parents, Branches = branches, Pool = pool }; Save();
                }
                void FinishBranch(string outcome)
                {
                    var branch = new SpcOptimizedBranch(branchId, id, s.Sign, outcome, s.InitialCoordinates,
                        s.BranchMinimum?.Coordinates.ToImmutableArray() ?? s.InitialCoordinates, s.InitialValue?.Value, s.BranchMinimum?.Evaluation.Value,
                        s.Stages, s.BestPhysical, s.BestAssessment)
                    { NullMinimum = s.BranchCertificate, NullGradient = s.BranchMinimum?.Evaluation.Gradient.ToImmutableArray() ?? [],
                        NullInteriorStationary = s.BranchCertificate?.InteriorStationary ?? false, NullOptimizationStopReason = s.BranchMinimum?.StopReason };
                    var branches = s.Branches.Add(branch);
                    if (s.Sign < 0) s = s with { Sign = 1, Branches = branches, Phase = SpcPreparationPhase.StartBranch,
                        InitialCoordinates = [], InitialValue = null, BranchMinimum = null, BranchCertificate = null, ThicknessCursor = 0, MaterialCursor = 0,
                        PhysicalSource = null, PhysicalOptions = null, PhysicalContinuation = null, PhysicalOutput = null, MaterialBase = null,
                        BestPhysical = null, BestAssessment = null, Stages = [], Curvature = null, Coarse = null };
                    else s = new() { Order = s.Order, ParentCursor = s.ParentCursor + 1, Parents = s.Parents, Branches = branches, Pool = s.Pool };
                    Save();
                }
            }
            return Finish("Completed");
        }
        catch (SpcPause) { return state; }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return Finish("TimeLimit", SeedPreparationStatus.TimeLimit); }
        catch (Limit error) { return Finish(error.Message, SeedPreparationStatus.BudgetExhausted); }
        catch (Exception error) when (SequentialTraceBudgetExceededException.IsBudgetException(error)) { return Finish("RayBudget", SeedPreparationStatus.BudgetExhausted); }
        catch (Exception error) when (TopologyEvaluationPlan.DomainError(error)) { return Finish("Domain:" + error.Message); }

        AutonomousSearchWork Work() => state.Consumed with { Elapsed = beforeElapsed + timer.Elapsed, TracedRays = beforeRays + rays.RayCount };
        void Check(int functions, int gradients)
        {
            token.ThrowIfCancellationRequested();
            if (state.Consumed.FunctionEvaluations + functions > options.MaximumFunctionEvaluations) throw new Limit("FunctionBudget");
            if (state.Consumed.GradientEvaluations + gradients > policy.MaximumGradientEvaluations) throw new Limit("GradientBudget");
            if (beforeRays + rays.RayCount >= options.MaximumTracedRays) throw new Limit("RayBudget");
        }
        void Reserve(int functions, int gradients)
        {
            if (state.Pending is not null) return;
            CheckPause(); Check(0, 0);
            state = state with { Pending = new() { Name = "SPC:" + s.Phase, Source = plan.Seeds[s.Order.IsEmpty ? 0 : s.Order[Math.Min(s.ParentCursor, s.Order.Length - 1)]],
                Variables = s.Variables, PhaseBase = Work(), InFlight = true, Reservation = new()
                { FunctionEvaluations = Math.Max(0, Math.Min(functions, options.MaximumFunctionEvaluations - state.Consumed.FunctionEvaluations)),
                    GradientEvaluations = Math.Max(0, Math.Min(gradients, policy.MaximumGradientEvaluations - state.Consumed.GradientEvaluations)),
                    TracedRays = options.MaximumTracedRays - beforeRays - rays.RayCount, Elapsed = options.TimeLimit > Work().Elapsed ? options.TimeLimit - Work().Elapsed : TimeSpan.Zero } } };
            Save(SeedPreparationStatus.Running, clearReservation: false, checkPause: false); CheckPause();
        }
        void CheckPause()
        { if (pause.IsCancellationRequested) { Save(SeedPreparationStatus.Suspended, checkPause: false); throw new SpcPause(); } }
        void Advance(SpcPreparationPhase phase) { s = s with { Phase = phase, Curvature = null, Coarse = null }; Save(); }
        void Save(SeedPreparationStatus status = SeedPreparationStatus.Ready, bool clearReservation = true, bool checkPause = true)
        {
            timer.Stop(); deadline.CancelAfter(Timeout.InfiniteTimeSpan);
            state = state with { Spc = s, Stage = s.Phase == SpcPreparationPhase.Finished ? "Finished" : "Spc:" + s.Phase,
                Cursor = state.Cursor + 1, RandomState = random.State, Consumed = Work(), Status = status,
                Pending = clearReservation ? null : state.Pending, Revision = state.Revision + 1, PersistenceElapsed = persistence };
            if (s.Phase == SpcPreparationPhase.Finished) state = state with { Result = PreparationResult(state) };
            var saving = Stopwatch.StartNew();
            try { persist(state); }
            finally { persistence += saving.Elapsed; timer.Start(); var left = options.TimeLimit - Work().Elapsed; deadline.CancelAfter(left > TimeSpan.Zero ? left : TimeSpan.Zero); }
            progress?.Invoke(state.Cursor, state.Consumed.FunctionEvaluations);
            if (checkPause) CheckPause();
        }
        SeedPreparationCheckpoint Finish(string reason, SeedPreparationStatus status = SeedPreparationStatus.Completed)
        { s = s with { Phase = SpcPreparationPhase.Finished }; state = state with { StopReason = reason }; Save(status, checkPause: false); return state; }
        void AddPool(string id, DesignTopology topology, TopologyAssessment assessment)
        {
            if (!assessment.Completed || !assessment.MeetsTargets || topology.HasTransitionElements) return;
            var hash = TopologyCompiler.Compile(topology, plan.Constraints).OpticFingerprint;
            if (!s.Pool.Any(c => c.Assessment.OpticFingerprint == hash))
                s = s with { Pool = s.Pool.Add(new(id, null, topology, assessment, assessment.Fields.Max(f => f.RmsRadiusMillimeters ?? double.PositiveInfinity))) };
        }
        StationaryValue Optical(Optic optic, OpticalDesignParameter[] parameters, (double, double)[] fields, PupilSample[] pupils, int first = -1, int last = -1)
        {
            Check(1, 1); Reserve(options.MaximumFunctionEvaluations - state.Consumed.FunctionEvaluations, policy.MaximumGradientEvaluations - state.Consumed.GradientEvaluations);
            state = state with { Consumed = state.Consumed with { FunctionEvaluations = state.Consumed.FunctionEvaluations + 1, GradientEvaluations = state.Consumed.GradientEvaluations + 1 } };
            var measured = first < 0 ? DifferentiableOpticalMetrics.EvaluateAtFields(optic, parameters, fields, pupils, token, DesignComputeMode.Cpu, true)
                : DifferentiableOpticalMetrics.EvaluateNullElementContinuation(optic, parameters, fields, pupils, first, last, token);
            var t = plan.Targets;
            var value = DifferentialScalar.Square((measured.OpticalPowerPerMillimeter * t.EffectiveFocalLengthMillimeters - 1) / t.FocalLengthRelativeTolerance)
                + DifferentialScalar.Square((measured.InverseFNumber * t.FNumber - 1) / t.FNumberRelativeTolerance);
            foreach (var field in measured.Fields)
            {
                value += DifferentialScalar.Square(field.RmsRadiusMillimeters / t.MaximumRmsRadiusMillimeters) / measured.Fields.Count;
                value += field.DistortionSquaredFraction / (t.MaximumDistortionFraction * t.MaximumDistortionFraction) / measured.Fields.Count;
                foreach (var ray in field.Rays) value += DifferentialScalar.Square(DifferentialScalar.PositivePart(-ray.MinimumClearanceMillimeters) / .01) / (field.Rays.Count * measured.Fields.Count);
            }
            return new(value.Value, value.Gradient());
        }
        SpcStationarityEvidence Certificate(string scope, Func<double[], StationaryValue> evaluate, double[] point, double[] lo, double[] hi, StationaryValue at, int negative)
        {
            StationaryValue DifferenceValue(double[] x)
            {
                var value = evaluate(x); state = state with { Consumed = state.Consumed with { DifferenceEvaluations = state.Consumed.DifferenceEvaluations + 1 } }; return value;
            }
            if (s.Coarse is null)
            {
                var a = StationaryPointSearch.Curvature(DifferenceValue, point, lo, hi, policy.CurvatureDifferenceStep, token, s.Curvature,
                    c => { s = s with { Curvature = c }; Save(); });
                s = s with { Coarse = a, Curvature = null }; Save();
            }
            var coarse = s.Coarse;
            var fine = StationaryPointSearch.Curvature(DifferenceValue, point, lo, hi, policy.CurvatureDifferenceStep / 2, token, s.Curvature,
                c => { s = s with { Curvature = c }; Save(); });
            var delta = coarse.Hessian.Select((r, i) => r.Select((v, j) => Math.Abs(v - fine.Hessian[i][j])).Sum()).Max();
            var uncertainty = Math.Max(8 * Math.Max(delta, point.Length * Math.Max(coarse.SymmetryError, fine.SymmetryError)), 1e-10 * Math.Max(1, fine.Eigenvalues.Max(Math.Abs)));
            var count = fine.Eigenvalues.Count(v => v < -uncertainty);
            var stationary = StationaryPointSearch.Norm(at.Gradient) <= policy.GradientTolerance * Math.Max(1, Math.Abs(at.Value))
                && point.Select((v, j) => v > lo[j] + 1e-7 && v < hi[j] - 1e-7).All(v => v);
            var stable = coarse.Eigenvalues.Count(v => v < -uncertainty) == count && coarse.Eigenvalues.Concat(fine.Eigenvalues).All(v => Math.Abs(v) > uncertainty);
            return new(scope, at.Value, point.ToImmutableArray(), at.Gradient.ToImmutableArray(), stationary, coarse, fine, uncertainty, count, stationary && stable && count == negative);
        }
        TopologyAssessment Assess(DesignTopology topology)
        {
            Check(1, 0); Reserve(1, 0); state = state with { Consumed = state.Consumed with { FunctionEvaluations = state.Consumed.FunctionEvaluations + 1,
                ValidationEvaluations = state.Consumed.ValidationEvaluations + 1 } };
            var result = TopologyValidator.Validate(TopologyCompiler.Compile(topology, plan.Constraints), plan.Targets, token);
            state = state with { Consumed = state.Consumed with { GeometryProbes = state.Consumed.GeometryProbes + result.GeometryProbes } };
            token.ThrowIfCancellationRequested(); if (SequentialTraceMeasurement.CpuBudgetReached) throw new Limit("RayBudget"); return result;
        }
        void Account(FixedTopologyWork work) => state = state with { Consumed = state.Consumed with
        { FunctionEvaluations = s.PhysicalBase.FunctionEvaluations + work.FunctionEvaluations, GradientEvaluations = s.PhysicalBase.GradientEvaluations + work.GradientEvaluations,
            GeometryProbes = s.PhysicalBase.GeometryProbes + work.GeometryProbes } };
    }

    private static bool Manufacturing(TopologyAssessment a) => a.Completed && !a.Violations.Any(v => v.Code.StartsWith("geometry:", StringComparison.Ordinal));

    internal static ParaxialSeedSearchResult PreparationResult(SeedPreparationCheckpoint state)
    {
        var s = state.Spc!;
        var branchResult = new SpcBranchSearchResult { Algorithm = "initial-structure-spc-preparation-v1", ProtocolFingerprint = state.ProtocolFingerprint,
            StopReason = state.StopReason ?? state.Status.ToString(), Work = state.Consumed, Parents = s.Parents, Branches = s.Branches,
            QualifiedBranchIds = s.Branches.Where(b => b.Outcome == "QualifiedOriginalSpecification" && b.Assessment?.MeetsTargets == true).Select(b => b.Id).ToImmutableArray() };
        var eligible = state.Status == SeedPreparationStatus.Completed && state.Stage == "Finished" && state.Consumed.Elapsed <= state.Options.TimeLimit;
        var pool = s.Pool.Where(c => c.Assessment is { Completed: true, MeetsTargets: true } && !c.Topology.HasTransitionElements).ToArray();
        // Keep the original qualified parent. A grown candidate enters the seed portfolio only if its RMS improves all no-more-complex alternatives.
        var nondominated = pool.Where(c => !pool.Any(other => other.Id != c.Id && other.Topology.Elements.Length <= c.Topology.Elements.Length
            && other.Score <= c.Score && (other.Topology.Elements.Length < c.Topology.Elements.Length || other.Score < c.Score)))
            .OrderBy(c => c.Topology.Elements.Length).ThenBy(c => c.Score).ThenBy(c => c.Id, StringComparer.Ordinal).Take(state.Options.RetainedSeeds);
        return new() { Algorithm = branchResult.Algorithm, ProtocolFingerprint = state.ProtocolFingerprint, SourcePlan = state.Plan, Options = state.Options,
            Spc = branchResult, Work = state.Consumed, Seeds = eligible ? nondominated.Select(c => c.Topology).ToImmutableArray() : [],
            Termination = state.Status == SeedPreparationStatus.Suspended ? ParaxialSeedTermination.Cancelled : state.Status == SeedPreparationStatus.TimeLimit ? ParaxialSeedTermination.TimeLimit
                : state.Status is SeedPreparationStatus.Interrupted or SeedPreparationStatus.BudgetExhausted ? ParaxialSeedTermination.BudgetExhausted : ParaxialSeedTermination.Completed };
    }

    internal static void ValidatePreparation(SeedPreparationCheckpoint state, SeedPreparationCheckpoint expected)
    {
        var s = state.Spc ?? throw new InvalidDataException("Missing typed SPC continuation."); var policy = state.Options.Spc!;
        if (state.ProtocolFingerprint != expected.ProtocolFingerprint || s.Version != 1 || !Enum.IsDefined(s.Phase) || !Enum.IsDefined(state.Status)
            || state.EngineState is not null || state.Cursor < 0 || s.ParentCursor < 0 || s.ParentCursor > s.Order.Length
            || s.Order.Length > policy.MaximumParents || s.Order.Distinct().Count() != s.Order.Length || s.Order.Any(i => i < 0 || i >= state.Plan.Seeds.Length)
            || s.Sign is not (-1 or 1) || s.ThicknessCursor < 0 || s.ThicknessCursor > policy.ThicknessMultipliers.Length || s.MaterialCursor < 0 || s.MaterialCursor > policy.MaximumMaterialAlternatives
            || !SeedPreparationSearch.Nonnegative(state.Consumed) || state.Consumed.FunctionEvaluations > state.Options.MaximumFunctionEvaluations
            || state.Consumed.GradientEvaluations > policy.MaximumGradientEvaluations || state.Consumed.TracedRays > state.Options.MaximumTracedRays
            || s.Parents.Length > policy.MaximumParents || s.Parents.Select(p => p.Id).Distinct().Count() != s.Parents.Length
            || s.Branches.Length > policy.MaximumParents * 2 || s.Branches.Select(b => b.Id).Distinct().Count() != s.Branches.Length
            || s.Pool.Length > policy.MaximumParents * (1 + 2 * (policy.ThicknessMultipliers.Length + policy.MaximumMaterialAlternatives))
            || state.LostReservations < 0 || state.PersistenceElapsed < TimeSpan.Zero
            || state.Stage != (s.Phase == SpcPreparationPhase.Finished ? "Finished" : "Spc:" + s.Phase)) throw new InvalidDataException("Invalid SPC checkpoint phase, lineage or budget.");
        if (state.Pending is { } p && (!p.InFlight || state.Status != SeedPreparationStatus.Running || !SeedPreparationSearch.Nonnegative(p.Reservation)
            || state.Consumed.FunctionEvaluations + p.Reservation.FunctionEvaluations > state.Options.MaximumFunctionEvaluations
            || state.Consumed.GradientEvaluations + p.Reservation.GradientEvaluations > policy.MaximumGradientEvaluations
            || state.Consumed.TracedRays + p.Reservation.TracedRays > state.Options.MaximumTracedRays)) throw new InvalidDataException("Invalid SPC reservation.");
        foreach (var candidate in s.Pool)
        {
            var compiled = TopologyCompiler.Compile(candidate.Topology, state.Plan.Constraints);
            if (!candidate.Assessment.Completed || !candidate.Assessment.MeetsTargets || candidate.Topology.HasTransitionElements
                || candidate.Assessment.OpticFingerprint != compiled.OpticFingerprint
                || candidate.Assessment.Fields.Length != TopologyEvaluationPlan.Fields(compiled, 21).Length || !candidate.Assessment.Violations.IsEmpty
                || ContentFingerprint.Compute(candidate.Assessment.Targets) != ContentFingerprint.Compute(state.Plan.Targets)
                || ContentFingerprint.Compute(candidate.Assessment.StructuralConstraints) != ContentFingerprint.Compute(state.Plan.Constraints)
                || candidate.Assessment.ProtocolFingerprint != TopologyValidator.GetProtocolFingerprint(compiled, state.Plan.Targets))
                throw new InvalidDataException("SPC candidate pool does not contain original-qualified physical designs.");
        }
        var random = new DeterministicRandom(state.Plan.Options.Seed);
        if (!s.Order.IsEmpty)
        {
            var order = state.Plan.Seeds.Select((_, i) => (i, rank: random.NextUnitDouble())).OrderBy(x => x.rank).Take(policy.MaximumParents).Select(x => x.i);
            if (!s.Order.SequenceEqual(order)) throw new InvalidDataException("SPC parent schedule changed.");
            for (var i = 0; i < s.Order.Length; i++)
            {
                var id = "parent-" + s.Order[i]; var completed = s.Parents.SingleOrDefault(p => p.Id == id);
                var site = i == s.ParentCursor ? s.SiteId : i < s.ParentCursor ? completed?.InterfaceElementId : null;
                if (site is null) continue;
                var original = state.Plan.Seeds[s.Order[i]];
                var allowed = original.Elements.Where(e => !e.Protected && e.Back.Profile.Kind == TopologyProfileKind.Standard && e.Back.Profile.Conic == 0
                    && e.BackZMillimeters > original.Stop.ZMillimeters).ToArray();
                if (allowed.Length == 0 || allowed[random.NextInt32(allowed.Length)].Id != site) throw new InvalidDataException("SPC insertion site or random stream changed.");
            }
        }
        if (state.RandomState != random.State) throw new InvalidDataException("SPC random state does not match its saved schedule.");
        var dimension = s.Variables.Length;
        CheckOptimizer(s.Optimizer, dimension, policy.ParentIterations); CheckOptimizer(s.BranchMinimum, dimension + 2, policy.BranchIterations);
        if (s.Curvature is { } curvature)
        {
            var n = s.Phase == SpcPreparationPhase.ParentCurvature ? dimension : dimension + 2;
            if (curvature.NextColumn < 0 || curvature.NextColumn > n || curvature.Raw.Length != n || curvature.Raw.Any(r => r.Length != n || r.Any(v => !double.IsFinite(v))))
                throw new InvalidDataException("Invalid saved SPC Hessian columns.");
        }
        if (s.Phase is not (SpcPreparationPhase.SelectParent or SpcPreparationPhase.Finished) && (s.SiteId is null || dimension is < 1 or > 30))
            throw new InvalidDataException("Missing SPC parent binding.");
        foreach (var topology in s.Pool.Select(c => c.Topology).Concat(new[] { s.StationaryParent, s.PhysicalSource, s.PhysicalOutput, s.MaterialBase, s.BestPhysical }.OfType<DesignTopology>()))
            CheckPermissions(topology);
        if (s.PhysicalOptions is { } phase && (phase.ComputeMode != DesignComputeMode.Cpu || phase.PerformFinalAssessment
            || phase.MaximumUpdates > policy.PhysicalUpdatesPerStage || phase.MaximumFunctionEvaluations > policy.PhysicalUpdatesPerStage + 1
            || phase.MaximumGradientEvaluations > policy.PhysicalUpdatesPerStage + 1 || phase.TimeLimit != state.Options.TimeLimit))
            throw new InvalidDataException("SPC physical stage changed its original quotas.");
        if (state.Result is { } result && (state.Stage != "Finished" || ContentFingerprint.Compute(result) != ContentFingerprint.Compute(PreparationResult(state))))
            throw new InvalidDataException("SPC result disagrees with continuation.");
        void CheckOptimizer(StationaryContinuation? c, int n, int maximum)
        {
            if (c is null) return;
            if (c.Iterations < 0 || c.Iterations > maximum || c.Coordinates.Length != n || c.Coordinates.Any(v => !double.IsFinite(v))
                || !double.IsFinite(c.Evaluation.Value) || c.Evaluation.Gradient.Length != n || c.Evaluation.Gradient.Any(v => !double.IsFinite(v))
                || c.Inverse.Length != n || c.Inverse.Any(r => r.Length != n || r.Any(v => !double.IsFinite(v)))
                || c.StopReason is not (null or "InteriorStationary" or "IterationLimit" or "LineSearchStalled")) throw new InvalidDataException("Invalid SPC optimizer continuation.");
        }
        void CheckPermissions(DesignTopology topology)
        {
            var original = state.Plan.Seeds.FirstOrDefault(t => t.Elements.All(e => topology.Elements.Any(x => x.Id == e.Id)));
            if (original is null || topology.HasTransitionElements || topology.Elements.Length > original.Elements.Length + 1
                || ContentFingerprint.Compute(topology with { Elements = original.Elements }) != ContentFingerprint.Compute(original))
                throw new InvalidDataException("SPC topology changed the original fixed frame or specification.");
            foreach (var before in original.Elements)
            {
                var after = topology.Elements.Single(e => e.Id == before.Id);
                var normalized = after with { Front = after.Front with { Profile = after.Front.Profile with { CurvaturePerMillimeter = before.Front.Profile.CurvaturePerMillimeter } },
                    Back = after.Back with { Profile = after.Back.Profile with { CurvaturePerMillimeter = before.Back.Profile.CurvaturePerMillimeter } } };
                if (ContentFingerprint.Compute(before) != ContentFingerprint.Compute(normalized)
                    || before.Protected && ContentFingerprint.Compute(before) != ContentFingerprint.Compute(after))
                    throw new InvalidDataException("SPC changed a retained aperture, material, position, protected face or profile permission.");
            }
            _ = TopologyCompiler.Compile(topology, state.Plan.Constraints);
        }
    }
}

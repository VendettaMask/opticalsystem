using System.Collections.Immutable;
using System.Diagnostics;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.InitialStructure.Contracts;

namespace OpticalSystemDesign.InitialStructure.Engine;

/// <summary>Durable cold preparation. Suspension finishes the current atomic operation;
/// an accepted optimizer state is continued without replaying its gradients or RNG draws.</summary>
public static partial class SeedPreparationSearch
{
    private static readonly string KernelIdentity = ContentFingerprint.Compute(new
    {
        Engine = typeof(SeedPreparationSearch).Assembly.ManifestModule.ModuleVersionId,
        Core = typeof(SequentialTraceMeasurement).Assembly.ManifestModule.ModuleVersionId
    });
    private static readonly string SpcKernelIdentity = ContentFingerprint.Compute(new
    {
        KernelIdentity, Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
        OperatingSystem = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
        Architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
        VectorWidth = System.Numerics.Vector<double>.Count,
        Avx2 = System.Runtime.Intrinsics.X86.Avx2.IsSupported, Fma = System.Runtime.Intrinsics.X86.Fma.IsSupported,
        AdvSimd = System.Runtime.Intrinsics.Arm.AdvSimd.IsSupported
    });
    public static bool CanExecute(SeedPreparationCheckpoint state) => state.KernelFingerprint == (state.Mode == "Spc" ? SpcKernelIdentity : KernelIdentity);
    private sealed class Pause : Exception;
    private sealed class FunctionLimit : Exception;
    private sealed record Phase(string Name, int Power, int Functions, double Seconds, int Rings, int Arms, double Reserve);

    public static SeedPreparationCheckpoint Create(string runId, AutonomousSearchPlan plan, bool continueIntoMain = false)
    {
        var options = plan.Options.SeedPreparation ?? throw new ArgumentException("Missing preparation options.");
        ParaxialSeedSearch.ValidateOptions(options);
        _ = AutonomousSearchService.Create("preparation-checkpoint-preflight", plan);
        if (options.ColdStart is { } cold) ColdStartDesignSearch.Validate(plan, options, cold);
        var mode = options.Spc is not null ? "Spc" : options.ColdStart is not null ? "Cold" : options.Retrieval is not null ? "Retrieval" : options.Evolution is not null ? "Evolution" : options.Competition is not null ? "Competition" : "Paraxial";
        return new()
        {
            RunId = runId,
            Plan = plan,
            Options = options,
            PlanFingerprint = ContentFingerprint.Compute(plan),
            ProtocolFingerprint = ContentFingerprint.Compute(new
            {
                Identity = options.Spc is not null ? "seed-preparation-spc-checkpoint-v1" : "seed-preparation-checkpoint-v1",
                plan,
                options,
                SafePoints = options.Spc is not null ? "SPC CPU/original validation/complete BFGS or fixed-topology update/complete central-gradient Hessian column"
                    : "complete construction/original validation/accepted-or-rejected update",
                CheckpointUpdates = options.Spc is not null ? 1 : 8,
                continueIntoMain
            }),
            RandomState = new DeterministicRandom(plan.Options.Seed).State,
            Status = SeedPreparationStatus.Ready,
            Mode = mode,
            Spc = options.Spc is not null ? new() : null,
            Stage = options.Spc is not null ? "Spc:SelectParent" : "Construction",
            ContinueIntoMain = continueIntoMain,
            KernelFingerprint = options.Spc is not null ? SpcKernelIdentity : KernelIdentity
        };
    }

    public static void Validate(SeedPreparationCheckpoint state)
    {
        if (state.Version != 1 || state.Revision < 0 || string.IsNullOrWhiteSpace(state.RunId)
            || state.PlanFingerprint != ContentFingerprint.Compute(state.Plan)
            || state.KernelFingerprint.Length != 64 || state.KernelFingerprint.Any(c => !Uri.IsHexDigit(c))
            || ContentFingerprint.Compute(state.Options) != ContentFingerprint.Compute(state.Plan.Options.SeedPreparation))
            throw new InvalidDataException("Preparation checkpoint identity mismatch.");
        var expected = Create(state.RunId, state.Plan, state.ContinueIntoMain);
        if (expected.Mode != state.Mode) throw new InvalidDataException("Preparation mode does not match its immutable original options.");
        if (state.Mode == "Spc") { SpcBranchSearch.ValidatePreparation(state, expected); return; }
        if (state.Spc is not null) throw new InvalidDataException("SPC state cannot be attached to another preparation route.");
        if (state.Mode != "Cold") { PreparationCheckpointContext.Validate(state, expected); return; }
        if (expected.ProtocolFingerprint != state.ProtocolFingerprint || !Enum.IsDefined(state.Status)
            || state.Cursor < 0 || state.Candidates.IsDefault || state.Phases.IsDefault || state.Variables.IsDefault
            || state.Candidates.Length > state.Options.ColdStart!.BackFocusSlackFractions.Length * state.Options.ColdStart.AttemptsPerFraction
            || !state.Candidates.Select(c => c.Index).SequenceEqual(Enumerable.Range(1, state.Candidates.Length))
            || state.Stage is not ("Construction" or "Compensation" or "Finished")
            || state.Stage == "Construction" && state.Cursor != state.Candidates.Length
            || state.Stage == "Compensation" && state.Cursor > Phases(state.Plan, state.Options.ColdStart).Length
            || state.LostReservations < 0 || state.PersistenceElapsed < TimeSpan.Zero)
            throw new InvalidDataException("Preparation stage, cursor or protocol mismatch.");
        var work = state.Consumed;
        if (!Nonnegative(work) || work.FunctionEvaluations > state.Options.MaximumFunctionEvaluations
            || work.GradientEvaluations > state.Options.ColdStart.MaximumGradientEvaluations || work.TracedRays > state.Options.MaximumTracedRays)
            throw new InvalidDataException("Preparation consumed work is outside the original limits.");
        foreach (var topology in state.Candidates.Where(c => c.Topology is not null).Select(c => c.Topology!)
            .Concat(state.Endpoint is null ? [] : new[] { state.Endpoint }))
            ColdStartDesignSearch.CheckPermissions(topology, ColdStartDesignSearch.ColdPlan(state.Plan), state.Options.Generation!);
        if (state.Pending is { } pending)
        {
            if (!Nonnegative(pending.Reservation) || !Nonnegative(pending.PhaseBase)
                || work.FunctionEvaluations + pending.Reservation.FunctionEvaluations > state.Options.MaximumFunctionEvaluations
                || work.GradientEvaluations + pending.Reservation.GradientEvaluations > state.Options.ColdStart.MaximumGradientEvaluations
                || work.TracedRays + pending.Reservation.TracedRays > state.Options.MaximumTracedRays
                || pending.Variables.IsDefault || pending.Elapsed < TimeSpan.Zero)
                throw new InvalidDataException("Preparation reservation exceeds the original available budget.");
            if (pending.Options is { } options && (options.ComputeMode != DesignComputeMode.Cpu || options.PerformFinalAssessment
                || options.MaximumUpdates > 400 || options.TimeLimit > state.Options.TimeLimit))
                throw new InvalidDataException("Preparation phase changed its original execution permissions.");
        }
        if (state.Result is { } result && (state.Stage != "Finished" || ContentFingerprint.Compute(result.SourcePlan) != state.PlanFingerprint
            || ContentFingerprint.Compute(result.Work) != ContentFingerprint.Compute(state.Consumed)))
            throw new InvalidDataException("Preparation result and checkpoint disagree.");
    }

    public static SeedPreparationCheckpoint Run(SeedPreparationCheckpoint input,
        Action<SeedPreparationCheckpoint> persist, CancellationToken pauseRequested = default,
        Action<int, int>? progress = null)
    {
        Validate(input);
        if (input.Stage == "Finished") return input;
        if (!CanExecute(input)) throw new NotSupportedException("This preparation checkpoint was computed by a different Core/engine build. It remains readable; use its original runtime for exact continuation.");
        if (input.Mode == "Spc") return SpcBranchSearch.RunPreparation(input, persist, pauseRequested, progress);
        if (input.Mode != "Cold") return RunOther(input, persist, pauseRequested, progress);
        var state = input;
        var plan = ColdStartDesignSearch.ColdPlan(state.Plan); var options = state.Options; var policy = options.ColdStart!;
        // An in-flight file has no knowledge of work after its last durable state. Do not replay it for free.
        if (state.Status == SeedPreparationStatus.Running && state.Pending is { InFlight: true } lost)
        {
            state = state with
            {
                Consumed = Add(state.Consumed, lost.Reservation),
                Pending = null,
                Assessment = null,
                LostReservations = state.LostReservations + 1,
                Status = SeedPreparationStatus.Interrupted,
                StopReason = "UnsettledReservationCharged",
                Stage = "Finished"
            };
            state = state with { Result = Result(state) };
            state = state with { Revision = state.Revision + 1 }; persist(state); return state;
        }
        var beforeElapsed = state.Consumed.Elapsed; var beforeRays = state.Consumed.TracedRays;
        if (beforeElapsed >= options.TimeLimit || beforeRays >= options.MaximumTracedRays
            || state.Consumed.FunctionEvaluations >= options.MaximumFunctionEvaluations)
        {
            state = state with
            {
                Stage = "Finished",
                Status = beforeElapsed >= options.TimeLimit ? SeedPreparationStatus.TimeLimit : SeedPreparationStatus.BudgetExhausted,
                StopReason = "PreparationBudget",
                Pending = null
            };
            state = state with { Result = Result(state), Revision = state.Revision + 1 }; persist(state); return state;
        }
        var timer = Stopwatch.StartNew();
        var persistenceElapsed = state.PersistenceElapsed;
        using var deadline = new CancellationTokenSource(options.TimeLimit - beforeElapsed);
        var token = deadline.Token;
        using var cpu = new SequentialGpuSession(DesignComputeMode.Cpu, token);
        using var serial = ComputationParallelism.SuppressNestedParallelism();
        using var rays = SequentialTraceMeasurement.BeginCpuBudget(options.MaximumTracedRays - beforeRays);
        var random = DeterministicRandom.Restore(state.RandomState);
        try
        {
            CheckPause();
            if (StructureSearchDiagnostics.ExternalStopAperturePrecheck(plan.Seeds[0]).ProvenInfeasible)
                return Finish("OriginalExternalStopApertureConflict");
            var descriptor = options.Generation!;
            var margin = Math.Max(.05, plan.Constraints.MinimumAirGapMillimeters);
            var minimum = Math.Max(descriptor.MinimumBackFocusMillimeters, plan.Constraints.MinimumBackFocusMillimeters);
            var maximum = plan.Seeds[0].Image.ZMillimeters - plan.Seeds[0].Stop.ZMillimeters - margin
                - descriptor.Elements.Length * (descriptor.MinimumThicknessMillimeters + margin);
            while (state.Stage == "Construction" && state.Cursor < policy.BackFocusSlackFractions.Length * policy.AttemptsPerFraction)
            {
                Check(); CheckPause();
                var fraction = policy.BackFocusSlackFractions[state.Cursor / policy.AttemptsPerFraction];
                var samplingMinimum = policy.ExploreBackFocus ? minimum + fraction * Math.Max(0, maximum - minimum) : minimum;
                var original = state.Consumed; var started = Elapsed(); var before = rays.RayCount;
                state = state with
                {
                    Pending = new()
                    {
                        Name = "Construction",
                        Source = plan.Seeds[0],
                        Variables = [],
                        PhaseBase = original,
                        InFlight = true,
                        Reservation = Remaining(Math.Min(200 - state.Consumed.FunctionEvaluations,
                        options.MaximumFunctionEvaluations - state.Consumed.FunctionEvaluations), 0)
                    }
                };
                Save(SeedPreparationStatus.Running);
                var work = new StructureGenerationWork(); DesignTopology? proposal = null; TopologyAssessment? measured = null;
                var outcome = "NoFeasibleConstruction";
                try
                {
                    proposal = ThickStructureSeedGenerator.Create(plan, descriptor with
                    {
                        MinimumBackFocusMillimeters = samplingMinimum,
                        AnnealingProposals = Math.Min(16, descriptor.AnnealingProposals)
                    }, ref random,
                        () => { if (state.Consumed.FunctionEvaluations >= 200) throw new FunctionLimit(); Charge(); }, work, token,
                        requireFeasibleGeometry: true, initialShape: StructureCompetitionSearch.InitialShape(StructureRayPathFamily.Balanced, descriptor.Elements.Length));
                    ColdStartDesignSearch.CheckPermissions(proposal, plan, descriptor);
                    measured = Assess(proposal); outcome = measured.Completed ? "OriginalAssessed" : "OriginalAssessmentIncomplete";
                    var loss = measured.Completed && !measured.Fields.IsEmpty ? measured.Fields.Max(f => Math.Max(
                        1 - (double)f.ValidRays / f.AttemptedRays, (double)(f.CheckAttemptedRays - f.CheckValidRays) / f.CheckAttemptedRays)) : double.PositiveInfinity;
                    var quality = !measured.Fields.IsEmpty && measured.Fields.All(f => f.RmsRadiusMillimeters is not null && f.MaximumRadiusMillimeters is not null)
                        ? measured.Fields.Max(f => Math.Max(f.RmsRadiusMillimeters!.Value / plan.Targets.MaximumRmsRadiusMillimeters,
                            f.MaximumRadiusMillimeters!.Value / plan.Targets.MaximumSpotRadiusMillimeters)) : double.PositiveInfinity;
                    if (loss < state.RankingLoss || loss == state.RankingLoss && quality < state.RankingQuality)
                        state = state with { Endpoint = proposal, Assessment = measured, RankingLoss = loss, RankingQuality = quality };
                }
                catch (Exception error) when (TopologyEvaluationPlan.DomainError(error) && !SequentialTraceBudgetExceededException.IsBudgetException(error))
                { outcome = "Rejected:" + error.Message; }
                finally
                {
                    state = state with
                    {
                        Consumed = state.Consumed with
                        {
                            MatrixEvaluations = state.Consumed.MatrixEvaluations + work.MatrixRequests,
                            GeometryProbes = state.Consumed.GeometryProbes + work.GeometryProbes
                        },
                        RandomState = random.State,
                        Candidates = state.Candidates.Add(new(state.Candidates.Length + 1, samplingMinimum, outcome, new()
                        {
                            FunctionEvaluations = state.Consumed.FunctionEvaluations - original.FunctionEvaluations,
                            MatrixEvaluations = work.MatrixRequests,
                            GeometryProbes = work.GeometryProbes + (measured?.GeometryProbes ?? 0),
                            ValidationEvaluations = measured is null ? 0 : 1,
                            TracedRays = rays.RayCount - before,
                            Elapsed = Elapsed() - started
                        }, proposal, measured)),
                        Cursor = state.Cursor + 1,
                        Pending = null
                    };
                }
                Save(SeedPreparationStatus.Ready); progress?.Invoke(state.Candidates.Length, state.Consumed.FunctionEvaluations); CheckPause();
            }
            if (state.Endpoint is null) return Finish("NoEligibleConstruction");
            if (state.Assessment?.MeetsTargets == true) return Finish("Completed");
            if (state.Stage == "Construction")
            {
                state = state with { Stage = "Compensation", Cursor = 0, Variables = StructureEvolutionSearch.EvolutionVariables(state.Endpoint, plan.Options) };
                Save(SeedPreparationStatus.Ready);
            }
            var stages = Phases(plan, policy);
            while (state.Cursor < stages.Length)
            {
                Check(); CheckPause(); var phase = stages[state.Cursor];
                if (state.Pending is null)
                {
                    var topology = state.Endpoint!; var variables = state.Variables;
                    if (phase.Power > 0)
                    {
                        foreach (var face in topology.Elements.Where(e => !e.Protected).SelectMany(e => new[] { (e.FrontSurfaceId, e.Front), (e.BackSurfaceId, e.Back) })
                            .Where(f => f.Item2.Profile.MaximumEvenPower < phase.Power).Select(f => f.Item1).ToArray())
                        {
                            var edit = TopologyOperators.UpgradeProfile(topology, face, phase.Power, plan.Constraints);
                            if (!edit.Succeeded) throw new ArgumentException(edit.Failure!.Message); topology = edit.Topology!;
                        }
                        variables = variables.AddRange(StructureEvolutionSearch.EvolutionVariables(topology, plan.Options)
                            .Where(v => v.Kind == OpticalDesignParameterKind.EvenAsphereCoefficient && !variables.Any(old => old.SurfaceId == v.SurfaceId && old.Kind == v.Kind && old.Power == v.Power)));
                    }
                    if (variables.Length > 64) throw new ArgumentException("Original permitted release exceeds the shared Core 64-variable workspace.");
                    var remaining = options.TimeLimit.TotalSeconds - Elapsed().TotalSeconds - 5 - phase.Reserve;
                    var quota = Math.Min(phase.Functions, options.MaximumFunctionEvaluations - state.Consumed.FunctionEvaluations - 1);
                    var gradients = Math.Min(quota - 1, policy.MaximumGradientEvaluations - state.Consumed.GradientEvaluations);
                    if (remaining <= 0 || quota < 2 || gradients < 1) { state = state with { Cursor = state.Cursor + 1 }; continue; }
                    state = state with
                    {
                        Endpoint = topology,
                        Assessment = null,
                        Variables = variables,
                        Pending = new()
                        {
                            Name = phase.Name,
                            Source = topology,
                            Variables = variables,
                            PhaseBase = CurrentWork(),
                            Options = new()
                            {
                                GateCompensationVersion = policy.Version >= 3 ? 4 : policy.Version == 2 ? 3 : 2,
                                MaximumUpdates = policy.Version >= 6 ? Math.Min(400, quota - 2) : quota - 2,
                                MaximumFunctionEvaluations = quota,
                                MaximumGradientEvaluations = gradients,
                                TimeLimit = TimeSpan.FromSeconds(Math.Min(phase.Seconds, remaining)),
                                ComputeMode = DesignComputeMode.Cpu,
                                PerformFinalAssessment = false,
                                Sampling = new() { FieldsPerDirection = 3, PupilRings = phase.Rings, PupilArms = phase.Arms, ProfileRadialSamples = 64, IncludeBoundaryClearance = true, IncludeBoundaryMaximum = true }
                            }
                        }
                    };
                    Save(SeedPreparationStatus.Ready);
                }
                var pending = state.Pending!;
                if (pending.Name != phase.Name || pending.Options is null) throw new InvalidDataException("Preparation phase cursor mismatch.");
                if (pending.Optimized is null)
                {
                    var entry = state.Consumed; var previous = pending.Continuation?.Work;
                    var compiled = TopologyCompiler.Compile(pending.Source, plan.Constraints);
                    var binding = TopologyParameterBinder.Bind(compiled, pending.Variables, exactMechanicalFrame: true);
                    state = state with
                    {
                        Pending = pending with
                        {
                            InFlight = true,
                            Reservation = Remaining(pending.Options.MaximumFunctionEvaluations - (previous?.FunctionEvaluations ?? 0),
                        pending.Options.MaximumGradientEvaluations - (previous?.GradientEvaluations ?? 0))
                        }
                    };
                    Save(SeedPreparationStatus.Running);
                    var result = FixedTopologyOptimizer.Run(compiled, binding, plan.Targets, pending.Options, token, continuation: pending.Continuation, checkpoint: next =>
                    {
                        // The optimizer owns unsaved accepted states; materialize a second topology only for a durable point or a stop.
                        if (!pauseRequested.IsCancellationRequested && next.Steps.Length % 8 != 0) return false;
                        Apply(next.Work);
                        state = state with { Endpoint = TopologyParameterBinder.Apply(compiled, binding, next.Adam.Coordinates).Topology };
                        var pause = pauseRequested.IsCancellationRequested;
                        state = state with
                        {
                            Pending = pending with
                            {
                                Continuation = next,
                                Elapsed = next.Work.Elapsed,
                                InFlight = !pause,
                                Reservation = Remaining(pending.Options.MaximumFunctionEvaluations - next.Work.FunctionEvaluations,
                                pending.Options.MaximumGradientEvaluations - next.Work.GradientEvaluations)
                            }
                        };
                        if (pause || next.Steps.Length % 8 == 0) Save(pause ? SeedPreparationStatus.Suspended : SeedPreparationStatus.Running);
                        if (!pause && pauseRequested.IsCancellationRequested)
                        { state = state with { Pending = state.Pending! with { InFlight = false } }; Save(SeedPreparationStatus.Suspended); pause = true; }
                        return pause;
                    });
                    Apply(result.Work);
                    if (pauseRequested.IsCancellationRequested && result.Termination == FixedTopologyTermination.Cancelled)
                    {
                        state = state with
                        {
                            Pending = pending with
                            {
                                Continuation = result.Continuation,
                                Elapsed = result.Work.Elapsed,
                                InFlight = false,
                                Reservation = Remaining(pending.Options.MaximumFunctionEvaluations - result.Work.FunctionEvaluations,
                                pending.Options.MaximumGradientEvaluations - result.Work.GradientEvaluations)
                            }
                        };
                        Save(SeedPreparationStatus.Suspended); return state;
                    }
                    state = state with { Endpoint = result.Topology, Pending = pending with { Optimized = result, Continuation = null, InFlight = false, Elapsed = result.Work.Elapsed, Reservation = new() } };
                    Save(SeedPreparationStatus.Ready);
                    void Apply(FixedTopologyWork current)
                    {
                        state = state with
                        {
                            Consumed = entry with
                            {
                                FunctionEvaluations = entry.FunctionEvaluations + current.FunctionEvaluations - (previous?.FunctionEvaluations ?? 0),
                                GradientEvaluations = entry.GradientEvaluations + current.GradientEvaluations - (previous?.GradientEvaluations ?? 0),
                                GeometryProbes = entry.GeometryProbes + current.GeometryProbes - (previous?.GeometryProbes ?? 0)
                            }
                        };
                    }
                }
                CheckPause(); pending = state.Pending!;
                if (!token.IsCancellationRequested && state.Consumed.FunctionEvaluations < options.MaximumFunctionEvaluations)
                {
                    state = state with { Pending = pending with { InFlight = true, Reservation = Remaining(1, 0) } }; Save(SeedPreparationStatus.Running);
                    var assessment = Assess(state.Endpoint!); state = state with { Assessment = assessment };
                }
                var optimized = pending.Optimized!;
                state = state with
                {
                    Phases = state.Phases.Add(new(phase.Name, optimized.Termination.ToString(), Subtract(CurrentWork(), pending.PhaseBase),
                    pending.Variables, state.Endpoint!, state.Assessment, optimized.Diagnostics)),
                    Cursor = state.Cursor + 1,
                    Pending = null
                };
                Save(SeedPreparationStatus.Ready); progress?.Invoke(state.Candidates.Length + state.Phases.Length, state.Consumed.FunctionEvaluations); CheckPause();
                if (state.Assessment?.MeetsTargets == true) return Finish("Completed");
            }
            return Finish("NoQualifiedEndpointWithinBudget");
        }
        catch (Pause) { return state; }
        catch (FunctionLimit) { return Finish("FunctionBudget", SeedPreparationStatus.BudgetExhausted); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return Finish("TimeLimit", SeedPreparationStatus.TimeLimit); }
        catch (Exception error) when (SequentialTraceBudgetExceededException.IsBudgetException(error)) { return Finish("RayBudget", SeedPreparationStatus.BudgetExhausted); }
        catch (Exception error) when (TopologyEvaluationPlan.DomainError(error)) { return Finish("Domain:" + error.Message); }

        TimeSpan Elapsed() => beforeElapsed + timer.Elapsed;
        AutonomousSearchWork CurrentWork() => state.Consumed with { Elapsed = Elapsed(), TracedRays = beforeRays + rays.RayCount };
        AutonomousSearchWork Remaining(int functions, int gradients) => new()
        {
            FunctionEvaluations = Math.Max(0, Math.Min(functions, options.MaximumFunctionEvaluations - state.Consumed.FunctionEvaluations)),
            GradientEvaluations = Math.Max(0, Math.Min(gradients, policy.MaximumGradientEvaluations - state.Consumed.GradientEvaluations)),
            TracedRays = Math.Max(0, options.MaximumTracedRays - beforeRays - rays.RayCount),
            Elapsed = options.TimeLimit > Elapsed() ? options.TimeLimit - Elapsed() : TimeSpan.Zero
        };
        void Check() { token.ThrowIfCancellationRequested(); if (state.Consumed.FunctionEvaluations >= options.MaximumFunctionEvaluations) throw new FunctionLimit(); }
        void Charge() { Check(); state = state with { Consumed = state.Consumed with { FunctionEvaluations = state.Consumed.FunctionEvaluations + 1 } }; }
        TopologyAssessment Assess(DesignTopology topology)
        {
            Charge(); state = state with { Consumed = state.Consumed with { ValidationEvaluations = state.Consumed.ValidationEvaluations + 1 } };
            var measured = TopologyValidator.Validate(TopologyCompiler.Compile(topology, plan.Constraints), plan.Targets, token);
            state = state with { Consumed = state.Consumed with { GeometryProbes = state.Consumed.GeometryProbes + measured.GeometryProbes } }; return measured;
        }
        void CheckPause()
        { if (pauseRequested.IsCancellationRequested) { if (state.Pending is { } p) state = state with { Pending = p with { InFlight = false } }; Save(SeedPreparationStatus.Suspended); throw new Pause(); } }
        void Save(SeedPreparationStatus status)
        {
            timer.Stop(); deadline.CancelAfter(Timeout.InfiniteTimeSpan);
            state = state with { Consumed = CurrentWork(), RandomState = random.State, Status = status, Revision = state.Revision + 1, PersistenceElapsed = persistenceElapsed };
            var saving = Stopwatch.StartNew();
            try { persist(state); }
            finally { persistenceElapsed += saving.Elapsed; timer.Start(); var left = options.TimeLimit - Elapsed(); deadline.CancelAfter(left > TimeSpan.Zero ? left : TimeSpan.Zero); }
        }
        SeedPreparationCheckpoint Finish(string reason, SeedPreparationStatus status = SeedPreparationStatus.Completed)
        {
            timer.Stop(); state = state with { Consumed = CurrentWork(), Stage = "Finished", Status = status, StopReason = reason, Pending = null, RandomState = random.State };
            state = state with { Result = Result(state) }; Save(status); return state;
        }
    }

    public static ParaxialSeedSearchResult Result(SeedPreparationCheckpoint state)
    {
        if (state.Mode == "Spc") return SpcBranchSearch.PreparationResult(state);
        if (state.Mode != "Cold") return state.Result ?? new()
        {
            Algorithm = "initial-structure-" + state.Mode.ToLowerInvariant() + "-preparation-checkpoint-v1",
            ProtocolFingerprint = state.ProtocolFingerprint,
            SourcePlan = state.Plan,
            Options = state.Options,
            Work = state.Consumed,
            Termination = state.Status == SeedPreparationStatus.Suspended ? ParaxialSeedTermination.Cancelled : state.Status == SeedPreparationStatus.TimeLimit ?
                ParaxialSeedTermination.TimeLimit : state.Status is SeedPreparationStatus.Interrupted or SeedPreparationStatus.BudgetExhausted ? ParaxialSeedTermination.BudgetExhausted : ParaxialSeedTermination.Completed
        };
        var plan = ColdStartDesignSearch.ColdPlan(state.Plan); var options = state.Options; var policy = options.ColdStart!;
        var cold = new ColdStartDesignResult
        {
            ProtocolFingerprint = ContentFingerprint.Compute(new
            {
                Identity = "cold-back-focus-design-v" + policy.Version,
                plan,
                preparation = options,
                policy,
                Final = "original-TopologyValidator-v2",
                Coordinates = "anchored-on-generated-layout; append-original-permitted-terms"
            }),
            ColdPlan = plan,
            Preparation = options,
            Policy = policy,
            StopReason = state.StopReason ?? state.Status.ToString(),
            Work = state.Consumed,
            Construction = state.Candidates,
            Phases = state.Phases,
            Endpoint = state.Endpoint,
            Assessment = state.Assessment
        };
        return new()
        {
            Algorithm = "initial-structure-cold-design-v" + policy.Version,
            ProtocolFingerprint = cold.ProtocolFingerprint,
            SourcePlan = state.Plan,
            Options = options,
            ColdStart = cold,
            Work = state.Consumed,
            MatrixRequests = state.Consumed.MatrixEvaluations,
            Termination = state.Status == SeedPreparationStatus.Suspended ? ParaxialSeedTermination.Cancelled :
            state.Status == SeedPreparationStatus.TimeLimit ? ParaxialSeedTermination.TimeLimit : state.Status is SeedPreparationStatus.BudgetExhausted or SeedPreparationStatus.Interrupted ? ParaxialSeedTermination.BudgetExhausted : ParaxialSeedTermination.Completed,
            Seeds = state.Status == SeedPreparationStatus.Completed && cold.Qualified && state.Consumed.Elapsed <= options.TimeLimit ? [cold.Endpoint!] : [],
            Attempts = state.Candidates.Select(c => new ParaxialSeedAttempt(c.Index, ContentFingerprint.Compute(plan), c.Outcome, c.Work.FunctionEvaluations, null, c.Topology)).ToImmutableArray()
        };
    }

    private static ImmutableArray<Phase> Phases(AutonomousSearchPlan plan, ColdStartDesignOptions policy)
    {
        var four = plan.Options.Expansion is { EnableProfile: true, MaximumEvenPower: >= 4 }; var six = plan.Options.Expansion is { EnableProfile: true, MaximumEvenPower: >= 6 };
        var phases = ImmutableArray.CreateBuilder<Phase>();
        phases.Add(new("SphereSparse", 0, 102, policy.Version >= 2 ? 60 : 90, 2, 8, policy.Version >= 2 ? four ? 360 : 0 : 180));
        if (policy.Version == 1 || !four) phases.Add(new("SphereDense", 0, policy.Version == 1 ? 82 : 600, policy.Version == 1 ? 180 : 600, 10, 32, policy.Version == 1 ? 150 : 0));
        if (four) phases.Add(new(policy.Version >= 4 && six ? "Even4Sparse" : "Even4", 4, policy.Version >= 4 && six ? 102 : policy.Version >= 2 ? six ? 82 : 600 : 200,
            policy.Version >= 4 && six ? 60 : policy.Version >= 2 ? six ? 150 : 600 : 300, policy.Version >= 4 && six ? 2 : 10, policy.Version >= 4 && six ? 8 : 32, policy.Version >= 4 && six ? 420 : policy.Version >= 2 ? six ? 240 : 0 : 75));
        if (six) phases.Add(new("Even6", 6, policy.Version >= 2 ? 600 : 120, policy.Version >= 2 ? 600 : 270, 10, 32, 0)); return phases.ToImmutable();
    }
    internal static bool Nonnegative(AutonomousSearchWork w) => w.FunctionEvaluations >= 0 && w.GradientEvaluations >= 0 && w.ValidationEvaluations >= 0 && w.MatrixEvaluations >= 0 && w.GeometryProbes >= 0 && w.TracedRays >= 0 && w.Elapsed >= TimeSpan.Zero && w.DifferenceEvaluations >= 0 && w.JacobianBuilds >= 0;
    internal static AutonomousSearchWork Add(AutonomousSearchWork a, AutonomousSearchWork b) => new()
    {
        FunctionEvaluations = checked(a.FunctionEvaluations + b.FunctionEvaluations),
        GradientEvaluations = checked(a.GradientEvaluations + b.GradientEvaluations),
        ValidationEvaluations = checked(a.ValidationEvaluations + b.ValidationEvaluations),
        MatrixEvaluations = checked(a.MatrixEvaluations + b.MatrixEvaluations),
        GeometryProbes = checked(a.GeometryProbes + b.GeometryProbes),
        TracedRays = checked(a.TracedRays + b.TracedRays),
        DifferenceEvaluations = checked(a.DifferenceEvaluations + b.DifferenceEvaluations),
        JacobianBuilds = checked(a.JacobianBuilds + b.JacobianBuilds),
        Elapsed = a.Elapsed + b.Elapsed
    };
    private static AutonomousSearchWork Subtract(AutonomousSearchWork a, AutonomousSearchWork b) => new()
    {
        FunctionEvaluations = a.FunctionEvaluations - b.FunctionEvaluations,
        GradientEvaluations = a.GradientEvaluations - b.GradientEvaluations,
        ValidationEvaluations = a.ValidationEvaluations - b.ValidationEvaluations,
        MatrixEvaluations = a.MatrixEvaluations - b.MatrixEvaluations,
        GeometryProbes = a.GeometryProbes - b.GeometryProbes,
        TracedRays = a.TracedRays - b.TracedRays,
        Elapsed = a.Elapsed - b.Elapsed
    };
}

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;

namespace OpticalSystemDesign.InitialStructure.Engine;

/// <summary>Typed stage snapshots around whole, bounded legacy preparation operations.
/// Nested competitions belong to their parent operation and cannot steal its checkpoint context.</summary>
internal sealed class PreparationCheckpointContext : IDisposable
{
    private static readonly AsyncLocal<PreparationCheckpointContext?> Current = new();
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        IncludeFields = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
    private readonly PreparationCheckpointContext? _previous = Current.Value;
    private readonly Action<SeedPreparationCheckpoint> _persist;
    private readonly CancellationToken _pause;
    private TimeSpan _persistence;
    private CancellationTokenSource? _deadline;
    private PreparationClock? _clock;
    private PreparationRays? _rays;
    internal SeedPreparationCheckpoint State { get; private set; }
    internal TimeSpan RemainingTime => State.Options.TimeLimit - State.Consumed.Elapsed > TimeSpan.Zero ? State.Options.TimeLimit - State.Consumed.Elapsed : TimeSpan.Zero;
    internal PreparationCheckpointContext(SeedPreparationCheckpoint state, Action<SeedPreparationCheckpoint> persist, CancellationToken pause)
    { State = state; _persist = persist; _pause = pause; _persistence = state.PersistenceElapsed; Current.Value = this; }
    internal static PreparationCheckpointContext? For(string mode, AutonomousSearchPlan plan) => Current.Value is { } frame && frame.State.Mode == mode
        && frame.State.PlanFingerprint == ContentFingerprint.Compute(plan) ? frame : null;
    internal T? Load<T>() where T : class => State.EngineState is { } data ? data.Deserialize<T>(Json) ?? throw new InvalidDataException("Empty preparation state.") : null;
    internal PreparationClock Clock() => _clock = new(State.Consumed.Elapsed);
    internal PreparationRays Rays(long maximum) => _rays = new(maximum, State.Consumed.TracedRays);
    internal void Attach(CancellationTokenSource deadline) => _deadline = deadline;
    internal void Reserve(string name, int functions, int gradients = 0)
    {
        CheckPause();
        State = State with
        {
            Pending = new()
            {
                Name = name,
                Source = State.Plan.Seeds[0],
                Variables = [],
                InFlight = true,
                PhaseBase = State.Consumed,
                Reservation = new()
                {
                    FunctionEvaluations = Math.Max(0, Math.Min(functions, State.Options.MaximumFunctionEvaluations - State.Consumed.FunctionEvaluations)),
                    GradientEvaluations = Math.Max(0, Math.Min(gradients, (State.Options.Competition?.MaximumGradientEvaluations ?? 0) - State.Consumed.GradientEvaluations)),
                    TracedRays = Math.Max(0, State.Options.MaximumTracedRays - (_rays?.RayCount ?? State.Consumed.TracedRays)),
                    Elapsed = TimeSpan.FromTicks(Math.Max(0, (State.Options.TimeLimit - (_clock?.Elapsed ?? State.Consumed.Elapsed)).Ticks))
                }
            }
        };
        Save(SeedPreparationStatus.Running);
    }
    internal void Commit<T>(T snapshot, string stage, int cursor, ulong random, AutonomousSearchWork consumed)
    {
        State = State with
        {
            EngineState = JsonSerializer.SerializeToElement(snapshot, Json),
            Stage = stage,
            Cursor = cursor,
            RandomState = random,
            Consumed = consumed,
            Pending = null
        };
        Save(_pause.IsCancellationRequested ? SeedPreparationStatus.Suspended : SeedPreparationStatus.Ready); CheckPause();
    }
    internal void CheckPause()
    { if (!_pause.IsCancellationRequested) return; State = State with { Pending = null }; Save(SeedPreparationStatus.Suspended); throw new PreparationPause(); }
    internal SeedPreparationCheckpoint Finish(ParaxialSeedSearchResult result)
    {
        // The engine owns these scopes and has disposed them before returning its result.
        _deadline = null; _rays = null;
        _clock?.Stop(); var work = result.Work with { Elapsed = _clock?.Elapsed ?? result.Work.Elapsed };
        result = result with { Work = work };
        State = State with
        {
            Result = result,
            Consumed = work,
            Stage = "Finished",
            Pending = null,
            Status = result.Termination == ParaxialSeedTermination.TimeLimit ? SeedPreparationStatus.TimeLimit :
                result.Termination == ParaxialSeedTermination.BudgetExhausted ? SeedPreparationStatus.BudgetExhausted : SeedPreparationStatus.Completed,
            StopReason = result.Termination.ToString()
        }; Save(State.Status); return State;
    }
    private void Save(SeedPreparationStatus status)
    {
        _clock?.Stop(); _deadline?.CancelAfter(Timeout.InfiniteTimeSpan);
        var work = State.Consumed with { Elapsed = _clock?.Elapsed ?? State.Consumed.Elapsed, TracedRays = _rays?.RayCount ?? State.Consumed.TracedRays };
        State = State with { Status = status, Revision = State.Revision + 1, Consumed = work, PersistenceElapsed = _persistence };
        if (State.Result is { } result) State = State with { Result = result with { Work = work } };
        var timer = Stopwatch.StartNew();
        try { _persist(State); }
        finally { _persistence += timer.Elapsed; _clock?.Start(); var left = State.Options.TimeLimit - (_clock?.Elapsed ?? State.Consumed.Elapsed); _deadline?.CancelAfter(left > TimeSpan.Zero ? left : TimeSpan.Zero); }
    }
    internal static void Validate(SeedPreparationCheckpoint state, SeedPreparationCheckpoint expected)
    {
        if (state.ProtocolFingerprint != expected.ProtocolFingerprint || !Enum.IsDefined(state.Status) || !SeedPreparationSearch.Nonnegative(state.Consumed)
            || state.Cursor < 0 || state.Stage.Length > 128 || state.Consumed.FunctionEvaluations > state.Options.MaximumFunctionEvaluations
                || state.Consumed.TracedRays > state.Options.MaximumTracedRays || state.LostReservations < 0
                || state.Consumed.GradientEvaluations > (state.Options.Competition?.MaximumGradientEvaluations ?? 0))
            throw new InvalidDataException("Invalid preparation stage or consumed budget.");
        if (state.Pending is { } pending && (!SeedPreparationSearch.Nonnegative(pending.Reservation)
            || pending.Reservation.FunctionEvaluations + state.Consumed.FunctionEvaluations > state.Options.MaximumFunctionEvaluations
                || pending.Reservation.TracedRays + state.Consumed.TracedRays > state.Options.MaximumTracedRays
                || pending.Reservation.GradientEvaluations + state.Consumed.GradientEvaluations > (state.Options.Competition?.MaximumGradientEvaluations ?? 0)))
            throw new InvalidDataException("Invalid preparation reservation.");
        if (state.Result is { } result && (state.Stage != "Finished" || ContentFingerprint.Compute(result.SourcePlan) != state.PlanFingerprint
            || ContentFingerprint.Compute(result.Work) != ContentFingerprint.Compute(state.Consumed))) throw new InvalidDataException("Preparation result mismatch.");
    }
    public void Dispose() => Current.Value = _previous;
}

internal sealed class PreparationPause : Exception;
internal sealed class PreparationClock(TimeSpan before = default)
{
    private readonly Stopwatch _timer = Stopwatch.StartNew();
    internal TimeSpan Elapsed => before + _timer.Elapsed;
    internal void Stop() => _timer.Stop();
    internal void Start() => _timer.Start();
}
internal sealed class PreparationRays : IDisposable
{
    private readonly SequentialTraceMeasurement _measurement;
    private readonly long _before;
    internal PreparationRays(long maximum, long before = 0) { _before = before; _measurement = SequentialTraceMeasurement.BeginCpuBudget(Math.Max(0, maximum - before)); }
    internal long RayCount => _before + _measurement.RayCount;
    internal bool LimitReached => _measurement.LimitReached;
    public void Dispose() => _measurement.Dispose();
}

public static partial class SeedPreparationSearch
{
    private static SeedPreparationCheckpoint RunOther(SeedPreparationCheckpoint state, Action<SeedPreparationCheckpoint> persist, CancellationToken pause, Action<int, int>? progress)
    {
        if (state.Status == SeedPreparationStatus.Running && state.Pending is { InFlight: true } pending)
        {
            state = state with
            {
                Consumed = Add(state.Consumed, pending.Reservation),
                Pending = null,
                Stage = "Finished",
                Status = SeedPreparationStatus.Interrupted,
                LostReservations = state.LostReservations + 1,
                StopReason = "UnsettledReservationCharged",
                Revision = state.Revision + 1
            };
            state = state with { Result = Result(state) }; persist(state); return state;
        }
        using var frame = new PreparationCheckpointContext(state, persist, pause);
        try { frame.CheckPause(); return frame.Finish(ParaxialSeedSearch.Run(state.Plan, state.Options, progress: progress)); }
        catch (PreparationPause) { return frame.State; }
    }
}

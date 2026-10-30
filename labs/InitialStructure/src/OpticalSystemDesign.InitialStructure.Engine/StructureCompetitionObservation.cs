namespace OpticalSystemDesign.InitialStructure.Engine;

/// <summary>Scoped benchmark observer; never enters persisted identities or search decisions.</summary>
internal static class StructureCompetitionObservation
{
    private static readonly AsyncLocal<Action<TimeSpan>?> Qualified = new();
    internal static IDisposable ObserveFirstQualified(Action<TimeSpan> observer)
    {
        var previous = Qualified.Value; Qualified.Value = observer;
        return new Restore(() => Qualified.Value = previous);
    }
    internal static void RecordQualified(TimeSpan elapsed) => Qualified.Value?.Invoke(elapsed);
    private sealed class Restore(Action restore) : IDisposable { public void Dispose() => restore(); }
}

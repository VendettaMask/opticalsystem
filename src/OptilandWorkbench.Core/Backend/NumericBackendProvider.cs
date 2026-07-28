namespace OptilandWorkbench.Core.Backend;

public sealed class NumericBackendProvider
{
    private readonly Dictionary<string, INumericBackend> _backends = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IBatchedNumericBackend> _batchedBackends =
        new(StringComparer.OrdinalIgnoreCase);

    public NumericBackendProvider()
    {
        Register(new ManagedCpuBackend());
        Current = _backends["managed-cpu"];
    }

    public INumericBackend Current { get; private set; }

    public IReadOnlyCollection<string> Names => _backends.Keys.ToArray();
    public IBatchedNumericBackend CurrentBatched => _batchedBackends[Current.Name];

    public void Register(INumericBackend backend)
    {
        _batchedBackends[backend.Name] = backend as IBatchedNumericBackend
            ?? new ScalarBatchedNumericBackendAdapter(backend);
        _backends[backend.Name] = backend;
    }

    public void SetBackend(string name)
    {
        if (!_backends.TryGetValue(name, out var backend))
        {
            throw new ArgumentException($"Backend '{name}' is not registered.", nameof(name));
        }

        Current = backend;
    }

    internal NumericBackendProvider Clone()
    {
        var clone = new NumericBackendProvider();
        foreach (var backend in _backends.Values)
        {
            clone.Register(backend);
        }

        clone.SetBackend(Current.Name);
        return clone;
    }
}

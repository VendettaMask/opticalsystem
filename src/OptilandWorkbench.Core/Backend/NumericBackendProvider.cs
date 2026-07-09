namespace OptilandWorkbench.Core.Backend;

public sealed class NumericBackendProvider
{
    private readonly Dictionary<string, INumericBackend> _backends = new(StringComparer.OrdinalIgnoreCase);

    public NumericBackendProvider()
    {
        Register(new ManagedCpuBackend());
        Current = _backends["managed-cpu"];
    }

    public INumericBackend Current { get; private set; }

    public IReadOnlyCollection<string> Names => _backends.Keys.ToArray();

    public void Register(INumericBackend backend)
    {
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
}

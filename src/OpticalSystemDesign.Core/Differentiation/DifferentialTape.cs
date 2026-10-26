namespace OpticalSystemDesign.Core.Differentiation;

internal sealed class GpuTapeRangeException(string message) : NotSupportedException(message);

// Only scalar chain-rule instructions cross the GPU boundary. All optical formulas,
// intersection roots, spatial normals and stop constraints still come from Core.
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public struct GradientTapeNode
{
    public int Left, Right, Parameter;
    public double LeftWeight, RightWeight;
}

internal sealed class DifferentialTape : IDisposable
{
    private static readonly System.Collections.Concurrent.ConcurrentBag<DifferentialTape> Pool = [];
    private static int _pooledCount;
    [ThreadStatic] private static DifferentialTape? _current;
    public static DifferentialTape? Current => _current;
    private DifferentialTape? _previous;
    private readonly List<GradientTapeNode> _nodes = new(2048);
    private readonly Dictionary<double[], int> _imports = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<(int Parameter, double Scale), int> _seeds = [];
    private bool _rented, _returned;
    public int ParameterCount { get; private set; }
    public int Dimension => ParameterCount + 2;
    public IReadOnlyList<GradientTapeNode> Nodes => _nodes;

    public DifferentialTape(int parameters)
    {
        if (parameters is < 1 or > 512) throw new GpuTapeRangeException("GPU parameter count must be between 1 and 512.");
        ParameterCount = parameters;
        _previous = _current;
        _current = this;
    }

    // The scope ends before its pending CUDA batch consumes the recorded nodes.
    // The batch releases storage separately after consuming or abandoning the ray.
    internal static DifferentialTape Rent(int parameters)
    {
        if (parameters is < 1 or > 512) throw new GpuTapeRangeException("GPU parameter count must be between 1 and 512.");
        DifferentialTape tape;
        if (Pool.TryTake(out var reused))
        {
            Interlocked.Decrement(ref _pooledCount);
            tape = reused;
            tape.ParameterCount = parameters;
            tape._previous = _current;
            _current = tape;
        }
        else tape = new(parameters);
        tape._rented = true;
        tape._returned = false;
        return tape;
    }

    internal void Release()
    {
        if (!_rented || _returned) return;
        if (ReferenceEquals(_current, this)) throw new InvalidOperationException("An active ray scope cannot be returned to the pool.");
        _returned = true;
        _previous = null;
        _nodes.Clear();
        _imports.Clear();
        _seeds.Clear();
        // At most 32 MiB of ordinary graph-node storage is retained.
        if (_nodes.Capacity > 8192) return;
        if (Interlocked.Increment(ref _pooledCount) <= 128) Pool.Add(this);
        else Interlocked.Decrement(ref _pooledCount);
    }

    public DifferentialScalar Variable(double value, int dimension, int index, double scale)
    {
        if (dimension != Dimension) throw new InvalidOperationException("The ray tape has a different tangent dimension.");
        var node = index < ParameterCount ? Seed(index, scale) : -1;
        return new(value, this, node, index == ParameterCount ? scale : 0,
            index == ParameterCount + 1 ? scale : 0, index < ParameterCount ? Math.Abs(scale) : 0);
    }

    public int Combine(int left, double leftWeight, int right, double rightWeight)
    {
        if (leftWeight == 0) left = -1;
        if (rightWeight == 0) right = -1;
        if (left < 0 && right < 0) return -1;
        if (_nodes.Count >= 262144) throw new GpuTapeRangeException("A single ray exceeds the bounded GPU graph capacity.");
        _nodes.Add(new() { Left = left, Right = right, Parameter = -1, LeftWeight = leftWeight, RightWeight = rightWeight });
        return _nodes.Count - 1;
    }

    private int Seed(int parameter, double scale)
    {
        if (scale == 0) return -1;
        if (_seeds.TryGetValue((parameter, scale), out var existing)) return existing;
        if (_nodes.Count >= 262144) throw new GpuTapeRangeException("A single ray exceeds the bounded GPU graph capacity.");
        _nodes.Add(new() { Left = -1, Right = -1, Parameter = parameter, LeftWeight = scale });
        var result = _nodes.Count - 1;
        _seeds.Add((parameter, scale), result);
        return result;
    }

    public int Import(double[]? gradient)
    {
        if (gradient is null) return -1;
        if (_imports.TryGetValue(gradient, out var cached)) return cached;
        if (gradient.Length != Dimension) throw new InvalidOperationException("The imported tangent dimension does not match the ray tape.");
        var result = -1;
        for (var index = 0; index < ParameterCount; index++)
            if (gradient[index] != 0) result = Combine(result, 1, Seed(index, gradient[index]), 1);
        _imports.Add(gradient, result);
        return result;
    }

    public void Dispose()
    {
        if (!ReferenceEquals(_current, this)) throw new InvalidOperationException("Ray tape scopes must be disposed in order.");
        _current = _previous;
    }
}

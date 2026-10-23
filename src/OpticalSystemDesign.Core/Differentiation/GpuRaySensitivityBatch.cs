using OpticalSystemDesign.Core.Raytrace;

namespace OpticalSystemDesign.Core.Differentiation;

internal sealed class GpuRaySensitivityBatch(int parameters, CancellationToken cancellationToken)
{
    private readonly List<(DifferentialTape Tape, DesignRaySensitivity Ray, Action<DesignRaySensitivity> Complete)> _pending = [];
    private int _nodes;
    public int Batches { get; private set; }
    public long Rays { get; private set; }
    public double Milliseconds { get; private set; }

    public void Add(DifferentialTape tape, DesignRaySensitivity ray, Action<DesignRaySensitivity> complete)
    {
        if (_nodes + tape.Nodes.Count > 1_750_000) Flush();
        _pending.Add((tape, ray, complete));
        _nodes += tape.Nodes.Count;
        if (_pending.Count >= 128) Flush();
    }

    public void Flush()
    {
        if (_pending.Count == 0) return;
        var outputs = _pending.Select(item => Values(item.Ray).Select(value => value.TapeNode(item.Tape)).ToArray()).ToArray();
        var result = DesignGpu.Differentiate(_pending.Select(item => item.Tape).ToArray(), outputs, parameters, cancellationToken);
        for (var ray = 0; ray < _pending.Count; ray++)
        {
            var values = Values(_pending[ray].Ray);
            var materialized = new DifferentialScalar[5];
            for (var component = 0; component < 5; component++)
            {
                var gradient = new double[parameters];
                Array.Copy(result.Gradients, (ray * 5 + component) * parameters, gradient, 0, parameters);
                materialized[component] = DifferentialScalar.FromGradient(values[component].Value, gradient);
            }
            _pending[ray].Complete(new(materialized[0], materialized[1], materialized[2], materialized[3], materialized[4]));
        }
        Batches++;
        Rays += _pending.Count;
        Milliseconds += result.Milliseconds;
        _pending.Clear();
        _nodes = 0;
    }

    private static DifferentialScalar[] Values(DesignRaySensitivity ray) =>
        [ray.X, ray.Y, ray.DirectionZ, ray.MinimumClearance, ray.MinimumBendCosine];
}

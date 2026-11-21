namespace OpticalSystemDesign.Core.NonSequential;

/// <summary>Eight-dimensional Sobol emission samples, independent of subsequent branch traversal.</summary>
internal sealed class NonSequentialSobolSource : Random
{
    private static readonly uint[][] Directions = BuildDirections();
    private readonly uint[] _shifts = new uint[8];
    private uint _gray;
    private int _dimension;
    internal NonSequentialSobolSource(int seed)
    {
        if (seed == 1) return;
        var scramble = new Random(seed);
        for (var i = 0; i < _shifts.Length; i++) _shifts[i] = (uint)scramble.NextInt64(1L << 32);
    }
    internal void BeginRay(int index) { var n = (uint)index + 1; _gray = n ^ (n >> 1); _dimension = 0; }
    public override double NextDouble()
    {
        if (_dimension >= Directions.Length) throw new NotSupportedException("此光源发射分布需要超过8个Sobol维度。");
        var value = _shifts[_dimension]; var bits = _gray; var bit = 0;
        while (bits != 0) { if ((bits & 1) != 0) value ^= Directions[_dimension][bit]; bit++; bits >>= 1; }
        _dimension++; return value / 4294967296d;
    }
    private static uint[][] BuildDirections()
    {
        var seeds = new[] { (0, 0, Array.Empty<int>()), (1, 0, new[] { 1 }), (2, 1, new[] { 1, 3 }),
            (3, 1, new[] { 1, 3, 1 }), (3, 2, new[] { 1, 1, 1 }), (4, 1, new[] { 1, 1, 3, 3 }),
            (4, 4, new[] { 1, 3, 5, 13 }), (5, 2, new[] { 1, 1, 5, 5, 17 }) };
        var result = new uint[seeds.Length][];
        for (var d = 0; d < seeds.Length; d++)
        {
            var (degree, polynomial, initial) = seeds[d]; var v = result[d] = new uint[32];
            for (var j = 0; j < 32; j++)
            {
                if (d == 0) v[j] = 1u << (31 - j);
                else if (j < degree) v[j] = (uint)initial[j] << (31 - j);
                else
                {
                    v[j] = v[j - degree] ^ (v[j - degree] >> degree);
                    for (var k = 1; k < degree; k++) if (((polynomial >> (degree - 1 - k)) & 1) != 0) v[j] ^= v[j - k];
                }
            }
        }
        return result;
    }
}

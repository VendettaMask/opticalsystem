namespace OpticalSystemDesign.InitialStructure.Engine.Optimization;

/// <summary>MT19937 input streams used by Python random, NumPy RandomState and torch CPU startup.
/// This reproduces starting prescriptions without importing those runtimes.</summary>
internal sealed class AutoLensInitializationRandom
{
    private readonly Twister _python;
    private readonly Twister _numpy;
    private readonly Twister _torch;
    public AutoLensInitializationRandom(uint seed)
    {
        _python = new(seed, python: true);
        _numpy = new(seed);
        _torch = new(seed);
        // main() creates a four-character result tag before create_lens().
        for (var index = 0; index < 4; index++) Below(62);
    }
    public int Below(int count)
    {
        var bits = 32 - int.LeadingZeroCount(count);
        uint value;
        do { value = _python.Next() >> (32 - bits); } while (value >= count);
        return (int)value;
    }
    public double Gap(bool glass)
    {
        var unit = (_torch.Next() & 0xffffff) * (1f / 16777216f);
        return (float)(unit + (glass ? 1f : .5f));
    }
    public double Curvature(bool glass)
    {
        var a = _numpy.Next() >> 5;
        var b = _numpy.Next() >> 6;
        return (float)((a * 67108864.0 + b) / 9007199254740992.0 * .001 * (glass ? 1 : -1));
    }
    public static double NormalizePosition(double value, double actualLength, double targetLength) =>
        (float)((float)((float)value / (float)actualLength) * (float)targetLength);

    private sealed class Twister
    {
        private readonly uint[] _state = new uint[624];
        private int _index = 624;
        public Twister(uint seed, bool python = false)
        {
            Initialize(python ? 19650218 : seed);
            if (!python) return;
            var i = 1;
            for (var k = 624; k > 0; k--)
            {
                _state[i] = unchecked((_state[i] ^ ((_state[i - 1] ^ (_state[i - 1] >> 30)) * 1664525)) + seed);
                if (++i >= 624) { _state[0] = _state[623]; i = 1; }
            }
            for (var k = 623; k > 0; k--)
            {
                _state[i] = unchecked((_state[i] ^ ((_state[i - 1] ^ (_state[i - 1] >> 30)) * 1566083941)) - (uint)i);
                if (++i >= 624) { _state[0] = _state[623]; i = 1; }
            }
            _state[0] = 0x80000000;
        }
        private void Initialize(uint seed)
        {
            _state[0] = seed;
            for (var i = 1; i < 624; i++) _state[i] = unchecked(1812433253 * (_state[i - 1] ^ (_state[i - 1] >> 30)) + (uint)i);
        }
        public uint Next()
        {
            if (_index >= 624)
            {
                for (var i = 0; i < 624; i++)
                {
                    var value = (_state[i] & 0x80000000) | (_state[(i + 1) % 624] & 0x7fffffff);
                    _state[i] = _state[(i + 397) % 624] ^ (value >> 1) ^ ((value & 1) == 0 ? 0 : 0x9908b0df);
                }
                _index = 0;
            }
            var result = _state[_index++];
            result ^= result >> 11;
            result ^= (result << 7) & 0x9d2c5680;
            result ^= (result << 15) & 0xefc60000;
            return result ^ (result >> 18);
        }
    }
}

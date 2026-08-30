namespace OptilandWorkbench.InitialStructure.Engine;

internal struct DeterministicRandom
{
    private ulong _state;

    public DeterministicRandom(long seed)
    {
        _state = unchecked((ulong)seed) + 0x9E3779B97F4A7C15UL;
    }

    public double NextUnitDouble()
    {
        var value = NextUInt64() >> 11;
        return value * (1.0 / (1UL << 53));
    }

    public int NextInt32(int exclusiveMaximum)
    {
        if (exclusiveMaximum <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(exclusiveMaximum));
        }

        return (int)(NextUnitDouble() * exclusiveMaximum);
    }

    private ulong NextUInt64()
    {
        var value = (_state += 0x9E3779B97F4A7C15UL);
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }
}

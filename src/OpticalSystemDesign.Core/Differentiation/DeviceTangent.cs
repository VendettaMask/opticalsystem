namespace OpticalSystemDesign.Core.Differentiation;

// One directional derivative and the same conservative all-parameter bound used
// by the recorded Core chain rule. This unmanaged value can execute on CUDA.
internal readonly struct DeviceTangent
{
    public readonly double Value, Derivative, Bound;
    public readonly int Invalid;
    public DeviceTangent(double value, double derivative = 0, double bound = 0, int invalid = 0)
        => (Value, Derivative, Bound, Invalid) = (value, derivative, bound, invalid);
    public static implicit operator DeviceTangent(double value) => new(value);
    public DeviceTangent WithValue(double value) => new(value, Derivative, Bound, Invalid);
    public bool IsFinite => Invalid == 0 && double.IsFinite(Value) && double.IsFinite(Derivative) && double.IsFinite(Bound);
    private static DeviceTangent Combine(double value, DeviceTangent a, double da, DeviceTangent b, double db)
    {
        var bound = Math.Abs(da) * a.Bound + Math.Abs(db) * b.Bound;
        var invalid = a.Invalid | b.Invalid;
        if (!double.IsFinite(bound) || !double.IsFinite(da) || !double.IsFinite(db)) invalid = 1;
        return new(value, da * a.Derivative + db * b.Derivative, bound, invalid);
    }
    public static DeviceTangent operator +(DeviceTangent a, DeviceTangent b) => Combine(a.Value + b.Value, a, 1, b, 1);
    public static DeviceTangent operator -(DeviceTangent a, DeviceTangent b) => Combine(a.Value - b.Value, a, 1, b, -1);
    public static DeviceTangent operator -(DeviceTangent a) => Combine(-a.Value, a, -1, 0, 0);
    public static DeviceTangent operator *(DeviceTangent a, DeviceTangent b) => Combine(a.Value * b.Value, a, b.Value, b, a.Value);
    public static DeviceTangent operator /(DeviceTangent a, DeviceTangent b)
    {
        if (a.Bound == 0 && b.Bound == 0 && a.Invalid == 0 && b.Invalid == 0) return a.Value / b.Value;
        var squared = b.Value * b.Value;
        var da = 1 / b.Value; var db = -a.Value / squared;
        var result = Combine(a.Value / b.Value, a, da, b, db);
        if (!double.IsFinite(squared) || squared == 0 || Math.Abs(squared) < 2.2250738585072014e-308
            || a.Value != 0 && db == 0) return new(result.Value, result.Derivative, result.Bound, 1);
        return result;
    }
    public static DeviceTangent Sqrt(DeviceTangent a)
    {
        if (a.Value == 0 && a.Bound == 0) return a;
        var root = Math.Sqrt(a.Value);
        return Combine(root, a, .5 / root, 0, 0);
    }
    public static DeviceTangent PositivePart(DeviceTangent a) => !a.IsFinite ? new(0, 0, 0, 1) : a.Value > 0 ? a : 0;
}

using OptilandWorkbench.Core.Materials;

namespace OptilandWorkbench.Core.Propagation;

/// <summary>
/// Conservative interval check before axial integration: a finite step must not jump
/// over a non-positive quartic index or a narrow dispersive pole. Ambiguous domains fail closed.
/// </summary>
internal static class Gradient5AxialDomain
{
    internal static void Validate(Gradient5IndexProfile profile, double start, double end, double wavelength,
        CancellationToken cancellationToken)
    {
        profile.Dispersion?.ValidateWavelength(wavelength);
        var pending = new Stack<(double Low, double High, int Depth)>();
        pending.Push((Math.Min(start, end), Math.Max(start, end), 0));
        var attempts = 0;
        while (pending.TryPop(out var interval))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var middle = interval.Low * .5 + interval.High * .5;
            _ = profile.Evaluate(new(0, 0, middle), wavelength);
            if (Certified(profile, new(interval.Low, interval.High), wavelength)) continue;
            if (++attempts > 8192 || interval.Depth >= 40 || middle == interval.Low || middle == interval.High)
                throw new SpatialIndexDomainException("Gradient 5 轴向区间不能确认始终为有限正折射率且不跨越色散极点；请检查材料系数和范围。");
            pending.Push((middle, interval.High, interval.Depth + 1));
            pending.Push((interval.Low, middle, interval.Depth + 1));
        }
    }

    private static bool Certified(Gradient5IndexProfile p, Bounds z, double wavelength)
    {
        var reference = Bounds.Point(p.BaseIndex) + z * (Bounds.Point(p.Axial1)
            + z * (Bounds.Point(p.Axial2) + z * (Bounds.Point(p.Axial3) + z * Bounds.Point(p.Axial4))));
        if (!reference.PositiveFinite) return false;
        if (p.Dispersion is not { } dispersion) return true;
        var wave = Bounds.Point(wavelength) * Bounds.Point(.001);
        var referenceWave = Bounds.Point(dispersion.ReferenceWavelengthNanometers) * Bounds.Point(.001);
        var square = wave * wave;
        var difference = (wave - referenceWave) * (wave + referenceWave);
        var indexSquared = reference * reference;
        for (var i = 0; i < 3; i++)
        {
            var k = Polynomial(dispersion.K[i], reference);
            var l = Polynomial(dispersion.L[i], reference);
            var denominator = square - l;
            if (!denominator.Finite || denominator.Low <= 0 && denominator.High >= 0) return false;
            indexSquared += difference * k * new Bounds(Down(1 / denominator.High), Up(1 / denominator.Low));
        }
        return indexSquared.PositiveFinite;
    }

    private static Bounds Polynomial(IReadOnlyList<double> coefficients, Bounds x)
    {
        var value = Bounds.Point(coefficients[^1]);
        for (var i = coefficients.Count - 2; i >= 0; i--) value = value * x + Bounds.Point(coefficients[i]);
        return value;
    }

    private static double Down(double value) => double.IsFinite(value) ? Math.BitDecrement(value) : value;
    private static double Up(double value) => double.IsFinite(value) ? Math.BitIncrement(value) : value;

    private readonly record struct Bounds(double Low, double High)
    {
        internal bool Finite => double.IsFinite(Low) && double.IsFinite(High);
        internal bool PositiveFinite => Finite && Low > 0;
        internal static Bounds Point(double value) => new(value, value);
        public static Bounds operator +(Bounds a, Bounds b) => new(Down(a.Low + b.Low), Up(a.High + b.High));
        public static Bounds operator -(Bounds a, Bounds b) => new(Down(a.Low - b.High), Up(a.High - b.Low));
        public static Bounds operator *(Bounds a, Bounds b)
        {
            var aa = a.Low * b.Low; var ab = a.Low * b.High;
            var ba = a.High * b.Low; var bb = a.High * b.High;
            return new(Down(Math.Min(Math.Min(aa, ab), Math.Min(ba, bb))), Up(Math.Max(Math.Max(aa, ab), Math.Max(ba, bb))));
        }
    }
}

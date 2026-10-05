using OptilandWorkbench.Core.Materials;

namespace OptilandWorkbench.Core.Propagation;

/// <summary>Maps (height, dy/dz) between two axial planes in the profile's own +Z frame.</summary>
public readonly record struct GradientIndexParaxialMatrix(double A, double B, double C, double D)
{
    public double Determinant => A * D - B * C;
    public (double Height, double Slope) Transform(double height, double slope) =>
        (A * height + B * slope, C * height + D * slope);

    public static GradientIndexParaxialMatrix operator +(GradientIndexParaxialMatrix a, GradientIndexParaxialMatrix b) =>
        new(a.A + b.A, a.B + b.B, a.C + b.C, a.D + b.D);
    public static GradientIndexParaxialMatrix operator -(GradientIndexParaxialMatrix a, GradientIndexParaxialMatrix b) =>
        new(a.A - b.A, a.B - b.B, a.C - b.C, a.D - b.D);
    public static GradientIndexParaxialMatrix operator *(GradientIndexParaxialMatrix a, double scale) =>
        new(a.A * scale, a.B * scale, a.C * scale, a.D * scale);
}

/// <summary>
/// Linearizes the formal isotropic ray equation about a straight axial ray:
/// dy/dz = p/n, dp/dz = n_yy*y, p = n*dy/dz. Integrates both independent columns.
/// Not a finite-height real-ray approximation or an entrance-only direction kick.
/// </summary>
public static class GradientIndexParaxialTransport
{
    public static GradientIndexParaxialMatrix Between(GradientIndexMaterial material, double startZ, double endZ,
        double wavelengthNanometers, bool useX = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(material);
        if (!double.IsFinite(startZ) || !double.IsFinite(endZ) || !double.IsFinite(endZ - startZ))
            throw new ArgumentOutOfRangeException(nameof(endZ));
        var distance = Math.Abs(endZ - startZ);
        if (distance > material.MaximumPathLength)
            throw new InvalidOperationException("GRIN 近轴传播超过材料的有限路径预算。");
        ValidateAxis(material.Profile);
        var options = material.IntegrationOptions;
        cancellationToken.ThrowIfCancellationRequested();
        var inputIndex = material.RefractiveIndex(new(0, 0, startZ), wavelengthNanometers);
        var outputIndex = material.RefractiveIndex(new(0, 0, endZ), wavelengthNanometers);
        ValidateAxialInterval(material, startZ, endZ, wavelengthNanometers);
        if (material.Profile is Gradient5IndexProfile gradient5)
            Gradient5AxialDomain.Validate(gradient5, startZ, endZ, wavelengthNanometers, cancellationToken);
        var curvature = material.Profile switch
        {
            Gradient1IndexProfile p => 2 * p.RadialQuadratic,
            Gradient2IndexProfile p => p.RadialCoefficients[0] / Math.Sqrt(p.BaseIndexSquared),
            Gradient3IndexProfile p => 2 * p.Radial2,
            Gradient4IndexProfile p => 2 * (useX ? p.Quadratic.X : p.Quadratic.Y),
            Gradient5IndexProfile => 0, // Actual curvature depends on z and wavelength; evaluated below.
            _ => throw new NotSupportedException("该空间分布尚无轴上一阶导数。")
        };
        if (!double.IsFinite(curvature)) throw new SpatialIndexDomainException("GRIN 横向二阶导数不是有限值。");
        // Internal columns are canonical (y,p); conversion to slopes happens only at the endpoints.
        var state = new GradientIndexParaxialMatrix(1, 0, 0, 1);
        var sign = Math.Sign(endZ - startZ);
        var traveled = 0.0;
        var step = Math.Min(options.MaximumStep, distance);
        var attempts = 0;
        while (traveled < distance)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++attempts > options.MaximumAttempts)
                throw new InvalidOperationException("GRIN 近轴积分超过最大尝试次数。");
            step = Math.Min(step, distance - traveled);
            if (step <= 0 || traveled + step == traveled)
                throw new InvalidOperationException("GRIN 近轴积分无法推进到目标平面。");
            var z = startZ + sign * traveled;
            var h = sign * step;
            GradientIndexParaxialMatrix high, delta;
            try
            {
                var k1 = Derivative(z, state);
                var k2 = Derivative(z + h / 5, state + k1 * (h / 5));
                var k3 = Derivative(z + 3 * h / 10, state + (k1 * (3.0 / 40) + k2 * (9.0 / 40)) * h);
                var k4 = Derivative(z + 4 * h / 5, state + (k1 * (44.0 / 45) - k2 * (56.0 / 15) + k3 * (32.0 / 9)) * h);
                var k5 = Derivative(z + 8 * h / 9, state + (k1 * (19372.0 / 6561) - k2 * (25360.0 / 2187)
                    + k3 * (64448.0 / 6561) - k4 * (212.0 / 729)) * h);
                var k6 = Derivative(z + h, state + (k1 * (9017.0 / 3168) - k2 * (355.0 / 33)
                    + k3 * (46732.0 / 5247) + k4 * (49.0 / 176) - k5 * (5103.0 / 18656)) * h);
                high = state + (k1 * (35.0 / 384) + k3 * (500.0 / 1113) + k4 * (125.0 / 192)
                    - k5 * (2187.0 / 6784) + k6 * (11.0 / 84)) * h;
                var k7 = Derivative(z + h, high);
                var low = state + (k1 * (5179.0 / 57600) + k3 * (7571.0 / 16695) + k4 * (393.0 / 640)
                    - k5 * (92097.0 / 339200) + k6 * (187.0 / 2100) + k7 * (1.0 / 40)) * h;
                delta = high - low;
            }
            catch (SpatialIndexDomainException)
            {
                step = Reduce(step, double.PositiveInfinity);
                continue;
            }
            var error = Math.Max(Math.Max(Error(delta.A, state.A, high.A, options.PositionTolerance),
                    Error(delta.B, state.B, high.B, options.PositionTolerance)),
                Math.Max(Error(delta.C, state.C, high.C, options.DirectionTolerance),
                    Error(delta.D, state.D, high.D, options.DirectionTolerance)));
            if (!double.IsFinite(error) || error > 1)
            {
                step = Reduce(step, error);
                continue;
            }
            state = high;
            traveled += step;
            step = Math.Min(options.MaximumStep, step * Math.Clamp(error == 0 ? 5 : .9 * Math.Pow(error, -.2), .2, 5));
        }
        var result = new GradientIndexParaxialMatrix(state.A, state.B * inputIndex,
            state.C / outputIndex, state.D * inputIndex / outputIndex);
        RequireFinite(result);
        return result;

        GradientIndexParaxialMatrix Derivative(double z, GradientIndexParaxialMatrix value)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequireFinite(value);
            var index = material.RefractiveIndex(new(0, 0, z), wavelengthNanometers);
            var localCurvature = material.Profile is Gradient5IndexProfile p
                ? p.AxialTransverseCurvature(z, wavelengthNanometers) : curvature;
            var derivative = new GradientIndexParaxialMatrix(value.C / index, value.D / index,
                localCurvature * value.A, localCurvature * value.B);
            RequireFinite(derivative);
            return derivative;
        }
        double Error(double deltaValue, double oldValue, double newValue, double tolerance) =>
            Math.Abs(deltaValue) / (tolerance + options.RelativeTolerance * Math.Max(Math.Abs(oldValue), Math.Abs(newValue)));
        double Reduce(double value, double error)
        {
            var reduced = value * (double.IsFinite(error) ? Math.Clamp(.9 * Math.Pow(error, -.2), .1, .5) : .1);
            if (reduced < options.MinimumStep)
                throw new InvalidOperationException("GRIN 近轴积分无法在最小步长内达到指定误差或保持有效折射率。");
            return reduced;
        }
    }

    internal static void ValidateAxis(ISpatialRefractiveIndex profile)
    {
        if (profile is Gradient1IndexProfile { RadialLinear: not 0 })
            throw new NotSupportedException("Gradient 1 的径向一次项在轴上不可微，不能建立轴上一阶传递。");
        if (profile is Gradient4IndexProfile p && (p.Linear.X != 0 || p.Linear.Y != 0))
            throw new NotSupportedException("横向一次梯度会弯曲中心光线，当前轴上一阶传递不适用于该分布。");
    }

    private static void ValidateAxialInterval(GradientIndexMaterial material, double start, double end, double wavelength)
    {
        var (linear, quadratic, cubic) = material.Profile switch
        {
            Gradient3IndexProfile p => (p.Axial1, p.Axial2, p.Axial3),
            Gradient4IndexProfile p => (p.Linear.Z, p.Quadratic.Z, 0.0),
            _ => (0.0, 0.0, 0.0)
        };
        // Endpoints alone miss a non-positive interior minimum. Check all axial
        // stationary points of these finite-degree profiles before any integration.
        var scale = Math.Max(Math.Abs(linear), Math.Max(Math.Abs(quadratic), Math.Abs(cubic)));
        if (scale == 0) return;
        var a = 3 * (cubic / scale); var b = 2 * (quadratic / scale); var c = linear / scale;
        if (a == 0)
        {
            if (b != 0) Check(-c / b);
            return;
        }
        var discriminant = b * b - 4 * a * c;
        if (discriminant < 0) return;
        var q = -.5 * (b + Math.CopySign(Math.Sqrt(discriminant), b));
        if (q == 0) Check(-b / (2 * a));
        else { Check(q / a); Check(c / q); }
        void Check(double z)
        {
            if (z > Math.Min(start, end) && z < Math.Max(start, end))
                _ = material.RefractiveIndex(new(0, 0, z), wavelength);
        }
    }

    private static void RequireFinite(GradientIndexParaxialMatrix matrix)
    {
        if (!double.IsFinite(matrix.A) || !double.IsFinite(matrix.B) || !double.IsFinite(matrix.C) || !double.IsFinite(matrix.D))
            throw new SpatialIndexDomainException("GRIN 近轴矩阵超出有限数值范围。");
    }
}

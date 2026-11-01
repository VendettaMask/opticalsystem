using OpticalSystemDesign.Core.Backend;

namespace OpticalSystemDesign.Core.Materials;

/// <summary>Immutable rectangular grid with explicit interface queries and a continuous natural-spline volume field.</summary>
public sealed class GridGradientIndexProfile : ISpatialRefractiveIndex
{
    private readonly object _cacheGate = new();
    private readonly Dictionary<double, SplineField> _fields = new();
    public GridGradientIndexProfile(int nx, int ny, int nz, Vector3D origin, Vector3D spacing,
        IReadOnlyList<double> values, string sourceName = "", string sourceSha256 = "")
    {
        if (nx is < 6 or > 128 || ny is < 6 or > 128 || nz is < 6 or > 128 || (long)nx * ny * nz > 80_000
            || values.Count != (long)nx * ny * nz * 11) throw new ArgumentException("Grid GRIN requires complete bounded 6+ point axes and eleven values per node.");
        if (!SpatialIndexValidation.Finite(origin) || !SpatialIndexValidation.Finite(spacing)
            || spacing.X <= 0 || spacing.Y <= 0 || spacing.Z <= 0 || values.Any(v => !double.IsFinite(v)))
            throw new ArgumentException("Grid coordinates, spacing and coefficients must be finite.");
        if (values.Where((_, i) => i % 11 >= 7).Any(v => v != 0)) throw new NotSupportedException("Schott grid coefficients beyond C6 are undefined.");
        Nx = nx; Ny = ny; Nz = nz; Origin = origin; Spacing = spacing;
        Values = Array.AsReadOnly(values.ToArray()); SourceName = sourceName; SourceSha256 = sourceSha256;
    }
    public int Nx { get; }
    public int Ny { get; }
    public int Nz { get; }
    public Vector3D Origin { get; }
    public Vector3D Spacing { get; }
    // Node order: X outermost, Z innermost; n0, C1..C10 at each node.
    public IReadOnlyList<double> Values { get; }
    public string SourceName { get; }
    public string SourceSha256 { get; }
    public string Formula => "Schott";
    private SplineField Field(double wavelength)
    {
        if (!double.IsFinite(wavelength) || wavelength <= 0) throw new ArgumentOutOfRangeException(nameof(wavelength));
        lock (_cacheGate)
        {
            if (_fields.TryGetValue(wavelength, out var cached)) return cached;
            var lambda2 = wavelength * wavelength * 1e-6;
            var powers = new[] { 1d, lambda2, 1 / lambda2, 1 / (lambda2 * lambda2), 1 / Math.Pow(lambda2, 3), 1 / Math.Pow(lambda2, 4) };
            var indices = new double[Nx * Ny * Nz];
            for (var i = 0; i < indices.Length; i++)
            {
                var square = 0d; for (var c = 0; c < 6; c++) square += Values[11 * i + c + 1] * powers[c];
                if (!double.IsFinite(square) || square < 0) throw new SpatialIndexDomainException("Invalid Schott grid dispersion.");
                indices[i] = Values[11 * i] + Math.Sqrt(square);
                SpatialIndexValidation.Sample(indices[i], Vector3D.Zero);
            }
            var field = new SplineField(this, indices);
            if (_fields.Count == 4) _fields.Remove(_fields.Keys.First());
            _fields.Add(wavelength, field); return field;
        }
    }
    public double RefractiveIndex(Vector3D localPosition, double wavelengthNanometers) => Evaluate(localPosition, wavelengthNanometers).Index;
    /// <summary>GGD index-query interpolant, distinct from the continuous volume-gradient interpolant.</summary>
    public double InterfaceRefractiveIndex(Vector3D point, double wavelength)
    {
        SpatialIndexValidation.Input(point, wavelength);
        return SpatialIndexValidation.Sample(Field(wavelength).QueryIndex(point), Vector3D.Zero).Index;
    }
    // GGD queries use four transverse nodes and the full axial spline. The
    // propagation gradient is the derivative of the continuous tensor field.
    // Keep this explicit: Evaluate remains a consistent differentiable field
    // for Gaussian transport and callers requesting an index and its gradient.
    internal SpatialIndexSample PropagationSample(Vector3D point, double wavelength)
    {
        SpatialIndexValidation.Input(point, wavelength);
        var field = Field(wavelength);
        return SpatialIndexValidation.Sample(field.QueryIndex(point), field.AtGlobal(point, gradientOnly: true).Gradient);
    }
    public SpatialIndexSample Evaluate(Vector3D localPosition, double wavelengthNanometers)
    {
        SpatialIndexValidation.Input(localPosition, wavelengthNanometers);
        var p = Field(wavelengthNanometers).AtGlobal(localPosition);
        return SpatialIndexValidation.Sample(p.Index, p.Gradient);
    }
    internal double AxialTransverseCurvature(double z, double wavelength, bool useX)
    {
        var p = Field(wavelength).AtGlobal(new(0, 0, z)); var gradient = useX ? p.Gradient.X : p.Gradient.Y;
        // Linearize u' = (n_y - u*n_z)/n at the declared axial reference.
        // In canonical p=n_axis*u this is p'=(n_yy-n_y²/n)*y.
        // The affine chief steering is excluded by the Gaussian definition.
        return (useX ? p.Hessian.X : p.Hessian.Y) - gradient * gradient / p.Index;
    }

    internal void ValidateGaussianReferenceAxis()
    {
        // Grid-gradient Gaussian rays are the homogeneous linear component
        // about the declared axis. Native zero pupil rays stay at zero even
        // when the real chief is steered by a linear gradient. No real-ray
        // steering term is removed from physical propagation.
        if (Origin.X > 0 || Origin.Y > 0 || Origin.X + (Nx - 1) * Spacing.X < 0 || Origin.Y + (Ny - 1) * Spacing.Y < 0)
            throw new NotSupportedException("The declared Gaussian axis lies outside the finite grid.");
    }
    internal void ValidateAxialInterval(double start, double end, double wavelength)
    {
        var field = Field(wavelength); var lo = Math.Min(start, end); var hi = Math.Max(start, end);
        _ = field.AtGlobal(new(0, 0, lo)); _ = field.AtGlobal(new(0, 0, hi));
        for (var k = 0; k < Nz - 1; k++)
        {
            var z = Origin.Z + k * Spacing.Z; var h = Spacing.Z;
            if (z + h < lo || z > hi) continue;
            var p = field.AtGlobal(new(0, 0, z)); var q = field.AtGlobal(new(0, 0, z + h));
            var a = 2 * p.Index - 2 * q.Index + h * (p.Gradient.Z + q.Gradient.Z);
            var b = -3 * p.Index + 3 * q.Index - h * (2 * p.Gradient.Z + q.Gradient.Z);
            var c = h * p.Gradient.Z;
            Check(0); Check(1);
            if (a == 0) { if (b != 0) Check(-c / (2 * b)); }
            else
            {
                var d = b * b - 3 * a * c;
                if (d >= 0) { Check((-b + Math.Sqrt(d)) / (3 * a)); Check((-b - Math.Sqrt(d)) / (3 * a)); }
            }
            void Check(double t)
            {
                if (t < 0 || t > 1 || z + t * h < lo || z + t * h > hi) return;
                SpatialIndexValidation.Sample(((a * t + b) * t + c) * t + p.Index, Vector3D.Zero);
            }
        }
    }
    private readonly record struct Differential(double Index, Vector3D Gradient, Vector3D Hessian);
    private sealed class SplineField
    {
        private readonly GridGradientIndexProfile _grid;
        private readonly double[] _nodes;
        private readonly Lazy<double[][]> _globalDerivatives;
        internal SplineField(GridGradientIndexProfile grid, double[] nodes)
        {
            _grid = grid; _nodes = nodes;
            _globalDerivatives = new(BuildGlobalDerivatives, LazyThreadSafetyMode.ExecutionAndPublication);
        }
        private double[][] BuildGlobalDerivatives()
        {
            var derivatives = new double[8][]; derivatives[0] = _nodes; var grid = _grid;
            for (var mask = 1; mask < 8; mask++)
            {
                var axis = (mask & 1) != 0 ? 0 : (mask & 2) != 0 ? 1 : 2; var source = derivatives[mask ^ (1 << axis)];
                var count = axis == 0 ? grid.Nx : axis == 1 ? grid.Ny : grid.Nz;
                var stride = axis == 0 ? grid.Ny * grid.Nz : axis == 1 ? grid.Nz : 1;
                var h = axis == 0 ? grid.Spacing.X : axis == 1 ? grid.Spacing.Y : grid.Spacing.Z;
                var output = new double[_nodes.Length]; var values = new double[count];
                for (var start = 0; start < _nodes.Length; start++)
                {
                    if (start / stride % count != 0) continue;
                    for (var i = 0; i < count; i++) values[i] = source[start + i * stride];
                    var m = new double[count]; var upper = new double[count]; var rhs = new double[count];
                    for (var i = 1; i < count - 1; i++)
                    {
                        var d = 4 - upper[i - 1]; upper[i] = 1 / d;
                        rhs[i] = (6 * (values[i - 1] - 2 * values[i] + values[i + 1]) / (h * h) - rhs[i - 1]) / d;
                    }
                    for (var i = count - 2; i >= 1; i--) m[i] = rhs[i] - upper[i] * m[i + 1];
                    for (var i = 0; i < count - 1; i++) output[start + i * stride] = (values[i + 1] - values[i]) / h - h * (2 * m[i] + m[i + 1]) / 6;
                    output[start + (count - 1) * stride] = (values[count - 1] - values[count - 2]) / h + h * (m[count - 2] + 2 * m[count - 1]) / 6;
                }
                derivatives[mask] = output;
            }
            return derivatives;
        }
        internal Differential AtGlobal(Vector3D point, bool gradientOnly = false)
        {
            var g = _grid; var u = (point.X - g.Origin.X) / g.Spacing.X; var v = (point.Y - g.Origin.Y) / g.Spacing.Y;
            var w = (point.Z - g.Origin.Z) / g.Spacing.Z; var ix = Cell(ref u, g.Nx); var iy = Cell(ref v, g.Ny); var iz = Cell(ref w, g.Nz);
            Span<double> x = stackalloc double[12]; Span<double> y = stackalloc double[12]; Span<double> z = stackalloc double[12];
            Hermite(u, g.Spacing.X, x); Hermite(v, g.Spacing.Y, y); Hermite(w, g.Spacing.Z, z);
            double n = 0, dx = 0, dy = 0, dz = 0, dxx = 0, dyy = 0, dzz = 0; var derivatives = _globalDerivatives.Value;
            for (var a = 0; a < 2; a++) for (var b = 0; b < 2; b++) for (var c = 0; c < 2; c++) for (var mask = 0; mask < 8; mask++)
            {
                var p = 2 * a + (mask & 1); var q = 2 * b + ((mask >> 1) & 1); var r = 2 * c + ((mask >> 2) & 1);
                var f = derivatives[mask][((ix + a) * g.Ny + iy + b) * g.Nz + iz + c];
                dx += f * x[p + 4] * y[q] * z[r]; dy += f * x[p] * y[q + 4] * z[r]; dz += f * x[p] * y[q] * z[r + 4];
                if (!gradientOnly)
                {
                    n += f * x[p] * y[q] * z[r];
                    dxx += f * x[p + 8] * y[q] * z[r]; dyy += f * x[p] * y[q + 8] * z[r]; dzz += f * x[p] * y[q] * z[r + 8];
                }
            }
            return new(n, new(dx, dy, dz), new(dxx, dyy, dzz));
        }
        private static void Hermite(double t, double h, Span<double> b)
        {
            b[0] = 2 * t * t * t - 3 * t * t + 1; b[1] = (t * t * t - 2 * t * t + t) * h;
            b[2] = -2 * t * t * t + 3 * t * t; b[3] = (t * t * t - t * t) * h;
            b[4] = (6 * t * t - 6 * t) / h; b[5] = 3 * t * t - 4 * t + 1;
            b[6] = (-6 * t * t + 6 * t) / h; b[7] = 3 * t * t - 2 * t;
            b[8] = (12 * t - 6) / (h * h); b[9] = (6 * t - 4) / h; b[10] = (-12 * t + 6) / (h * h); b[11] = (6 * t - 2) / h;
        }
        internal double QueryIndex(Vector3D point)
        {
            var g = _grid;
            var u = (point.X - g.Origin.X) / g.Spacing.X; var v = (point.Y - g.Origin.Y) / g.Spacing.Y;
            var w = (point.Z - g.Origin.Z) / g.Spacing.Z;
            var ix = Cell(ref u, g.Nx); var iy = Cell(ref v, g.Ny); var iz = Cell(ref w, g.Nz);
            var sx = QueryWindow(point.X, g.Origin.X, g.Spacing.X, g.Nx);
            var sy = QueryWindow(point.Y, g.Origin.Y, g.Spacing.Y, g.Ny);
            Span<double> x = stackalloc double[12]; Span<double> y = stackalloc double[12]; Span<double> z = stackalloc double[12];
            Basis(ix - sx, u, g.Spacing.X, x); Basis(iy - sy, v, g.Spacing.Y, y); Hermite(w, g.Spacing.Z, z);
            var derivatives = _globalDerivatives.Value; var value = 0d;
            for (var a = 0; a < 4; a++) for (var b = 0; b < 4; b++) for (var c = 0; c < 2; c++)
            {
                var index = ((sx + a) * g.Ny + sy + b) * g.Nz + iz + c;
                value += x[a] * y[b] * (derivatives[0][index] * z[2 * c] + derivatives[4][index] * z[2 * c + 1]);
            }
            return value;
        }
        private static int QueryWindow(double position, double origin, double spacing, int count) =>
            // The signed coordinate bucket and integer origin are separate.
            // Combining them before floor changes even and shifted grids.
            (int)Math.Clamp(Math.Floor(position / spacing) - Math.Truncate(origin / spacing) - (position > 0 ? 0 : 1), 0, count - 4);
        private static int Cell(ref double coordinate, int count)
        {
            const double epsilon = 2.2204460492503131e-16;
            var tolerance = 64 * epsilon * count;
            if (!double.IsFinite(coordinate) || coordinate < -tolerance || coordinate > count - 1 + tolerance)
                throw new SpatialIndexDomainException("The ray has left the finite index grid.");
            coordinate = Math.Clamp(coordinate, 0, count - 1);
            var i = Math.Min(count - 2, (int)Math.Floor(coordinate)); coordinate -= i; return i;
        }
        private static void Basis(int interval, double t, double h, Span<double> b)
        {
            // Solve the two interior natural-spline second derivatives:
            // 4 M1 + M2 = 6 Δ²f1/h², M1 + 4 M2 = 6 Δ²f2/h².
            // These integer coefficients are the exact inverse system, not fitted controls.
            ReadOnlySpan<double> m1 = [4, -9, 6, -1]; ReadOnlySpan<double> m2 = [-1, 6, -9, 4];
            var s = 1 - t;
            for (var i = 0; i < 4; i++)
            {
                var left = interval == 1 ? m1[i] : interval == 2 ? m2[i] : 0;
                var right = interval == 0 ? m1[i] : interval == 1 ? m2[i] : 0;
                b[i] = (i == interval ? s : i == interval + 1 ? t : 0)
                    + ((s * s * s - s) * left + (t * t * t - t) * right) / 15;
                b[i + 4] = ((i == interval ? -1 : i == interval + 1 ? 1 : 0)
                    + ((1 - 3 * s * s) * left + (3 * t * t - 1) * right) / 15) / h;
                b[i + 8] = (6 * s * left + 6 * t * right) / (15 * h * h);
            }
        }
    }
}

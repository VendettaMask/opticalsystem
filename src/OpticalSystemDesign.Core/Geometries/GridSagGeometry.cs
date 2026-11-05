using OpticalSystemDesign.Core.Backend;

namespace OpticalSystemDesign.Core.Geometries;

public readonly record struct GridSagPoint(double Sag, double Dx, double Dy, double Dxy, bool NoData = false);

/// <summary>Rectangular sag grid, stored left-to-right from the upper (+Y) row.</summary>
public sealed class GridSagGeometry : IGeometry
{
    public GridSagGeometry(double radius, double conic, IReadOnlyList<double> asphereCoefficients,
        int columns, int rows, double spacingX, double spacingY, double decenterX, double decenterY,
        bool linear, IReadOnlyList<GridSagPoint> points)
    {
        if (columns is < 5 or > 4096 || rows is < 5 or > 4096 || (long)columns * rows > 100_000
            || points.Count != (long)columns * rows) throw new ArgumentException("网格面需要完整且有界的至少 5×5 数据。");
        if (!double.IsFinite(spacingX) || !double.IsFinite(spacingY) || spacingX <= 0 || spacingY <= 0
            || !double.IsFinite(decenterX) || !double.IsFinite(decenterY)) throw new ArgumentException("网格间距必须为正且有限。");
        if (asphereCoefficients.Count > 8 || asphereCoefficients.Any(c => !double.IsFinite(c))) throw new ArgumentException("Invalid grid base asphere.");
        Base = new(radius, conic, asphereCoefficients); Columns = columns; Rows = rows;
        SpacingX = spacingX; SpacingY = spacingY; DecenterX = decenterX; DecenterY = decenterY; Linear = linear;
        var data = points.Select(p => p.NoData ? new GridSagPoint(0, 0, 0, 0, true) : p).ToArray();
        if (data.Any(p => !double.IsFinite(p.Sag) || !double.IsFinite(p.Dx) || !double.IsFinite(p.Dy) || !double.IsFinite(p.Dxy)))
            throw new ArgumentException("Grid sag values must be finite.");
        // Embedded ZMX grids already contain their authoritative derivatives.
        // Automatic estimation belongs to a separate DAT-import operation;
        // a saved grid with zero derivatives must retain those zero derivatives.
        Points = Array.AsReadOnly(data);
    }
    public string Kind => "grid_sag";
    public EvenAsphereGeometry Base { get; }
    public int Columns { get; }
    public int Rows { get; }
    public double SpacingX { get; }
    public double SpacingY { get; }
    public double DecenterX { get; }
    public double DecenterY { get; }
    public bool Linear { get; }
    public IReadOnlyList<GridSagPoint> Points { get; }

    internal bool ContainsGridPoint(Vector3D point) =>
        Math.Abs(point.X - DecenterX) <= (Columns - 1) * SpacingX / 2
        && Math.Abs(point.Y - DecenterY) <= (Rows - 1) * SpacingY / 2;

    private (double Value, double Dx, double Dy, double Dyy) Grid(double x, double y, bool normalField = false)
    {
        var u = (x - DecenterX) / SpacingX + (Columns - 1) / 2d;
        var v = (Rows - 1) / 2d - (y - DecenterY) / SpacingY;
        if (!double.IsFinite(u) || !double.IsFinite(v) || u < 0 || v < 0 || u > Columns - 1 || v > Rows - 1)
            return (double.NaN, double.NaN, double.NaN, double.NaN);
        var i = Math.Min((int)Math.Floor(u), Columns - 2); var j = Math.Min((int)Math.Floor(v), Rows - 2);
        u -= i; v -= j;
        if (Linear)
        {
            var pa = Points[j * Columns + i]; var pb = Points[j * Columns + i + 1];
            var pc = Points[(j + 1) * Columns + i]; var pd = Points[(j + 1) * Columns + i + 1];
            var a = pa.Sag; var b = pb.Sag; var c = pc.Sag; var d = pd.Sag;
            return ((1 - v) * ((1 - u) * a + u * b) + v * ((1 - u) * c + u * d),
                normalField ? (1 - v) * ((1 - u) * pa.Dx + u * pb.Dx) + v * ((1 - u) * pc.Dx + u * pd.Dx)
                    : ((1 - v) * (b - a) + v * (d - c)) / SpacingX,
                normalField ? (1 - v) * ((1 - u) * pa.Dy + u * pb.Dy) + v * ((1 - u) * pc.Dy + u * pd.Dy)
                    : -((1 - u) * (c - a) + u * (d - b)) / SpacingY,
                normalField ? -((1 - u) * (pc.Dy - pa.Dy) + u * (pd.Dy - pb.Dy)) / SpacingY : 0);
        }
        Span<double> hx = stackalloc double[4]; Span<double> hy = stackalloc double[4];
        Span<double> dx = stackalloc double[4]; Span<double> dy = stackalloc double[4];
        Hermite(u, hx, dx); Hermite(v, hy, dy);
        Span<double> ddy = stackalloc double[] { 12 * v - 6, 6 * v - 4, -12 * v + 6, 6 * v - 2 };
        double value = 0, gx = 0, gy = 0, gyy = 0;
        for (var a = 0; a < 2; a++) for (var b = 0; b < 2; b++)
        {
            var p = Points[(j + b) * Columns + i + a]; var ax = 2 * a; var by = 2 * b;
            value += hx[ax] * hy[by] * p.Sag + hx[ax + 1] * hy[by] * p.Dx * SpacingX
                - hx[ax] * hy[by + 1] * p.Dy * SpacingY - hx[ax + 1] * hy[by + 1] * p.Dxy * SpacingX * SpacingY;
            gx += dx[ax] * hy[by] * p.Sag + dx[ax + 1] * hy[by] * p.Dx * SpacingX
                - dx[ax] * hy[by + 1] * p.Dy * SpacingY - dx[ax + 1] * hy[by + 1] * p.Dxy * SpacingX * SpacingY;
            gy += hx[ax] * dy[by] * p.Sag + hx[ax + 1] * dy[by] * p.Dx * SpacingX
                - hx[ax] * dy[by + 1] * p.Dy * SpacingY - hx[ax + 1] * dy[by + 1] * p.Dxy * SpacingX * SpacingY;
            gyy += hx[ax] * ddy[by] * p.Sag + hx[ax + 1] * ddy[by] * p.Dx * SpacingX
                - hx[ax] * ddy[by + 1] * p.Dy * SpacingY - hx[ax + 1] * ddy[by + 1] * p.Dxy * SpacingX * SpacingY;
        }
        return (value, gx / SpacingX, -gy / SpacingY, gyy / (SpacingY * SpacingY));
    }
    private static void Hermite(double t, Span<double> h, Span<double> d)
    {
        var t2 = t * t; var t3 = t2 * t;
        h[0] = 2 * t3 - 3 * t2 + 1; h[1] = t3 - 2 * t2 + t;
        h[2] = -2 * t3 + 3 * t2; h[3] = t3 - t2;
        d[0] = 6 * t2 - 6 * t; d[1] = 3 * t2 - 4 * t + 1;
        d[2] = -d[0]; d[3] = 3 * t2 - 2 * t;
    }
    public double Sag(double x, double y) => Base.Sag(x, y) + Grid(x, y).Value;
    public Vector3D SurfaceNormal(Vector3D point) => Normal(point, true);
    private Vector3D Normal(Vector3D point, bool normalField)
    {
        // In native linear-grid tracing, sag and the supplied normal field are
        // interpolated separately. Bicubic normals are analytic sag derivatives.
        var grid = Grid(point.X, point.Y, normalField); var b = Base.SurfaceNormal(point);
        var n = new Vector3D(b.X / b.Z - grid.Dx, b.Y / b.Z - grid.Dy, 1); return n / n.Length;
    }
    public IntersectionResult DistanceToIntersection(Vector3D origin, Vector3D direction)
    {
        // Newton's derivative belongs to the sag equation, even when the linear
        // grid's independent measured normal field differs from that derivative.
        var result = StandardGeometry.NewtonSolveDistance(origin, direction, Sag, p => Normal(p, false));
        return result.IsHit ? result with { Normal = SurfaceNormal(result.Point) } : result;
    }
    internal double VertexMeridionalCurvature =>
        (Base.Base.Radius == 0 || double.IsInfinity(Base.Base.Radius) ? 0 : 1 / Base.Base.Radius)
            + (Base.Coefficients.Count == 0 ? 0 : 2 * Base.Coefficients[0])
            + (Grid(0, SpacingY * 1e-8, true).Dyy + Grid(0, -SpacingY * 1e-8, true).Dyy) / 2;
    public IGeometry Clone() => new GridSagGeometry(Base.Base.Radius, Base.Base.Conic, Base.Coefficients,
        Columns, Rows, SpacingX, SpacingY, DecenterX, DecenterY, Linear, Points);
}

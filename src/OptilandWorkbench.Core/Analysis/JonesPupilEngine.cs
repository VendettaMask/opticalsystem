using System.Numerics;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Core.Analysis;

public sealed record JonesPupilSample(
    double Px,
    double Py,
    Complex Jxx,
    Complex Jxy,
    Complex Jyx,
    Complex Jyy,
    bool IsValid);

public sealed record JonesPupilResult(
    IReadOnlyList<JonesPupilSample> Samples,
    int GridSize,
    (double Hx, double Hy) Field,
    Wavelength Wavelength,
    bool UsesFresnelCoatings);

public static class JonesPupilEngine
{
    public static JonesPupilResult Generate(
        Optic optic,
        (double Hx, double Hy) field,
        Wavelength wavelength,
        int gridSize = 65,
        bool useFresnelCoatings = true,
        bool cellCentered = false,
        bool aimAtStop = false)
    {
        gridSize = Math.Max(3, gridSize);
        var samples = new List<JonesPupilSample>(gridSize * gridSize);
        for (var row = 0; row < gridSize; row++)
        {
            var py = cellCentered
                ? -1 + ((2.0 * row + 1) / gridSize)
                : -1 + (2.0 * row / (gridSize - 1));
            for (var column = 0; column < gridSize; column++)
            {
                var px = cellCentered
                    ? -1 + ((2.0 * column + 1) / gridSize)
                    : -1 + (2.0 * column / (gridSize - 1));
                if ((px * px) + (py * py) > 1 + 1e-12)
                {
                    samples.Add(Invalid(px, py));
                    continue;
                }

                samples.Add(TraceSample(
                    optic,
                    field,
                    wavelength,
                    px,
                    py,
                    useFresnelCoatings,
                    aimAtStop: cellCentered || aimAtStop));
            }
        }

        return new JonesPupilResult(samples, gridSize, field, wavelength, useFresnelCoatings);
    }

    private static JonesPupilSample TraceSample(
        Optic optic,
        (double Hx, double Hy) field,
        Wavelength wavelength,
        double px,
        double py,
        bool useFresnelCoatings,
        bool aimAtStop)
    {
        var bundle = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(
            field.Hx,
            field.Hy,
            px,
            py,
            wavelength.Micrometers,
            aimAtStop);
        var sourceRay = bundle.Rays.Single();
        var trace = optic.SequentialRayTracer.Trace(bundle);
        var history = trace.RayHistories.Single();
        if (history.Count != optic.SurfaceGroup.Items.Count || history.Any(item => item.Vignetted || item.Intensity <= 0))
        {
            return Invalid(px, py);
        }

        var polarization = ComplexMatrix3x3.Identity;
        var incoming = Normalize(sourceRay.Direction);
        var materialBefore = optic.Materials.Resolve("Air");
        for (var index = 0; index < history.Count; index++)
        {
            var surface = optic.SurfaceGroup.Items[index];
            var sample = history[index];
            var outgoing = Normalize(sample.Direction);
            var materialAfter = surface.MaterialAfter;

            var localPoint = surface.CoordinateSystem.ToLocalPoint(sample.Position);
            var normal = Normalize(surface.CoordinateSystem.ToGlobalDirection(
                surface.Geometry.SurfaceNormal(localPoint)));
            if (Dot(incoming, normal) > 0)
            {
                normal = -normal;
            }
            var reflected = surface.IsReflective || Dot(outgoing, normal) > 0;
            var s = Cross(incoming, outgoing);
            if (s.Length <= 1e-14)
            {
                s = Cross(incoming, new Vector3D(1, 0, 0));
            }

            s = Normalize(s);
            var p0 = Normalize(Cross(incoming, s));
            var p1 = Normalize(Cross(outgoing, s));
            var oIn = ComplexMatrix3x3.FromRows(s, p0, incoming);
            var oOut = ComplexMatrix3x3.FromColumns(s, p1, outgoing);
            var jones = ComplexMatrix3x3.Identity;
            if (useFresnelCoatings)
            {
                var cosine = Math.Clamp(Math.Abs(Dot(normal, incoming)), -1, 1);
                jones = FresnelMatrix(
                    materialBefore.RefractiveIndex(wavelength.Nanometers),
                    materialAfter.RefractiveIndex(wavelength.Nanometers),
                    cosine,
                    reflected);
            }

            polarization = oOut * jones * oIn * polarization;
            incoming = outgoing;
            materialBefore = reflected ? materialBefore : materialAfter;
        }

        var k = incoming;
        var v = Normalize(Cross(k, new Vector3D(1, 0, 0)));
        var u = Normalize(Cross(v, k));
        var xColumn = polarization.Column1;
        var yColumn = polarization.Column2;
        return new JonesPupilSample(
            px,
            py,
            Dot(u, xColumn),
            Dot(u, yColumn),
            Dot(v, xColumn),
            Dot(v, yColumn),
            true);
    }

    private static ComplexMatrix3x3 FresnelMatrix(double n1, double n2, double cosine, bool reflect)
    {
        var n = n2 / Math.Max(1e-30, n1);
        var sineSquared = Math.Max(0, 1 - (cosine * cosine));
        var root = Complex.Sqrt((n * n) - sineSquared);
        Complex s;
        Complex p;
        Complex longitudinal;
        if (reflect)
        {
            s = (cosine - root) / (cosine + root);
            p = (((n * n) * cosine) - root) / (((n * n) * cosine) + root);
            p = -p;
            longitudinal = -Complex.One;
        }
        else
        {
            s = 2 * cosine / (cosine + root);
            p = 2 * n * cosine / (((n * n) * cosine) + root);
            longitudinal = Complex.One;
        }

        return ComplexMatrix3x3.Diagonal(s, p, longitudinal);
    }

    private static JonesPupilSample Invalid(double px, double py)
    {
        var nan = new Complex(double.NaN, double.NaN);
        return new JonesPupilSample(px, py, nan, nan, nan, nan, false);
    }

    private static Vector3D Normalize(Vector3D value)
    {
        return value.Length <= 1e-15 ? new Vector3D(0, 0, 1) : value / value.Length;
    }

    private static Vector3D Cross(Vector3D left, Vector3D right)
    {
        return new Vector3D(
            (left.Y * right.Z) - (left.Z * right.Y),
            (left.Z * right.X) - (left.X * right.Z),
            (left.X * right.Y) - (left.Y * right.X));
    }

    private static double Dot(Vector3D left, Vector3D right)
    {
        return (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);
    }

    private static Complex Dot(Vector3D left, (Complex X, Complex Y, Complex Z) right)
    {
        return (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);
    }

    private readonly record struct ComplexMatrix3x3(
        Complex M11,
        Complex M12,
        Complex M13,
        Complex M21,
        Complex M22,
        Complex M23,
        Complex M31,
        Complex M32,
        Complex M33)
    {
        public static ComplexMatrix3x3 Identity => Diagonal(Complex.One, Complex.One, Complex.One);

        public (Complex X, Complex Y, Complex Z) Column1 => (M11, M21, M31);

        public (Complex X, Complex Y, Complex Z) Column2 => (M12, M22, M32);

        public static ComplexMatrix3x3 Diagonal(Complex x, Complex y, Complex z)
        {
            return new ComplexMatrix3x3(x, 0, 0, 0, y, 0, 0, 0, z);
        }

        public static ComplexMatrix3x3 FromRows(Vector3D first, Vector3D second, Vector3D third)
        {
            return new ComplexMatrix3x3(
                first.X, first.Y, first.Z,
                second.X, second.Y, second.Z,
                third.X, third.Y, third.Z);
        }

        public static ComplexMatrix3x3 FromColumns(Vector3D first, Vector3D second, Vector3D third)
        {
            return new ComplexMatrix3x3(
                first.X, second.X, third.X,
                first.Y, second.Y, third.Y,
                first.Z, second.Z, third.Z);
        }

        public static ComplexMatrix3x3 operator *(ComplexMatrix3x3 left, ComplexMatrix3x3 right)
        {
            return new ComplexMatrix3x3(
                (left.M11 * right.M11) + (left.M12 * right.M21) + (left.M13 * right.M31),
                (left.M11 * right.M12) + (left.M12 * right.M22) + (left.M13 * right.M32),
                (left.M11 * right.M13) + (left.M12 * right.M23) + (left.M13 * right.M33),
                (left.M21 * right.M11) + (left.M22 * right.M21) + (left.M23 * right.M31),
                (left.M21 * right.M12) + (left.M22 * right.M22) + (left.M23 * right.M32),
                (left.M21 * right.M13) + (left.M22 * right.M23) + (left.M23 * right.M33),
                (left.M31 * right.M11) + (left.M32 * right.M21) + (left.M33 * right.M31),
                (left.M31 * right.M12) + (left.M32 * right.M22) + (left.M33 * right.M32),
                (left.M31 * right.M13) + (left.M32 * right.M23) + (left.M33 * right.M33));
        }
    }
}

using System.Numerics;
using OptilandWorkbench.Core.Backend;

namespace OptilandWorkbench.Core.Rays;

/// <summary>
/// Propagates two mutually incoherent, orthonormal Jones inputs. Averaging is
/// performed after the complete chain, retaining correlations as planes of
/// incidence rotate. Coherent input superpositions use the same transported basis.
/// Scalar source/coating/bulk weights remain in the ray tracer.
/// </summary>
internal sealed class UnpolarizedPowerTransport
{
    private ComplexElectricField _first;
    private ComplexElectricField _second;

    internal UnpolarizedPowerTransport(Vector3D direction)
    {
        var k = Normalize(direction);
        var u = Perpendicular(k);
        _first = ComplexElectricField.FromReal(u);
        _second = ComplexElectricField.FromReal(Cross(k, u));
    }

    internal UnpolarizedPowerTransport(Vector3D first, Vector3D second)
    {
        first = Normalize(first); second = Normalize(second);
        if (Math.Abs(Dot(first, second)) > 1e-12) throw new ArgumentException("Jones input basis must be orthogonal.");
        _first = ComplexElectricField.FromReal(first);
        _second = ComplexElectricField.FromReal(second);
    }

    internal ComplexElectricField Combine(Complex first, Complex second) => _first * first + _second * second;

    internal void ApplyCommonPhase(Complex phase)
    {
        _first *= phase;
        _second *= phase;
    }

    internal double Power => (_first.SquaredNorm + _second.SquaredNorm) / 2;

    internal void Apply(Vector3D incoming, Vector3D outgoing, Vector3D normal,
        Complex sAmplitude, Complex pAmplitude)
    {
        incoming = Normalize(incoming);
        outgoing = Normalize(outgoing);
        normal = Normalize(normal);
        var s = Cross(incoming, normal);
        s = s.Length > 1e-12 ? Normalize(s) : Perpendicular(incoming);
        var pIn = Normalize(Cross(s, incoming));
        var pOut = Normalize(Cross(s, outgoing));
        _first = Transform(_first);
        _second = Transform(_second);
        if (!double.IsFinite(Power) || Power < 0)
            throw new ArithmeticException("Polarization transport produced non-finite power.");

        ComplexElectricField Transform(ComplexElectricField field) =>
            ComplexElectricField.FromReal(s) * (field.Dot(s) * sAmplitude)
            + ComplexElectricField.FromReal(pOut) * (field.Dot(pIn) * pAmplitude);
    }

    private static Vector3D Perpendicular(Vector3D direction)
    {
        var reference = Math.Abs(direction.X) < 0.9 ? new Vector3D(1, 0, 0) : new Vector3D(0, 1, 0);
        return Normalize(reference - direction * Dot(reference, direction));
    }

    private static Vector3D Normalize(Vector3D value) => double.IsFinite(value.Length) && value.Length > 1e-15
        ? value / value.Length : throw new ArgumentException("Polarization basis direction is degenerate.");
    private static double Dot(Vector3D a, Vector3D b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    private static Vector3D Cross(Vector3D a, Vector3D b) => new(
        a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

}

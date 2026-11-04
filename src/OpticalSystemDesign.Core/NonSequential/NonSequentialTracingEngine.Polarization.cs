using System.Numerics;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Rays;

namespace OpticalSystemDesign.Core.NonSequential;

public sealed partial class NonSequentialDocumentTracer
{
    private static (NonSequentialFieldState? R, NonSequentialFieldState? T, double Reflection, double Transmission) InterfaceFields(
        BranchState state, Vector3D normal, Vector3D reflection, Vector3D transmission, IMaterial before, IMaterial after,
        bool tir, ICoatingModel? coating = null, bool reverse = false)
    {
        if (state.Field is not { } field) throw new InvalidOperationException("Missing polarization state.");
        var wave = state.Ray.WavelengthNanometers; var n1 = before.RefractiveIndex(wave); var n2 = after.RefractiveIndex(wave);
        var k1 = before.ExtinctionCoefficient(wave); var k2 = after.ExtinctionCoefficient(wave);
        if (k1 / n1 > 1e-6 || k2 / n2 > 1e-6) throw new NotSupportedException("Strongly absorbing/inhomogeneous Jones ray transport needs a vector wave model.");
        var context = new SurfaceInteractionContext(normal, n1, n2, wave, false, IncidentDirection: state.Ray.Direction,
            ExtinctionCoefficientBefore: k1, ExtinctionCoefficientAfter: k2);
        Complex rs, rp, ts, tp;
        if (coating is null or NoneCoatingModel)
        {
            var f = FresnelPower.EvaluateHomogeneousPower(new(n1, k1), new(n2, k2), Math.Clamp(Math.Abs(Dot(state.Ray.Direction, normal)), 1e-15, 1));
            rs = f.ReflectionS; rp = f.ReflectionP; ts = f.TransmissionS; tp = f.TransmissionP;
        }
        else if (coating is CoherentMultilayerCoating film)
        {
            if (reverse) film = new(film.Layers.Reverse());
            var r = film.EvaluateJones(context with { IsReflective = true }); rs = r.S; rp = r.P;
            var t = tir ? new CoatingJonesPower(Complex.Zero, Complex.Zero) : film.EvaluateJones(context); ts = t.S; tp = t.P;
        }
        else throw new NotSupportedException("This coating supplies power only, not physical Jones amplitudes/phases.");
        var rState = field.Apply(state.Ray.Direction, reflection, normal, rs, rp);
        var tState = tir ? (State: field, Fraction: 0d) : field.Apply(state.Ray.Direction, transmission, normal, ts, tp);
        if (rState.Fraction + tState.Fraction > 1 + 1e-10) throw new ArithmeticException("Non-passive polarized boundary.");
        return (rState.State, tState.State, rState.Fraction, tState.Fraction);
    }
}

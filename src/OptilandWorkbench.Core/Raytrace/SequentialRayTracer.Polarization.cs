using System.Numerics;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Capabilities;
using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Coordinates;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Propagation;
using OptilandWorkbench.Core.Rays;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Raytrace;

/// <summary>Power-normalized electric field projected into the target surface's local axes.</summary>
public sealed record PolarizedRayTraceResult(RayTraceSample Sample, ComplexElectricField LocalField,
    double UnpolarizedIntensity, HarmonicTimeConvention TimeConvention, bool IncludesPropagationPhase)
{
    public double PolarizedIntensity => LocalField.SquaredNorm;
    public PolarizationInterfaceData? Interface { get; init; }
}

/// <summary>Single-interface flux-normalized coefficients, in the trace's stated time convention.</summary>
public sealed record PolarizationInterfaceData(ThinFilmAmplitude S, ThinFilmAmplitude P)
{
    internal PolarizationInterfaceData Conjugate() => new(Convert(S), Convert(P));
    private static ThinFilmAmplitude Convert(ThinFilmAmplitude value) => value with
    {
        Reflection = Complex.Conjugate(value.Reflection),
        PowerTransmission = value.PowerTransmission is { } t ? Complex.Conjugate(t) : null,
        TransmissionPhaseRadians = -value.TransmissionPhaseRadians
    };
}

public sealed partial class SequentialRayTracer
{
    /// <summary>
    /// A coherent Jones state through the formal sequential surface engine. No aperture
    /// continuation or private trace cache. Coating power is applied exactly once.
    /// A positive-time field conjugates the n+ik coating solver response and uses -k.OPL.
    /// </summary>
    public PolarizedRayTraceResult TracePolarized(RealRay sourceRay, JonesInputState input,
        int? targetSurfaceIndex = null, CoordinateSystem? inputCoordinates = null,
        HarmonicTimeConvention timeConvention = HarmonicTimeConvention.Positive,
        bool includePropagationPhase = true, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceRay);
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        ComputationCancellation.ThrowIfCancellationRequested();
        if (!sourceRay.CanTrace || !double.IsFinite(sourceRay.WavelengthNanometers) || sourceRay.WavelengthNanometers <= 0
            || !double.IsFinite(sourceRay.Intensity) || sourceRay.Intensity < 0 || sourceRay.PolarizationMatrix is not null
            || !Enum.IsDefined(timeConvention))
            throw new ArgumentException("偏振追迹需要有效光线、正波长、非负有限强度及单一 Jones 输入；不混用旧实数偏振矩阵。");
        OpticCapabilityPreflight.EnsureSupported(_optic, OpticCapabilityOperation.RayTrace);
        var surfaces = _optic.SurfaceGroup.Items;
        var target = targetSurfaceIndex ?? surfaces.Count - 1;
        if (target < 0 || target >= surfaces.Count) throw new ArgumentOutOfRangeException(nameof(targetSurfaceIndex));
        if (target == 0 && ObjectConjugate.IsInfinite(surfaces[0]))
            throw new InvalidOperationException("无穷远物面没有有限偏振测量面。");
        var normalized = sourceRay.Normalize();
        var basis = input.Resolve(inputCoordinates?.ToLocalDirection(normalized.Direction) ?? normalized.Direction);
        var transport = new UnpolarizedPowerTransport(
            inputCoordinates?.ToGlobalDirection(basis.X) ?? basis.X,
            inputCoordinates?.ToGlobalDirection(basis.Y) ?? basis.Y);
        var ray = RayState.FromRealRay(normalized);
        IMaterial material = ResolveMaterial("Air");
        double path = 0, opticalPath = 0;
        SequentialTraceMeasurement.Record(1);
        for (var index = 0; index <= target; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ComputationCancellation.ThrowIfCancellationRequested();
            var surface = surfaces[index];
            if (material.PropagationModel is not HomogeneousPropagationModel
                || surface.MaterialAfter.PropagationModel is not HomogeneousPropagationModel)
                throw new NotSupportedException("GRIN 连续偏振运输尚未实现，不能用直线 Jones 链替代。");
            var result = TraceSequentialSurface(surface, index, ray, material, path, opticalPath,
                bypassCoating: surface.CoatingModel is CoherentMultilayerCoating);
            var sample = result.Sample;
            PolarizationInterfaceData? interfaceData = null;
            if (sample.Vignetted || result.StopTracing)
                throw new InvalidOperationException($"偏振光线未有效到达表面 {surface.Number}，包括失交或孔径渐晕。");
            if (sample.InteractionKind is { } kind)
            {
                var response = EvaluatePolarizationInterface(surface, sample, material, sourceRay.WavelengthNanometers, kind);
                var conjugate = timeConvention == HarmonicTimeConvention.Positive;
                interfaceData = conjugate ? response.Data.Conjugate() : response.Data;
                transport.Apply(response.Incoming, response.Outgoing, response.Normal,
                    conjugate ? Complex.Conjugate(response.S) : response.S,
                    conjugate ? Complex.Conjugate(response.P) : response.P);
                if (includePropagationPhase)
                {
                    var wavelength = sourceRay.WavelengthNanometers / 1e6;
                    var phase = 2 * Math.PI * Math.IEEERemainder(sample.SegmentOpticalPathLength, wavelength) / wavelength;
                    transport.ApplyCommonPhase(Complex.FromPolarCoordinates(1, conjugate ? -phase : phase));
                }
            }
            if (index == target)
            {
                var field = transport.Combine(basis.Jx, basis.Jy) * Math.Sqrt(sample.Intensity);
                var frame = surface.CoordinateSystem;
                var local = new ComplexElectricField(field.Dot(frame.ToGlobalDirection(new(1, 0, 0))),
                    field.Dot(frame.ToGlobalDirection(new(0, 1, 0))), field.Dot(frame.ToGlobalDirection(new(0, 0, 1))));
                if (!double.IsFinite(local.SquaredNorm)) throw new ArithmeticException("偏振电场不是有限值。");
                return new(sample.ToRayTraceSample(), local, sample.Intensity * transport.Power, timeConvention, includePropagationPhase)
                { Interface = interfaceData };
            }
            ray = result.Ray;
            material = result.OutgoingMaterial;
            path = result.CumulativePathLength;
            opticalPath = result.CumulativeOpticalPathLength;
        }
        throw new InvalidOperationException("没有偏振追迹结果。");
    }

    private readonly record struct InterfaceJonesTransform(Vector3D Incoming, Vector3D Outgoing,
        Vector3D Normal, Complex S, Complex P, PolarizationInterfaceData Data);

    private static InterfaceJonesTransform EvaluatePolarizationInterface(OpticalSurface surface,
        RayTraceSampleValue sample, IMaterial incidentMaterial, double wavelength, RayInteractionKind kind)
    {
        if (surface.ScatteringModel is not null
            || surface.InteractionModel is not RefractiveReflectiveInteractionModel
            || surface.Geometry is OptilandWorkbench.Core.Geometries.IGratingGeometry
            || incidentMaterial.PropagationModel is not HomogeneousPropagationModel
            || surface.MaterialAfter.PropagationModel is not HomogeneousPropagationModel)
            throw new NotSupportedException("Polarized illumination requires deterministic isotropic homogeneous media and standard interfaces.");
        if (surface.CoatingModel is not (NoneCoatingModel or SimpleCoatingModel or CoherentMultilayerCoating))
            throw new NotSupportedException("This coating does not supply physical polarization amplitudes.");

        var n1 = sample.RefractiveIndexBefore
            ?? throw new InvalidOperationException("Polarization transport requires the traced incident index.");
        var n2 = sample.RefractiveIndexAfter
            ?? throw new InvalidOperationException("Polarization transport requires the traced interface index.");
        var incoming = sample.IncidentDirection
            ?? throw new InvalidOperationException("Polarization transport requires traced incident direction metadata.");
        var normal = surface.CoordinateSystem.ToGlobalDirection(
            surface.Geometry.SurfaceNormal(surface.CoordinateSystem.ToLocalPoint(sample.Position)));
        if (surface.CoatingModel is CoherentMultilayerCoating coherent)
        {
            var response = coherent.EvaluateAmplitudes(new SurfaceInteractionContext(normal,
                n1, n2,
                wavelength, kind is RayInteractionKind.Reflected or RayInteractionKind.TotalInternalReflection,
                surface.Geometry, incoming, incidentMaterial.ExtinctionCoefficient(wavelength),
                surface.MaterialAfter.ExtinctionCoefficient(wavelength)));
            var reflection = kind is RayInteractionKind.Reflected or RayInteractionKind.TotalInternalReflection;
            var s = reflection ? response.S.Reflection : response.S.PowerTransmission
                ?? throw new NotSupportedException("吸收基底中的透射复电场尚未支持。");
            var p = reflection ? response.P.Reflection : response.P.PowerTransmission
                ?? throw new NotSupportedException("吸收基底中的透射复电场尚未支持。");
            return new(incoming, sample.Direction, normal, s, p, new(response.S, response.P));
        }
        if (incidentMaterial.ExtinctionCoefficient(wavelength) != 0
            || surface.MaterialAfter.ExtinctionCoefficient(wavelength) != 0)
            throw new NotSupportedException("Absorbing interface polarization requires complex-index power transport, which is not implemented.");
        var cosine = Math.Clamp(Math.Abs(VectorDot(incoming, normal) / (incoming.Length * normal.Length)), 0, 1);
        // Explicit scalar coatings already attenuate the ray. Their declared
        // non-polarizing response replaces bare-interface loss; never apply it twice.
        // A commanded mirror without a coating is an ideal reflector. TIR is
        // distinct and must retain the dielectric reflection phase.
        if (surface.CoatingModel is SimpleCoatingModel || kind == RayInteractionKind.Reflected)
        {
            var (reflectance, transmittance) = surface.CoatingModel is SimpleCoatingModel scalar
                ? (scalar.Reflectance, scalar.Transmittance) : (1.0, 0.0);
            if (!double.IsFinite(reflectance) || !double.IsFinite(transmittance)
                || reflectance < 0 || transmittance < 0 || reflectance + transmittance > 1 + 1e-12)
                throw new InvalidOperationException("标量膜层需要非负有限 R/T 且 R+T<=1。");
            var r = Math.Sqrt(reflectance); var t = Math.Sqrt(transmittance);
            return new(incoming, sample.Direction, normal,
                kind == RayInteractionKind.Transmitted ? 1 : -1, 1, new(Amplitude(-r, t), Amplitude(r, t)));
        }
        var fresnel = FresnelPower.Evaluate(n1, n2, cosine);
        if ((kind == RayInteractionKind.TotalInternalReflection) != fresnel.TotalInternalReflection)
            throw new InvalidOperationException("Traced interaction and Fresnel propagation disagree.");
        return new(incoming, sample.Direction, normal,
            fresnel.TotalInternalReflection ? fresnel.ReflectionS : fresnel.TransmissionS,
            fresnel.TotalInternalReflection ? fresnel.ReflectionP : fresnel.TransmissionP,
            new(Amplitude(fresnel.ReflectionS, fresnel.TransmissionS), Amplitude(fresnel.ReflectionP, fresnel.TransmissionP)));

        static ThinFilmAmplitude Amplitude(Complex r, Complex t)
        {
            var rp = r.Magnitude * r.Magnitude; var tp = t.Magnitude * t.Magnitude;
            return new(r, t, new(rp, tp, Math.Max(0, 1 - rp - tp), Math.Log(tp)), t.Phase);
        }
    }

}

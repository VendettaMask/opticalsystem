using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Serialization;

namespace OpticalSystemDesign.Core.Differentiation;

public enum OpticalDesignParameterKind { Curvature, Thickness, Conic, EvenAsphereCoefficient, MaterialIndex, MaterialAbbe }

/// <summary>The physical quantity equals the optimizer coordinate times Scale.</summary>
public sealed record OpticalDesignParameter(int SurfaceIndex, OpticalDesignParameterKind Kind, double Scale = 1, int CoefficientIndex = 0,
    int? LinkedSurfaceIndex = null);

/// <summary>Core-owned parameter binding. No module needs geometry or dispersion equations.</summary>
public static class OpticalDesignParameters
{
    public static double[] Read(Optic optic, IReadOnlyList<OpticalDesignParameter> parameters)
    {
        Validate(optic, parameters);
        var physical = parameters.Select(parameter => ReadPhysical(optic, parameter)).ToArray();
        var coordinates = parameters.Select((parameter, index) => physical[index] / parameter.Scale).ToArray();
        if (coordinates.Where((value, index) => !double.IsFinite(value) || value == 0 && physical[index] != 0).Any())
            throw new ArgumentException("The physical parameters cannot be represented in the requested optimizer coordinates.", nameof(parameters));
        return coordinates;
    }

    public static Optic Apply(OpticSnapshot template, IReadOnlyList<OpticalDesignParameter> parameters, IReadOnlyList<double> values)
    {
        if (parameters.Count != values.Count || values.Any(value => !double.IsFinite(value)))
            throw new ArgumentException("Invalid design coordinate vector.");
        var optic = Optic.FromSnapshot(template);
        Validate(optic, parameters);
        // Rebuild each component once: changing legacy radius/conic can otherwise erase an asphere.
        foreach (var group in Expand(parameters).GroupBy(item => item.parameter.SurfaceIndex))
        {
            var surface = optic.SurfaceGroup.Items[group.Key];
            var (curvature, conic, coefficients) = Profile(surface.Geometry);
            var material = surface.MaterialAfter;
            var hasMaterial = group.Any(item => item.parameter.Kind is OpticalDesignParameterKind.MaterialIndex or OpticalDesignParameterKind.MaterialAbbe);
            var (nd, vd) = hasMaterial ? RelaxedGlassCoordinates(material) : (0.0, 0.0);
            foreach (var (parameter, index) in group)
            {
                var value = values[index] * parameter.Scale;
                if (!double.IsFinite(value) || value == 0 && values[index] != 0
                    || parameter.Kind == OpticalDesignParameterKind.Curvature && value != 0
                    && (!double.IsFinite(1 / value) || Curvature(1 / value) == 0))
                    throw new ArgumentException("The optimizer coordinate cannot be represented by the physical surface parameter.", nameof(values));
                switch (parameter.Kind)
                {
                    case OpticalDesignParameterKind.Curvature: curvature = value; break;
                    case OpticalDesignParameterKind.Conic: conic = value; break;
                    case OpticalDesignParameterKind.EvenAsphereCoefficient: coefficients[parameter.CoefficientIndex] = value; break;
                    case OpticalDesignParameterKind.Thickness: surface.Thickness = value; break;
                    case OpticalDesignParameterKind.MaterialIndex: nd = value; break;
                    case OpticalDesignParameterKind.MaterialAbbe: vd = value; break;
                }
            }
            surface.Geometry = coefficients.Length == 0
                ? new StandardGeometry(curvature == 0 ? 0 : 1 / curvature, conic)
                : new EvenAsphereGeometry(curvature == 0 ? 0 : 1 / curvature, conic, coefficients);
            if (hasMaterial) surface.MaterialAfter = CreateRelaxedGlass(material.Name, nd, vd);
        }
        optic.SurfaceGroup.Renumber();
        SynchronizeIncidentMedia(optic);
        optic.InvalidateRayTraceCache();
        return optic;
    }

    /// <summary>Bind incident-medium metadata to the same directed sequence used by formal refractive tracing.</summary>
    public static void SynchronizeIncidentMedia(Optic optic)
    {
        IMaterial medium = new AirMaterial();
        foreach (var surface in optic.SurfaceGroup.Items)
        {
            if (surface.IsReflective) throw new NotSupportedException("Refractive design medium binding does not support reflections.");
            surface.MaterialBefore = medium.Clone();
            medium = surface.MaterialAfter;
        }
        optic.InvalidateRayTraceCache();
    }

    /// <summary>AutoLens's optimizable two-term Cauchy model, not a real catalog glass.</summary>
    public static CauchyMaterial CreateRelaxedGlass(string name, double nd, double vd)
    {
        if (!double.IsFinite(nd) || nd <= 1 || !double.IsFinite(vd) || vd <= 0)
            throw new ArgumentOutOfRangeException(nameof(nd));
        var b = (nd - 1) / vd / (1 / (.486 * .486) - 1 / (.656 * .656));
        return new CauchyMaterial(name, nd - b / (.587 * .587), b);
    }

    public static (double Nd, double Vd) RelaxedGlassCoordinates(IMaterial material)
    {
        var nd = material.RefractiveIndex(587);
        var dispersion = material.RefractiveIndex(486) - material.RefractiveIndex(656);
        if (!double.IsFinite(nd) || nd <= 1 || !double.IsFinite(dispersion) || dispersion <= 0)
            throw new NotSupportedException("Continuous glass optimization requires finite normal dispersion.");
        return (nd, (nd - 1) / dispersion);
    }

    /// <summary>Shared geometry-based shape feature, not an optical quality or acceptance metric.</summary>
    public static double? NormalizedProfileDifference(SurfaceSnapshot first, SurfaceSnapshot second)
    {
        var radius = Math.Min(first.MechanicalSemiDiameter ?? first.SemiDiameter, second.MechanicalSemiDiameter ?? second.SemiDiameter);
        if (!double.IsFinite(radius) || radius <= 0) return null;
        var a = ComponentSnapshotFactory.ToGeometry(first.Components?.Geometry, first.Radius, first.Conic);
        var b = ComponentSnapshotFactory.ToGeometry(second.Components?.Geometry, second.Radius, second.Conic);
        var sum = 0.0;
        for (var sample = 1; sample <= 4; sample++)
        {
            var radial = radius * sample / 4;
            var difference = (a.Sag(radial, 0) - b.Sag(radial, 0)) / radius;
            if (!double.IsFinite(difference)) return null;
            sum += difference * difference / 4;
        }
        return sum;
    }

    internal static (double Curvature, double Conic, double[] Coefficients) Profile(IGeometry geometry) => geometry switch
    {
        PlaneGeometry => (0, 0, []),
        StandardGeometry standard => (Curvature(standard.Radius), standard.Conic, []),
        EvenAsphereGeometry asphere => (Curvature(asphere.Base.Radius), asphere.Base.Conic, asphere.Coefficients.ToArray()),
        _ => throw new NotSupportedException($"Design derivatives do not support {geometry.Kind} geometry.")
    };

    internal static double Curvature(double radius) => Math.Abs(radius) < 1e-12 || double.IsInfinity(radius) ? 0 : 1 / radius;

    internal static void Validate(Optic optic, IReadOnlyList<OpticalDesignParameter> parameters)
    {
        if (parameters.Count == 0 || parameters.Count > 512 || parameters.Any(parameter =>
                parameter.SurfaceIndex <= 0 || parameter.SurfaceIndex >= optic.SurfaceGroup.Items.Count
                || !Enum.IsDefined(parameter.Kind) || !double.IsFinite(parameter.Scale) || parameter.Scale <= 0
                || parameter.CoefficientIndex < 0 || parameter.Kind != OpticalDesignParameterKind.EvenAsphereCoefficient && parameter.CoefficientIndex != 0)
            || parameters.Any(parameter => parameter.LinkedSurfaceIndex is { } linked && (linked <= 0 || linked >= optic.SurfaceGroup.Items.Count || linked == parameter.SurfaceIndex))
            || Expand(parameters).Select(item => (item.parameter.SurfaceIndex, item.parameter.Kind, item.parameter.CoefficientIndex)).Distinct().Count() != Expand(parameters).Count())
            throw new ArgumentException("Invalid, duplicate or excessive design parameters.", nameof(parameters));
        foreach (var (parameter, _) in Expand(parameters))
        {
            var surface = optic.SurfaceGroup.Items[parameter.SurfaceIndex];
            var profile = Profile(surface.Geometry);
            if (parameter.Kind == OpticalDesignParameterKind.EvenAsphereCoefficient
                && (parameter.CoefficientIndex < 0 || parameter.CoefficientIndex >= profile.Coefficients.Length))
                throw new ArgumentOutOfRangeException(nameof(parameters), "Asphere coefficient is not present in the template.");
            if (parameter.Kind is OpticalDesignParameterKind.MaterialIndex or OpticalDesignParameterKind.MaterialAbbe
                && surface.MaterialAfter is not CauchyMaterial { C: 0 })
                throw new NotSupportedException("Material tangents require an explicitly relaxed two-term Cauchy medium.");
            if (parameter.Kind == OpticalDesignParameterKind.Thickness && !double.IsFinite(surface.Thickness))
                throw new NotSupportedException("An infinite conjugate is not an optimization coordinate.");
        }
        foreach (var parameter in parameters)
            if (parameter.LinkedSurfaceIndex is { } linked
                && ReadPhysical(optic, parameter) != ReadPhysical(optic, parameter with { SurfaceIndex = linked, LinkedSurfaceIndex = null }))
                throw new ArgumentException("Linked design parameters must describe the same nominal physical value.", nameof(parameters));
    }

    private static IEnumerable<(OpticalDesignParameter parameter, int index)> Expand(IReadOnlyList<OpticalDesignParameter> parameters)
    {
        for (var index = 0; index < parameters.Count; index++)
        {
            var parameter = parameters[index];
            yield return (parameter, index);
            if (parameter.LinkedSurfaceIndex is { } linked) yield return (parameter with { SurfaceIndex = linked, LinkedSurfaceIndex = null }, index);
        }
    }

    private static double ReadPhysical(Optic optic, OpticalDesignParameter parameter)
    {
        if (!double.IsFinite(parameter.Scale) || parameter.Scale <= 0) throw new ArgumentOutOfRangeException(nameof(parameter));
        var surface = optic.SurfaceGroup.Items[parameter.SurfaceIndex];
        var profile = Profile(surface.Geometry);
        return parameter.Kind switch
        {
            OpticalDesignParameterKind.Curvature => profile.Curvature,
            OpticalDesignParameterKind.Conic => profile.Conic,
            OpticalDesignParameterKind.EvenAsphereCoefficient => profile.Coefficients[parameter.CoefficientIndex],
            OpticalDesignParameterKind.Thickness => surface.Thickness,
            OpticalDesignParameterKind.MaterialIndex => RelaxedGlassCoordinates(surface.MaterialAfter).Nd,
            OpticalDesignParameterKind.MaterialAbbe => RelaxedGlassCoordinates(surface.MaterialAfter).Vd,
            _ => throw new ArgumentOutOfRangeException(nameof(parameter))
        };
    }
}

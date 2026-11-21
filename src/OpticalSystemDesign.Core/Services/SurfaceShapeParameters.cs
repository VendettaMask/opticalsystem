using System.Globalization;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Optimization;
using OpticalSystemDesign.Core.Serialization;

namespace OpticalSystemDesign.Core.Services;

/// <summary>Stable identities for conic and immutable surface polynomial coefficients.</summary>
public static partial class SurfaceShapeParameters
{
    public static bool IsVariableName(string? name) => name == "conic" || IsAdditionalVariableName(name) || name is { Length: > 1 }
        && name[0] is 'c' or 'a' or 'q' or 'z' or 'i' or 'o'
        && int.TryParse(name.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var index)
        && index is >= 0 and < 4096 && name == $"{name[0]}{index}";

    public static IReadOnlyList<string> CoefficientNames(OpticalSurface surface)
    {
        if (surface.Geometry is ConcentricZoneGeometry zones)
            return Names('i', zones.Inner.Coefficients.Count).Concat(Names('o', zones.Outer.Coefficients.Count)).Concat(AdditionalNames(surface)).ToArray();
        var (prefix, values) = Coefficients(surface.Geometry);
        var names = Names(prefix, values.Count);
        if (surface.Geometry is FringeSagGeometry fringe) names = names.Concat(Names('z', fringe.FringeCoefficients.Count));
        return names.Concat(AdditionalNames(surface)).ToArray();
    }

    private static IEnumerable<string> Names(char prefix, int count) => Enumerable.Range(0, count).Select(index => $"{prefix}{index}");

    private static (char Prefix, IReadOnlyList<double> Values) Coefficients(IGeometry geometry) => geometry switch
    {
        EvenAsphereGeometry even => ('c', even.Coefficients),
        OddAsphereGeometry odd => ('c', odd.Coefficients),
        NormalizedPolynomialGeometry polynomial => ('c', polynomial.Coefficients),
        FringeSagGeometry fringe => ('a', fringe.Base.Coefficients),
        GridSagGeometry grid => ('a', grid.Base.Coefficients),
        ToroidalGeometry toroidal => ('a', toroidal.Coefficients),
        ForbesQGeometry forbes => ('q', forbes.QCoefficients),
        ForbesNormalSlopeGeometry forbes => ('q', forbes.Coefficients),
        ChebyshevSagGeometry chebyshev => ('c', chebyshev.NormalizedCoefficients),
        _ => ('\0', Array.Empty<double>())
    };

    public static double Read(OpticalSurface surface, string parameter)
    {
        if (parameter == "conic") return surface.Conic;
        if (IsAdditionalVariableName(parameter)) return ReadAdditional(surface, parameter);
        var (prefix, values) = surface.Geometry switch
        {
            FringeSagGeometry fringe when parameter.StartsWith('z') => ('z', fringe.FringeCoefficients),
            ConcentricZoneGeometry zones when parameter.StartsWith('i') => ('i', zones.Inner.Coefficients),
            ConcentricZoneGeometry zones when parameter.StartsWith('o') => ('o', zones.Outer.Coefficients),
            _ => Coefficients(surface.Geometry)
        };
        if (!IsVariableName(parameter) || parameter[0] != prefix
            || !int.TryParse(parameter.AsSpan(1), out var index) || index >= values.Count)
            throw new ArgumentException($"Surface {surface.Number} has no editable shape coefficient '{parameter}'.", nameof(parameter));
        var value = values[index];
        return double.IsFinite(value) ? value : throw new ArgumentException("Shape coefficient must be finite.", nameof(parameter));
    }

    public static void Write(OpticalSurface surface, string parameter, double value)
    {
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        _ = Read(surface, parameter);
        if (parameter == "conic") { surface.Conic = value; return; }
        if (IsAdditionalVariableName(parameter)) { WriteAdditional(surface, parameter, value); return; }
        if (surface.Geometry is ConcentricZoneGeometry zones)
        {
            var inner = zones.Inner.Coefficients.ToArray(); var outer = zones.Outer.Coefficients.ToArray();
            (parameter[0] == 'i' ? inner : outer)[int.Parse(parameter.AsSpan(1), CultureInfo.InvariantCulture)] = value;
            surface.Geometry = new ConcentricZoneGeometry(zones.Inner.Base.Radius, zones.Inner.Base.Conic, inner,
                zones.Outer.Base.Radius, zones.Outer.Base.Conic, outer, zones.BoundaryRadius, zones.BreakSag);
            return;
        }
        var snapshot = ComponentSnapshotFactory.FromGeometry(surface.Geometry);
        var numbers = new Dictionary<string, double>(snapshot.Numbers) { [parameter] = value };
        surface.Geometry = ComponentSnapshotFactory.ToGeometry(snapshot with { Numbers = numbers }, surface.Radius, surface.Conic);
    }

    public static string Property(string parameter) => parameter == "conic" ? "conic" : $"shape:{parameter}";
    public static bool IsShapeProperty(string? property) => property is not null
        && property.StartsWith("shape:", StringComparison.Ordinal) && property[6..] != "conic" && IsVariableName(property[6..]);

    public static int ZemaxParameterNumber(OpticalSurface surface, string parameter)
    {
        _ = Read(surface, parameter);
        if (parameter is "r0" or "r1" or "b0" or "t0" or "t1") return AdditionalNativeField(parameter).Number;
        var index = int.Parse(parameter.AsSpan(1), CultureInfo.InvariantCulture);
        return surface.Geometry switch
        {
            EvenAsphereGeometry or OddAsphereGeometry or FringeSagGeometry or GridSagGeometry => index + 1,
            NormalizedPolynomialGeometry => index + 15,
            ForbesNormalSlopeGeometry => index + 16,
            ToroidalGeometry => index + 2,
            _ => throw new NotSupportedException("This shape coefficient has no ZMX parameter mapping.")
        };
    }

    /// <summary>Native extra-data variability is carried inline by XDAT.</summary>
    public static (bool ExtraData, int Number) ZemaxVariableField(OpticalSurface surface, string parameter)
    {
        _ = Read(surface, parameter);
        if (IsAdditionalVariableName(parameter)) return AdditionalNativeField(parameter);
        var index = int.Parse(parameter.AsSpan(1), CultureInfo.InvariantCulture);
        return surface.Geometry switch
        {
            NormalizedPolynomialGeometry => (true, index + 3),
            ForbesNormalSlopeGeometry => (true, index + 4),
            FringeSagGeometry when parameter[0] == 'z' => (true, index + 3),
            ChebyshevSagGeometry => (true, index + 1),
            ConcentricZoneGeometry => (true, 2 + 4 * index + (parameter[0] == 'i' ? 0 : 2)),
            _ => (false, ZemaxParameterNumber(surface, parameter))
        };
    }

    public static string Label(OpticalSurface surface, string parameter)
    {
        if (parameter == "conic") return "Conic";
        if (IsAdditionalVariableName(parameter)) return AdditionalLabel(parameter);
        var index = int.Parse(parameter.AsSpan(1), CultureInfo.InvariantCulture);
        return surface.Geometry switch
        {
            EvenAsphereGeometry => $"A{2 * (index + 1)}",
            OddAsphereGeometry => $"A{index + 1}",
            _ => parameter
        };
    }

    public static IOptimizationVariable CreateVariable(OpticalSurface surface, string parameter, Action changed)
    {
        var initial = Read(surface, parameter);
        var power = parameter == "conic" ? 0 : surface.Geometry switch
        {
            EvenAsphereGeometry => 2 * (int.Parse(parameter.AsSpan(1), CultureInfo.InvariantCulture) + 1),
            OddAsphereGeometry => int.Parse(parameter.AsSpan(1), CultureInfo.InvariantCulture) + 1,
            _ => 0
        };
        // Search scaling from coefficient magnitude and declared aperture; no optical approximation.
        var extent = parameter == "conic" ? Math.Max(1, 2 * Math.Abs(initial))
            : initial != 0 ? 2 * Math.Abs(initial) : .01 / Math.Pow(Math.Max(1, surface.SemiDiameter), power);
        extent = Math.Max(1e-100, extent);
        var (lower, upper) = AdditionalBounds(surface, parameter, initial - extent, initial + extent);
        return new DelegateVariable($"表面 {surface.Number} {Label(surface, parameter)}", () => Read(surface, parameter),
            value => { Write(surface, parameter, value); changed(); }, lower, upper,
            Math.Min(extent * .05, (upper - lower) * .05), new UnitRangeScaler(lower, upper));
    }
}

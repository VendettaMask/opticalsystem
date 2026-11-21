using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Phase;

namespace OpticalSystemDesign.Core.Services;

public static partial class SurfaceShapeParameters
{
    public static bool IsAdditionalVariableName(string? key) => key is "r0" or "r1" or "b0" or "t0" or "t1" or "f0" or "f1" or "f2" or "f3" or "f4"
        || key is { Length: > 1 } && key[0] is 'p' or 's'
        && int.TryParse(key.AsSpan(1), out var index) && index is >= 0 and < 60 && key == $"{key[0]}{index}";

    private static IEnumerable<string> AdditionalNames(OpticalSurface surface)
    {
        if (surface.Placement?.Break is not null) foreach (var key in Names('f', 5)) yield return key;
        if (surface.Geometry is TiltedPlaneGeometry) { yield return "t0"; yield return "t1"; }
        if (surface.Geometry is not ConcentricZoneGeometry) yield break;
        yield return "r0"; yield return "r1"; yield return "b0";
        if (surface.InteractionModel is not DiffractivePhaseInteractionModel { Profile: DualZoneRadialPhaseProfile phase }) yield break;
        foreach (var key in Names('p', phase.Inner.Coefficients.Count).Concat(Names('s', phase.Outer.Coefficients.Count))) yield return key;
    }

    private static double ReadAdditional(OpticalSurface surface, string key)
    {
        if (!AdditionalNames(surface).Contains(key)) throw new ArgumentException($"Surface {surface.Number} has no parameter '{key}'.");
        if (key[0] == 'f')
        {
            var frame = surface.Placement!.Break!;
            return key switch { "f0" => frame.X, "f1" => frame.Y, "f2" => frame.TiltX, "f3" => frame.TiltY, _ => frame.TiltZ };
        }
        if (surface.Geometry is TiltedPlaneGeometry tilted) return key == "t0" ? tilted.TangentX : tilted.TangentY;
        var zones = (ConcentricZoneGeometry)surface.Geometry;
        if (key == "r0") return zones.Outer.Base.Radius;
        if (key == "r1") return zones.Outer.Base.Conic;
        if (key == "b0") return zones.BoundaryRadius;
        var phase = (DualZoneRadialPhaseProfile)((DiffractivePhaseInteractionModel)surface.InteractionModel).Profile;
        return (key[0] == 'p' ? phase.Inner : phase.Outer).Coefficients[int.Parse(key.AsSpan(1))];
    }

    private static void WriteAdditional(OpticalSurface surface, string key, double value)
    {
        _ = ReadAdditional(surface, key);
        if (key[0] == 'f')
        {
            var frame = surface.Placement!.Break!;
            surface.Placement = surface.Placement with
            {
                Break = key switch
                {
                    "f0" => frame with { X = value },
                    "f1" => frame with { Y = value },
                    "f2" => frame with { TiltX = value },
                    "f3" => frame with { TiltY = value },
                    _ => frame with { TiltZ = value }
                }
            };
            return;
        }
        if (surface.Geometry is TiltedPlaneGeometry tilted)
        {
            surface.Geometry = new TiltedPlaneGeometry(key == "t0" ? value : tilted.TangentX, key == "t1" ? value : tilted.TangentY);
            return;
        }
        var zones = (ConcentricZoneGeometry)surface.Geometry;
        var interaction = surface.InteractionModel as DiffractivePhaseInteractionModel;
        var phase = interaction?.Profile as DualZoneRadialPhaseProfile;
        var geometry = new ConcentricZoneGeometry(zones.Inner.Base.Radius, zones.Inner.Base.Conic, zones.Inner.Coefficients,
            key == "r0" ? value : zones.Outer.Base.Radius, key == "r1" ? value : zones.Outer.Base.Conic, zones.Outer.Coefficients,
            key == "b0" ? value : zones.BoundaryRadius, zones.BreakSag);
        IInteractionModel? replacement = null;
        if (phase is not null && (key == "b0" || key[0] is 'p' or 's'))
        {
            var inner = phase.Inner.Coefficients.ToArray(); var outer = phase.Outer.Coefficients.ToArray();
            if (key[0] is 'p' or 's') (key[0] == 'p' ? inner : outer)[int.Parse(key.AsSpan(1))] = value;
            replacement = new DiffractivePhaseInteractionModel(new DualZoneRadialPhaseProfile(
                key == "b0" ? value : phase.Inner.NormalizationRadius, phase.Outer.NormalizationRadius,
                inner, outer, phase.Inner.Order, phase.Outer.Order), interaction!.IsReflective);
        }
        // Construct both before installing either, so an invalid boundary cannot split geometry and phase.
        surface.Geometry = geometry;
        if (replacement is not null) surface.InteractionModel = replacement;
    }

    private static (bool ExtraData, int Number) AdditionalNativeField(string key) => key switch
    {
        "r0" => (false, 1),
        "r1" => (false, 2),
        "b0" => (false, 3),
        "t0" => (false, 1),
        "t1" => (false, 2),
        "f0" or "f1" or "f2" or "f3" or "f4" => (false, int.Parse(key.AsSpan(1)) + 1),
        _ => (true, 3 + 4 * int.Parse(key.AsSpan(1)) + (key[0] == 's' ? 2 : 0))
    };

    private static string AdditionalLabel(string key) => key switch
    {
        "r0" => "Outer zone radius",
        "r1" => "Outer zone conic",
        "b0" => "Zone boundary radius",
        "t0" => "X tangent",
        "t1" => "Y tangent",
        "f0" => "Coordinate break X",
        "f1" => "Coordinate break Y",
        "f2" => "Coordinate break tilt X",
        "f3" => "Coordinate break tilt Y",
        "f4" => "Coordinate break tilt Z",
        _ => $"{(key[0] == 'p' ? "Inner" : "Outer")} phase {int.Parse(key.AsSpan(1)) + 1}"
    };

    private static (double Lower, double Upper) AdditionalBounds(OpticalSurface surface, string key, double lower, double upper)
    {
        if (key[0] == 'f' || key == "r1")
        {
            var initial = ReadAdditional(surface, key); var extent = Math.Max(1, Math.Abs(initial) * 2);
            lower = initial - extent; upper = initial + extent;
        }
        if (key == "b0" && surface.Geometry is ConcentricZoneGeometry zones)
        {
            lower = Math.Max(1e-12, zones.BoundaryRadius * .5);
            upper = zones.BoundaryRadius * 1.5;
            if (surface.InteractionModel is DiffractivePhaseInteractionModel { Profile: DualZoneRadialPhaseProfile phase })
                upper = Math.Min(upper, phase.Outer.NormalizationRadius * (1 - 1e-12));
        }
        return (lower, upper);
    }
}

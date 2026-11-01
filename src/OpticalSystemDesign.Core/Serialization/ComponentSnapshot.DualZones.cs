using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Phase;

namespace OpticalSystemDesign.Core.Serialization;

public static partial class ComponentSnapshotFactory
{
    private static ConcentricZoneGeometry ReadConcentricZones(ComponentSnapshot snapshot)
    {
        var inner = (EvenAsphereGeometry)ToGeometry(snapshot.Children!["inner"], 0, 0);
        var outer = (EvenAsphereGeometry)ToGeometry(snapshot.Children!["outer"], 0, 0);
        return new(inner.Base.Radius, inner.Base.Conic, inner.Coefficients, outer.Base.Radius, outer.Base.Conic,
            outer.Coefficients, snapshot.Numbers["boundaryRadius"], snapshot.Numbers["breakSag"] == 1);
    }
    private static DualZoneRadialPhaseProfile ReadDualZonePhase(ComponentSnapshot snapshot)
    {
        var inner = (NormalizedRadialPhaseProfile)ToPhaseProfile(snapshot.Children!["inner"]);
        var outer = (NormalizedRadialPhaseProfile)ToPhaseProfile(snapshot.Children!["outer"]);
        return new(inner.NormalizationRadius, outer.NormalizationRadius, inner.Coefficients, outer.Coefficients, inner.Order, outer.Order);
    }
}

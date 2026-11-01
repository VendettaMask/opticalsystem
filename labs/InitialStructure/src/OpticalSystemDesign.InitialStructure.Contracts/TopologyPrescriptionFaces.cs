namespace OpticalSystemDesign.InitialStructure.Contracts;

public sealed record TopologyPrescriptionFace(string Id, double ZMillimeters, string MaterialAfter, TopologyFace Face, bool Protected);

/// <summary>Read-only prescription rows, one per physical face, including a shared glass/glass interface once.</summary>
public static class TopologyPrescriptionFaces
{
    public static IEnumerable<TopologyPrescriptionFace> Enumerate(DesignTopology topology)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in topology.Elements)
        {
            if (seen.Add(e.FrontSurfaceId)) yield return new(e.FrontSurfaceId, e.FrontZMillimeters, e.Material, e.Front, e.Protected);
            if (!seen.Add(e.BackSurfaceId)) continue;
            var next = topology.Elements.FirstOrDefault(r => r.FrontSurfaceId == e.BackSurfaceId);
            yield return new(e.BackSurfaceId, e.BackZMillimeters, next?.Material ?? "Air", e.Back, e.Protected || next?.Protected == true);
        }
    }
}

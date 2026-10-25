using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Geometries;

namespace OpticalSystemDesign.Core.FileIO;

internal static partial class ZemaxZmxReader
{
    private static string? ReadPolygonAsset(string? directory, string name)
    {
        if (directory is null || Path.GetFileName(name) != name || !name.EndsWith(".pob", StringComparison.OrdinalIgnoreCase)) return null;
        var current = new DirectoryInfo(directory);
        for (var depth = 0; current is not null && depth < 8; depth++, current = current.Parent)
            foreach (var path in new[] { Path.Combine(current.FullName, name), Path.Combine(current.FullName, "Objects", "Polygon Objects", name) })
                if (File.Exists(path))
                {
                    if (new FileInfo(path).Length > 1_048_576) throw new InvalidDataException("Polygon object exceeds resource budget.");
                    return File.ReadAllText(path);
                }
        return null;
    }

    private static PolygonGroupGeometry CreatePolygonGroup(ZemaxSurface source, Optic optic, IReadOnlyList<string> catalogs)
    {
        if (source.PolygonObjectCount != 1 || !source.PolygonIsVolume)
            throw new NotSupportedException("A sequential polygon group requires one closed volume object; multiple objects and open shells are not supported.");
        if (!double.IsFinite(source.PolygonScale) || source.PolygonScale <= 0 || source.PolygonPose.Any(v => !double.IsFinite(v)))
            throw new InvalidDataException("Polygon pose and positive scale must be finite.");
        var vertices = new Dictionary<int, Vector3D>();
        var faces = new List<PolygonGroupGeometry.Face>();
        var port = new Vector3D(source.Parameter(0), source.Parameter(1), source.Parameter(2));
        var portRotation = RotationMatrix(source.Parameter(3),source.Parameter(4),source.Parameter(5));
        var pose = source.PolygonPose;
        var objectRotation = RotationMatrix(pose[3], pose[4], pose[5]);
        var objectPosition = new Vector3D(pose[0],pose[1],pose[2]);
        foreach (var line in source.PolygonPayload!.Split('\n'))
        {
            var t = line.Split('!',2)[0].Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
            if(t.Length == 0) continue;
            if(t[0] == "V" && t.Length == 5)
            {
                var v = new Vector3D(RequiredDouble(t,2,"V"),RequiredDouble(t,3,"V"),RequiredDouble(t,4,"V"));
                vertices.Add(RequiredInt(t,1,"V"), portRotation.Transpose().Transform(objectPosition + objectRotation.Transform(v * source.PolygonScale) - port));
            }
            else if(t[0] is "T" or "R")
            {
                var count = t[0] == "T" ? 3 : 4;
                if(t.Length != count+2 && t.Length != count+3) throw new InvalidDataException("Unsupported polygon face record.");
                var type=RequiredInt(t,count+1,t[0]);
                if(type is < -1 or > 1 || (t.Length == count+3 && RequiredInt(t,count+2,t[0]) < 0))
                    throw new InvalidDataException("Invalid polygon interaction flag or face group.");
                faces.Add(new(Enumerable.Range(1,count).Select(i=>vertices[RequiredInt(t,i,t[0])]).ToArray(),type<0,type>0));
            }
            else if(t[0] is "I" or "C" or "N") continue; // Cosmetic edge/name/group labels.
            else throw new NotSupportedException("Unsupported polygon object record: "+t[0]);
            if(vertices.Count > 16384 || faces.Count > 4096) throw new InvalidDataException("Polygon object exceeds geometry budget.");
        }
        return new PolygonGroupGeometry(faces, optic.Materials.Resolve(source.PolygonMaterial));
    }
}

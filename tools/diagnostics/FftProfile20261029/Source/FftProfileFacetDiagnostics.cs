using System.IO.Compression;
using System.Text.Json;
using System.Reflection;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Serialization;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

internal static class FftProfileFacetDiagnostics
{
    internal static object CapturePaths(Optic optic)
    {
        var paths = new List<object>();
        var observer = typeof(PolygonGroupGeometry).GetProperty("DiagnosticObserver", BindingFlags.Static|BindingFlags.NonPublic)!;
        try
        {
            foreach (var (x,y) in new[] { (0d,0d),(-.001,0d),(.001,0d),(0d,-.001),(0d,.001) })
            {
                var events = new List<object>();
                observer.SetValue(null, (Action<object>)(e => events.Add(e)));
                var launches = optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(0,0,
                    optic.Wavelengths[1].Micrometers,[new PupilSample(x,y,1)],optic.RayAimingEnabled);
                var ray = optic.SequentialRayTracer.TraceFinalSamples(launches).Single();
                paths.Add(new { x,y,events,ray });
            }
        }
        finally { observer.SetValue(null,null); }
        return new { faces=optic.SurfaceGroup.Items.Select(s => s.Geometry).OfType<PolygonGroupGeometry>().Single().Faces,paths };
    }

    internal static object? Inspect(Optic source, int configuration)
    {
        if (Environment.GetEnvironmentVariable("OPTICAL_FFT_PROFILE_FACET_DIAGNOSTICS") != "1") return null;
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "OpticalSystemDesign.slnx"))) root = root.Parent;
        using var zip = ZipFile.OpenRead(Path.Combine(root!.FullName, "validation/rechecks/2026-10-27/afocal-reference-focus/evidence.zip"));
        var id = configuration == 1 ? "0065-003-binocular_system-c1-primary-f1-w2-encircled-energy" : "0095-003-binocular_system-c2-primary-f1-w2-encircled-energy";
        var prefix = "artifacts/validation/afocal-reference-focus-20261027/native-centroid-rays/"+id+"/";
        JsonElement Read(string name)
        {
            using var stream = zip.GetEntry(prefix+name)!.Open();
            using var doc = JsonDocument.Parse(stream); return doc.RootElement.Clone();
        }
        var inputs = Read("point-reference.json").GetProperty("inputs").EnumerateArray().ToArray();
        var expected = Read("image-geometry.json").EnumerateArray().ToArray();
        var records = new List<object>();
        foreach (var alternate in new[] { false, true })
        {
            var optic = Optic.FromSnapshot(source.ToSnapshot());
            if (alternate)
            {
                foreach (var surface in optic.SurfaceGroup.Items)
                {
                    if (surface.Geometry is not PolygonGroupGeometry geometry) continue;
                    var faces = new List<PolygonGroupGeometry.Face>();
                    for (var i=0; i<geometry.Faces.Count; i++)
                    {
                        var a = geometry.Faces[i];
                        if (i+1 >= geometry.Faces.Count) { faces.Add(a); continue; }
                        var b = geometry.Faces[i+1];
                        if (a.Vertices[0] != b.Vertices[0] || a.Vertices[2] != b.Vertices[1]
                            || a.Absorbing != b.Absorbing || a.Reflective != b.Reflective)
                        { faces.Add(a); continue; }
                        faces.Add(a with { Vertices = [a.Vertices[1],a.Vertices[2],b.Vertices[2]] });
                        faces.Add(b with { Vertices = [b.Vertices[2],a.Vertices[0],a.Vertices[1]] });
                        i++;
                    }
                    surface.Geometry = new PolygonGroupGeometry(faces, geometry.VolumeMaterial);
                }
            }
            var image = optic.SurfaceGroup.Items[^1];
            var rays = new List<object>();
            foreach (var group in inputs.Select((p,i) => (Point:p,Index:i)).GroupBy(p =>
                (Hx:p.Point.GetProperty("hx").GetDouble(),Hy:p.Point.GetProperty("hy").GetDouble())))
            {
                var pupils = group.Select(p => new PupilSample(p.Point.GetProperty("px").GetDouble(),p.Point.GetProperty("py").GetDouble(),1)).ToArray();
                var launches = optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(group.Key.Hx,group.Key.Hy,
                    optic.Wavelengths[1].Micrometers,pupils,optic.RayAimingEnabled);
                var traced = optic.SequentialRayTracer.TraceFinalSamples(launches);
                foreach (var (entry, ray) in group.Zip(traced))
                {
                    if (ray is null) { rays.Add(new { entry.Index, failed=true }); continue; }
                    var position = image.CoordinateSystem.ToLocalPoint(ray.Position);
                    var direction = image.CoordinateSystem.ToLocalDirection(ray.Direction);
                    var reference = expected[entry.Index];
                    rays.Add(new { entry.Index, ray.Vignetted,
                        positionError = double.Hypot(position.X-reference.GetProperty("x").GetDouble(),position.Y-reference.GetProperty("y").GetDouble()),
                        directionError = Math.Sqrt(Math.Pow(direction.X-reference.GetProperty("l").GetDouble(),2)
                            +Math.Pow(direction.Y-reference.GetProperty("m").GetDouble(),2)+Math.Pow(direction.Z-reference.GetProperty("n").GetDouble(),2)) });
                }
            }
            records.Add(new { alternate, rays });
        }
        return records;
    }
}

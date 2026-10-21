using System.Reflection;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.ZemaxComparison;

internal static class Energy32MarginalAudit
{
    public static void Prepare(string output, bool importSource = false)
    {
        output = Path.GetFullPath(output);
        if (Directory.Exists(output)) throw new IOException("Fresh output required.");
        Directory.CreateDirectory(output);
        using var items = JsonDocument.Parse(File.ReadAllText("artifacts/validation/objectives-20261020/final-v7-intersection-fallback/items.json"));
        using var controls = JsonDocument.Parse(File.ReadAllText("artifacts/validation/objectives32-repair-20261021/coordinate-audit-v4/inputs.json"));
        var jobs = new List<object>();
        foreach (var control in controls.RootElement.EnumerateArray())
        {
            var lens = control.GetProperty("lens").GetString()!;
            var id = Path.GetFileNameWithoutExtension(lens).Replace(' ', '-').ToLowerInvariant();
            var item = items.RootElement.EnumerateArray().Single(i => i.GetProperty("lens").GetString() == lens
                && i.GetProperty("run").GetProperty("key").GetString() == "Diffraction Encircled Energy");
            var request = item.GetProperty("run").GetProperty("request").Deserialize<CanonicalAnalysisRequest>(JsonFiles.Options)!;
            var source = Path.Combine(Path.GetDirectoryName(item.GetProperty("snapshot").GetString()!)!, "../sources", lens);
            var optic = importSource ? OpticalSystemDesign.Core.FileIO.OpticalFormatCatalog.Import(File.ReadAllText(Path.GetFullPath(source)), ".zmx")
                : Optic.FromSnapshot(JsonFiles.Read<OpticSnapshot>(item.GetProperty("snapshot").GetString()!));
            var wave = optic.Wavelengths[request.Wavelength - 1];
            var directory = Path.Combine(output, id);
            Directory.CreateDirectory(directory);
            var copy = Path.Combine(directory, "source.zmx");
            File.Copy(Path.GetFullPath(source), copy);
            (double X, double Y)[] samples = [(0d, 0d), (0d, 1d), (0d, -1d), (1d, 0d), (-1d, 0d)];
            var method = optic.SequentialRayTracer.GetType().GetMethod("TraceToSurface", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var rays = samples.Select((p, index) =>
            {
                var ray = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 0, p.X, p.Y, wave.Micrometers, optic.RayAimingEnabled).Rays[0];
                var surfaces = optic.SurfaceGroup.Items;
                var launchFrame = double.IsPositiveInfinity(surfaces[0].Thickness) ? surfaces[1].CoordinateSystem : surfaces[0].CoordinateSystem;
                return new { pupilIndex = index, launch = new { origin = launchFrame.ToLocalPoint(ray.Origin), direction = launchFrame.ToLocalDirection(ray.Direction) }, rows = Enumerable.Range(1, surfaces.Count - 1).Select(surface =>
                {
                    var hit = (RayTraceSample?)method.Invoke(optic.SequentialRayTracer, [ray, surface]);
                    if (hit is null) throw new InvalidOperationException("Marginal ray missing.");
                    var frame = surfaces[surface].CoordinateSystem;
                    var position = frame.ToLocalPoint(hit.Position);
                    return new { surface, position, direction = frame.ToLocalDirection(hit.Direction),
                        incident = hit.IncidentDirection is { } incoming ? frame.ToLocalDirection(incoming) : default,
                        normal = surfaces[surface].Geometry.SurfaceNormal(position), hit.RefractiveIndexBefore, hit.RefractiveIndexAfter,
                        curvature = surfaces[surface].Geometry is OpticalSystemDesign.Core.Geometries.StandardGeometry g ? 1 / g.Radius : 0,
                        conic = surfaces[surface].Geometry is OpticalSystemDesign.Core.Geometries.StandardGeometry cg ? cg.Conic : 0 };
                }).ToArray() };
            }).ToArray();
            JsonFiles.Write(Path.Combine(directory, "core.json"), rays);
            JsonFiles.Write(Path.Combine(directory, "identity.json"), new { lens, request.Wavelength, wave.Micrometers,
                entrancePupilPosition = optic.Paraxial.EstimateEntrancePupilLocation(), entrancePupilDiameter = optic.Paraxial.EstimateEntrancePupilDiameter(),
                objectThickness = optic.SurfaceGroup.Items[0].Thickness, firstOriginZ = optic.SurfaceGroup.Items[1].CoordinateSystem.Origin.Z,
                sourceSha256 = JsonFiles.Hash(File.ReadAllBytes(copy)) });
            jobs.Add(new { id, lens, input = copy, rayAuditRequest = new { wavelength = request.Wavelength, removeVignettingFactors = false,
                surfaceRecordLimit = 1000000, rayAuditInputs = samples.Select((p, i) => new { fieldIndex = 0, pupilIndex = i, hx = 0d, hy = 0d, px = p.X, py = p.Y, weight = 1d }) } });
        }
        JsonFiles.Write(Path.Combine(output, "native-request.json"), new { zosApiPath = @"D:\Program Files\ANSYS Inc\v261\Zemax OpticStudio", jobs });
    }

    public static void Compare(string output)
    {
        using var request = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "native-request.json")));
        var records = new List<object>();
        foreach (var job in request.RootElement.GetProperty("jobs").EnumerateArray())
        {
            var id = job.GetProperty("id").GetString()!;
            using var identity = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, id, "identity.json")));
            var wave = identity.RootElement.GetProperty("wavelength").GetInt32();
            using var core = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, id, "core.json")));
            using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "native/capture", id, "ray-audit.json")));
            using var indices = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "native/capture", id, "refractive-indices.json")));
            foreach (var surface in native.RootElement.GetProperty("surfaces").EnumerateArray())
            {
                var number = surface.GetProperty("surface").GetInt32();
                var n = surface.GetProperty("rays")[1];
                var c = core.RootElement[1].GetProperty("rows")[number - 1];
                var nIndex = indices.RootElement[number].GetProperty("indices")[wave - 1].GetDouble();
                var cIndex = c.GetProperty("refractiveIndexAfter").GetDouble();
                var normal = c.GetProperty("normal");
                var sign = Math.Sign(normal.GetProperty("z").GetDouble() * n.GetProperty("nz").GetDouble());
                records.Add(new { id, surface = number, cIndex, nIndex, indexError = cIndex - nIndex,
                    cy = c.GetProperty("position").GetProperty("y").GetDouble(), ny = n.GetProperty("y").GetDouble(),
                    cn = c.GetProperty("direction").GetProperty("z").GetDouble(), nn = n.GetProperty("n").GetDouble(),
                    cm = c.GetProperty("direction").GetProperty("y").GetDouble(), nm = n.GetProperty("m").GetDouble(),
                    normalYError = sign * normal.GetProperty("y").GetDouble() - n.GetProperty("ny").GetDouble(),
                    normalZError = sign * normal.GetProperty("z").GetDouble() - n.GetProperty("nz").GetDouble() });
            }
            Console.WriteLine(id);
        }
        JsonFiles.Write(Path.Combine(output, "surface-deltas.json"), records);
    }
}

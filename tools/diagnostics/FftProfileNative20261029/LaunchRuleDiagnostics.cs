using System.IO.Compression;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.ZemaxComparison;

internal static class LaunchRuleDiagnostics
{
    internal static int Run(string repository, string capture, string output)
    {
        if (File.Exists(output)) throw new IOException("Fresh output required");
        using var zip = ZipFile.OpenRead(Path.Combine(repository, "validation/rechecks/2026-10-27/afocal-fft-energy/evidence-current.zip"));
        using var stream = zip.GetEntry("artifacts/validation/afocal-recompare-20261026/baseline-v4/models/001-afocal-riflescope-c1.json")!.Open();
        var optic = Optic.FromSnapshot(JsonSerializer.Deserialize<OpticSnapshot>(stream, JsonFiles.Options)!);
        var rows = new List<object>();
        foreach (var folder in Directory.GetDirectories(capture).Order())
        {
            var packetPath = Path.Combine(folder, "point-reference.json");
            if (!File.Exists(packetPath)) continue;
            var packet = JsonFiles.Read<JsonElement>(packetPath);
            var launches = JsonFiles.Read<JsonElement>(Path.Combine(folder, "direct-launches.json"));
            var image = JsonFiles.Read<JsonElement>(Path.Combine(folder, "image-geometry.json"));
            var control = JsonFiles.Read<JsonElement>(Path.Combine(folder, "diagnostic-pupil-aiming.json"));
            var aim = control.GetProperty("actual").GetProperty("RayAiming").GetString() != "Off";
            var stop = optic.SurfaceGroup.Items.ToList().FindIndex(s => s.IsStop);
            var samples = new List<object>(); var i = 0;
            foreach (var p in packet.GetProperty("inputs").EnumerateArray())
            {
                double N(string key) => p.GetProperty(key).GetDouble();
                var ray = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(N("hx"), N("hy"), N("px"), N("py"), optic.Wavelengths[1].Micrometers, aim).Rays[0];
                var firstZ = optic.SurfaceGroup.Items[1].CoordinateSystem.Origin.Z;
                var launch = ray.Origin + ray.Direction * ((firstZ - ray.Origin.Z) / ray.Direction.Z);
                var nativeLaunch = V(launches[i], "x", "y", "z");
                var hit = optic.SequentialRayTracer.TraceGenericSurfaceSample(N("hx"), N("hy"), N("px"), N("py"), optic.Wavelengths[1].Micrometers, stop, aim);
                var final = optic.SequentialRayTracer.TraceGenericSurfaceSample(N("hx"), N("hy"), N("px"), N("py"), optic.Wavelengths[1].Micrometers, optic.SurfaceGroup.Items.Count - 1, aim);
                var nativeStop = packet.GetProperty("surfaces").EnumerateArray().Single(s => s.GetProperty("surface").GetInt32() == stop).GetProperty("rays")[i];
                samples.Add(new { i, input = p, coreLaunch = launch, nativeLaunch, launchDifference = launch - nativeLaunch,
                    coreStop = hit is null ? (Vector3D?)null : optic.SurfaceGroup.Items[stop].CoordinateSystem.ToLocalPoint(hit.Position), nativeStop,
                    coreImage = final is null ? (Vector3D?)null : optic.SurfaceGroup.Items[^1].CoordinateSystem.ToLocalPoint(final.Position), nativeImage = image[i] });
                i++;
            }
            rows.Add(new { control = Path.GetFileName(folder), aim, samples });
        }
        JsonFiles.Write(output, new { optic.RayAimingEnabled, entranceDiameter = optic.Paraxial.EstimateEntrancePupilDiameter(), entrancePosition = optic.Paraxial.EstimateEntrancePupilLocation(),
            paraxialExitDiameter = optic.Paraxial.EstimateExitPupilDiameter(), paraxialExitPosition = optic.Paraxial.EstimateExitPupilLocation(), rows });
        return 0;
    }
    private static Vector3D V(JsonElement p, string x, string y, string z) => new(p.GetProperty(x).GetDouble(), p.GetProperty(y).GetDouble(), p.GetProperty(z).GetDouble());
}

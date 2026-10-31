using System.IO.Compression;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.ZemaxComparison;

internal static class NativePupilCheck
{
    internal static int Run(string repository, string capture, string output)
    {
        if (File.Exists(output)) throw new IOException("Output exists");
        using var zip = ZipFile.OpenRead(Path.Combine(repository, "validation/rechecks/2026-10-27/afocal-fft-energy/evidence-current.zip"));
        using var stream = zip.GetEntry("artifacts/validation/afocal-recompare-20261026/baseline-v4/models/001-afocal-riflescope-c1.json")!.Open();
        var optic = Optic.FromSnapshot(JsonSerializer.Deserialize<OpticSnapshot>(stream, JsonFiles.Options)!);
        var packet = JsonFiles.Read<JsonElement>(Path.Combine(capture, "riflescope-nine-parabasal-rays/point-reference.json"));
        var inputs = packet.GetProperty("inputs").EnumerateArray().ToArray();
        var rows = new List<object>();
        foreach (var surface in packet.GetProperty("surfaces").EnumerateArray())
        {
            var index = surface.GetProperty("surface").GetInt32();
            var frame = optic.SurfaceGroup.Items[index].CoordinateSystem;
            var rays = new List<object>();
            for (var i = 0; i < inputs.Length; i++)
            {
                var p = inputs[i]; var native = surface.GetProperty("rays")[i];
                if (native.GetProperty("error").GetInt32() != 0 || native.GetProperty("vignette").GetInt32() != 0) throw new InvalidDataException("Invalid native ray");
                var ray = optic.SequentialRayTracer.TraceGenericSurfaceSample(p.GetProperty("hx").GetDouble(), p.GetProperty("hy").GetDouble(),
                    p.GetProperty("px").GetDouble(), p.GetProperty("py").GetDouble(), optic.Wavelengths[1].Micrometers, index, optic.RayAimingEnabled)!;
                if (ray is not { Vignetted: false, Intensity: > 0 }) throw new InvalidDataException("Invalid Core ray");
                var position = frame.ToLocalPoint(ray.Position); var direction = frame.ToLocalDirection(ray.Direction);
                var nativePosition = V(native, "x", "y", "z"); var nativeDirection = V(native, "l", "m", "n");
                // Afocal CurrentAndChief reports image coordinates on the native
                // exit-reference plane. Retain those bytes, but propagate to the
                // Core image plane before comparing position in this centered control.
                var projectedImageReference = index == optic.SurfaceGroup.Items.Count - 1;
                var comparisonPosition = projectedImageReference
                    ? nativePosition + nativeDirection * ((position.Z - nativePosition.Z) / nativeDirection.Z)
                    : nativePosition;
                var dp = position - comparisonPosition; var dd = direction - nativeDirection;
                rays.Add(new { i, input = p, position, direction, native, projectedImageReference,
                    nativePositionForComparison = comparisonPosition, positionDifference = dp, directionDifference = dd,
                    positionError = dp.Length, directionError = dd.Length });
            }
            rows.Add(new { surface = index, optic.SurfaceGroup.Items[index].IsStop, rays });
        }
        JsonFiles.Write(output, new { role = "Diagnostic only; image reference explicitly projected, no optical acceptance or scale correction",
            optic.RayAimingEnabled, surfaces = rows });
        Console.WriteLine("Compared " + inputs.Length + " actual rays across " + rows.Count + " surfaces; no product scale correction applied.");
        return 0;
    }
    static Vector3D V(JsonElement p, string x, string y, string z) => new(p.GetProperty(x).GetDouble(), p.GetProperty(y).GetDouble(), p.GetProperty(z).GetDouble());
}

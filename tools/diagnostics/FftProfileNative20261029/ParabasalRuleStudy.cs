using System.IO.Compression;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.ZemaxComparison;

// Investigation only. Every map sample is a real shared Core trace.
internal static class ParabasalRuleStudy
{
    internal static int Run(string repository, string capture, string output)
    {
        if (File.Exists(output)) throw new IOException("Fresh output required");
        using var zip = ZipFile.OpenRead(Path.Combine(repository, "validation/rechecks/2026-10-27/afocal-fft-energy/evidence-current.zip"));
        using var stream = zip.GetEntry("artifacts/validation/afocal-recompare-20261026/baseline-v4/models/001-afocal-riflescope-c1.json")!.Open();
        var optic = Optic.FromSnapshot(JsonSerializer.Deserialize<OpticSnapshot>(stream, JsonFiles.Options)!);
        var wave = optic.Wavelengths[1].Micrometers;
        var stop = optic.SurfaceGroup.Items.ToList().FindIndex(s => s.IsStop);
        var target = optic.Paraxial.TraceNormalizedPupil(0, [1], wave).Heights[stop][0];
        var gain = target / (optic.Paraxial.EstimateEntrancePupilDiameter() / 2);
        var rows = new List<object>();
        foreach (var folder in Directory.GetDirectories(capture).Order())
        {
            var packetPath = Path.Combine(folder, "point-reference.json");
            if (!File.Exists(packetPath)) continue;
            var packet = JsonFiles.Read<JsonElement>(packetPath);
            var launches = JsonFiles.Read<JsonElement>(Path.Combine(folder, "direct-launches.json"));
            var control = JsonFiles.Read<JsonElement>(Path.Combine(folder, "diagnostic-pupil-aiming.json")).GetProperty("actual");
            var aim = control.GetProperty("RayAiming").GetString() != "Off";
            var cache = control.GetProperty("UseRayAimingCache").GetBoolean();
            var samples = new List<object>(); var i = 0;
            foreach (var p in packet.GetProperty("inputs").EnumerateArray())
            {
                double N(string key) => p.GetProperty(key).GetDouble();
                var ray = Generate(N("hx"), N("hy"), N("px"), N("py"), aim, cache, out var passes);
                var launch = ray.Origin + ray.Direction * ((optic.SurfaceGroup.Items[1].CoordinateSystem.Origin.Z - ray.Origin.Z) / ray.Direction.Z);
                var nativeLaunch = new Vector3D(launches[i].GetProperty("x").GetDouble(), launches[i].GetProperty("y").GetDouble(), launches[i].GetProperty("z").GetDouble());
                samples.Add(new { i, input = p, passes, launch, nativeLaunch, difference = launch - nativeLaunch });
                i++;
            }
            var fieldProbe = .01 / optic.Fields.Max(f => Math.Abs(f.Y));
            var chief = optic.SequentialRayTracer.TraceFinalSamples(new([Generate(0, fieldProbe, 0, 0, aim, false, out _)]))[0]!;
            var imageFrame = optic.SurfaceGroup.Items[^1].CoordinateSystem;
            var point = imageFrame.ToLocalPoint(chief.Position); var direction = imageFrame.ToLocalDirection(chief.Direction);
            var z = -point.Y * direction.Z / direction.Y;
            var marginal = optic.SequentialRayTracer.TraceFinalSamples(new([Generate(0, 0, 0, .001, aim, cache, out _)]))[0]!;
            var mp = imageFrame.ToLocalPoint(marginal.Position); var md = imageFrame.ToLocalDirection(marginal.Direction);
            rows.Add(new { control = Path.GetFileName(folder), fieldProbe, exitPosition = z,
                exitDiameter = 2 * (mp.Y + z * md.Y / md.Z) / .001, samples });
        }
        JsonFiles.Write(output, rows); return 0;

        RealRay Generate(double hx, double hy, double px, double py, bool aim, bool cache, out int passes)
        {
            var generator = optic.SequentialRayTracer.RayGenerator;
            var initial = generator.GenerateGeneric(hx, hy, px, py, wave, false).Rays[0];
            passes = 0;
            if (!aim) return initial;
            var x = initial.Origin.X; var y = initial.Origin.Y;
            if (cache && hx == 0 && hy == 0 && (px != 0 || py != 0))
            {
                var half = generator.GenerateGeneric(0, 0, 0, .5, wave, true).Rays[0].Origin.Y;
                var full = generator.GenerateGeneric(0, 0, 0, 1, wave, true).Rays[0].Origin.Y;
                double Predict(double p) => Math.CopySign((4 * half - full) * Math.Abs(p) + 2 * (full - 2 * half) * p * p, p);
                x = Predict(px); y = Predict(py);
            }
            var hit = Sample(x, y);
            const double delta = 1e-6;
            var jacX = (Sample(x + delta, y) - Sample(x - delta, y)) / (2 * delta);
            var jacY = (Sample(x, y + delta) - Sample(x, y - delta)) / (2 * delta);
            var smallX = Math.Abs((px - hit.X / target) * jacX.X / gain) <= 2e-7;
            var smallY = Math.Abs((py - hit.Y / target) * jacY.Y / gain) <= 2e-7;
            for (; passes < 64; passes++)
            {
                var ex = px * target - hit.X; var ey = py * target - hit.Y;
                if (Math.Abs(ex / target) + Math.Abs(ey / target) <= 1e-8) break;
                jacX = (Sample(x + delta, y) - Sample(x - delta, y)) / (2 * delta);
                jacY = (Sample(x, y + delta) - Sample(x, y - delta)) / (2 * delta);
                var determinant = jacX.X * jacY.Y - jacX.Y * jacY.X;
                var relaxation = passes == 0 ? .75 : .9998;
                var smallRelaxation = passes == 0 ? .5 : .25;
                x += smallX ? smallRelaxation * ex / gain : relaxation * (jacY.Y * ex - jacY.X * ey) / determinant;
                y += smallY ? smallRelaxation * ey / gain : relaxation * (jacX.X * ey - jacX.Y * ex) / determinant;
                hit = Sample(x, y);
            }
            return new(new(x, y, initial.Origin.Z), initial.Direction, wave * 1000);
            Vector3D Sample(double a, double b)
            {
                using var trace = optic.SequentialRayTracer.Trace(new([new RealRay(new(a, b, initial.Origin.Z), initial.Direction, wave * 1000)]), TraceRequest.Selected([stop]));
                if (!trace.TryGetSample(0, stop, out var r)) throw new InvalidDataException("Pencil map is blocked");
                return optic.SurfaceGroup.Items[stop].CoordinateSystem.ToLocalPoint(r.Position);
            }
        }
    }
}

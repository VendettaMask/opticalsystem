using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.ZemaxComparison;

internal static class GridControls
{
    internal static async Task EntranceIndex(string study)
    {
        var request = JsonFiles.Read<JsonElement>(Path.Combine(study, "controls.request.json"));
        var path = request.TryGetProperty("coreInput", out var input) ? input.GetString()! : request.GetProperty("input").GetString()!;
        var optic = await new ZemaxZmxImporter().ImportFileAsync(path);
        var native = JsonFiles.Read<JsonElement>(Path.Combine(study, "controls", "model-controls.json")).GetProperty("rows").EnumerateArray().ToArray();
        var stop = optic.SurfaceGroup.Items[1];
        if (!stop.IsPlane || !stop.IsStop || optic.Fields.Any(f => f.X != 0 || f.Y != 0)
            || stop.MaterialAfter is not OpticalSystemDesign.Core.Materials.GradientIndexMaterial { Profile: OpticalSystemDesign.Core.Materials.GridGradientIndexProfile profile })
            throw new InvalidDataException("This independent Snell control requires a finite on-axis object and a plane grid entrance");
        var rows = new List<object>();
        foreach (var query in native.Select(r => r.GetProperty("input")).Where(p => p.GetProperty("code").GetString() == "REAX" && p.GetProperty("int1").GetInt32() == 1))
        {
            var px = query.GetProperty("data3").GetDouble(); var py = query.GetProperty("data4").GetDouble();
            if (px == 0 && py == 0) continue;
            double Value(string code) => native.Single(r => { var p = r.GetProperty("input"); return p.GetProperty("code").GetString() == code
                && p.GetProperty("int1").GetInt32() == 1 && p.GetProperty("data3").GetDouble() == px && p.GetProperty("data4").GetDouble() == py; }).GetProperty("value").GetDouble();
            var local = new OpticalSystemDesign.Core.Backend.Vector3D(Value("REAX"), Value("REAY"), Value("REAZ"));
            var delta = stop.CoordinateSystem.ToGlobalPoint(local) - optic.SurfaceGroup.Items[0].CoordinateSystem.Origin;
            var incident = stop.CoordinateSystem.ToLocalDirection(delta / delta.Length);
            var inferred = Math.Sqrt((incident.X * incident.X + incident.Y * incident.Y)
                / (Value("REAA") * Value("REAA") + Value("REAB") * Value("REAB")));
            var direct = profile.InterfaceRefractiveIndex(local, 550);
            var volume = profile.Evaluate(local, 550).Index;
            var candidates = new List<object>();
            if (py == 0 && Math.Abs(-profile.Origin.Y / profile.Spacing.Y - Math.Round(-profile.Origin.Y / profile.Spacing.Y)) < 1e-12)
            {
                var u = (local.X - profile.Origin.X) / profile.Spacing.X;
                var iy = (int)Math.Round(-profile.Origin.Y / profile.Spacing.Y);
                for (var start = 0; start <= profile.Nx - 4; start++)
                {
                    if (u < start || u > start + 3) continue;
                    var f = Enumerable.Range(start, 4).Select(i => profile.Values[11 * ((i * profile.Ny + iy) * profile.Nz)]).ToArray();
                    var h = profile.Spacing.X;
                    var a = 6 * (f[0] - 2 * f[1] + f[2]) / (h * h);
                    var b = 6 * (f[1] - 2 * f[2] + f[3]) / (h * h);
                    double[] second = [0, (4 * a - b) / 15, (4 * b - a) / 15, 0];
                    var interval = Math.Min(2, (int)Math.Floor(u - start)); var t = u - start - interval; var s = 1 - t;
                    var index = s * f[interval] + t * f[interval + 1]
                        + h * h / 6 * ((s * s * s - s) * second[interval] + (t * t * t - t) * second[interval + 1]);
                    candidates.Add(new { start, index, error = index - inferred });
                }
            }
            rows.Add(new { px, py, inferredFromNativePlaneSnell = inferred, standaloneIndex = direct, volumeIndex = volume,
                standaloneError = direct - inferred, volumeError = volume - inferred, xOnlyFourNodeNaturalWindows = candidates,
                nearestEvenIndex = Tensor(Math.Round), nearestHalfUpIndex = Tensor(q => Math.Floor(q + .5)),
                lagrangeIndex = Tensor(Math.Floor, "Lagrange"), catmullRomIndex = Tensor(Math.Floor, "CatmullRom"), nativeLocal = local });

            double? Tensor(Func<double, double> select, string kind = "Natural")
            {
                if (profile.Values.Where((_, i) => i % 11 != 0).Any(v => v != 0)) return null;
                var q = new[] { (local.X - profile.Origin.X) / profile.Spacing.X, (local.Y - profile.Origin.Y) / profile.Spacing.Y,
                    (local.Z - profile.Origin.Z) / profile.Spacing.Z };
                var counts = new[] { profile.Nx, profile.Ny, profile.Nz };
                var starts = q.Select((v, i) => Math.Clamp((int)select(v) - 1, 0, counts[i] - 4)).ToArray();
                var weights = q.Select((v, axis) => Enumerable.Range(0, 4).Select(basis =>
                {
                    var coordinate = v - starts[axis];
                    if (kind == "Lagrange")
                    {
                        var result = 1d;
                        for (var node = 0; node < 4; node++) if (node != basis) result *= (coordinate - node) / (basis - node);
                        return result;
                    }
                    if (kind == "CatmullRom")
                    {
                        if (coordinate == Math.Round(coordinate)) return coordinate == basis ? 1d : 0;
                        if (coordinate < 1 || coordinate > 2) throw new InvalidDataException("Independent Catmull control requires interior cells or exact endpoints");
                        var ct = coordinate - 1; var ct2 = ct * ct; var ct3 = ct2 * ct;
                        return basis switch { 0 => -.5 * ct + ct2 - .5 * ct3, 1 => 1 - 2.5 * ct2 + 1.5 * ct3,
                            2 => .5 * ct + 2 * ct2 - 1.5 * ct3, _ => -.5 * ct2 + .5 * ct3 };
                    }
                    var f = Enumerable.Range(0, 4).Select(i => i == basis ? 1d : 0).ToArray();
                    var a = 6 * (f[0] - 2 * f[1] + f[2]); var b = 6 * (f[1] - 2 * f[2] + f[3]);
                    double[] second = [0, (4 * a - b) / 15, (4 * b - a) / 15, 0];
                    var interval = Math.Clamp((int)Math.Floor(v - starts[axis]), 0, 2);
                    var t = v - starts[axis] - interval; var s = 1 - t;
                    return s * f[interval] + t * f[interval + 1]
                        + ((s * s * s - s) * second[interval] + (t * t * t - t) * second[interval + 1]) / 6;
                }).ToArray()).ToArray();
                var value = 0d;
                for (var a = 0; a < 4; a++) for (var b = 0; b < 4; b++) for (var c = 0; c < 4; c++)
                    value += profile.Values[11 * (((starts[0] + a) * profile.Ny + starts[1] + b) * profile.Nz + starts[2] + c)]
                        * weights[0][a] * weights[1][b] * weights[2][c];
                return value;
            }
        }
        JsonFiles.Write(Path.Combine(study, "entrance-index-diagnostic.json"), new { diagnosticOnly = true,
            method = "Native hit/direction plus declared finite object distance; plane Snell inversion, no Core ray tracing in reference", rows });
        foreach (var row in rows) Console.WriteLine(JsonSerializer.Serialize(row));
    }

    internal static async Task Pencil(string study)
    {
        var request = JsonFiles.Read<JsonElement>(Path.Combine(study, "controls.request.json"));
        var optic = await new ZemaxZmxImporter().ImportFileAsync(request.GetProperty("input").GetString()!);
        var stopIndex = optic.SurfaceGroup.Items.ToList().FindIndex(s => s.IsStop);
        var stop = optic.SurfaceGroup.Items[stopIndex]; var wave = optic.Wavelengths[request.GetProperty("wavelength").GetInt32() - 1];
        var source = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 0, 0, 0, wave.Micrometers);
        using var trace = optic.SequentialRayTracer.Trace(source, TraceRequest.Selected([stopIndex, optic.SurfaceGroup.Items.Count - 1]));
        var hit = trace.GetSurfaceSamples(stopIndex)[0]!.Value;
        var material = stop.MaterialAfter;
        if (material is OpticalSystemDesign.Core.Materials.GradientIndexMaterial gradient)
            material = (OpticalSystemDesign.Core.Materials.IMaterial)typeof(OpticalSystemDesign.Core.Materials.GradientIndexMaterial)
                .GetMethod("At", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(gradient, [stop.CoordinateSystem, stop.CoordinateSystem.ToLocalPoint(hit.Position)])!;
        var method = typeof(SequentialRayTracer).GetMethod("TracePupilGeometryProbe", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var output = new List<object>();
        foreach (var h in new[] { .001, .0001, .00001 }) foreach (var xAxis in new[] { false, true })
        {
            var values = new List<OpticalSystemDesign.Core.Rays.RayTraceSample>();
            foreach (var sign in new[] { -1, 1 })
            {
                var d = hit.Direction + (xAxis ? new OpticalSystemDesign.Core.Backend.Vector3D(sign * h, 0, 0) : new OpticalSystemDesign.Core.Backend.Vector3D(0, sign * h, 0));
                var ray = new OpticalSystemDesign.Core.Rays.RealRay(hit.Position, d, wave.Nanometers);
                values.Add((OpticalSystemDesign.Core.Rays.RayTraceSample)method.Invoke(optic.SequentialRayTracer, [ray, stopIndex + 1, material])!);
            }
            var a = values[0]; var b = values[1];
            var distance = xAxis ? -(b.Position.X - a.Position.X) / (b.Direction.X / b.Direction.Z - a.Direction.X / a.Direction.Z)
                : -(b.Position.Y - a.Position.Y) / (b.Direction.Y / b.Direction.Z - a.Direction.Y / a.Direction.Z);
            output.Add(new { h, xAxis, distance });
        }
        JsonFiles.Write(Path.Combine(study, "pupil-pencil-diagnostic.json"), output);
        foreach (var item in output) Console.WriteLine(JsonSerializer.Serialize(item));
    }

    internal static async Task ProjectedGrid(string study, bool directional = false)
    {
        var request = JsonFiles.Read<JsonElement>(Path.Combine(study, "controls.request.json"));
        var optic = await new ZemaxZmxImporter().ImportFileAsync(request.GetProperty("input").GetString()!);
        var axes = DiffractionEngine.WorkingFNumbers(optic, (0, 0), optic.Wavelengths[1], false, zemaxDirectionalAverage: directional);
        var sx = Math.Min(axes.Tangential, axes.Sagittal) / axes.Sagittal;
        var sy = Math.Min(axes.Tangential, axes.Sagittal) / axes.Tangential;
        var pupils = Enumerable.Range(0, 64).SelectMany(y => Enumerable.Range(0, 64).Select(x =>
            (X: (x - 32d) / (31 * sx), Y: (y - 32d) / (31 * sy))))
            .Where(p => p.X * p.X + p.Y * p.Y <= 1).ToArray();
        var sphere = WavefrontEngine.CreateChiefRayReferenceSphere(optic, (0, 0), optic.Wavelengths[1], false);
        var native = JsonFiles.Read<JsonElement>(Path.Combine(study, "controls", "model-controls.json"));
        var nativeRadius = -native.GetProperty("rows").EnumerateArray()
            .Single(r => r.GetProperty("input").GetProperty("code").GetString() == "EXPP").GetProperty("value").GetDouble();
        var variants = new List<object>();
        foreach (var diagnosticRadius in new[] { sphere.Radius, nativeRadius })
        {
            // The second variant is a diagnostic intervention, never product evaluation or acceptance.
            var wavefront = WavefrontEngine.GenerateChiefRaySamples(optic, (0, 0), optic.Wavelengths[1], pupils,
                aimAtStop: false, referenceSphere: sphere with { Radius = diagnosticRadius });
            variants.Add(new { diagnosticRadius, usesNativeRadius = diagnosticRadius == nativeRadius,
                points = wavefront.Samples.Select(s => new { x = s.NormalizedPupilX * sx, y = s.NormalizedPupilY * sy,
                    value = s.Intensity > 0 ? (double?)s.OpdWaves : null }) });
        }
        JsonFiles.Write(Path.Combine(study, directional ? "projected-directional-grid.json" : "projected-grid.json"), new { sx, sy, variants,
            coreSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(Optic).Assembly.Location))) });
    }

    internal static async Task Run(string study, bool includeWavefront = true)
    {
        var request = JsonFiles.Read<JsonElement>(Path.Combine(study, "controls.request.json"));
        var coreInput = request.TryGetProperty("coreInput", out var originalInput) ? originalInput.GetString()! : request.GetProperty("input").GetString()!;
        var optic = await new ZemaxZmxImporter().ImportFileAsync(coreInput);
        var native = JsonFiles.Read<JsonElement>(Path.Combine(study, "controls", "model-controls.json")).GetProperty("rows").EnumerateArray().ToArray();
        var outputs = new List<object>();
        var grid = includeWavefront ? WavefrontEngine.GenerateChiefRayUniform(optic, (0, 0), optic.Wavelengths[1], 64, zemaxCentered: true) : null;
        foreach (var group in native.Where(r => r.GetProperty("input").GetProperty("code").GetString() == "REAX")
            .Select(r => r.GetProperty("input")).GroupBy(p => (p.GetProperty("data3").GetDouble(), p.GetProperty("data4").GetDouble())))
        {
            var (px, py) = group.Key;
            var bundle = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 0, px, py, .55, false);
            var indices = Enumerable.Range(1, optic.SurfaceGroup.Items.Count - 1).ToArray();
            using var traced = optic.SequentialRayTracer.Trace(bundle, TraceRequest.Selected(indices));
            var first = traced.GetSurfaceSamples(1)[0];
            foreach (var p in group)
            {
                var index = p.GetProperty("int1").GetInt32();
                var ray = traced.GetSurfaceSamples(index)[0];
                if (!ray.HasValue) { outputs.Add(new { px, py, index, failure = "No Core hit" }); continue; }
                var s = ray.Value; var surface = optic.SurfaceGroup.Items[index];
                var position = surface.CoordinateSystem.ToLocalPoint(s.Position);
                var direction = surface.CoordinateSystem.ToLocalDirection(s.Direction);
                double Value(string code, int surfaceNumber) => native.Single(r =>
                {
                    var q = r.GetProperty("input");
                    return q.GetProperty("code").GetString() == code && q.GetProperty("int1").GetInt32() == surfaceNumber
                        && q.GetProperty("data3").GetDouble() == px && q.GetProperty("data4").GetDouble() == py;
                }).GetProperty("value").GetDouble();
                foreach (var (code, actual) in new[] { ("REAX", position.X), ("REAY", position.Y), ("REAZ", position.Z),
                    ("REAA", direction.X), ("REAB", direction.Y), ("REAC", direction.Z),
                    ("OPTH", s.PhaseInclusiveOpticalPathLength!.Value - first!.Value.PhaseInclusiveOpticalPathLength!.Value) })
                {
                    var expected = Value(code, index) - (code == "OPTH" ? Value(code, 1) : 0);
                    outputs.Add(new { px, py, index, code, actual, expected, error = actual - expected });
                }
            }
        }
        JsonFiles.Write(Path.Combine(study, "product-controls.json"), new { rows = outputs, radius = grid?.Radius, referenceOpticalPath = grid?.ReferenceOpticalPath,
            vignettedRayCount = grid?.VignettedRayCount, coreInput, coreInputSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(coreInput))),
            coreSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(Optic).Assembly.Location))),
            wavefront = grid?.Samples.Where(s => s.NormalizedPupilX == 0 || s.NormalizedPupilY == 0).Select(s => new { s.NormalizedPupilX, s.NormalizedPupilY, s.OpdWaves, s.Intensity }) });
        foreach (var item in outputs) Console.WriteLine(JsonSerializer.Serialize(item));
    }
}

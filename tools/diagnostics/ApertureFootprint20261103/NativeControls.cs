using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Diagnostics;
using OpticalSystemDesign.ZemaxComparison.Workbench;
using OpticalSystemDesign.ZemaxComparison.Zemax;

internal static class NativeControls
{
    private static readonly string[] Lenses = ["Telescopes/Unobscured Gregorian.zmx", "Miscellaneous/Parabolas at 90 degrees.zmx",
        "Tilted systems & prisms/OAP using Chebyshev Polynomial surface.zmx", "Miscellaneous/Beam splitter.zmx",
        "Tilted systems & prisms/Prisms using coordinate breaks.ZMX", "Miscellaneous/Apochromat_prism.zmx"];
    internal static async Task Run(string repo, string output, string mode)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(Optic).Assembly.Location)));
        var records = new List<object>();
        using var lease = new FileStream(Path.Combine(repo, "artifacts/validation/.background-small-native-controls.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var native = mode == "native" ? await ZemaxExecutor.Build(ZemaxExecutor.Locate(null), output, CancellationToken.None) : null;
        for (var lens = 0; lens < Lenses.Length; lens++)
        {
            var source = Path.Combine(repo, "src/OpticalSystemDesign.App/Assets/SequentialExamples", Lenses[lens]);
            var optic = (await WorkbenchExecutor.Import(source)).Configurations[0];
            var wave = optic.Wavelengths.FirstOrDefault(w => w.IsPrimary) ?? optic.Wavelengths[0];
            var fields = new[] { (0d, 0d), FieldCoordinates.Normalize(optic.Fields, optic.Fields[^1].X, optic.Fields[^1].Y) }.Distinct().ToArray();
            var pupils = new[] { (0d, 0d), (.95, 0d), (-.95, 0d), (0d, .95), (0d, -.95), (.5, .5), (-.5, -.5), (.5, -.5), (-.5, .5) };
            var inputs = fields.SelectMany((f, fi) => pupils.Select((p, pi) => new { fieldIndex = fi, pupilIndex = pi, hx = f.Item1, hy = f.Item2, px = p.Item1, py = p.Item2, weight = 1d })).ToArray();
            var folder = Path.Combine(output, lens.ToString("D2"));
            if (mode == "native")
            {
                var processes = Process.GetProcessesByName("OpticStudio"); var count = processes.Length; foreach (var p in processes) p.Dispose();
                if (count > 1) throw new InvalidOperationException("Independent native slot unavailable");
                var job = new RayAuditJob { Source = source, SourceSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(source))), Output = folder,
                    Configuration = 1, Wavelength = optic.Wavelengths.IndexOf(wave) + 1, RayDensity = 6, Fields = fields.Select(f => new[] { f.Item1, f.Item2 }).ToArray(), TimeoutSeconds = 120, RemoveVignettingFactors = false };
                var result = await native!.CaptureRayAudit(source, Path.Combine(folder, "native"), job, inputs, CancellationToken.None);
                records.Add(new { lens, source = Lenses[lens], result, coreHash = hash, job.SourceSha256 }); JsonFiles.Write(Path.Combine(output, "executions.json"), records);
                if (result.ExitCode != 0) throw new InvalidOperationException("Native capture failed; retained");
                Console.WriteLine($"{Lenses[lens]}: captured {inputs.Length} fixed rays"); continue;
            }
            var reference = Path.Combine(Path.GetDirectoryName(output)!, "native-controls", lens.ToString("D2"), "native");
            var data = JsonFiles.Read<JsonElement>(Path.Combine(reference, "ray-audit.json"));
            var model = JsonFiles.Read<JsonElement>(Path.Combine(reference, "model.json"));
            // Explicit merit operands can retain a zero-thickness reference
            // plane too. Use declared source surface numbers, never a fitted
            // nearest-position or label-based map.
            var meritSurfaces = File.ReadLines(source).Select(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                .Where(t => t.Length > 1 && t[0] is "REAX" or "REAY" or "REAR" or "REAZ" or "REAA" or "REAB" or "REAC")
                .Select(t => int.Parse(t[1], System.Globalization.CultureInfo.InvariantCulture)).ToHashSet();
            var physical = model.GetProperty("surfaces").EnumerateArray().Where(s => s.GetProperty("Type").GetString() != "CoordinateBreak"
                || s.TryGetProperty("Thickness", out var t) && t.ValueKind == JsonValueKind.Number && t.GetDouble() != 0
                || meritSurfaces.Contains(s.GetProperty("SurfaceNumber").GetInt32())).ToArray();
            if (physical.Length != optic.SurfaceGroup.Items.Count)
                physical = model.GetProperty("surfaces").EnumerateArray().Where(s => s.GetProperty("Type").GetString() != "CoordinateBreak").ToArray();
            if (physical.Length != optic.SurfaceGroup.Items.Count) throw new InvalidDataException("Ambiguous physical surface map");
            var map = physical.Select((s, i) => (Number: s.GetProperty("SurfaceNumber").GetInt32(), Index: i)).ToDictionary(p => p.Number, p => p.Index);
            var signs = NativeReflectionConvention.PropagationSigns(model);
            var unit = model.GetProperty("units").GetProperty("LensUnits").GetString();
            var scale = unit switch { "Millimeters" => 1d, "Inches" => 25.4, "Centimeters" => 10d, "Meters" => 1000d, _ => throw new InvalidDataException("Unmapped native length unit") };
            int acceptance = 0, clipping = 0, coordinates = 0, directions = 0, pairs = 0;
            double maxPosition = 0, maxDirection = 0;
            var core = inputs.Select(p =>
            {
                try
                {
                var bundle = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(p.hx, p.hy, p.px, p.py, wave.Micrometers, optic.RayAimingEnabled);
                return new CoreRay(optic.SequentialRayTracer.DiagnoseApertures(bundle.Rays.Single()), optic.SequentialRayTracer.Trace(bundle).RayHistories.Single(), null);
                }
                catch (Exception e) { return new CoreRay(null, [], e.ToString()); }
            }).ToArray();
            JsonFiles.Write(Path.Combine(folder, "core-rays.json"), core);
            var last = data.GetProperty("surfaces").EnumerateArray().Last().GetProperty("rays");
            for (var i = 0; i < inputs.Length; i++)
            {
                if (core[i].diagnostic is not { } diagnostic) continue;
                var n = last[i]; if (diagnostic.InsideAllApertures != (n.GetProperty("error").GetInt32() == 0 && n.GetProperty("vignette").GetInt32() == 0)) acceptance++;
                var clip = n.GetProperty("vignette").GetInt32(); var blocked = diagnostic.FirstBlockedSurfaceIndex;
                if (clip == 0 ? blocked is not null : blocked is null || !map.TryGetValue(clip, out var index) || index != blocked) clipping++;
            }
            foreach (var surface in data.GetProperty("surfaces").EnumerateArray())
            {
                if (!map.TryGetValue(surface.GetProperty("surface").GetInt32(), out var index)) continue;
                for (var i = 0; i < inputs.Length; i++)
                {
                    var n = surface.GetProperty("rays")[i]; var hit = core[i].history.FirstOrDefault(s => s.SurfaceNumber == index);
                    if (n.GetProperty("error").GetInt32() != 0 || n.GetProperty("vignette").GetInt32() != 0 || hit is null || hit.Vignetted) continue;
                    pairs++; var frame = optic.SurfaceGroup.Items[index].CoordinateSystem;
                    var p = frame.ToLocalPoint(hit.Position); var d = frame.ToLocalDirection(hit.Direction);
                    foreach (var pair in new[] { ("x", p.X), ("y", p.Y), ("z", p.Z) }) { var value = n.GetProperty(pair.Item1).GetDouble() * scale; var error = Math.Abs(pair.Item2 - value); maxPosition = Math.Max(maxPosition, error); if (error > 1e-6 + 1e-6 * Math.Abs(value)) coordinates++; }
                    foreach (var pair in new[] { ("l", d.X), ("m", d.Y), ("n", d.Z) }) { var value = n.GetProperty(pair.Item1).GetDouble() * signs[surface.GetProperty("surface").GetInt32()]; var error = Math.Abs(pair.Item2 - value); maxDirection = Math.Max(maxDirection, error); if (error > 1e-8 + 1e-6 * Math.Abs(value)) directions++; }
                }
            }
            records.Add(new { lens, source = Lenses[lens], rays = inputs.Length, surfacePairs = pairs, acceptanceDifferences = acceptance, firstClipDifferences = clipping,
                coordinateFailures = coordinates, directionFailures = directions, maximumPositionDifference = maxPosition, maximumDirectionDifference = maxDirection,
                generationFailures = core.Count(r => r.error is not null), conclusion = acceptance + clipping + coordinates + directions == 0 && pairs > 0 && core.All(r => r.error is null) ? "Pass" : "Difference", coreHash = hash });
            JsonFiles.Write(Path.Combine(output, "results.json"), records); Console.WriteLine($"{Lenses[lens]}: acceptance={acceptance}, clipping={clipping}, positions={coordinates}, directions={directions}");
        }
    }
    private sealed record CoreRay(ApertureRayDiagnostic? diagnostic, IReadOnlyList<RayTraceSample> history, string? error);
}

using System.Text.Json;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Workbench;

// Reuses the frozen GQ inputs, native reflection convention and component
// limits from the Sequential corpus. All optical calculations use formal Core.
internal static class WideSchmidtRays
{
    internal static void Gregorian(string output)
    {
        if (Directory.Exists(output)) throw new IOException("Fresh output required.");
        Directory.CreateDirectory(output);
        var imported = WorkbenchExecutor.Import("src/OpticalSystemDesign.App/Assets/SequentialExamples/Telescopes/Unobscured Gregorian.zmx").GetAwaiter().GetResult().Configurations[0];
        var trace = typeof(OpticalSystemDesign.Core.Analysis.DiffractionEngine).Assembly.GetType("OpticalSystemDesign.Core.Analysis.AnalysisTrace")!;
        var optic = (OpticalSystemDesign.Core.Optic)trace.GetMethod("PrepareVignettingFactors")!.Invoke(null, [imported, true])!;
        var rows = new List<object>();
        foreach (var f in Enumerable.Range(0, 16))
        {
            var bundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(0, f / 15d, optic.Wavelengths[0].Micrometers,
                ApertureSampler.GenerateGaussianQuadrature(6, 12), aimAtStop: optic.RayAimingEnabled);
            var histories = optic.SequentialRayTracer.TraceGaussianPupil(bundle);
            rows.Add(new { field = f, rays = histories.Select(h => h.Select(s => new { s.SurfaceNumber, s.Position, s.Direction, s.SegmentLength,
                localPosition = optic.SurfaceGroup.Items[s.SurfaceNumber].CoordinateSystem.ToLocalPoint(s.Position),
                localDirection = optic.SurfaceGroup.Items[s.SurfaceNumber].CoordinateSystem.ToLocalDirection(s.Direction),
                s.IncidentDirection, s.Vignetted, s.Intensity })) });
        }
        JsonFiles.Write(Path.Combine(output, "rays.json"), rows);
        JsonFiles.Write(Path.Combine(output, "model.json"), optic.ToSnapshot());
    }

    internal static void Run(string output)
    {
        if (Directory.Exists(output)) throw new IOException("Fresh output required.");
        Directory.CreateDirectory(output);
        var rows = new List<object>();
        foreach (var (lens, prefix, scale) in new[]
        {
            (WideSchmidtRepair.Lenses[0], "064-wide-angle-lens-200-degree-field-c1", 25.4),
            (WideSchmidtRepair.Lenses[1], "065-wide-angle-lens-210-degree-field-c1", 1d),
            (WideSchmidtRepair.Lenses[2], "074-flat-field-schmidt-c1", 1d)
        })
        foreach (var wave in Enumerable.Range(1, 3))
        {
            var id = prefix + "-w" + wave;
            var baseline = Path.Combine(WideSchmidtRepair.Baseline, "rays", id);
            using var job = JsonDocument.Parse(File.ReadAllText(Path.Combine(baseline, "job.json")));
            using var raw = JsonDocument.Parse(File.ReadAllText(Path.Combine(baseline, "native/ray-audit.json")));
            using var nativeModel = JsonDocument.Parse(File.ReadAllText(Path.Combine(baseline, "native/model.json")));
            var signs = NativeReflectionConvention.PropagationSigns(nativeModel.RootElement);
            var source = Path.Combine("src/OpticalSystemDesign.App/Assets/SequentialExamples", lens);
            var optic = WorkbenchExecutor.Import(source).GetAwaiter().GetResult().Configurations[0];
            var samples = ApertureSampler.GenerateGaussianQuadrature(6, 12);
            var histories = job.RootElement.GetProperty("fields").EnumerateArray().SelectMany(f =>
            {
                var bundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(f[0].GetDouble(), f[1].GetDouble(),
                    optic.Wavelengths[wave - 1].Micrometers, samples, aimAtStop: optic.RayAimingEnabled);
                return optic.SequentialRayTracer.Trace(bundle).RayHistories;
            }).ToArray();
            var folder = Path.Combine(output, id); Directory.CreateDirectory(folder);
            JsonFiles.Write(Path.Combine(folder, "core.json"), histories);
            var image = raw.RootElement.GetProperty("surfaces").EnumerateArray().Last().GetProperty("rays");
            if (image.GetArrayLength() != histories.Length) throw new InvalidDataException("Ray input count differs.");
            var acceptance = 0; var firstClip = 0; var positionFailures = 0; var directionFailures = 0; var comparisons = 0;
            var maximumPosition = 0d; var maximumDirection = 0d;
            for (var p = 0; p < histories.Length; p++)
            {
                var h = histories[p]; var last = h[^1]; var n = image[p];
                var accepted = n.GetProperty("error").GetInt32() == 0 && n.GetProperty("vignette").GetInt32() == 0;
                var coreAccepted = last.SurfaceNumber == optic.SurfaceGroup.Items.Count - 1 && !last.Vignetted && last.Intensity > 0;
                if (accepted != coreAccepted) acceptance++;
                if (n.GetProperty("error").GetInt32() == 0 && n.GetProperty("vignette").GetInt32() > 0
                    && last.Vignetted && last.RefractiveIndexBefore.HasValue && n.GetProperty("vignette").GetInt32() != last.SurfaceNumber) firstClip++;
                foreach (var s in raw.RootElement.GetProperty("surfaces").EnumerateArray())
                {
                    var si = s.GetProperty("surface").GetInt32(); var expected = s.GetProperty("rays")[p];
                    var actual = h.FirstOrDefault(v => v.SurfaceNumber == si);
                    if (expected.GetProperty("error").GetInt32() != 0 || expected.GetProperty("vignette").GetInt32() != 0
                        || actual is null || actual.Vignetted || !actual.RefractiveIndexBefore.HasValue) continue;
                    comparisons++;
                    var frame = optic.SurfaceGroup.Items[si].CoordinateSystem;
                    var position = frame.ToLocalPoint(actual.Position); var direction = frame.ToLocalDirection(actual.Direction);
                    foreach (var (value, key) in new[] { (position.X, "x"), (position.Y, "y"), (position.Z, "z") })
                    {
                        var e = expected.GetProperty(key).GetDouble() * scale;
                        var delta = Math.Abs(value - e); maximumPosition = Math.Max(maximumPosition, delta);
                        if (delta > 1e-6 + 1e-6 * Math.Abs(e)) positionFailures++;
                    }
                    foreach (var (value, key) in new[] { (direction.X, "l"), (direction.Y, "m"), (direction.Z, "n") })
                    {
                        var e = expected.GetProperty(key).GetDouble() * signs[si];
                        var delta = Math.Abs(value - e); maximumDirection = Math.Max(maximumDirection, delta);
                        if (delta > 1e-8 + 1e-6 * Math.Abs(e)) directionFailures++;
                    }
                }
            }
            var conclusion = acceptance + firstClip + positionFailures + directionFailures == 0 && comparisons > 0 ? "Pass" : "Difference";
            rows.Add(new { id, lens, wave, conclusion, inputs = histories.Length, comparisons, acceptance, firstClip,
                positionFailures, directionFailures, maximumPosition, maximumDirection, nativeSha256 = JsonFiles.Hash(File.ReadAllBytes(Path.Combine(baseline, "native/ray-audit.json"))) });
            JsonFiles.Write(Path.Combine(output, "summary.json"), rows);
            Console.WriteLine($"{id}: {conclusion}, acceptance={acceptance}, position={maximumPosition:R}, direction={maximumDirection:R}");
        }
    }
}

using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Normalization;

internal static class SpectrometerControl
{
    internal static void Run(string root, IReadOnlyDictionary<int, JsonElement> rows)
    {
        var entry = AnalysisComparisonRegistry.Get("Optical Path Difference");
        var results = new List<object>();
        foreach (var id in new[] { 458, 466 })
        {
            var row = rows[id];
            var original = row.GetProperty("request").Deserialize<CanonicalAnalysisRequest>(JsonFiles.Options)!;
            var wave = row.GetProperty("wavelengthIdentity");
            var micrometers = N(wave, "selectedMicrometers");
            var request = original with { RayCount = original.RayCount * 8 };
            var actualPath = At($"spectro-{id}-dense/data.json");
            var actual = ResultNormalizer.Workbench(actualPath, entry, request).Series.Single(c => c.Id == "sagittal");
            var native = ResultNormalizer.Zemax(At($"opd-{id}-x8/data.json"), entry, request, actualPath).Series.Single(c => c.Id == "sagittal");
            if (!actual.X.Zip(native.X).All(p => Math.Abs(p.First - p.Second) <= 1e-12))
                throw new InvalidDataException("Spectrometer neighborhood coordinates differ.");
            var neighborhood = Enumerable.Range(0, native.X.Length).Where(i => native.X[i] is >= -.331 and <= -.249)
                .Select(i => new { pupil = native.X[i], core = actual.Y[i], native = native.Y[i],
                    absoluteErrorWaves = Math.Abs(actual.Y[i] - native.Y[i]),
                    signedErrorNanometers = (actual.Y[i] - native.Y[i]) * micrometers * 1000 }).ToArray();

            var fan = id - 1;
            var point = Read($"{fan}/point-reference.json");
            var inputs = point.GetProperty("inputs");
            var inputIndex = Enumerable.Range(1, inputs.GetArrayLength() - 1).Single(i =>
                inputs[i].GetProperty("role").GetString() == "sagittal" && Math.Abs(N(inputs[i], "coordinate") + .3) <= 1e-12);
            var input = inputs[inputIndex];
            var geometry = Read($"{fan}/image-geometry.json");
            var opd = point.GetProperty("surfaces").EnumerateArray().Last().GetProperty("rays")[inputIndex];
            var opdc = Read($"{fan}/opdc.json").GetProperty("rows")[inputIndex];
            var optic = Optic.FromSnapshot(JsonFiles.Read<OpticSnapshot>(At("spectro-snapshot.json")));
            var signs = NativeReflectionConvention.PropagationSigns(Read($"{fan}/model.json"));
            var chief = Trace(inputs[0]);
            var traced = Trace(input);
            var stages = point.GetProperty("surfaces").EnumerateArray().Select(s =>
            {
                var index = s.GetProperty("surface").GetInt32();
                var isImage = index == optic.SurfaceGroup.Items.Count - 1;
                var rays = isImage ? geometry : s.GetProperty("rays");
                var raw = rays[inputIndex];
                var expectedChief = rays[0];
                var ray = traced.Single(r => r.SurfaceNumber == index);
                var reference = chief.Single(r => r.SurfaceNumber == index);
                var frame = optic.SurfaceGroup.Items[index].CoordinateSystem;
                var p = frame.ToLocalPoint(ray.Position);
                var d = frame.ToLocalDirection(ray.Direction);
                var nativeDifferentialOpl = N(raw, "opdWaves") - N(expectedChief, "opdWaves");
                var coreDifferentialOpl = ray.CumulativeOpticalPathLength - reference.CumulativeOpticalPathLength;
                var phaseInclusive = (ray.PhaseInclusiveOpticalPathLength ?? ray.CumulativeOpticalPathLength)
                    - (reference.PhaseInclusiveOpticalPathLength ?? reference.CumulativeOpticalPathLength);
                return new { surface = index, coreVignetted = ray.Vignetted, nativeError = N(raw, "error"), nativeVignette = N(raw, "vignette"),
                    corePosition = new[] { p.X, p.Y, p.Z }, nativePosition = new[] { N(raw, "x"), N(raw, "y"), N(raw, "z") },
                    coreDirection = new[] { d.X, d.Y, d.Z }, nativeDirection = new[] { N(raw, "l") * signs[index], N(raw, "m") * signs[index], N(raw, "n") * signs[index] },
                    nativeDifferentialOplMillimeters = nativeDifferentialOpl, coreDifferentialOplMillimeters = coreDifferentialOpl,
                    coreDifferentialPhaseInclusiveMillimeters = phaseInclusive };
            }).ToArray();
            results.Add(new { id, wavelengthMicrometers = micrometers, primaryMicrometers = N(wave, "primaryMicrometers"),
                input, nativeCurrentAndChiefWaves = N(opd, "opdWaves"), nativeOpdcWaves = N(opdc, "opdWaves"),
                matchedPlotWaves = native.Y[Array.FindIndex(native.X, x => Math.Abs(x + .3) <= 1e-12)], stages, neighborhood });

            IReadOnlyList<OpticalSystemDesign.Core.Rays.RayTraceSample> Trace(JsonElement ray)
            {
                var bundle = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(N(ray, "hx"), N(ray, "hy"),
                    N(ray, "px"), N(ray, "py"), micrometers, aimAtStop: original.UseRayAiming);
                return optic.SequentialRayTracer.Trace(bundle).RayHistories.Single();
            }
        }
        JsonFiles.Write(At("spectrometer-controls.json"), results);

        string At(string relative) => Path.Combine(root, relative);
        JsonElement Read(string relative) => JsonFiles.Read<JsonElement>(At(relative));
        static double N(JsonElement element, string key) => element.GetProperty(key).GetDouble();
    }
}

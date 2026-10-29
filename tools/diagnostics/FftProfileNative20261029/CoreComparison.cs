using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Coordinates;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Metrics;
using OpticalSystemDesign.ZemaxComparison.Normalization;
using OpticalSystemDesign.ZemaxComparison.Tests;

internal static class CoreComparison
{
    internal static int Run(string repository, string capture, string output)
    {
        if (Directory.Exists(output)) throw new IOException("Diagnostic output already exists: " + output);
        Directory.CreateDirectory(output);
        var archivePath = Path.Combine(repository, "validation/rechecks/2026-10-27/afocal-fft-energy/evidence-current.zip");
        using (var input = File.OpenRead(archivePath))
            if (Convert.ToHexStringLower(SHA256.HashData(input)) != "235f113b4633fce7341e837da03850ac3c8cff14a48c32f4b4731e7250d7f4b1")
                throw new InvalidDataException("Frozen source archive hash mismatch");
        using var archive = ZipFile.OpenRead(archivePath);
        JsonElement Read(string name)
        {
            using var stream = archive.GetEntry(name)!.Open();
            using var document = JsonDocument.Parse(stream);
            return document.RootElement.Clone();
        }
        var summaries = new List<object>();
        var sourceOptics = Environment.GetEnvironmentVariable("OPTICAL_FFT_SOURCE_IMPORT") == "1"
            ? new ZemaxZmxImporter().ImportConfigurationSetFileAsync(Path.Combine(capture, "Binocular_System.ZMX")).GetAwaiter().GetResult().Configurations : null;
        foreach (var configuration in new[] { 1, 2 })
        {
            var model = $"003-binocular_system-c{configuration}";
            var optic = sourceOptics is not null ? sourceOptics[configuration - 1] : Optic.FromSnapshot(Read("artifacts/validation/afocal-recompare-20261026/baseline-v4/models/" + model + ".json").Deserialize<OpticSnapshot>(JsonFiles.Options)!);
            var nativeDirectory = Path.Combine(capture, $"binocular-c{configuration}-boundary-and-seam33");
            var request = JsonFiles.Read<JsonElement>(Path.Combine(capture, $"binocular-c{configuration}-boundary-and-seam33.request.json"));
            var expected = JsonFiles.Read<JsonElement>(Path.Combine(nativeDirectory, "image-geometry.json")).EnumerateArray().ToArray();
            var observer = typeof(PolygonGroupGeometry).GetProperty("DiagnosticObserver", BindingFlags.Static | BindingFlags.NonPublic)!;
            var rays = new List<object>();
            var index = 0;
            foreach (var point in request.GetProperty("pointInputs").EnumerateArray())
            {
                var px = point.GetProperty("px").GetDouble();
                var py = point.GetProperty("py").GetDouble();
                if (Math.Abs(px) > 1 || Math.Abs(py) > 1)
                {
                    rays.Add(new { index, px, py, coreStatus = "OutsideNormalizedPupilApiDomain", native = expected[index] });
                    index++;
                    continue;
                }
                var events = new List<object>();
                observer.SetValue(null, (Action<object>)(entry => events.Add(entry)));
                var bundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(0, 0,
                    optic.Wavelengths[1].Micrometers, [new PupilSample(px, py, 1)], optic.RayAimingEnabled);
                OpticalSystemDesign.Core.Rays.RayTraceSample actual;
                try { actual = optic.SequentialRayTracer.TraceFinalSamples(bundle).Single()!; }
                finally { observer.SetValue(null, null); }
                var frame = optic.SurfaceGroup.Items[^1].CoordinateSystem;
                var position = frame.ToLocalPoint(actual.Position);
                var direction = frame.ToLocalDirection(actual.Direction);
                var reference = expected[index];
                var positionDifference = position - new Vector3D(reference.GetProperty("x").GetDouble(), reference.GetProperty("y").GetDouble(), reference.GetProperty("z").GetDouble());
                var directionDifference = direction - new Vector3D(reference.GetProperty("l").GetDouble(), reference.GetProperty("m").GetDouble(), reference.GetProperty("n").GetDouble());
                rays.Add(new
                {
                    index,
                    px,
                    py,
                    positionDifference,
                    directionDifference,
                    positionError = positionDifference.Length,
                    directionError = directionDifference.Length,
                    actual.Vignetted,
                    actual.Intensity,
                    native = reference,
                    events
                });
                index++;
            }
            JsonFiles.Write(Path.Combine(output, $"rays-c{configuration}.json"), new { configuration, rays });
        }
        var models = new[] { "001-afocal-riflescope-c1", "002-beam-expander-c1", "003-binocular_system-c1", "003-binocular_system-c2", "004-apochromat_prism-c1" };
        var ids = new[] { "0011-001-afocal-riflescope-c1-primary-f1-w2-fft-psf-cross-section", "0041-002-beam-expander-c1-primary-f1-w1-fft-psf-cross-section", "0063-003-binocular_system-c1-primary-f1-w2-fft-psf-cross-section", "0093-003-binocular_system-c2-primary-f1-w2-fft-psf-cross-section", "0123-004-apochromat_prism-c1-primary-f1-w3-fft-psf-cross-section" };
        Environment.SetEnvironmentVariable("OPTICAL_FFT_PROFILE_DIAGNOSTICS", Path.Combine(output, "diagnostics"));
        for (var i = 0; i < models.Length; i++)
        {
            var id = ids[i];
            var request = Read("artifacts/validation/afocal-fft-energy-20261027/final-150-v2/" + id + "/workbench/request.json").GetProperty("request").Deserialize<CanonicalAnalysisRequest>(JsonFiles.Options)!;
            var optic = Optic.FromSnapshot(Read("artifacts/validation/afocal-recompare-20261026/baseline-v4/models/" + models[i] + ".json").Deserialize<OpticSnapshot>(JsonFiles.Options)!);
            if (sourceOptics is not null && i is 2 or 3) optic = sourceOptics[i - 2];
            var nativeDirectory = Path.Combine(output, id);
            Directory.CreateDirectory(nativeDirectory);
            foreach (var name in new[] { "data.json", "data.txt" })
            {
                using var source = archive.GetEntry("artifacts/validation/afocal-recompare-20261026/expanded/" + id + "/native/" + name)!.Open();
                using var destination = File.Create(Path.Combine(nativeDirectory, name));
                source.CopyTo(destination);
            }
            var data = new WorkbenchRuntime(optic).BuildAnalysisData("FFT PSF Cross Section", request.WorkbenchSettings);
            var actual = ExtendedResultNormalizer.Workbench(data, request);
            var expected = ExtendedResultNormalizer.Zemax(Path.Combine(nativeDirectory, "data.json"), request);
            var matched = ComparisonMetrics.Align(actual.Series.Single(), expected.Series.Single(), []);
            var ledger = JsonFiles.Read<JsonElement>(Path.Combine(repository, "validation/rechecks/2026-10-29/final-core-170/afocal-comparison.json"));
            var gate = ledger.GetProperty("cases").EnumerateArray().Single(row => row.GetProperty("id").GetString() == id)
                .GetProperty("effectiveTolerances").GetProperty("Irradiance").Deserialize<Tolerances>(JsonFiles.Options)!;
            var violations = matched.Workbench.Zip(matched.Zemax, (a, b) => Math.Abs(a - b) > gate.Absolute + gate.Relative * Math.Abs(b)).Count(value => value);
            var maximum = matched.Workbench.Zip(matched.Zemax, (a, b) => Math.Abs(a - b)).Max();
            summaries.Add(new { id, request.Fingerprint, maximum, violations, matched.Coverage });
            FftProfileDiagnostics.WriteIfRequested(id, optic, request, data, actual, expected, archive);
        }
        JsonFiles.Write(Path.Combine(output, "summary.json"), summaries);
        Console.WriteLine(JsonSerializer.Serialize(summaries, JsonFiles.Options));
        return 0;
    }
}

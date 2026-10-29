using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.ZemaxComparison;

internal static class NativeAssessment
{
    internal static int Run(string repository, string baseDirectory, string output)
    {
        var prefix = Path.Combine(baseDirectory, "afocal-native-resume-20261029");
        var controls = prefix + "-reference-controls";
        var cohorts = new[] { "-02", "-paths-02", "-seam-sweep", "-glue-controls", "-reference-controls" };
        var executions = cohorts.SelectMany(suffix => Read(Path.Combine(prefix + suffix, "execution.json")).EnumerateArray()
            .Select(row => new
            {
                cohort = suffix,
                id = row.GetProperty("id").GetString(),
                exitCode = row.GetProperty("result").GetProperty("ExitCode").GetInt32(),
                sourceUnchanged = row.GetProperty("sourceUnchanged").GetBoolean()
            })).ToArray();
        if (executions.Length != 20 || executions.Any(row => row.exitCode != 0 || !row.sourceUnchanged))
            throw new InvalidDataException("Incomplete native execution set");
        var tags = new[] { "0013-001-afocal-riflescope-c1-primary-f1-w2-encircled-energy", "0065-003-binocular_system-c1-primary-f1-w2-encircled-energy", "0095-003-binocular_system-c2-primary-f1-w2-encircled-energy" };
        var centers = tags.Select(tag =>
        {
            var centroid = Path.Combine(controls, tag + "-Centroid-field2");
            var chief = Path.Combine(controls, tag + "-ChiefRay-field2");
            var settingsA = Read(Path.Combine(centroid, "captured-settings.json"));
            var settingsB = Read(Path.Combine(chief, "captured-settings.json"));
            var same = JsonElement.DeepEquals(Read(Path.Combine(centroid, "data.json")).GetProperty("dataSeries"), Read(Path.Combine(chief, "data.json")).GetProperty("dataSeries"));
            if (settingsA.GetProperty("properties").GetProperty("ReferTo").GetString() != "Centroid"
                || settingsB.GetProperty("properties").GetProperty("ReferTo").GetString() != "ChiefRay"
                || settingsA.GetProperty("selectors").GetProperty("Field").GetInt32() != 2
                || settingsB.GetProperty("selectors").GetProperty("Field").GetInt32() != 2)
                throw new InvalidDataException("Native reference selector mismatch");
            return new { tag, dataSeriesExactlyEqual = same, centroidSettings = settingsA.GetProperty("selectors"), chiefSettings = settingsB.GetProperty("selectors"), freshInstances = true };
        }).ToArray();
        using var zip = ZipFile.OpenRead(Path.Combine(repository, "validation/rechecks/2026-10-27/afocal-fft-energy/evidence-current.zip"));
        var sources = new Dictionary<string, string>
        {
            ["binocular-c2-original-fft-profile"] = "artifacts/validation/afocal-recompare-20261026/expanded/0093-003-binocular_system-c2-primary-f1-w2-fft-psf-cross-section/native/data.json",
            ["binocular-c2-original-complex-Real"] = "artifacts/validation/afocal-fft-energy-20261027/native-binocular/binocular-c2-Real/data.json",
            ["binocular-c2-original-complex-Imaginary"] = "artifacts/validation/afocal-fft-energy-20261027/native-binocular/binocular-c2-Imaginary/data.json"
        };
        var freshFft = sources.Select(pair =>
        {
            using var stream = zip.GetEntry(pair.Value)!.Open();
            using var frozen = JsonDocument.Parse(stream);
            var fresh = Read(Path.Combine(controls, pair.Key, "data.json"));
            return new
            {
                id = pair.Key,
                frozenMember = pair.Value,
                sameSeries = JsonElement.DeepEquals(frozen.RootElement.GetProperty("dataSeries"), fresh.GetProperty("dataSeries")),
                sameGrids = JsonElement.DeepEquals(frozen.RootElement.GetProperty("dataGrids"), fresh.GetProperty("dataGrids"))
            };
        }).ToArray();
        var matchingRays = new[] { 1, 2 }.Select(configuration =>
        {
            var rays = Read(Path.Combine(prefix + "-compare-final", $"rays-c{configuration}.json")).GetProperty("rays").EnumerateArray().ToArray();
            var matched = rays.Where(ray => ray.TryGetProperty("directionError", out _)).ToArray();
            return new
            {
                configuration,
                nativeProbeCount = rays.Length,
                coreComparableCount = matched.Length,
                outsideCoreNormalizedApiDomain = rays.Length - matched.Length,
                maximumPositionDifferenceMillimeters = matched.Max(ray => ray.GetProperty("positionError").GetDouble()),
                maximumDirectionDifference = matched.Max(ray => ray.GetProperty("directionError").GetDouble())
            };
        }).ToArray();
        var originalSweep = Read(Path.Combine(prefix + "-seam-sweep", "binocular-c2-seam-sweep-pupil/nsc-paths.json")).GetProperty("rays");
        var glue = new[] { "small", "large" }.Select(label =>
        {
            var directory = Path.Combine(prefix + "-glue-controls", $"binocular-c2-glue-{label}-pupil");
            return new
            {
                label,
                settings = Read(Path.Combine(directory, "nsc-settings-control.json")),
                sameRaysAsOriginalGlueDistance = JsonElement.DeepEquals(originalSweep, Read(Path.Combine(directory, "nsc-paths.json")).GetProperty("rays"))
            };
        }).ToArray();
        var finalProfiles = Read(Path.Combine(prefix + "-compare-final", "summary.json"));
        var restoredProfileSummaryExactlyEqual = JsonElement.DeepEquals(finalProfiles, Read(Path.Combine(prefix + "-compare-before", "summary.json")));
        if (!restoredProfileSummaryExactlyEqual) throw new InvalidDataException("Reverted Core did not reproduce its original five profiles");
        var allProfilePointsPassed = finalProfiles.EnumerateArray().All(row => row.GetProperty("violations").GetInt32() == 0);
        var report = new
        {
            status = "native_capture_completed_precision_issue_open",
            date = "2026-10-29",
            executions,
            nativeVersion = Read(Path.Combine(prefix + "-02", tags[1] + "-pupil/environment.json")),
            nscContext = Read(Path.Combine(prefix + "-02", tags[1] + "-pupil/afocal-nsc-context.json")),
            polygonDependencies = Read(Path.Combine(prefix + "-02", tags[1] + "-pupil/polygon-dependencies.json")),
            referenceControls = centers,
            freshFft,
            matchingRays,
            glueControls = glue,
            finalProfiles,
            allProfilePointsPassed,
            restoredProfileSummaryExactlyEqual,
            rejectedHypothesis = Read(Path.Combine(prefix + "-compare-normalized", "summary.json")),
            productOpticalSourceChanged = false,
            toleranceReferenceSamplingChanged = false,
            coreAssemblySha256 = Hash(typeof(OpticalSystemDesign.Core.Optic).Assembly.Location),
            polygonSourceSha256 = Hash(Path.Combine(repository, "src/OpticalSystemDesign.Core/Geometries/PolygonGroupGeometry.cs")),
            nativeInternalFacetIdsAvailable = false,
            interpretation = "NSC reflection planes can be inferred from incoming/outgoing directions; internal facet IDs and FFT pixel launch mapping remain unavailable. Successful capture is not an optical pass."
        };
        JsonFiles.Write(output, report);
        Console.WriteLine($"20 native captures complete; 3 reference pairs and 3 frozen FFT outputs checked; pointwise precision passed: {allProfilePointsPassed}");
        return 0;
    }
    private static JsonElement Read(string path) => JsonFiles.Read<JsonElement>(path);
    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Metrics;
using OpticalSystemDesign.ZemaxComparison.Normalization;
using OpticalSystemDesign.ZemaxComparison.Workbench;
using Xunit.Abstractions;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

public sealed class AfocalPsfContractTests(ITestOutputHelper output)
{
    private static readonly Lazy<Dictionary<string, byte[]>> Files = new(() =>
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Fixtures/afocal-psf-contract");
        var manifestBytes = File.ReadAllBytes(Path.Combine(directory, "manifest.json"));
        using var manifest = JsonDocument.Parse(manifestBytes);
        var source = Path.Combine(directory, "source-evidence.zip");
        using (var stream = File.OpenRead(source))
            Assert.Equal(manifest.RootElement.GetProperty("sourceArchiveSha256").GetString(),
                Convert.ToHexStringLower(SHA256.HashData(stream)));
        using var zip = ZipFile.OpenRead(source);
        var files = new Dictionary<string, byte[]> { ["manifest.json"] = manifestBytes };
        foreach (var entry in manifest.RootElement.GetProperty("entries").EnumerateArray())
        {
            using var stream = zip.GetEntry(entry.GetProperty("sourcePath").GetString()!)!.Open();
            using var bytes = new MemoryStream();
            stream.CopyTo(bytes);
            var data = bytes.ToArray();
            Assert.Equal(entry.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(data)));
            files.Add(entry.GetProperty("path").GetString()!, data);
        }
        return files;
    });

    private static JsonElement Manifest() => JsonSerializer.Deserialize<JsonElement>(Files.Value["manifest.json"]);
    public static IEnumerable<object[]> Cases() => Manifest().GetProperty("cases").EnumerateArray()
        .Select(row => new object[] { row.GetProperty("id").GetString()! });

    public static IEnumerable<object[]> BeamPsfCases() => Manifest().GetProperty("cases").EnumerateArray()
        .Where(row => row.GetProperty("lens").GetString() == "Afocal/Beam Expander.ZMX"
            && row.GetProperty("key").GetString() == "PSF")
        .Select(row => new object[] { row.GetProperty("id").GetString()! });

    [Theory]
    [MemberData(nameof(BeamPsfCases))]
    public async Task GaussianBeamPsfPeakMeetsOriginalToleranceWithoutPeakFitting(string id)
    {
        var row = Manifest().GetProperty("cases").EnumerateArray().Single(r => r.GetProperty("id").GetString() == id);
        var request = row.GetProperty("request").Deserialize<CanonicalAnalysisRequest>(JsonFiles.Options)!;
        var directory = WriteNative(id);
        try
        {
            var entry = AnalysisComparisonRegistry.Get("PSF");
            var expected = ResultNormalizer.Zemax(Path.Combine(directory, "data.json"), entry, request).Grids.Single();
            var optic = (await WorkbenchExecutor.Import(Path.Combine(AppContext.BaseDirectory,
                "Fixtures/afocal-repair/sources", row.GetProperty("lens").GetString()!))).Configurations[request.Configuration - 1];
            var data = new WorkbenchRuntime(optic).BuildAnalysisData("PSF", request.WorkbenchSettings);
            var workbench = Path.Combine(directory, "workbench.json");
            JsonFiles.Write(workbench, data);
            var actual = ResultNormalizer.Workbench(workbench, entry, request).Grids.Single();
            var matched = ComparisonMetrics.Align(actual, expected, []);
            var policy = ComparisonConfiguration.Load(Path.Combine(AppContext.BaseDirectory, "comparison-settings.json"))
                .Analyses["PSF"].Quantities["Irradiance"];
            var errors = matched.Workbench.Zip(matched.Zemax, (a, b) => Math.Abs(a - b)).ToArray();
            var violations = errors.Where((e, i) => e > policy.Absolute + policy.Relative * Math.Abs(matched.Zemax[i])).Count();
            output.WriteLine(JsonSerializer.Serialize(new { id, samples = errors.Length, violations,
                actualPeak = matched.Workbench.Max(), nativePeak = matched.Zemax.Max(),
                maximumAbsolute = errors.Max(), windowSumRatio = matched.Workbench.Sum() / matched.Zemax.Sum(),
                idealPeakRatio = Convert.ToDouble(data.Values["UnaberratedPeakRatio"]),
                metric = ComparisonMetrics.Calculate("grid", "Dimensionless", matched, policy) }));
            BeamPsfShapeDiagnostics.WriteIfRequested(id, optic, request, expected, actual);
            // Apply the unchanged pointwise gate to the peak explicitly, rather
            // than relying on whole-grid NRMSE to detect its previous 23% loss.
            // The same gate is also required for every matched pixel below.
            var nativePeak = matched.Zemax.Max();
            Assert.InRange(Math.Abs(matched.Workbench.Max() - nativePeak), 0,
                policy.Absolute + policy.Relative * Math.Abs(nativePeak));
            Assert.Equal(Conclusion.Pass, ComparisonMetrics.Calculate("grid", "Dimensionless", matched, policy).Conclusion);
            Assert.Equal(1, matched.Coverage);
            // This captured beam is slightly aberrated: Normalize=false must retain that loss.
            Assert.True(matched.Workbench.Max() < 0.999999);
            Assert.Equal(0, violations);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task FormalNormalizerHonorsUnchangedGridGatesAndCurrentOutcomes(string id)
    {
        var row = Manifest().GetProperty("cases").EnumerateArray().Single(r => r.GetProperty("id").GetString() == id);
        var request = row.GetProperty("request").Deserialize<CanonicalAnalysisRequest>(JsonFiles.Options)!;
        var outcomes = JsonFiles.Read<JsonElement>(Path.Combine(AppContext.BaseDirectory, "Fixtures/afocal-current-grid-expectations.json"));
        Assert.Equal(outcomes.GetProperty("originalManifestSha256").GetString(),
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,
                "Fixtures/afocal-psf-contract/manifest.json")))));
        Assert.Equal(Manifest().GetProperty("cases").GetArrayLength(), outcomes.GetProperty("cases").GetArrayLength());
        var current = outcomes.GetProperty("cases").EnumerateArray().Single(c => c.GetProperty("id").GetString() == id);
        Assert.Equal(request.Fingerprint, current.GetProperty("requestFingerprint").GetString());
        Assert.Equal(row.GetProperty("originalConclusion").GetString(), current.GetProperty("originalConclusion").GetString());
        var expectedConclusion = current.GetProperty("currentConclusion").GetString();
        var directory = WriteNative(id);
        try
        {
            var entry = AnalysisComparisonRegistry.Get(request.CanonicalAnalysisKey);
            var expected = ResultNormalizer.Zemax(Path.Combine(directory, "data.json"), entry, request).Grids.Single();
            Assert.Equal(new Axis("IncidentAngle", "Milliradian"), expected.XAxis);
            Assert.Equal(expected.XAxis, expected.YAxis);
            var optic = (await WorkbenchExecutor.Import(Path.Combine(AppContext.BaseDirectory,
                "Fixtures/afocal-repair/sources", row.GetProperty("lens").GetString()!))).Configurations[request.Configuration - 1];
            var data = new WorkbenchRuntime(optic).BuildAnalysisData(request.CanonicalAnalysisKey, request.WorkbenchSettings);
            var workbench = Path.Combine(directory, "workbench.json");
            JsonFiles.Write(workbench, data);
            var actual = ResultNormalizer.Workbench(workbench, entry, request).Grids.Single();
            var policy = ComparisonConfiguration.Load(Path.Combine(AppContext.BaseDirectory, "comparison-settings.json"))
                .Analyses[request.CanonicalAnalysisKey].Quantities["Irradiance"];
            var xError = actual.X.Zip(expected.X, (a, b) => Math.Abs(a - b)).Max();
            var yError = actual.Y.Zip(expected.Y, (a, b) => Math.Abs(a - b)).Max();
            output.WriteLine($"{id}: original={row.GetProperty("originalConclusion").GetString()}, maxAngularCoordinateError=({xError:R}, {yError:R}) mrad");
            if (expectedConclusion == "Incomparable")
            {
                var error = Assert.Throws<InvalidDataException>(() => ComparisonMetrics.Align(actual, expected, []));
                Assert.Contains("Grid coordinates differ", error.Message);
            }
            else
            {
                var matched = ComparisonMetrics.Align(actual, expected, []);
                var metric = ComparisonMetrics.Calculate("grid", "Dimensionless", matched, policy);
                output.WriteLine($"NRMSE={metric.Nrmse:R}; coverage={matched.Coverage:R}; conclusion={metric.Conclusion}");
                Assert.Equal(Conclusion.Pass, metric.Conclusion);
                Assert.True(current.GetProperty("pointwiseRequired").GetBoolean());
                Assert.All(matched.Workbench.Zip(matched.Zemax), pair =>
                    Assert.True(Math.Abs(pair.First - pair.Second) <= policy.Absolute + policy.Relative * Math.Abs(pair.Second)));
            }
            var run = new AnalysisRun { Key = request.CanonicalAnalysisKey, Directory = id, Request = request };
            var settings = ComparisonConfiguration.Load(Path.Combine(AppContext.BaseDirectory, "comparison-settings.json"))
                .Analyses[request.CanonicalAnalysisKey];
            ComparisonRunner.Compare(run, entry, settings, new NumericResult { Grids = [actual] },
                new NumericResult { Grids = [expected] }, directory);
            Assert.Equal(expectedConclusion, run.Conclusion.ToString());
            if (run.Conclusion == Conclusion.Incomparable)
            {
                Assert.Empty(run.Metrics);
                Assert.Contains("Grid coordinates differ", run.Reason);
            }
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("PSF", "ArcMinute")]
    [InlineData("Huygens PSF", "ArcSecond")]
    [InlineData("PSF", "Microradian")]
    public void NativePayloadConvertsExplicitAngularPitchWithoutChangingRequest(string key, string unit)
    {
        var row = Manifest().GetProperty("cases")[0];
        var request = row.GetProperty("request").Deserialize<CanonicalAnalysisRequest>(JsonFiles.Options)! with
        { CanonicalAnalysisKey = key, NativeAfocalAngleUnit = unit, ImageDeltaMicrometers = 0.05 };
        var identity = request.Fingerprint;
        var payload = new Dictionary<string, object?> { ["imageDeltaMicrometers"] = request.ImageDeltaMicrometers };
        AfocalAnalysisContracts.MapNativePayload(payload, request);
        Assert.Equal(0.05 * PhysicalNormalization.UnitScale("Milliradian", unit), (double)payload["imageDeltaMicrometers"]!, 13);
        Assert.Equal(identity, request.Fingerprint);
    }

    private static string WriteNative(string id)
    {
        var directory = Path.Combine(Path.GetTempPath(), "afocal-psf-contract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        foreach (var name in new[] { "data.json", "model.json", "captured-settings.json" })
            File.WriteAllBytes(Path.Combine(directory, name), Files.Value[id + "/" + name]);
        return directory;
    }

    [Theory]
    [InlineData("properties", "Type", "\"Real\"")]
    [InlineData("properties", "Normalize", "true")]
    [InlineData("properties", "UsePolarization", "true")]
    [InlineData("properties", "Rotation", "\"CW90\"")]
    [InlineData("properties", "ImageDelta", "0.05")]
    [InlineData("properties", "SampleSize", "\"PsfS_32x32\"")]
    [InlineData("properties", "OutputSize", "\"PsfS_64x64\"")]
    [InlineData("selectors", "Wavelength", "1")]
    [InlineData("selectors", "Field", "2")]
    [InlineData("selectors", "Surface", "1")]
    [InlineData("request", "configuration", "2")]
    public void RejectsChangedNativePhysicalContract(string group, string key, string value)
    {
        var row = Manifest().GetProperty("cases")[0];
        var request = row.GetProperty("request").Deserialize<CanonicalAnalysisRequest>(JsonFiles.Options)!;
        var directory = WriteNative(row.GetProperty("id").GetString()!);
        try
        {
            var path = Path.Combine(directory, "captured-settings.json");
            var capture = JsonNode.Parse(File.ReadAllText(path))!;
            capture[group]![key] = JsonNode.Parse(value);
            File.WriteAllText(path, capture.ToJsonString());
            Assert.Throws<InvalidDataException>(() => ResultNormalizer.Zemax(Path.Combine(directory, "data.json"),
                AnalysisComparisonRegistry.Get(request.CanonicalAnalysisKey), request));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void RejectsUnsupportedRequestsBeforeNativeDispatch(int variant)
    {
        var request = Manifest().GetProperty("cases")[0].GetProperty("request").Deserialize<CanonicalAnalysisRequest>(JsonFiles.Options)!;
        request = variant switch
        {
            0 => request with { Polarization = true },
            1 => request with { Reference = "Centroid" },
            2 => request with { Wavelength = 0 },
            _ => request with { ImageDeltaMicrometers = -1 }
        };
        Assert.Throws<InvalidDataException>(() => AfocalAnalysisContracts.MapNativePayload([], request));
    }

    [Theory]
    [InlineData("PSF")]
    [InlineData("Huygens PSF")]
    public void AutomaticPsfPitchIsAnExplicitConfigurationMode(string key)
    {
        var config = ComparisonConfiguration.Load(Path.Combine(AppContext.BaseDirectory, "comparison-settings.json"));
        config.Analyses[key] = config.Analyses[key] with { ImageDeltaMicrometers = 0 };
        config.Validate();
        config.Analyses[key] = config.Analyses[key] with { ImageDeltaMicrometers = -0.05 };
        Assert.Throws<ArgumentException>(config.Validate);
    }
}

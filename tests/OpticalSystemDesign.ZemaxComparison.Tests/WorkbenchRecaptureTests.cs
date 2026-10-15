using System.Text.Json;
using System.Text.Json.Nodes;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.ZemaxComparison.Workbench;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

[Collection("Workbench recapture console")]
public sealed class WorkbenchRecaptureTests
{
    [Theory]
    [InlineData("0", "1")]
    [InlineData("-1", "1")]
    [InlineData("5", "4")]
    [InlineData("word", "5")]
    [InlineData("1", "2147483648")]
    public void InvalidIntervalsAreRejectedBeforeOutputIsCreated(string start, string end)
    {
        Assert.Throws<ArgumentException>(() => WorkbenchRecaptureOptions.Parse(["镜头.zmx", "settings.json", "output", start, end]));
        Assert.Throws<ArgumentException>(() => WorkbenchRecaptureOptions.Parse(["source.zmx"]));
        Assert.Throws<ArgumentException>(() => WorkbenchRecaptureOptions.Parse(["source.staropt", "settings.json", "output"]));
    }

    [Fact]
    public async Task PartialRecapturePreservesTheHistoricalViewSchemaAndActualRuntimeResults()
    {
        using var scope = new Scope();
        var sourceBefore = File.ReadAllBytes(scope.Source);
        var settingsBefore = File.ReadAllBytes(scope.Settings);
        Assert.Equal(0, await WorkbenchRecapture.RunAsync(scope.Options("First Order")));
        using var manifest = scope.Manifest();
        var rows = manifest.RootElement.GetProperty("analyses").EnumerateArray().ToArray();
        var selected = Assert.Single(rows, row => row.GetProperty("status").GetString() == "captured");
        Assert.Equal("First Order", selected.GetProperty("name").GetString());
        Assert.Equal(new AnalysisCatalog(new Optic()).Names.Count, rows.Length);
        Assert.All(rows.Where(row => row.GetProperty("name").GetString() != "First Order"), row =>
        {
            Assert.Equal("not-requested", row.GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Null, row.GetProperty("output").ValueKind);
        });
        using var view = scope.View(selected);
        var expected = new WorkbenchRuntime(OpticalFormatCatalog.Import(File.ReadAllText(scope.Source), ".zmx"))
            .BuildAnalysisView("First Order");
        Assert.Equal(expected.Name, view.RootElement.GetProperty("name").GetString());
        Assert.Equal(expected.ReportText, view.RootElement.GetProperty("reportText").GetString());
        Assert.Equal(expected.Rows.Count, view.RootElement.GetProperty("rows").GetArrayLength());
        Assert.Equal("success", view.RootElement.GetProperty("outcome").GetString());
        Assert.True(view.RootElement.TryGetProperty("seriesList", out _));
        Assert.True(view.RootElement.TryGetProperty("plotPanes", out _));
        Assert.Equal(JsonFiles.Hash(sourceBefore), manifest.RootElement.GetProperty("sourceSha256").GetString());
        Assert.Equal(JsonFiles.Hash(settingsBefore), manifest.RootElement.GetProperty("settingsManifestSha256").GetString());
        Assert.Equal(sourceBefore, File.ReadAllBytes(scope.Source));
        Assert.Equal(settingsBefore, File.ReadAllBytes(scope.Settings));
    }

    [Fact]
    public async Task MatchingResultsOutsideTheIntervalAreReusedWithoutRewritingTheirBytes()
    {
        using var scope = new Scope();
        Assert.Equal(0, await WorkbenchRecapture.RunAsync(scope.Options("First Order")));
        using var previous = scope.Manifest();
        var prior = Find(previous, "First Order");
        var path = Path.Combine(scope.Output, prior.GetProperty("output").GetString()!);
        var before = File.ReadAllBytes(path);
        Assert.Equal(0, await WorkbenchRecapture.RunAsync(scope.Options("Seidel Coefficients")));
        using var current = scope.Manifest();
        var reused = Find(current, "First Order");
        Assert.True(reused.GetProperty("reused").GetBoolean());
        Assert.Equal("captured", reused.GetProperty("status").GetString());
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(JsonFiles.Hash(before), reused.GetProperty("outputSha256").GetString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ChangedSourceOrComputationFingerprintRequiresAnotherOutputDirectory(bool changeSource)
    {
        using var scope = new Scope();
        Assert.Equal(0, await WorkbenchRecapture.RunAsync(scope.Options("First Order")));
        if (changeSource) File.AppendAllText(scope.Source, "\n! changed source\n");
        else
        {
            var edited = JsonNode.Parse(File.ReadAllText(scope.ManifestPath))!;
            edited["codeFingerprint"] = "different binaries";
            File.WriteAllText(scope.ManifestPath, edited.ToJsonString());
        }
        var before = File.ReadAllBytes(scope.ManifestPath);
        await Assert.ThrowsAsync<IOException>(() => WorkbenchRecapture.RunAsync(scope.Options("First Order")));
        Assert.Equal(before, File.ReadAllBytes(scope.ManifestPath));
    }

    [Fact]
    public async Task TamperedReusableResultIsReportedAsFailureAndKeptForInspection()
    {
        using var scope = new Scope();
        Assert.Equal(0, await WorkbenchRecapture.RunAsync(scope.Options("First Order")));
        using var previous = scope.Manifest();
        var path = Path.Combine(scope.Output, Find(previous, "First Order").GetProperty("output").GetString()!);
        File.WriteAllText(path, "{\"name\":\"modified\"}");
        Assert.Equal(1, await WorkbenchRecapture.RunAsync(scope.Options("Seidel Coefficients")));
        using var current = scope.Manifest();
        var failed = Find(current, "First Order");
        Assert.Equal("failed", failed.GetProperty("status").GetString());
        Assert.Contains("checksum mismatch", failed.GetProperty("error").GetString());
        Assert.Equal("{\"name\":\"modified\"}", File.ReadAllText(path));
    }

    [Fact]
    public async Task LegacyThroughFocusSettingsReachTheCanonicalFactoryAndKeepTypedViewAxes()
    {
        using var scope = new Scope();
        JsonFiles.Write(scope.Settings, new
        {
            analyses = new[] { new
        {
            name = "Through Focus MTF",
            settings = new Dictionary<string, string>
            {
                ["Sampling"]="8", ["NumberOfSteps"]="2", ["FieldNumber"]="1", ["Frequency"]="50", ["DeltaFocus"]="0.1"
            }
        } }
        });
        Assert.Equal(0, await WorkbenchRecapture.RunAsync(scope.Options("Fourier Through Focus MTF")));
        using var manifest = scope.Manifest();
        var row = Find(manifest, "Fourier Through Focus MTF");
        Assert.Equal("8", row.GetProperty("settings").GetProperty("Sampling").GetString());
        using var view = scope.View(row);
        Assert.Equal(2, view.RootElement.GetProperty("seriesList").GetArrayLength());
        var curve = view.RootElement.GetProperty("seriesList")[0];
        Assert.Equal("defocus", curve.GetProperty("xQuantity").GetString());
        Assert.Equal("millimeter", curve.GetProperty("xUnit").GetString());
        Assert.Equal("modulation", curve.GetProperty("yQuantity").GetString());
        Assert.Equal("dimensionless", curve.GetProperty("yUnit").GetString());
    }

    [Fact]
    public async Task FrozenBaselineAndInputDirectoriesAreRejectedBeforeAnyOutputWrite()
    {
        using var scope = new Scope();
        await Assert.ThrowsAsync<IOException>(() => WorkbenchRecapture.RunAsync(scope.Options("First Order") with { Output = scope.Root }));
        Directory.CreateDirectory(Path.Combine(scope.Root, "analyses"));
        File.WriteAllText(Path.Combine(scope.Root, "manifest.json"), "{}");
        await Assert.ThrowsAsync<IOException>(() => WorkbenchRecapture.RunAsync(scope.Options("First Order")));
        Assert.False(Directory.Exists(scope.Output));
    }

    [Fact]
    public async Task ExistingUnrelatedOutputAndConcurrentWriterAreRejected()
    {
        using var scope = new Scope();
        Directory.CreateDirectory(scope.Output);
        var unrelated = Path.Combine(scope.Output, "user.txt");
        File.WriteAllText(unrelated, "keep");
        await Assert.ThrowsAsync<IOException>(() => WorkbenchRecapture.RunAsync(scope.Options("First Order")));
        Assert.Equal("keep", File.ReadAllText(unrelated));
        var cleanOutput = Path.Combine(scope.Root, "another-output");
        var options = scope.Options("First Order") with { Output = cleanOutput };
        Assert.Equal(0, await WorkbenchRecapture.RunAsync(options));
        var manifestPath = Path.Combine(cleanOutput, "current-manifest.json");
        var before = File.ReadAllBytes(manifestPath);
        using var held = new FileStream(Path.Combine(cleanOutput, ".recapture.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        await Assert.ThrowsAsync<IOException>(() => WorkbenchRecapture.RunAsync(options));
        Assert.Equal(before, File.ReadAllBytes(manifestPath));
    }

    [Fact]
    public async Task CancelledRequestDoesNotCreateAnOutputDirectory()
    {
        using var scope = new Scope();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => WorkbenchRecapture.RunAsync(scope.Options("First Order"), new CancellationToken(true)));
        Assert.False(Directory.Exists(scope.Output));
    }

    [Fact]
    public async Task CancellationPreservesUnvisitedVerifiedResultsAndTheirResumeIdentity()
    {
        using var scope = new Scope();
        Assert.Equal(0, await WorkbenchRecapture.RunAsync(scope.Options("First Order")));
        Assert.Equal(0, await WorkbenchRecapture.RunAsync(scope.Options("Seidel Coefficients")));
        using var previous = scope.Manifest();
        var seidel = Find(previous, "Seidel Coefficients");
        var path = Path.Combine(scope.Output, seidel.GetProperty("output").GetString()!);
        var bytes = File.ReadAllBytes(path);

        await CancelAfterFirstOrder(scope);
        using (var cancelled = scope.Manifest())
        {
            Assert.True(cancelled.RootElement.GetProperty("cancelled").GetBoolean());
            Assert.Equal(new AnalysisCatalog(new Optic()).Names.Count, cancelled.RootElement.GetProperty("analyses").GetArrayLength());
            var retained = Find(cancelled, "Seidel Coefficients");
            Assert.Equal("captured", retained.GetProperty("status").GetString());
            Assert.True(retained.GetProperty("reused").GetBoolean());
            Assert.Equal(JsonFiles.Hash(bytes), retained.GetProperty("outputSha256").GetString());
        }
        Assert.Equal(0, await WorkbenchRecapture.RunAsync(scope.Options("First Order")));
        using var resumed = scope.Manifest();
        Assert.Equal("captured", Find(resumed, "Seidel Coefficients").GetProperty("status").GetString());
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationCannotCarryForwardChangedOrTamperedUnvisitedResults(bool changeSettings)
    {
        using var scope = new Scope();
        var name = changeSettings ? "Ray Fan" : "Seidel Coefficients";
        Assert.Equal(0, await WorkbenchRecapture.RunAsync(scope.Options(name)));
        using var previous = scope.Manifest();
        var path = Path.Combine(scope.Output, Find(previous, name).GetProperty("output").GetString()!);
        if (changeSettings)
            JsonFiles.Write(scope.Settings, new { analyses = new[] { new {
                name, settings = new Dictionary<string, string> { ["NumberOfRays"] = "21" }
            } } });
        else File.WriteAllText(path, "{\"name\":\"tampered\"}");
        var bytes = File.ReadAllBytes(path);

        await CancelAfterFirstOrder(scope);
        using var cancelled = scope.Manifest();
        var unvisited = Find(cancelled, name);
        Assert.Equal(changeSettings ? "not-requested" : "failed", unvisited.GetProperty("status").GetString());
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task FreshCancellationKeepsACompleteCatalogWithoutComputingUnvisitedAnalyses()
    {
        using var scope = new Scope();
        await CancelAfterFirstOrder(scope);
        using var cancelled = scope.Manifest();
        var rows = cancelled.RootElement.GetProperty("analyses").EnumerateArray().ToArray();
        Assert.Equal(new AnalysisCatalog(new Optic()).Names.Count, rows.Length);
        Assert.Single(rows, row => row.GetProperty("status").GetString() == "captured");
        Assert.Equal("not-requested", Find(cancelled, "Seidel Coefficients").GetProperty("status").GetString());
        Assert.Single(Directory.GetFiles(Path.Combine(scope.Output, "current"), "*.json"));
    }

    private static async Task CancelAfterFirstOrder(Scope scope)
    {
        using var cancellation = new CancellationTokenSource();
        var previous = Console.Out;
        try
        {
            Console.SetOut(new CancelWriter(previous, cancellation));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => WorkbenchRecapture.RunAsync(
                scope.Options("First Order"), cancellation.Token));
        }
        finally { Console.SetOut(previous); }
    }

    private sealed class CancelWriter(TextWriter parent, CancellationTokenSource cancellation) : TextWriter
    {
        public override System.Text.Encoding Encoding => parent.Encoding;
        public override void WriteLine(string? value)
        {
            parent.WriteLine(value);
            if (value?.Contains(" First Order: ", StringComparison.Ordinal) == true) cancellation.Cancel();
        }
    }

    private static JsonElement Find(JsonDocument manifest, string name) => manifest.RootElement.GetProperty("analyses")
        .EnumerateArray().Single(row => row.GetProperty("name").GetString() == name);

    private sealed class Scope : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "zemax-recapture-" + Guid.NewGuid().ToString("N"));
        public string Source => Path.Combine(Root, "镜头 Δ.ZMX");
        public string Settings => Path.Combine(Root, "settings.json");
        public string Output => Path.Combine(Root, "output");
        public string ManifestPath => Path.Combine(Output, "current-manifest.json");
        public Scope()
        {
            Directory.CreateDirectory(Root);
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "recapture-source.ZMX"), Source);
            JsonFiles.Write(Settings, new { analyses = Array.Empty<object>() });
        }
        public WorkbenchRecaptureOptions Options(string name)
        {
            var index = new AnalysisCatalog(new Optic()).Names.ToList().IndexOf(name) + 1;
            return new(Source, Settings, Output, index, index);
        }
        public JsonDocument Manifest() => JsonDocument.Parse(File.ReadAllText(ManifestPath));
        public JsonDocument View(JsonElement entry) => JsonDocument.Parse(File.ReadAllText(Path.Combine(Output, entry.GetProperty("output").GetString()!)));
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}

[CollectionDefinition("Workbench recapture console", DisableParallelization = true)]
public sealed class WorkbenchRecaptureConsoleCollection { }

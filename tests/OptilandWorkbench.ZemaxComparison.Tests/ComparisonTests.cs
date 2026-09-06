using System.Text.Json;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.ZemaxComparison;
using OptilandWorkbench.ZemaxComparison.Configuration;
using OptilandWorkbench.ZemaxComparison.Metrics;
using OptilandWorkbench.ZemaxComparison.Normalization;
using OptilandWorkbench.ZemaxComparison.Reporting;
using OptilandWorkbench.ZemaxComparison.Zemax;

namespace OptilandWorkbench.ZemaxComparison.Tests;

public sealed class ComparisonTests
{
    private static readonly Axis Length = new("ImageHeight", "Millimeter");
    private static readonly Axis Modulation = new("Modulation", "Dimensionless");
    private static readonly Tolerances Tolerance = new(0.0001, 0.001, 0.01, 0.03);
    private static CanonicalAnalysisRequest Request(string key) => new() { CanonicalAnalysisKey = key, Apodization = "none", WorkbenchSettings = [] };

    [Fact]
    public void CliPreservesUnicodePathsAndRepeatedKeys()
    {
        var o = ComparisonOptions.Parse(["--input", @"C:\镜头 目录\MS-L7.ZMX", "--analysis", "Ray Fan", "--analysis", "MTF", "--configuration", "all", "--keep-raw", "--capture-screenshots"]);
        Assert.Equal(@"C:\镜头 目录\MS-L7.ZMX", o.Input); Assert.Equal(2, o.Analyses.Count); Assert.Equal("all", o.Configuration);
        Assert.True(o.KeepRaw); Assert.True(o.CaptureScreenshots);
    }
    [Theory]
    [InlineData("--unknown", "x")]
    [InlineData("--timeout", "0")]
    [InlineData("--timeout", "-1")]
    [InlineData("--configuration", "0")]
    [InlineData("--fail-on", "pass")]
    [InlineData("--report-language", "es")]
    public void CliRejectsInvalidArguments(string key, string value) => Assert.Throws<ArgumentException>(() => ComparisonOptions.Parse(["--input", "x.zmx", key, value]));
    [Fact] public void InputRequiredExceptListing() { Assert.Throws<ArgumentException>(() => ComparisonOptions.Parse([])); Assert.True(ComparisonOptions.Parse(["--list-analyses"]).ListAnalyses); }
    [Fact] public void AllAndSubsetConflict() => Assert.Throws<ArgumentException>(() => ComparisonOptions.Parse(["--input", "a.zmx", "--all", "--analysis", "MTF"]));
    [Fact]
    public void MissingValueRejected()
    {
        Assert.Throws<ArgumentException>(() => ComparisonOptions.Parse(["--input"]));
        Assert.Throws<ArgumentException>(() => ComparisonOptions.Parse(["--input", "x.zmx", "--timeout", "invalid"]));
        Assert.Throws<ArgumentException>(() => ComparisonOptions.Parse(["--input", "x.zmx", "--timeout", "99999999999999"]));
    }
    [Fact]
    public void RegistryExplicitlyAuditsEntireCanonicalCatalog()
    {
        var expected = new AnalysisCatalog(new Optic()).Names.Order().ToArray();
        Assert.Equal(expected, AnalysisComparisonRegistry.Entries.Select(e => e.CanonicalAnalysisKey).Order().ToArray());
        Assert.All(AnalysisComparisonRegistry.Entries, e => Assert.False(string.IsNullOrWhiteSpace(e.WorkbenchRequestMapper)));
        Assert.All(AnalysisComparisonRegistry.Entries.Where(e => e.Support is SupportStatus.WorkbenchOnly or SupportStatus.UnsupportedByZosApi), e => Assert.NotEmpty(e.Reason));
        Assert.Null(AnalysisComparisonRegistry.Get("Centroid Sphere Wavefront").ZemaxAnalysisType);
        Assert.Null(AnalysisComparisonRegistry.Get("Best Fit Sphere Wavefront").ZemaxAnalysisType);
        Assert.Throws<ArgumentException>(() => AnalysisComparisonRegistry.Get("傅里叶 MTF"));
    }
    [Fact]
    public void ConfigurationRequiresPerAnalysisTolerances()
    {
        var c = JsonFiles.Read<ComparisonConfiguration>(Path.Combine(AppContext.BaseDirectory, "comparison-settings.json")); c.Validate();
        Assert.Throws<ArgumentException>(() => (c with { Analyses = null! }).Validate());
        var mtf = c.Analyses["MTF"];
        c.Analyses["MTF"] = mtf with { Quantities = new() { ["WrongQuantity"] = Tolerance } };
        Assert.Throws<ArgumentException>(c.Validate);
        c.Analyses["MTF"] = mtf with { Quantities = null! }; Assert.Throws<ArgumentException>(c.Validate);
        c.Analyses["MTF"] = mtf;
        c.Analyses.Remove("MTF"); Assert.Throws<ArgumentException>(c.Validate);
    }
    [Theory]
    [InlineData("Micrometer", "Millimeter", 0.001)]
    [InlineData("Degree", "Radian", 0.017453292519943295)]
    [InlineData("Milliradian", "Radian", 0.001)]
    [InlineData("Wave", "Wave", 1)]
    [InlineData("Diopter", "Diopter", 1)]
    [InlineData("InverseMicrometer", "CyclesPerMillimeter", 1000)]
    public void ConvertsTypedUnits(string from, string to, double expected) => Assert.Equal(expected, PhysicalNormalization.UnitScale(from, to), 12);
    [Theory]
    [InlineData("Millimeter", "Degree")]
    [InlineData("Wave", "Micrometer")]
    [InlineData("CyclesPerMillimeter", "CyclesPerMilliradian")]
    [InlineData("Unspecified", "Unspecified")]
    public void IncompatibleUnitsFailClosed(string from, string to) => Assert.Throws<InvalidDataException>(() => PhysicalNormalization.UnitScale(from, to));
    [Fact]
    public void CurvesAlignOnlyPhysicalOverlap()
    {
        var w = new Series1DResult("T", "ignored title", [0, 1000, 2000], [0, 1, 2], new("ImageHeight", "Micrometer"), Modulation);
        var z = new Series1DResult("T", "different title", [0.5, 1.5], [0.5, 1.5], Length, Modulation);
        var log = new List<string>(); var match = ComparisonMetrics.Align(w, z, log);
        Assert.Equal([0.5, 1, 1.5], match.X); Assert.Equal(match.Zemax, match.Workbench); Assert.Equal(0.5, match.Coverage);
        Assert.True(log.Count >= 2); Assert.Throws<InvalidDataException>(() => ComparisonMetrics.Align(w with { XAxis = new("FieldHeight", "Micrometer") }, z, []));
    }
    [Fact]
    public void InterpolationRejectsExtrapolationDuplicatesAndNonfinite()
    {
        Assert.Equal(2, PhysicalNormalization.Interpolate([0, 1], [0, 4], 0.5));
        Assert.Throws<InvalidDataException>(() => PhysicalNormalization.Interpolate([0, 1], [0, 4], 2));
        Assert.Throws<InvalidDataException>(() => PhysicalNormalization.Sort([0, 0], [1, 2]));
        Assert.Throws<InvalidDataException>(() => PhysicalNormalization.Sort([0, 1], [1, double.NaN]));
    }
    [Fact]
    public void OrientationMovesCoordinatesAndMaskWithValues()
    {
        var g = new Grid2DResult("grid", [0, 1], [4, 5, 6], [[1, 2], [3, null], [5, 6]], Length, Length, Modulation);
        var flipped = PhysicalNormalization.Orient(g, true, true, false, "documented detector coordinate basis", []);
        Assert.Equal([6, 5, 4], flipped.X); Assert.Equal([0, 1], flipped.Y);
        Assert.Equal([5d, 3d, 1d], flipped.Z[0]); Assert.Null(flipped.Z[1][1]);
        Assert.Throws<ArgumentException>(() => PhysicalNormalization.Orient(g, false, true, false, "", []));
    }
    [Fact]
    public void GridMismatchDoesNotSearchForBetterAlignment()
    {
        var g = new Grid2DResult("grid", [0, 1], [0, 1], [[1, 2], [3, null]], Length, Length, Modulation);
        var same = ComparisonMetrics.Align(g, g, []); Assert.Equal(3, same.Workbench.Length); Assert.Equal(1, same.Coverage);
        Assert.Throws<InvalidDataException>(() => ComparisonMetrics.Align(g with { X = [0.1, 1.1] }, g, []));
    }
    [Fact]
    public void MetricsReportWorstPointPercentilesAndUndefinedCorrelation()
    {
        var metric = ComparisonMetrics.Calculate("scalar", "Millimeter", new([3], null, [2], [1], 1), Tolerance);
        Assert.Equal(1, metric.MaxAbsolute); Assert.Equal(1, metric.Nrmse); Assert.Equal(3, metric.WorstX); Assert.Null(metric.Pearson);
        Assert.Equal(Conclusion.Difference, metric.Conclusion);
        var identical = ComparisonMetrics.Calculate("T", "Dimensionless", new([0, 1, 2], null, [1, 2, 3], [1, 2, 3], 1), Tolerance);
        Assert.Equal(Conclusion.Pass, identical.Conclusion); Assert.Equal(1, identical.Pearson); Assert.Equal(0, identical.P95);
    }
    [Fact]
    public void InsufficientCoverageCannotPass() => Assert.Equal(Conclusion.Incomparable,
        ComparisonMetrics.Calculate("T", "Dimensionless", new([0, 1], null, [1, 2], [1, 2], 0.5), Tolerance).Conclusion);
    [Fact]
    public void ZeroReferenceAndEmptyMetricsAreNotFalsePasses()
    {
        Assert.Equal(Conclusion.Difference, ComparisonMetrics.Calculate("zero", "Dimensionless", new([0], null, [1], [0], 1), Tolerance).Conclusion);
        Assert.Throws<InvalidDataException>(() => ComparisonMetrics.Calculate("empty", "Dimensionless", new([], null, [], [], 1), Tolerance));
    }
    [Fact]
    public void PsfAndWavefrontMetricsPreserveDefinitions()
    {
        var v = new MatchedValues([-1, 0, 1, -1, 0, 1, -1, 0, 1], [-1, -1, -1, 0, 0, 0, 1, 1, 1], [0, 1, 0, 1, 4, 1, 0, 1, 0], [0, 1, 0, 1, 4, 1, 0, 1, 0], 1);
        var d = ComparisonMetrics.GridStatistics(v, "psf", false);
        Assert.Equal(8, d["psf.workbench.sampleSum"]); Assert.Equal(0, d["psf.workbench.centroidX"]);
        Assert.NotNull(d["psf.workbench.marginalFwhmX"]);
        Assert.DoesNotContain(ComparisonMetrics.GridStatistics(v, "wave", true).Keys, k => k.Contains("Energy", StringComparison.Ordinal));
    }
    [Fact]
    public void MtfThresholdsAreInterpolatedOnPhysicalFrequencies()
    {
        var metrics = ComparisonMetrics.MtfStatistics(new([0, 10, 20], null, [1, 0.5, 0], [1, 0.5, 0], 1));
        Assert.Equal(10, metrics["workbench.firstCrossing50"]); Assert.Equal(18, metrics["workbench.firstCrossing10"]);
        Assert.Null(metrics["workbench.frequency50"]);
    }
    [Fact]
    public void FrozenZemaxRawGridCanBeNormalizedOffline()
    {
        var entry = AnalysisComparisonRegistry.Get("Huygens PSF");
        var result = ResultNormalizer.Zemax(Path.Combine(AppContext.BaseDirectory, "Fixtures", "huygens-psf.json"), entry, Request(entry.CanonicalAnalysisKey));
        var grid = Assert.Single(result.Grids); Assert.Equal(32, grid.X.Length); Assert.Equal(32, grid.Y.Length);
        Assert.Equal("Micrometer", grid.XAxis.Unit); Assert.Equal("Irradiance", grid.ValueAxis.Quantity);
        var values = ComparisonMetrics.Align(grid, grid, []);
        Assert.Equal(Conclusion.Pass, ComparisonMetrics.Calculate("historical fixture identity", "Dimensionless", values, Tolerance).Conclusion);
    }
    [Fact]
    public void ManifestHashAndRequestFingerprintAreContentAddressed()
    {
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", JsonFiles.Hash("abc"u8.ToArray()));
        var r = Request("MTF"); Assert.Equal(r.Fingerprint, r.Fingerprint); Assert.NotEqual(r.Fingerprint, (r with { Wavelength = 2 }).Fingerprint);
        Assert.Equal((r with { WorkbenchSettings = new() { ["a"] = "1", ["b"] = "2" } }).Fingerprint,
            (r with { WorkbenchSettings = new() { ["b"] = "2", ["a"] = "1" } }).Fingerprint);
    }
    [Fact]
    public void OutputProtectionRejectsToleranceChangesEvenWithOverwrite()
    {
        using var temp = new TemporaryDirectory(); var source = new string('a', 64); var config = new string('b', 64);
        ComparisonRunner.PrepareOutput(temp.Path, "a.zmx", source, config, "2026 R1", false);
        JsonFiles.Write(System.IO.Path.Combine(temp.Path, "manifest.json"), new { SourceSha256 = source, ConfigurationSha256 = config });
        Assert.Throws<IOException>(() => ComparisonRunner.PrepareOutput(temp.Path, "a.zmx", source, config, "2026 R1", false));
        Assert.Throws<IOException>(() => ComparisonRunner.PrepareOutput(temp.Path, "a.zmx", source, new string('c', 64), "2026 R1", true));
        Assert.Equal(temp.Path, ComparisonRunner.PrepareOutput(temp.Path, "a.zmx", source, config, "2026 R1", true));
        var oldPlot = System.IO.Path.Combine(temp.Path, "comparisons", "mtf", "overlay.png");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(oldPlot)!); File.WriteAllText(oldPlot, "previous evidence");
        var userNote = System.IO.Path.Combine(temp.Path, "notes.txt"); File.WriteAllText(userNote, "user note");
        ComparisonRunner.ArchivePreviousRun(temp.Path);
        Assert.False(File.Exists(oldPlot));
        var archive = Assert.Single(Directory.GetDirectories(temp.Path, "previous-run-*"));
        Assert.Equal("previous evidence", File.ReadAllText(System.IO.Path.Combine(archive, "comparisons", "mtf", "overlay.png")));
        Assert.True(File.Exists(System.IO.Path.Combine(archive, "manifest.json")));
        Assert.Equal("user note", File.ReadAllText(userNote));
    }
    [Theory]
    [InlineData("difference", Conclusion.Difference, 1)]
    [InlineData("difference", Conclusion.Close, 1)]
    [InlineData("difference", Conclusion.Pass, 0)]
    [InlineData("error", Conclusion.Difference, 0)]
    [InlineData("error", Conclusion.Error, 2)]
    [InlineData("none", Conclusion.Error, 0)]
    public void FailurePolicyIsExplicit(string policy, Conclusion c, int expected)
    {
        var runs = new[] { new AnalysisRun { Key = "MTF", Directory = "mtf", Conclusion = c } };
        Assert.Equal(expected, ComparisonRunner.ExitCode(runs, policy, false, false, false, false));
        Assert.Equal(4, ComparisonRunner.ExitCode(runs, policy, true, false, false, false));
        Assert.Equal(4, ComparisonRunner.ExitCode(runs, policy, false, true, false, false));
        Assert.Equal(2, ComparisonRunner.ExitCode(runs, policy, false, false, true, false));
        Assert.Equal(3, ComparisonRunner.ExitCode(runs, policy, false, false, false, true));
    }
    [Fact]
    public void ReportRetainsCompletedResultsWhenAnotherAnalysisFails()
    {
        using var temp = new TemporaryDirectory();
        var runs = new[] { new AnalysisRun { Key = "MTF", Directory = "mtf", Conclusion = Conclusion.Pass, ZemaxStatus = CaptureStatus.Captured, WorkbenchStatus = CaptureStatus.Captured },
            new AnalysisRun { Key = "Wavefront", Directory = "wavefront", Conclusion = Conclusion.Error, Reason = "Synthetic executor failure" } };
        ReportWriter.Write(temp.Path, new { InputSha256 = "abc" }, runs, "zh-CN", 2, null);
        Assert.Contains("Synthetic executor failure", File.ReadAllText(System.IO.Path.Combine(temp.Path, "COMPARISON_REPORT.md")));
        using var json = JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(temp.Path, "run-summary.json")));
        Assert.Equal(1, json.RootElement.GetProperty("counts").GetProperty("Pass").GetInt32());
        Assert.Equal(1, json.RootElement.GetProperty("counts").GetProperty("Error").GetInt32());
    }
    [Fact]
    public async Task WorkerCancellationPreservesUnrelatedCompletedArtifact()
    {
        using var temp = new TemporaryDirectory(); var finished = System.IO.Path.Combine(temp.Path, "completed.json"); File.WriteAllText(finished, "{\"completed\":true}");
        using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        var result = OperatingSystem.IsWindows()
            ? await ProcessIsolation.Run("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 30"], temp.Path, 20, cancelled.Token)
            : await ProcessIsolation.Run("sleep", ["30"], temp.Path, 20, cancelled.Token);
        Assert.True(result.Cancelled); Assert.Equal("{\"completed\":true}", File.ReadAllText(finished));
    }
    [Fact]
    public void ProductHasNoReverseDependencyOnValidationTool()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(System.IO.Path.Combine(root.FullName, "OptilandWorkbench.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        foreach (var file in Directory.EnumerateFiles(System.IO.Path.Combine(root.FullName, "src"), "*", SearchOption.AllDirectories)
            .Where(p => !p.Split(System.IO.Path.DirectorySeparatorChar).Any(s => s is "obj" or "bin") && System.IO.Path.GetExtension(p) is ".cs" or ".csproj"))
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("ZemaxComparison", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ZOSAPI", text, StringComparison.OrdinalIgnoreCase);
        }
    }
    [Fact]
    public void RawCanonicalExecutionUsesTheExistingUiFactory()
    {
        var optic = Optic.CreateCookeTriplet(); var runtime = new WorkbenchRuntime(optic);
        var settings = runtime.MergeAnalysisSettings("MTF", new Dictionary<string, string> { ["Sampling"] = "32", ["FieldNumber"] = "1", ["WavelengthNumber"] = "1", ["MaximumFrequency"] = "30" });
        var raw = runtime.BuildAnalysisData("MTF", settings); var view = runtime.BuildAnalysisView("MTF", settings);
        Assert.Equal(JsonSerializer.Serialize(raw.PlotSeries, JsonFiles.Options), JsonSerializer.Serialize(view.SeriesList, JsonFiles.Options));
        Assert.Equal(raw.Outcome, view.Outcome);
        using var temp = new TemporaryDirectory();
        var path = System.IO.Path.Combine(temp.Path, "data.json"); JsonFiles.Write(path, raw);
        var captured = ResultNormalizer.CaptureWorkbench(path);
        Assert.Equal(raw.PlotSeries.Count, captured.Series.Count);
        Assert.Equal(raw.PlotSeries[0].Points.Select(p => p.X), captured.Series[0].X);
        Assert.Equal(raw.PlotSeries[0].Points.Select(p => p.Y), captured.Series[0].Y);
        Assert.Equal(raw.PlotSeries[0].XUnit.ToString(), captured.Series[0].XAxis.Unit);
        Assert.Single(captured.Reports);
    }
    [Fact]
    public async Task WorkerTimeoutIsDistinctFromCancellation()
    {
        using var temp = new TemporaryDirectory();
        var result = OperatingSystem.IsWindows()
            ? await ProcessIsolation.Run("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 30"], temp.Path, 1, CancellationToken.None)
            : await ProcessIsolation.Run("sleep", ["30"], temp.Path, 1, CancellationToken.None);
        Assert.True(result.TimedOut); Assert.False(result.Cancelled); Assert.Equal(4, result.ExitCode);
    }
    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "zemax-comparison-tests-" + Guid.NewGuid().ToString("N"));
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, true);
    }
}

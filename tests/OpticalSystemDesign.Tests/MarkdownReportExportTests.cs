using System.Text.RegularExpressions;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

public sealed class MarkdownReportExportTests
{
    [Fact]
    public async Task SnapshotFreezesUnsavedPrescriptionAndAllConfigurationsWithoutSavingSource()
    {
        using var directory = new ReportTestDirectory();
        using var application = WorkbenchApplication.Create("cooke");
        var path = Path.Combine(directory.Path, "source.staropt");
        await application.Documents.SaveAsync(path);
        var number = application.MultiConfiguration.Add();
        application.MultiConfiguration.Activate(number);
        var surface = application.Prescription.GetSurfaces()[1];
        application.Prescription.UpdateSurface(surface with { Thickness = surface.Thickness + 0.5 });
        var before = application.Documents.GetRecoveryState();
        var sourceBytes = await File.ReadAllBytesAsync(path);
        using var snapshot = application.CaptureReportSnapshot();
        application.Prescription.UpdateSurface(surface with { Thickness = surface.Thickness + 1.5 });
        Assert.Equal(surface.Thickness + 0.5, snapshot.Workspace.Prescription.GetSurfaces()[1].Thickness);
        Assert.Equal(2, snapshot.Workspace.MultiConfiguration.GetNavigationState().Count);
        Assert.Equal(number, snapshot.Workspace.MultiConfiguration.GetNavigationState().ActiveIndex);
        Assert.Equal(path, snapshot.SourceDocument.Path);
        Assert.True(snapshot.SourceDocument.IsDirty);
        Assert.Equal(before.Revision, snapshot.SourceDocument.Revision);
        Assert.Equal(sourceBytes, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task ExportUsesSameDirectoryTimestampEscapedLinksAndActualAnalysisSettings()
    {
        using var directory = new ReportTestDirectory();
        using var application = WorkbenchApplication.Create("cooke");
        var path = Path.Combine(directory.Path, "设计 #1[镜头].staropt");
        await application.Documents.SaveAsync(path);
        var initial = application.Documents.GetRecoveryState();
        using var snapshot = application.CaptureReportSnapshot();
        var progress = new List<ReportExportProgress>();
        var options = new MarkdownReportSettings
        {
            AnalysisKeys = ["Spot Diagram"],
            Include2DLayout = true,
            Include3DLayout = true,
            AnalysisParameters = new() { ["Spot Diagram"] = new() { ["RayDensity"] = "3" } }
        };
        var result = await MarkdownReportExporter.ExportAsync(snapshot, options,
            MarkdownReportExporter.SuggestedDirectory(path), WritePlaceholder,
            new InlineProgress<ReportExportProgress>(progress.Add));
        Assert.Equal(directory.Path, Path.GetDirectoryName(result.ReportPath));
        Assert.Matches(@"_report_\d{8}_\d{6}_\d{3}\.md$", result.ReportPath);
        Assert.Equal(3, result.Succeeded); Assert.Equal(0, result.Failed);
        var markdown = await File.ReadAllTextAsync(result.ReportPath);
        Assert.Contains("镜头结构", markdown); Assert.Contains("RayDensity", markdown);
        Assert.Contains("计算来源", markdown); Assert.Contains("```json", markdown);
        var links = Regex.Matches(markdown, @"!\[[^\]]*\]\(<([^>]+)>\)");
        Assert.Equal(3, links.Count);
        foreach (Match link in links)
            Assert.True(File.Exists(Path.Combine(directory.Path, Uri.UnescapeDataString(link.Groups[1].Value))));
        Assert.Equal(progress[^1].Total, progress[^1].Completed);
        Assert.True(progress.Zip(progress.Skip(1)).All(pair => pair.First.Completed <= pair.Second.Completed));
        Assert.Equal(initial, application.Documents.GetRecoveryState());
        using var repeat = application.CaptureReportSnapshot();
        var second = await MarkdownReportExporter.ExportAsync(repeat, options, directory.Path, WritePlaceholder);
        Assert.NotEqual(result.ReportPath, second.ReportPath);
        Assert.True(File.Exists(result.ReportPath));
    }

    [Theory]
    [InlineData(DesignReportFormat.Markdown)]
    [InlineData(DesignReportFormat.Word)]
    [InlineData(DesignReportFormat.Pdf)]
    [InlineData(DesignReportFormat.Html)]
    public async Task CancellationRetiresOnlyOwnedIncompleteOutputAndPreservesExistingFiles(DesignReportFormat format)
    {
        using var directory = new ReportTestDirectory();
        var existing = Path.Combine(directory.Path, "existing.md");
        await File.WriteAllTextAsync(existing, "keep");
        using var application = WorkbenchApplication.Create("cooke");
        using var snapshot = application.CaptureReportSnapshot();
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => MarkdownReportExporter.ExportAsync(
            snapshot, new() { Format = format, AnalysisKeys = [] }, directory.Path,
            async (request, output, stem, token) =>
            {
                var result = await WritePlaceholder(request, output, stem, token);
                cancellation.Cancel(); return result;
            }, cancellationToken: cancellation.Token));
        Assert.Equal("keep", await File.ReadAllTextAsync(existing));
        Assert.Equal([existing], Directory.GetFileSystemEntries(directory.Path));
    }

    [Fact]
    public async Task SavedGenericZernikeSelectionsAreOmittedWithoutChangingExplicitCoefficientAnalyses()
    {
        using var directory = new ReportTestDirectory();
        using var application = WorkbenchApplication.Create();
        await application.Documents.OpenAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "zemax-123456.ZMX"));
        using var snapshot = application.CaptureReportSnapshot();
        var keys = new[] { "Zernike Fringe", "Zernike Standard", "Zernike Annular" };
        var options = new MarkdownReportSettings
        {
            AnalysisKeys = ["Zernike", "Zernike 系数", .. keys],
            Include2DLayout = false,
            Include3DLayout = false,
            AnalysisParameters = keys.ToDictionary(key => key, _ => new Dictionary<string, string>
            { ["PupilSampling"] = "32 x 32", ["ZernikeTerms"] = "12", ["FieldNumber"] = "1 - 轴上视场" })
        };
        var seen = new List<AnalysisPresentationKind>();
        var result = await MarkdownReportExporter.ExportAsync(snapshot, options, directory.Path,
            (request, output, stem, token) =>
            {
                seen.Add(request.Analysis!.View.PresentationKind);
                return WritePlaceholder(request, output, stem, token);
            });
        var markdown = await File.ReadAllTextAsync(result.ReportPath);
        Assert.True(result.Succeeded == 3, string.Join(Environment.NewLine,
            markdown.Split('\n').Where(line => line.Contains("未完成", StringComparison.Ordinal))));
        Assert.Equal(0, result.Failed);
        Assert.Equal(new[] { AnalysisPresentationKind.ZernikeFringe, AnalysisPresentationKind.ZernikeStandard,
            AnalysisPresentationKind.ZernikeAnnular }, seen);
        Assert.DoesNotContain("Zernike Fringe Fit", markdown);
        Assert.DoesNotContain("### Zernike 系数", markdown);
        Assert.DoesNotContain("未完成", markdown);
        foreach (var key in keys) Assert.Contains(WorkbenchAnalysisCatalog.DisplayName(key), markdown);
        Assert.Equal(new[] { "Zernike", "Zernike 系数" }.Concat(keys), options.AnalysisKeys);
    }

    [Fact]
    public async Task FailureIsReportedAndOtherAnalysesContinue()
    {
        using var directory = new ReportTestDirectory();
        using var application = WorkbenchApplication.Create("cooke");
        using var snapshot = application.CaptureReportSnapshot();
        var result = await MarkdownReportExporter.ExportAsync(snapshot,
            new() { AnalysisKeys = ["Unknown Analysis", "Spot Diagram"], Include2DLayout = false, Include3DLayout = false },
            directory.Path, WritePlaceholder);
        Assert.Equal(1, result.Succeeded); Assert.Equal(1, result.Failed);
        var markdown = await File.ReadAllTextAsync(result.ReportPath);
        Assert.Contains("未完成", markdown); Assert.Contains("点列图", markdown);
        Assert.Empty(Directory.GetDirectories(directory.Path, ".report-export-*"));
    }

    [Fact]
    public async Task EachConfigurationHasItsOwnPrescriptionAndLayout()
    {
        using var directory = new ReportTestDirectory();
        using var application = WorkbenchApplication.Create("cooke");
        application.MultiConfiguration.Add();
        application.MultiConfiguration.SetThickness(1, 1, 4.321);
        var before = application.MultiConfiguration.GetNavigationState();
        using var snapshot = application.CaptureReportSnapshot();
        var result = await MarkdownReportExporter.ExportAsync(snapshot,
            new() { AnalysisKeys = [], Include3DLayout = false, AllConfigurations = true }, directory.Path, WritePlaceholder);
        var markdown = await File.ReadAllTextAsync(result.ReportPath);
        Assert.Contains("## 配置 1", markdown); Assert.Contains("## 配置 2", markdown);
        Assert.Contains("4.321", markdown); Assert.Equal(2, result.Succeeded);
        Assert.Equal(before, application.MultiConfiguration.GetNavigationState());
    }

    [Fact]
    public void DefaultsSelectTheFourCommonAnalysesAndSafeTimeStampedFileNames()
    {
        Assert.Equal(["Spot Diagram", "Ray Fan", "MTF", "Field Curvature and Distortion"], new MarkdownReportSettings().AnalysisKeys);
        var stem = MarkdownReportExporter.ReportStem("a/b:*?<>|", new DateTimeOffset(2026, 10, 24, 20, 30, 40, TimeSpan.FromHours(8)));
        Assert.Equal("a_b_______report_20261024_203040_000", stem);
    }

    private static async Task<IReadOnlyList<string>> WritePlaceholder(ReportImageRequest request, string directory, string stem, CancellationToken token)
    {
        // Rendering is tested separately with the real Skia backend.
        var file = stem + ".png";
        await File.WriteAllBytesAsync(Path.Combine(directory, file), [137, 80, 78, 71], token);
        return [file];
    }

    private sealed class InlineProgress<T>(Action<T> action) : IProgress<T>
    { public void Report(T value) => action(value); }
}

internal sealed class ReportTestDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "OpticalSystemDesign-ReportTests-" + Guid.NewGuid().ToString("N"));
    public ReportTestDirectory() => Directory.CreateDirectory(Path);
    public void Dispose()
    {
        var absolute = System.IO.Path.GetFullPath(Path);
        if (absolute.StartsWith(System.IO.Path.GetFullPath(System.IO.Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
            && System.IO.Path.GetFileName(absolute).StartsWith("OpticalSystemDesign-ReportTests-", StringComparison.Ordinal)
            && Directory.Exists(absolute)) Directory.Delete(absolute, true);
    }
}

using System.Diagnostics;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

[Collection(nameof(WorkspaceSchedulingCollection))]
public sealed class RuntimeFileLogTests
{
    [Fact]
    public async Task ActualDocumentAndAnalysisOperationsReachTheLog()
    {
        using var directory = new ReportTestDirectory();
        var path = Path.Combine(directory.Path, "运行记录.staropt");
        using (var log = RuntimeFileLog.Start(directory.Path))
        {
            Assert.NotNull(log);
            using var app = WorkbenchApplication.Create("cooke");
            await app.Documents.SaveAsync(path);
            await app.Documents.OpenAsync(path);
            var valid = new AnalysisRequestDto(Guid.NewGuid(), 1, "PSF", app.Analyses.MergeSettings("PSF", null));
            var result = await app.Analyses.RunAsync(valid);
            Assert.Equal(AnalysisOutcome.Success, result.View.Outcome);
            var invalid = valid with
            {
                AnalysisKey = "MTF",
                Generation = 2,
                Settings = new Dictionary<string, string> { ["Sampling"] = "invalid" }
            };
            await Assert.ThrowsAsync<ArgumentException>(() => app.Analyses.RunAsync(invalid));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                app.Analyses.RunAsync(valid with { Generation = 3 }, new CancellationToken(true)));
        }
        var contents = File.ReadAllText(Path.Combine(directory.Path, "runtime.log"));
        foreach (var expected in new[] { "Session started", "Document saved: 运行记录.staropt",
            "Document opened: 运行记录.staropt", "Analysis PSF", "Success in", "Device=CPU",
            "Analysis MTF", "run=2", "failed.", "cancelled.", "Session ended" })
            Assert.Contains(expected, contents);
    }

    [Fact]
    public void ConcurrentEntriesAreCompleteAndRotationPreservesPreviousRecords()
    {
        using var directory = new ReportTestDirectory();
        var path = Path.Combine(directory.Path, "runtime.log");
        using (var log = RuntimeFileLog.Start(directory.Path, 1024))
        {
            Assert.NotNull(log);
            Parallel.For(0, 40, i => log.WriteLine($"entry {i}: " + new string('x', 100)));
        }
        var files = Directory.GetFiles(directory.Path);
        Assert.Equal(2, files.Length);
        Assert.Contains(path + ".previous", files);
        foreach (var file in files)
        {
            Assert.InRange(new FileInfo(file).Length, 1, 1024);
            foreach (var line in File.ReadLines(file).Where(line => line.Contains("[TRACE]")))
                Assert.EndsWith(new string('x', 100), line);
        }
        Assert.Contains("Session ended", File.ReadAllText(path));
    }

    [Fact]
    public void UnavailableDirectoryAndLockedFileDoNotBreakCommands()
    {
        using var directory = new ReportTestDirectory();
        var path = Path.Combine(directory.Path, "runtime.log");
        File.WriteAllText(path, "existing");
        Assert.Null(RuntimeFileLog.Start(path));
        using var log = RuntimeFileLog.Start(directory.Path);
        Assert.NotNull(log);
        using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
            Trace.TraceWarning("write while locked");
        Trace.TraceWarning("write after unlock");
        Assert.Contains("write after unlock", File.ReadAllText(path));
    }
}

using System.Diagnostics;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

public sealed class RecoveryPathBoundaryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "recovery-boundary-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _links = [];

    public RecoveryPathBoundaryTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task AnAliasedParentUsesOnePhysicalRecoveryRootAndRetainsRecoveryProtection()
    {
        var parent = Directory.CreateDirectory(Path.Combine(_directory, "physical-parent")).FullName;
        var alias = CreateDirectoryAlias("parent-alias", parent);
        using var app = WorkbenchApplication.Create();
        app.Prescription.UpdateSurface(app.Prescription.GetSurfaces()[0] with { Label = "Unsaved checkpoint" });
        await using (var store = new DocumentRecoveryStore(app.Documents, Path.Combine(alias, "Recovery")))
        {
            Assert.True(await store.CheckpointAsync());
            await store.StopAsync(cleanExit: false);
        }
        await using var observer = new DocumentRecoveryStore(app.Documents, Path.Combine(parent, "Recovery"));
        var candidate = Assert.Single(observer.ListCandidates());
        Assert.True(Directory.Exists(candidate.SessionDirectory));
        Assert.DoesNotContain("parent-alias", candidate.SessionDirectory, StringComparison.Ordinal);
        Assert.Equal(app.Documents.GetSnapshot().Name, candidate.Entry.Name);
        await observer.RestoreAsync(candidate);
        Assert.Equal("Unsaved checkpoint", app.Prescription.GetSurfaces()[0].Label);
    }

    [Fact]
    public void TheManagedRootItselfCannotBeALink()
    {
        var outside = Directory.CreateDirectory(Path.Combine(_directory, "outside")).FullName;
        var link = CreateDirectoryAlias("root-alias", outside);
        using var app = WorkbenchApplication.Create();
        Assert.Throws<InvalidDataException>(() => new DocumentRecoveryStore(app.Documents, link));
        Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
    }

    [Fact]
    public async Task LinkedSessionCannotBeListedOrUsedToDeleteOutsideFiles()
    {
        var outside = Directory.CreateDirectory(Path.Combine(_directory, "outside")).FullName;
        var root = Directory.CreateDirectory(Path.Combine(_directory, "Recovery")).FullName;
        var id = Guid.NewGuid().ToString("N");
        var link = CreateDirectoryAlias(Path.Combine("Recovery", id), outside);
        var keep = Path.Combine(outside, "keep.txt");
        await File.WriteAllTextAsync(keep, "must remain");
        using var app = WorkbenchApplication.Create();
        await using var store = new DocumentRecoveryStore(app.Documents, root);
        Assert.Empty(store.ListCandidates());
        var entry = new DocumentRecoveryEntry(1, Guid.NewGuid().ToString("N"), "forged", null, 1, 1,
            DateTimeOffset.UtcNow, new string('0', 64));
        Assert.Throws<InvalidDataException>(() => store.DeleteCandidate(new(link, entry)));
        Assert.Equal("must remain", await File.ReadAllTextAsync(keep));
    }

    private string CreateDirectoryAlias(string name, string target)
    {
        var link = Path.Combine(_directory, name);
        if (OperatingSystem.IsWindows())
        {
            // Junction creation does not require Developer Mode or administrator rights.
            var start = new ProcessStartInfo("cmd.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var argument in new[] { "/c", "mklink", "/J", link, target }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, output + error);
        }
        else Directory.CreateSymbolicLink(link, target);
        _links.Add(link);
        return link;
    }

    public void Dispose()
    {
        // Unlink aliases first; recursive cleanup never traverses an external target.
        foreach (var link in _links.AsEnumerable().Reverse()) Directory.Delete(link);
        Directory.Delete(_directory, recursive: true);
    }
}

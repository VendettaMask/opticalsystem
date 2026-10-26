using System.Diagnostics;
using System.Security.Cryptography;
using OpticalSystemDesign.App.Services;

namespace OpticalSystemDesign.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class WindowsInstallationCleanupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "installation-cleanup-" + Guid.NewGuid().ToString("N"));
    private readonly string _id = "OpticalInstallationTest-" + Guid.NewGuid().ToString("N");

    public WindowsInstallationCleanupTests()
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(Path.Combine(_root, ".installed-releases"));
        File.WriteAllText(Path.Combine(_root, "release-state.ini"),
            $"[Release]\nProductAppId={_id}\nActive=current\nPrevious=previous\nVersion=1.0.3\n");
    }

    [WindowsInstallationFact]
    public void RemovesOnlyOwnedOldPayloadAndKeepsCurrentPreviousExecutingAndUnknown()
    {
        foreach (var release in new[] { "current", "previous", "executing", "old", "unknown" }) Add(release);
        File.Delete(Receipt("unknown"));
        var result = Run("executing");
        Assert.Equal(1, result.Removed);
        Assert.Empty(result.Retained);
        Assert.False(Directory.Exists(Payload("old")));
        Assert.False(File.Exists(Receipt("old")));
        foreach (var release in new[] { "current", "previous", "executing", "unknown" })
            Assert.True(File.Exists(Path.Combine(Payload(release), "OpticalSystemDesign.App.exe")));
    }

    [WindowsInstallationTheory]
    [InlineData("modified")]
    [InlineData("user-file")]
    [InlineData("user-directory")]
    [InlineData("receipt")]
    [InlineData("manifest")]
    [InlineData("foreign-product")]
    [InlineData("traversal")]
    [InlineData("duplicate")]
    public void UnsafeCandidatesAreRetainedWithoutDeletingAnyPayloadFile(string reason)
    {
        Add("old");
        var exe = Path.Combine(Payload("old"), "OpticalSystemDesign.App.exe");
        switch (reason)
        {
            case "modified": File.AppendAllText(exe, "modified"); break;
            case "user-file": File.WriteAllText(Path.Combine(Payload("old"), "my-project.staropt"), "user"); break;
            case "user-directory": Directory.CreateDirectory(Path.Combine(Payload("old"), "My projects")); break;
            case "receipt": File.AppendAllText(Receipt("old"), "invalid"); break;
            case "manifest": File.AppendAllText(Path.ChangeExtension(Receipt("old"), ".sha256"), "corrupt"); break;
            case "foreign-product": File.WriteAllText(Receipt("old"), File.ReadAllText(Receipt("old")).Replace(_id, "other")); break;
            case "traversal": ReplaceManifest("old", new string('a', 64) + "  ../outside.txt\n"); break;
            case "duplicate":
                ReplaceManifest("old", File.ReadAllText(Path.ChangeExtension(Receipt("old"), ".sha256")) +
                File.ReadAllText(Path.ChangeExtension(Receipt("old"), ".sha256"))); break;
        }
        var before = Directory.GetFiles(Payload("old"), "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllBytes);
        var result = Run();
        Assert.Equal(0, result.Removed);
        Assert.Single(result.Retained);
        foreach (var (path, bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.True(File.Exists(Receipt("old")));
    }

    [WindowsInstallationFact]
    public void InterruptedCleanupResumesFromExternalReceiptWithoutManifestInPayload()
    {
        Add("old");
        var interrupted = Run(afterDelete: path =>
        {
            if (Path.GetFileName(path) == "release.files.sha256") throw new IOException("Injected storage failure");
        });
        Assert.Equal(0, interrupted.Removed);
        Assert.True(File.Exists(Receipt("old")));
        Assert.False(File.Exists(Path.Combine(Payload("old"), "release.files.sha256")));
        Assert.Equal(1, Run().Removed);
        Assert.False(Directory.Exists(Payload("old")));
    }

    [WindowsInstallationFact]
    public void PartiallyDeletedPayloadRetainsReceiptAndRetriesAfterLockedFileReleased()
    {
        Add("old");
        var nested = Path.Combine(Payload("old"), "data", "sample.txt");
        using (var locked = File.Open(nested, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var result = Run();
            Assert.Equal(0, result.Removed);
            Assert.Single(result.Retained);
            Assert.Equal("example", File.ReadAllText(nested));
            Assert.True(File.Exists(Receipt("old")));
        }
        Assert.Equal(1, Run().Removed);
    }

    [WindowsInstallationFact]
    public void ChangeBetweenPreflightAndDeletionIsNotLost()
    {
        Add("old");
        var sample = Path.Combine(Payload("old"), "data", "sample.txt");
        var result = Run(afterDelete: _ => File.WriteAllText(sample, "user change"));
        Assert.Equal(0, result.Removed);
        Assert.Equal("user change", File.ReadAllText(sample));
        Assert.True(File.Exists(Receipt("old")));
    }

    [WindowsInstallationTheory]
    [InlineData("pending")]
    [InlineData("state")]
    [InlineData("missing-previous")]
    public void UncertainRecoveryStateBlocksAllCleanup(string reason)
    {
        Add("old");
        if (reason == "pending")
        {
            Directory.CreateDirectory(Path.Combine(_root, ".install-recovery"));
            File.WriteAllText(Path.Combine(_root, ".install-recovery", "pending.ini"), "interrupted");
        }
        else
        {
            var state = Path.Combine(_root, "release-state.ini");
            File.WriteAllText(state, File.ReadAllText(state).Replace(reason == "state" ? _id : "Previous=previous", "invalid"));
        }
        var exception = Record.Exception(() => Run());
        Assert.True(exception is IOException or InvalidDataException);
        Assert.True(File.Exists(Path.Combine(Payload("old"), "OpticalSystemDesign.App.exe")));
    }

    [WindowsInstallationFact]
    public void RunningApplicationBlocksCleanupEvenWithoutLocalMutex()
    {
        Add("old");
        using (WindowsInstallationGate.Application(_root)) Assert.ThrowsAny<IOException>(() => Run());
        Assert.Equal(1, Run().Removed);
    }

    [WindowsInstallationFact]
    public void LockedLegacyExecutablePreventsAnyDeletion()
    {
        Add("old");
        using var running = File.Open(Path.Combine(Payload("old"), "OpticalSystemDesign.App.exe"), FileMode.Open, FileAccess.Read, FileShare.Read);
        Assert.Equal(0, Run().Removed);
        Assert.Equal("example", File.ReadAllText(Path.Combine(Payload("old"), "data", "sample.txt")));
    }

    [WindowsInstallationFact]
    public void LegacyInstallerMutexBlocksCleanupWithoutDirectoryLock()
    {
        Add("old");
        using (var setup = new Mutex(false, WindowsInstallationLifetime.MutexName(_id) + "-Setup"))
        {
            Assert.Throws<IOException>(() => Run());
            Assert.True(File.Exists(Path.Combine(Payload("old"), "OpticalSystemDesign.App.exe")));
        }
        Assert.Equal(1, Run().Removed);
    }

    [WindowsInstallationTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void JunctionsAreRejectedWithoutFollowingOrDeletingTheirTargets(bool atRoot)
    {
        Add("old");
        var outside = Path.Combine(_root, "outside"); Directory.CreateDirectory(outside);
        var sentinel = Path.Combine(outside, "keep.txt"); File.WriteAllText(sentinel, "untouched");
        var link = atRoot ? Path.Combine(_root, "root-link") : Path.Combine(Payload("old"), "extra-link");
        var start = new ProcessStartInfo("powershell.exe")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-NonInteractive"); start.ArgumentList.Add("-Command");
        start.ArgumentList.Add("$ErrorActionPreference='Stop'; New-Item -ItemType Junction -Path '" + link.Replace("'", "''")
            + "' -Target '" + outside.Replace("'", "''") + "' | Out-Null");
        using var process = Process.Start(start)!;
        Assert.True(process.WaitForExit(15000));
        Assert.Equal(0, process.ExitCode);
        try
        {
            if (atRoot) Assert.Throws<InvalidDataException>(() => WindowsInstallationGate.Maintenance(link));
            else Assert.Equal(0, Run().Removed);
            Assert.Equal("untouched", File.ReadAllText(sentinel));
            Assert.True(File.Exists(Path.Combine(Payload("old"), "OpticalSystemDesign.App.exe")));
        }
        finally { Directory.Delete(link, recursive: false); }
    }

    private InstallationCleanupResult Run(string executing = "current", Action<string>? afterDelete = null) =>
        WindowsInstallationCleanup.Run(_root, Payload(executing), _id, afterDelete);
    private string Payload(string id) => Path.Combine(_root, "versions", id);
    private string Receipt(string id) => Path.Combine(_root, ".installed-releases", id + ".ini");
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private void Add(string id)
    {
        Directory.CreateDirectory(Path.Combine(Payload(id), "data"));
        var exe = Path.Combine(Payload(id), "OpticalSystemDesign.App.exe"); File.WriteAllText(exe, "binary");
        var data = Path.Combine(Payload(id), "data", "sample.txt"); File.WriteAllText(data, "example");
        ReplaceManifest(id, $"{Hash(exe)}  OpticalSystemDesign.App.exe\n{Hash(data)}  data/sample.txt\n");
    }
    [WindowsInstallationFact]
    public void UnrecognizedExecutablePayloadIsRetainedWithoutMigration()
    {
        Add("old");
        var previousName = "unrecognized-product.exe";
        var current = Path.Combine(Payload("old"), "OpticalSystemDesign.App.exe");
        var previous = Path.Combine(Payload("old"), previousName);
        File.Move(current, previous);
        ReplaceManifest("old", $"{Hash(previous)}  {previousName}\n{Hash(Path.Combine(Payload("old"), "data", "sample.txt"))}  data/sample.txt\n");
        var result = Run();
        Assert.Equal(0, result.Removed);
        Assert.Single(result.Retained);
        Assert.True(Directory.Exists(Payload("old")));
        Assert.Equal("binary", File.ReadAllText(previous));
    }

    private void ReplaceManifest(string id, string manifest)
    {
        var copy = Path.ChangeExtension(Receipt(id), ".sha256"); File.WriteAllText(copy, manifest);
        File.WriteAllText(Path.Combine(Payload(id), "release.files.sha256"), manifest);
        File.WriteAllText(Receipt(id), $"[Release]\nFormat=1\nProductAppId={_id}\nId={id}\nManifestSha256={Hash(copy)}\n");
    }
    public void Dispose() => Directory.Delete(_root, recursive: true);
}

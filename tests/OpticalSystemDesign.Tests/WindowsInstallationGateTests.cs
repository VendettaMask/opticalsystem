using System.Diagnostics;
using OpticalSystemDesign.App.Services;

namespace OpticalSystemDesign.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class WindowsInstallationGateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "installation-gate-" + Guid.NewGuid().ToString("N"));
    public WindowsInstallationGateTests() => Directory.CreateDirectory(_root);

    [WindowsInstallationFact]
    public void MultipleApplicationsCoexistAndMaintenanceWaitsForAllOfThem()
    {
        using (var first = WindowsInstallationGate.Application(_root))
        {
            using (WindowsInstallationGate.Application(_root))
                Assert.ThrowsAny<IOException>(() => WindowsInstallationGate.Maintenance(_root));
            Assert.ThrowsAny<IOException>(() => WindowsInstallationGate.Maintenance(_root));
        }
        using var maintenance = WindowsInstallationGate.Maintenance(_root);
    }

    [WindowsInstallationFact]
    public void MaintenanceRejectsApplicationAndAnotherMaintenanceThenReleases()
    {
        using (WindowsInstallationGate.Maintenance(_root))
        {
            Assert.ThrowsAny<IOException>(() => WindowsInstallationGate.Application(_root));
            Assert.ThrowsAny<IOException>(() => WindowsInstallationGate.Maintenance(_root));
        }
        using var app = WindowsInstallationGate.Application(_root);
    }

    [WindowsInstallationFact]
    public void OtherInstallRootIsIndependent()
    {
        var other = Path.Combine(_root, "other"); Directory.CreateDirectory(other);
        using var app = WindowsInstallationGate.Application(_root);
        using var maintenance = WindowsInstallationGate.Maintenance(other);
    }

    [WindowsInstallationTheory]
    [InlineData(".application.lock")]
    [InlineData(".setup.lock")]
    public async Task IndependentProcessWithoutLocalMutexBlocksMaintenanceAndHardExitReleasesLock(string name)
    {
        var start = new ProcessStartInfo("powershell.exe")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-NonInteractive"); start.ArgumentList.Add("-Command");
        start.ArgumentList.Add("$lock = [IO.File]::Open('" + Path.Combine(_root, name).Replace("'", "''")
            + "',[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None); [Console]::WriteLine('READY'); Start-Sleep -Seconds 60");
        using var process = Process.Start(start)!;
        try
        {
            Assert.Equal("READY", await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15)));
            Assert.ThrowsAny<IOException>(() => WindowsInstallationGate.Maintenance(_root));
            process.Kill(); // This test's exact child, never a user's application.
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            // Windows process termination can signal before its final file-handle
            // cleanup completes. Verify eventual release, without weakening the
            // assertion that maintenance was blocked while the child was alive.
            var deadline = Stopwatch.StartNew();
            while (true)
            {
                try { using var maintenance = WindowsInstallationGate.Maintenance(_root); break; }
                catch (IOException) when (deadline.Elapsed < TimeSpan.FromSeconds(5)) { await Task.Delay(25); }
            }
        }
        finally { if (!process.HasExited) { process.Kill(); await process.WaitForExitAsync(); } }
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}

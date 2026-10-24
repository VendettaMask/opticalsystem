using System.Diagnostics;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Services.Updates;

namespace OpticalSystemDesign.Tests;

public sealed class OtaHelperRuntimeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "optical ota runtime " + Guid.NewGuid().ToString("N"));
    private string Folder(string name) { var path = Path.Combine(_root, name); Directory.CreateDirectory(path); return path; }
    private string Source()
    {
        var source = Folder("source");
        foreach (var name in new[] { "OpticalSystemDesign.App.exe", "OpticalSystemDesign.App.dll",
                     "OpticalSystemDesign.App.deps.json", "OpticalSystemDesign.App.runtimeconfig.json", "coreclr.dll", "release.identity.json" })
            File.WriteAllText(Path.Combine(source, name), "fixture " + name);
        return source;
    }

    [Fact]
    public void CopiesRuntimeBytesAndNativeDependenciesWithoutUserDataOrExamples()
    {
        var source = Source(); var operation = Folder("operation");
        var native = Path.Combine(source, "runtimes", "win-x64", "native"); Directory.CreateDirectory(native);
        File.WriteAllText(Path.Combine(native, "dependency.dll"), "native bytes");
        var culture = Path.Combine(source, "ja"); Directory.CreateDirectory(culture);
        File.WriteAllText(Path.Combine(culture, "dependency.resources.dll"), "localized bytes");
        File.WriteAllText(Path.Combine(source, "settings.json"), "do not copy");
        File.WriteAllText(Path.Combine(source, "OpticalSystemDesign.App.pdb"), "do not copy");
        var data = Path.Combine(source, "Examples"); Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "user.staropt"), "do not copy");
        var executable = OtaHelperRuntime.Stage(source, operation);
        var staged = Path.GetDirectoryName(executable)!;
        foreach (var file in Directory.GetFiles(staged, "*", SearchOption.AllDirectories))
            Assert.Equal(File.ReadAllBytes(Path.Combine(source, Path.GetRelativePath(staged, file))), File.ReadAllBytes(file));
        Assert.True(File.Exists(Path.Combine(staged, "runtimes", "win-x64", "native", "dependency.dll")));
        Assert.True(File.Exists(Path.Combine(staged, "ja", "dependency.resources.dll")));
        Assert.False(File.Exists(Path.Combine(staged, "settings.json")));
        Assert.False(File.Exists(Path.Combine(staged, "OpticalSystemDesign.App.pdb")));
        Assert.False(Directory.Exists(Path.Combine(staged, "Examples")));
        OtaHelperRuntime.ValidateLocation(operation, executable);
        Assert.Throws<InvalidDataException>(() => OtaHelperRuntime.ValidateLocation(operation, Path.Combine(source, OtaHelperRuntime.ExecutableName)));
        Assert.Throws<InvalidDataException>(() => OtaHelperRuntime.ValidateLocation(operation, Path.Combine(operation, OtaHelperRuntime.ExecutableName)));
        Assert.Throws<IOException>(() => OtaHelperRuntime.Stage(source, operation));
    }

    [Theory]
    [InlineData("OpticalSystemDesign.App.exe")]
    [InlineData("OpticalSystemDesign.App.dll")]
    [InlineData("OpticalSystemDesign.App.deps.json")]
    [InlineData("OpticalSystemDesign.App.runtimeconfig.json")]
    public void IncompleteRuntimeCannotBeLaunched(string missing)
    {
        var source = Source(); File.Delete(Path.Combine(source, missing));
        Assert.Throws<InvalidDataException>(() => OtaHelperRuntime.Stage(source, Folder("operation")));
    }

    [WindowsInstallationFact]
    public async Task WaitsForActualFileHandleReleaseAndNeverForcesItClosed()
    {
        var source = Source(); var executable = Path.Combine(source, OtaHelperRuntime.ExecutableName);
        using var held = new FileStream(executable, FileMode.Open, FileAccess.Read, FileShare.Read);
        var pending = WindowsOtaHandoff.WaitForExecutableReleaseAsync(executable, TimeSpan.FromSeconds(2));
        Assert.False(pending.IsCompleted);
        await Assert.ThrowsAnyAsync<IOException>(() => WindowsOtaHandoff.WaitForExecutableReleaseAsync(executable, TimeSpan.Zero));
        Assert.True(held.CanRead);
        held.Dispose();
        await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("fixture " + OtaHelperRuntime.ExecutableName, File.ReadAllText(executable));
    }

    [WindowsInstallationFact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public async Task RestartWaitsForBothInstallerFileGateAndFinalBootstrapperMarker()
    {
        var root = Folder("installation");
        var id = "OpticalSystemDesign-OtaValidation-" + Guid.NewGuid().ToString("N");
        using var gate = WindowsInstallationGate.Maintenance(root);
        using var marker = new Mutex(false, WindowsInstallationLifetime.MutexName(id) + "-Setup");
        var pending = WindowsOtaHandoff.WaitForMaintenanceReleaseAsync(root, id, TimeSpan.FromSeconds(3));
        Assert.False(pending.IsCompleted);
        await Assert.ThrowsAnyAsync<IOException>(() => WindowsOtaHandoff.WaitForMaintenanceReleaseAsync(root, id, TimeSpan.Zero));
        marker.Dispose();
        await Assert.ThrowsAnyAsync<IOException>(() => WindowsOtaHandoff.WaitForMaintenanceReleaseAsync(root, id, TimeSpan.Zero));
        gate.Dispose();
        await pending.WaitAsync(TimeSpan.FromSeconds(5));
        using var released = WindowsInstallationGate.Maintenance(root);
    }

    [WindowsInstallationFact]
    public async Task RealProductRunsFromCopyWhileOriginalExecutableIsExclusivelyAvailable()
    {
        var source = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_OTA_RUNTIME_SOURCE")
            ?? Path.GetDirectoryName(typeof(WindowsOtaHandoff).Assembly.Location)!;
        var original = Path.Combine(source, OtaHelperRuntime.ExecutableName);
        var executable = OtaHelperRuntime.Stage(source, Folder("real operation"));
        var report = Path.Combine(_root, "product-verification.json");
        var start = new ProcessStartInfo(executable)
        { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(executable)!, RedirectStandardError = true };
        if (File.Exists(Path.Combine(source, "coreclr.dll")))
        {
            // Self-contained acceptance must not borrow the installed SDK/runtime.
            start.Environment["DOTNET_ROOT"] = Path.Combine(_root, "no installed runtime");
            start.Environment["DOTNET_ROOT_X64"] = start.Environment["DOTNET_ROOT"];
            start.Environment["DOTNET_MULTILEVEL_LOOKUP"] = "0";
        }
        foreach (var value in new[] { "--verify-installation", report, "--hold-milliseconds", "3000" }) start.ArgumentList.Add(value);
        using var process = Process.Start(start)!;
        try
        {
            var deadline = Stopwatch.StartNew();
            while (!File.Exists(report) && !process.HasExited && deadline.Elapsed < TimeSpan.FromSeconds(20)) await Task.Delay(50);
            Assert.True(File.Exists(report), process.HasExited ? await process.StandardError.ReadToEndAsync() : "No verification report.");
            Assert.False(process.HasExited);
            Assert.Equal(executable, process.MainModule!.FileName);
            // The installer uses this exact access/share combination for the old EXE.
            using var oldProbe = new FileStream(original, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.ThrowsAny<IOException>(() => new FileStream(executable, FileMode.Open, FileAccess.ReadWrite, FileShare.None));
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(0, process.ExitCode);
        }
        finally
        {
            // Only this test's exact, non-GUI verification child.
            if (!process.HasExited) { process.Kill(); await process.WaitForExitAsync(); }
        }
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}

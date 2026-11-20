using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace OpticalSystemDesign.Tests;

public sealed class DesktopLauncherTests
{
    [Theory]
    [InlineData("standard")]
    [InlineData("explicit-root")]
    [InlineData("incompatible-sdk")]
    public async Task WindowsLauncherChecksThePinnedSdkWithASparsePath(string scenario)
    {
        if (!OperatingSystem.IsWindows()) return;

        var repository = ProductDependencyArchitectureTests.RepositoryRoot();
        using var files = new CameraSensorFileTests.TemporaryFiles();
        var launcher = files.Path(Path.Combine("source & files", "Run-OpticalSystemDesign.cmd"));
        var workingDirectory = Path.GetDirectoryName(launcher)!;
        Directory.CreateDirectory(workingDirectory);
        File.Copy(Path.Combine(repository, "Run-OpticalSystemDesign.cmd"), launcher);
        var globalJson = File.ReadAllText(Path.Combine(repository, "global.json"));
        if (scenario == "incompatible-sdk")
            globalJson = """{"sdk":{"version":"99.0.100","rollForward":"disable"}}""";
        File.WriteAllText(Path.Combine(workingDirectory, "global.json"), globalJson);

        var host = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", "dotnet.exe"));
        Assert.True(File.Exists(host), $"Shared .NET host: {host}");
        var systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var start = new ProcessStartInfo(Path.Combine(systemDirectory, "cmd.exe"))
        {
            Arguments = "/d /s /c \"\"" + launcher + "\" --check\"",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.Environment["PATH"] = systemDirectory;
        start.Environment.Remove("DOTNET_ROOT_X64");
        start.Environment["DOTNET_ROOT"] = scenario == "explicit-root"
            ? Path.GetDirectoryName(host)! : files.Path("missing-sdk");
        using var process = Process.Start(start)!;
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw;
        }
        var output = await outputTask + await errorTask;
        if (scenario == "incompatible-sdk")
        {
            Assert.True(process.ExitCode == 1, $"Exit {process.ExitCode}: {output}");
            Assert.Contains("global.json", output);
            Assert.Contains("99.0.100", output);
        }
        else
        {
            Assert.True(process.ExitCode == 0, output);
            using var settings = JsonDocument.Parse(globalJson);
            var expected = settings.RootElement.GetProperty("sdk").GetProperty("version").GetString()!;
            Assert.Contains(expected[..expected.LastIndexOf('.')], output);
            Assert.Contains(".NET host:", output);
        }
        Assert.DoesNotContain("Restoring package dependencies", output);
    }
}

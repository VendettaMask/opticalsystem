using System.Diagnostics;

namespace OpticalSystemDesign.Tests;

public sealed class MacPackagingTests
{
    [Fact]
    public void AppleSiliconPlanDoesNotCreatePackagesAndUsesTheProductVersion()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var destination = Path.Combine(Path.GetTempPath(), "mac package plan " + Guid.NewGuid().ToString("N"));
        var result = Run("--plan", "--test-package", "--output-root", destination);
        Assert.True(result.ExitCode == 0, result.Output);
        var version = global::OpticalSystemDesign.App.Services.ProductVersionInfo.Current;
        Assert.Contains($"OpticalSystemDesign-{version}-osx-arm64-AdHocTest.dmg", result.Output);
        Assert.False(Directory.Exists(destination));
    }

    [Theory]
    [InlineData("osx-x64")]
    [InlineData("win-arm64")]
    [InlineData("linux-arm64")]
    public void UnsupportedMacArchitecturesFailWithoutCreatingAnOutput(string runtime)
    {
        if (!OperatingSystem.IsMacOS()) return;
        var destination = Path.Combine(Path.GetTempPath(), "mac reject " + Guid.NewGuid().ToString("N"));
        var result = Run("--runtime", runtime, "--output-root", destination);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Only osx-arm64", result.Output);
        Assert.False(Directory.Exists(destination));
    }

    private static (int ExitCode, string Output) Run(params string[] args)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OpticalSystemDesign.slnx"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var start = new ProcessStartInfo("bash") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add(Path.Combine(directory.FullName, "scripts", "build-macos.sh"));
        foreach (var argument in args) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30_000)) { process.Kill(entireProcessTree: true); throw new TimeoutException(); }
        return (process.ExitCode, output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
    }
}

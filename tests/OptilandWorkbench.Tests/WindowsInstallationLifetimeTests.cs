using System.Text.Json;
using OptilandWorkbench.App.Services;

namespace OptilandWorkbench.Tests;

public sealed class WindowsInstallationLifetimeTests
{
    [Theory]
    [InlineData("")]
    [InlineData("a\\b")]
    [InlineData("a,b")]
    [InlineData("../other")]
    public void InvalidProductIdentityIsRejected(string identity) =>
        Assert.Throws<InvalidDataException>(() => WindowsInstallationLifetime.MutexName(identity));

    [Fact]
    public void ProductIdentityHasStableMutexAcrossVersions() =>
        Assert.Equal(@"Local\OpticalSystemDesign-8D387EA8-F349-447F-B5D3-B58E27F46D4A-Application",
            WindowsInstallationLifetime.MutexName(WindowsInstallationLifetime.ProductId));

    [Fact]
    public void RunningPresenceSurvivesOtherInstanceExit()
    {
        if (!OperatingSystem.IsWindows()) return;
        var id = "OpticalInstallationTest-" + Guid.NewGuid().ToString("N");
        using var first = WindowsInstallationLifetime.Enter(id);
        using (WindowsInstallationLifetime.Enter(id)) { }
        Assert.True(Mutex.TryOpenExisting(WindowsInstallationLifetime.MutexName(id), out var marker));
        marker!.Dispose();
    }

    [Fact]
    public void SetupPresenceRejectsStartupWithoutKeepingApplicationMarker()
    {
        if (!OperatingSystem.IsWindows()) return;
        var id = "OpticalInstallationTest-" + Guid.NewGuid().ToString("N");
        using var setup = new Mutex(false, WindowsInstallationLifetime.MutexName(id) + "-Setup");
        Assert.Throws<InvalidOperationException>(() => WindowsInstallationLifetime.Enter(id));
        Assert.False(Mutex.TryOpenExisting(WindowsInstallationLifetime.MutexName(id), out var marker));
        marker?.Dispose();
    }

    [Fact]
    public async Task VerificationLoadsLegacySettingsWithoutModifyingThem()
    {
        var directory = Path.Combine(Path.GetTempPath(), "installation-verification-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var settings = Path.Combine(directory, "legacy-settings.json");
        var report = Path.Combine(directory, "report.json");
        var input = "{\"Theme\":\"Dark\",\"DecimalPlaces\":5,\"LeftPaneWidth\":400}";
        try
        {
            await File.WriteAllTextAsync(settings, input);
            await InstallationVerification.RunAsync(["--verify-installation", report, "--settings", settings]);
            Assert.Equal(input, await File.ReadAllTextAsync(settings));
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(report));
            Assert.True(json.RootElement.GetProperty("SettingsLoaded").GetBoolean());
            Assert.False(json.RootElement.GetProperty("UserDataWritten").GetBoolean());
            Assert.False(json.RootElement.GetProperty("GuiVerified").GetBoolean());
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task VerificationRequiresExplicitOutputAndBoundsHoldTime()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => InstallationVerification.RunAsync(["--verify-installation"]));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => InstallationVerification.RunAsync(
            ["--verify-installation", "unused.json", "--hold-milliseconds", "60001"]));
    }
}

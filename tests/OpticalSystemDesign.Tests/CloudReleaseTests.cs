using System.Diagnostics;
using System.Text.Json;

namespace OpticalSystemDesign.Tests;

public sealed class CloudReleaseTests
{
    [Fact]
    public void CloudWorkflowGatesPublishingAndRestrictsWriteCredentialsToPublisher()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "workflows", "release.yml"));
        Assert.Contains("workflow_dispatch:", text);
        Assert.Contains("tags: ['v*']", text);
        Assert.Contains("cancel-in-progress: false", text);
        Assert.Contains("needs: [plan, package]", text);
        Assert.Contains("CloudReleaseTests", text);
        Assert.Contains("verify-built-in-package.ps1", text);
        Assert.Contains("GitFrozenIntegrity.ps1", text);
        Assert.Contains("verify-documentation.ps1 -Strict", text);
        Assert.Contains("publish-github-exe.ps1", text);
        Assert.Contains("GH_TOKEN: ${{ github.token }}", text);
        Assert.DoesNotContain("${{ secrets.", text);
        Assert.DoesNotContain("--clobber", text);
        Assert.True(text.IndexOf("  publish:", StringComparison.Ordinal) < text.IndexOf("contents: write", StringComparison.Ordinal));
        Assert.Contains("$env:REQUESTED_TAG", text);
        Assert.DoesNotContain("$tag = '${{", text);
    }

    [Fact]
    public void ExeOnlyHandoffPlanNeedsNeitherNetworkNorCredentials()
    {
        using var fixture = new Handoff();
        // Extra internal files are never interpreted as release attachments.
        File.WriteAllText(Path.Combine(fixture.Directory, "release.json"), "internal");
        File.WriteAllText(Path.Combine(fixture.Directory, "installer.exe.sha256"), "internal");
        var result = fixture.Run();
        Assert.True(result.ExitCode == 0, result.Output);
        using var json = JsonDocument.Parse(result.Output);
        var packages = json.RootElement.GetProperty("Packages");
        Assert.Equal(1, packages.GetArrayLength());
        Assert.Equal(fixture.ExeName, packages[0].GetProperty("Name").GetString());
        Assert.Equal(64, packages[0].GetProperty("Sha256").GetString()!.Length);
        Assert.False(System.IO.Directory.Exists(Path.Combine(fixture.Directory, "release-evidence")));
    }

    [Theory]
    [InlineData("SourceDirty", "true")]
    [InlineData("ValidationHooks", "true")]
    [InlineData("ModulesInProcess", "false")]
    [InlineData("Signed", "true")]
    [InlineData("TestPackage", "false")]
    [InlineData("SourceCommit", "\"not-a-commit\"")]
    [InlineData("Version", "\"99.0.0\"")]
    [InlineData("Runtime", "\"win-arm64\"")]
    [InlineData("ProductAppId", "\"other-product\"")]
    [InlineData("RequiredModules", "[\"InitialStructure\"]")]
    public void UnsafeBuildRecordsAreRejectedBeforeNetworking(string key, string value)
    {
        using var fixture = new Handoff();
        fixture.Record[key] = JsonSerializer.Deserialize<JsonElement>(value);
        fixture.SaveRecord();
        var result = fixture.Run();
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Invalid build identity", result.Output);
        Assert.DoesNotContain("GH_TOKEN is required", result.Output);
    }

    [Fact]
    public void NonExeBytesAndUnexpectedInstallerNamesAreRejected()
    {
        using var fixture = new Handoff();
        var path = Path.Combine(fixture.Directory, fixture.ExeName);
        File.WriteAllText(path, "not an exe");
        var bytes = fixture.Run();
        Assert.NotEqual(0, bytes.ExitCode);
        Assert.Contains("not a Windows EXE", bytes.Output);
        File.Move(path, Path.Combine(fixture.Directory, "unknown.exe"));
        var name = fixture.Run();
        Assert.NotEqual(0, name.ExitCode);
        Assert.Contains("Unexpected installer name", name.Output);
    }

    [Fact]
    public void ReplacingOldAssetsRequiresVerifiedUploadsAndKeepsRecoverableBackups()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "scripts", "publish-github-exe.ps1"));
        Assert.Contains("-not $ReplaceExisting", text);
        Assert.Contains("Unrecognized release asset; preserved for review", text);
        Assert.Contains("Previous asset backup verification failed; release unchanged", text);
        Assert.Contains("$Asset.digest -cne", text);
        Assert.Contains("$record.SourceCommit -cne $env:GITHUB_SHA", text);
        Assert.Contains("previous-assets.json", text);
        Assert.Contains("$renamed.Add($asset)", text);
        Assert.Contains("$rollbackComplete", text);
        Assert.Contains("Build source differs", text);
        Assert.DoesNotContain("refs/tags", text); // No tag-moving API.
        var remove = text.IndexOf("foreach ($asset in @($renamed) + @($sidecars))", StringComparison.Ordinal);
        Assert.True(remove > text.IndexOf("Assert-UploadedAsset $uploaded $entry.Package", StringComparison.Ordinal));
        Assert.True(text.IndexOf("if ($PlanOnly)", StringComparison.Ordinal) < text.IndexOf("if (-not $env:GH_TOKEN)", StringComparison.Ordinal));
    }

    private sealed class Handoff : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "optical-cloud-plan-" + Guid.NewGuid().ToString("N"));
        public string Version { get; } = global::OpticalSystemDesign.App.Services.ProductVersionInfo.Current;
        public string ExeName => $"OpticalSystemDesign-{Version}-win-x64-UnsignedTest-Setup.exe";
        public Dictionary<string, object> Record { get; }

        public Handoff()
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllBytes(Path.Combine(Directory, ExeName), [0x4d, 0x5a, 0, 0]);
            Record = new()
            {
                ["Version"] = Version, ["Runtime"] = "win-x64", ["TestPackage"] = true,
                ["SourceDirty"] = false, ["ValidationHooks"] = false, ["Signed"] = false,
                ["ModulesInProcess"] = true, ["RequiredModules"] = new[] { "InitialStructure", "CoatingDesign" },
                ["ProductAppId"] = "OpticalSystemDesign-8D387EA8-F349-447F-B5D3-B58E27F46D4A",
                ["SourceCommit"] = new string('a', 40), ["ReleaseId"] = $"{Version}-win-x64-0123456789ab"
            };
            SaveRecord();
        }

        public void SaveRecord() => File.WriteAllText(Path.Combine(Directory, "release-win-x64.json"), JsonSerializer.Serialize(Record));

        public (int ExitCode, string Output) Run()
        {
            var start = new ProcessStartInfo("pwsh")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            start.Environment["GH_TOKEN"] = "";
            start.Environment["GITHUB_SHA"] = "";
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File",
                Path.Combine(RepositoryRoot(), "scripts", "publish-github-exe.ps1"),
                "-Repository", "VendettaMask/opticalsystem", "-Tag", $"v{Version}-test.20261022",
                "-DistributionDirectory", Directory, "-PlanOnly" }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(30_000))
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException("Local release-plan validation timed out.");
            }
            return (process.ExitCode, output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
        }

        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OpticalSystemDesign.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}

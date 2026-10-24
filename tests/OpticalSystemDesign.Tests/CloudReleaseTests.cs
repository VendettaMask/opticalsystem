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
        foreach (var name in new[] { "LensConfigurationNavigationTests", "LensEditorLayoutTests", "LensHeaderTypographyTests",
            "LensMaterialRowThemeTests", "ViewerSettingsInputThemeTests", "ViewerConfigurationTests", "Viewer3DGeometryTests" })
            Assert.Contains("FullyQualifiedName~" + name, text);
        Assert.Contains("LogFileName=lens-viewers.trx", text);
        foreach (var name in new[] { "AnalysisResponsivePlotTests", "AnalysisImageExportTests", "SpotTitleLayoutTests",
            "AnalysisLegendLayoutTests", "FieldCurvatureFooterTests", "ConfigurationMatrixAnalysisGuiTests" })
            Assert.Contains("FullyQualifiedName~" + name, text);
        Assert.Contains("OPTICAL_SYSTEM_DESIGN_ANALYSIS_FIT_CAPTURE_DIR", text);
        Assert.Contains("OPTICAL_SYSTEM_DESIGN_ANALYSIS_EXPORT_CAPTURE_DIR", text);
        Assert.Contains("LogFileName=analysis-contracts.trx", text);
        Assert.Contains("LogFileName=analysis-render.trx", text);
        Assert.Contains("LogFileName=analysis-export.trx", text);
        Assert.Contains("AboutUpdateTests.CheckButtonShowsBusyProgressAndPersistentLocalizedResults", text);
        Assert.Contains("OPTICAL_SYSTEM_DESIGN_UPDATE_CAPTURE_DIR", text);
        Assert.Contains("verify-built-in-package.ps1", text);
        Assert.Contains("FullyQualifiedName~OtaHelperRuntimeTests", text);
        Assert.Contains("OPTICAL_SYSTEM_DESIGN_OTA_RUNTIME_SOURCE = $payload", text);
        Assert.Contains("LogFileName=ota-runtime-package.trx", text);
        Assert.Contains("GitFrozenIntegrity.ps1", text);
        Assert.Contains("verify-documentation.ps1 -Strict", text);
        Assert.Contains("publish-github-exe.ps1", text);
        Assert.Contains("GH_TOKEN: ${{ github.token }}", text);
        var publishStart = text.IndexOf("  publish:", StringComparison.Ordinal);
        Assert.DoesNotContain("${{ secrets.", text[..publishStart]);
        foreach (var name in new[] { "OPTICAL_OTA_SIGNING_KEY", "OPTICAL_OTA_ENCRYPTED_BACKUP", "OPTICAL_OTA_RECOVERY_PASSWORD" })
            Assert.Contains("${{ secrets." + name + " }}", text[publishStart..]);
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(text, @"\$\{\{ secrets\.").Count);
        Assert.Contains("test-ota-key-crypto.ps1", text);
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
    [InlineData(false)]
    [InlineData(true)]
    public void AppleSiliconDmgCanBePlannedAloneOrAlongsideWindowsWithoutCredentials(bool includeWindows)
    {
        using var fixture = new Handoff();
        fixture.AddMac();
        if (!includeWindows) File.Delete(Path.Combine(fixture.Directory, fixture.ExeName));
        var result = fixture.Run();
        Assert.True(result.ExitCode == 0, result.Output);
        using var json = JsonDocument.Parse(result.Output);
        var packages = json.RootElement.GetProperty("Packages").EnumerateArray().ToArray();
        Assert.Equal(includeWindows ? 2 : 1, packages.Length);
        var mac = Assert.Single(packages, item => item.GetProperty("Runtime").GetString() == "osx-arm64");
        Assert.EndsWith("-osx-arm64-AdHocTest.dmg", mac.GetProperty("Name").GetString());
    }

    [Theory]
    [InlineData("bytes")]
    [InlineData("architecture")]
    [InlineData("signature")]
    [InlineData("notarization")]
    [InlineData("dirty")]
    public void InvalidMacHandoffsAreRejectedBeforeCredentialsOrNetworking(string defect)
    {
        using var fixture = new Handoff();
        fixture.AddMac();
        File.Delete(Path.Combine(fixture.Directory, fixture.ExeName));
        var path = Path.Combine(fixture.Directory, fixture.MacName);
        var recordPath = Path.Combine(fixture.Directory, "release-osx-arm64.json");
        var record = JsonSerializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(recordPath))!;
        switch (defect)
        {
            case "bytes": File.WriteAllBytes(path, new byte[512]); break;
            case "architecture": File.Move(path, path.Replace("osx-arm64", "osx-x64", StringComparison.Ordinal)); break;
            case "signature": record["MacCodeSignature"] = "DeveloperId"; break;
            case "notarization": record["Notarized"] = true; break;
            case "dirty": record["SourceDirty"] = true; break;
        }
        File.WriteAllText(recordPath, JsonSerializer.Serialize(record));
        var result = fixture.Run();
        Assert.NotEqual(0, result.ExitCode);
        Assert.DoesNotContain("GH_TOKEN is required", result.Output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AddingMacPreservesOnlyValidUntouchedWindowsOtaEnvelopes(bool tamper)
    {
        var path = Path.Combine(RepositoryRoot(), "scripts", "publish-github-exe.ps1").Replace("'", "''");
        var command = $$"""
            $ErrorActionPreference = 'Stop'
            $ast = [System.Management.Automation.Language.Parser]::ParseFile('{{path}}', [ref]$null, [ref]$null)
            $definition = $ast.Find({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Get-PreservedOtaMarkers' }, $true)
            Invoke-Expression $definition.Extent.Text
            $rsa = [Security.Cryptography.RSA]::Create(4096)
            try {
                $bytes = [Text.Encoding]::UTF8.GetBytes((@{ Name='windows.exe'; Runtime='win-x64'; Size=123; Sha256=('a'*64) } | ConvertTo-Json -Compress))
                $signature = $rsa.SignData($bytes, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pss)
                $marker = "<!-- optical-system-design-ota-v1`n$([Convert]::ToBase64String($bytes)).$([Convert]::ToBase64String($signature))`n-->"
                $asset = [pscustomobject]@{ name='windows.exe'; size=123; digest=('sha256:' + ('{{(tamper ? "b" : "a")}}'*64)) }
                $failed = $false
                try {
                    $result = @(Get-PreservedOtaMarkers $marker @($asset) @('mac.dmg') $rsa.ExportSubjectPublicKeyInfoPem())
                    if ($result.Count -ne 1 -or $result[0] -cne $marker) { throw 'Old signature was altered.' }
                    $replaced = @(Get-PreservedOtaMarkers $marker @($asset) @('windows.exe') $rsa.ExportSubjectPublicKeyInfoPem())
                    if ($replaced.Count -ne 0) { throw 'Replaced installer retained an old envelope.' }
                } catch { $failed = $true }
                if ($failed -ne ${{tamper.ToString().ToLowerInvariant()}}) { throw 'Incorrect preservation decision.' }
                'OTA preservation passed.'
            } finally { $rsa.Dispose() }
            """;
        var result = RunPowerShell("-Command", command);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("OTA preservation passed", result.Output);
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
        Assert.Contains("Test OTA build sequence is missing or differs", text);
        Assert.DoesNotContain("if (-not $testPackage)", text); // Test EXEs must be signed too.
        var remove = text.IndexOf("foreach ($asset in @($renamed) + @($sidecars))", StringComparison.Ordinal);
        Assert.True(remove > text.IndexOf("Assert-UploadedAsset $uploaded $entry.Package", StringComparison.Ordinal));
        Assert.True(text.IndexOf("if ($PlanOnly)", StringComparison.Ordinal) < text.IndexOf("if (-not $env:GH_TOKEN)", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void GitHubJsonArraysAreEnumeratedRatherThanNested(int count)
    {
        // Exercise the actual API wrapper with the same non-enumerated JSON-array
        // shape as Invoke-RestMethod, without credentials or any network request.
        var scriptPath = Path.Combine(RepositoryRoot(), "scripts", "publish-github-exe.ps1").Replace("'", "''");
        var command = $$"""
            $ErrorActionPreference = 'Stop'
            $ast = [System.Management.Automation.Language.Parser]::ParseFile('{{scriptPath}}', [ref]$null, [ref]$null)
            $definition = $ast.Find({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Invoke-ReleaseApi' }, $true)
            Invoke-Expression $definition.Extent.Text
            function Invoke-RestMethod { ,@(for ($i=0; $i -lt {{count}}; $i++) { [pscustomobject]@{ name = "asset-$i.exe" } }) }
            $base = 'https://example.invalid'
            $headers = @{}
            $assets = @(Invoke-ReleaseApi GET 'unused')
            if ($assets.Count -ne {{count}}) { throw "Nested API array: $($assets.Count)" }
            foreach ($asset in $assets) { if ($asset -is [array]) { throw 'Nested element.' } }
            'API array shape passed.'
            """;
        var result = RunPowerShell("-Command", command);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("API array shape passed", result.Output);
    }

    private sealed class Handoff : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "optical-cloud-plan-" + Guid.NewGuid().ToString("N"));
        public string Version { get; } = global::OpticalSystemDesign.App.Services.ProductVersionInfo.Current;
        public string ExeName => $"OpticalSystemDesign-{Version}-win-x64-UnsignedTest-Setup.exe";
        public string MacName => $"OpticalSystemDesign-{Version}-osx-arm64-AdHocTest.dmg";
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

        public void AddMac()
        {
            var bytes = new byte[1024];
            new byte[] { 0x6b, 0x6f, 0x6c, 0x79 }.CopyTo(bytes, bytes.Length - 512);
            File.WriteAllBytes(Path.Combine(Directory, MacName), bytes);
            var record = new Dictionary<string, object>(Record)
            {
                ["Runtime"] = "osx-arm64", ["ReleaseId"] = $"{Version}-osx-arm64-0123456789ab",
                ["MacCodeSignature"] = "AdHoc", ["Notarized"] = false
            };
            File.WriteAllText(Path.Combine(Directory, "release-osx-arm64.json"), JsonSerializer.Serialize(record));
        }

        public (int ExitCode, string Output) Run()
        {
            return RunPowerShell("-File",
                Path.Combine(RepositoryRoot(), "scripts", "publish-github-exe.ps1"),
                "-Repository", "VendettaMask/opticalsystem", "-Tag", $"v{Version}-test.20261022",
                "-DistributionDirectory", Directory, "-PlanOnly");
        }

        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }

    private static (int ExitCode, string Output) RunPowerShell(params string[] arguments)
    {
        var start = new ProcessStartInfo("pwsh")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.Environment["GH_TOKEN"] = "";
        start.Environment["GITHUB_SHA"] = "";
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive" }.Concat(arguments)) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("Local release validation timed out.");
        }
        return (process.ExitCode, output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OpticalSystemDesign.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using OpticalSystemDesign.App.Services.Updates;

namespace OpticalSystemDesign.Tests;

public sealed class ReleaseUpdateTests
{
    [Theory]
    [InlineData("1.0.9", "1.0.10", -1)]
    [InlineData("1.9.0", "1.10.0", -1)]
    [InlineData("2.0.0", "1.99.99", 1)]
    [InlineData("1.0.4+abc", "1.0.4+def", 0)]
    [InlineData("1.0.4-rc.1", "1.0.4", -1)]
    [InlineData("1.0.4-rc.9", "1.0.4-rc.10", -1)]
    [InlineData("1.0.4-alpha.99999999999999999999999999999", "1.0.4-alpha.100000000000000000000000000000", -1)]
    [InlineData("1.0.4-alpha.1", "1.0.4-alpha.beta", -1)]
    [InlineData("1.0.4-alpha", "1.0.4-alpha.1", -1)]
    public void VersionsUseNumericSemVerPrecedence(string left, string right, int sign)
    {
        Assert.True(ReleaseVersion.TryParse(left, out var a));
        Assert.True(ReleaseVersion.TryParse(right, out var b));
        Assert.Equal(sign, Math.Sign(a.CompareTo(b)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1.0")]
    [InlineData("1.0.3.4")]
    [InlineData("v1.0.3")]
    [InlineData(" 1.0.3")]
    [InlineData("1.0.3\n")]
    [InlineData("01.0.3")]
    [InlineData("1.0.3-rc.01")]
    [InlineData("1.0.3-rc..1")]
    [InlineData("2147483648.0.0")]
    public void MalformedVersionsAreNotGuessed(string? value) => Assert.False(ReleaseVersion.TryParse(value, out _));

    [Theory]
    [InlineData("win-x64", false)]
    [InlineData("win-arm64", false)]
    [InlineData("win-x64", true)]
    public void NewerStableReleaseSelectsOnlyTheExactFullInstaller(string runtime, bool unsigned)
    {
        var result = Parse(Release(runtime: runtime, unsigned: unsigned), runtime: runtime);
        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Equal("1.0.5", result.Release!.Version);
        Assert.Contains(runtime, result.Release.Installer!.Name);
        Assert.Equal(unsigned, result.Release.Installer.Name.Contains("Unsigned"));
    }

    [Theory]
    [InlineData("1.0.5", "1.0.5")]
    [InlineData("1.0.6", "1.0.5")]
    [InlineData("1.0.5+local", "1.0.5")]
    public void EqualOrOlderLatestNeverOffersDowngrade(string installed, string latest)
    {
        var result = Parse(Release(latest), installed);
        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
        Assert.Null(result.Release!.Installer);
    }

    [Fact]
    public void StableReleaseCanUpgradeAnInstalledPrerelease() =>
        Assert.Equal(UpdateCheckStatus.UpdateAvailable, Parse(Release(), "1.0.5-rc.1").Status);

    [Fact]
    public void WrongArchitectureAndTestPackagesAreNeverOffered()
    {
        Assert.Equal(UpdateCheckStatus.NoCompatibleInstaller, Parse(Release(runtime: "win-arm64")).Status);
        var release = Release();
        release["assets"]![0]!["name"] = "OpticalSystemDesign-1.0.5-win-x64-UnsignedTest-Setup.exe";
        Assert.Equal(UpdateCheckStatus.NoCompatibleInstaller, Parse(release).Status);
        Assert.Equal(UpdateCheckStatus.UnsupportedPlatform, Parse(Release(), runtime: null).Status);
        Assert.Equal(UpdateCheckStatus.UnsupportedPlatform, Parse(Release(), runtime: "linux-x64").Status);
    }

    [Fact]
    public void DraftPrereleaseMissingFlagsAndMalformedPayloadAreRejected()
    {
        foreach (var property in new[] { "draft", "prerelease" })
        {
            var release = Release();
            release[property] = true;
            Assert.Equal(UpdateCheckStatus.InvalidResponse, Parse(release).Status);
            release.Remove(property);
            Assert.Equal(UpdateCheckStatus.InvalidResponse, Parse(release).Status);
        }
        Assert.Equal(UpdateCheckStatus.InvalidResponse, Parse(Release("1.0.5-rc.1")).Status);
        Assert.Equal(UpdateCheckStatus.InvalidResponse, Parse(Release("01.0.5")).Status);
        Assert.Equal(UpdateCheckStatus.InvalidResponse, GitHubReleaseUpdateService.Parse(Encoding.UTF8.GetBytes("{bad"), "1.0.4", "win-x64").Status);
        Assert.Equal(UpdateCheckStatus.InvalidResponse, GitHubReleaseUpdateService.Parse(Encoding.UTF8.GetBytes("[]"), "1.0.4", "win-x64").Status);
        Assert.Equal(UpdateCheckStatus.InvalidCurrentVersion, Parse(Release(), "unknown").Status);
    }

    [Theory]
    [InlineData("http://github.com/VendettaMask/opticalsystem/releases/tag/v1.0.5")]
    [InlineData("https://github.com.attacker.test/VendettaMask/opticalsystem/releases/tag/v1.0.5")]
    [InlineData("https://github.com/another/repo/releases/tag/v1.0.5")]
    [InlineData("https://github.com/VendettaMask/opticalsystem/releases/tag/v1.0.6")]
    [InlineData("file:///C:/payload.exe")]
    public void UntrustedOrMismatchedReleasePageIsRejected(string url)
    {
        var release = Release();
        release["html_url"] = url;
        Assert.Equal(UpdateCheckStatus.InvalidResponse, Parse(release).Status);
    }

    [Theory]
    [InlineData("https://evil.test/setup.exe")]
    [InlineData("https://github.com/VendettaMask/opticalsystem/releases/download/v1.0.4/OpticalSystemDesign-1.0.5-win-x64-Setup.exe")]
    [InlineData("https://github.com/VendettaMask/opticalsystem/releases/download/v1.0.5/OpticalSystemDesign-1.0.5-win-x64-Setup.exe?redirect=1")]
    public void InstallerUrlMustMatchRepositoryTagAndAssetName(string url)
    {
        var release = Release();
        release["assets"]![0]!["browser_download_url"] = url;
        Assert.Equal(UpdateCheckStatus.InvalidResponse, Parse(release).Status);
    }

    [Fact]
    public void IncompleteOrAmbiguousAssetsFailClosed()
    {
        foreach (var bad in new JsonNode?[] { JsonValue.Create(0), JsonValue.Create(-1), JsonValue.Create("123"), null })
        {
            var release = Release();
            release["assets"]![0]!["size"] = bad;
            Assert.Equal(UpdateCheckStatus.InvalidResponse, Parse(release).Status);
        }
        var duplicate = Release();
        ((JsonArray)duplicate["assets"]!).Add(duplicate["assets"]![0]!.DeepClone());
        Assert.Equal(UpdateCheckStatus.InvalidResponse, Parse(duplicate).Status);
        var pending = Release();
        pending["assets"]![0]!["state"] = "new";
        Assert.Equal(UpdateCheckStatus.InvalidResponse, Parse(pending).Status);
    }

    [Theory]
    [InlineData(404, "NoPublishedRelease")]
    [InlineData(403, "RateLimited")]
    [InlineData(429, "RateLimited")]
    [InlineData(500, "NetworkError")]
    [InlineData(302, "NetworkError")]
    public async Task HttpFailuresAreNotReportedAsAlreadyUpdated(int status, string expected)
    {
        using var client = Client((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)));
        Assert.Equal(expected, (await new GitHubReleaseUpdateService(client, "1.0.4", "win-x64").CheckAsync(default)).Status.ToString());
    }

    [Fact]
    public async Task DiscoverySendsOnlyReadOnlyUnauthenticatedRequestToTheFixedEndpoint()
    {
        var calls = 0;
        using var client = Client((request, _) =>
        {
            calls++;
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(ProductReleaseSource.LatestApi, request.RequestUri);
            Assert.Null(request.Headers.Authorization);
            Assert.Contains("OpticalSystemDesign", request.Headers.UserAgent.ToString());
            Assert.Equal("2026-03-10", Assert.Single(request.Headers.GetValues("X-GitHub-Api-Version")));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Release().ToJsonString()) });
        });
        Assert.Equal(UpdateCheckStatus.UpdateAvailable, (await new GitHubReleaseUpdateService(client, "1.0.4", "win-x64").CheckAsync(default)).Status);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task NetworkFailureTimeoutCallerCancellationAndBodyLimitAreDistinct()
    {
        using var offline = Client((_, _) => throw new HttpRequestException("offline"));
        Assert.Equal(UpdateCheckStatus.NetworkError, (await new GitHubReleaseUpdateService(offline, "1.0.4", "win-x64").CheckAsync(default)).Status);
        using var waiting = Client(async (_, token) => { await Task.Delay(Timeout.Infinite, token); return new HttpResponseMessage(); });
        Assert.Equal(UpdateCheckStatus.TimedOut, (await new GitHubReleaseUpdateService(waiting, "1.0.4", "win-x64", TimeSpan.FromMilliseconds(25)).CheckAsync(default)).Status);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new GitHubReleaseUpdateService(waiting, "1.0.4", "win-x64").CheckAsync(cancellation.Token));
        using var oversized = Client((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new ByteArrayContent(new byte[GitHubReleaseUpdateService.MaximumResponseBytes + 1]) }));
        Assert.Equal(UpdateCheckStatus.InvalidResponse, (await new GitHubReleaseUpdateService(oversized, "1.0.4", "win-x64").CheckAsync(default)).Status);
        using var slowBody = Client((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new WaitingStream()) }));
        Assert.Equal(UpdateCheckStatus.TimedOut, (await new GitHubReleaseUpdateService(slowBody, "1.0.4", "win-x64", TimeSpan.FromMilliseconds(25)).CheckAsync(default)).Status);
    }

    [Fact]
    public async Task ReservedInstallerCannotPretendToPerformAnUpgrade()
    {
        IOnlineUpgradeInstaller installer = new ReservedOnlineUpgradeInstaller();
        Assert.False(installer.IsAvailable);
        await Assert.ThrowsAsync<NotSupportedException>(() => installer.InstallAsync(Parse(Release()).Release!, default));
    }

    [Theory]
    [InlineData(199, "UpdateAvailable")][InlineData(200, "UpToDate")][InlineData(201, "UpToDate")]
    public void TestDiscoveryUsesSignedBuildSequenceAndPreservesStableOnlyChecks(long sequence, string status)
    {
        var release = SignedTestRelease();
        var bytes = Encoding.UTF8.GetBytes(release.ToJsonString());
        Assert.Equal(UpdateCheckStatus.InvalidResponse, GitHubReleaseUpdateService.Parse(bytes, "99.0.0", "win-x64", OtaTestData.PublicKey).Status);
        var result = GitHubReleaseUpdateService.ParseReleases(Encoding.UTF8.GetBytes(new JsonArray(release).ToJsonString()),
            "99.0.0", "win-x64", OtaTestData.PublicKey, sequence);
        Assert.Equal(status, result.Status.ToString());
        if (status == "UpdateAvailable") Assert.Equal(200, result.Release!.Ota!.Payload.BuildSequence);
    }

    [Fact]
    public void TestListSelectionIsIndependentOfApiOrderAndNeverSelectsDraftsOrOldBuilds()
    {
        var current = SignedTestRelease();
        var newer = SignedTestRelease(201);
        var draft = Release("100.0.0"); draft["draft"] = true;
        foreach (var releases in new[] { new JsonArray(current.DeepClone(), newer.DeepClone(), draft.DeepClone()),
            new JsonArray(draft.DeepClone(), newer.DeepClone(), current.DeepClone()) })
        {
            var result = GitHubReleaseUpdateService.ParseReleases(Encoding.UTF8.GetBytes(releases.ToJsonString()), "99.0.0", "win-x64", OtaTestData.PublicKey, 200);
            Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
            Assert.Equal(201, result.Release!.Ota!.Payload.BuildSequence);
        }
        Assert.Equal(UpdateCheckStatus.NoPublishedRelease,
            GitHubReleaseUpdateService.ParseReleases("[]"u8.ToArray(), "99.0.0", "win-x64").Status);
    }

    [Fact]
    public async Task IncludingTestsUsesTheBoundedReleaseListWithoutCredentials()
    {
        using var client = Client((request, _) =>
        {
            Assert.Equal(ProductReleaseSource.ReleasesApi, request.RequestUri);
            Assert.Null(request.Headers.Authorization);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new JsonArray(Release()).ToJsonString()) });
        });
        Assert.Equal(UpdateCheckStatus.UpdateAvailable, (await new GitHubReleaseUpdateService(client, "1.0.4", "win-x64", includeTestReleases: true).CheckAsync(default)).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceCodedInstallersRequireTheMatchingSignedCommitAndAnUnambiguousAsset(bool test)
    {
        var payload = test ? OtaTestData.TestPayload() : OtaTestData.Payload();
        payload = payload with { Name = payload.Name.Replace(".exe", "-aaaaaaaa.exe", StringComparison.Ordinal) };
        var root = SignedRelease(payload);
        var result = GitHubReleaseUpdateService.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()), "1.0.4", "win-x64",
            OtaTestData.PublicKey, allowTestPackages: test);
        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Equal(payload, result.Release!.Ota!.Payload);
        Assert.EndsWith("-Setup-aaaaaaaa.exe", result.Release.Installer!.DownloadUri.AbsoluteUri);

        var wrongCode = SignedRelease(payload with { Name = payload.Name.Replace("aaaaaaaa.exe", "bbbbbbbb.exe", StringComparison.Ordinal) });
        Assert.Equal(UpdateCheckStatus.InvalidResponse, GitHubReleaseUpdateService.Parse(
            Encoding.UTF8.GetBytes(wrongCode.ToJsonString()), "1.0.4", "win-x64", OtaTestData.PublicKey, allowTestPackages: test).Status);
        root.Remove("body");
        Assert.Equal(UpdateCheckStatus.InvalidResponse, GitHubReleaseUpdateService.Parse(
            Encoding.UTF8.GetBytes(root.ToJsonString()), "1.0.4", "win-x64", OtaTestData.PublicKey, allowTestPackages: test).Status);

        root = SignedRelease(payload);
        var legacy = SignedRelease(test ? OtaTestData.TestPayload() : OtaTestData.Payload());
        ((JsonArray)root["assets"]!).Add(legacy["assets"]![0]!.DeepClone());
        Assert.Equal(UpdateCheckStatus.InvalidResponse, GitHubReleaseUpdateService.Parse(
            Encoding.UTF8.GetBytes(root.ToJsonString()), "1.0.4", "win-x64", OtaTestData.PublicKey, allowTestPackages: test).Status);
    }

    private static JsonObject SignedTestRelease(long sequence = 200) => SignedRelease(OtaTestData.TestPayload(sequence));

    private static JsonObject SignedRelease(OtaPayload payload)
    {
        var update = OtaTestData.Update(payload);
        var root = Release("99.0.0");
        root["tag_name"] = update.Payload.Tag; root["prerelease"] = payload.Format == 2;
        root["html_url"] = $"https://github.com/{ProductReleaseSource.Repository}/releases/tag/{update.Payload.Tag}";
        root["body"] = OtaTestData.Body(update);
        root["assets"]![0]!["name"] = update.Payload.Name;
        root["assets"]![0]!["size"] = update.Payload.Size;
        root["assets"]![0]!["browser_download_url"] = OtaManifest.DownloadUri(update.Payload).AbsoluteUri;
        root["assets"]![0]!["digest"] = "sha256:" + update.Payload.Sha256;
        return root;
    }

    internal static JsonObject Release(string version = "1.0.5", string runtime = "win-x64", bool unsigned = false)
    {
        var name = $"OpticalSystemDesign-{version}-{runtime}{(unsigned ? "-Unsigned" : "")}-Setup.exe";
        return new JsonObject
        {
            ["tag_name"] = $"v{version}",
            ["draft"] = false,
            ["prerelease"] = false,
            ["html_url"] = $"https://github.com/{ProductReleaseSource.Repository}/releases/tag/v{version}",
            ["assets"] = new JsonArray(new JsonObject
            {
                ["name"] = name,
                ["state"] = "uploaded",
                ["size"] = 123456,
                ["browser_download_url"] = $"https://github.com/{ProductReleaseSource.Repository}/releases/download/v{version}/{name}"
            })
        };
    }

    private static UpdateCheckResult Parse(JsonObject json, string current = "1.0.4", string? runtime = "win-x64") =>
        GitHubReleaseUpdateService.Parse(Encoding.UTF8.GetBytes(json.ToJsonString()), current, runtime);

    private static HttpClient Client(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) => new(new Handler(send));
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }

    private sealed class WaitingStream : MemoryStream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
    }
}

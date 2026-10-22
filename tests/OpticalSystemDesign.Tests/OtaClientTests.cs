using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Services.Updates;

namespace OpticalSystemDesign.Tests;

internal static class OtaTestData
{
    private static readonly RSA Key = RSA.Create(4096);
    internal static string PublicKey { get { lock (Key) return Key.ExportSubjectPublicKeyInfoPem(); } }
    internal static byte[] InstallerBytes => [0x4d, 0x5a, 1, 2, 3];
    internal static OtaPayload Payload(string runtime = "win-x64") => new(1, WindowsInstallationLifetime.ProductId,
        ProductReleaseSource.Repository, "99.0.0", runtime, $"OpticalSystemDesign-99.0.0-{runtime}-Unsigned-Setup.exe",
        InstallerBytes.Length, Convert.ToHexString(SHA256.HashData(InstallerBytes)).ToLowerInvariant(), "v99.0.0",
        $"99.0.0-{runtime}-0123456789ab", new string('a', 40));
    internal static string SignRaw(byte[] bytes)
    {
        lock (Key) return Convert.ToBase64String(bytes) + "." + Convert.ToBase64String(Key.SignData(bytes,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pss));
    }
    internal static SignedOtaUpdate Update(OtaPayload? payload = null)
    {
        payload ??= Payload();
        return new(SignRaw(JsonSerializer.SerializeToUtf8Bytes(payload)), payload);
    }
    internal static string Body(SignedOtaUpdate update) => OtaManifest.Marker + update.Envelope + "\n-->";
}

public sealed class OtaClientTests
{
    [Fact]
    public void DevelopmentInstallationHasAnUnavailableReasonRatherThanThrowing() =>
        Assert.False(string.IsNullOrEmpty(WindowsOtaHandoff.UnavailableReason()));

    [Fact]
    public void EmbeddedTrustKeyIsTheConfiguredPublicKeyOnly()
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(OtaManifest.PublicKey);
        Assert.Equal(4096, rsa.KeySize);
        Assert.Equal("1d360b38844b1ba09b91786eea7c2c86ce1d6ebf900359174483ada5b613b521",
            Convert.ToHexString(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo())).ToLowerInvariant());
        Assert.DoesNotContain("PRIVATE", OtaManifest.PublicKey);
    }

    [Fact]
    public void SignedReleaseDiscoveryBindsEveryInstallerAndUsesThePinnedKeyByDefault()
    {
        var update = OtaTestData.Update();
        var root = ReleaseUpdateTests.Release("99.0.0");
        root["body"] = OtaTestData.Body(update);
        root["assets"]![0]!["size"] = update.Payload.Size;
        root["assets"]![0]!["name"] = update.Payload.Name;
        root["assets"]![0]!["browser_download_url"] = OtaManifest.DownloadUri(update.Payload).AbsoluteUri;
        root["assets"]![0]!["digest"] = "sha256:" + update.Payload.Sha256;
        var bytes = Encoding.UTF8.GetBytes(root.ToJsonString());
        var result = GitHubReleaseUpdateService.Parse(bytes, "1.0.4", "win-x64", OtaTestData.PublicKey);
        Assert.Equal(update, result.Release!.Ota);
        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Null(GitHubReleaseUpdateService.Parse(bytes, "1.0.4", "win-x64").Release!.Ota);
        root["assets"]![0]!["digest"] = "sha256:" + new string('0', 64);
        Assert.Null(GitHubReleaseUpdateService.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()), "1.0.4", "win-x64", OtaTestData.PublicKey).Release!.Ota);
    }

    [Theory]
    [InlineData("product")][InlineData("repository")][InlineData("format")][InlineData("prerelease")]
    [InlineData("metadata")][InlineData("overflowVersion")][InlineData("runtime")][InlineData("traversal")]
    [InlineData("testExe")][InlineData("empty")][InlineData("oversized")][InlineData("hash")]
    [InlineData("releaseId")][InlineData("commit")][InlineData("tag")]
    public void EvenAValidSignatureCannotAuthorizeAnUnsafePayload(string change)
    {
        var p = OtaTestData.Payload();
        p = change switch
        {
            "product" => p with { ProductAppId = "other" }, "repository" => p with { Repository = "other/repo" },
            "format" => p with { Format = 2 }, "prerelease" => p with { Version = "99.0.0-test" },
            "metadata" => p with { Version = "99.0.0+build" }, "overflowVersion" => p with { Version = "65535.0.0" },
            "runtime" => p with { Runtime = "linux-x64" }, "traversal" => p with { Name = "../installer.exe" },
            "testExe" => p with { Name = "OpticalSystemDesign-99.0.0-win-x64-UnsignedTest-Setup.exe" },
            "empty" => p with { Size = 0 }, "oversized" => p with { Size = OtaManifest.MaximumInstallerBytes + 1 },
            "hash" => p with { Sha256 = "not-a-hash" }, "releaseId" => p with { ReleaseId = "../other" },
            "commit" => p with { SourceCommit = "unknown" }, _ => p with { Tag = "v98.0.0" }
        };
        Assert.Null(OtaManifest.Verify(OtaTestData.Update(p).Envelope, OtaTestData.PublicKey));
    }

    [Fact]
    public void TamperedBytesWrongKeysAndAmbiguousJsonAreRejected()
    {
        var update = OtaTestData.Update();
        var parts = update.Envelope.Split('.');
        var bytes = Convert.FromBase64String(parts[0]); bytes[0] ^= 1;
        Assert.Null(OtaManifest.Verify(Convert.ToBase64String(bytes) + "." + parts[1], OtaTestData.PublicKey));
        var signature = Convert.FromBase64String(parts[1]); signature[0] ^= 1;
        Assert.Null(OtaManifest.Verify(parts[0] + "." + Convert.ToBase64String(signature), OtaTestData.PublicKey));
        using var other = RSA.Create(2048);
        Assert.Null(OtaManifest.Verify(update.Envelope, other.ExportSubjectPublicKeyInfoPem()));
        var json = JsonSerializer.Serialize(update.Payload);
        Assert.Null(OtaManifest.Verify(OtaTestData.SignRaw(Encoding.UTF8.GetBytes(json[..^1] + ",\"Format\":1}")), OtaTestData.PublicKey));
        Assert.Null(OtaManifest.Verify("x".PadRight(20000, 'x'), OtaTestData.PublicKey));
    }

    [Fact]
    public void ReleaseBodyAcceptsTwoArchitecturesButRejectsDuplicateOrDamagedMarkers()
    {
        var update = OtaTestData.Update(); var second = OtaTestData.Update(OtaTestData.Payload("win-arm64"));
        var installer = new ReleaseInstaller(update.Payload.Name, OtaManifest.DownloadUri(update.Payload), update.Payload.Size);
        SignedOtaUpdate? Read(string body) => OtaManifest.FromReleaseBody(body, "99.0.0", "win-x64", installer, null, OtaTestData.PublicKey);
        Assert.Equal(update, Read(OtaTestData.Body(update) + "\n" + OtaTestData.Body(second)));
        Assert.Equal(update, Read(OtaTestData.Body(update).Replace("\n", "\r\n")));
        Assert.Null(Read(OtaTestData.Body(update) + "\n" + OtaTestData.Body(update)));
        Assert.Null(Read(OtaTestData.Body(update) + OtaManifest.Marker + "invalid\n-->"));
    }

    [Theory]
    [InlineData("99.0.0", false)][InlineData("100.0.0", false)][InlineData("98.9.9", true)][InlineData("invalid", false)]
    public void SameVersionAndDowngradesCannotUseOta(string installed, bool expected) =>
        Assert.Equal(expected, OtaManifest.IsNewer(OtaTestData.Payload(), installed));

    [Fact]
    public async Task DownloadIsHashedAndScopedCleanupPreservesUnownedFiles()
    {
        using var directory = new CacheFixture();
        using var handler = new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(OtaTestData.InstallerBytes) });
        using var client = new HttpClient(handler);
        var downloader = new OtaDownloader(client, directory.Path, OtaTestData.PublicKey);
        var prepared = await downloader.DownloadAsync(OtaTestData.Update(), "1.0.4", new Progress<double>(), CancellationToken.None);
        Assert.Equal(OtaTestData.InstallerBytes, await File.ReadAllBytesAsync(prepared.InstallerPath));
        File.WriteAllText(Path.Combine(prepared.Directory, "unowned.txt"), "keep");
        prepared.Dispose();
        Assert.False(File.Exists(prepared.InstallerPath));
        Assert.True(File.Exists(Path.Combine(prepared.Directory, "unowned.txt")));
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("hash")][InlineData("truncated")][InlineData("oversized")][InlineData("notExe")]
    public async Task InvalidPayloadBytesNeverBecomeAnInstallerAndAreCleaned(string condition)
    {
        using var directory = new CacheFixture();
        var bytes = condition switch { "truncated" => new byte[4], "oversized" => new byte[6], _ => new byte[5] };
        var update = OtaTestData.Update();
        if (condition == "notExe") update = OtaTestData.Update(update.Payload with { Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() });
        using var handler = new Handler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            response.Content.Headers.ContentLength = 5;
            return response;
        });
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => new OtaDownloader(client, directory.Path, OtaTestData.PublicKey)
            .DownloadAsync(update, "1.0.4", new Progress<double>(), CancellationToken.None));
        Assert.Empty(Directory.GetDirectories(directory.Path));
    }

    [Theory]
    [InlineData("http://github.com/file")][InlineData("https://example.invalid/file")]
    [InlineData("https://github.com.evil.invalid/file")][InlineData("https://name@github.com/file")]
    [InlineData("https://github.com:444/file")]
    public async Task UntrustedRedirectsStopBeforeAnySecondRequest(string location)
    {
        using var directory = new CacheFixture();
        using var handler = new Handler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Redirect);
            response.Headers.Location = new Uri(location); return response;
        });
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => new OtaDownloader(client, directory.Path, OtaTestData.PublicKey)
            .DownloadAsync(OtaTestData.Update(), "1.0.4", new Progress<double>(), CancellationToken.None));
        Assert.Equal(1, handler.Calls);
        Assert.Empty(Directory.GetDirectories(directory.Path));
    }

    [Fact]
    public async Task OfficialCdnRedirectWorksWithoutCredentials()
    {
        using var directory = new CacheFixture();
        var calls = 0;
        using var handler = new Handler(request =>
        {
            Assert.Null(request.Headers.Authorization);
            if (++calls == 1)
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Redirect);
                redirect.Headers.Location = new("https://release-assets.githubusercontent.com/fixture?token=not-a-real-token"); return redirect;
            }
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(OtaTestData.InstallerBytes) };
        });
        using var client = new HttpClient(handler);
        using var prepared = await new OtaDownloader(client, directory.Path, OtaTestData.PublicKey)
            .DownloadAsync(OtaTestData.Update(), "1.0.4", new Progress<double>(), CancellationToken.None);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task CancellationAndReplayDoNotLeavePartialFiles()
    {
        using var directory = new CacheFixture();
        using var handler = new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(OtaTestData.InstallerBytes) });
        using var client = new HttpClient(handler);
        var downloader = new OtaDownloader(client, directory.Path, OtaTestData.PublicKey);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => downloader.DownloadAsync(OtaTestData.Update(), "1.0.4", new Progress<double>(), cancelled.Token));
        await Assert.ThrowsAsync<InvalidDataException>(() => downloader.DownloadAsync(OtaTestData.Update(), "99.0.0", new Progress<double>(), CancellationToken.None));
        Assert.Equal(0, handler.Calls);
        Assert.Empty(Directory.GetDirectories(directory.Path));
    }

    [Theory]
    [InlineData("nonce")][InlineData("expired")][InlineData("future")][InlineData("parent")]
    [InlineData("startTime")][InlineData("runtime")][InlineData("replay")]
    public void HandoffRejectsExpiredMismatchedAndReplayedTickets(string change)
    {
        var now = DateTime.UtcNow; var nonce = new string('A', 64);
        var ticket = new OtaHandoffTicket(OtaTestData.Update().Envelope, nonce, 123, 123456, now);
        ticket = change switch
        {
            "nonce" => ticket with { Nonce = new string('B', 64) }, "expired" => ticket with { CreatedUtc = now.AddMinutes(-11) },
            "future" => ticket with { CreatedUtc = now.AddSeconds(1) }, "parent" => ticket with { ParentId = 0 },
            "startTime" => ticket with { ParentStartUtcTicks = 0 }, _ => ticket
        };
        Assert.Throws<InvalidDataException>(() => WindowsOtaHandoff.ValidateTicket(ticket, nonce, now,
            change == "runtime" ? "win-arm64" : "win-x64", change == "replay" ? "99.0.0" : "1.0.4", OtaTestData.PublicKey));
    }

    [Fact]
    public void HandoffUsesOnlyOwnedCacheAndNonDestructiveInstallerFlags()
    {
        var nonce = new string('A', 64); var now = DateTime.UtcNow; var update = OtaTestData.Update();
        Assert.Equal(update, WindowsOtaHandoff.ValidateTicket(new(update.Envelope, nonce, 123, 123456, now), nonce,
            now, "win-x64", "1.0.4", OtaTestData.PublicKey));
        Assert.Throws<InvalidDataException>(() => WindowsOtaHandoff.ValidateCacheDirectory(Path.GetTempPath()));
        var args = WindowsOtaHandoff.SetupArguments(Path.GetTempPath());
        Assert.Contains("/NORESTART", args); Assert.Contains("/NOCLOSEAPPLICATIONS", args);
        Assert.DoesNotContain(args, arg => arg.StartsWith("/DIR", StringComparison.Ordinal) || arg.StartsWith("/GROUP", StringComparison.Ordinal) || arg.Contains("FORCE", StringComparison.Ordinal));
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(response(request)); }
    }
    private sealed class CacheFixture : IDisposable
    {
        internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "optical-ota-test-" + Guid.NewGuid().ToString("N"));
        internal CacheFixture() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, true);
    }
}

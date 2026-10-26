using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace OpticalSystemDesign.App.Services.Updates;

internal enum UpdateCheckStatus
{
    UpdateAvailable, UpToDate, NoPublishedRelease, NoCompatibleInstaller, UnsupportedPlatform,
    InvalidCurrentVersion, InvalidResponse, NetworkError, TimedOut, RateLimited
}

internal sealed record ReleaseInstaller(string Name, Uri DownloadUri, long Size);
internal sealed record AvailableRelease(string Version, Uri PageUri, ReleaseInstaller? Installer, SignedOtaUpdate? Ota = null);
internal sealed record UpdateCheckResult(UpdateCheckStatus Status, AvailableRelease? Release = null);

internal interface IReleaseUpdateService
{
    Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken);
}

internal static class ProductReleaseSource
{
    internal const string Repository = "VendettaMask/opticalsystem";
    internal static Uri ReleasesPage { get; } = new($"https://github.com/{Repository}/releases");
    internal static Uri LatestApi { get; } = new($"https://api.github.com/repos/{Repository}/releases/latest");
    internal static Uri ReleasesApi { get; } = new($"https://api.github.com/repos/{Repository}/releases?per_page=100");
    internal static string? CurrentRuntime => OperatingSystem.IsWindows() ? RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => "win-x64",
        Architecture.Arm64 => "win-arm64",
        _ => null
    } : null;
}

/// <summary>Read-only, user-initiated release discovery. Never downloads or runs an installer.</summary>
internal sealed class GitHubReleaseUpdateService(HttpClient client, string currentVersion, string? runtime, TimeSpan? timeout = null,
    bool includeTestReleases = false, long installedBuildSequence = 0) : IReleaseUpdateService
{
    internal const int MaximumResponseBytes = 1024 * 1024;
    internal bool IncludeTestReleases { get; set; } = includeTestReleases;

    internal static HttpClient CreateClient() => new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        if (!ReleaseVersion.TryParse(currentVersion, out _)) return new(UpdateCheckStatus.InvalidCurrentVersion);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout ?? TimeSpan.FromSeconds(20));
        try
        {
            var includeTests = IncludeTestReleases;
            using var request = new HttpRequestMessage(HttpMethod.Get, includeTests ? ProductReleaseSource.ReleasesApi : ProductReleaseSource.LatestApi);
            request.Headers.UserAgent.ParseAdd("OpticalSystemDesign-UpdateCheck/1.0");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound) return new(UpdateCheckStatus.NoPublishedRelease);
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests) return new(UpdateCheckStatus.RateLimited);
            if (!response.IsSuccessStatusCode) return new(UpdateCheckStatus.NetworkError);
            if (response.Content.Headers.ContentLength > MaximumResponseBytes) return new(UpdateCheckStatus.InvalidResponse);
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            using var body = new MemoryStream();
            var buffer = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(buffer, deadline.Token).ConfigureAwait(false)) > 0)
            {
                if (body.Length + count > MaximumResponseBytes) return new(UpdateCheckStatus.InvalidResponse);
                body.Write(buffer, 0, count);
            }
            return includeTests ? ParseReleases(body.ToArray(), currentVersion, runtime, installedBuildSequence: installedBuildSequence)
                : Parse(body.ToArray(), currentVersion, runtime);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return new(UpdateCheckStatus.TimedOut); }
        catch (HttpRequestException) { return new(UpdateCheckStatus.NetworkError); }
        catch (IOException) { return new(UpdateCheckStatus.NetworkError); }
    }

    internal static UpdateCheckResult ParseReleases(ReadOnlyMemory<byte> json, string currentVersion, string? runtime,
        string? otaPublicKey = null, long installedBuildSequence = 0)
    {
        if (!ReleaseVersion.TryParse(currentVersion, out _)) return new(UpdateCheckStatus.InvalidCurrentVersion);
        if (json.Length > MaximumResponseBytes) return new(UpdateCheckStatus.InvalidResponse);
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() > 100)
                return new(UpdateCheckStatus.InvalidResponse);
            var results = new List<UpdateCheckResult>();
            var invalid = false;
            foreach (var release in document.RootElement.EnumerateArray())
            {
                if (release.ValueKind == JsonValueKind.Object && release.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) continue;
                var result = Parse(System.Text.Encoding.UTF8.GetBytes(release.GetRawText()), currentVersion, runtime,
                    otaPublicKey, allowTestPackages: true, installedBuildSequence);
                invalid |= result.Status == UpdateCheckStatus.InvalidResponse;
                if (result.Release is not null) results.Add(result);
            }
            var candidates = results.Where(result => result.Status != UpdateCheckStatus.UpToDate).ToArray();
            if (candidates.Length == 0) candidates = results.ToArray();
            return candidates.OrderByDescending(result =>
                {
                    var numeric = result.Release!.Version.Split("-test.", StringSplitOptions.None)[0];
                    return ReleaseVersion.TryParse(numeric, out var version) ? version : null;
                })
                .ThenBy(result => result.Release!.Version.Contains("-test.", StringComparison.Ordinal))
                .ThenByDescending(result => result.Release!.Ota?.Payload.BuildSequence ?? 0)
                .ThenByDescending(result => result.Release!.Version, StringComparer.Ordinal)
                .FirstOrDefault() ?? new(invalid ? UpdateCheckStatus.InvalidResponse : UpdateCheckStatus.NoPublishedRelease);
        }
        catch (JsonException) { return new(UpdateCheckStatus.InvalidResponse); }
    }

    internal static UpdateCheckResult Parse(ReadOnlyMemory<byte> json, string currentVersion, string? runtime, string? otaPublicKey = null,
        bool allowTestPackages = false, long installedBuildSequence = 0)
    {
        if (!ReleaseVersion.TryParse(currentVersion, out var installed)) return new(UpdateCheckStatus.InvalidCurrentVersion);
        if (json.Length > MaximumResponseBytes) return new(UpdateCheckStatus.InvalidResponse);
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("draft", out var draft) || draft.ValueKind != JsonValueKind.False ||
                !root.TryGetProperty("prerelease", out var prerelease) || prerelease.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return new(UpdateCheckStatus.InvalidResponse);
            var tag = String(root, "tag_name");
            var displayVersion = tag?.StartsWith('v') == true ? tag[1..] : tag;
            var isTest = prerelease.ValueKind == JsonValueKind.True;
            var version = isTest ? displayVersion?.Split("-test.", StringSplitOptions.None)[0] : displayVersion;
            if (isTest && (!allowTestPackages || version is null || !OtaManifest.IsTestTag(tag, version)))
                return new(UpdateCheckStatus.InvalidResponse);
            if (!ReleaseVersion.TryParse(version, out var latest) || latest.Prerelease is not null ||
                !IsExpectedUrl(String(root, "html_url"), $"releases/tag/{Uri.EscapeDataString(tag!)}", out var page))
                return new(UpdateCheckStatus.InvalidResponse);
            if (latest.CompareTo(installed) < 0 || latest.CompareTo(installed) == 0 && !isTest)
                return new(UpdateCheckStatus.UpToDate, new(displayVersion!, page!, null));
            if (runtime is not ("win-x64" or "win-arm64"))
                return new(UpdateCheckStatus.UnsupportedPlatform, new(displayVersion!, page!, null));
            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
                return new(UpdateCheckStatus.InvalidResponse);
            var matches = new List<ReleaseInstaller>();
            string? digest = null;
            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.ValueKind != JsonValueKind.Object) continue;
                var name = String(asset, "name");
                if (name is null || !OtaManifest.IsInstallerName(name, version!, runtime, isTest)) continue;
                if (String(asset, "state") != "uploaded" ||
                    !asset.TryGetProperty("size", out var size) || !size.TryGetInt64Safe(out var bytes) || bytes <= 0 ||
                    !IsExpectedUrl(String(asset, "browser_download_url"),
                        $"releases/download/{Uri.EscapeDataString(tag!)}/{Uri.EscapeDataString(name)}", out var download))
                    return new(UpdateCheckStatus.InvalidResponse);
                matches.Add(new(name, download!, bytes));
                digest = String(asset, "digest");
            }
            // Exactly one installer per architecture/channel, including legacy names.
            if (matches.Count > 1) return new(UpdateCheckStatus.InvalidResponse);
            if (matches.Count == 0) return new(UpdateCheckStatus.NoCompatibleInstaller, new(displayVersion!, page!, null));
            var ota = OtaManifest.FromReleaseBody(String(root, "body"), version!, runtime, matches[0], digest, otaPublicKey);
            if (!matches[0].Name.EndsWith("-Setup.exe", StringComparison.Ordinal) && ota is null)
                return new(UpdateCheckStatus.InvalidResponse); // Bind a displayed source code to the signed full commit.
            if (isTest && latest.CompareTo(installed) == 0 &&
                (ota is null || !OtaManifest.IsNewer(ota.Payload, currentVersion, installedBuildSequence)))
                return ota is null ? new(UpdateCheckStatus.InvalidResponse)
                    : new(UpdateCheckStatus.UpToDate, new(displayVersion!, page!, null));
            return new(UpdateCheckStatus.UpdateAvailable, new(displayVersion!, page!, matches[0], ota));
        }
        catch (JsonException) { return new(UpdateCheckStatus.InvalidResponse); }
    }

    private static string? String(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool IsExpectedUrl(string? value, string path, out Uri? uri)
    {
        var expected = $"https://github.com/{ProductReleaseSource.Repository}/{path}";
        return Uri.TryCreate(value, UriKind.Absolute, out uri) &&
            string.Equals(value, expected, StringComparison.Ordinal) && uri.UserInfo.Length == 0;
    }
}

internal static class ReleaseJsonExtensions
{
    internal static bool TryGetInt64Safe(this JsonElement value, out long number)
    {
        number = 0;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out number);
    }
}

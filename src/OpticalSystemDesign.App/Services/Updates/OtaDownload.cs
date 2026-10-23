using System.Net;
using System.Net.Http;
using System.Security.Cryptography;

namespace OpticalSystemDesign.App.Services.Updates;

internal sealed class PreparedOtaUpdate(string directory, SignedOtaUpdate update) : IDisposable
{
    internal string Directory { get; } = directory;
    internal SignedOtaUpdate Update { get; } = update;
    internal string InstallerPath => Path.Combine(Directory, Update.Payload.Name);
    internal bool HandedOff { get; set; }
    public void Dispose()
    {
        if (HandedOff) return;
        // Only exact files owned by this operation; never recurse through user directories.
        foreach (var name in new[] { Update.Payload.Name, Update.Payload.Name + ".partial", "handoff.json" })
        {
            try { File.Delete(Path.Combine(Directory, name)); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        try { System.IO.Directory.Delete(Directory); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

internal interface IOtaDownloader
{
    Task<PreparedOtaUpdate> DownloadAsync(SignedOtaUpdate update, string installedVersion,
        IProgress<double> progress, CancellationToken cancellationToken);
}

internal sealed class OtaDownloader(HttpClient client, string? cacheRoot = null, string? publicKey = null, long? installedBuildSequence = null) : IOtaDownloader
{
    internal static string CacheRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Optical System Design", "Updates");

    internal static HttpClient CreateClient() => new(new HttpClientHandler { AllowAutoRedirect = false })
    { Timeout = Timeout.InfiniteTimeSpan };

    public async Task<PreparedOtaUpdate> DownloadAsync(SignedOtaUpdate update, string installedVersion,
        IProgress<double> progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var verified = OtaManifest.Verify(update.Envelope, publicKey);
        if (verified is null || verified.Payload != update.Payload || !OtaManifest.IsNewer(verified.Payload, installedVersion,
            installedBuildSequence ?? ProductVersionInfo.OtaBuildSequence))
            throw new InvalidDataException("Invalid update signature or version.");
        var directory = Path.Combine(cacheRoot ?? CacheRoot, Guid.NewGuid().ToString("N"));
        if (OperatingSystem.IsWindows()) WindowsInstallationRecovery.CheckPath(directory);
        Directory.CreateDirectory(directory);
        var prepared = new PreparedOtaUpdate(directory, verified);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromMinutes(30));
            var uri = OtaManifest.DownloadUri(verified.Payload);
            for (var hop = 0; hop <= 5; hop++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.UserAgent.ParseAdd("OpticalSystemDesign-OTA/1.0");
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
                if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.RedirectMethod
                    or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
                {
                    var location = response.Headers.Location;
                    if (location is null || hop == 5) throw new InvalidDataException("Invalid update redirect.");
                    uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                    if (!IsAllowedDownloadHost(uri)) throw new InvalidDataException("Untrusted update redirect.");
                    continue;
                }
                if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentLength is { } length && length != verified.Payload.Size)
                    throw new InvalidDataException("Incomplete update response.");
                var partial = prepared.InstallerPath + ".partial";
                await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                await using (var input = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false))
                using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                {
                    var buffer = new byte[81920];
                    long total = 0;
                    while (true)
                    {
                        using var idle = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
                        idle.CancelAfter(TimeSpan.FromSeconds(45));
                        var count = await input.ReadAsync(buffer, idle.Token).ConfigureAwait(false);
                        if (count == 0) break;
                        total += count;
                        if (total > verified.Payload.Size) throw new InvalidDataException("Update exceeded signed size.");
                        hash.AppendData(buffer, 0, count);
                        await output.WriteAsync(buffer.AsMemory(0, count), deadline.Token).ConfigureAwait(false);
                        progress.Report(100.0 * total / verified.Payload.Size);
                    }
                    if (total != verified.Payload.Size || Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant() != verified.Payload.Sha256)
                        throw new InvalidDataException("Update hash verification failed.");
                }
                using (var executable = File.OpenRead(partial))
                    if (executable.ReadByte() != 'M' || executable.ReadByte() != 'Z') throw new InvalidDataException("Not a Windows installer.");
                File.Move(partial, prepared.InstallerPath);
                if (OperatingSystem.IsWindows())
                    await File.WriteAllTextAsync(prepared.InstallerPath + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n", deadline.Token).ConfigureAwait(false);
                return prepared;
            }
            throw new InvalidDataException("Too many update redirects.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { prepared.Dispose(); throw new TimeoutException("Update download timed out."); }
        catch { prepared.Dispose(); throw; }
    }

    internal static bool IsAllowedDownloadHost(Uri uri) => uri.Scheme == Uri.UriSchemeHttps && uri.Port == 443 &&
        uri.UserInfo.Length == 0 && uri.Fragment.Length == 0 && uri.Host is
            "github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com" or "github-releases.githubusercontent.com";
}

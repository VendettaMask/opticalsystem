using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OpticalSystemDesign.App.Services.Updates;

internal sealed record OtaPayload(int Format, string ProductAppId, string Repository, string Version,
    string Runtime, string Name, long Size, string Sha256, string Tag, string ReleaseId, string SourceCommit);

// Keep the original signed bytes: reserializing JSON would change the signed message.
internal sealed record SignedOtaUpdate(string Envelope, OtaPayload Payload);

internal static class OtaManifest
{
    internal const string Marker = "<!-- optical-system-design-ota-v1\n";
    internal const long MaximumInstallerBytes = 2L * 1024 * 1024 * 1024;

    internal static string PublicKey
    {
        get
        {
            using var stream = typeof(OtaManifest).Assembly.GetManifestResourceStream("OpticalSystemDesign.App.Updates.PublicKey")
                ?? throw new InvalidDataException("The OTA trust key is missing.");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }

    internal static SignedOtaUpdate? FromReleaseBody(string? body, string version, string runtime,
        ReleaseInstaller installer, string? digest, string? publicKey = null)
    {
        if (body is null || body.Length > 65536) return null;
        body = body.Replace("\r\n", "\n", StringComparison.Ordinal);
        var matches = Regex.Matches(body, Regex.Escape(Marker) + @"([A-Za-z0-9+/=]+\.[A-Za-z0-9+/=]+)\n-->", RegexOptions.CultureInvariant);
        if (matches.Count is 0 or > 4 || Regex.Matches(body, Regex.Escape(Marker)).Count != matches.Count) return null;
        SignedOtaUpdate? result = null;
        foreach (Match match in matches)
        {
            var signed = Verify(match.Groups[1].Value, publicKey);
            if (signed is null) return null; // Do not silently ignore damaged signatures.
            var payload = signed.Payload;
            if (payload.Runtime != runtime) continue;
            if (result is not null || payload.Version != version || payload.Name != installer.Name || payload.Size != installer.Size ||
                installer.DownloadUri.AbsoluteUri != DownloadUri(payload).AbsoluteUri ||
                digest is not null && digest != "sha256:" + payload.Sha256) return null;
            result = signed;
        }
        return result;
    }

    internal static SignedOtaUpdate? Verify(string envelope, string? publicKey = null)
    {
        if (envelope.Length is < 100 or > 16384) return null;
        try
        {
            var parts = envelope.Split('.');
            if (parts.Length != 2) return null;
            var bytes = Convert.FromBase64String(parts[0]);
            var signature = Convert.FromBase64String(parts[1]);
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKey ?? PublicKey);
            if (rsa.KeySize != 4096 || signature.Length != 512 || bytes.Length > 8192 ||
                !rsa.VerifyData(bytes, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss)) return null;
            using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 4 });
            if (json.RootElement.ValueKind != JsonValueKind.Object ||
                json.RootElement.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != 11 ||
                json.RootElement.EnumerateObject().Count() != 11) return null;
            var payload = JsonSerializer.Deserialize<OtaPayload>(bytes);
            return payload is not null && IsValid(payload) ? new(envelope, payload) : null;
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException or JsonException or ArgumentException)
        { return null; }
    }

    internal static bool IsValid(OtaPayload p) => p.Format == 1 &&
        p.ProductAppId == WindowsInstallationLifetime.ProductId && p.Repository == ProductReleaseSource.Repository &&
        ReleaseVersion.TryParse(p.Version, out var version) && version.Prerelease is null &&
        p.Version == $"{version.Major}.{version.Minor}.{version.Patch}" && version.Major <= 65534 && version.Minor <= 65534 && version.Patch <= 65534 &&
        p.Runtime is "win-x64" or "win-arm64" && p.Size is > 2 and <= MaximumInstallerBytes &&
        p.Tag == "v" + p.Version &&
        (p.Name == $"OpticalSystemDesign-{p.Version}-{p.Runtime}-Setup.exe" ||
         p.Name == $"OpticalSystemDesign-{p.Version}-{p.Runtime}-Unsigned-Setup.exe") &&
        Regex.IsMatch(p.Sha256 ?? "", "^[0-9a-f]{64}$", RegexOptions.CultureInvariant) &&
        Regex.IsMatch(p.SourceCommit ?? "", "^[0-9a-f]{40}$", RegexOptions.CultureInvariant) &&
        Regex.IsMatch(p.ReleaseId ?? "", "^" + Regex.Escape(p.Version + "-" + p.Runtime + "-") + "[0-9a-f]{12}$", RegexOptions.CultureInvariant);

    internal static Uri DownloadUri(OtaPayload p) => new($"https://github.com/{ProductReleaseSource.Repository}/releases/download/{p.Tag}/{p.Name}");

    internal static bool IsNewer(OtaPayload payload, string installedVersion) =>
        ReleaseVersion.TryParse(installedVersion, out var installed) &&
        ReleaseVersion.TryParse(payload.Version, out var target) && target.CompareTo(installed) > 0;
}

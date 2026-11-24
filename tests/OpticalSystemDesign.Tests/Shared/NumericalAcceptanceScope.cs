using System.Security.Cryptography;
using System.Text.Json;

namespace OpticalSystemDesign.Tests;

// Selects archived data rows by exact source bytes, never by measured error.
internal static class NumericalAcceptanceScope
{
    private static readonly Lazy<HashSet<string>> RetiredSources = new(() =>
    {
        using var policy = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "Fixtures", "acceptance-policy.json")));
        return policy.RootElement.GetProperty("retiredCaptureSources").EnumerateArray()
            .Select(entry => entry.GetProperty("sha256").GetString()!).ToHashSet(StringComparer.Ordinal);
    });

    internal static bool IsRetired(IReadOnlyDictionary<string, byte[]> files, string id) =>
        RetiredSources.Value.Contains(Convert.ToHexStringLower(SHA256.HashData(files[id + "/source.zmx"])));
}

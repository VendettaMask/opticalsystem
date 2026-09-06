using System.Security.Cryptography;
using System.Text.Json;

namespace OptilandWorkbench.Tests;

public sealed class FrozenHistoryIntegrityTests
{
    [Fact]
    public void HistoricalReferencesAndTranscribedInputsMatchFrozenHashes()
    {
        var history = Path.Combine(ProductDependencyArchitectureTests.RepositoryRoot(), "validation", "history", "optiland-0.5.8");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(history, "manifest.json")));
        var files = manifest.RootElement.GetProperty("files").EnumerateArray().ToArray();
        Assert.NotEmpty(files);
        foreach (var item in files)
        {
            var path = Path.GetFullPath(Path.Combine(history, item.GetProperty("path").GetString()!));
            Assert.StartsWith(history + Path.DirectorySeparatorChar, path, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(item.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));
        }
    }
}

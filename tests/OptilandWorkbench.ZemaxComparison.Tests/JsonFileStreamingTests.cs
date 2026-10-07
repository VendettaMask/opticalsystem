using System.Text;
using System.Text.Json;
using OptilandWorkbench.ZemaxComparison;

namespace OptilandWorkbench.ZemaxComparison.Tests;

public sealed class JsonFileStreamingTests
{
    [Fact]
    public void StreamedFilesRetainTheExactExistingUtf8SerializationContract()
    {
        InDirectory(root =>
        {
            var value = new { text = "镜头 Δ μm\n", value = double.PositiveInfinity,
                request = new CanonicalAnalysisRequest { CanonicalAnalysisKey = "RMS vs Field", Apodization = "Uniform", WorkbenchSettings = [] } };
            var expected = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, JsonFiles.Options));
            var path = Path.Combine(root, "data.json");
            JsonFiles.Write(path, value);
            Assert.Equal(expected, File.ReadAllBytes(path));
            Assert.DoesNotContain("fingerprint", File.ReadAllText(path));
        });
    }

    [Fact]
    public void LazyLargePayloadWritesBeforeEnumerationCompletes()
    {
        InDirectory(root =>
        {
            var path = Path.Combine(root, "data.json");
            var wroteDuringEnumeration = false;
            var chunk = new string('x', 32768);
            IEnumerable<object> Values()
            {
                for (var i = 0; i < 1024; i++)
                {
                    if (i == 512)
                        wroteDuringEnumeration = new FileInfo(Assert.Single(Directory.EnumerateFiles(root, "*.tmp-*"))).Length > 0;
                    yield return new { index = i, chunk };
                }
            }
            JsonFiles.Write(path, Values());
            Assert.True(wroteDuringEnumeration);
            Assert.True(new FileInfo(path).Length > 32 * 1024 * 1024);
            Assert.Empty(Directory.EnumerateFiles(root, "*.tmp-*"));
        });
    }

    [Fact]
    public void SerializationFailureKeepsExistingFileAndRemovesOnlyOwnedTemporaryFile()
    {
        InDirectory(root =>
        {
            var path = Path.Combine(root, "data.json");
            File.WriteAllText(path, "existing evidence");
            IEnumerable<object> Values()
            {
                yield return new { value = 1 };
                throw new InvalidOperationException("deliberate serialization failure");
            }
            Assert.Throws<InvalidOperationException>(() => JsonFiles.Write(path, Values()));
            Assert.Equal("existing evidence", File.ReadAllText(path));
            Assert.Empty(Directory.EnumerateFiles(root, "*.tmp-*"));
        });
    }

    private static void InDirectory(Action<string> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "zemax-json-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { action(root); }
        finally { Directory.Delete(root, recursive: true); }
    }
}

using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Validation;

namespace OpticalSystemDesign.Tests;

public sealed class EvidenceStorageTests
{
    [Fact]
    public void LogicalCaptureRetainsItsPathAndExactBytesWithoutAnotherStoredCopy()
    {
        using var fixture = new StorageFixture();
        Assert.False(File.Exists(fixture.Logical));
        Assert.Equal(fixture.Bytes, EvidenceFiles.ReadAllBytes(fixture.Logical));
        Assert.Equal(fixture.Canonical, EvidenceFiles.Resolve(fixture.Logical));
        Assert.True(EvidenceFiles.DirectoryExists(Path.GetDirectoryName(fixture.Logical)!));
        Assert.Equal(new[] { fixture.Logical }, EvidenceFiles.GetFiles(Path.GetDirectoryName(fixture.Logical)!, "*.bin"));
        Assert.False(File.Exists(fixture.Logical));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChangedOrMissingAuthorityIsRejected(bool missing)
    {
        using var fixture = new StorageFixture();
        if (missing) File.Delete(fixture.Canonical);
        else File.WriteAllBytes(fixture.Canonical, new byte[] { 1, 10, 13, 255, 42 });
        Assert.Throws<InvalidDataException>(() => EvidenceFiles.ReadAllBytes(fixture.Logical));
    }

    [Fact]
    public void StorageAuthorityCannotLeaveTheRepository()
    {
        using var fixture = new StorageFixture("../outside.bin");
        Assert.Throws<InvalidDataException>(() => EvidenceFiles.Resolve(fixture.Logical));
    }

    private sealed class StorageFixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "evidence-storage", Guid.NewGuid().ToString("N"));
        internal byte[] Bytes { get; } = [0, 10, 13, 255, 42];
        internal string Canonical => Path.Combine(_root, "validation", "authority.bin");
        internal string Logical => Path.Combine(_root, "validation", "capture", "original.bin");
        internal StorageFixture(string canonical = "validation/authority.bin")
        {
            Directory.CreateDirectory(Path.Combine(_root, "validation"));
            File.WriteAllText(Path.Combine(_root, "OpticalSystemDesign.slnx"), "<Solution />");
            File.WriteAllBytes(Canonical, Bytes);
            File.WriteAllText(Path.Combine(_root, "validation", "evidence-aliases.json"), JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                groups = new[] { new { canonical, sha256 = Convert.ToHexString(SHA256.HashData(Bytes)).ToLowerInvariant(), bytes = Bytes.Length, aliases = new[] { "validation/capture/original.bin" } } }
            }));
        }
        public void Dispose()
        {
            var allowed = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "evidence-storage")) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(_root).StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unexpected test cleanup path.");
            Directory.Delete(_root, recursive: true);
        }
    }
}

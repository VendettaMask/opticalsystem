using System.Security.Cryptography;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

public sealed class AfocalPolygonDependencyTests
{
    [Theory]
    [InlineData("matching")]
    [InlineData("missing")]
    [InlineData("changed")]
    [InlineData("traversal")]
    public void CaptureRequiresExactFrozenPolygonBytes(string scenario)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Polygon Objects"));
        try
        {
            var bytes = System.Text.Encoding.ASCII.GetBytes("V 1 0 0 0\r\n");
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var path = Path.Combine(root, "Polygon Objects", "test.POB");
            if (scenario != "missing") File.WriteAllBytes(path, bytes);
            if (scenario == "changed") File.AppendAllText(path, "V 2 1 0 0\r\n");
            var name = scenario == "traversal" ? "../test.POB" : "test.POB";
            if (scenario == "matching") Assert.Equal(hash, PolygonDependencyGuard.Verify(root, name, hash));
            else if (scenario == "missing") Assert.Throws<FileNotFoundException>(() => PolygonDependencyGuard.Verify(root, name, hash));
            else Assert.Throws<InvalidDataException>(() => PolygonDependencyGuard.Verify(root, name, hash));
            if (scenario == "matching") Assert.Equal(bytes, File.ReadAllBytes(path));
        }
        finally { Directory.Delete(root, true); }
    }
}

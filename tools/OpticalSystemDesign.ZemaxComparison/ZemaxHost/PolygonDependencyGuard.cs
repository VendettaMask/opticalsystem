// No ZOS-API dependency: the identical guard is exercised by offline tests.
using System;
using System.IO;
using System.Security.Cryptography;

internal static class PolygonDependencyGuard
{
    internal static string Verify(string objectsDirectory, string name, string expectedSha256)
    {
        if (string.IsNullOrWhiteSpace(objectsDirectory) || string.IsNullOrEmpty(name)
            || name.IndexOfAny(new[] { '/', '\\', ':' }) >= 0
            || !name.EndsWith(".POB", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(expectedSha256) || expectedSha256.Length != 64)
            throw new InvalidDataException("Invalid polygon dependency contract.");
        var path = Path.Combine(objectsDirectory, "Polygon Objects", name);
        string actual;
        using (var algorithm = SHA256.Create())
        using (var stream = File.OpenRead(path))
            actual = BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Polygon dependency SHA-256 mismatch: " + name + "; actual=" + actual);
        return actual;
    }
}

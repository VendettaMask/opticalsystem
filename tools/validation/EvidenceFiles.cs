using System.Collections.Concurrent;
using System.IO.Enumeration;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpticalSystemDesign.Validation;

// Validation-only logical storage. This file is linked into tests/tools, never the product.
internal static class EvidenceFiles
{
    private sealed record Alias(string Canonical, string Sha256, long Bytes);
    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, Alias>> Catalogs = new(StringComparer.OrdinalIgnoreCase);

    private static string? Repository(string path)
    {
        for (var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(path))!); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "validation", "evidence-aliases.json")) &&
                File.Exists(Path.Combine(directory.FullName, "OpticalSystemDesign.slnx"))) return directory.FullName;
        return null;
    }

    private static IReadOnlyDictionary<string, Alias> Catalog(string root) => Catalogs.GetOrAdd(root, static repository =>
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(repository, "validation", "evidence-aliases.json")));
        var result = new Dictionary<string, Alias>(StringComparer.Ordinal);
        foreach (var group in document.RootElement.GetProperty("groups").EnumerateArray())
        {
            var entry = new Alias(group.GetProperty("canonical").GetString()!, group.GetProperty("sha256").GetString()!, group.GetProperty("bytes").GetInt64());
            foreach (var alias in group.GetProperty("aliases").EnumerateArray()) result.Add(alias.GetString()!, entry);
        }
        return result;
    });

    internal static string Resolve(string path)
    {
        if (File.Exists(path)) return path;
        var full = Path.GetFullPath(path);
        var root = Repository(full);
        if (root is null || !Catalog(root).TryGetValue(Path.GetRelativePath(root, full).Replace('\\', '/'), out var alias)) return path;
        var canonical = Path.GetFullPath(Path.Combine(root, alias.Canonical));
        if (!canonical.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Evidence authority leaves its repository.");
        for (FileSystemInfo? item = new FileInfo(canonical); item is not null; item = item is FileInfo file ? file.Directory : ((DirectoryInfo)item).Parent)
        {
            if (item.Exists && item.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException("Evidence authority traverses a link.");
            if (string.Equals(item.FullName, root, StringComparison.OrdinalIgnoreCase)) break;
        }
        var info = new FileInfo(canonical);
        if (!info.Exists || info.Length != alias.Bytes) throw new InvalidDataException("Evidence authority length changed.");
        using var stream = File.OpenRead(canonical);
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(stream)), alias.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Evidence authority hash changed.");
        return canonical;
    }

    internal static bool Exists(string path) => File.Exists(Resolve(path));
    internal static string ReadAllText(string path) => File.ReadAllText(Resolve(path));
    internal static string ReadAllText(string path, Encoding encoding) => File.ReadAllText(Resolve(path), encoding);
    internal static byte[] ReadAllBytes(string path) => File.ReadAllBytes(Resolve(path));
    internal static string[] ReadAllLines(string path) => File.ReadAllLines(Resolve(path));
    internal static string[] ReadAllLines(string path, Encoding encoding) => File.ReadAllLines(Resolve(path), encoding);
    internal static IEnumerable<string> ReadLines(string path) => File.ReadLines(Resolve(path));
    internal static IEnumerable<string> ReadLines(string path, Encoding encoding) => File.ReadLines(Resolve(path), encoding);
    internal static FileStream OpenRead(string path) => File.OpenRead(Resolve(path));
    internal static Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default) => File.ReadAllTextAsync(Resolve(path), cancellationToken);
    internal static Task<string> ReadAllTextAsync(string path, Encoding encoding, CancellationToken cancellationToken = default) => File.ReadAllTextAsync(Resolve(path), encoding, cancellationToken);
    internal static Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default) => File.ReadAllBytesAsync(Resolve(path), cancellationToken);
    internal static Task<string[]> ReadAllLinesAsync(string path, CancellationToken cancellationToken = default) => File.ReadAllLinesAsync(Resolve(path), cancellationToken);

    internal static string[] GetFiles(string path, string pattern = "*", SearchOption option = SearchOption.TopDirectoryOnly)
    {
        var paths = Directory.Exists(path) ? Directory.GetFiles(path, pattern, option).ToList() : new List<string>();
        var root = Repository(Path.Combine(Path.GetFullPath(path), "placeholder"));
        if (root is null) return paths.ToArray();
        var relative = Path.GetRelativePath(root, Path.GetFullPath(path)).Replace('\\', '/').TrimEnd('/') + "/";
        foreach (var alias in Catalog(root).Keys)
        {
            if (!alias.StartsWith(relative, StringComparison.Ordinal)) continue;
            var tail = alias[relative.Length..];
            if (option == SearchOption.TopDirectoryOnly && tail.Contains('/')) continue;
            if (FileSystemName.MatchesSimpleExpression(pattern, Path.GetFileName(alias), ignoreCase: OperatingSystem.IsWindows())) paths.Add(Path.GetFullPath(Path.Combine(root, alias)));
        }
        return paths.Distinct(StringComparer.Ordinal).ToArray();
    }
    internal static IEnumerable<string> EnumerateFiles(string path, string pattern = "*", SearchOption option = SearchOption.TopDirectoryOnly) => GetFiles(path, pattern, option);
    internal static bool DirectoryExists(string path)
    {
        if (Directory.Exists(path)) return true;
        var root = Repository(Path.Combine(Path.GetFullPath(path), "placeholder"));
        if (root is null) return false;
        var prefix = Path.GetRelativePath(root, Path.GetFullPath(path)).Replace('\\', '/').TrimEnd('/') + "/";
        return Catalog(root).Keys.Any(alias => alias.StartsWith(prefix, StringComparison.Ordinal));
    }
}

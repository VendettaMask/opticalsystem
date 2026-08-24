using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OptilandWorkbench.Core.Multiconfig;

namespace OptilandWorkbench.Core.Serialization;

public sealed record StarOptProjectDocument(
    IReadOnlyList<Optic> Configurations,
    int ActiveConfigurationIndex,
    IReadOnlyList<MultiConfigurationLinkOverride>? BrokenLinks = null);

public static class StarOptProjectStore
{
    public const string Extension = ".staropt";
    public const ushort ContainerVersion = 1;
    public const int ProjectFormatVersion = 1;
    public const int MaximumConfigurationCount = 4096;
    public const int MaximumPayloadLength = 256 * 1024 * 1024;

    private const ushort BrotliCompressionFlag = 1;
    private const int HeaderLength = 52;
    private static readonly byte[] Magic = "STAROPT\x1a"u8.ToArray();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    public static async Task SaveAsync(
        StarOptProjectDocument document,
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ValidateDocument(document);

        var configurations = document.Configurations
            .Select(configuration => configuration.ToSnapshot())
            .ToList();
        foreach (var configuration in configurations)
        {
            OpticSnapshotValidator.Validate(configuration);
        }

        var project = new StarOptProjectSnapshot(
            ProjectFormatVersion,
            "Optical System Design",
            document.ActiveConfigurationIndex,
            configurations,
            document.BrokenLinks?.ToList());
        var json = JsonSerializer.SerializeToUtf8Bytes(project, JsonOptions);
        if (json.Length > MaximumPayloadLength)
        {
            throw new InvalidDataException(
                "The STAROPT project is too large to be reopened by this application.");
        }

        var compressed = Compress(json);
        if (compressed.Length > MaximumPayloadLength)
        {
            throw new InvalidDataException(
                "The STAROPT compressed project payload is too large to be reopened by this application.");
        }

        var header = BuildHeader(json, compressed.Length);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("The project path does not have a parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous))
            {
                await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
                await stream.WriteAsync(compressed, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public static async Task<StarOptProjectDocument> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var fileLength = new FileInfo(path).Length;
        if (fileLength < HeaderLength || fileLength > HeaderLength + MaximumPayloadLength)
        {
            throw new InvalidDataException("The STAROPT project file length is invalid.");
        }

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        return Deserialize(bytes);
    }

    public static bool HasMagic(ReadOnlySpan<byte> bytes)
    {
        return bytes.Length >= Magic.Length && bytes[..Magic.Length].SequenceEqual(Magic);
    }

    public static async Task<bool> HasMagicAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: Magic.Length,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var prefix = new byte[Magic.Length];
        var offset = 0;
        while (offset < prefix.Length)
        {
            var read = await stream.ReadAsync(prefix.AsMemory(offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return false;
            }

            offset += read;
        }

        return HasMagic(prefix);
    }

    internal static StarOptProjectDocument Deserialize(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < HeaderLength || !HasMagic(bytes))
        {
            throw new InvalidDataException("The selected file is not a STAROPT project.");
        }

        var containerVersion = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(8, 2));
        if (containerVersion != ContainerVersion)
        {
            throw new InvalidDataException(
                $"STAROPT container version {containerVersion} is not supported by this application.");
        }

        var flags = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(10, 2));
        if (flags != BrotliCompressionFlag)
        {
            throw new InvalidDataException($"STAROPT compression flags 0x{flags:x4} are not supported.");
        }

        var uncompressedLength = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(12, 4));
        var compressedLength = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(16, 4));
        if (uncompressedLength <= 0 || uncompressedLength > MaximumPayloadLength ||
            compressedLength <= 0 || compressedLength != bytes.Length - HeaderLength)
        {
            throw new InvalidDataException("The STAROPT project payload length is invalid.");
        }

        var expectedHash = bytes.Slice(20, SHA256.HashSizeInBytes);
        var json = Decompress(bytes[HeaderLength..], uncompressedLength);
        var actualHash = SHA256.HashData(json);
        if (!CryptographicOperations.FixedTimeEquals(expectedHash, actualHash))
        {
            throw new InvalidDataException("The STAROPT project checksum does not match its contents.");
        }

        StarOptProjectSnapshot project;
        try
        {
            project = JsonSerializer.Deserialize<StarOptProjectSnapshot>(json, JsonOptions)
                ?? throw new InvalidDataException("The STAROPT project payload is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The STAROPT project payload is not valid JSON.", exception);
        }

        if (project.FormatVersion != ProjectFormatVersion)
        {
            throw new InvalidDataException(
                $"STAROPT project format version {project.FormatVersion} is not supported by this application.");
        }

        if (project.Configurations is null ||
            project.Configurations.Count == 0 ||
            project.Configurations.Count > MaximumConfigurationCount ||
            project.Configurations.Any(configuration => configuration is null) ||
            project.ActiveConfigurationIndex < 0 ||
            project.ActiveConfigurationIndex >= project.Configurations.Count)
        {
            throw new InvalidDataException("The STAROPT project configuration table is invalid.");
        }

        var configurations = project.Configurations
            .Select(Optic.FromSnapshot)
            .ToArray();
        ValidateBrokenLinks(project.BrokenLinks, configurations);
        return new StarOptProjectDocument(
            configurations,
            project.ActiveConfigurationIndex,
            project.BrokenLinks);
    }

    private static byte[] BuildHeader(ReadOnlySpan<byte> json, int compressedLength)
    {
        var header = new byte[HeaderLength];
        Magic.CopyTo(header, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(8, 2), ContainerVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(10, 2), BrotliCompressionFlag);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(12, 4), json.Length);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(16, 4), compressedLength);
        SHA256.HashData(json).CopyTo(header, 20);
        return header;
    }

    private static byte[] Compress(ReadOnlySpan<byte> data)
    {
        using var output = new MemoryStream();
        using (var compressed = new BrotliStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            compressed.Write(data);
        }

        return output.ToArray();
    }

    private static byte[] Decompress(ReadOnlySpan<byte> data, int expectedLength)
    {
        try
        {
            using var source = new MemoryStream(data.ToArray(), writable: false);
            using var compressed = new BrotliStream(source, CompressionMode.Decompress);
            using var output = new MemoryStream(expectedLength);
            var buffer = new byte[64 * 1024];
            while (true)
            {
                var read = compressed.Read(buffer);
                if (read == 0)
                {
                    break;
                }

                if (output.Length + read > expectedLength)
                {
                    throw new InvalidDataException("The STAROPT project expands beyond its declared payload length.");
                }

                output.Write(buffer, 0, read);
            }

            if (output.Length != expectedLength)
            {
                throw new InvalidDataException("The STAROPT project payload is truncated.");
            }

            return output.ToArray();
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidDataException("The STAROPT project compressed payload is invalid.", exception);
        }
    }

    private static void ValidateDocument(StarOptProjectDocument document)
    {
        ArgumentNullException.ThrowIfNull(document.Configurations);
        if (document.Configurations.Count == 0)
        {
            throw new ArgumentException("A STAROPT project must contain at least one configuration.", nameof(document));
        }

        if (document.Configurations.Count > MaximumConfigurationCount)
        {
            throw new ArgumentException(
                $"A STAROPT project cannot contain more than {MaximumConfigurationCount} configurations.",
                nameof(document));
        }

        if (document.Configurations.Any(configuration => configuration is null))
        {
            throw new ArgumentException("A STAROPT project cannot contain a null configuration.", nameof(document));
        }

        if (document.ActiveConfigurationIndex < 0 ||
            document.ActiveConfigurationIndex >= document.Configurations.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(document),
                "The active configuration index is outside the configuration table.");
        }

        ValidateBrokenLinks(document.BrokenLinks, document.Configurations);
    }

    private static void ValidateBrokenLinks(
        IReadOnlyList<MultiConfigurationLinkOverride>? links,
        IReadOnlyList<Optic> configurations)
    {
        if (links is null)
        {
            return;
        }

        foreach (var link in links)
        {
            if (link.ConfigurationIndex <= 0 || link.ConfigurationIndex >= configurations.Count
                || string.IsNullOrWhiteSpace(link.Property)
                || link.Property.Trim().ToLowerInvariant() is not ("radius" or "thickness" or "conic" or "material")
                || configurations[0].SurfaceGroup.Items.All(
                    surface => surface.Number != link.SurfaceNumber)
                || configurations[link.ConfigurationIndex].SurfaceGroup.Items.All(
                    surface => surface.Number != link.SurfaceNumber))
            {
                throw new InvalidDataException("The STAROPT multi-configuration link table is invalid.");
            }
        }
    }

    private sealed record StarOptProjectSnapshot(
        int FormatVersion,
        string Application,
        int ActiveConfigurationIndex,
        List<OpticSnapshot> Configurations,
        List<MultiConfigurationLinkOverride>? BrokenLinks = null);
}

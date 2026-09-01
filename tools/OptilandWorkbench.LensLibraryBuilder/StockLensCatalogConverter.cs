using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core.FileIO;

namespace OptilandWorkbench.LensLibraryBuilder;

internal static class StockLensCatalogConverter
{
    private const uint SupportedVersion = 1001;
    private const int RecordHeaderSize = 144;
    private const int MaximumElementCount = 1_024;
    private const int MaximumRecordCount = 100_000;
    private const string ShapeCodes = "?EBPM";

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly IReadOnlyDictionary<string, (string Manufacturer, string ProductUrl)> Catalogs =
        new Dictionary<string, (string Manufacturer, string ProductUrl)>(StringComparer.OrdinalIgnoreCase)
        {
            ["THORLABS"] = ("Thorlabs", "https://www.thorlabs.com/"),
            ["EDMUND OPTICS"] = ("Edmund Optics", "https://www.edmundoptics.com/"),
            ["DAHENG OPTICS"] = ("Daheng Optics", "https://www.cdhcorp.com.cn/"),
            ["NEWPORT CORP"] = ("Newport", "https://www.newport.com/"),
            ["SIGMA KOKI"] = ("Sigma Koki", "https://www.sigma-koki.com/")
        };

    public static async Task<CommercialLensCatalogDocument> ConvertAsync(
        string sourceDirectory,
        string outputDirectory,
        string? seedCatalogPath = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        var fullSourceDirectory = Path.GetFullPath(sourceDirectory);
        var fullOutputDirectory = Path.GetFullPath(outputDirectory);
        if (!Directory.Exists(fullSourceDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Stock-lens source directory was not found: {fullSourceDirectory}");
        }

        var entries = new Dictionary<string, CommercialLensEntryDto>(StringComparer.Ordinal);
        var existingPath = Directory.Exists(fullOutputDirectory)
            ? fullOutputDirectory
            : string.IsNullOrWhiteSpace(seedCatalogPath)
                ? null
                : Path.GetFullPath(seedCatalogPath);
        if (existingPath is not null && (File.Exists(existingPath) || Directory.Exists(existingPath)))
        {
            var existingCatalogs = Directory.Exists(existingPath)
                ? Directory.EnumerateFiles(existingPath, "*.json", SearchOption.TopDirectoryOnly)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .Select(CommercialLensCatalogStore.LoadFile)
                    .ToArray()
                : new[]
                {
                    JsonSerializer.Deserialize<CommercialLensCatalogDocument>(
                        await BoundedFile.ReadAllTextAsync(
                            existingPath,
                            BoundedFile.MaximumCatalogBytes,
                            "Commercial-lens seed catalog",
                            cancellationToken).ConfigureAwait(false),
                        ReadOptions) ?? throw new InvalidDataException("Commercial-lens seed catalog is empty.")
                };
            if (existingCatalogs.Any(existing => existing.Version != 1))
            {
                throw new InvalidDataException("A commercial-lens seed catalog has an unsupported version.");
            }

            foreach (var entry in existingCatalogs.SelectMany(existing => existing.Entries))
            {
                entries[CommercialKey(entry)] = entry;
            }
        }

        var sourceFiles = Directory.EnumerateFiles(fullSourceDirectory)
            .Where(path => Path.GetExtension(path).Equals(".zmf", StringComparison.OrdinalIgnoreCase))
            .Where(path => Catalogs.ContainsKey(Path.GetFileNameWithoutExtension(path)))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var missingCatalogs = Catalogs.Keys
            .Where(catalog => !sourceFiles.Any(path =>
                Path.GetFileNameWithoutExtension(path).Equals(catalog, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if (missingCatalogs.Length > 0)
        {
            throw new FileNotFoundException(
                $"Stock-lens source is missing required catalogs: {string.Join(", ", missingCatalogs)}.");
        }

        foreach (var sourcePath in sourceFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var converted in ReadFile(sourcePath))
            {
                var key = CommercialKey(converted);
                if (!entries.TryGetValue(key, out var existing))
                {
                    entries[key] = converted;
                    continue;
                }

                entries[key] = existing with
                {
                    EntrancePupilDiameter = existing.EntrancePupilDiameter > 0
                        ? existing.EntrancePupilDiameter
                        : converted.EntrancePupilDiameter,
                    ShapeCode = existing.ShapeCode == "?" ? converted.ShapeCode : existing.ShapeCode,
                    SurfaceType = string.IsNullOrWhiteSpace(existing.SurfaceType)
                        ? converted.SurfaceType
                        : existing.SurfaceType,
                    SourceNote = MergeNotes(existing.SourceNote, converted.SourceNote)
                };
            }
        }

        var catalog = new CommercialLensCatalogDocument(
            1,
            sourceFiles
                .Select(path => new DateTimeOffset(File.GetLastWriteTimeUtc(path)))
                .Max(),
            entries.Values
                .Where(entry => StockLensCatalogPolicy.IncludesManufacturer(entry.Manufacturer))
                .OrderBy(entry => entry.Manufacturer, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.PartNumber, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Id, StringComparer.Ordinal)
                .ToArray());
        await CommercialLensCatalogStore.SaveDirectoryAsync(catalog, fullOutputDirectory, cancellationToken)
            .ConfigureAwait(false);
        return catalog;
    }

    internal static IReadOnlyList<CommercialLensEntryDto> ReadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var catalogKey = Path.GetFileNameWithoutExtension(path);
        if (!Catalogs.TryGetValue(catalogKey, out var catalog))
        {
            throw new InvalidDataException($"Unsupported stock-lens catalog: {catalogKey}.");
        }

        using var stream = BoundedFile.OpenRead(path, BoundedFile.MaximumCatalogBytes, "ZMF stock catalog");
        using var reader = new BinaryReader(stream, Encoding.Latin1, leaveOpen: false);
        if (stream.Length < sizeof(uint) || reader.ReadUInt32() != SupportedVersion)
        {
            throw new InvalidDataException("Only Zemax ZMF catalog version 1001 is supported.");
        }

        var sourceTimestamp = new DateTimeOffset(File.GetLastWriteTimeUtc(path));
        var entries = new List<CommercialLensEntryDto>();
        var recordIndex = 0;
        while (stream.Position < stream.Length)
        {
            if (recordIndex >= MaximumRecordCount)
            {
                throw new InvalidDataException($"ZMF catalog {Path.GetFileName(path)} exceeds the record limit.");
            }
            if (stream.Length - stream.Position < RecordHeaderSize)
            {
                throw new InvalidDataException($"ZMF catalog {Path.GetFileName(path)} has an incomplete record header.");
            }

            var name = DecodeName(reader.ReadBytes(100));
            var lensVersion = reader.ReadUInt32();
            var elementCount = reader.ReadUInt32();
            var shapeIndex = reader.ReadUInt32();
            var aspheric = reader.ReadUInt32();
            var grin = reader.ReadUInt32();
            var toroidal = reader.ReadUInt32();
            var descriptionLength = reader.ReadUInt32();
            var effectiveFocalLength = reader.ReadDouble();
            var entrancePupilDiameter = reader.ReadDouble();
            if (elementCount > MaximumElementCount || descriptionLength > stream.Length - stream.Position)
            {
                throw new InvalidDataException(
                    $"ZMF catalog {Path.GetFileName(path)} has an invalid element count or record length.");
            }

            stream.Seek(descriptionLength, SeekOrigin.Current);
            if (!string.IsNullOrWhiteSpace(name))
            {
                var shapeCode = shapeIndex < ShapeCodes.Length
                    ? ShapeCodes[(int)shapeIndex].ToString()
                    : "?";
                var surfaceType = SurfaceType(aspheric, grin, toroidal);
                entries.Add(new CommercialLensEntryDto(
                    StableId(catalogKey, name, recordIndex),
                    catalog.Manufacturer,
                    name,
                    name,
                    "离线目录快照；不表示实时供应",
                    catalog.ProductUrl,
                    string.Empty,
                    LensType(shapeCode, surfaceType, elementCount),
                    shapeCode,
                    surfaceType,
                    (int)elementCount,
                    FiniteOrZero(effectiveFocalLength),
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    "仅目录元数据；未随附厂商处方",
                    null,
                    $"离线维护工具从 {Path.GetFileName(path)} 目录版本 {lensVersion} 的目录头转换；不包含处方正文。",
                    sourceTimestamp,
                    FiniteOrZero(entrancePupilDiameter)));
            }
            recordIndex++;
        }

        return entries;
    }

    private static string DecodeName(byte[] bytes)
    {
        var length = Array.IndexOf(bytes, (byte)0);
        return Encoding.Latin1.GetString(bytes, 0, length >= 0 ? length : bytes.Length).Trim();
    }

    private static string SurfaceType(uint aspheric, uint grin, uint toroidal) =>
        grin > 0 ? "G" : toroidal > 0 ? "T" : aspheric > 0 ? "A" : "S";

    private static string LensType(string shapeCode, string surfaceType, uint elements)
    {
        var surface = surfaceType switch
        {
            "G" => "GRIN",
            "T" => "环曲面",
            "A" => "非球面",
            _ => "球面"
        };
        var shape = shapeCode switch
        {
            "E" => "等曲率",
            "B" => "双面曲率",
            "P" => "平面型",
            "M" => "弯月型",
            _ => "其他形状"
        };
        return $"{surface} · {shape} · {elements} 元件";
    }

    private static string StableId(string catalog, string name, int index)
    {
        var value = Encoding.UTF8.GetBytes($"{catalog}\n{name}\n{index}");
        return $"stock-{Convert.ToHexString(SHA256.HashData(value))[..20].ToLowerInvariant()}";
    }

    private static string CommercialKey(CommercialLensEntryDto entry) =>
        $"{Canonical(entry.Manufacturer)}:{Canonical(entry.PartNumber)}";

    private static string Canonical(string value) =>
        string.Concat(value.Where(char.IsLetterOrDigit)).ToUpperInvariant();

    private static string MergeNotes(string existing, string converted) =>
        existing.Contains(converted, StringComparison.Ordinal) ? existing : $"{existing} {converted}".Trim();

    private static double FiniteOrZero(double value) => double.IsFinite(value) ? value : 0;
}

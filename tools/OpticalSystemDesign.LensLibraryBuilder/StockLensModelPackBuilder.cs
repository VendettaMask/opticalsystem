using System.Globalization;
using System.Text;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Serialization;

namespace OpticalSystemDesign.LensLibraryBuilder;

internal static class StockLensModelPackBuilder
{
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    internal static async Task<int> BuildAsync(string sourcePath, string libraryDirectory, string outputPath,
        string? supplementalModelDirectory)
    {
        var existing = CommercialLensCatalogStore.LoadDirectory(libraryDirectory).Entries
            .ToLookup(entry => (entry.Manufacturer.ToUpperInvariant(), entry.PartNumber.ToUpperInvariant()));
        var entries = new List<CommercialLensEntryDto>();
        var projects = new Dictionary<string, string>(StringComparer.Ordinal);
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"stock-lens-pack-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            var importer = new ZemaxZmxImporter();
            foreach (var record in StockLensCatalogConverter.ReadRecords(sourcePath, includePrescription: true))
            {
                var sourceEntry = record.Entry;
                var current = existing[(sourceEntry.Manufacturer.ToUpperInvariant(), sourceEntry.PartNumber.ToUpperInvariant())].FirstOrDefault();
                var entry = sourceEntry with
                {
                    Id = current?.Id ?? sourceEntry.Id,
                    ProductUrl = current?.ProductUrl ?? sourceEntry.ProductUrl,
                    DataSheetUrl = current?.DataSheetUrl ?? sourceEntry.DataSheetUrl,
                    CatalogDiameter = current?.CatalogDiameter ?? sourceEntry.CatalogDiameter,
                    SourceNote = $"原厂 {Path.GetFileName(sourcePath)}，处方版本 {record.LensVersion}。"
                };
                try
                {
                    var imported = importer.ImportConfigurationSet(DecodePrescription(record));
                    var surfaces = imported.Configurations.SelectMany(optic => optic.SurfaceGroup.Items).ToArray();
                    var unresolved = surfaces.Select(surface => surface.MaterialAfter).OfType<UnresolvedMaterial>()
                        .Select(material => material.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                    var unmapped = surfaces.Select(surface => surface.Geometry).OfType<INonComputableGeometry>()
                        .Select(geometry => geometry.OriginalType).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                    var projectPath = Path.Combine(temporaryDirectory, $"{entries.Count:D5}.staropt");
                    await StarOptProjectStore.SaveAsync(new StarOptProjectDocument(
                        imported.Configurations, imported.ActiveConfigurationIndex), projectPath).ConfigureAwait(false);
                    _ = await StarOptProjectStore.LoadAsync(projectPath).ConfigureAwait(false);
                    projects.Add(entry.Id, projectPath);
                    entry = entry with
                    {
                        ModelStatus = "库内原厂处方可载入"
                            + (unresolved.Length == 0 ? "" : $" · 待解析玻璃 {string.Join("、", unresolved)}")
                            + (unmapped.Length == 0 ? "" : $" · 待映射面型 {string.Join("、", unmapped)}")
                    };
                    if (unresolved.Length > 0)
                        Console.WriteLine($"MATERIAL\t{entry.PartNumber}\t{string.Join(", ", unresolved)}");
                    if (unmapped.Length > 0)
                        Console.WriteLine($"GEOMETRY\t{entry.PartNumber}\t{string.Join(", ", unmapped)}");
                }
                catch (Exception exception) when (exception is InvalidDataException or NotSupportedException or InvalidOperationException or ArgumentException or FormatException or OverflowException)
                {
                    entry = entry with { ModelStatus = "原厂处方待转换" };
                    Console.Error.WriteLine($"FAILED\t{entry.PartNumber}\t{exception.Message}");
                }
                entries.Add(entry);
            }

            var sourceCount = entries.Count;
            var manufacturer = entries.FirstOrDefault()?.Manufacturer
                ?? throw new InvalidDataException("The stock-lens source is empty.");
            var sourceParts = entries.Select(entry => entry.PartNumber).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var sourceModels = projects.Count;
            foreach (var entry in existing.SelectMany(group => group)
                         .Where(entry => entry.Manufacturer.Equals(manufacturer, StringComparison.OrdinalIgnoreCase) &&
                             !sourceParts.Contains(entry.PartNumber)))
            {
                var supplemental = FindSupplementalProject(supplementalModelDirectory, entry.LocalModelPath);
                if (supplemental is not null)
                {
                    _ = await StarOptProjectStore.LoadAsync(supplemental).ConfigureAwait(false);
                    projects.Add(entry.Id, supplemental);
                }

                entries.Add(entry with
                {
                    ModelStatus = supplemental is null ? "历史目录记录" : "库内原厂处方可载入 · 历史料号",
                    NativePath = null,
                    LocalModelPath = null
                });
            }
            var catalog = new CommercialLensCatalogDocument(1, new DateTimeOffset(File.GetLastWriteTimeUtc(sourcePath)), entries);
            await StockLensModelPack.SaveAsync(outputPath, catalog, projects).ConfigureAwait(false);
            Console.WriteLine($"Models: {sourceModels}/{sourceCount}; supplemental models: {projects.Count - sourceModels}; historical entries: {entries.Count - sourceCount}");
            return sourceModels == sourceCount ? 0 : 1;
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private static string? FindSupplementalProject(string? directory, string? relativePath)
    {
        if (directory is null || string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath)) return null;
        var root = Path.GetFullPath(directory);
        var path = Path.GetFullPath(Path.Combine(root, relativePath));
        var relative = Path.GetRelativePath(root, path);
        if (Path.IsPathRooted(relative) || relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains("..")
            || !Path.GetExtension(path).Equals(StarOptProjectStore.Extension, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Supplemental model path is outside the local model directory or has an invalid format.");
        return File.Exists(path) ? path : null;
    }

    internal static string DecodePrescription(StockLensCatalogConverter.ZmfStockRecord record)
    {
        // ZMF 1001 uses the header's EFL and EPD to encode each embedded text prescription.
        // Interoperability reference: https://github.com/quartiq/rayopt/blob/master/rayopt/zemax.py
        var prescription = record.Prescription ?? throw new InvalidDataException("Missing ZMF prescription.");
        var offset = Math.Cos(6 * record.EffectiveFocalLength + 3 * record.EntrancePupilDiameter);
        offset += Math.Cos(655 * (Math.PI / 180) * offset);
        var decoded = new byte[prescription.Length];
        Span<char> scientific = stackalloc char[32];
        for (var index = 0; index < prescription.Length; index++)
        {
            var coefficient = 13.2 * (offset + Math.Sin(17.0 * (index + 3))) * (index + 1);
            if (!double.IsFinite(coefficient) ||
                !coefficient.TryFormat(scientific, out _, "e8", CultureInfo.InvariantCulture) ||
                !int.TryParse(scientific.Slice(4, 3), NumberStyles.None, CultureInfo.InvariantCulture, out var key))
                throw new InvalidDataException("Invalid ZMF prescription encoding.");
            decoded[index] = (byte)(prescription[index] ^ (byte)key);
        }

        string text;
        try { text = StrictUtf8.GetString(decoded); }
        catch (DecoderFallbackException) { text = Encoding.Latin1.GetString(decoded); }
        var version = $"VERS {record.LensVersion:D6}";
        if (!text.StartsWith(version, StringComparison.Ordinal) || text.Length <= version.Length || !char.IsWhiteSpace(text[version.Length]))
            throw new InvalidDataException("The ZMF prescription version does not match its record.");
        return text;
    }
}

using System.Text.Json;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Serialization;

namespace OptilandWorkbench.ZemaxLibraryImporter;

public sealed record ZemaxLibraryInstallOptions(
    string InputPath,
    string ExampleDirectory,
    string LensLibraryDirectory,
    string SourceId = "user-examples",
    string SourceName = "STAR Labs 用户示例",
    string Category = "示例镜头",
    string SourceUrl = "",
    string License = "用户提供",
    string? Name = null,
    string? Id = null,
    string? ExampleFileName = null,
    string? LensType = null,
    string? Application = null,
    string? DesignOrganization = null);

public sealed record ZemaxLibraryInstallResult(
    string Id,
    string Name,
    string ExampleProjectPath,
    string LibraryProjectPath,
    string CatalogPath,
    bool UpdatedExistingEntry,
    int ConfigurationCount);

public sealed class ZemaxLibraryInstaller
{
    private const int SupportedCatalogVersion = 2;
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true
    };

    public async Task<ZemaxLibraryInstallResult> InstallAsync(
        ZemaxLibraryInstallOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var inputPath = Path.GetFullPath(options.InputPath);
        if (!Path.GetExtension(inputPath).Equals(".zmx", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("输入文件必须是 Zemax .zmx 文件。", nameof(options));
        }

        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException("找不到 Zemax 输入文件。", inputPath);
        }

        var exampleDirectory = Path.GetFullPath(options.ExampleDirectory);
        var lensLibraryDirectory = Path.GetFullPath(options.LensLibraryDirectory);
        if (exampleDirectory.Equals(lensLibraryDirectory, PathComparison))
        {
            throw new ArgumentException("示例库与数据库镜头库必须是两个不同目录。", nameof(options));
        }

        var sourceId = Required(options.SourceId, "来源 ID");
        var sourceName = Required(options.SourceName, "来源名称");
        var category = Required(options.Category, "镜头分类");
        var license = Required(options.License, "许可说明");
        var id = string.IsNullOrWhiteSpace(options.Id)
            ? LensLibraryCatalogEntryFactory.CreateStableId(sourceId, Path.GetFileName(inputPath))
            : LensLibraryCatalogEntryFactory.SafeName(options.Id);
        var exampleFileName = ResolveExampleFileName(inputPath, options.ExampleFileName);
        var exampleProjectPath = SafeChildPath(exampleDirectory, exampleFileName);
        var libraryProjectPath = SafeChildPath(
            lensLibraryDirectory,
            Path.Combine("projects", $"{id}{StarOptProjectStore.Extension}"));
        var catalogPath = SafeChildPath(lensLibraryDirectory, "index.json");

        var importer = new ZemaxZmxImporter();
        var imported = await importer
            .ImportConfigurationSetFileAsync(inputPath, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var existingCatalog = await LoadCatalogAsync(catalogPath, cancellationToken).ConfigureAwait(false);
        var updatedExisting = existingCatalog.Entries.Any(entry =>
            entry.Id.Equals(id, StringComparison.Ordinal));
        var nativePath = $"projects/{id}{StarOptProjectStore.Extension}";
        var entry = LensLibraryCatalogEntryFactory.Create(
            id,
            options.Name,
            category,
            sourceName,
            options.SourceUrl ?? string.Empty,
            license,
            nativePath,
            inputPath,
            imported.ActiveOptic,
            options.LensType,
            options.Application,
            options.DesignOrganization,
            DateTimeOffset.UtcNow);
        var entries = existingCatalog.Entries
            .Where(existing => !existing.Id.Equals(id, StringComparison.Ordinal))
            .Append(entry)
            .OrderBy(existing => existing.Category, StringComparer.Ordinal)
            .ThenBy(existing => existing.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(existing => existing.Id, StringComparer.Ordinal)
            .ToArray();
        var updatedCatalog = new LensLibraryCatalogDocument(
            SupportedCatalogVersion,
            existingCatalog.BuiltAt == default ? DateTimeOffset.UnixEpoch : existingCatalog.BuiltAt,
            entries);

        var stagingDirectory = Path.Combine(
            Path.GetTempPath(),
            $"staropt-zemax-import-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDirectory);
        try
        {
            var stagedProjectPath = Path.Combine(stagingDirectory, $"{id}{StarOptProjectStore.Extension}");
            var stagedCatalogPath = Path.Combine(stagingDirectory, "index.json");
            await StarOptProjectStore.SaveAsync(
                    new StarOptProjectDocument(
                        imported.Configurations,
                        imported.ActiveConfigurationIndex),
                    stagedProjectPath,
                    cancellationToken)
                .ConfigureAwait(false);
            var verified = await StarOptProjectStore
                .LoadAsync(stagedProjectPath, cancellationToken)
                .ConfigureAwait(false);
            if (verified.Configurations.Count != imported.Configurations.Count
                || verified.ActiveConfigurationIndex != imported.ActiveConfigurationIndex)
            {
                throw new InvalidDataException("STAROPT 转换后的配置数量或活动配置不一致。");
            }

            await File.WriteAllTextAsync(
                    stagedCatalogPath,
                    JsonSerializer.Serialize(updatedCatalog, WriteOptions),
                    cancellationToken)
                .ConfigureAwait(false);
            await ReplaceFilesAtomicallyAsync(
                    new[]
                    {
                        (Source: stagedProjectPath, Target: exampleProjectPath),
                        (Source: stagedProjectPath, Target: libraryProjectPath),
                        (Source: stagedCatalogPath, Target: catalogPath)
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(stagingDirectory, recursive: true);
            }
        }

        return new ZemaxLibraryInstallResult(
            id,
            entry.Name,
            exampleProjectPath,
            libraryProjectPath,
            catalogPath,
            updatedExisting,
            imported.Configurations.Count);
    }

    private static async Task<LensLibraryCatalogDocument> LoadCatalogAsync(
        string catalogPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(catalogPath))
        {
            return new LensLibraryCatalogDocument(
                SupportedCatalogVersion,
                DateTimeOffset.UnixEpoch,
                Array.Empty<LensLibraryEntryDto>());
        }

        try
        {
            var json = await File.ReadAllTextAsync(catalogPath, cancellationToken).ConfigureAwait(false);
            var catalog = JsonSerializer.Deserialize<LensLibraryCatalogDocument>(json, ReadOptions)
                ?? throw new InvalidDataException("数据库镜头库索引为空。");
            if (catalog.Version != SupportedCatalogVersion)
            {
                throw new InvalidDataException(
                    $"不支持镜头库索引版本 {catalog.Version}，当前仅支持版本 {SupportedCatalogVersion}。");
            }

            if (catalog.Entries is null)
            {
                throw new InvalidDataException("数据库镜头库索引缺少 Entries 列表。");
            }

            return catalog;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("数据库镜头库 index.json 格式无效。", exception);
        }
    }

    private static async Task ReplaceFilesAtomicallyAsync(
        IReadOnlyList<(string Source, string Target)> files,
        CancellationToken cancellationToken)
    {
        var prepared = new List<PreparedFile>(files.Count);
        var committed = new List<PreparedFile>(files.Count);
        var completed = false;
        try
        {
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var targetDirectory = Path.GetDirectoryName(file.Target)
                    ?? throw new InvalidOperationException("输出文件没有父目录。");
                Directory.CreateDirectory(targetDirectory);
                var token = Guid.NewGuid().ToString("N");
                var temporaryPath = Path.Combine(
                    targetDirectory,
                    $".{Path.GetFileName(file.Target)}.{token}.tmp");
                var backupPath = Path.Combine(
                    targetDirectory,
                    $".{Path.GetFileName(file.Target)}.{token}.bak");
                await using (var source = new FileStream(
                                 file.Source,
                                 FileMode.Open,
                                 FileAccess.Read,
                                 FileShare.Read,
                                 64 * 1024,
                                 FileOptions.Asynchronous | FileOptions.SequentialScan))
                await using (var target = new FileStream(
                                 temporaryPath,
                                 FileMode.CreateNew,
                                 FileAccess.Write,
                                 FileShare.None,
                                 64 * 1024,
                                 FileOptions.Asynchronous))
                {
                    await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
                    await target.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                prepared.Add(new PreparedFile(
                    file.Target,
                    temporaryPath,
                    backupPath,
                    File.Exists(file.Target)));
            }

            foreach (var file in prepared)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (file.Existed)
                {
                    File.Move(file.TargetPath, file.BackupPath);
                }

                File.Move(file.TemporaryPath, file.TargetPath);
                committed.Add(file);
            }

            completed = true;
        }
        finally
        {
            if (!completed)
            {
                foreach (var file in committed.AsEnumerable().Reverse())
                {
                    if (File.Exists(file.TargetPath))
                    {
                        File.Delete(file.TargetPath);
                    }

                    if (file.Existed && File.Exists(file.BackupPath))
                    {
                        File.Move(file.BackupPath, file.TargetPath);
                    }
                }
            }

            foreach (var file in prepared)
            {
                if (File.Exists(file.TemporaryPath))
                {
                    File.Delete(file.TemporaryPath);
                }

                if (File.Exists(file.BackupPath))
                {
                    if (completed)
                    {
                        File.Delete(file.BackupPath);
                    }
                    else if (!File.Exists(file.TargetPath))
                    {
                        File.Move(file.BackupPath, file.TargetPath);
                    }
                }
            }
        }
    }

    private static string ResolveExampleFileName(string inputPath, string? requestedFileName)
    {
        var candidate = string.IsNullOrWhiteSpace(requestedFileName)
            ? $"{Path.GetFileNameWithoutExtension(inputPath)}{StarOptProjectStore.Extension}"
            : requestedFileName.Trim();
        if (!Path.GetFileName(candidate).Equals(candidate, StringComparison.Ordinal)
            || candidate.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("示例文件名只能是普通文件名，不能包含目录或非法字符。");
        }

        return Path.GetExtension(candidate).Equals(
            StarOptProjectStore.Extension,
            StringComparison.OrdinalIgnoreCase)
            ? candidate
            : $"{candidate}{StarOptProjectStore.Extension}";
    }

    private static string SafeChildPath(string root, string relativePath)
    {
        var fullRoot = Path.GetFullPath(root);
        var candidate = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        var prefix = fullRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullRoot
            : $"{fullRoot}{Path.DirectorySeparatorChar}";
        if (!candidate.StartsWith(prefix, PathComparison))
        {
            throw new InvalidDataException("输出路径超出了指定的库目录。");
        }

        return candidate;
    }

    private static string Required(string value, string label)
    {
        return string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{label}不能为空。")
            : value.Trim();
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private sealed record PreparedFile(
        string TargetPath,
        string TemporaryPath,
        string BackupPath,
        bool Existed);
}

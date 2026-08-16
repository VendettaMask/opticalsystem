using System.Text.Json;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.ZemaxLibraryImporter;

namespace OptilandWorkbench.Tests;

public sealed class ZemaxLibraryImporterTests
{
    [Fact]
    public async Task InstallerPublishesSameNativeProjectToExamplesAndLensDatabase()
    {
        var root = Path.Combine(Path.GetTempPath(), $"zemax-library-import-{Guid.NewGuid():N}");
        var examples = Path.Combine(root, "samples", "lenses");
        var library = Path.Combine(root, "LensLibrary");
        var source = Path.Combine(AppContext.BaseDirectory, "Samples", "achromatic-doublet.zmx");

        try
        {
            var installer = new ZemaxLibraryInstaller();
            var result = await installer.InstallAsync(new ZemaxLibraryInstallOptions(
                source,
                examples,
                library,
                SourceId: "test-examples",
                SourceName: "测试示例库",
                Category: "测试镜头",
                License: "测试许可"));

            Assert.False(result.UpdatedExistingEntry);
            Assert.Equal(1, result.ConfigurationCount);
            Assert.True(File.Exists(result.ExampleProjectPath));
            Assert.True(File.Exists(result.LibraryProjectPath));
            Assert.True(File.Exists(result.CatalogPath));
            Assert.Equal(
                await File.ReadAllBytesAsync(result.ExampleProjectPath),
                await File.ReadAllBytesAsync(result.LibraryProjectPath));
            Assert.True(await StarOptProjectStore.HasMagicAsync(result.ExampleProjectPath));
            var project = await StarOptProjectStore.LoadAsync(result.LibraryProjectPath);
            Assert.Single(project.Configurations);

            var catalog = JsonSerializer.Deserialize<LensLibraryCatalogDocument>(
                await File.ReadAllTextAsync(result.CatalogPath));
            var entry = Assert.Single(catalog!.Entries);
            Assert.Equal(2, catalog.Version);
            Assert.Equal(result.Id, entry.Id);
            Assert.Equal("测试镜头", entry.Category);
            Assert.Equal("测试示例库", entry.SourceName);
            Assert.Equal("测试许可", entry.License);
            Assert.Equal("ZMX", entry.SourceFormat);
            Assert.Equal("可用", entry.ImportStatus);
            Assert.Equal($"projects/{result.Id}.staropt", entry.NativePath);
            Assert.Equal("achromatic-doublet.zmx", entry.SourcePath);
            Assert.NotNull(entry.ImportedAt);
            Assert.False(string.IsNullOrWhiteSpace(entry.ImporterVersion));
            Assert.True(entry.LensElementCount > 0);
            Assert.True(entry.MaximumClearAperture > 0);
            Assert.False(string.IsNullOrWhiteSpace(entry.LensType));
            Assert.False(string.IsNullOrWhiteSpace(entry.Application));
            Assert.False(string.IsNullOrWhiteSpace(entry.DesignOrganization));

            using var application = WorkbenchApplication.Create(lensLibraryDirectory: library);
            Assert.Equal(result.Id, Assert.Single(application.Lenses.GetLenses()).Id);
            Assert.NotNull(await application.Lenses.BuildPreviewAsync(result.Id));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ReimportUpdatesStableEntryWithoutCreatingDuplicates()
    {
        var root = Path.Combine(Path.GetTempPath(), $"zemax-library-update-{Guid.NewGuid():N}");
        var examples = Path.Combine(root, "examples");
        var library = Path.Combine(root, "library");
        var source = Path.Combine(AppContext.BaseDirectory, "Samples", "achromatic-doublet.zmx");

        try
        {
            var installer = new ZemaxLibraryInstaller();
            var options = new ZemaxLibraryInstallOptions(
                source,
                examples,
                library,
                SourceId: "stable-source",
                SourceName: "稳定来源",
                Category: "示例镜头",
                License: "测试");
            var first = await installer.InstallAsync(options);
            var second = await installer.InstallAsync(options with { Name = "更新后的消色差双胶合镜" });

            Assert.Equal(first.Id, second.Id);
            Assert.True(second.UpdatedExistingEntry);
            var catalog = JsonSerializer.Deserialize<LensLibraryCatalogDocument>(
                await File.ReadAllTextAsync(second.CatalogPath));
            var entry = Assert.Single(catalog!.Entries);
            Assert.Equal("更新后的消色差双胶合镜", entry.Name);
            Assert.Single(Directory.EnumerateFiles(
                Path.Combine(library, "projects"),
                "*.staropt",
                SearchOption.TopDirectoryOnly));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task InvalidZmxLeavesBothLibrariesUnchanged()
    {
        var root = Path.Combine(Path.GetTempPath(), $"zemax-library-failure-{Guid.NewGuid():N}");
        var examples = Path.Combine(root, "examples");
        var library = Path.Combine(root, "library");
        var source = Path.Combine(root, "invalid.zmx");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(source, "MODE NSC\n");

        try
        {
            await Assert.ThrowsAsync<NotSupportedException>(() =>
                new ZemaxLibraryInstaller().InstallAsync(new ZemaxLibraryInstallOptions(
                    source,
                    examples,
                    library)));

            Assert.False(Directory.Exists(examples));
            Assert.False(Directory.Exists(library));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task InvalidExistingCatalogIsNotOverwrittenAndCreatesNoProject()
    {
        var root = Path.Combine(Path.GetTempPath(), $"zemax-library-index-{Guid.NewGuid():N}");
        var examples = Path.Combine(root, "examples");
        var library = Path.Combine(root, "library");
        var catalogPath = Path.Combine(library, "index.json");
        var source = Path.Combine(AppContext.BaseDirectory, "Samples", "achromatic-doublet.zmx");
        Directory.CreateDirectory(library);
        const string invalidCatalog = "{ this is not a lens catalog }";
        await File.WriteAllTextAsync(catalogPath, invalidCatalog);

        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                new ZemaxLibraryInstaller().InstallAsync(new ZemaxLibraryInstallOptions(
                    source,
                    examples,
                    library)));

            Assert.Equal(invalidCatalog, await File.ReadAllTextAsync(catalogPath));
            Assert.False(Directory.Exists(examples));
            Assert.False(Directory.Exists(Path.Combine(library, "projects")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void SharedEntryFactoryKeepsBatchAndIncrementalStableIdsIdentical()
    {
        Assert.Equal(
            "bundled-industrial-samples-2a4c55ad2e1b",
            LensLibraryCatalogEntryFactory.CreateStableId(
                "bundled-industrial-samples",
                "achromatic-doublet.zmx"));
    }
}

using System.Text.Json;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Coordinates;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.LensLibraryBuilder;

namespace OptilandWorkbench.Tests;

public sealed class LensLibraryTests
{
    [Fact]
    public async Task ReleaseOutputContainsPackagedNativeLensLibrary()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "LensLibrary");
        using var application = WorkbenchApplication.Create(
            lensLibraryDirectory: root);

        var lenses = application.Lenses.GetLenses();
        Assert.Equal(925, lenses.Count);
        Assert.Equal(56, lenses.Count(lens => lens.Category == "显微物镜"));
        Assert.Equal(5, lenses.Count(lens => lens.Category == "工业镜头"));
        Assert.Equal(864, lenses.Count(lens => lens.Category == "Public Zemax Designs"));
        Assert.DoesNotContain(lenses.Where(lens => lens.Category == "显微物镜"), lens =>
            new[] { "管镜", "Tube", "傅里叶", "Fourier", "聚光", "Condenser", "MultiConfig", "显微系统" }
                .Any(token =>
                    lens.Name.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                    lens.SourcePath.Contains(token, StringComparison.OrdinalIgnoreCase)));
        Assert.All(lenses, lens =>
        {
            Assert.EndsWith(".staropt", lens.NativePath, StringComparison.OrdinalIgnoreCase);
            Assert.False(string.IsNullOrWhiteSpace(lens.NumericalApertureBasis));
            Assert.False(string.IsNullOrWhiteSpace(lens.WorkingDistanceBasis));
            Assert.False(string.IsNullOrWhiteSpace(lens.LensType));
            Assert.False(string.IsNullOrWhiteSpace(lens.Application));
            Assert.False(string.IsNullOrWhiteSpace(lens.DesignOrganization));
            Assert.False(string.IsNullOrWhiteSpace(lens.ImporterVersion));
            Assert.True(lens.LensElementCount >= 0);
            Assert.True(lens.MaximumClearAperture >= 0);
        });

        var commercial = application.Lenses.GetCommercialLenses();
        var stockCatalogDirectory = Path.Combine(root, CommercialLensCatalogStore.DirectoryName);
        Assert.Equal(
            new[]
            {
                "Daheng Optics.json",
                "Edmund Optics.json",
                "Newport.json",
                "Sigma Koki.json",
                "Thorlabs.json"
            },
            Directory.EnumerateFiles(stockCatalogDirectory, "*.json")
                .Select(Path.GetFileName)
                .Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(16_289, commercial.Count);
        Assert.Equal(
            new[] { "Daheng Optics", "Edmund Optics", "Newport", "Sigma Koki", "Thorlabs" },
            commercial
                .Select(entry => entry.Manufacturer)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(302, commercial.Count(entry => entry.Manufacturer == "Daheng Optics"));
        Assert.Equal(9_546, commercial.Count(entry => entry.Manufacturer == "Edmund Optics"));
        Assert.Equal(1_556, commercial.Count(entry => entry.Manufacturer == "Newport"));
        Assert.Equal(1_771, commercial.Count(entry => entry.Manufacturer == "Sigma Koki"));
        Assert.Equal(3_114, commercial.Count(entry => entry.Manufacturer == "Thorlabs"));
        Assert.All(commercial, entry =>
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.PartNumber));
            Assert.True(Uri.IsWellFormedUriString(entry.ProductUrl, UriKind.Absolute));
            Assert.False(string.IsNullOrWhiteSpace(entry.LensType));
            Assert.False(string.IsNullOrWhiteSpace(entry.ShapeCode));
            Assert.False(string.IsNullOrWhiteSpace(entry.SurfaceType));
            Assert.True(entry.ElementCount > 0);
            Assert.Null(entry.NativePath);
            Assert.Null(application.Lenses.GetCommercialNativeProjectPath(entry.Id));
        });
        Assert.Empty(Directory.EnumerateFiles(root, "*.zmx", SearchOption.AllDirectories));
        Assert.Empty(Directory.EnumerateFiles(root, "*.zar", SearchOption.AllDirectories));
        Assert.Empty(Directory.EnumerateFiles(root, "*.zip", SearchOption.AllDirectories));
        Assert.Empty(Directory.EnumerateFiles(root, "*.agf", SearchOption.AllDirectories));
        var previewFailures = new List<string>();
        foreach (var lens in lenses)
        {
            try
            {
                Assert.NotNull(await application.Lenses.BuildPreviewAsync(lens.Id));
            }
            catch (Exception exception)
            {
                previewFailures.Add($"{lens.Id}: {exception.Message}");
            }
        }
        Assert.True(
            previewFailures.Count == 0,
            $"Packaged lens previews failed:{Environment.NewLine}{string.Join(Environment.NewLine, previewFailures)}");
    }

    [Fact]
    public async Task PackagedLibraryLoadsNativeProjectsAndPreservesSignedCoordinates()
    {
        var root = Path.Combine(Path.GetTempPath(), $"staropt-lenses-{Guid.NewGuid():N}");
        var projects = Path.Combine(root, "projects");
        Directory.CreateDirectory(projects);

        try
        {
            var microscopePath = Path.Combine(projects, "microscope.staropt");
            var industrialPath = Path.Combine(projects, "industrial.staropt");
            var microscopeOptic = Optic.CreateCookeTriplet();
            microscopeOptic.SurfaceGroup.Items[2].Thickness = -0.0002834;
            microscopeOptic.SurfaceGroup.Items[2].CoordinateSystem = new CoordinateSystem(
                new Vector3D(1.25, -0.5, 7.75),
                2,
                -3,
                4);
            await StarOptProjectStore.SaveAsync(
                new StarOptProjectDocument(new[] { microscopeOptic }, 0),
                microscopePath);
            await StarOptProjectStore.SaveAsync(
                new StarOptProjectDocument(new[] { Optic.CreateTessarLens() }, 0),
                industrialPath);
            var catalog = new LensLibraryCatalogDocument(
                2,
                DateTimeOffset.UtcNow,
                new[]
                {
                    Entry("microscope", "显微镜", "projects/microscope.staropt", microscopeOptic),
                    Entry("industrial", "工业镜头", "projects/industrial.staropt", Optic.CreateTessarLens())
                });
            await File.WriteAllTextAsync(
                Path.Combine(root, "index.json"),
                JsonSerializer.Serialize(catalog));

            using var application = WorkbenchApplication.Create(
                lensLibraryDirectory: root);
            var lenses = application.Lenses.GetLenses();

            Assert.Equal(2, lenses.Count);
            Assert.Contains(lenses, lens => lens.Category == "显微镜");
            Assert.Contains(lenses, lens => lens.Category == "工业镜头");
            Assert.Equal(microscopePath, application.Lenses.GetNativeProjectPath("microscope"));
            Assert.Null(application.Lenses.GetNativeProjectPath("missing"));
            var restored = await StarOptProjectStore.LoadAsync(microscopePath);
            var signedSurface = restored.Configurations[0].SurfaceGroup.Items[2];
            Assert.Equal(-0.0002834, signedSurface.Thickness, precision: 12);
            Assert.Equal(microscopeOptic.SurfaceGroup.Items[2].CoordinateSystem, signedSurface.CoordinateSystem);
            var preview = await application.Lenses.BuildPreviewAsync("microscope");
            Assert.NotNull(preview?.TwoDimensional);
            Assert.NotEmpty(preview!.TwoDimensional!.LensElements);
            Assert.False(Directory.Exists(Path.Combine(root, "originals")));
            Assert.False(Directory.Exists(Path.Combine(root, "extracted")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task NativeProjectPathRejectsCatalogTraversal()
    {
        var container = Path.Combine(Path.GetTempPath(), $"staropt-lenses-traversal-{Guid.NewGuid():N}");
        var root = Path.Combine(container, "library");
        var outsidePath = Path.Combine(container, "outside.staropt");
        Directory.CreateDirectory(root);

        try
        {
            var optic = Optic.CreateCookeTriplet();
            await StarOptProjectStore.SaveAsync(
                new StarOptProjectDocument(new[] { optic }, 0),
                outsidePath);
            var catalog = new LensLibraryCatalogDocument(
                2,
                DateTimeOffset.UtcNow,
                new[] { Entry("outside", "显微镜", "../outside.staropt", optic) });
            await File.WriteAllTextAsync(
                Path.Combine(root, "index.json"),
                JsonSerializer.Serialize(catalog));

            using var application = WorkbenchApplication.Create(
                lensLibraryDirectory: root);

            Assert.Null(application.Lenses.GetNativeProjectPath("outside"));
            Assert.Null(await application.Lenses.BuildPreviewAsync("outside"));
        }
        finally
        {
            Directory.Delete(container, recursive: true);
        }
    }

    [Fact]
    public void OfflineStockCatalogConverterPublishesHeaderEntriesWithoutExtractingPrescriptions()
    {
        var container = Path.Combine(Path.GetTempPath(), $"zemax-stockcat-{Guid.NewGuid():N}");
        var stockCatalog = Path.Combine(container, "source");
        Directory.CreateDirectory(stockCatalog);

        try
        {
            var path = Path.Combine(stockCatalog, "THORLABS.ZMF");
            using (var stream = File.Create(path))
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.Latin1))
            {
                writer.Write((uint)1001);
                WriteZmfRecord(writer, "AC254-100-A", 2, 2, 0, 0, 0, 100.1, 25.4);
                WriteZmfRecord(writer, "ACL25416U-A", 2, 4, 1, 0, 0, 16, 22);
            }

            var entries = StockLensCatalogConverter.ReadFile(path);

            Assert.Equal(2, entries.Count);
            var achromat = Assert.Single(entries, entry => entry.PartNumber == "AC254-100-A");
            Assert.Equal("Thorlabs", achromat.Manufacturer);
            Assert.Equal("B", achromat.ShapeCode);
            Assert.Equal("S", achromat.SurfaceType);
            Assert.Equal(2, achromat.ElementCount);
            Assert.Equal(100.1, achromat.EffectiveFocalLength, precision: 8);
            Assert.Equal(25.4, achromat.EntrancePupilDiameter, precision: 8);
            Assert.Null(achromat.NativePath);
            Assert.Contains("不包含处方正文", achromat.SourceNote, StringComparison.Ordinal);

            var asphere = Assert.Single(entries, entry => entry.PartNumber == "ACL25416U-A");
            Assert.Equal("M", asphere.ShapeCode);
            Assert.Equal("A", asphere.SurfaceType);
        }
        finally
        {
            Directory.Delete(container, recursive: true);
        }
    }

    [Fact]
    public void RuntimeLensLibraryIsReadOnlyAndEmptyCatalogDoesNotCreateFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), $"staropt-lenses-empty-{Guid.NewGuid():N}");
        try
        {
            using var application = WorkbenchApplication.Create(
                lensLibraryDirectory: root);

            Assert.Empty(application.Lenses.GetLenses());
            Assert.Empty(application.Lenses.GetCommercialLenses());
            Assert.False(Directory.Exists(root));
            Assert.DoesNotContain(
                typeof(ILensLibraryService).GetMethods(),
                method => method.Name.Contains("Synchronize", StringComparison.Ordinal));
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
    public void EntryFactoryPublishesComputedAndProvenanceMetadata()
    {
        var optic = Optic.CreateCookeTriplet();
        var importedAt = new DateTimeOffset(2026, 8, 16, 8, 30, 0, TimeSpan.Zero);

        var entry = LensLibraryCatalogEntryFactory.Create(
            "metadata",
            "Metadata lens",
            "工业镜头",
            "S.T.A.R. Labs sample",
            "https://example.invalid/lens",
            "测试许可证",
            "projects/metadata.staropt",
            "metadata.zmx",
            optic,
            "双高斯镜头",
            "工业检测",
            "S.T.A.R. Labs",
            importedAt,
            "test-importer");

        Assert.True(entry.EffectiveFocalLength > 0);
        Assert.True(entry.FNumber > 0);
        Assert.True(entry.NumericalAperture > 0);
        Assert.Equal("像方空气近轴估算", entry.NumericalApertureBasis);
        Assert.True(entry.WorkingDistance >= 0);
        Assert.NotEqual("未提供", entry.WorkingDistanceBasis);
        Assert.True(entry.LensElementCount > 0);
        Assert.True(entry.MaximumClearAperture > 0);
        Assert.Equal("双高斯镜头", entry.LensType);
        Assert.Equal("工业检测", entry.Application);
        Assert.Equal("S.T.A.R. Labs", entry.DesignOrganization);
        Assert.Equal(importedAt, entry.ImportedAt);
        Assert.Equal("test-importer", entry.ImporterVersion);
    }

    private static LensLibraryEntryDto Entry(
        string id,
        string category,
        string nativePath,
        Optic optic)
    {
        return LensLibraryCatalogEntryFactory.Create(
            id,
            optic.Name,
            category,
            "测试镜头库",
            string.Empty,
            "测试",
            nativePath,
            "test.zmx",
            optic,
            importedAt: DateTimeOffset.UnixEpoch,
            importerVersion: "test-importer");
    }

    private static void WriteZmfRecord(
        BinaryWriter writer,
        string name,
        uint elements,
        uint shape,
        uint aspheric,
        uint grin,
        uint toroidal,
        double effectiveFocalLength,
        double entrancePupilDiameter)
    {
        var nameBytes = System.Text.Encoding.Latin1.GetBytes(name);
        var field = new byte[100];
        nameBytes.CopyTo(field, 0);
        writer.Write(field);
        writer.Write((uint)230101);
        writer.Write(elements);
        writer.Write(shape);
        writer.Write(aspheric);
        writer.Write(grin);
        writer.Write(toroidal);
        writer.Write((uint)4);
        writer.Write(effectiveFocalLength);
        writer.Write(entrancePupilDiameter);
        writer.Write(new byte[] { 1, 2, 3, 4 });
    }
}

using System.Text.Json;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Coordinates;
using OptilandWorkbench.Core.Serialization;

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
        Assert.Equal(106, lenses.Count);
        Assert.Equal(56, lenses.Count(lens => lens.Category == "显微物镜"));
        Assert.Equal(5, lenses.Count(lens => lens.Category == "工业镜头"));
        Assert.Equal(45, lenses.Count(lens => lens.Category == "Public Zemax Designs"));
        Assert.DoesNotContain(lenses.Where(lens => lens.Category == "显微物镜"), lens =>
            new[] { "管镜", "Tube", "傅里叶", "Fourier", "聚光", "Condenser", "MultiConfig", "显微系统" }
                .Any(token =>
                    lens.Name.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                    lens.SourcePath.Contains(token, StringComparison.OrdinalIgnoreCase)));
        Assert.All(lenses, lens =>
        {
            Assert.EndsWith(".staropt", lens.NativePath, StringComparison.OrdinalIgnoreCase);
        });
        Assert.Empty(Directory.EnumerateFiles(root, "*.zmx", SearchOption.AllDirectories));
        Assert.Empty(Directory.EnumerateFiles(root, "*.zar", SearchOption.AllDirectories));
        Assert.Empty(Directory.EnumerateFiles(root, "*.zip", SearchOption.AllDirectories));
        Assert.Empty(Directory.EnumerateFiles(root, "*.agf", SearchOption.AllDirectories));
        foreach (var lens in lenses)
        {
            Assert.NotNull(await application.Lenses.BuildPreviewAsync(lens.Id));
        }
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
                1,
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
                1,
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
    public void RuntimeLensLibraryIsReadOnlyAndEmptyCatalogDoesNotCreateFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), $"staropt-lenses-empty-{Guid.NewGuid():N}");
        try
        {
            using var application = WorkbenchApplication.Create(
                lensLibraryDirectory: root);

            Assert.Empty(application.Lenses.GetLenses());
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

    private static LensLibraryEntryDto Entry(
        string id,
        string category,
        string nativePath,
        Optic optic)
    {
        var wavelengths = optic.Wavelengths.Select(wavelength => wavelength.Nanometers).ToArray();
        return new LensLibraryEntryDto(
            id,
            optic.Name,
            category,
            "测试镜头库",
            string.Empty,
            "测试",
            "STAROPT",
            "可用",
            null,
            optic.Paraxial.EstimateEffectiveFocalLength(),
            optic.Paraxial.EstimateFNumber(),
            optic.Aperture.Kind.ToString(),
            optic.Aperture.Value,
            optic.SurfaceGroup.TotalTrack,
            optic.SurfaceGroup.Items.Count,
            optic.FieldDefinition.ToString(),
            optic.Fields.Select(field => Math.Sqrt((field.X * field.X) + (field.Y * field.Y))).Max(),
            optic.Fields.Count,
            wavelengths.Length,
            wavelengths.Min(),
            wavelengths.Max(),
            nativePath,
            string.Empty);
    }
}

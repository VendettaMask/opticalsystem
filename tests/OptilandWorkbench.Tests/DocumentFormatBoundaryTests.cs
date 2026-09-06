using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Serialization;

namespace OptilandWorkbench.Tests;

public sealed class DocumentFormatBoundaryTests
{
    [Theory]
    [InlineData(".optiland-python.json")]
    [InlineData(".python-optiland.json")]
    [InlineData(".OPTILAND-PYTHON.JSON")]
    [InlineData(".PYTHON-OPTILAND.JSON")]
    public async Task RetiredExtensionsAreRejectedBeforeReadingOrOverwriting(string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), $"retired-{Guid.NewGuid():N}{extension}");
        Assert.False(WorkbenchRuntime.IsNativeJsonPath(path));
        Assert.Throws<NotSupportedException>(() => WorkbenchRuntime.FormatNameForPath(path));
        await Assert.ThrowsAsync<NotSupportedException>(() => WorkbenchRuntime.ReadDocumentAsync(path));
        try
        {
            // Even a valid project renamed to a retired suffix must not bypass the boundary via magic sniffing.
            await StarOptProjectStore.SaveAsync(new StarOptProjectDocument(new[] { Optic.CreateCookeTriplet() }, 0), path);
            var original = await File.ReadAllBytesAsync(path);
            await Assert.ThrowsAsync<NotSupportedException>(() => WorkbenchRuntime.ReadDocumentAsync(path));
            await Assert.ThrowsAsync<NotSupportedException>(() => WorkbenchRuntime.SaveOpticAsync(Optic.CreateTessarLens(), path));
            Assert.Equal(original, await File.ReadAllBytesAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(".staropt", "staropt-project")]
    [InlineData(".ZMX", "zemax-zmx")]
    [InlineData(".SEQ", "codev-seq-subset")]
    [InlineData(".LEN", "oslo-len-subset")]
    [InlineData(".optic.json", "native-json")]
    [InlineData(".optiland.json", "native-json")]
    [InlineData(".json", "native-json")]
    [InlineData(".optiland", "native-json")]
    public async Task SupportedDocumentRoutesStillReadAndWrite(string extension, string formatName)
    {
        var path = Path.Combine(Path.GetTempPath(), $"document-{Guid.NewGuid():N}{extension}");
        var original = Optic.CreateCookeTriplet();
        try
        {
            Assert.Equal(formatName, WorkbenchRuntime.FormatNameForPath(path));
            await WorkbenchRuntime.SaveOpticAsync(original, path);
            var restored = await WorkbenchRuntime.ReadOpticAsync(path);
            Assert.Equal(original.SurfaceGroup.Items.Count, restored.SurfaceGroup.Items.Count);
            for (var i = 1; i < original.SurfaceGroup.Items.Count; i++)
            {
                var radius = original.SurfaceGroup.Items[i].Radius;
                var restoredRadius = restored.SurfaceGroup.Items[i].Radius;
                if (double.IsFinite(radius))
                {
                    // ZMX stores a decimal curvature, so its inverse is not a bitwise radius round trip.
                    Assert.InRange(Math.Abs(radius - restoredRadius), 0, 1e-7 * Math.Max(1, Math.Abs(radius)));
                }
                else
                {
                    Assert.Equal(radius, restoredRadius);
                }
                Assert.Equal(original.SurfaceGroup.Items[i].Thickness, restored.SurfaceGroup.Items[i].Thickness);
            }
            Assert.Equal(original.SurfaceGroup.Items.Count, restored.TraceGeneric(0, 0, 0, 0, 0.5876).RayHistories.Single().Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("{\"surface_group\":{\"surfaces\":[]},\"fields\":{},\"wavelengths\":{}}")]
    [InlineData("{}")]
    public async Task ForeignDictionariesRenamedToGenericJsonAreNotNativeSnapshots(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"foreign-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(path, json);
            await Assert.ThrowsAsync<InvalidDataException>(() => OpticJsonStore.LoadAsync(path));
            await Assert.ThrowsAsync<InvalidDataException>(() => WorkbenchRuntime.ReadDocumentAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}

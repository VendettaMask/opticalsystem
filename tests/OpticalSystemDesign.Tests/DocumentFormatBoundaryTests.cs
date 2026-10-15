using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Serialization;

namespace OpticalSystemDesign.Tests;

public sealed class DocumentFormatBoundaryTests
{
    [Theory]
    [MemberData(nameof(RetiredExtensions))]
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
    [MemberData(nameof(SupportedExtensions))]
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

    public static IEnumerable<object[]> RetiredExtensions => new[]
        {
            ".json", ".legacy.json", "." + RetiredProductFixture.Brand.ToLowerInvariant(),
            "." + RetiredProductFixture.Brand.ToLowerInvariant() + ".json",
            "." + RetiredProductFixture.Brand.ToLowerInvariant() + "-python.json",
            ".python-" + RetiredProductFixture.Brand.ToLowerInvariant() + ".json"
        }
        .SelectMany(extension => new[] { new object[] { extension }, new object[] { extension.ToUpperInvariant() } });

    public static IEnumerable<object[]> SupportedExtensions => new[]
    {
        new object[] { ".staropt", "staropt-project" }, new object[] { ".ZMX", "zemax-zmx" },
        new object[] { ".SEQ", "codev-seq-subset" }, new object[] { ".LEN", "oslo-len-subset" },
        new object[] { ".optic.json", "native-json" }
    };

    [Theory]
    [InlineData("{\"surface_group\":{\"surfaces\":[]},\"fields\":{},\"wavelengths\":{}}")]
    [InlineData("{}")]
    public async Task ForeignDictionariesRenamedToCurrentSnapshotSuffixAreNotNativeSnapshots(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"foreign-{Guid.NewGuid():N}.optic.json");
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

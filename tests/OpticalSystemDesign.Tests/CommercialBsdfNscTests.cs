using System.IO.Compression;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.NonSequential;
using OpticalSystemDesign.Core.Scattering;
using OpticalSystemDesign.Core.Serialization;

namespace OpticalSystemDesign.Tests;

public sealed class CommercialBsdfNscTests
{
    internal static NonSequentialDocument Scene(bool pole = false)
    {
        var scene = MeasuredBsdfNscTests.Scene(.2, .5, 4096); var source = scene.Objects[0]; var plane = scene.Objects[1];
        scene.Replace(source.Id, source with { Parameters = new SourcePointParameters(1e-10, 1, 0, 20, 4096) });
        scene.Replace(plane.Id, plane with { SurfaceBsdf = new(NonSequentialBsdfKind.Measured, 0, CommercialBsdfTests.FixedData(), pole ? new(0, 0, 1) : new()) });
        return scene;
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void FixedAxisAndPoleFallbackFollowTheWholeReferenceCoordinateChain(bool pole)
    {
        var scene = Scene(pole); var materials = Optic.CreateBlank().Materials; var tracer = new NonSequentialDocumentTracer();
        var original = tracer.Trace(scene, materials);
        var parent = NonSequentialObjectDefinition.Create(NonSequentialObjectKind.PlaneRectangle) with
        { Enabled = false, LocalCoordinateSystem = new(new(3, 4, 5), 37, 24, 90) };
        scene.Insert(scene.Objects.Count, parent);
        foreach (var item in scene.Objects.Where(o => o.Id != parent.Id).ToArray()) scene.Replace(item.Id, item with { ReferenceObjectId = parent.Id });
        var rotated = tracer.Trace(scene, materials);
        Assert.Equal(original.Branches.Count, rotated.Branches.Count);
        for (var k = 0; k < original.Branches.Count; k++)
        {
            var expected = scene.ToWorldDirection(parent.Id, original.Branches[k].Segments[0].OutgoingDirection);
            Assert.InRange((expected - rotated.Branches[k].Segments[0].OutgoingDirection).Length, 0, 2e-12);
        }
        var e = rotated.EnergyBalance;
        Assert.InRange(Math.Abs(e.AccountedPowerWatts / e.SourcePowerWatts - 1), 0, 1e-12);
        Assert.InRange(Math.Abs(e.AbsorbedPowerWatts / e.SourcePowerWatts - .3), 0, 1e-12);
        Assert.InRange(Math.Abs(e.DetectorPowerWatts - original.EnergyBalance.DetectorPowerWatts) / e.SourcePowerWatts, 0, 1e-12);
        var a = original.Detectors[0].PowerByWavelength[1]; var b = rotated.Detectors[0].PowerByWavelength[1];
        double maxPixelError = 0;
        for (var k = 0; k < a.Count; k++)
        {
            maxPixelError = Math.Max(maxPixelError, Math.Abs(a[k] - b[k]));
            Assert.InRange(Math.Abs(a[k] - b[k]), 0, 1e-12 * Math.Max(a[k], e.SourcePowerWatts / 4096));
        }
        var s = rotated.BsdfStatistics!; Assert.Equal(4096, s.MaterialAxisSampleCount);
        Assert.InRange(s.MaterialDirectionXSum / s.MaterialAxisSampleCount, .04, .14);
        Assert.InRange(Math.Abs(s.MaterialDirectionYSum / s.MaterialAxisSampleCount), 0, .05);
        CommercialBsdfTests.Record("nsc-covariance-" + pole, new { e, s, MaximumPixelPowerErrorWatts = maxPixelError, MaximumDirectionErrorTolerance = 2e-12 });
    }

    [Fact]
    public async Task DatabaseProjectVersionTenAndSceneIdentityFreezeTheMaterialAxisAndImportMetadata()
    {
        var scene = Scene(); var materials = Optic.CreateBlank().Materials;
        using var stream = new MemoryStream();
        using (var writer = new NonSequentialRayDatabaseWriter(stream, NonSequentialRayDatabaseHeader.Create(scene, materials: materials), true))
        { new NonSequentialDocumentTracer().Trace(scene, materials, sink: writer); writer.Complete(); }
        stream.Position = 0; using var reader = new NonSequentialRayDatabaseReader(stream);
        Assert.Equal(MeasuredBsdfData.FixedAxisProtocol, reader.Header.SurfaceBsdfProtocol);
        Assert.NotEmpty(reader.ReadBranches(NonSequentialPathFilter.Parse("B2 & T2 & D3")));
        using var files = new CameraSensorFileTests.TemporaryFiles(); var path = files.Path("fixed.staropt");
        await StarOptProjectStore.SaveAsync(new([Optic.CreateBlank()], 0, NonSequentialDocument: scene), path);
        var opened = (await StarOptProjectStore.LoadAsync(path)).NonSequentialDocument!;
        Assert.Equal(NonSequentialSceneHasher.Compute(scene, materials), NonSequentialSceneHasher.Compute(opened, materials));
        Assert.Equal(scene.Objects[1].SurfaceBsdf!.MeasuredData!.DataSha256, opened.Objects[1].SurfaceBsdf!.MeasuredData!.DataSha256);
        Assert.Equal(new BsdfMaterialAxis(), opened.Objects[1].SurfaceBsdf!.MaterialAxis);
        using var raw = File.OpenRead(path); raw.Position = 52; using var decompressor = new BrotliStream(raw, CompressionMode.Decompress);
        using var json = await JsonDocument.ParseAsync(decompressor); Assert.Equal(10, json.RootElement.GetProperty("FormatVersion").GetInt32());
        var plane = scene.Objects[1]; scene.Replace(plane.Id, plane with { SurfaceBsdf = plane.SurfaceBsdf! with { MaterialAxis = new(0, 1, 0) } });
        Assert.True(reader.IsStale(scene, materials));
        Assert.Throws<ArgumentException>(() => scene.Replace(plane.Id, plane with { SurfaceBsdf = plane.SurfaceBsdf! with { MaterialAxis = new(0, 0, 0) } }));
    }
}

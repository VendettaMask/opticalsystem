using System.IO.Compression;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.NonSequential;
using OpticalSystemDesign.Core.Scattering;
using OpticalSystemDesign.Core.Serialization;

namespace OpticalSystemDesign.Tests;

public sealed class SpeosV11NscTests
{
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task NewBtdfModelsUpdateMediumAndUseVersionTwelve(bool speos)
    {
        var scene = CommercialBsdfNscTests.Scene(); var source = scene.Objects[0]; var plane = scene.Objects[1];
        using var calibrationFiles = new CameraSensorFileTests.TemporaryFiles(); var inputPath = calibrationFiles.Path("xyz.bsdf"); var calibrationPath = calibrationFiles.Path("calibration.json");
        File.WriteAllText(inputPath, speos ? SpeosV11XyzTests.SpeosText(true, incidenceAngles: [0, 15, 30], ratio: 1 / 1.5) : SpeosV11XyzTests.XyzText(true, incidences: [0, 30])); File.WriteAllText(calibrationPath, SpeosV11XyzTests.Calibration());
        var data = BsdfFileImporter.Read(inputPath, BinaryZemaxBsdfTests.Options with { TransmittedIndex = 1.5, ZemaxXyzCalibrationPath = speos ? null : calibrationPath, SpeosPeak = speos ? new(14, .01, 550) : null });
        scene = new(scene.Name, scene.Wavelengths, scene.Objects, "Vacuum");
        scene.Replace(plane.Id, plane with { Parameters = new PlaneRectangleParameters(1, 1, NonSequentialSurfaceBehavior.Refractive, "dense", "Vacuum"), SurfaceBsdf = new(NonSequentialBsdfKind.Measured, 0, data) });
        var materials = Optic.CreateBlank().Materials; materials.Register(new ConstantIndexMaterial("dense", 1.5));
        using var stream = new MemoryStream(); NonSequentialDocumentTraceResult result;
        using (var writer = new NonSequentialRayDatabaseWriter(stream, NonSequentialRayDatabaseHeader.Create(scene, materials: materials), true))
        { result = new NonSequentialDocumentTracer().Trace(scene, materials, sink: writer); writer.Complete(); }
        Assert.InRange(Math.Abs(result.EnergyBalance.AbsorbedPowerWatts / result.EnergyBalance.SourcePowerWatts - .7), 0, 1e-12);
        Assert.InRange(Math.Abs(result.EnergyBalance.AccountedPowerWatts / result.EnergyBalance.SourcePowerWatts - 1), 0, 1e-12);
        Assert.Equal(4096, result.BsdfStatistics!.TransmissionSampleCount);
        var view = 4 / (Math.PI * Math.Sqrt(2)) * Math.Atan(1 / Math.Sqrt(2));
        if (!speos) Assert.InRange(Math.Abs(result.EnergyBalance.DetectorPowerWatts / result.EnergyBalance.SourcePowerWatts - .3 * view), 0, .3 * 6 * Math.Sqrt(view * (1 - view) / 4096));
        foreach (var branch in result.Branches.Where(b => b.Segments.Count == 2))
        { var s = branch.Segments; Assert.Equal(1.5 * s[1].SegmentLength, s[1].CumulativeOpticalPathLength - s[0].CumulativeOpticalPathLength, 10); }
        stream.Position = 0; using var reader = new NonSequentialRayDatabaseReader(stream); Assert.Equal(MeasuredBsdfData.EnhancedProtocol, reader.Header.SurfaceBsdfProtocol); Assert.NotEmpty(reader.ReadBranches(NonSequentialPathFilter.Parse("B2 & D3")));
        using var files = new CameraSensorFileTests.TemporaryFiles(); var path = files.Path("specular.staropt");
        await StarOptProjectStore.SaveAsync(new([Optic.CreateBlank()], 0, NonSequentialDocument: scene), path);
        var restored = (await StarOptProjectStore.LoadAsync(path)).NonSequentialDocument!; Assert.Equal(data.DataSha256, restored.Objects[1].SurfaceBsdf!.MeasuredData!.DataSha256);
        Assert.Equal(NonSequentialSceneHasher.Compute(scene, materials), NonSequentialSceneHasher.Compute(restored, materials));
        using var raw = File.OpenRead(path); raw.Position = 52; using var unzip = new BrotliStream(raw, CompressionMode.Decompress); using var json = await JsonDocument.ParseAsync(unzip); Assert.Equal(12, json.RootElement.GetProperty("FormatVersion").GetInt32());
        var downgraded = System.Text.Json.Nodes.JsonNode.Parse(json.RootElement.GetRawText())!; downgraded["FormatVersion"] = 11;
        var payload = System.Text.Encoding.UTF8.GetBytes(downgraded.ToJsonString()); using var packed = new MemoryStream(); using (var compressor = new BrotliStream(packed, CompressionLevel.Optimal, true)) compressor.Write(payload);
        var header = (await File.ReadAllBytesAsync(path))[..52]; System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(12), payload.Length); System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(16), (int)packed.Length); System.Security.Cryptography.SHA256.HashData(payload).CopyTo(header, 20);
        var rejection = Assert.Throws<InvalidDataException>(() => StarOptProjectStore.Deserialize(header.Concat(packed.ToArray()).ToArray())); Assert.Contains("version 12", rejection.Message);
        SpeosV11XyzTests.Record("nsc-medium-database-project-" + speos, new { result.EnergyBalance, result.BsdfStatistics, ProjectVersion = 12, ExactModelDigest = true });
    }
}


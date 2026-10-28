using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using SkiaSharp;

namespace OpticalSystemDesign.Tests;

public sealed class CameraMaterialServiceTests
{
    internal static string Example => Path.Combine(CameraSensorFileTests.Examples, "three-materials.camera-scene.json");
    internal static async Task<CameraImagingRequest> Request(ICameraImagingService service) => (await CameraContinuousServiceTests.Request(service)) with
    { SceneMode = CameraSceneMode.MaterialMap, MaterialScene = await service.ImportMaterialSceneAsync(Example), Reflectance = null };

    [Fact]
    public async Task SceneRoundTripsAndABFreezeMapCurvesOriginsAndExportReimportableData()
    {
        using var files = new CameraSensorFileTests.TemporaryFiles(); using var app = WorkbenchApplication.Create();
        var request = await Request(app.CameraImaging); var scene = request.MaterialScene!;
        Assert.Equal(3, scene.Materials.Count); Assert.Equal(48, scene.MaterialIds.Count);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Example))), scene.Origin!.Sha256);
        var saved = files.Path("scene.json"); await app.CameraImaging.SaveMaterialSceneAsync(scene, saved);
        var reloaded = await app.CameraImaging.ImportMaterialSceneAsync(saved);
        Assert.Equal(scene.MaterialIds, reloaded.MaterialIds); Assert.Equal(scene.Materials[0].Reflectance!.Samples, reloaded.Materials[0].Reflectance!.Samples);
        var callerLabels = scene.MaterialIds.ToArray(); var callerCurve = scene.Materials[0].Reflectance!.Samples.ToArray();
        var materials = scene.Materials.Select(m => m.Id == 0 ? m with { Reflectance = m.Reflectance! with { Samples = callerCurve } } : m).ToArray();
        request = request with { MaterialScene = scene with { MaterialIds = callerLabels, Materials = materials } };
        var a = await app.CameraImaging.SimulateAsync(request);
        callerLabels[0] = 2; callerCurve[0] = callerCurve[0] with { Value = 1 };
        Assert.Equal(0, a.Request.MaterialScene!.MaterialIds[0]); Assert.Equal(.05, a.Request.MaterialScene.Materials[0].Reflectance!.Samples[0].Value);
        var uniform = await app.CameraImaging.SimulateAsync(request with { SceneMode = CameraSceneMode.Uniform,
            Reflectance = await app.CameraImaging.ImportReflectanceAsync(Path.Combine(CameraSensorFileTests.Examples, "reflectance-neutral.csv")) });
        Assert.Null(uniform.Integration!.Materials); Assert.Equal(3, a.Integration!.Materials!.Count);
        Assert.Equal(256, a.Integration.Materials.Sum(m => m.SampledPixels)); Assert.Contains("Label-area", a.Integration.ReflectanceMeaning);
        var output = files.Path("materials-AB.zip"); await app.CameraImaging.ExportAsync(uniform, output, a);
        using (var zip = ZipFile.OpenRead(output))
        {
            Assert.NotNull(zip.GetEntry("A/material-00-reflectance.csv")); Assert.Null(zip.GetEntry("A/reflectance.csv"));
            Assert.NotNull(zip.GetEntry("B/reflectance.csv")); Assert.Null(zip.GetEntry("B/material-scene.json"));
            using (var integration = new StreamReader(zip.GetEntry("A/integration.csv")!.Open()))
                Assert.Contains("label_area_mean_reflectance", await integration.ReadLineAsync());
            zip.GetEntry("A/material-scene.json")!.ExtractToFile(files.Path("restored.json"));
            zip.GetEntry("A/material-map.png")!.ExtractToFile(files.Path("labels.png"));
            var restored = await app.CameraImaging.ImportMaterialSceneAsync(files.Path("restored.json"));
            var labels = await app.CameraImaging.ImportMaterialSceneAsync(files.Path("labels.png"));
            Assert.Equal(scene.MaterialIds, labels.MaterialIds); Assert.All(labels.Materials, m => Assert.Null(m.Reflectance));
            var rerun = await app.CameraImaging.SimulateAsync(request with { MaterialScene = restored }); Assert.Equal(a.RawDn, rerun.RawDn);
            using var metadata = new StreamReader(zip.GetEntry("A/metadata.json")!.Open()); var text = await metadata.ReadToEndAsync();
            Assert.Contains("CameraLabV4-Materials", text); Assert.Contains(scene.Origin.Sha256, text); Assert.Contains("SampledPixels", text);
        }
        var evidence = Environment.GetEnvironmentVariable("OPTICAL_CAMERA_EVIDENCE_DIR");
        if (!string.IsNullOrWhiteSpace(evidence)) { Directory.CreateDirectory(evidence); File.Copy(output, Path.Combine(evidence, "materials-AB.zip"), true); }
    }

    [Theory]
    [InlineData("missing")] [InlineData("unknown")] [InlineData("duplicate-id")] [InlineData("unknown-label")]
    [InlineData("kind")] [InlineData("dimensions")] [InlineData("version")] [InlineData("duplicate-key")]
    public async Task InvalidSceneSchemasAndLabelAssignmentsAreRejected(string kind)
    {
        using var files = new CameraSensorFileTests.TemporaryFiles(); using var app = WorkbenchApplication.Create();
        var node = JsonNode.Parse(File.ReadAllText(Example))!;
        switch (kind)
        {
            case "missing": node.AsObject().Remove("MaterialIds"); break;
            case "unknown": node["MaterialIdz"] = new JsonArray(0); break;
            case "duplicate-id": node["Materials"]![1]!["Id"] = 0; break;
            case "unknown-label": node["MaterialIds"]![0] = 9; break;
            case "kind": node["Materials"]![0]!["Reflectance"]!["Kind"] = "Illuminant"; break;
            case "dimensions": node["Width"] = 1000; break;
            case "version": node["SchemaVersion"] = 2; break;
        }
        var text = node.ToJsonString(); if (kind == "duplicate-key") text = text.Replace("\"SchemaVersion\":1", "\"SchemaVersion\":1,\"schemaversion\":1");
        var path = files.Path("invalid.json"); File.WriteAllText(path, text);
        await Assert.ThrowsAsync<InvalidDataException>(() => app.CameraImaging.ImportMaterialSceneAsync(path));
    }

    [Theory]
    [InlineData(1, 1, 1, 255, true)] [InlineData(16, 16, 16, 255, false)]
    [InlineData(0, 1, 0, 255, false)] [InlineData(0, 0, 0, 128, false)]
    public async Task PngUsesExactOpaqueEightBitGrayIdsAndNeverGuessesPhotoMaterials(byte r, byte g, byte b, byte alpha, bool valid)
    {
        using var files = new CameraSensorFileTests.TemporaryFiles(); using var app = WorkbenchApplication.Create();
        var path = files.Path("labels.png");
        using (var bitmap = new SKBitmap(2, 1, SKColorType.Rgba8888, SKAlphaType.Unpremul))
        {
            bitmap.Erase(new SKColor(r, g, b, alpha)); using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            var encoded = png.ToArray(); using var tagged = new MemoryStream(); tagged.Write(encoded.AsSpan(0, 33));
            // Labels are data: a gamma declaration must not turn ID 1 into a different gray value.
            WritePngChunk(tagged, "gAMA", new byte[] { 0, 1, 134, 160 }); tagged.Write(encoded.AsSpan(33));
            File.WriteAllBytes(path, tagged.ToArray());
        }
        if (!valid) { await Assert.ThrowsAsync<InvalidDataException>(() => app.CameraImaging.ImportMaterialSceneAsync(path)); return; }
        var scene = await app.CameraImaging.ImportMaterialSceneAsync(path);
        Assert.Equal(new[] { 1, 1 }, scene.MaterialIds); Assert.Null(scene.Materials.Single().Reflectance);
        var request = await CameraContinuousServiceTests.Request(app.CameraImaging);
        await Assert.ThrowsAsync<ArgumentException>(() => app.CameraImaging.SimulateAsync(request with { SceneMode = CameraSceneMode.MaterialMap, MaterialScene = scene }));
    }

    [Fact]
    public async Task OneAssignedMaterialMatchesUniformSharedLensPropagation()
    {
        using var app = WorkbenchApplication.Create("cooke"); var before = app.Documents.GetSnapshot();
        var request = (await CameraContinuousServiceTests.Request(app.CameraImaging)) with { Optics = CameraOptics.CurrentLens, Aberration = CameraAberration.Geometric };
        var scene = new CameraMaterialScene(CameraMaterialScene.FormatIdentifier, 1, "One material", "Analytic linearity control", 1, 1,
            new[] { 3 }, new[] { new CameraSceneMaterial(3, "Neutral", "Synthetic", request.Reflectance) });
        var uniform = await app.CameraImaging.SimulateAsync(request);
        var segmented = await app.CameraImaging.SimulateAsync(request with { SceneMode = CameraSceneMode.MaterialMap, MaterialScene = scene, Reflectance = null });
        var error = uniform.PhotoElectrons.Zip(segmented.PhotoElectrons, (a, b) => Math.Abs(a - b)).Max();
        Assert.InRange(error, 0, 1e-8); Assert.Equal(uniform.RawDn, segmented.RawDn); Assert.Equal(before, app.Documents.GetSnapshot());
        Assert.Equal(9, segmented.Integration!.Nodes.Count); Assert.Equal(256, segmented.Integration.Materials!.Single().SampledPixels);
        CameraContinuousSpectrumTests.Record("material-shared-lens-linearity", new { MaximumChargeDifference = error,
            IdenticalRaw = uniform.RawDn.SequenceEqual(segmented.RawDn), PrescriptionUnchanged = before == app.Documents.GetSnapshot(), Nodes = segmented.Integration.Nodes.Count });
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task SixteenBitAndAnimatedPngAreRejectedBeforeAnyIdConversion(bool animation)
    {
        using var files = new CameraSensorFileTests.TemporaryFiles(); using var app = WorkbenchApplication.Create();
        var path = files.Path("unsafe-labels.png");
        using var output = new MemoryStream(); output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13]; System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), 1);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), 1); header[8] = animation ? (byte)8 : (byte)16;
        WritePngChunk(output, "IHDR", header);
        if (animation) WritePngChunk(output, "acTL", new byte[] { 0, 0, 0, 2, 0, 0, 0, 0 });
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionMode.Compress, leaveOpen: true)) zlib.Write(animation ? new byte[] { 0, 1 } : new byte[] { 0, 0, 1 });
        WritePngChunk(output, "IDAT", compressed.ToArray()); WritePngChunk(output, "IEND", Array.Empty<byte>());
        File.WriteAllBytes(path, output.ToArray());
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => app.CameraImaging.ImportMaterialSceneAsync(path));
        Assert.Contains(animation ? "动画" : "8 位", error.Message);
    }

    private static void WritePngChunk(Stream stream, string type, byte[] data)
    {
        var length = new byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, data.Length); stream.Write(length);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type); stream.Write(typeBytes); stream.Write(data);
        uint crc = uint.MaxValue;
        foreach (var value in typeBytes.Concat(data))
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(length, ~crc); stream.Write(length);
    }

    [Fact]
    public async Task MissingCoverageCancellationAndOversizedInputRemainSafe()
    {
        using var files = new CameraSensorFileTests.TemporaryFiles(); using var app = WorkbenchApplication.Create();
        var request = await Request(app.CameraImaging);
        await Assert.ThrowsAsync<ArgumentException>(() => app.CameraImaging.SimulateAsync(request with { MaterialScene = null }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => app.CameraImaging.SimulateAsync(request with { Integration = new(350, 700) }));
        var path = files.Path("preserved.json"); File.WriteAllText(path, "preserve");
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => app.CameraImaging.SaveMaterialSceneAsync(request.MaterialScene!, path, cts.Token));
        Assert.Equal("preserve", File.ReadAllText(path));
        var huge = files.Path("huge.json"); using (var stream = File.Create(huge)) stream.SetLength(8 * 1024 * 1024 + 1);
        await Assert.ThrowsAsync<InvalidDataException>(() => app.CameraImaging.ImportMaterialSceneAsync(huge));
    }
}

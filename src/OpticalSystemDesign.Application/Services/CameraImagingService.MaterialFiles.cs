using System.Text.Json;
using System.Buffers.Binary;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Core.Analysis.Imaging;
using OpticalSystemDesign.Core.FileIO;
using SkiaSharp;

namespace OpticalSystemDesign.Application.Services;

internal sealed partial class CameraImagingService
{
    private const int MaximumMaterialFileBytes = 8 * 1024 * 1024;
    private static readonly JsonSerializerOptions MaterialJsonOptions = new(SensorJsonOptions) { WriteIndented = false };

    public async Task<CameraMaterialScene> ImportMaterialSceneAsync(string path, CancellationToken cancellationToken = default)
    {
        var bytes = await BoundedFile.ReadAllBytesAsync(path, MaximumMaterialFileBytes, "Camera material scene", cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            CameraMaterialScene scene;
            if (Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase)) scene = ReadMaterialPng(path, bytes, cancellationToken);
            else
            {
                var payload = bytes.AsMemory();
                if (payload.Length >= 3 && payload.Span[0] == 0xEF && payload.Span[1] == 0xBB && payload.Span[2] == 0xBF) payload = payload[3..];
                using var document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 16 });
                CheckDuplicateProperties(document.RootElement);
                RequireProperties(document.RootElement, new[] { "Format", "SchemaVersion", "Name", "Source", "Width", "Height", "MaterialIds", "Materials" });
                var labels = Property(document.RootElement, "MaterialIds");
                if (labels.ValueKind != JsonValueKind.Array || labels.GetArrayLength() > CameraSensorPipeline.MaximumPixels)
                    throw new InvalidDataException("材质标签数组无效或超过 262144 像元。");
                var materials = Property(document.RootElement, "Materials");
                if (materials.ValueKind != JsonValueKind.Array || materials.GetArrayLength() > SpectralMaterialScene.MaximumMaterials)
                    throw new InvalidDataException("材质定义必须是最多 16 项的数组。");
                foreach (var material in materials.EnumerateArray())
                {
                    RequireProperties(material, new[] { "Id", "Name", "Source", "Reflectance" });
                    var curve = Property(material, "Reflectance");
                    if (curve.ValueKind != JsonValueKind.Null)
                    {
                        RequireProperties(curve, new[] { "Name", "Source", "Kind", "Samples" });
                        var samples = Property(curve, "Samples");
                        if (samples.ValueKind != JsonValueKind.Array || samples.GetArrayLength() > SensorSpectralResponse.MaximumSamples)
                            throw new InvalidDataException("反射率采样数组无效。");
                        foreach (var sample in samples.EnumerateArray()) RequireProperties(sample, new[] { "WavelengthNanometers", "Value" });
                    }
                }
                scene = JsonSerializer.Deserialize<CameraMaterialScene>(payload.Span, SensorJsonOptions)
                    ?? throw new InvalidDataException("材质场景为空。");
            }
            cancellationToken.ThrowIfCancellationRequested();
            return FreezeMaterialScene(scene)! with { Origin = Origin(path, bytes) };
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        { throw new InvalidDataException("材质分区导入失败：" + ex.Message, ex); }
    }

    public Task SaveMaterialSceneAsync(CameraMaterialScene scene, string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var frozen = FreezeMaterialScene(scene) ?? throw new ArgumentNullException(nameof(scene));
        return BoundedFile.WriteAllBytesAtomicAsync(path, JsonSerializer.SerializeToUtf8Bytes(frozen, MaterialJsonOptions),
            MaximumMaterialFileBytes, "Camera material scene", cancellationToken);
    }

    private static CameraMaterialScene ReadMaterialPng(string path, byte[] bytes, CancellationToken token)
    {
        ValidateMaterialPngHeader(bytes);
        using var data = SKData.CreateCopy(bytes); using var codec = SKCodec.Create(data);
        if (codec is null || codec.EncodedFormat != SKEncodedImageFormat.Png) throw new InvalidDataException("分区图必须是 PNG。");
        var width = codec.Info.Width; var height = codec.Info.Height;
        if (width is < 1 or > 1024 || height is < 1 or > 1024 || (long)width * height > CameraSensorPipeline.MaximumPixels)
            throw new InvalidDataException("材质标签图每边最多 1024，最多 262144 像元。");
        // Skia reports zero frames for a non-animated image.
        if (codec.FrameCount > 1) throw new InvalidDataException("只支持单帧 PNG 标签图。");
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success) throw new InvalidDataException("PNG 解码失败。");
        var labels = new int[width * height];
        for (var y = 0; y < height; y++)
        {
            token.ThrowIfCancellationRequested();
            for (var x = 0; x < width; x++)
            {
                var color = bitmap.GetPixel(x, y);
                if (color.Alpha != 255 || color.Red != color.Green || color.Red != color.Blue || color.Red >= SpectralMaterialScene.MaximumMaterials)
                    throw new InvalidDataException("PNG 必须是不透明灰度标签：R=G=B=材质 ID（0–15），不将普通照片或显示色自动分类。");
                labels[y * width + x] = color.Red;
            }
        }
        return new(CameraMaterialScene.FormatIdentifier, 1, Path.GetFileNameWithoutExtension(path), "PNG categorical material labels; spectra unassigned",
            width, height, labels, labels.Distinct().Order().Select(id => new CameraSceneMaterial(id, "材质 " + id, "User label; spectrum unassigned")).ToArray());
    }

    private static void ValidateMaterialPngHeader(byte[] bytes)
    {
        if (bytes.Length < 26 || !bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8) || bytes[24] != 8)
            throw new InvalidDataException("分区 PNG 必须是 8 位数据，避免 16 位标签解码降位后被误认。");
        for (var offset = 8; offset <= bytes.Length - 12;)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));
            if (length > bytes.Length - offset - 12) throw new InvalidDataException("PNG 数据块长度无效。");
            if (bytes.AsSpan(offset + 4, 4).SequenceEqual("acTL"u8)) throw new InvalidDataException("材质标签不支持动画 PNG。");
            offset += (int)length + 12;
        }
    }

    private static CameraMaterialScene? FreezeMaterialScene(CameraMaterialScene? scene)
    {
        if (scene is null) return null;
        if (scene.Format != CameraMaterialScene.FormatIdentifier || scene.SchemaVersion != 1)
            throw new ArgumentException("不支持的材质场景格式或版本。");
        Label(scene.Name, 256, "Scene name", allowEmpty: false); Label(scene.Source, 2048, "Scene source"); ValidateOrigin(scene.Origin);
        if (scene.Width is < 1 or > 1024 || scene.Height is < 1 or > 1024 || (long)scene.Width * scene.Height > CameraSensorPipeline.MaximumPixels
            || scene.MaterialIds is null || scene.MaterialIds.Count != scene.Width * scene.Height
            || scene.Materials is null || scene.Materials.Count is < 1 or > SpectralMaterialScene.MaximumMaterials)
            throw new ArgumentException("材质场景尺寸、标签数组或定义数量无效。");
        var labels = scene.MaterialIds.ToArray();
        var materials = scene.Materials.Select(m =>
        {
            if (m is null || m.Id is < 0 or >= SpectralMaterialScene.MaximumMaterials) throw new ArgumentException("材质 ID 需要在 0–15 内。");
            Label(m.Name, 256, "Material name", allowEmpty: false); Label(m.Source, 2048, "Material source");
            var rho = FreezeSpectrum(m.Reflectance);
            if (rho is not null && rho.Kind != CameraSpectrumKind.Reflectance) throw new ArgumentException("材质只能使用反射率，不能使用光源光谱。");
            return m with { Reflectance = rho };
        }).ToArray();
        if (materials.Select(m => m.Id).Distinct().Count() != materials.Length || !labels.ToHashSet().SetEquals(materials.Select(m => m.Id)))
            throw new ArgumentException("标签必须与唯一的材质定义完全对应，不能缺少或重复 ID。");
        return scene with { MaterialIds = Array.AsReadOnly(labels), Materials = Array.AsReadOnly(materials) };
    }
    private static SpectralMaterialScene ToCore(CameraMaterialScene scene) => new(scene.Width, scene.Height, scene.MaterialIds,
        scene.Materials.Select(m => new SpectralMaterial(m.Id, m.Reflectance is null
            ? throw new ArgumentException($"材质 {m.Id}（{m.Name}）尚未指定反射率。") : ToCore(m.Reflectance))).ToArray());

    private static byte[] MaterialMapPng(CameraMaterialScene scene)
    {
        using var bitmap = new SKBitmap(scene.Width, scene.Height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        for (var y = 0; y < scene.Height; y++) for (var x = 0; x < scene.Width; x++)
        { var id = (byte)scene.MaterialIds[y * scene.Width + x]; bitmap.SetPixel(x, y, new SKColor(id, id, id)); }
        using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100); return png.ToArray();
    }
}

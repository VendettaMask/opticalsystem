using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis.Imaging;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Application.Services;

internal sealed partial class CameraImagingService
{
    internal const string ExperimentEngineVersion = CameraExperiment.CurrentEngineVersion;
    private const int MaximumExperimentBytes = 256 * 1024 * 1024;
    private static readonly JsonSerializerOptions ExperimentJsonOptions = new()
    {
        MaxDepth = 64, WriteIndented = false, RespectRequiredConstructorParameters = true,
        PropertyNameCaseInsensitive = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };
    private static readonly JsonSerializerOptions LensJsonOptions = new()
    { MaxDepth = 64, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };

    public Task SaveExperimentAsync(CameraExperiment experiment, string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var cancellation = ComputationCancellation.Push(cancellationToken);
        ValidateExperiment(experiment);
        // Serialize before returning: later edits to caller-owned arrays cannot change a pending save.
        var bytes = JsonSerializer.SerializeToUtf8Bytes(experiment, ExperimentJsonOptions);
        if (bytes.Length > MaximumExperimentBytes) throw new InvalidDataException("实验展开数据超过 256 MiB。");
        return BoundedFile.WriteAtomicAsync(path, MaximumExperimentBytes, "Camera experiment", async (stream, token) =>
        {
            using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
            await using (var entry = zip.CreateEntry("experiment.json", CompressionLevel.Fastest).Open())
                await entry.WriteAsync(bytes, token).ConfigureAwait(false);
            await using var hash = zip.CreateEntry("sha256.txt").Open();
            await hash.WriteAsync(Encoding.ASCII.GetBytes(Convert.ToHexStringLower(SHA256.HashData(bytes))), token).ConfigureAwait(false);
        }, cancellationToken);
    }

    public Task<CameraExperiment> OpenExperimentAsync(string path, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var cancellation = ComputationCancellation.Push(cancellationToken);
        using var file = BoundedFile.OpenRead(path, MaximumExperimentBytes, "Camera experiment");
        using var zip = new ZipArchive(file, ZipArchiveMode.Read);
        if (zip.Entries.Count != 2 || zip.Entries.Count(e => e.FullName == "experiment.json") != 1
            || zip.Entries.Count(e => e.FullName == "sha256.txt") != 1)
            throw new InvalidDataException("实验包必须且只能包含 experiment.json 和 sha256.txt。");
        var entry = zip.GetEntry("experiment.json")!;
        if (entry.Length > MaximumExperimentBytes) throw new InvalidDataException("实验展开数据超过 256 MiB。");
        using var payload = entry.Open();
        var bytes = BoundedFile.ReadToEnd(payload, MaximumExperimentBytes, "Camera experiment");
        using var hashStream = zip.GetEntry("sha256.txt")!.Open();
        var hash = Encoding.ASCII.GetString(BoundedFile.ReadToEnd(hashStream, 64, "Experiment digest"));
        if (!hash.Equals(Convert.ToHexStringLower(SHA256.HashData(bytes)), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("实验校验失败，数据可能已损坏。");
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 64 });
            CheckDuplicateProperties(json.RootElement);
            RequireRunProperties(Property(json.RootElement, "B"));
            if (TryProperty(json.RootElement, "A", out var a) && a.ValueKind != JsonValueKind.Null) RequireRunProperties(a);
            var experiment = JsonSerializer.Deserialize<CameraExperiment>(bytes, ExperimentJsonOptions)
                ?? throw new InvalidDataException("实验为空。");
            ValidateExperiment(experiment);
            cancellationToken.ThrowIfCancellationRequested();
            return experiment;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or NullReferenceException)
        { throw new InvalidDataException("实验内容无效：" + ex.Message, ex); }
    }, cancellationToken);

    private static void RequireRunProperties(JsonElement run)
    {
        var request = Property(run, "Request");
        RequireProperties(request, JsonSerializer.SerializeToElement(new CameraImagingRequest(), ExperimentJsonOptions).EnumerateObject().Select(p => p.Name));
        RequireProperties(Property(request, "Sensor"), JsonSerializer.SerializeToElement(new CameraSensorParameters(), ExperimentJsonOptions).EnumerateObject().Select(p => p.Name));
        RequireProperties(Property(request, "Isp"), JsonSerializer.SerializeToElement(new CameraIspParameters(), ExperimentJsonOptions).EnumerateObject().Select(p => p.Name));
        _ = Property(run, "Inputs");
    }

    private static void ValidateExperiment(CameraExperiment experiment)
    {
        ArgumentNullException.ThrowIfNull(experiment);
        if (experiment.Format != CameraExperiment.FormatIdentifier || experiment.SchemaVersion != 1)
            throw new InvalidDataException("不支持的相机实验格式或版本。");
        ValidateRun(experiment.B);
        if (experiment.A is not null) ValidateRun(experiment.A);
    }

    private static void ValidateRun(CameraImagingResult run)
    {
        ArgumentNullException.ThrowIfNull(run); ValidateExport(run);
        Label(run.DocumentName, 2048, "Document name");
        var r = run.Request ?? throw new InvalidDataException("实验缺少参数。");
        CameraSensorPipeline.ValidateDimensions(r.Width, r.Height);
        if (r.Width != run.Raw.Width || r.Height != run.Raw.Height || r.Sensor is null || r.Isp is null
            || !Enum.IsDefined(r.Source) || !Enum.IsDefined(r.Optics) || !Enum.IsDefined(r.Aberration)
            || !Enum.IsDefined(r.SpectralModel) || !Enum.IsDefined(r.SceneMode))
            throw new InvalidDataException("实验参数或图像尺寸无效。");
        CameraSensorPipeline.Validate(ToCore(r.Sensor), ToCore(r.Isp));
        if (r.Integration is not { } settings || !double.IsFinite(settings.MinimumNanometers) || !double.IsFinite(settings.MaximumNanometers)
            || settings.MinimumNanometers < 200 || settings.MaximumNanometers > 20000 || settings.MinimumNanometers >= settings.MaximumNanometers
            || !double.IsFinite(settings.MaximumIntervalNanometers) || settings.MaximumIntervalNanometers is < 1 or > 1000)
            throw new InvalidDataException("实验积分设置无效。");
        _ = FreezeResponse(r.SpectralResponse); _ = FreezeSpectrum(r.Illuminant); _ = FreezeSpectrum(r.Reflectance);
        _ = FreezeMaterialScene(r.MaterialScene);
        if (r.SpectralModel == CameraSpectralModel.Continuous)
        { if (r.SceneMode == CameraSceneMode.MaterialMap) _ = MaterialPlan(r); else _ = ContinuousPlan(r); }
        else ToCore(r.SpectralResponse)?.ValidateCameraCoverage();
        if (r.SensorIdentity?.Calibration is { } calibration) _ = FreezeCalibration(calibration);
        var inputs = FreezeInputs(run.Inputs ?? throw new InvalidDataException("实验缺少冻结输入。"));
        if (r.Optics == CameraOptics.CurrentLens && inputs.LensSnapshotJson is null
            || r.Source == CameraSource.ExternalImage && inputs.ExternalImageBytes is null)
            throw new InvalidDataException("实验缺少镜头或外部图片。");
        if (run.RawDn.Any(v => v >= 1 << r.Sensor.BitDepth) || run.ReferenceRawDn.Any(v => v >= 1 << r.Sensor.BitDepth)
            || run.PhotoElectrons.Any(v => !double.IsFinite(v) || v < 0 || v > 1e10)
            || !double.IsFinite(run.MeanPhotoElectrons) || !double.IsFinite(run.RawNoiseRmseDn)
            || run.MeanPhotoElectrons < 0 || run.RawNoiseRmseDn < 0 || run.SaturatedPixels < 0 || run.SaturatedPixels > r.Width * r.Height)
            throw new InvalidDataException("实验包含无效传感器结果。");
    }

    private static CameraExperimentInputs FreezeInputs(CameraExperimentInputs inputs)
    {
        Label(inputs.EngineVersion, 128, "Camera engine version", allowEmpty: false);
        if (inputs.LensDocumentName is not null) Label(inputs.LensDocumentName, 2048, "Lens document name");
        if (inputs.LensSnapshotJson is { } lens)
        {
            if (Encoding.UTF8.GetByteCount(lens) > BoundedFile.MaximumOpticalDocumentBytes) throw new InvalidDataException("镜头快照过大。");
            _ = RestoreLens(lens);
        }
        ValidateOrigin(inputs.ImageOrigin);
        if (inputs.ExternalImageBytes is { } bytes)
        {
            if (bytes.LongLength > BoundedFile.MaximumImageDataBytes || inputs.ImageOrigin is null
                || !inputs.ImageOrigin.Sha256.Equals(Convert.ToHexStringLower(SHA256.HashData(bytes)), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("输入图片大小或 SHA-256 无效。");
        }
        return inputs with { ExternalImageBytes = inputs.ExternalImageBytes?.ToArray() };
    }

    private static Optic RestoreLens(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
        CheckDuplicateProperties(document.RootElement);
        return Optic.FromSnapshot(JsonSerializer.Deserialize<OpticSnapshot>(json, LensJsonOptions)
            ?? throw new InvalidDataException("镜头快照为空。"));
    }
}

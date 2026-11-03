using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Core.Analysis.Imaging;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Application.Services;

internal sealed partial class CameraImagingService
{
    private sealed record SceneRoiSfrFile(CameraSceneRoiSfrReport Report, string Sha256);
    private const int MaximumRoiReportBytes = MaximumSceneSfrBytes;

    public async Task<CameraSceneRoiSfrReport> MeasureSceneLinearRoisAsync(CameraSceneLinearBatchArtifact artifact,
        CameraSceneRoiSfrRequest request, IProgress<CameraSceneRoiSfrProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact); ArgumentNullException.ThrowIfNull(request);
        LinearImageRoiSfr.ValidateRegions(request.Regions);
        if (request.TaskIndices is null || request.TaskIndices.Count is < 1 or > 16
            || request.TaskIndices.Any(i => i < 0 || i >= artifact.Manifest.Plan.Tasks.Count)
            || request.TaskIndices.Distinct().Count() != request.TaskIndices.Count
            || request.TaskIndices.Count * request.Regions.Count > CameraSceneRoiSfrReport.MaximumMeasurements)
            throw new ArgumentException("Select valid, unique source tasks and at most 128 task/ROI measurements.");
        request = new(Array.AsReadOnly(request.TaskIndices.ToArray()), Array.AsReadOnly(request.Regions.Select(r => r with { Region = r.Region with { } }).ToArray()));
        var tasks = new List<SceneLinearTaskSfr>(); var completed = 0; var total = request.TaskIndices.Count * request.Regions.Count;
        foreach (var index in request.TaskIndices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // This verifies archive identity and hashes. Measurement never reads the RGB preview or PNG.
            var image = await ReadSceneLinearTaskAsync(artifact, index, cancellationToken).ConfigureAwait(false);
            var regions = await Task.Run(() =>
            {
                using var scope = ComputationCancellation.Push(cancellationToken);
                return LinearImageRoiSfr.Measure(image.IrradianceWattsPerSquareMeter, image.Width, image.Height,
                    image.PixelPitchMicrometers, artifact.Manifest.RowOrder, request.Regions,
                    n => progress?.Report(new(index, completed + n, total)));
            }, cancellationToken).ConfigureAwait(false);
            tasks.Add(new(index, image.Name, artifact.Manifest.Files.Single(f => f.Path == artifact.Manifest.Outputs[index].IrradiancePath).Sha256, regions));
            completed += regions.Count;
        }
        var m = artifact.Manifest; var r = m.Plan.Request;
        return new(CameraSceneRoiSfrReport.FormatIdentifier, 1, LinearImageRoiSfr.Algorithm, RoiHash(m), m.Plan.LensSha256,
            m.Plan.SourceDocument, r.Width, r.Height, r.PixelPitchMicrometers, m.RowOrder, request, tasks.AsReadOnly());
    }

    public Task SaveSceneLinearRoisAsync(CameraSceneRoiSfrReport report, string path, CancellationToken cancellationToken = default)
    {
        ValidateRoiReportHeader(report);
        if (!path.EndsWith(".scene-sfr-rois.json", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("ROI 记录须保存为 .scene-sfr-rois.json，不能覆盖完整线性图归档。", nameof(path));
        return BoundedFile.WriteAllBytesAtomicAsync(path, JsonSerializer.SerializeToUtf8Bytes(new SceneRoiSfrFile(report, RoiHash(report)), SceneSfrJson),
            MaximumRoiReportBytes, "linear image multi-ROI SFR report", cancellationToken);
    }

    public async Task<CameraSceneRoiSfrReport> OpenSceneLinearRoisAsync(CameraSceneLinearBatchArtifact artifact, string path,
        CancellationToken cancellationToken = default)
    {
        var bytes = await BoundedFile.ReadAllBytesAsync(path, MaximumRoiReportBytes, "linear image multi-ROI SFR report", cancellationToken).ConfigureAwait(false);
        using var parsed = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 40 }); CheckDuplicateProperties(parsed.RootElement);
        var file = JsonSerializer.Deserialize<SceneRoiSfrFile>(bytes, SceneSfrJson) ?? throw new InvalidDataException("Missing ROI report.");
        var report = file.Report; ValidateRoiReportHeader(report);
        if (file.Sha256 != RoiHash(report) || report.SourceManifestSha256 != RoiHash(artifact.Manifest))
            throw new InvalidDataException("ROI 测量记录或源图批次身份不一致，请打开对应的完整线性实验。");
        var replay = await MeasureSceneLinearRoisAsync(artifact, report.Request, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (RoiHash(report) != RoiHash(replay)) throw new InvalidDataException("ROI 记录与原始浮点图重新测量的结果不一致。");
        return replay;
    }

    private static void ValidateRoiReportHeader(CameraSceneRoiSfrReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (report.Format != CameraSceneRoiSfrReport.FormatIdentifier || report.SchemaVersion != 1 || report.Algorithm != LinearImageRoiSfr.Algorithm
            || report.Request is null || report.Request.Regions is null || report.Tasks is null || report.Tasks.Count is < 1 or > 16
            || report.Tasks.Any(t => t is null || t.Regions is null)
            || report.Request.TaskIndices is null || report.Request.TaskIndices.Count != report.Tasks.Count
            || report.Tasks.Count * report.Request.Regions.Count > CameraSceneRoiSfrReport.MaximumMeasurements
            || !report.Tasks.Select(t => t.TaskIndex).SequenceEqual(report.Request.TaskIndices)
            || report.Tasks.Select(t => t.TaskIndex).Distinct().Count() != report.Tasks.Count)
            throw new InvalidDataException("Unsupported or incomplete multi-ROI SFR report.");
        LinearImageRoiSfr.ValidateRegions(report.Request.Regions);
    }

    private static string RoiHash<T>(T value) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, SceneSfrJson)));
}

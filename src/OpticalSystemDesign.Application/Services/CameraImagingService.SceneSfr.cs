using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis.Imaging;
using OpticalSystemDesign.Core.Apodization;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Application.Services;

internal sealed partial class CameraImagingService
{
    private const int MaximumSceneSfrBytes = 64 * 1024 * 1024;
    private static readonly JsonSerializerOptions SceneSfrJson = new() { WriteIndented = true, MaxDepth = 40,
        RespectRequiredConstructorParameters = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    public Task<CameraSceneSfrResult> SimulateSceneSlantedEdgeAsync(CameraSceneSfrRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request); cancellationToken.ThrowIfCancellationRequested();
        Optic optic; OpticalDocumentSnapshot document; CancellationTokenSource linked;
        lock (Gate)
        {
            if (modes.CurrentMode != OpticalWorkbenchMode.Sequential) throw new InvalidOperationException("场景斜边需要当前顺序镜头。");
            optic = Runtime.CurrentOptic.Clone(); document = Workspace.GetDocumentSnapshot();
            linked = Workspace.LinkDocumentToken(cancellationToken);
        }
        try
        {
            var wave = optic.Wavelengths.FirstOrDefault(w => w.IsPrimary) ?? optic.Wavelengths.FirstOrDefault()
                ?? throw new InvalidOperationException("镜头没有波长。");
            var bands = request.Bands ?? [new(wave.Nanometers, 1)];
            if (request.CaptureLensPrimaryWavelength)
            {
                if (bands.Count != 1) throw new InvalidDataException("主波长捕获只接受一个源波段。");
                bands = [bands[0] with { WavelengthNanometers = wave.Nanometers }];
            }
            request = FreezeSceneSfrRequest(request with { Chart = request.Chart ?? new(), Bands = bands, CaptureLensPrimaryWavelength = false });
            return RunSceneSfrAsync(optic, JsonSerializer.Serialize(optic.ToSnapshot(), LensJsonOptions), document, request, linked);
        }
        catch { linked.Dispose(); throw; }
    }

    public Task<CameraSceneSfrResult> RecomputeSceneSlantedEdgeAsync(CameraSceneSfrResult result, CancellationToken cancellationToken = default)
    {
        result = ValidateSceneSfr(result);
        return RunSceneSfrAsync(RestoreLens(result.LensSnapshotJson), result.LensSnapshotJson, result.SourceDocument,
            result.Request, CancellationTokenSource.CreateLinkedTokenSource(cancellationToken));
    }

    private static async Task<CameraSceneSfrResult> RunSceneSfrAsync(Optic optic, string snapshot, OpticalDocumentSnapshot document,
        CameraSceneSfrRequest request, CancellationTokenSource linked)
    {
        using (linked) return await Task.Run(() =>
        {
            using var scope = ComputationCancellation.Push(linked.Token);
            if (optic.SurfaceGroup.Items.Count < 3) throw new InvalidOperationException("场景成像需要实体镜面和保存的像面。");
            var firstZ = optic.SurfaceGroup.Items[1].CoordinateSystem.Origin.Z;
            var scene = EmissiveCheckerboard.Create(request.Chart!, request.Bands!, firstZ);
            // Both declared uniform models are exactly intensity 1 at every pupil point.
            // Canonicalize only the computation clone; keep the original prescription in the frozen snapshot.
            if (optic.Apodization is UniformApodization or ZemaxApodization { Type: ZemaxApodizationType.Uniform })
                optic.Apodization = null;
            var render = DirectCameraRenderer.Render(optic, scene, new(request.Width, request.Height, request.PixelPitchMicrometers,
                new(request.PixelSamplesPerAxis, request.FillFactor), request.PupilRings, request.PupilArms, request.BareInterfaceFresnel));
            var frame = new CameraSceneLinearFrame(request.Width, request.Height, request.PixelPitchMicrometers, request.FillFactor,
                render.DetectorPower.RowOrder, Array.AsReadOnly(render.DetectorPower.Bands.Select(b =>
                    new CameraDetectorBand(b.WavelengthNanometers, b.PixelBandPowerWatts)).ToArray()),
                render.LaunchedRays, render.PassedLensRays, render.SceneHitRays, render.SurfaceInteractions,
                firstZ, optic.SurfaceGroup.Items[^1].CoordinateSystem.Origin.Z);
            var (edge, failure) = MeasureSceneEdge(frame, request.Edge);
            return new CameraSceneSfrResult(CameraSceneSfrResult.FormatIdentifier, 1, CameraSceneSfrResult.CurrentAlgorithm,
                document, snapshot, SfrHash(snapshot), request, frame, edge, failure);
        }, linked.Token).ConfigureAwait(false);
    }

    private static CameraSceneSfrRequest FreezeSceneSfrRequest(CameraSceneSfrRequest request)
    {
        if (request.Chart is null || request.Bands is null || request.CaptureLensPrimaryWavelength) throw new InvalidDataException("实验必须固定靶标和源波段。");
        EmissiveCheckerboard.Validate(request.Chart, request.Bands);
        CameraSensorPipeline.ValidateDimensions(request.Width, request.Height);
        CameraPixelAperture.Validate(new(request.PixelSamplesPerAxis, request.FillFactor));
        if (!double.IsFinite(request.PixelPitchMicrometers) || request.PixelPitchMicrometers is < .1 or > 100
            || request.PupilRings is < 1 or > 8 || request.PupilArms is < 4 or > 32)
            throw new InvalidDataException("像元或瞳面设置超出支持范围。");
        var work = checked((long)request.Width * request.Height * CameraPixelAperture.Quadrature(new(request.PixelSamplesPerAxis, request.FillFactor)).Count
            * request.PupilRings * request.PupilArms * request.Bands.Count);
        if (work > DirectCameraRenderer.MaximumRays) throw new InvalidDataException("整幅场景超出既有光线预算。");
        if (request.Edge is { } edge && (edge.Region is null || edge.BandIndex < 0 || edge.BandIndex >= request.Bands.Count
            || !Enum.IsDefined(edge.Orientation) || edge.HalfWindowPixels is < 8 or > 48
            || edge.Region.Width is < 32 or > 256 || edge.Region.Height is < 32 or > 256 || edge.Region.X < 0 || edge.Region.Y < 0
            || (long)edge.Region.X + edge.Region.Width > request.Width || (long)edge.Region.Y + edge.Region.Height > request.Height))
            throw new InvalidDataException("请选择有效波段及图像内的单个 ROI。");
        return request with { Bands = Array.AsReadOnly(request.Bands.ToArray()) };
    }

    private static (LinearImageEdgeResult? Edge, string? Failure) MeasureSceneEdge(CameraSceneLinearFrame frame, CameraSceneEdgeRequest? edge)
    {
        if (edge is null) return (null, null);
        try
        {
            var image = frame.ToLinearImage();
            return (LinearImageSfr.Measure(image.Bands[edge.BandIndex].WattsPerSquareMeter, frame.Width, frame.Height,
                edge.Region, frame.PixelPitchMicrometers, edge.Orientation, edge.HalfWindowPixels), null);
        }
        catch (InvalidOperationException ex) { return (null, ex.Message); }
    }

    private static CameraSceneSfrResult ValidateSceneSfr(CameraSceneSfrResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Format != CameraSceneSfrResult.FormatIdentifier || result.SchemaVersion != 1 || result.Algorithm != CameraSceneSfrResult.CurrentAlgorithm
            || result.SourceDocument is null || result.LensSnapshotJson is null || SfrHash(result.LensSnapshotJson) != result.LensSha256)
            throw new InvalidDataException("场景斜边实验格式、算法或镜头摘要无效。");
        var request = FreezeSceneSfrRequest(result.Request); var frame = result.Frame;
        var expectedRays = checked((long)request.Width * request.Height * request.PixelSamplesPerAxis * request.PixelSamplesPerAxis
            * request.PupilRings * request.PupilArms * request.Bands!.Count);
        if (frame is null || frame.Width != request.Width || frame.Height != request.Height || frame.PixelPitchMicrometers != request.PixelPitchMicrometers
            || frame.FillFactor != request.FillFactor || frame.RowOrder != DetectorRowOrder.PositiveLocalYFirst
            || frame.Bands is null || frame.Bands.Count != request.Bands!.Count
            || frame.Bands.Where((b, i) => b is null || b.WavelengthNanometers != request.Bands[i].WavelengthNanometers).Any()
            || frame.LaunchedRays != expectedRays
            || frame.SceneHitRays < 0 || frame.SceneHitRays > frame.PassedLensRays || frame.PassedLensRays > frame.LaunchedRays
            || frame.LaunchedRays is <= 0 or > DirectCameraRenderer.MaximumRays || frame.SurfaceInteractions is < 0 or > DirectCameraRenderer.MaximumSurfaceInteractions)
            throw new InvalidDataException("线性像元、坐标或计算量与实验条件不一致。");
        var lens = RestoreLens(result.LensSnapshotJson);
        if (frame.FirstLensVertexZMillimeters != lens.SurfaceGroup.Items[1].CoordinateSystem.Origin.Z
            || frame.SensorPlaneZMillimeters != lens.SurfaceGroup.Items[^1].CoordinateSystem.Origin.Z)
            throw new InvalidDataException("实验像面与镜头快照不一致。");
        var linear = frame.ToLinearImage();
        var frozenFrame = frame with { Bands = Array.AsReadOnly(linear.Power.Bands.Select(b => new CameraDetectorBand(b.WavelengthNanometers, b.PixelBandPowerWatts)).ToArray()) };
        var (measured, failure) = MeasureSceneEdge(frozenFrame, request.Edge);
        if (JsonSerializer.Serialize(measured, SceneSfrJson) != JsonSerializer.Serialize(result.Edge, SceneSfrJson) || failure != result.EdgeFailure)
            throw new InvalidDataException("保存的 SFR 不对应原始线性像元。");
        return result with { Request = request, Frame = frozenFrame, Edge = measured };
    }

    public Task ExportSceneSlantedEdgeAsync(CameraSceneSfrResult result, string path, CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        using var scope = ComputationCancellation.Push(cancellationToken);
        result = ValidateSceneSfr(result);
        var payload = JsonSerializer.SerializeToUtf8Bytes(result, SceneSfrJson);
        if (payload.Length > MaximumSceneSfrBytes) throw new InvalidDataException("场景实验超过 64 MiB。");
        using var memory = new MemoryStream();
        long uncompressedBytes = 0;
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            Write("experiment.json", payload); Write("sha256.txt", Encoding.UTF8.GetBytes(SfrHash(Encoding.UTF8.GetString(payload))));
            var image = result.Frame.ToLinearImage();
            for (var b = 0; b < image.Bands.Count; b++)
            {
                var csv = new StringBuilder("x,y,x_mm,y_mm,power_W,irradiance_W_per_m2\n");
                for (var y = 0; y < image.Power.Height; y++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    for (var x = 0; x < image.Power.Width; x++)
                    {
                        var i = y * image.Power.Width + x; var center = image.PixelCenter(x, y);
                        csv.Append(CultureInfo.InvariantCulture, $"{x},{y},{center.XMillimeters:G17},{center.YMillimeters:G17},{image.Power.Bands[b].PixelBandPowerWatts[i]:G17},{image.Bands[b].WattsPerSquareMeter[i]:G17}\n");
                    }
                }
                Write($"band-{b + 1}-linear.csv", Encoding.UTF8.GetBytes(csv.ToString()));
            }
            Write("README.txt", Encoding.UTF8.GetBytes("Authoritative experiment.json contains frozen physical chart, band-integrated radiances, lens snapshot and W/pixel. CSV uses collecting-area average W/m2 and sensor-local pixel centers. No exposure, QE, gamma, noise, ADC or ISP. Geometric transport only; no ROM, formal MTF comparison or convergence certification in this version. Edge failure is retained as a failure.\n"));
            void Write(string name, byte[] bytes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                uncompressedBytes = checked(uncompressedBytes + bytes.Length);
                if (uncompressedBytes > MaximumSceneSfrBytes) throw new InvalidDataException("场景实验解压总量超过 64 MiB。");
                using var stream = zip.CreateEntry(name, CompressionLevel.Optimal).Open(); stream.Write(bytes);
            }
        }
        await BoundedFile.WriteAllBytesAtomicAsync(path, memory.ToArray(), MaximumSceneSfrBytes, "scene slanted-edge experiment", cancellationToken);
    }, cancellationToken);

    public Task<CameraSceneSfrResult> OpenSceneSlantedEdgeAsync(string path, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        using var scope = ComputationCancellation.Push(cancellationToken);
        using var memory = new MemoryStream(BoundedFile.ReadAllBytes(path, MaximumSceneSfrBytes, "scene slanted-edge experiment"));
        using var zip = new ZipArchive(memory, ZipArchiveMode.Read);
        if (zip.Entries.Count is < 3 or > 19 || zip.Entries.Select(e => e.FullName).Distinct().Count() != zip.Entries.Count
            || zip.Entries.Sum(e => e.Length) > MaximumSceneSfrBytes)
            throw new InvalidDataException("场景实验 ZIP 内容或解压预算无效。");
        string Read(string name)
        {
            var entry = zip.GetEntry(name) ?? throw new InvalidDataException("实验缺少 " + name);
            using var reader = new StreamReader(entry.Open()); return reader.ReadToEnd();
        }
        var json = Read("experiment.json");
        if (SfrHash(json) != Read("sha256.txt")) throw new InvalidDataException("实验摘要不匹配。");
        using var parsed = JsonDocument.Parse(json); CheckDuplicateProperties(parsed.RootElement);
        return ValidateSceneSfr(JsonSerializer.Deserialize<CameraSceneSfrResult>(json, SceneSfrJson) ?? throw new InvalidDataException("实验为空。"));
    }, cancellationToken);
}

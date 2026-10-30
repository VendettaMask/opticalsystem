using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Analysis.Imaging;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.Core.Serialization;
using SkiaSharp;

namespace OpticalSystemDesign.Application.Services;

internal sealed partial class CameraImagingService(WorkspaceCoordinator workspace, IWorkbenchModeService modes)
    : WorkbenchServiceBase(workspace), ICameraImagingService
{
    internal const string Scope = "Generic laboratory model; 650/550/450 nm narrow bands; ideal non-overlapping Bayer filters; "
        + "user-specified image-plane irradiance per band at linear value 1 (W/m^2); input assumed sRGB; "
        + "bilinear demosaic, black-level subtraction, RGB gains, box denoise, unsharp mask, power gamma. "
        + "Detector sampling is declared separately; user RAW scalar calibration is not independently verified; no full spectral scene, angular QE, 3D scene, motion, stray-light coupling or vendor ISP. "
        + "Not an ImSym/Speos numerical comparison or EMVA certification.";

    public Task<CameraImagingResult> SimulateAsync(CameraImagingRequest request, CancellationToken cancellationToken = default)
        => SimulateCoreAsync(request, null, null, cancellationToken);

    public Task<CameraImagingResult> RecomputeAsync(CameraImagingResult run, CameraImagingRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.Inputs is null) throw new InvalidDataException("此结果没有可复算输入，请使用新的实验文件。");
        if (!CameraExperiment.CanRecompute(run.Inputs.EngineVersion)) throw new InvalidDataException("实验计算模型版本不同；可查看原结果，不能保证同模型复算。");
        if (run.Inputs.EngineVersion == CameraExperiment.LegacyEngineVersion && run.Request.PixelSampling is not null)
            throw new InvalidDataException("旧模型记录不允许包含新增像元面积采样设置。");
        var inputs = FreezeInputs(run.Inputs);
        if (request is not null) inputs = inputs with { EngineVersion = ExperimentEngineVersion };
        if (request is { Source: CameraSource.ExternalImage } && (inputs.ExternalImageBytes is null
            || request.SourceFile != run.Request.SourceFile))
        {
            var bytes = BoundedFile.ReadAllBytes(request.SourceFile, BoundedFile.MaximumImageDataBytes, "Camera input image");
            inputs = inputs with { ExternalImageBytes = bytes, ImageOrigin = Origin(request.SourceFile, bytes) };
        }
        return SimulateCoreAsync(request ?? run.Request, inputs, run.DocumentName, cancellationToken);
    }

    private async Task<CameraImagingResult> SimulateCoreAsync(CameraImagingRequest request, CameraExperimentInputs? inputs,
        string? savedName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request); cancellationToken.ThrowIfCancellationRequested();
        if (request.Source == CameraSource.ExternalImage && string.IsNullOrWhiteSpace(request.SourceFile) && inputs?.ExternalImageBytes is null)
            throw new InvalidOperationException("External bitmap mode requires an input image file.");
        request = request with { Sensor = request.Sensor ?? new(), Isp = request.Isp ?? new() };
        request = request with
        {
            SpectralResponse = FreezeResponse(request.SpectralResponse),
            Illuminant = FreezeSpectrum(request.Illuminant),
            Reflectance = FreezeSpectrum(request.Reflectance),
            MaterialScene = FreezeMaterialScene(request.MaterialScene),
            Integration = request.Integration ?? new()
        };
        if (request.SensorIdentity?.Calibration is { } calibration)
            request = request with { SensorIdentity = request.SensorIdentity with { Calibration = FreezeCalibration(calibration) } };
        CameraSensorPipeline.ValidateDimensions(request.Width, request.Height);
        if (!Enum.IsDefined(request.Source) || !Enum.IsDefined(request.Optics) || !Enum.IsDefined(request.Aberration)
            || !Enum.IsDefined(request.SpectralModel) || !Enum.IsDefined(request.SceneMode))
            throw new ArgumentException("Unsupported camera simulation option.");
        if (request.Optics == CameraOptics.CurrentLens && (request.Width > 256 || request.Height > 256))
            throw new ArgumentOutOfRangeException(nameof(request), "当前镜头阶段每边最多 256 像素；512 × 384 可用于理想光学传感器对照。");
        var sensor = ToCore(request.Sensor); var isp = ToCore(request.Isp);
        CameraSensorPipeline.Validate(sensor, isp);
        var pixelSampling = ToCore(request.PixelSampling); CameraPixelAperture.Validate(pixelSampling);
        ValidateQeArea(request);
        if (request.Source == CameraSource.Scene3D)
            return await SimulateScene3DAsync(request, inputs, savedName, cancellationToken).ConfigureAwait(false);
        if (request.SpectralModel == CameraSpectralModel.SceneBands || request.Scene3D is not null || request.Render3D is not null)
            throw new ArgumentException("三维场景与场景波段仅用于三维直射模式。");
        if ((inputs?.EngineVersion ?? ExperimentEngineVersion) == ExperimentEngineVersion && pixelSampling is { FillFactor: < 1 })
            throw new ArgumentException("旧二维网格不能正确移动感光区采样点；非满填充请使用三维场景的实际感光区积分。旧实验可按原版本复算。");
        var samplesPerAxis = pixelSampling?.SamplesPerAxis ?? 1;
        if (samplesPerAxis == 4 && request.Optics == CameraOptics.CurrentLens)
            throw new ArgumentException("在原资源上限内，当前镜头暂支持 2×2 面积采样；4×4 用于理想光学对照。");
        var sampleRequest = request with { Width = checked(request.Width * samplesPerAxis), Height = checked(request.Height * samplesPerAxis) };
        CameraPixelAperture.OutputDimensions(sampleRequest.Width, sampleRequest.Height, pixelSampling);
        var opticalSensor = sensor with { PixelPitchMicrometers = sensor.PixelPitchMicrometers / samplesPerAxis };
        if (request.SpectralModel == CameraSpectralModel.ThreeBand) ToCore(request.SpectralResponse)?.ValidateCameraCoverage();
        var materialPlan = request.SpectralModel == CameraSpectralModel.Continuous && request.SceneMode == CameraSceneMode.MaterialMap
            ? MaterialPlan(request) : null;
        var plan = materialPlan?.Common ?? (request.SpectralModel == CameraSpectralModel.Continuous ? ContinuousPlan(request) : null);
        var materialSampling = materialPlan?.DescribeSampling(sampleRequest.Width, sampleRequest.Height);
        if (plan is not null)
        {
            if (request.Optics == CameraOptics.CurrentLens && (long)sampleRequest.Width * sampleRequest.Height * plan.Nodes.Count > CameraContinuousSpectrum.MaximumPixelNodes)
                throw new ArgumentException("连续光谱的像素 × 积分节点超过一亿预算，请降低分辨率或减少采样点。");
            if (request.Optics == CameraOptics.CurrentLens)
            {
                if (plan.Nodes.Count > 32) throw new ArgumentException("当前镜头连续光谱最多 32 个积分节点；请减少曲线节点/积分范围，或使用理想光学。");
                ValidateOpticalSampling(sampleRequest, samplesPerAxis, plan.Nodes.Count);
            }
        }
        else if (request.Optics == CameraOptics.CurrentLens) ValidateOpticalSampling(sampleRequest, samplesPerAxis, 3);
        Optic? optic; long revision; string name; CancellationTokenSource linked;
        lock (Gate)
        {
            if (inputs is null && request.Optics == CameraOptics.CurrentLens && modes.CurrentMode != OpticalWorkbenchMode.Sequential)
                throw new InvalidOperationException("当前镜头成像需要顺序模式；可选择理想光学对照。");
            optic = request.Optics != CameraOptics.CurrentLens ? null : inputs is null ? Runtime.CurrentOptic.Clone()
                : RestoreLens(inputs.LensSnapshotJson ?? throw new InvalidDataException("实验没有镜头快照。"));
            revision = Workspace.Revision; name = savedName ?? Runtime.CurrentOptic.Name;
            linked = Workspace.LinkDocumentToken(cancellationToken);
        }
        // Preserve the prescription BEFORE the image-plane field configuration below.
        var lensJson = optic is null ? inputs?.LensSnapshotJson : JsonSerializer.Serialize(optic.ToSnapshot(), LensJsonOptions);
        using (linked)
            return await Task.Run(() =>
            {
                using var scope = ComputationCancellation.Push(linked.Token);
                var imageBytes = request.Source != CameraSource.ExternalImage ? null : inputs is not null
                    ? inputs.ExternalImageBytes ?? throw new InvalidDataException("实验没有保存外部输入图片。")
                    : BoundedFile.ReadAllBytes(request.SourceFile, BoundedFile.MaximumImageDataBytes, "Camera input image");
                var captured = new CameraExperimentInputs(inputs?.EngineVersion ?? ExperimentEngineVersion, lensJson, imageBytes ?? inputs?.ExternalImageBytes,
                    imageBytes is null ? inputs?.ImageOrigin : inputs?.ImageOrigin ?? Origin(request.SourceFile, imageBytes),
                    inputs?.LensDocumentName ?? name, inputs?.LensSourceRevision ?? revision);
                var encoded = imageBytes is not null
                    ? ImageFileLoader.LoadRgb(imageBytes)
                    : ImageSimulationEngine.CreateSourceImage((ImageSimulationSourcePattern)request.Source, sampleRequest.Width, sampleRequest.Height);
                var source = ResizeLinear(encoded, sampleRequest.Width, sampleRequest.Height, samplesPerAxis > 1);
                var optical = source; var opticalMode = "Ideal optical control"; var fallbacks = 0;
                if (optic is not null)
                {
                    if (optic.SurfaceGroup.Items.Count < 4) throw new InvalidOperationException("请先打开有效顺序镜头，或使用理想光学对照。");
                    // Set an image-plane sampling field on the independent snapshot, in the actual sensor dimensions.
                    ConfigureSensorField(optic, request, sensor.PixelPitchMicrometers);
                }
                CameraSensorResult sensorResult;
                if (plan is not null)
                {
                    var modes = new HashSet<string>();
                    Func<RgbImage, double, RgbImage>? propagation = optic is null ? null : (mask, wavelength) =>
                    {
                        var image = Propagate(optic, mask, sampleRequest, opticalSensor, new[] { wavelength }, samplesPerAxis);
                        modes.Add(image.Mode); fallbacks += image.Fallbacks; return image.Image;
                    };
                    var integrated = materialPlan is null ? CameraContinuousSpectrum.Integrate(source, sensor, plan, propagation, pixelSampling)
                        : CameraContinuousSpectrum.IntegrateMaterials(source, sensor, materialPlan, propagation, pixelSampling);
                    source = integrated.SourceMask; optical = integrated.OpticalPreview;
                    if (modes.Count > 0) opticalMode = string.Join(" + ", modes.Order(StringComparer.Ordinal));
                    sensorResult = CameraSensorPipeline.SimulatePhotoElectrons(integrated.PhotoElectrons, request.Width, request.Height, sensor, isp);
                }
                else
                {
                    if (optic is not null)
                    {
                        var image = Propagate(optic, source, sampleRequest, opticalSensor, CameraSensorPipeline.BandWavelengthsNanometers, samplesPerAxis);
                        optical = image.Image; opticalMode = image.Mode; fallbacks = image.Fallbacks;
                    }
                    sensorResult = CameraSensorPipeline.Simulate(optical, sensor, isp, ToCore(request.SpectralResponse), pixelSampling);
                    source = CameraPixelAperture.Average(source, pixelSampling is null ? null : pixelSampling with { FillFactor = 1 });
                    optical = CameraPixelAperture.Average(optical, pixelSampling);
                }
                var maxDn = (1 << sensor.BitDepth) - 1;
                var electrons = Gray(sensorResult.PhotoElectrons, request.Width, request.Height, sensor.FullWellElectrons);
                var raw = Gray(sensorResult.RawDn.Select(x => (double)x).ToArray(), request.Width, request.Height, maxDn);
                linked.Token.ThrowIfCancellationRequested();
                return new CameraImagingResult(name, revision, request, Display(source, true), Display(optical, true),
                    electrons, raw, Display(sensorResult.ReferenceDeveloped), Display(sensorResult.Developed),
                    sensorResult.RawDn, sensorResult.ReferenceRawDn, sensorResult.PhotoElectrons,
                    sensorResult.MeanPhotoElectrons, sensorResult.RawNoiseRmseDn, sensorResult.SaturatedPixels,
                    opticalMode, fallbacks, (materialPlan is not null ? MaterialScope : plan is not null ? ContinuousScope : request.SpectralResponse is null ? Scope : SpectralScope)
                        + PixelScope(pixelSampling),
                    materialPlan is not null ? Diagnostics(request.MaterialScene!, materialPlan, materialSampling!, samplesPerAxis) : plan is null ? null : Diagnostics(plan, samplesPerAxis), captured);
            }, linked.Token).ConfigureAwait(false);
    }

    internal static void ConfigureSensorField(Optic optic, CameraImagingRequest request, double pixelPitchMicrometers)
    {
        optic.FieldDefinition = FieldDefinitionKind.ParaxialImageHeight;
        optic.Fields.Clear();
        optic.Fields.Add(new FieldPoint { Y = request.Height * pixelPitchMicrometers * 0.001 / 2 });
    }

    public Task ExportAsync(CameraImagingResult result, string path, CameraImagingResult? comparison = null,
        CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        ArgumentNullException.ThrowIfNull(result); cancellationToken.ThrowIfCancellationRequested();
        ValidateExport(result); if (comparison is not null) ValidateExport(comparison);
        using var memory = new MemoryStream();
        var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true);
        using (archive)
        {
            AddRun(result, "B"); if (comparison is not null) AddRun(comparison, "A");
            Write("README.txt", Encoding.UTF8.GetBytes("B: " + result.ModelScope
                + (comparison is null ? "" : "\nA: " + comparison.ModelScope) + "\n\nRAW CSV values are original ADC DN (black level retained). "
                + "Electron CSV contains expected photoelectrons before noise/clipping. PNGs are display previews. "
                + "Noiseless uses the same exposure, ADC and ISP with stochastic/fixed-pattern noise omitted. "
                + "Optical/source previews use fixed gamma 2.2; electron/RAW previews have fixed full-well/ADC scales. "
                + "RAW has top-left origin and the Bayer pattern in metadata; it is not a DNG file."));
        }
        await BoundedFile.WriteAllBytesAtomicAsync(path, memory.ToArray(), BoundedFile.MaximumExportBytes,
            "Camera imaging comparison", cancellationToken).ConfigureAwait(false);

        void AddRun(CameraImagingResult run, string folder)
        {
            if (run.Scene3D is { } scene3d)
            {
                Write(folder + "/scene3d.json", JsonSerializer.SerializeToUtf8Bytes(run.Request.Scene3D, Scene3DJson));
                Write(folder + "/scene3d-diagnostics.json", JsonSerializer.SerializeToUtf8Bytes(scene3d, ExperimentJsonOptions));
                var powers = new StringBuilder("x,y,wavelength_nm,active_pixel_power_W\n");
                foreach (var band in scene3d.Bands) for (var y = 0; y < run.Request.Height; y++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    for (var x = 0; x < run.Request.Width; x++) powers.Append(CultureInfo.InvariantCulture,
                        $"{x},{y},{band.WavelengthNanometers:G17},{band.PixelPowerWatts[y * run.Request.Width + x]:G17}\n");
                }
                Write(folder + "/detector-power.csv", Encoding.UTF8.GetBytes(powers.ToString()));
            }
            Write(folder + "/metadata.json", JsonSerializer.SerializeToUtf8Bytes(new
            {
                run.DocumentName,
                run.SourceRevision,
                run.Request,
                run.ModelScope,
                run.OpticalModel,
                run.OpticalFallbackCount,
                run.MeanPhotoElectrons,
                run.RawNoiseRmseDn,
                run.SaturatedPixels,
                BandsNanometers = run.Integration is null ? CameraSensorPipeline.BandWavelengthsNanometers
                    : run.Integration.Nodes.Select(n => n.WavelengthNanometers).ToArray(),
                run.Integration,
                DetectorModelVersion = run.Scene3D is null ? "CameraPixelArea/1" : "CameraActiveGauss/1",
                EngineVersion = run.Inputs?.EngineVersion,
                DetectorSampling = run.Request.PixelSampling,
                ModelVersion = run.Scene3D?.Diffraction is not null ? "CameraLabV8-SceneDiffraction" : run.Scene3D is not null ? "CameraLabV7-DirectScene" : run.Integration?.Materials is not null ? "CameraLabV4-Materials" : run.Integration is not null ? "CameraLabV3-Continuous" : run.Request.SpectralResponse is null ? "CameraLabV1" : "CameraLabV2-QE",
                OpticalSampling = run.Scene3D?.Diffraction is not null ? $"Absolute scalar Huygens surface cells; {run.Request.Render3D!.Diffraction}; active pixel Gauss {run.Request.PixelSampling?.SamplesPerAxis ?? 1}; no crop normalization, no geometric convolution"
                    : run.Scene3D is not null ? $"Actual reverse lens; pixel Gauss {run.Request.PixelSampling?.SamplesPerAxis ?? 1}; pupil Gaussian {run.Request.Render3D?.PupilRings ?? 2} rings x {run.Request.Render3D?.PupilArms ?? 8} arms; absolute active W/pixel"
                    : run.Request.Optics == CameraOptics.CurrentLens
                    ? $"3x3 field; {16 * (run.Request.PixelSampling?.SamplesPerAxis ?? 1)}x{16 * (run.Request.PixelSampling?.SamplesPerAxis ?? 1)} PSF; 3 components; 12 pupil sampling parameter; {8 * (run.Request.PixelSampling?.SamplesPerAxis ?? 1)} optical-cell guard; 5x5 distortion grid; degree 3; optical cell pitch = pixel pitch / samples per axis"
                    : "Ideal optical control"
            }, new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } }));
            if (run.Request.SpectralResponse is { } response)
                Write(folder + "/spectral-response.csv", Encoding.UTF8.GetBytes(ResponseCsv(response)));
            if (run.Integration is { } integration)
            {
                Write(folder + "/illuminant.csv", Encoding.UTF8.GetBytes(SpectrumCsv(run.Request.Illuminant!)));
                if (integration.Materials is not null)
                {
                    var scene = run.Request.MaterialScene!;
                    Write(folder + "/material-scene.json", JsonSerializer.SerializeToUtf8Bytes(scene, MaterialJsonOptions));
                    Write(folder + "/material-map.png", MaterialMapPng(scene));
                    foreach (var material in scene.Materials)
                        Write(folder + $"/material-{material.Id:D2}-reflectance.csv", Encoding.UTF8.GetBytes(SpectrumCsv(material.Reflectance!)));
                }
                else Write(folder + "/reflectance.csv", Encoding.UTF8.GetBytes(SpectrumCsv(run.Request.Reflectance!)));
                Write(folder + "/integration.csv", Encoding.UTF8.GetBytes(IntegrationCsv(integration)));
            }
            Write(folder + "/sensor-profile.json", JsonSerializer.SerializeToUtf8Bytes(new CameraSensorProfile(
                SensorFormat, 1, run.Request.SensorIdentity?.Name ?? "Generic laboratory sensor",
                run.Request.SensorIdentity?.Source ?? "User configured, not calibrated", run.Request.Sensor!, run.Request.SpectralResponse,
                Calibration: run.Request.SensorIdentity?.Calibration), SensorJsonOptions));
            foreach (var (label, image) in new[] { ("source", run.Source), ("optical", run.Optical), ("electrons", run.Electrons),
                ("raw-preview", run.Raw), ("noiseless-isp", run.Noiseless), ("developed", run.Developed) })
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var bitmap = new SKBitmap(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Opaque);
                for (var y = 0; y < image.Height; y++) for (var x = 0; x < image.Width; x++)
                {
                    var i = (y * image.Width + x) * 3;
                    bitmap.SetPixel(x, y, new SKColor(image.Rgb24[i], image.Rgb24[i + 1], image.Rgb24[i + 2]));
                }
                using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
                Write(folder + "/" + label + ".png", encoded.ToArray());
            }
            var csv = new StringBuilder("x,y,raw_dn,noiseless_raw_dn,expected_photoelectrons\n");
            for (var y = 0; y < run.Raw.Height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (var x = 0; x < run.Raw.Width; x++)
                {
                    var i = y * run.Raw.Width + x;
                    csv.Append(CultureInfo.InvariantCulture, $"{x},{y},{run.RawDn[i]},{run.ReferenceRawDn[i]},{run.PhotoElectrons[i]:G17}\n");
                }
            }
            Write(folder + "/sensor.csv", Encoding.UTF8.GetBytes(csv.ToString()));
        }
        void Write(string name, byte[] bytes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var stream = archive.CreateEntry(name, CompressionLevel.Fastest).Open(); stream.Write(bytes);
        }
    }, cancellationToken);

    private static void ValidateExport(CameraImagingResult result)
    {
        CameraSensorPipeline.ValidateDimensions(result.Raw.Width, result.Raw.Height);
        var count = result.Raw.Width * result.Raw.Height;
        foreach (var image in new[] { result.Source, result.Optical, result.Electrons, result.Raw, result.Noiseless, result.Developed })
            if (image.Width != result.Raw.Width || image.Height != result.Raw.Height || image.Rgb24.Count != count * 3)
                throw new ArgumentException("Camera export image dimensions are inconsistent.");
        if (result.RawDn.Count != count || result.ReferenceRawDn.Count != count || result.PhotoElectrons.Count != count)
            throw new ArgumentException("Camera export sensor dimensions are inconsistent.");
    }

    private static CameraSensorSettings ToCore(CameraSensorParameters s) => new(s.ExposureMilliseconds, s.PixelPitchMicrometers,
        s.ReferenceIrradiance, s.RedQe, s.GreenQe, s.BlueQe, s.FullWellElectrons, s.ReadNoiseElectrons,
        s.DarkElectronsPerSecond, s.PrnuPercent, s.DsnuElectrons, s.GainDnPerElectron, s.BlackLevel, s.BitDepth,
        s.NoiseEnabled, s.Seed, (BayerPattern)s.Pattern);
    private static CameraIspSettings ToCore(CameraIspParameters i) => new(i.Enabled, i.RedGain, i.GreenGain, i.BlueGain,
        i.Gamma, i.Denoise, i.Sharpen);

    private static CameraPixelSampling? ToCore(CameraPixelSamplingParameters? p) => p is null ? null : new(p.SamplesPerAxis, p.FillFactor, p.QeIncludesFillFactor);

    private static void ValidateOpticalSampling(CameraImagingRequest samples, int n, int wavelengths)
    {
        if (n == 1)
        {
            AnalysisResourceLimits.ValidateConvolutionWork(samples.Width + 16, samples.Height + 16, 16, 16, 5, wavelengths);
            return;
        }
        try { AnalysisResourceLimits.ValidateConvolutionWork(samples.Width + 16 * n, samples.Height + 16 * n, 16 * n, 16 * n, 5, wavelengths); }
        catch (ArgumentOutOfRangeException ex) { throw new ArgumentException("像元面积采样后的光学网格或卷积超过原资源上限；请降低输出分辨率、采样或光谱节点数。", ex); }
    }

    private static void ValidateQeArea(CameraImagingRequest request)
    {
        if (request.SpectralResponse is not null && request.PixelSampling is { QeIncludesFillFactor: false })
            throw new ArgumentException("导入曲线的格式定义为整像元总 QE；请使用整像元总 QE 口径。感光区 QE 仅用于手动固定 QE。");
    }

    private static string PixelScope(CameraPixelSampling? p) => p is null || p.SamplesPerAxis == 1
        ? " Detector: legacy pixel-center sampling."
        : FormattableString.Invariant($" Detector: {p.SamplesPerAxis}x{p.SamplesPerAxis} optical cells/pixel; centered square active area, fill factor {p.FillFactor:G17}; overlap-weighted midpoint irradiance quadrature before CFA/noise; QE refers to {(p.QeIncludesFillFactor ? "whole-pixel area (no repeated fill-factor loss)" : "active area (charge multiplied by fill factor)")}. Finite sampling, not a convergence certificate.");

    private static RgbImage ResizeLinear(RgbImage source, int width, int height, bool cellCenters = false)
    {
        var result = new double[3, height, width];
        for (var y = 0; y < height; y++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var sy = cellCenters ? Math.Clamp((y + .5) * source.Height / height - .5, 0, source.Height - 1)
                : (double)y * (source.Height - 1) / (height - 1); var y0 = (int)sy; var y1 = Math.Min(y0 + 1, source.Height - 1);
            for (var x = 0; x < width; x++)
            {
                var sx = cellCenters ? Math.Clamp((x + .5) * source.Width / width - .5, 0, source.Width - 1)
                    : (double)x * (source.Width - 1) / (width - 1); var x0 = (int)sx; var x1 = Math.Min(x0 + 1, source.Width - 1);
                for (var c = 0; c < 3; c++)
                {
                    var top = Linear(y0, x0) * (1 - sx + x0) + Linear(y0, x1) * (sx - x0);
                    var bottom = Linear(y1, x0) * (1 - sx + x0) + Linear(y1, x1) * (sx - x0);
                    result[c, y, x] = top * (1 - sy + y0) + bottom * (sy - y0);
                    double Linear(int yy, int xx) => CameraSensorPipeline.SrgbToLinear(source[c, yy, xx]);
                }
            }
        }
        return new(result);
    }

    private static CameraImage Display(RgbImage image, bool opticalPreview = false)
    {
        var bytes = new byte[image.Width * image.Height * 3];
        for (var y = 0; y < image.Height; y++) for (var x = 0; x < image.Width; x++) for (var c = 0; c < 3; c++)
        {
            var value = Math.Clamp(image[c, y, x], 0, 1);
            bytes[(y * image.Width + x) * 3 + c] = (byte)Math.Round(255 * (opticalPreview ? Math.Pow(value, 1 / 2.2) : value));
        }
        return new(image.Width, image.Height, bytes);
    }
    private static CameraImage Gray(double[] pixels, int width, int height, double scale)
    {
        var bytes = new byte[width * height * 3];
        for (var i = 0; i < pixels.Length; i++)
        {
            var value = (byte)Math.Round(Math.Clamp(pixels[i] / scale, 0, 1) * 255);
            bytes[3 * i] = bytes[3 * i + 1] = bytes[3 * i + 2] = value;
        }
        return new(width, height, bytes);
    }
}

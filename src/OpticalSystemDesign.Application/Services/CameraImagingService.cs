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
using SkiaSharp;

namespace OpticalSystemDesign.Application.Services;

internal sealed partial class CameraImagingService(WorkspaceCoordinator workspace, IWorkbenchModeService modes)
    : WorkbenchServiceBase(workspace), ICameraImagingService
{
    internal const string Scope = "Generic laboratory model; 650/550/450 nm narrow bands; ideal non-overlapping Bayer filters; "
        + "user-specified image-plane irradiance per band at linear value 1 (W/m^2); input assumed sRGB; "
        + "bilinear demosaic, black-level subtraction, RGB gains, box denoise, unsharp mask, power gamma. "
        + "Pixel-center optical samples, no pixel-area integration; no measured sensor calibration, full spectral scene, angular QE, 3D scene, motion, stray-light coupling or vendor ISP. "
        + "Not an ImSym/Speos numerical comparison or EMVA certification.";

    public async Task<CameraImagingResult> SimulateAsync(CameraImagingRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request); cancellationToken.ThrowIfCancellationRequested();
        request = request with { Sensor = request.Sensor ?? new(), Isp = request.Isp ?? new() };
        request = request with
        {
            SpectralResponse = FreezeResponse(request.SpectralResponse),
            Illuminant = FreezeSpectrum(request.Illuminant),
            Reflectance = FreezeSpectrum(request.Reflectance),
            MaterialScene = FreezeMaterialScene(request.MaterialScene),
            Integration = request.Integration ?? new()
        };
        CameraSensorPipeline.ValidateDimensions(request.Width, request.Height);
        if (!Enum.IsDefined(request.Source) || !Enum.IsDefined(request.Optics) || !Enum.IsDefined(request.Aberration)
            || !Enum.IsDefined(request.SpectralModel) || !Enum.IsDefined(request.SceneMode))
            throw new ArgumentException("Unsupported camera simulation option.");
        if (request.Optics == CameraOptics.CurrentLens && (request.Width > 256 || request.Height > 256))
            throw new ArgumentOutOfRangeException(nameof(request), "当前镜头阶段每边最多 256 像素；512 × 384 可用于理想光学传感器对照。");
        var sensor = ToCore(request.Sensor); var isp = ToCore(request.Isp);
        CameraSensorPipeline.Validate(sensor, isp);
        if (request.SpectralModel == CameraSpectralModel.ThreeBand) ToCore(request.SpectralResponse)?.ValidateCameraCoverage();
        var materialPlan = request.SpectralModel == CameraSpectralModel.Continuous && request.SceneMode == CameraSceneMode.MaterialMap
            ? MaterialPlan(request) : null;
        var plan = materialPlan?.Common ?? (request.SpectralModel == CameraSpectralModel.Continuous ? ContinuousPlan(request) : null);
        var materialSampling = materialPlan?.DescribeSampling(request.Width, request.Height);
        if (plan is not null)
        {
            if (request.Optics == CameraOptics.CurrentLens && (long)request.Width * request.Height * plan.Nodes.Count > CameraContinuousSpectrum.MaximumPixelNodes)
                throw new ArgumentException("连续光谱的像素 × 积分节点超过一亿预算，请降低分辨率或减少采样点。");
            if (request.Optics == CameraOptics.CurrentLens)
            {
                if (plan.Nodes.Count > 32) throw new ArgumentException("当前镜头连续光谱最多 32 个积分节点；请减少曲线节点/积分范围，或使用理想光学。");
                AnalysisResourceLimits.ValidateConvolutionWork(request.Width + 16, request.Height + 16, 16, 16, 5, plan.Nodes.Count);
            }
        }
        Optic? optic; long revision; string name; CancellationTokenSource linked;
        lock (Gate)
        {
            if (request.Optics == CameraOptics.CurrentLens && modes.CurrentMode != OpticalWorkbenchMode.Sequential)
                throw new InvalidOperationException("当前镜头成像需要顺序模式；可选择理想光学对照。");
            optic = request.Optics == CameraOptics.CurrentLens ? Runtime.CurrentOptic.Clone() : null;
            revision = Workspace.Revision; name = Runtime.CurrentOptic.Name;
            linked = Workspace.LinkDocumentToken(cancellationToken);
        }
        using (linked)
            return await Task.Run(() =>
            {
                using var scope = ComputationCancellation.Push(linked.Token);
                var encoded = request.Source == CameraSource.ExternalImage
                    ? ImageFileLoader.LoadRgb(request.SourceFile)
                    : ImageSimulationEngine.CreateSourceImage((ImageSimulationSourcePattern)request.Source, request.Width, request.Height);
                var source = ResizeLinear(encoded, request.Width, request.Height);
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
                        var image = Propagate(optic, mask, request, sensor, new[] { wavelength });
                        modes.Add(image.Mode); fallbacks += image.Fallbacks; return image.Image;
                    };
                    var integrated = materialPlan is null ? CameraContinuousSpectrum.Integrate(source, sensor, plan, propagation)
                        : CameraContinuousSpectrum.IntegrateMaterials(source, sensor, materialPlan, propagation);
                    source = integrated.SourceMask; optical = integrated.OpticalPreview;
                    if (modes.Count > 0) opticalMode = string.Join(" + ", modes.Order(StringComparer.Ordinal));
                    sensorResult = CameraSensorPipeline.SimulatePhotoElectrons(integrated.PhotoElectrons, request.Width, request.Height, sensor, isp);
                }
                else
                {
                    if (optic is not null)
                    {
                        var image = Propagate(optic, source, request, sensor, CameraSensorPipeline.BandWavelengthsNanometers);
                        optical = image.Image; opticalMode = image.Mode; fallbacks = image.Fallbacks;
                    }
                    sensorResult = CameraSensorPipeline.Simulate(optical, sensor, isp, ToCore(request.SpectralResponse));
                }
                var maxDn = (1 << sensor.BitDepth) - 1;
                var electrons = Gray(sensorResult.PhotoElectrons, request.Width, request.Height, sensor.FullWellElectrons);
                var raw = Gray(sensorResult.RawDn.Select(x => (double)x).ToArray(), request.Width, request.Height, maxDn);
                linked.Token.ThrowIfCancellationRequested();
                return new CameraImagingResult(name, revision, request, Display(source, true), Display(optical, true),
                    electrons, raw, Display(sensorResult.ReferenceDeveloped), Display(sensorResult.Developed),
                    sensorResult.RawDn, sensorResult.ReferenceRawDn, sensorResult.PhotoElectrons,
                    sensorResult.MeanPhotoElectrons, sensorResult.RawNoiseRmseDn, sensorResult.SaturatedPixels,
                    opticalMode, fallbacks, materialPlan is not null ? MaterialScope : plan is not null ? ContinuousScope : request.SpectralResponse is null ? Scope : SpectralScope,
                    materialPlan is not null ? Diagnostics(request.MaterialScene!, materialPlan, materialSampling!) : plan is null ? null : Diagnostics(plan));
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
                ModelVersion = run.Integration?.Materials is not null ? "CameraLabV4-Materials" : run.Integration is not null ? "CameraLabV3-Continuous" : run.Request.SpectralResponse is null ? "CameraLabV1" : "CameraLabV2-QE",
                OpticalSampling = run.Request.Optics == CameraOptics.CurrentLens
                    ? "3x3 field; 16x16 PSF; 3 components; 12 pupil sampling parameter; 8px guard; 5x5 distortion grid; degree 3"
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
                run.Request.SensorIdentity?.Source ?? "User configured, not calibrated", run.Request.Sensor!, run.Request.SpectralResponse), SensorJsonOptions));
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

    private static RgbImage ResizeLinear(RgbImage source, int width, int height)
    {
        var result = new double[3, height, width];
        for (var y = 0; y < height; y++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var sy = (double)y * (source.Height - 1) / (height - 1); var y0 = (int)sy; var y1 = Math.Min(y0 + 1, source.Height - 1);
            for (var x = 0; x < width; x++)
            {
                var sx = (double)x * (source.Width - 1) / (width - 1); var x0 = (int)sx; var x1 = Math.Min(x0 + 1, source.Width - 1);
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

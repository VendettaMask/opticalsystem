using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Analysis.Imaging;
using OpticalSystemDesign.Core.NonSequential;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Application.Services;

internal sealed partial class CameraImagingService
{
    public Task<CameraNscDetectorInput> CaptureNonSequentialDetectorAsync(Guid detectorId, string? pathFilter = null,
        CancellationToken cancellationToken = default) => modes.CurrentMode == OpticalWorkbenchMode.NonSequential
            ? nonSequentialAnalysis.CaptureCameraDetectorAsync(detectorId, pathFilter, cancellationToken)
            : throw new InvalidOperationException("请切换到非序列模式后读取探测器。");

    private static CameraNscDetectorInput FreezeNscInput(CameraNscDetectorInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Format != CameraNscDetectorInput.FormatIdentifier || input.SchemaVersion != 1 || input.SessionId == Guid.Empty
            || input.SceneHash is null || input.SceneHash.Length != 64 || input.SceneHash.Any(c => !Uri.IsHexDigit(c))
            || input.BranchCount < 0 || !Enum.IsDefined(input.SplittingMode) || input.Detector is not { } d || d.Id == Guid.Empty
            || input.Wavelengths is null || input.Wavelengths.Count is < 1 or > 1024 || input.Bands is null
            || input.Bands.Count > input.Wavelengths.Count || input.Warnings is null || input.Warnings.Count > 1024
            || input.Wavelengths.Any(w => w is null) || input.Bands.Any(b => b is null)
            || input.TraceSettings is null || input.TracePassCount < 1 || input.SourceObjectIds is null || input.SourceObjectIds.Count > 100000)
            throw new InvalidDataException("非序列探测器输入的身份、格式或数量无效。");
        Label(d.Name, 2048, "Detector name"); Label(input.PathFilter, 4096, "Path filter");
        if (input.SavedPathFilter is not null) Label(input.SavedPathFilter, 4096, "Saved path filter");
        _ = NonSequentialPathFilter.Parse(input.PathFilter); _ = NonSequentialPathFilter.Parse(input.SavedPathFilter);
        CameraSensorPipeline.ValidateDimensions(d.PixelsX, d.PixelsY);
        if (input.Wavelengths.Count(w => w.IsPrimary) != 1 || input.Wavelengths.Where((w, i) => w.Index != i + 1
            || !double.IsFinite(w.Nanometers) || w.Nanometers is < 200 or > 20000 || !double.IsFinite(w.Weight) || w.Weight < 0).Any()
            || input.Bands.Select(b => b.WavelengthNumber).Distinct().Count() != input.Bands.Count)
            throw new InvalidDataException("非序列波长快照无效。");
        foreach (var w in input.Wavelengths) Label(w.Label, 2048, "Wavelength label");
        foreach (var warning in input.Warnings) Label(warning, 4096, "Trace warning");
        if (input.SurfaceBsdfs is { } models)
        {
            if (input.SurfaceBsdfProtocol is not ("LambertianReflection/1" or "MeasuredBsdfGrid/1" or "MeasuredBsdfGrid/2" or "MeasuredBsdfGrid/3" or "MeasuredBsdfGrid/4") || models.Count is < 1 or > 100000 || models.Any(m => m is null || m.Model is null || m.ObjectId == Guid.Empty || m.ObjectNumber < 1)
                || models.Select(m => m.ObjectId).Distinct().Count() != models.Count)
                throw new InvalidDataException("NSC BSDF 来源快照无效。");
            if ((models.Any(m => m.Model.MeasuredData is { } v4 && (v4.SpeosPeak is not null || v4.ZemaxXyz is not null || v4.SpeosSpectral is not null)) ? "MeasuredBsdfGrid/4" : models.Any(m => m.Model.MeasuredData is { } v3 && (v3.ZemaxDefinition is not null || v3.StoredQuantity != 0)) ? "MeasuredBsdfGrid/3"
                    : models.Any(m => m.Model.MeasuredData is { } d && (d.Coordinates != 0 || d.SourceFormat is not null)) ? "MeasuredBsdfGrid/2"
                    : models.Any(m => m.Model.Kind == Contracts.NonSequentialBsdfKind.Measured) ? "MeasuredBsdfGrid/1" : "LambertianReflection/1") != input.SurfaceBsdfProtocol
                || models.Sum(m => (long)((m.Model.MeasuredData?.Points?.Count ?? 0) + (m.Model.MeasuredData?.SpeosPeak?.Points?.Count ?? 0) + (m.Model.MeasuredData?.ZemaxXyz?.XPoints?.Count ?? 0) + (m.Model.MeasuredData?.ZemaxXyz?.ZPoints?.Count ?? 0))) > Core.Scattering.MeasuredBsdfData.MaximumDocumentPoints)
                throw new InvalidDataException("NSC BSDF 协议或总节点数无效。");
            foreach (var model in models)
            {
                Label(model.Name, 2048, "BSDF object name");
                _ = NonSequentialBsdfMapper.ToCore(model.Model);
                if (model.Model.MeasuredData?.Coordinates == 1)
                {
                    if (model.WorldMaterialAxisX is not { } x || model.WorldMaterialAxisY is not { } y
                        || new[] { x.X, x.Y, x.Z, y.X, y.Y, y.Z }.Any(v => !double.IsFinite(v))
                        || Math.Abs(x.X * x.X + x.Y * x.Y + x.Z * x.Z - 1) > 1e-12
                        || Math.Abs(y.X * y.X + y.Y * y.Y + y.Z * y.Z - 1) > 1e-12
                        || Math.Abs(x.X * y.X + x.Y * y.Y + x.Z * y.Z) > 1e-12)
                        throw new InvalidDataException("固定材料轴 BSDF 缺少有效的冻结世界基底。");
                }
            }
        }
        else if (input.SurfaceBsdfProtocol is not null) throw new InvalidDataException("NSC BSDF 协议缺少对应的表面来源。");
        if (input.BsdfStatistics is { } stats && (input.SurfaceBsdfProtocol is null || stats.SampleCount <= 0
            || new[] { stats.IncomingPowerWatts, stats.ReflectedPowerWatts, stats.TransmittedPowerWatts, stats.AbsorbedPowerWatts, stats.CosineSum,
                stats.CosineSquaredSum, stats.MinimumPdf, stats.MaximumPdf }.Any(v => !double.IsFinite(v) || v < 0)
            || stats.CosineSum > stats.SampleCount * (1 + 1e-12) || stats.CosineSquaredSum > stats.CosineSum + stats.SampleCount * 1e-12
            || stats.MinimumPdf <= 0 || stats.MaximumPdf < stats.MinimumPdf || input.SurfaceBsdfProtocol == "LambertianReflection/1" && stats.MaximumPdf > (1 + 1e-12) / Math.PI
            || stats.ReflectionSampleCount < 0 || stats.TransmissionSampleCount < 0 || stats.ReflectionSampleCount > stats.SampleCount - stats.TransmissionSampleCount
            || stats.MaterialAxisSampleCount < 0 || stats.MaterialAxisSampleCount > stats.SampleCount
            || !double.IsFinite(stats.MaterialDirectionXSum) || !double.IsFinite(stats.MaterialDirectionYSum)
            || Math.Abs(stats.MaterialDirectionXSum) > stats.MaterialAxisSampleCount * (1 + 1e-12)
            || Math.Abs(stats.MaterialDirectionYSum) > stats.MaterialAxisSampleCount * (1 + 1e-12)
            || Math.Abs(stats.IncomingPowerWatts - stats.ReflectedPowerWatts - stats.TransmittedPowerWatts - stats.AbsorbedPowerWatts)
                > Math.Max(1e-300, stats.IncomingPowerWatts * 1e-10)))
            throw new InvalidDataException("NSC BSDF 采样统计无效。");
        foreach (var v in new[] { d.WorldOrigin, d.WorldXAxis, d.WorldYAxis, d.WorldNormal })
            if (v is null || !double.IsFinite(v.X) || !double.IsFinite(v.Y) || !double.IsFinite(v.Z))
                throw new InvalidDataException("探测器坐标快照无效。");
        foreach (var b in input.Bands)
            if (b.WavelengthNumber < 1 || b.WavelengthNumber > input.Wavelengths.Count
                || b.WavelengthNanometers != input.Wavelengths[b.WavelengthNumber - 1].Nanometers)
                throw new InvalidDataException("探测功率与冻结波长表不一致。");
        var power = NscPower(input);
        return input with
        {
            Wavelengths = Array.AsReadOnly(input.Wavelengths.ToArray()), Warnings = Array.AsReadOnly(input.Warnings.ToArray()),
            SourceObjectIds = Array.AsReadOnly(input.SourceObjectIds.ToArray()),
            SurfaceBsdfs = input.SurfaceBsdfs is null ? null : Array.AsReadOnly(input.SurfaceBsdfs.Select(m => m with
                { Model = NonSequentialBsdfMapper.ToDto(NonSequentialBsdfMapper.ToCore(m.Model))! }).ToArray()),
            Bands = Array.AsReadOnly(input.Bands.Select((b, i) => b with { PixelPowerWatts = power.Bands[i].PixelBandPowerWatts }).ToArray())
        };
    }

    private static SpectralDetectorPower NscPower(CameraNscDetectorInput input) => new(input.Detector.PixelsX, input.Detector.PixelsY,
        input.Detector.WidthMillimeters / input.Detector.PixelsX * 1000,
        input.Detector.HeightMillimeters / input.Detector.PixelsY * 1000, DetectorRowOrder.NegativeLocalYFirst,
        input.Bands.Select(b => new SpectralDetectorPowerBand(b.WavelengthNanometers, b.PixelPowerWatts)));

    private static CameraNscDetectorInput ValidateNscRequest(CameraImagingRequest r, string version)
    {
        if (version != CameraExperiment.NonSequentialEngineVersion || r.Optics != CameraOptics.DetectorInput
            || r.SpectralModel != CameraSpectralModel.DetectorBands || r.PixelSampling is not null
            || r.Scene3D is not null || r.Render3D is not null || r.SceneMode != CameraSceneMode.Uniform || r.MaterialScene is not null)
            throw new InvalidDataException("非序列输入须使用已计算的探测器功率，不再次传播或积分像元面积。");
        var input = FreezeNscInput(r.NscDetector ?? throw new InvalidDataException("请先读取非序列探测器结果。"));
        if (r.Width != input.Detector.PixelsX || r.Height != input.Detector.PixelsY)
            throw new InvalidDataException("相机尺寸必须与冻结探测器完全一致，不重采样物理功率。");
        var response = ToCore(r.SpectralResponse) ?? throw new InvalidDataException("非序列输入需要覆盖探测波长的绝对 QE 曲线。");
        foreach (var w in input.Wavelengths) _ = response.Evaluate(0, w.Nanometers);
        // Shared conversion verifies square-pixel pitch and applies neither area nor spectral weights twice.
        _ = SpectralDetectorSensor.IntegratePhotoElectrons(NscPower(input), response, ToCore(r.Sensor!));
        return input;
    }

    private async Task<CameraImagingResult> SimulateNonSequentialAsync(CameraImagingRequest request,
        CameraExperimentInputs? inputs, string? savedName, CancellationToken token)
    {
        var frozen = ValidateNscRequest(request, inputs?.EngineVersion ?? CameraExperiment.NonSequentialEngineVersion);
        request = request with { NscDetector = frozen };
        long revision; string name; CancellationTokenSource linked;
        lock (Gate)
        {
            revision = Workspace.Revision; name = savedName ?? Runtime.CurrentNonSequentialDocument.Name;
            linked = Workspace.LinkDocumentToken(token);
        }
        using (linked) return await Task.Run(() =>
        {
            using var cancellation = ComputationCancellation.Push(linked.Token);
            var power = NscPower(frozen); var s = ToCore(request.Sensor!);
            var output = SpectralDetectorSensor.Simulate(power, ToCore(request.SpectralResponse)!, s, ToCore(request.Isp!));
            var preview = new double[3, request.Height, request.Width];
            var area = power.PixelWidthMicrometers * power.PixelHeightMicrometers * 1e-12;
            foreach (var band in power.Bands)
                for (var y = 0; y < request.Height; y++) for (var x = 0; x < request.Width; x++)
                    for (var c = 0; c < 3; c++) preview[c, y, x] += band.PixelBandPowerWatts[(request.Height - 1 - y) * request.Width + x] / area / .02;
            var image = Display(new RgbImage(preview), true);
            linked.Token.ThrowIfCancellationRequested();
            return new CameraImagingResult(name, revision, request, image, image,
                Gray(output.PhotoElectrons, request.Width, request.Height, s.FullWellElectrons),
                Gray(output.RawDn.Select(v => (double)v).ToArray(), request.Width, request.Height, (1 << s.BitDepth) - 1),
                Display(output.ReferenceDeveloped), Display(output.Developed), output.RawDn, output.ReferenceRawDn, output.PhotoElectrons,
                output.MeanPhotoElectrons, output.RawNoiseRmseDn, output.SaturatedPixels,
                "Shared Core NSC saved paths -> absolute spectral detector -> sensor/RAW/ISP", 0,
                "Frozen NSC band-integrated W/pixel, negative-local-Y-first converted once to top-first Bayer. "
                + "Includes exactly the selected saved paths; no additional sequential image/PSF, area, throughput or wavelength weight. "
                + "One exposure/QE/noise/ADC/ISP. Preview is achromatic total irradiance at fixed 0.02 W/m² white. "
                + "Optical BSDF model provenance is retained when present. No angular QE or coherent field summation; optical Monte Carlo sampling is distinct from sensor noise. "
                + "Recompute redevelops frozen detector power, not a new NSC trace.",
                Inputs: new(CameraExperiment.NonSequentialEngineVersion, LensDocumentName: name, LensSourceRevision: frozen.SourceRevision));
        }, linked.Token).ConfigureAwait(false);
    }
}

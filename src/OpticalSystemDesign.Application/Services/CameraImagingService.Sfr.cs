using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis.Imaging;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Services;
using SkiaSharp;

namespace OpticalSystemDesign.Application.Services;

internal sealed partial class CameraImagingService
{
    private const int MaximumSfrBytes = 64 * 1024 * 1024;
    private static readonly JsonSerializerOptions SfrJson = new() { WriteIndented = true, MaxDepth = 40 };

    public Task<CameraSfrResult> SimulateSlantedEdgeAsync(CameraSfrRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        MonochromeSlantedEdge.ValidateSettings(ToSfrCore(request));
        Optic optic; OpticalDocumentSnapshot document; CancellationTokenSource linked;
        lock (Gate)
        {
            if (modes.CurrentMode != OpticalWorkbenchMode.Sequential) throw new InvalidOperationException("斜边仿真需要当前顺序镜头。");
            optic = Runtime.CurrentOptic.Clone(); document = Workspace.GetDocumentSnapshot();
            linked = Workspace.LinkDocumentToken(cancellationToken);
        }
        var snapshot = JsonSerializer.Serialize(optic.ToSnapshot(), LensJsonOptions);
        return RunSfrAsync(optic, snapshot, document, request, linked);
    }

    public Task<CameraSfrResult> RecomputeSlantedEdgeAsync(CameraSfrResult result, CancellationToken cancellationToken = default)
    {
        ValidateSfrResult(result);
        if (!CameraSfrResult.CanRecompute(result.Algorithm))
            throw new NotSupportedException("旧版斜边实验仅供查看，未使用当前完整瞳孔对照与瞳孔单元积分；请显式使用当前镜头重新生成实验。");
        return RunSfrAsync(RestoreLens(result.LensSnapshotJson), result.LensSnapshotJson, result.SourceDocument!,
            result.Request, CancellationTokenSource.CreateLinkedTokenSource(cancellationToken));
    }

    private static async Task<CameraSfrResult> RunSfrAsync(Optic optic, string snapshot, OpticalDocumentSnapshot document,
        CameraSfrRequest request, CancellationTokenSource linked)
    {
        using (linked) return await Task.Run(() =>
        {
            using var scope = ComputationCancellation.Push(linked.Token);
            var result = MonochromeSlantedEdge.Run(optic, ToSfrCore(request), request.CheckSampling, request.DifferenceThreshold);
            return new CameraSfrResult(CameraSfrResult.FormatIdentifier, 1,
                request.UseLensWavelengths ? CameraSfrResult.PolychromeAlgorithm : CameraSfrResult.CurrentAlgorithm, document.Name, document.Revision,
                snapshot, SfrHash(snapshot), request, SfrFrame(result.Baseline, request),
                Array.AsReadOnly(result.Sampling.Select(c => new CameraSfrSamplingComparison(c.Axis, SfrRequest(c.Requested, request),
                    c.Refined is null ? null : SfrFrame(c.Refined, request), c.MaximumImageDifference, c.MaximumSfrDifference,
                    c.MaximumMtfDifference, c.RelativeReadoutIntegralDifference, c.WithinCriteria, c.Message)
                    { MaximumFullPupilMtfDifference = c.MaximumFullPupilMtfDifference }).ToArray()), result.SamplingConverged)
                { SourceDocument = document };
        }, linked.Token).ConfigureAwait(false);
    }

    private static SlantedEdgeSettings ToSfrCore(CameraSfrRequest r) => new(r.WavelengthNanometers, r.FieldNumber,
        r.PixelPitchMicrometers, r.Width, r.Height, r.EdgeAngleDegrees, r.DarkLevel, r.BrightLevel, r.EdgeOffsetPixels,
        r.PupilSampling, r.ImageSamplesPerPixel, r.PsfRadiusPixels, new(r.RoiX, r.RoiY, r.RoiWidth, r.RoiHeight), r.EdgeHalfWindowPixels, r.UseLensWavelengths);
    private static CameraSfrRequest SfrRequest(SlantedEdgeSettings s, CameraSfrRequest r) => r with
        { PupilSampling = s.PupilSampling, ImageSamplesPerPixel = s.ImageSamplesPerPixel, PsfRadiusPixels = s.PsfRadiusPixels };
    private static CameraSfrFrame SfrFrame(SlantedEdgeFrame f, CameraSfrRequest r) => new(SfrRequest(f.Settings, r),
        Array.AsReadOnly(f.Source.ToArray()), Array.AsReadOnly(f.LinearPixels.ToArray()),
        Array.AsReadOnly(f.Measurement.Curve.Select((p, i) => new CameraSfrPoint(p.CyclesPerPixel, p.CyclesPerMillimeter,
            p.Response, f.OpticalMtf[i].Response, f.PixelIntegratedMtf[i].Response,
            f.FullPupilMtf?[i].Response, f.FullPupilPixelIntegratedMtf?[i].Response)).ToArray()),
        Array.AsReadOnly(f.Measurement.Esf.Select((p, i) => new CameraSfrProfilePoint(p.DistancePixels, p.Value, f.Measurement.Lsf[i].Value)).ToArray()),
        f.Measurement.EdgeSlope, f.Measurement.EdgeIntercept, f.Measurement.EdgeFitRmsPixels, f.Measurement.Contrast,
        f.Measurement.Mtf50CyclesPerPixel, f.Measurement.Mtf10CyclesPerPixel, f.PsfSpacingMicrometers, f.PsfGridSize,
        f.RelativePsfReadoutIntegral, f.MaximumSfrReferenceDifference)
        { MaximumSfrFullPupilDifference = f.MaximumSfrFullPupilDifference, MaximumWindowFullPupilDifference = f.MaximumWindowFullPupilDifference,
            ReferenceWavelengthNanometers = f.ReferenceWavelengthNanometers,
            Spectrum = f.Spectrum is null ? null : Array.AsReadOnly(f.Spectrum.Select(b => new CameraSfrSpectralBand(
                b.WavelengthNumber, b.WavelengthNanometers, b.Weight, b.NormalizedWeight, b.IsPrimary)).ToArray()) };
    private static string SfrHash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    public Task ExportSlantedEdgeAsync(CameraSfrResult result, string path, CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        ValidateSfrResult(result);
        var payload = JsonSerializer.SerializeToUtf8Bytes(result, SfrJson);
        if (payload.Length > MaximumSfrBytes) throw new InvalidDataException("斜边实验超过 64 MiB。");
        // Freeze before creating any user-visible output file.
        result = JsonSerializer.Deserialize<CameraSfrResult>(payload, SfrJson)!;
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            Write("experiment.json", payload);
            Write("sha256.txt", Encoding.UTF8.GetBytes(Convert.ToHexStringLower(SHA256.HashData(payload))));
            var image = new StringBuilder("x,y,source_linear,pixel_linear\n");
            for (var y = 0; y < result.Request.Height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (var x = 0; x < result.Request.Width; x++)
                {
                    var i = y * result.Request.Width + x;
                    image.Append(CultureInfo.InvariantCulture, $"{x},{y},{result.Baseline.Source[i]:G17},{result.Baseline.LinearPixels[i]:G17}\n");
                }
            }
            Write("linear-pixels.csv", Encoding.UTF8.GetBytes(image.ToString()));
            var curves = new StringBuilder("cycles_per_pixel,cycles_per_mm,sfr,optical_mtf,pixel_integrated_mtf,full_pupil_mtf,full_pupil_pixel_integrated_mtf\n");
            foreach (var p in result.Baseline.Curve)
                curves.Append(CultureInfo.InvariantCulture, $"{p.CyclesPerPixel:G17},{p.CyclesPerMillimeter:G17},{p.Sfr:G17},{p.OpticalMtf:G17},{p.PixelIntegratedMtf:G17},{p.FullPupilMtf:G17},{p.FullPupilPixelIntegratedMtf:G17}\n");
            Write("sfr.csv", Encoding.UTF8.GetBytes(curves.ToString()));
            using var bitmap = new SKBitmap(result.Request.Width, result.Request.Height);
            for (var y = 0; y < bitmap.Height; y++) for (var x = 0; x < bitmap.Width; x++)
            {
                var value = (byte)Math.Round(Math.Clamp(result.Baseline.LinearPixels[y * bitmap.Width + x], 0, 1) * 255);
                bitmap.SetPixel(x, y, new SKColor(value, value, value));
            }
            using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100); Write("linear-preview.png", png.ToArray());
            Write("README.txt", Encoding.UTF8.GetBytes("Relative linear irradiance; local isoplanatic traced-pupil Fourier PSF; full square pixels. No CFA, ADC, noise, ISP or gamma.\n"
                + "SFR uses floating-point pixel arrays, not the 8-bit PNG preview. ROI coordinates refer to the original sensor.\n"
                + $"Model: {result.Algorithm}. Monochrome versions 1/2 are view-only; monochrome 3 and polychrome 1 support frozen replay.\n"
                + "Optical and pixel-integrated MTF are direct Fourier sections of the same finite PSF along the fitted edge normal.\n"
                + "Full-pupil MTF is independently computed by pupil autocorrelation, without the finite PSF window. Both pixel references include the square pixel exactly once.\n"
                + "Polychrome mode freezes the lens wavelength rows and relative image-energy weights; zero weights are excluded, all-zero weights rejected.\n"
                + "All bands share the lens primary-wavelength chief reference. Mix linear PSF intensities before crop normalization; mix complex OTFs before magnitude. No extra QE or photon weighting.\n"
                + "Sampling checks double one axis at a time; incomplete checks are not passes. Numerical convergence is not ISO certification.\n"));
            void Write(string name, byte[] data)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var stream = archive.CreateEntry(name, CompressionLevel.Fastest).Open(); stream.Write(data);
            }
        }
        await BoundedFile.WriteAllBytesAtomicAsync(path, memory.ToArray(), MaximumSfrBytes, "Slanted edge", cancellationToken).ConfigureAwait(false);
    }, cancellationToken);

    public async Task<CameraSfrResult> OpenSlantedEdgeAsync(string path, CancellationToken cancellationToken = default)
    {
        var bytes = await BoundedFile.ReadAllBytesAsync(path, MaximumSfrBytes, "Slanted edge", cancellationToken).ConfigureAwait(false);
        using var memory = new MemoryStream(bytes); using var archive = new ZipArchive(memory, ZipArchiveMode.Read);
        var entry = archive.GetEntry("experiment.json") ?? throw new InvalidDataException("缺少斜边实验数据。");
        var digest = archive.GetEntry("sha256.txt") ?? throw new InvalidDataException("缺少实验校验。");
        if (archive.Entries.Count != 6 || archive.Entries.Select(e => e.FullName).Distinct().Count() != 6
            || archive.Entries.Any(e => e.FullName is not ("experiment.json" or "sha256.txt" or "linear-pixels.csv" or "sfr.csv" or "linear-preview.png" or "README.txt"))
            || entry.Length is < 1 or > MaximumSfrBytes || digest.Length is < 1 or > 128)
            throw new InvalidDataException("无效或超限的斜边实验包。");
        var payload = new byte[(int)entry.Length];
        using (var stream = entry.Open()) await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(digest.Open());
        if ((await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false)).Trim() != Convert.ToHexStringLower(SHA256.HashData(payload)))
            throw new InvalidDataException("斜边实验校验失败。");
        var result = JsonSerializer.Deserialize<CameraSfrResult>(payload, SfrJson) ?? throw new InvalidDataException("空斜边实验。");
        ValidateSfrResult(result); return result;
    }

    private static void ValidateSfrResult(CameraSfrResult r)
    {
        ArgumentNullException.ThrowIfNull(r);
        if (r.Format != CameraSfrResult.FormatIdentifier || r.SchemaVersion != 1
            || r.Algorithm is not (CameraSfrResult.CurrentAlgorithm or CameraSfrResult.WindowAlgorithm or CameraSfrResult.PreviousAlgorithm or CameraSfrResult.PolychromeAlgorithm)
            || r.Request is null || r.Baseline is null || r.LensSnapshotJson is null || r.LensSnapshotJson.Length > 16 * 1024 * 1024
            || r.LensSha256 != SfrHash(r.LensSnapshotJson) || r.Sampling is null || r.Sampling.Count > 3
            || r.Request.Width is < 32 or > 256 || r.Request.Height is < 32 or > 256)
            throw new InvalidDataException("斜边实验身份、镜头快照或尺寸无效。");
        if (r.Request.UseLensWavelengths != (r.Algorithm == CameraSfrResult.PolychromeAlgorithm))
            throw new InvalidDataException("斜边实验的波长模式与算法身份不一致。");
        if (r.SourceDocument is null || r.SourceDocument.Name != r.DocumentName || r.SourceDocument.Revision != r.SourceRevision)
            throw new InvalidDataException("斜边实验缺少一致的冻结文档来源。");
        try { MonochromeSlantedEdge.ValidateSettings(ToSfrCore(r.Request)); }
        catch (ArgumentException ex) { throw new InvalidDataException("斜边实验参数无效。", ex); }
        IReadOnlyList<CameraSfrSpectralBand>? expectedSpectrum = null; double? expectedReference = null;
        if (r.Request.UseLensWavelengths)
        {
            try
            {
                var lens = RestoreLens(r.LensSnapshotJson);
                expectedSpectrum = MonochromeSlantedEdge.LensSpectrum(lens).Select(b => new CameraSfrSpectralBand(
                    b.WavelengthNumber, b.WavelengthNanometers, b.Weight, b.NormalizedWeight, b.IsPrimary)).ToArray();
                expectedReference = (lens.Wavelengths.FirstOrDefault(w => w.IsPrimary) ?? lens.Wavelengths[0]).Nanometers;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or JsonException)
            { throw new InvalidDataException("冻结镜头的斜边光谱无效。", ex); }
        }
        if (!double.IsFinite(r.Request.DifferenceThreshold) || r.Request.DifferenceThreshold is <= 0 or > .1
            || r.Baseline.Sampling != r.Request || r.Sampling.Any(c => c is null)
            || r.Request.CheckSampling && (r.Sampling.Count != 3 || !r.Sampling.Select(c => c.Axis).SequenceEqual(new[] { "Pupil", "ImagePlane", "PsfWindow" })
                || r.SamplingConverged != r.Sampling.All(c => c.Refined is not null && c.WithinCriteria))
            || !r.Request.CheckSampling && (r.Sampling.Count != 0 || r.SamplingConverged is not null))
            throw new InvalidDataException("采样检查身份或状态无效。");
        foreach (var comparison in r.Sampling)
        {
            var expected = comparison.Axis switch
            {
                "Pupil" => r.Request with { PupilSampling = r.Request.PupilSampling * 2 },
                "ImagePlane" => r.Request with { ImageSamplesPerPixel = r.Request.ImageSamplesPerPixel * 2 },
                _ => r.Request with { PsfRadiusPixels = r.Request.PsfRadiusPixels * 2 }
            };
            if (comparison.Requested != expected || comparison.Refined is null && comparison.WithinCriteria
                || comparison.Refined is { } refined && refined.Sampling != expected
                || new[] { comparison.MaximumImageDifference, comparison.MaximumSfrDifference, comparison.MaximumMtfDifference,
                    comparison.RelativeReadoutIntegralDifference }.Any(v => v.HasValue && (!double.IsFinite(v.Value) || v < 0))
                || comparison.WithinCriteria && (comparison.MaximumImageDifference is null || comparison.MaximumSfrDifference is null
                    || comparison.MaximumMtfDifference is null || comparison.RelativeReadoutIntegralDifference is null
                    || comparison.MaximumImageDifference > r.Request.DifferenceThreshold || comparison.MaximumSfrDifference > r.Request.DifferenceThreshold
                    || comparison.MaximumMtfDifference > r.Request.DifferenceThreshold
                    || comparison.Axis == "PsfWindow" && comparison.RelativeReadoutIntegralDifference > r.Request.DifferenceThreshold))
                throw new InvalidDataException("采样检查参数或差值无效。");
            if (CameraSfrResult.CanRecompute(r.Algorithm) && comparison.Refined is not null
                && (comparison.MaximumFullPupilMtfDifference is not { } fullDifference || !double.IsFinite(fullDifference) || fullDifference < 0
                    || comparison.WithinCriteria && fullDifference > r.Request.DifferenceThreshold))
                throw new InvalidDataException("缺少有效的完整瞳孔采样差值。");
        }
        foreach (var frame in new[] { r.Baseline }.Concat(r.Sampling.Where(c => c.Refined is not null).Select(c => c.Refined!)))
        {
            if (r.Request.UseLensWavelengths
                ? frame.Spectrum is null || !frame.Spectrum.SequenceEqual(expectedSpectrum!) || frame.ReferenceWavelengthNanometers != expectedReference
                : frame.Spectrum is not null || frame.ReferenceWavelengthNanometers is not null)
                throw new InvalidDataException("斜边波长/权重或公共参考与冻结镜头不一致。");
            if (frame.Sampling is null || frame.Source is null || frame.LinearPixels is null || frame.Curve is null || frame.Profile is null
                || frame.Source.Count != r.Request.Width * r.Request.Height || frame.LinearPixels.Count != frame.Source.Count
                || frame.Curve.Count != 129 || frame.Profile.Count is < 129 or > 769
                || frame.Source.Concat(frame.LinearPixels).Any(v => !double.IsFinite(v) || v < 0)
                || frame.Curve.Where((p, i) => p is null || !double.IsFinite(p.Sfr) || p.Sfr < 0
                    || !double.IsFinite(p.OpticalMtf) || p.OpticalMtf < 0 || !double.IsFinite(p.PixelIntegratedMtf) || p.PixelIntegratedMtf < 0
                    || p.CyclesPerPixel != i / 256.0 || p.CyclesPerMillimeter != p.CyclesPerPixel * 1000 / r.Request.PixelPitchMicrometers).Any()
                || frame.Profile.Any(p => p is null || !double.IsFinite(p.DistancePixels) || !double.IsFinite(p.Esf) || !double.IsFinite(p.Lsf))
                || new[] { frame.EdgeSlope, frame.EdgeIntercept, frame.EdgeFitRmsPixels, frame.Contrast, frame.PsfSpacingMicrometers,
                    frame.RelativePsfReadoutIntegral, frame.MaximumSfrReferenceDifference }.Any(v => !double.IsFinite(v))
                || new[] { frame.Mtf50CyclesPerPixel, frame.Mtf10CyclesPerPixel }.Any(v => v.HasValue && (!double.IsFinite(v.Value) || v is < 0 or > .5)))
                throw new InvalidDataException("斜边实验包含无效线性图像或曲线。");
            if (CameraSfrResult.CanRecompute(r.Algorithm))
            {
                if (frame.Curve.Any(p => p.FullPupilMtf is not { } m || !double.IsFinite(m) || m < 0
                        || p.FullPupilPixelIntegratedMtf is not { } v || !double.IsFinite(v) || v < 0)
                    || frame.MaximumSfrFullPupilDifference is not { } sfrDifference || !double.IsFinite(sfrDifference) || sfrDifference < 0
                    || frame.MaximumWindowFullPupilDifference is not { } windowDifference || !double.IsFinite(windowDifference) || windowDifference < 0
                    || Math.Abs(sfrDifference - frame.Curve.Max(p => Math.Abs(p.Sfr - p.FullPupilPixelIntegratedMtf!.Value))) > 1e-10
                    || Math.Abs(windowDifference - frame.Curve.Max(p => Math.Abs(p.OpticalMtf - p.FullPupilMtf!.Value))) > 1e-10)
                    throw new InvalidDataException("完整瞳孔 MTF 对照缺失或与曲线不一致。");
            }
        }
        if (CameraSfrResult.CanRecompute(r.Algorithm))
            foreach (var comparison in r.Sampling.Where(c => c.Refined is not null))
            {
                var difference = r.Baseline.Curve.Zip(comparison.Refined!.Curve,
                    (a, b) => Math.Abs(a.FullPupilPixelIntegratedMtf!.Value - b.FullPupilPixelIntegratedMtf!.Value)).Max();
                if (Math.Abs(comparison.MaximumFullPupilMtfDifference!.Value - difference) > 1e-10)
                    throw new InvalidDataException("完整瞳孔采样差值与曲线不一致。");
            }
    }
}


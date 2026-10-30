using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Core.Analysis.Imaging;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Application.Services;

internal sealed partial class CameraImagingService
{
    private const int MaximumSamplingBytes = 16 * 1024 * 1024;

    public async Task<CameraSamplingReport> CheckSamplingAsync(CameraImagingResult run, CameraSamplingRegion region,
        CameraSamplingAxis axis, CameraSamplingReport? previous = null, CancellationToken cancellationToken = default, bool completeLadder = false)
    {
        ArgumentNullException.ThrowIfNull(run); ArgumentNullException.ThrowIfNull(region); cancellationToken.ThrowIfCancellationRequested();
        ValidateSamplingSource(run); ValidateSamplingRegion(region, run.Request);
        if (!Enum.IsDefined(axis)) throw new ArgumentException("无效采样检查项目。");
        // Deep-freeze caller-owned data before background computation; never reread live lens/scene inputs.
        var bytes = JsonSerializer.SerializeToUtf8Bytes(run, ExperimentJsonOptions);
        var frozen = JsonSerializer.Deserialize<CameraImagingResult>(bytes, ExperimentJsonOptions)!;
        var fingerprint = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (previous is not null)
        {
            ValidateSamplingReport(previous, frozen);
            var reportBytes = JsonSerializer.SerializeToUtf8Bytes(previous, ExperimentJsonOptions);
            if (reportBytes.Length > MaximumSamplingBytes) throw new InvalidDataException("采样报告超过保存上限。");
            previous = JsonSerializer.Deserialize<CameraSamplingReport>(reportBytes, ExperimentJsonOptions)!;
        }
        if (previous?.Region != region) previous = null;
        var existing = previous;
        CancellationTokenSource linked;
        lock (Gate) linked = Workspace.LinkDocumentToken(cancellationToken);
        using (linked) return await Task.Run(() =>
        {
            using var scope = ComputationCancellation.Push(linked.Token);
            var request = frozen.Request;
            var baselineSettings = new CameraSamplingSettings(request.Render3D!.Diffraction!, request.PixelSampling);
            var baseline = existing?.Baseline ?? Sample(baselineSettings);
            var savedBands = SceneBands.Select((_, b) => Difference(Crop(frozen.Scene3D!.Bands[b].PixelPowerWatts, region, request.Width),
                baseline.Bands[b].PixelPowerWatts, region)).ToArray();
            var savedElectrons = Difference(Crop(frozen.PhotoElectrons, region, request.Width), baseline.PhotoElectrons, region);
            var target = Refine(baselineSettings, axis);
            CameraSamplingComparison comparison;
            if (target == baselineSettings)
                comparison = new(axis, CameraSamplingComparisonStatus.AtLimit, target, "此项已到当前实现上限，未评估更密采样。");
            else if (savedBands.Any(m => m.MaximumAbsolute != 0) || savedElectrons.MaximumAbsolute != 0)
                comparison = new(axis, CameraSamplingComparisonStatus.Unavailable, target,
                    "局部基准与保存 B 不同；先按冻结输入复算 B，不能把复算差异混入采样比较。");
            else
            {
                comparison = Compare(baseline, target);
                if (completeLadder && comparison.Refined is { } first)
                {
                    var further = new List<CameraSamplingComparison>(); var current = first;
                    while (Refine(current.Settings, axis) is var next && next != current.Settings)
                    {
                        var step = Compare(current, next); further.Add(step);
                        if (step.Refined is not { } frame) break;
                        current = frame;
                    }
                    if (further.Count > 0) comparison = comparison with { FurtherRefinements = further.AsReadOnly() };
                }
            }
            var comparisons = (existing?.Comparisons ?? []).Where(c => c.Axis != axis).Append(comparison).OrderBy(c => c.Axis).ToArray();
            linked.Token.ThrowIfCancellationRequested();
            return new CameraSamplingReport(CameraSamplingReport.FormatIdentifier, 1, fingerprint, frozen.Inputs!.EngineVersion,
                region, baseline, Array.AsReadOnly(savedBands), savedElectrons, Array.AsReadOnly(comparisons));

            CameraSamplingComparison Compare(CameraSamplingSnapshot basis, CameraSamplingSettings requested)
            {
                try
                {
                    var refined = Sample(requested);
                    return new(axis, CameraSamplingComparisonStatus.Compared, requested,
                        "相邻层级的局部逐点比较，不是精度认证；未删减源点或瞳面点，原物理容差保持。", refined,
                        SceneBands.Select((_, b) => Difference(basis.Bands[b].PixelPowerWatts, refined.Bands[b].PixelPowerWatts, region)).ToArray(),
                        Difference(basis.PhotoElectrons, refined.PhotoElectrons, region),
                        Difference(basis.NoiselessRawDn.Select(v => (double)v).ToArray(), refined.NoiselessRawDn.Select(v => (double)v).ToArray(), region));
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException)
                { return new(axis, CameraSamplingComparisonStatus.Unavailable, requested, ex.Message); }
            }

            CameraSamplingSnapshot Sample(CameraSamplingSettings settings)
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var optic = RestoreLens(frozen.Inputs!.LensSnapshotJson!);
                var scene = CompileScene(request.Scene3D!, optic.SurfaceGroup.Items[1].CoordinateSystem.Origin.Z);
                var image = DiffractionCameraRenderer.RenderRegion(optic, scene,
                    new(request.Width, request.Height, request.Sensor!.PixelPitchMicrometers, ToCore(settings.PixelSampling),
                        request.Render3D!.PupilRings, request.Render3D.PupilArms, request.Render3D.BareInterfaceFresnel),
                    ToCore(settings.Diffraction), new(region.X, region.Y, region.Width, region.Height));
                var sensor = ToCore(request.Sensor);
                var originalPattern = sensor.Pattern;
                sensor = sensor with { Pattern = Enum.GetValues<BayerPattern>().Single(p =>
                    Enumerable.Range(0, 4).All(i => CameraSensorPipeline.ColorAt(p, i / 2, i % 2)
                        == CameraSensorPipeline.ColorAt(originalPattern, region.Y + i / 2, region.X + i % 2))) };
                var photo = SpectralDetectorSensor.IntegratePhotoElectrons(image.Result.Image.DetectorPower, SceneResponse(request), sensor,
                    settings.PixelSampling?.QeIncludesFillFactor == false ? QuantumEfficiencyAreaReference.CollectionArea : QuantumEfficiencyAreaReference.WholePixel);
                var sensorOutput = CameraSensorPipeline.SimulatePhotoElectrons(photo, region.Width, region.Height, sensor, ToCore(request.Isp!));
                var d = image.Result.Diffraction;
                return new(settings, Array.AsReadOnly(image.Result.Image.DetectorPower.Bands.Select(b =>
                        new CameraDetectorBand(b.WavelengthNanometers, b.PixelBandPowerWatts)).ToArray()), photo,
                    Array.AsReadOnly(sensorOutput.ReferenceRawDn), (CameraBayerPattern)sensor.Pattern,
                    new(d.SurfaceSamples, d.ScalarCellEvaluations, d.OccludedPupilCells, d.SourceAreaSquareMillimeters,
                        d.CollectedPowerWatts, d.SamplesPerObject, d.MaximumGeometricSpreadPixels, d.RequiredSourceGuardPixels), watch.Elapsed.TotalSeconds,
                    new(image.Result.Image.LaunchedRays, image.Result.Image.SurfaceInteractions, d.AvoidedAuxiliaryRays,
                        "RegionExactSupport/2", DiffractionCameraRenderer.MaximumRegionalRays, DiffractionCameraRenderer.MaximumRegionalSurfaceInteractions,
                        d.SourceCandidateVisits, DiffractionCameraRenderer.MaximumRegionalCandidateVisits));
            }
        }, linked.Token).ConfigureAwait(false);
    }

    public Task ExportSamplingAsync(CameraImagingResult run, CameraSamplingReport report, string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); ValidateSamplingReport(report, run);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(report, ExperimentJsonOptions);
        return BoundedFile.WriteAllBytesAtomicAsync(path, bytes, MaximumSamplingBytes, "Camera sampling comparison", cancellationToken);
    }

    private static CameraSamplingSettings Refine(CameraSamplingSettings settings, CameraSamplingAxis axis) => axis switch
    {
        CameraSamplingAxis.Pupil => settings with { Diffraction = settings.Diffraction with { PupilSamplesAcross = Math.Min(64, settings.Diffraction.PupilSamplesAcross * 2) } },
        CameraSamplingAxis.Scene => settings with { Diffraction = settings.Diffraction with { SceneSubdivisions = Math.Min(4, settings.Diffraction.SceneSubdivisions * 2) } },
        CameraSamplingAxis.Pixel => settings with { PixelSampling = (settings.PixelSampling ?? new()) with
            { SamplesPerAxis = Math.Min(4, (settings.PixelSampling?.SamplesPerAxis ?? 1) * 2) } },
        CameraSamplingAxis.Window => settings with { Diffraction = settings.Diffraction with { PsfRadiusPixels = Math.Min(32, settings.Diffraction.PsfRadiusPixels * 2) } },
        CameraSamplingAxis.Guard => settings with { Diffraction = settings.Diffraction with { SourceGuardPixels = Math.Min(32, settings.Diffraction.SourceGuardPixels + 8) } },
        _ => throw new ArgumentException("无效采样项目。")
    };

    private static double[] Crop(IReadOnlyList<double> values, CameraSamplingRegion region, int fullWidth) =>
        Enumerable.Range(0, region.Height).SelectMany(y => Enumerable.Range(0, region.Width)
            .Select(x => values[(region.Y + y) * fullWidth + region.X + x])).ToArray();

    private static CameraSamplingDifference Difference(IReadOnlyList<double> baseline, IReadOnlyList<double> refined, CameraSamplingRegion region)
    {
        if (baseline.Count != refined.Count || baseline.Count != region.Width * region.Height) throw new InvalidDataException("比较数组尺寸不同。");
        double sumAbs = 0, max = -1, squares = 0, baseAbs = 0, baseSquares = 0, baseSum = 0, refinedSum = 0; var worst = 0;
        for (var i = 0; i < baseline.Count; i++)
        {
            var delta = Math.Abs(refined[i] - baseline[i]); sumAbs += delta; squares += delta * delta;
            baseAbs += Math.Abs(baseline[i]); baseSquares += baseline[i] * baseline[i]; baseSum += baseline[i]; refinedSum += refined[i];
            if (delta > max) { max = delta; worst = i; }
        }
        return new(sumAbs / baseline.Count, max, Math.Sqrt(squares / baseline.Count),
            baseAbs > 0 ? sumAbs / baseAbs : sumAbs == 0 ? 0 : null,
            baseSquares > 0 ? Math.Sqrt(squares / baseSquares) : squares == 0 ? 0 : null,
            baseSum, refinedSum, region.X + worst % region.Width, region.Y + worst / region.Width);
    }

    private static void ValidateSamplingSource(CameraImagingResult run)
    {
        ValidateRun(run);
        if (run.Request.Source != CameraSource.Scene3D || run.Request.Aberration != CameraAberration.Diffraction
            || run.Inputs?.EngineVersion != CameraExperiment.CurrentEngineVersion)
            throw new InvalidDataException("采样检查需要当前 V8 三维衍射的冻结结果 B。");
    }

    private static void ValidateSamplingRegion(CameraSamplingRegion r, CameraImagingRequest request)
    {
        if (r.X < 0 || r.Y < 0 || r.Width is < 4 or > 16 || r.Height is < 4 or > 16
            || (long)r.X + r.Width > request.Width || (long)r.Y + r.Height > request.Height)
            throw new InvalidDataException("检查区域须在原传感器内，每边 4–16 像元。");
    }

    private static void ValidateSamplingReport(CameraSamplingReport report, CameraImagingResult run)
    {
        ArgumentNullException.ThrowIfNull(report); ValidateSamplingSource(run); ValidateSamplingRegion(report.Region, run.Request);
        var hash = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(run, ExperimentJsonOptions)));
        if (report.Format != CameraSamplingReport.FormatIdentifier || report.SchemaVersion != 1 || report.SourceRunSha256 != hash
            || report.SourceEngineVersion != run.Inputs!.EngineVersion || report.Comparisons is null || report.Comparisons.Count is < 1 or > 5
            || report.Comparisons.Select(c => c.Axis).Distinct().Count() != report.Comparisons.Count)
            throw new InvalidDataException("采样检查报告与保存 B 的身份或格式不同。");
        var expected = new CameraSamplingSettings(run.Request.Render3D!.Diffraction!, run.Request.PixelSampling);
        if (report.Baseline.Settings != expected) throw new InvalidDataException("采样基准设置不同。");
        ValidateFrame(report.Baseline);
        var savedBands = SceneBands.Select((_, b) => Difference(Crop(run.Scene3D!.Bands[b].PixelPowerWatts, report.Region, run.Request.Width),
            report.Baseline.Bands[b].PixelPowerWatts, report.Region)).ToArray();
        if (report.SavedBandDifferences is null || !savedBands.SequenceEqual(report.SavedBandDifferences)
            || Difference(Crop(run.PhotoElectrons, report.Region, run.Request.Width), report.Baseline.PhotoElectrons, report.Region) != report.SavedElectronDifference)
            throw new InvalidDataException("采样报告的基准重算差异不一致。");
        foreach (var c in report.Comparisons)
        {
            ValidateComparison(c, report.Baseline, allowFurther: true);
        }
        void ValidateComparison(CameraSamplingComparison c, CameraSamplingSnapshot basis, bool allowFurther)
        {
            if (!Enum.IsDefined(c.Axis) || !Enum.IsDefined(c.Status) || c.RequestedSettings != Refine(basis.Settings, c.Axis))
                throw new InvalidDataException("采样报告改变了未声明的参数。");
            Label(c.Message, 4096, "Sampling message");
            if ((c.Status == CameraSamplingComparisonStatus.AtLimit) != (c.RequestedSettings == basis.Settings)
                || c.Status == CameraSamplingComparisonStatus.Compared && (savedBands.Any(d => d.MaximumAbsolute != 0)
                    || report.SavedElectronDifference.MaximumAbsolute != 0))
                throw new InvalidDataException("采样比较状态与基准或实现上限不同。");
            if (c.Status != CameraSamplingComparisonStatus.Compared)
            {
                if (c.Refined is not null || c.BandDifferences is not null || c.ElectronDifference is not null || c.RawDifference is not null)
                    throw new InvalidDataException("未完成的比较不能附带成功数值。");
                if (c.FurtherRefinements is not null) throw new InvalidDataException("未完成的层级不能附加后续成功数据。");
                return;
            }
            var frame = c.Refined ?? throw new InvalidDataException("已比较项目缺少数值。");
            if (frame.Settings != c.RequestedSettings) throw new InvalidDataException("比较设置不一致。");
            ValidateFrame(frame);
            if (c.BandDifferences is null || !SceneBands.Select((_, b) => Difference(basis.Bands[b].PixelPowerWatts, frame.Bands[b].PixelPowerWatts, report.Region)).SequenceEqual(c.BandDifferences)
                || Difference(basis.PhotoElectrons, frame.PhotoElectrons, report.Region) != c.ElectronDifference
                || Difference(basis.NoiselessRawDn.Select(v => (double)v).ToArray(), frame.NoiselessRawDn.Select(v => (double)v).ToArray(), report.Region) != c.RawDifference)
                throw new InvalidDataException("采样报告的逐点差异不一致。");
            if (c.FurtherRefinements is { } levels)
            {
                if (!allowFurther || levels.Count is < 1 or > 4) throw new InvalidDataException("采样层级列表无效。");
                var prior = frame;
                foreach (var level in levels)
                {
                    if (level.Axis != c.Axis) throw new InvalidDataException("连续加密不能改变项目。");
                    ValidateComparison(level, prior, allowFurther: false);
                    if (level.Refined is { } next) prior = next;
                    else if (!ReferenceEquals(level, levels[^1])) throw new InvalidDataException("未完成层级之后不能继续加密。");
                }
            }
        }
        void ValidateFrame(CameraSamplingSnapshot f)
        {
            var count = report.Region.Width * report.Region.Height;
            DiffractionCameraRenderer.Validate(ToCore(f.Settings.Diffraction)); CameraPixelAperture.Validate(ToCore(f.Settings.PixelSampling));
            if (f.Bands is null || f.Bands.Count != 3 || f.PhotoElectrons is null || f.PhotoElectrons.Count != count
                || f.NoiselessRawDn is null || f.NoiselessRawDn.Count != count || !Enum.IsDefined(f.LocalBayerPattern)
                || f.NoiselessRawDn.Any(v => v > (1 << run.Request.Sensor!.BitDepth) - 1)
                || Enumerable.Range(0, 4).Any(i => CameraSensorPipeline.ColorAt((BayerPattern)f.LocalBayerPattern, i / 2, i % 2)
                    != CameraSensorPipeline.ColorAt((BayerPattern)run.Request.Sensor!.Pattern, report.Region.Y + i / 2, report.Region.X + i % 2))
                || !double.IsFinite(f.ElapsedSeconds) || f.ElapsedSeconds < 0 || f.PhotoElectrons.Any(v => !double.IsFinite(v) || v < 0 || v > 1e10))
                throw new InvalidDataException("采样报告的区域数组无效。");
            for (var b = 0; b < 3; b++) if (f.Bands[b].WavelengthNanometers != SceneBands[b]
                || f.Bands[b].PixelPowerWatts.Count != count || f.Bands[b].PixelPowerWatts.Any(v => !double.IsFinite(v) || v < 0))
                throw new InvalidDataException("采样报告的波段功率无效。");
            var wave = f.Diagnostics;
            if (wave is null || wave.Algorithm != "ScalarHuygensSurfaceCells/1" || wave.SurfaceSamples is < 0 or > DiffractionCameraRenderer.MaximumSurfaceSamples
                || wave.ScalarCellEvaluations is < 0 or > DiffractionCameraRenderer.MaximumCellEvaluations || wave.OccludedPupilCells < 0
                || !double.IsFinite(wave.SourceAreaSquareMillimeters) || wave.SourceAreaSquareMillimeters < 0
                || !double.IsFinite(wave.CollectedPowerWatts) || wave.CollectedPowerWatts < 0
                || !double.IsFinite(wave.MaximumGeometricSpreadPixels) || wave.MaximumGeometricSpreadPixels < 0
                || wave.RequiredSourceGuardPixels < 0 || wave.RequiredSourceGuardPixels > f.Settings.Diffraction.SourceGuardPixels
                || wave.SamplesPerObject is null || wave.SamplesPerObject.Count != run.Request.Scene3D!.Objects.Count
                || wave.SamplesPerObject.Any(n => n < 0) || wave.SamplesPerObject.Sum(n => (long)n) != wave.SurfaceSamples)
                throw new InvalidDataException("采样报告的工作量/功率诊断无效。");
            if (f.Execution is { } e)
            {
                var modern = e.Policy == "RegionExactSupport/2";
                if (!modern && e.Policy != "RegionExactSupport/1"
                    || e.MaximumRays != (modern ? DiffractionCameraRenderer.MaximumRegionalRays : DirectCameraRenderer.MaximumRays)
                    || e.MaximumSurfaceInteractions != (modern ? DiffractionCameraRenderer.MaximumRegionalSurfaceInteractions : DirectCameraRenderer.MaximumSurfaceInteractions)
                    || e.MaximumCandidateVisits != (modern ? DiffractionCameraRenderer.MaximumRegionalCandidateVisits : DiffractionCameraRenderer.MaximumSurfaceSamples)
                    || e.LaunchedRays < 0 || e.LaunchedRays > e.MaximumRays || e.SurfaceInteractions < 0 || e.SurfaceInteractions > e.MaximumSurfaceInteractions
                    || e.AvoidedAuxiliaryRays < 0 || e.SourceCandidateVisits < 0 || e.SourceCandidateVisits > e.MaximumCandidateVisits)
                    throw new InvalidDataException("采样报告的追迹工作量或额度身份无效。");
            }
        }
    }
}

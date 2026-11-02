using System.Text.Json;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis.Imaging;
using OpticalSystemDesign.Core.Apodization;

namespace OpticalSystemDesign.Application.Services;

internal sealed partial class CameraImagingService
{
    public CameraSceneLinearBatchPlan PrepareSceneLinearBatch(CameraSceneLinearBatchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (Gate)
        {
            if (modes.CurrentMode != OpticalWorkbenchMode.Sequential) throw new InvalidOperationException("全幅线性成像需要顺序镜头。");
            var optic = Runtime.CurrentOptic.Clone();
            return CreateLinearPlan(optic, JsonSerializer.Serialize(optic.ToSnapshot(), LensJsonOptions), Workspace.GetDocumentSnapshot(), request);
        }
    }

    private static CameraSceneLinearBatchPlan CreateLinearPlan(Optic optic, string lens, OpticalDocumentSnapshot document, CameraSceneLinearBatchRequest request)
    {
        if (!Enum.IsDefined(request.Mode) || request.RowsPerStrip is < 1 or > 256 || request.SpectralQuadratureOrder is not (2 or 3)
            || new[] { request.BrightBandRadiance, request.DarkBandRadiance }.Any(v => !double.IsFinite(v) || v is < 0 or > 1e6)
            || request.PngMaximumIrradiance is { } max && (!double.IsFinite(max) || max <= 0))
            throw new ArgumentException("Invalid scene batch source/export settings.");
        var chart = request.Chart ?? new();
        IReadOnlyList<SceneLinearSource> sources;
        if (request.Mode == SceneLinearBatchMode.CurrentLensWavelengths)
        {
            if (request.Sources is not null || optic.Wavelengths.Count is < 1 or > 16) throw new ArgumentException("Current-lens tasks require 1–16 lens wavelength rows, without replacement sources.");
            sources = optic.Wavelengths.Select((w, i) => new SceneLinearSource(FormattableString.Invariant($"W{i + 1}: {w.Nanometers:G17} nm"), w.Nanometers,
                SourceMultiplier: request.UseLensWeightsAsSourceMultipliers ? w.Weight : 1,
                LensWavelengthNumber: i + 1, LensWeight: w.Weight,
                Provenance: "Frozen current lens wavelength row. Declared source radiance multiplier; no image-energy normalization.")).ToArray();
        }
        else if (request.Mode == SceneLinearBatchMode.ArticleSourceShapes)
        {
            if (request.Sources is not null) throw new ArgumentException("Article source shapes cannot be replaced implicitly.");
            // Exact relative ordinates from the M0 source files. Band radiances are explicit native inputs,
            // not inferred original Speos flux units, ROM transmission or spectral range.
            sources = new[] { 486d, 588, 656 }.Select(w => new SceneLinearSource($"原文源谱形状 {w:G} nm", RelativeSpectrum:
                Array.AsReadOnly(new[] { new ScalarSpectrumSample(400, 0), new(w - 1.5, 0), new(w - 1, 100),
                    new(w, 100), new(w + 1, 100), new(w + 1.5, 0), new(800, 0) }),
                Provenance: "Ansys Data_MTF_KB.zip SHA256 5e685a058b2b48b5e3494097d66df5d3b8128b974e1ab4c77d78f5958b5632c4; Gaussian_*nm.spectrum relative samples; current native lens, no original ROM/range claim.")).ToArray();
        }
        else sources = request.Sources ?? throw new ArgumentException("Explicit sources are missing.");
        if (sources.Count is < 1 or > 16) throw new ArgumentException("Declare 1–16 independent source tasks.");
        var tasks = sources.Select(source =>
        {
            if (source is null || string.IsNullOrWhiteSpace(source.Name) || source.Name.Length > 256 || source.Provenance?.Length > 4096
                || !double.IsFinite(source.SourceMultiplier) || source.SourceMultiplier < 0
                || source.LensWeight is { } lensWeight && (!double.IsFinite(lensWeight) || lensWeight < 0)
                || (source.WavelengthNanometers is null) == (source.RelativeSpectrum is null))
                throw new ArgumentException("Each task needs a name and exactly one monochromatic wavelength or source spectrum.");
            var bright = request.BrightBandRadiance * source.SourceMultiplier; var dark = request.DarkBandRadiance * source.SourceMultiplier;
            if (!double.IsFinite(bright) || !double.IsFinite(dark) || bright > 1e6 || dark > 1e6) throw new ArgumentException("Source radiance multiplier overflows the physical model range.");
            if (source.WavelengthNanometers is { } wave)
            {
                EmissiveCheckerboard.Validate(chart, [new(wave, bright, dark)]);
                return new SceneLinearFrozenTask(source, Array.AsReadOnly(new[] { new SceneEmissionNode(wave, 1, bright, dark) }), null, null);
            }
            source = source with { RelativeSpectrum = Array.AsReadOnly(source.RelativeSpectrum!.ToArray()) };
            var quadrature = SceneEmissionSpectrum.Integrate(source.RelativeSpectrum, bright, dark, request.SpectralQuadratureOrder);
            EmissiveCheckerboard.Validate(chart, quadrature.Nodes.Select(n => new EmissiveCheckerboardBand(n.WavelengthNanometers, n.BrightRadiance, n.DarkRadiance)).ToArray());
            return new SceneLinearFrozenTask(source, quadrature.Nodes, quadrature.ShapeAreaNanometers, quadrature.InputShapeMaximum);
        }).ToArray();
        request = request with { Chart = chart, Sources = request.Mode == SceneLinearBatchMode.ExplicitSources
            ? Array.AsReadOnly(tasks.Select(t => t.Source).ToArray()) : null };
        var settings = LinearBatchSettings(request);
        var work = DirectCameraRenderer.PlanBatch(settings, tasks.Select(t => t.Nodes.Count).ToArray(), optic.SurfaceGroup.Items.Count - 2);
        // Per-node power, multi-node task sum power, and one task irradiance plane. Keep the existing
        // 8,388,608 double-value budget; storage is independent of display RGB and the old 64 MiB JSON archive.
        var values = checked(work.Pixels * (tasks.Sum(t => t.Nodes.Count) + tasks.Count(t => t.Nodes.Count > 1) + tasks.Length));
        var failure = work.Failure ?? (values > SpectralDetectorPower.MaximumPixelBands ? "全部任务浮点数据超过既有 8388608 数值预算。" : null);
        return new(CameraSceneLinearBatchPlan.FormatIdentifier, 1, CameraSceneLinearBatchPlan.CurrentAlgorithm, document,
            lens, SfrHash(lens), request, Array.AsReadOnly(tasks), work, values, failure is null, failure);
    }

    private static DirectCameraRenderSettings LinearBatchSettings(CameraSceneLinearBatchRequest request) => new(request.Width, request.Height,
        request.PixelPitchMicrometers, new(request.PixelSamplesPerAxis, request.FillFactor), request.PupilRings, request.PupilArms, request.BareInterfaceFresnel);

    private static (CameraSceneLinearBatchPlan Plan, Optic Optic) ValidateLinearPlan(CameraSceneLinearBatchPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Format != CameraSceneLinearBatchPlan.FormatIdentifier || plan.SchemaVersion != 1 || plan.Algorithm != CameraSceneLinearBatchPlan.CurrentAlgorithm
            || plan.LensSnapshotJson is null || SfrHash(plan.LensSnapshotJson) != plan.LensSha256 || plan.SourceDocument is null)
            throw new InvalidDataException("Invalid frozen full-sensor plan identity.");
        var optic = RestoreLens(plan.LensSnapshotJson);
        var regenerated = CreateLinearPlan(optic, plan.LensSnapshotJson, plan.SourceDocument, plan.Request);
        if (JsonSerializer.Serialize(regenerated, SceneSfrJson) != JsonSerializer.Serialize(plan, SceneSfrJson))
            throw new InvalidDataException("Frozen task spectrum, settings or global budget is inconsistent.");
        if (optic.Apodization is UniformApodization or ZemaxApodization { Type: ZemaxApodizationType.Uniform }) optic.Apodization = null;
        return (regenerated, optic);
    }
}

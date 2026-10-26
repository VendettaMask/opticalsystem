using System.Text.Json;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Apodization;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.NonSequential;
using OpticalSystemDesign.Core.Optimization;
using OpticalSystemDesign.Core.Phase;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.Core.Visualization;
using static OpticalSystemDesign.Application.Services.WorkbenchMapper;
using ContractAnalysisColorMap = OpticalSystemDesign.Application.Contracts.AnalysisColorMap;
using ContractAnalysisLineStyle = OpticalSystemDesign.Application.Contracts.AnalysisLineStyle;
using ContractAnalysisMarkerStyle = OpticalSystemDesign.Application.Contracts.AnalysisMarkerStyle;
using ContractAnalysisParameterDescriptor = OpticalSystemDesign.Application.Contracts.AnalysisParameterDescriptor;
using ContractAnalysisParameterKind = OpticalSystemDesign.Application.Contracts.AnalysisParameterKind;
using ContractAnalysisSeriesKind = OpticalSystemDesign.Application.Contracts.AnalysisSeriesKind;
using NonSequentialObjectKind = OpticalSystemDesign.Core.NonSequential.NonSequentialObjectKind;

namespace OpticalSystemDesign.Application.Services;

internal sealed class VisualizationService : WorkbenchServiceBase, IVisualizationService
{
    private readonly IWorkbenchModeService? _modes;
    private readonly NonSequentialLayoutSession? _nonSequentialLayoutSession;

    public VisualizationService(
        WorkspaceCoordinator workspace,
        IWorkbenchModeService? modes = null,
        NonSequentialLayoutSession? nonSequentialLayoutSession = null)
        : base(workspace)
    {
        _modes = modes;
        _nonSequentialLayoutSession = nonSequentialLayoutSession;
    }

    public Task<SceneDto> BuildSceneAsync(
        SceneDimension dimension,
        CancellationToken cancellationToken = default)
    {
        return BuildSceneAsync(new VisualizationRequestDto(
            dimension,
            RayCount: dimension == SceneDimension.TwoDimensional ? 3 : 5), cancellationToken);
    }

    public VisualizationOptionsDto GetVisualizationOptions(int? configurationIndex = null)
    {
        lock (Gate)
        {
            if (_modes?.CurrentMode == OpticalWorkbenchMode.NonSequential)
            {
                var document = Runtime.CurrentNonSequentialDocument;
                return new VisualizationOptionsDto(
                    Enumerable.Range(1, document.Objects.Count).ToArray(),
                    document.Objects.Where(item => item.Enabled
                        && item.Parameters is OpticalSystemDesign.Core.NonSequential.SourceParameters)
                        .Select((item, index) => new VisualizationSelectorOptionDto(index, item.Name)).ToArray(),
                    document.Wavelengths.Select((item, index) => new VisualizationSelectorOptionDto(
                        index, $"{item.Label}  {item.Nanometers:0.####} nm")).ToArray());
            }

            var navigation = Runtime.GetMultiConfigurationNavigation();
            var optic = SelectOptic(configurationIndex);
            return new VisualizationOptionsDto(
                optic.SurfaceGroup.Items.Select(surface => surface.Number).ToArray(),
                optic.Fields.Select((field, index) =>
                    new VisualizationSelectorOptionDto(index, field.Label)).ToArray(),
                optic.Wavelengths.Select((wavelength, index) =>
                    new VisualizationSelectorOptionDto(
                        index,
                        $"{wavelength.Label}  {wavelength.Nanometers:0.####} nm")).ToArray(),
                navigation.Count, navigation.ActiveIndex);
        }
    }

    // Resolve while holding Gate; only a clone may leave the workspace thread.
    private Optic SelectOptic(int? configurationIndex)
    {
        if (!configurationIndex.HasValue) return Runtime.CurrentOptic;
        var configurations = Runtime.CreateMeritConfigurationContext().Configurations;
        if (configurationIndex.Value < 0 || configurationIndex.Value >= configurations.Count)
            throw new ArgumentOutOfRangeException(nameof(configurationIndex));
        return configurations[configurationIndex.Value];
    }

    public Task<SceneDto> BuildSceneAsync(
        VisualizationRequestDto request,
        CancellationToken cancellationToken = default)
    {
        Optic snapshot;
        NonSequentialDocument? nonSequentialSnapshot;
        long sourceRevision;
        OpticalDocumentSnapshot summary;
        SceneConfigurationDto? configuration;
        bool resolveAutomaticApertures;
        CancellationTokenSource linked;
        lock (Gate)
        {
            sourceRevision = Workspace.Revision;
            summary = Workspace.GetDocumentSnapshot();
            nonSequentialSnapshot = _modes?.CurrentMode == OpticalWorkbenchMode.NonSequential
                ? Runtime.CurrentNonSequentialDocument.Clone()
                : null;
            snapshot = Optic.FromSnapshot((nonSequentialSnapshot is null
                ? SelectOptic(request.ConfigurationIndex) : Runtime.CurrentOptic).ToSnapshot());
            var navigation = Runtime.GetMultiConfigurationNavigation();
            configuration = nonSequentialSnapshot is null
                ? new SceneConfigurationDto((request.ConfigurationIndex ?? navigation.ActiveIndex) + 1, navigation.Count)
                : null;
            resolveAutomaticApertures = nonSequentialSnapshot is null
                && request.ConfigurationIndex.HasValue
                && request.ConfigurationIndex.Value != navigation.ActiveIndex;
            linked = Workspace.LinkDocumentToken(cancellationToken);
        }

        return nonSequentialSnapshot is null
            ? BuildSceneWorkerAsync(snapshot, sourceRevision, summary, request, linked, configuration!, resolveAutomaticApertures)
            : BuildNonSequentialSceneWorkerAsync(
                snapshot,
                nonSequentialSnapshot,
                sourceRevision,
                summary,
                request,
                linked,
                _nonSequentialLayoutSession);
    }

    private static async Task<SceneDto> BuildNonSequentialSceneWorkerAsync(
        Optic optic,
        NonSequentialDocument document,
        long sourceRevision,
        OpticalDocumentSnapshot summary,
        VisualizationRequestDto request,
        CancellationTokenSource linked,
        NonSequentialLayoutSession? layoutSession)
    {
        using (linked)
        {
            return await Task.Run(() =>
            {
                linked.Token.ThrowIfCancellationRequested();
                using var cancellationScope = ComputationCancellation.Push(linked.Token);
                var currentSceneHash = NonSequentialSceneHasher.Compute(document, optic.Materials);
                var layout = layoutSession?.LoadLayoutBranches(
                    document,
                    request.IncludeStaleNonSequentialRays, materials: optic.Materials);
                var scene = NonSequentialVisualizationBuilder.Build(
                    optic,
                    document,
                    request,
                    layout?.Branches);
                var provenance = layout is null
                    ? new NonSequentialLayoutResultDto(
                        false,
                        currentSceneHash,
                        null,
                        null,
                        false,
                        false,
                        null)
                    : new NonSequentialLayoutResultDto(
                        true,
                        layout.CurrentSceneHash,
                        layout.SceneHash,
                        layout.SourceRevision,
                        layout.IsStale,
                        layout.Branches.Count > 0,
                        layout.DatabasePath);
                return new SceneDto(
                    sourceRevision,
                    SceneDimension.ThreeDimensional,
                    null,
                    scene,
                    summary,
                    provenance);
            }, linked.Token).ConfigureAwait(false);
        }
    }

    private static async Task<SceneDto> BuildSceneWorkerAsync(
        Optic snapshot,
        long sourceRevision,
        OpticalDocumentSnapshot summary,
        VisualizationRequestDto request,
        CancellationTokenSource linked,
        SceneConfigurationDto configuration,
        bool resolveAutomaticApertures)
    {
        using (linked)
        {
            return await Task.Run(() =>
            {
                linked.Token.ThrowIfCancellationRequested();
                using var cancellationScope = ComputationCancellation.Push(linked.Token);
                // Inactive configurations have not necessarily had their
                // automatic apertures resolved by the workspace yet.
                if (resolveAutomaticApertures) AutomaticSemiDiameterSolver.Update(snapshot);
                // Geometry, rays and footer must describe the same frozen optic.
                var computable = Core.Capabilities.OpticCapabilityPreflight.Inspect(snapshot).Count == 0;
                summary = summary with
                {
                    Name = snapshot.Name,
                    EffectiveFocalLength = computable ? snapshot.Paraxial.EstimateEffectiveFocalLength() : double.NaN,
                    FNumber = computable ? snapshot.Paraxial.EstimateFNumber() : double.NaN,
                    ApertureValue = snapshot.Aperture.Value,
                    TotalTrack = snapshot.SurfaceGroup.TotalTrack,
                    SurfaceCount = snapshot.SurfaceGroup.Items.Count,
                    FieldCount = snapshot.Fields.Count,
                    WavelengthCount = snapshot.Wavelengths.Count,
                    EntrancePupilDiameter = computable ? snapshot.Paraxial.EstimateEntrancePupilDiameter() : double.NaN
                };
                var builder = new Layout2DBuilder(snapshot);
                var options = new LayoutBuildOptions(
                    request.FirstSurface,
                    request.LastSurface,
                    request.FieldIndex,
                    request.WavelengthIndex,
                    request.IncludeAllWavelengths,
                    request.RayCount,
                    request.LowerPupil,
                    request.UpperPupil,
                    request.DeleteVignetted,
                    request.MarginalAndChiefOnly);
                if (request.Dimension == SceneDimension.TwoDimensional)
                {
                    var scene = builder.Build(options: options);
                    linked.Token.ThrowIfCancellationRequested();
                    return new SceneDto(sourceRevision, request.Dimension, ToScene2Dto(scene), null, summary,
                        Configuration: configuration);
                }

                var scene3 = builder.Build3D(options: options);
                linked.Token.ThrowIfCancellationRequested();
                return new SceneDto(sourceRevision, request.Dimension, null, ToScene3Dto(scene3), summary,
                    Configuration: configuration);
            }, linked.Token).ConfigureAwait(false);
        }
    }
}

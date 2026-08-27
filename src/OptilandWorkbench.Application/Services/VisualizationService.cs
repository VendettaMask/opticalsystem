using System.Text.Json;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apodization;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.NonSequential;
using NonSequentialObjectKind = OptilandWorkbench.Core.NonSequential.NonSequentialObjectKind;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Phase;
using OptilandWorkbench.Core.Services;
using OptilandWorkbench.Core.Visualization;
using ContractAnalysisColorMap = OptilandWorkbench.Application.Contracts.AnalysisColorMap;
using ContractAnalysisLineStyle = OptilandWorkbench.Application.Contracts.AnalysisLineStyle;
using ContractAnalysisMarkerStyle = OptilandWorkbench.Application.Contracts.AnalysisMarkerStyle;
using ContractAnalysisParameterDescriptor = OptilandWorkbench.Application.Contracts.AnalysisParameterDescriptor;
using ContractAnalysisParameterKind = OptilandWorkbench.Application.Contracts.AnalysisParameterKind;
using ContractAnalysisSeriesKind = OptilandWorkbench.Application.Contracts.AnalysisSeriesKind;
using static OptilandWorkbench.Application.Services.WorkbenchMapper;

namespace OptilandWorkbench.Application.Services;

internal sealed class VisualizationService : WorkbenchServiceBase, IVisualizationService
{
    private readonly IWorkbenchModeService? _modes;
    private readonly NonSequentialAnalysisSession? _nonSequentialAnalysisSession;

    public VisualizationService(
        WorkspaceCoordinator workspace,
        IWorkbenchModeService? modes = null,
        NonSequentialAnalysisSession? nonSequentialAnalysisSession = null)
        : base(workspace)
    {
        _modes = modes;
        _nonSequentialAnalysisSession = nonSequentialAnalysisSession;
    }

    public Task<SceneDto> BuildSceneAsync(
        SceneDimension dimension,
        CancellationToken cancellationToken = default)
    {
        return BuildSceneAsync(new VisualizationRequestDto(
            dimension,
            RayCount: dimension == SceneDimension.TwoDimensional ? 3 : 5), cancellationToken);
    }

    public VisualizationOptionsDto GetVisualizationOptions()
    {
        lock (Gate)
        {
            if (_modes?.CurrentMode == OpticalWorkbenchMode.NonSequential)
            {
                var document = Runtime.CurrentNonSequentialDocument;
                return new VisualizationOptionsDto(
                    Enumerable.Range(1, document.Objects.Count).ToArray(),
                    document.Objects.Where(item => item.Enabled
                        && item.Parameters is OptilandWorkbench.Core.NonSequential.SourceParameters)
                        .Select((item, index) => new VisualizationSelectorOptionDto(index, item.Name)).ToArray(),
                    document.Wavelengths.Select((item, index) => new VisualizationSelectorOptionDto(
                        index, $"{item.Label}  {item.Nanometers:0.####} nm")).ToArray());
            }

            return new VisualizationOptionsDto(
                Runtime.CurrentOptic.SurfaceGroup.Items.Select(surface => surface.Number).ToArray(),
                Runtime.CurrentOptic.Fields.Select((field, index) =>
                    new VisualizationSelectorOptionDto(index, field.Label)).ToArray(),
                Runtime.CurrentOptic.Wavelengths.Select((wavelength, index) =>
                    new VisualizationSelectorOptionDto(
                        index,
                        $"{wavelength.Label}  {wavelength.Nanometers:0.####} nm")).ToArray());
        }
    }

    public Task<SceneDto> BuildSceneAsync(
        VisualizationRequestDto request,
        CancellationToken cancellationToken = default)
    {
        Optic snapshot;
        NonSequentialDocument? nonSequentialSnapshot;
        long sourceRevision;
        OpticalDocumentSnapshot summary;
        CancellationTokenSource linked;
        lock (Gate)
        {
            sourceRevision = Workspace.Revision;
            summary = Workspace.GetDocumentSnapshot();
            snapshot = Optic.FromSnapshot(Runtime.CurrentOptic.ToSnapshot());
            nonSequentialSnapshot = _modes?.CurrentMode == OpticalWorkbenchMode.NonSequential
                ? Runtime.CurrentNonSequentialDocument.Clone()
                : null;
            linked = Workspace.LinkDocumentToken(cancellationToken);
        }

        return nonSequentialSnapshot is null
            ? BuildSceneWorkerAsync(snapshot, sourceRevision, summary, request, linked)
            : BuildNonSequentialSceneWorkerAsync(
                snapshot,
                nonSequentialSnapshot,
                sourceRevision,
                summary,
                request,
                linked,
                _nonSequentialAnalysisSession);
    }

    private static async Task<SceneDto> BuildNonSequentialSceneWorkerAsync(
        Optic optic,
        NonSequentialDocument document,
        long sourceRevision,
        OpticalDocumentSnapshot summary,
        VisualizationRequestDto request,
        CancellationTokenSource linked,
        NonSequentialAnalysisSession? analysisSession)
    {
        using (linked)
        {
            return await Task.Run(() =>
            {
                linked.Token.ThrowIfCancellationRequested();
                using var cancellationScope = ComputationCancellation.Push(linked.Token);
                var databaseBranches = analysisSession?.LoadLayoutBranches(document);
                var scene = NonSequentialVisualizationBuilder.Build(optic, document, request, databaseBranches);
                return new SceneDto(sourceRevision, SceneDimension.ThreeDimensional, null, scene, summary);
            }, linked.Token).ConfigureAwait(false);
        }
    }

    private static async Task<SceneDto> BuildSceneWorkerAsync(
        Optic snapshot,
        long sourceRevision,
        OpticalDocumentSnapshot summary,
        VisualizationRequestDto request,
        CancellationTokenSource linked)
    {
        using (linked)
        {
            return await Task.Run(() =>
            {
                linked.Token.ThrowIfCancellationRequested();
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
                    return new SceneDto(sourceRevision, request.Dimension, ToScene2Dto(scene), null, summary);
                }

                var scene3 = builder.Build3D(options: options);
                linked.Token.ThrowIfCancellationRequested();
                return new SceneDto(sourceRevision, request.Dimension, null, ToScene3Dto(scene3), summary);
            }, linked.Token).ConfigureAwait(false);
        }
    }
}

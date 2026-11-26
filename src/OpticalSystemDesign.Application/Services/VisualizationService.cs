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

    public VisualizationOptionsDto GetVisualizationOptions(int? configurationIndex = null, bool includeAllConfigurations = false)
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
            var configurations = includeAllConfigurations
                ? Runtime.CreateMeritConfigurationContext().Configurations : new[] { optic };
            var surfaceNumbers = optic.SurfaceGroup.Items.Select(surface => surface.Number)
                .Where(number => configurations.All(item => item.SurfaceGroup.Items.Any(surface => surface.Number == number))).ToArray();
            return new VisualizationOptionsDto(
                surfaceNumbers,
                optic.Fields.Take(configurations.Min(item => item.Fields.Count)).Select((field, index) =>
                    new VisualizationSelectorOptionDto(index, field.Label)).ToArray(),
                optic.Wavelengths.Take(configurations.Min(item => item.Wavelengths.Count)).Select((wavelength, index) =>
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
        IReadOnlyList<Optic>? allSnapshots = null;
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
            if (nonSequentialSnapshot is null && request.IncludeAllConfigurations)
            {
                if (request.ConfigurationIndex.HasValue)
                    throw new ArgumentException("Choose one configuration or all configurations, not both.", nameof(request));
                allSnapshots = Runtime.CreateMeritConfigurationContext().Configurations
                    .Select(optic => Optic.FromSnapshot(optic.ToSnapshot())).ToArray();
            }
            configuration = nonSequentialSnapshot is null
                ? new SceneConfigurationDto((request.ConfigurationIndex ?? navigation.ActiveIndex) + 1, navigation.Count)
                : null;
            resolveAutomaticApertures = nonSequentialSnapshot is null
                && request.ConfigurationIndex.HasValue
                && request.ConfigurationIndex.Value != navigation.ActiveIndex;
            linked = Workspace.LinkDocumentToken(cancellationToken);
        }

        return nonSequentialSnapshot is null
            ? BuildSceneWorkerAsync(snapshot, sourceRevision, summary, request, linked, configuration!, resolveAutomaticApertures, allSnapshots)
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
        bool resolveAutomaticApertures,
        IReadOnlyList<Optic>? allSnapshots)
    {
        using (linked)
        {
            return await Task.Run(() =>
            {
                linked.Token.ThrowIfCancellationRequested();
                using var cancellationScope = ComputationCancellation.Push(linked.Token);
                if (allSnapshots is not null)
                {
                    var scenes = new List<SceneDto>();
                    for (var index = 0; index < allSnapshots.Count; index++)
                    {
                        linked.Token.ThrowIfCancellationRequested();
                        scenes.Add(BuildSequentialScene(allSnapshots[index], sourceRevision, summary, request,
                            new SceneConfigurationDto(index + 1, allSnapshots.Count),
                            index + 1 != configuration.Number, linked.Token));
                    }
                    return CombineConfigurations(scenes, summary, sourceRevision, request.Dimension);
                }
                return BuildSequentialScene(snapshot, sourceRevision, summary, request, configuration,
                    resolveAutomaticApertures, linked.Token);
            }, linked.Token).ConfigureAwait(false);
        }
    }

    private static SceneDto BuildSequentialScene(Optic snapshot, long sourceRevision,
        OpticalDocumentSnapshot summary, VisualizationRequestDto request, SceneConfigurationDto configuration,
        bool resolveAutomaticApertures, CancellationToken cancellationToken)
    {
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
            cancellationToken.ThrowIfCancellationRequested();
            var mapped = ToScene2Dto(scene);
            mapped = mapped with
            {
                Surfaces = mapped.Surfaces.Select(item => item with { ConfigurationNumber = configuration.Number }).ToArray(),
                LensElements = mapped.LensElements.Select(item => item with { ConfigurationNumber = configuration.Number }).ToArray(),
                LensEdges = mapped.LensEdges.Select(item => item with { ConfigurationNumber = configuration.Number }).ToArray(),
                Rays = mapped.Rays.Select(item => item with { ConfigurationNumber = configuration.Number }).ToArray()
            };
            return new SceneDto(sourceRevision, request.Dimension, mapped, null, summary,
                Configuration: configuration);
        }

        var scene3 = builder.Build3D(options: options);
        cancellationToken.ThrowIfCancellationRequested();
        var mapped3 = ToScene3Dto(scene3);
        mapped3 = mapped3 with
        {
            Surfaces = mapped3.Surfaces.Select(item => item with { ConfigurationNumber = configuration.Number }).ToArray(),
            LensElements = mapped3.LensElements.Select(item => item with { ConfigurationNumber = configuration.Number }).ToArray(),
            Rays = mapped3.Rays.Select(item => item with { ConfigurationNumber = configuration.Number }).ToArray()
        };
        return new SceneDto(sourceRevision, request.Dimension, null, mapped3, summary,
            Configuration: configuration);
    }

    private static SceneDto CombineConfigurations(IReadOnlyList<SceneDto> scenes,
        OpticalDocumentSnapshot summary, long sourceRevision, SceneDimension dimension)
    {
        // Each primitive retains its real surface/ray number plus its configuration
        // identity. Never reconnect surfaces or ray paths across configurations.
        var two = scenes.Select(scene => scene.TwoDimensional).OfType<Scene2Dto>().ToArray();
        var three = scenes.Select(scene => scene.ThreeDimensional).OfType<Scene3Dto>().ToArray();
        return new SceneDto(sourceRevision, dimension,
            two.Length == 0 ? null : new Scene2Dto(
                two.SelectMany(scene => scene.Surfaces).ToArray(),
                two.SelectMany(scene => scene.LensElements).ToArray(),
                two.SelectMany(scene => scene.LensEdges).ToArray(),
                two.SelectMany(scene => scene.Rays).ToArray(),
                two.Min(scene => scene.ZMin), two.Max(scene => scene.ZMax), two.Max(scene => scene.YExtent)),
            three.Length == 0 ? null : new Scene3Dto(
                three.SelectMany(scene => scene.Surfaces).ToArray(),
                three.SelectMany(scene => scene.LensElements).ToArray(),
                three.SelectMany(scene => scene.Rays).ToArray(),
                three.Max(scene => scene.XExtent), three.Max(scene => scene.YExtent),
                three.Min(scene => scene.ZMin), three.Max(scene => scene.ZMax))
            {
                FitBounds = EncloseScenePoints(three.Where(scene => scene.FitBounds is not null)
                    .SelectMany(scene => new[] { scene.FitBounds!.Minimum, scene.FitBounds.Maximum }))
            },
            summary with { EffectiveFocalLength = double.NaN, FNumber = double.NaN, TotalTrack = double.NaN },
            Configuration: new SceneConfigurationDto(0, scenes.Count, IncludesAll: true),
            ConfigurationSummaries: scenes.Select(scene => new SceneConfigurationSummaryDto(
                scene.Configuration!.Number, scene.Summary)).ToArray());
    }
}

using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Legacy;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apodization;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Phase;
using OptilandWorkbench.Core.Services;
using OptilandWorkbench.Core.Visualization;
using OptilandWorkbench.Core.Raytrace;
using ContractAnalysisColorMap = OptilandWorkbench.Application.Contracts.AnalysisColorMap;
using ContractAnalysisLineStyle = OptilandWorkbench.Application.Contracts.AnalysisLineStyle;
using ContractAnalysisMarkerStyle = OptilandWorkbench.Application.Contracts.AnalysisMarkerStyle;
using ContractAnalysisParameterDescriptor = OptilandWorkbench.Application.Contracts.AnalysisParameterDescriptor;
using ContractAnalysisParameterKind = OptilandWorkbench.Application.Contracts.AnalysisParameterKind;
using ContractAnalysisSeriesKind = OptilandWorkbench.Application.Contracts.AnalysisSeriesKind;
using static OptilandWorkbench.Application.Services.WorkbenchMapper;

namespace OptilandWorkbench.Application.Services;

internal sealed class AnalysisService : WorkbenchServiceBase, IAnalysisService
{
    private readonly RayTraceCache _rayTraceCache = new();

    public AnalysisService(WorkspaceCoordinator workspace)
        : base(workspace)
    {
    }

    public IReadOnlyList<string> AnalysisNames => Connector.AnalysisDisplayNames;

    public string CanonicalKey(string analysisName) => Connector.CanonicalAnalysisKey(analysisName);

    public IReadOnlyList<ContractAnalysisParameterDescriptor> GetParameters(string analysisName)
    {
        return Connector.GetAnalysisParameters(analysisName).Select(parameter => new ContractAnalysisParameterDescriptor(
            parameter.Key,
            parameter.DisplayName,
            (ContractAnalysisParameterKind)(int)parameter.Kind,
            parameter.DefaultValue,
            parameter.Minimum,
            parameter.Maximum,
            parameter.Increment,
            parameter.Choices)).ToArray();
    }

    public Dictionary<string, string> MergeSettings(
        string analysisName,
        IReadOnlyDictionary<string, string>? saved)
    {
        return Connector.MergeAnalysisSettings(analysisName, saved);
    }

    public Task<AnalysisResultDto> RunAsync(
        AnalysisRequestDto request,
        CancellationToken cancellationToken = default)
    {
        Optic snapshot;
        long sourceRevision;
        CancellationTokenSource linked;
        lock (Gate)
        {
            sourceRevision = Workspace.Revision;
            snapshot = Optic.FromSnapshot(Connector.CurrentOptic.ToSnapshot());
            snapshot.ConfigureRayTraceCache(_rayTraceCache, sourceRevision);
            linked = Workspace.LinkDocumentToken(cancellationToken);
        }

        return RunAnalysisWorkerAsync(snapshot, sourceRevision, request, linked);
    }

    private static async Task<AnalysisResultDto> RunAnalysisWorkerAsync(
        Optic snapshot,
        long sourceRevision,
        AnalysisRequestDto request,
        CancellationTokenSource linked)
    {
        using (linked)
        {
            return await Task.Run(() =>
            {
                linked.Token.ThrowIfCancellationRequested();
                var worker = new OpticalWorkspaceModel(snapshot);
                var canonicalAnalysisKey = worker.CanonicalAnalysisKey(request.AnalysisKey);
                var view = worker.BuildAnalysisView(canonicalAnalysisKey, request.Settings, linked.Token);
                linked.Token.ThrowIfCancellationRequested();
                return new AnalysisResultDto(
                    request.InstanceId,
                    request.Generation,
                    sourceRevision,
                    ToAnalysisViewDto(view) with
                    {
                        PresentationKind = AnalysisPresentationKindResolver.Resolve(canonicalAnalysisKey)
                    },
                    canonicalAnalysisKey,
                    CreateRequestFingerprint(canonicalAnalysisKey, request.Settings),
                    "Legacy.OpticalWorkspaceModel.BuildAnalysisView/v1");
            }, linked.Token).ConfigureAwait(false);
        }
    }

    private static string CreateRequestFingerprint(
        string canonicalAnalysisKey,
        IReadOnlyDictionary<string, string> settings)
    {
        var canonical = new StringBuilder(canonicalAnalysisKey.Length + (settings.Count * 32));
        canonical.Append(canonicalAnalysisKey.Length).Append(':').Append(canonicalAnalysisKey);
        foreach (var setting in settings.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            canonical.Append('|')
                .Append(setting.Key.Length).Append(':').Append(setting.Key)
                .Append('=')
                .Append(setting.Value.Length).Append(':').Append(setting.Value);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }
}

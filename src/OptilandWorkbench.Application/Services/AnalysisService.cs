using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using System.Globalization;
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

    public string CanonicalKey(string analysisName) => WorkbenchAnalysisCatalog.CanonicalKey(analysisName);

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
                var canonicalAnalysisKey = WorkbenchAnalysisCatalog.CanonicalKey(request.AnalysisKey);
                var normalizedSettings = NormalizeAnalysisSettings(
                    worker,
                    canonicalAnalysisKey,
                    request.Settings);
                var view = worker.BuildAnalysisView(canonicalAnalysisKey, normalizedSettings, linked.Token);
                linked.Token.ThrowIfCancellationRequested();
                return new AnalysisResultDto(
                    request.InstanceId,
                    request.Generation,
                    sourceRevision,
                    ToAnalysisViewDto(view) with
                    {
                        PresentationKind = WorkbenchAnalysisCatalog.PresentationKind(canonicalAnalysisKey)
                    },
                    new AnalysisExecutionProvenanceDto(
                        canonicalAnalysisKey,
                        CreateRequestFingerprint(canonicalAnalysisKey, normalizedSettings),
                        "Legacy.OpticalWorkspaceModel.BuildAnalysisView/v1"));
            }, linked.Token).ConfigureAwait(false);
        }
    }

    private static IReadOnlyDictionary<string, string> NormalizeAnalysisSettings(
        OpticalWorkspaceModel worker,
        string canonicalAnalysisKey,
        IReadOnlyDictionary<string, string> settings)
    {
        var descriptors = worker.GetAnalysisParameters(canonicalAnalysisKey)
            .ToDictionary(parameter => parameter.Key, StringComparer.Ordinal);
        var merged = worker.MergeAnalysisSettings(canonicalAnalysisKey, settings);
        foreach (var key in merged.Keys.ToArray())
        {
            if (!descriptors.TryGetValue(key, out var descriptor))
            {
                merged.Remove(key);
                continue;
            }

            merged[key] = descriptor.Kind switch
            {
                OptilandWorkbench.Application.Legacy.AnalysisParameterKind.Integer when int.TryParse(
                    merged[key],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var integer) => integer.ToString(CultureInfo.InvariantCulture),
                OptilandWorkbench.Application.Legacy.AnalysisParameterKind.Double when double.TryParse(
                    merged[key],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var number) => number.ToString("R", CultureInfo.InvariantCulture),
                OptilandWorkbench.Application.Legacy.AnalysisParameterKind.Boolean when bool.TryParse(merged[key], out var flag) =>
                    flag ? "true" : "false",
                _ => merged[key].Trim()
            };
        }

        return merged;
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

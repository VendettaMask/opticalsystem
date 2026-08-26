using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using System.Globalization;
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
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Phase;
using OptilandWorkbench.Core.Services;
using OptilandWorkbench.Core.Visualization;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.NonSequential;
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
    private readonly IWorkbenchModeService _modes;
    private readonly NonSequentialAnalysisSession? _nonSequentialAnalysisSession;

    public AnalysisService(
        WorkspaceCoordinator workspace,
        IWorkbenchModeService modes,
        NonSequentialAnalysisSession? nonSequentialAnalysisSession = null)
        : base(workspace)
    {
        _modes = modes ?? throw new ArgumentNullException(nameof(modes));
        _nonSequentialAnalysisSession = nonSequentialAnalysisSession;
    }

    public IReadOnlyList<string> AnalysisNames => WorkbenchAnalysisCatalog
        .DescriptorsForMode(_modes.CurrentMode)
        .Select(descriptor => descriptor.DisplayName)
        .ToArray();

    public string CanonicalKey(string analysisName) => WorkbenchAnalysisCatalog.CanonicalKey(analysisName);

    public IReadOnlyList<ContractAnalysisParameterDescriptor> GetParameters(string analysisName)
    {
        EnsureAvailable(analysisName);
        return Runtime.GetAnalysisParameters(analysisName).Select(parameter => new ContractAnalysisParameterDescriptor(
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
        EnsureAvailable(analysisName);
        return Runtime.MergeAnalysisSettings(analysisName, saved);
    }

    public Task<AnalysisResultDto> RunAsync(
        AnalysisRequestDto request,
        CancellationToken cancellationToken = default)
    {
        EnsureAvailable(request.AnalysisKey);
        Optic snapshot;
        NonSequentialDocument nonSequentialSnapshot;
        long sourceRevision;
        CancellationTokenSource linked;
        lock (Gate)
        {
            sourceRevision = Workspace.Revision;
            snapshot = Optic.FromSnapshot(Runtime.CurrentOptic.ToSnapshot());
            nonSequentialSnapshot = Runtime.CurrentNonSequentialDocument.Clone();
            snapshot.ConfigureRayTraceCache(_rayTraceCache, sourceRevision);
            linked = Workspace.LinkDocumentToken(cancellationToken);
        }

        return RunAnalysisWorkerAsync(
            snapshot,
            nonSequentialSnapshot,
            sourceRevision,
            request,
            linked,
            _nonSequentialAnalysisSession);
    }

    private void EnsureAvailable(string analysisName)
    {
        if (!WorkbenchAnalysisCatalog.IsAvailableInMode(analysisName, _modes.CurrentMode))
        {
            throw new InvalidOperationException(
                $"分析“{WorkbenchAnalysisCatalog.DisplayName(WorkbenchAnalysisCatalog.CanonicalKey(analysisName))}”不属于当前{(_modes.CurrentMode == OpticalWorkbenchMode.NonSequential ? "非序列" : "顺序")}模式。");
        }
    }

    private static async Task<AnalysisResultDto> RunAnalysisWorkerAsync(
        Optic snapshot,
        NonSequentialDocument nonSequentialSnapshot,
        long sourceRevision,
        AnalysisRequestDto request,
        CancellationTokenSource linked,
        NonSequentialAnalysisSession? analysisSession)
    {
        using (linked)
        {
            return await Task.Run(() =>
            {
                linked.Token.ThrowIfCancellationRequested();
                var databaseDetectors = WorkbenchAnalysisCatalog.CanonicalKey(request.AnalysisKey)
                    == "Non-Sequential Detector Viewer"
                    ? analysisSession?.ReconstructDetectors(nonSequentialSnapshot)
                    : null;
                var worker = new WorkbenchRuntime(snapshot, nonSequentialSnapshot, databaseDetectors);
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
                        "Application.WorkbenchRuntime.BuildAnalysisView/v1"));
            }, linked.Token).ConfigureAwait(false);
        }
    }

    private static IReadOnlyDictionary<string, string> NormalizeAnalysisSettings(
        WorkbenchRuntime worker,
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
                OptilandWorkbench.Application.Runtime.AnalysisParameterKind.Integer when int.TryParse(
                    merged[key],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var integer) => integer.ToString(CultureInfo.InvariantCulture),
                OptilandWorkbench.Application.Runtime.AnalysisParameterKind.Double when double.TryParse(
                    merged[key],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var number) => number.ToString("R", CultureInfo.InvariantCulture),
                OptilandWorkbench.Application.Runtime.AnalysisParameterKind.Boolean when bool.TryParse(merged[key], out var flag) =>
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

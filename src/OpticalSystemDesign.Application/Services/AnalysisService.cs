using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Apodization;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Optimization;
using OpticalSystemDesign.Core.Phase;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.Core.Visualization;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.NonSequential;
using ContractAnalysisColorMap = OpticalSystemDesign.Application.Contracts.AnalysisColorMap;
using ContractAnalysisLineStyle = OpticalSystemDesign.Application.Contracts.AnalysisLineStyle;
using ContractAnalysisMarkerStyle = OpticalSystemDesign.Application.Contracts.AnalysisMarkerStyle;
using ContractAnalysisParameterDescriptor = OpticalSystemDesign.Application.Contracts.AnalysisParameterDescriptor;
using ContractAnalysisParameterKind = OpticalSystemDesign.Application.Contracts.AnalysisParameterKind;
using ContractAnalysisSeriesKind = OpticalSystemDesign.Application.Contracts.AnalysisSeriesKind;
using static OpticalSystemDesign.Application.Services.WorkbenchMapper;

namespace OpticalSystemDesign.Application.Services;

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
                var canonicalAnalysisKey = WorkbenchAnalysisCatalog.CanonicalKey(request.AnalysisKey);
                var parameterWorker = new WorkbenchRuntime(snapshot, nonSequentialSnapshot);
                var normalizedSettings = NormalizeAnalysisSettings(
                    parameterWorker,
                    canonicalAnalysisKey,
                    request.Settings);
                Guid? selectedSourceId = null;
                if (canonicalAnalysisKey == "Non-Sequential Detector Viewer"
                    && normalizedSettings.TryGetValue("SourceNumber", out var sourceText)
                    && int.TryParse(sourceText, out var sourceNumber)
                    && sourceNumber > 0)
                {
                    var sources = nonSequentialSnapshot.Objects
                        .Where(item => item.Enabled
                            && item.Parameters is OpticalSystemDesign.Core.NonSequential.SourceParameters)
                        .ToArray();
                    if (sources.Length > 0)
                    {
                        selectedSourceId = sources[Math.Clamp(sourceNumber - 1, 0, sources.Length - 1)].Id;
                    }
                }
                var databaseDetectors = canonicalAnalysisKey == "Non-Sequential Detector Viewer"
                    ? analysisSession?.ReconstructDetectors(nonSequentialSnapshot, selectedSourceId)
                    : null;
                var worker = new WorkbenchRuntime(snapshot, nonSequentialSnapshot, databaseDetectors);
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

            var normalized = descriptor.Kind switch
            {
                OpticalSystemDesign.Application.Runtime.AnalysisParameterKind.Integer when int.TryParse(
                    merged[key],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var integer) && integer >= descriptor.Minimum && integer <= descriptor.Maximum =>
                    integer.ToString(CultureInfo.InvariantCulture),
                OpticalSystemDesign.Application.Runtime.AnalysisParameterKind.Double when double.TryParse(
                    merged[key],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var number) && double.IsFinite(number)
                    && number >= descriptor.Minimum && number <= descriptor.Maximum =>
                    number.ToString("R", CultureInfo.InvariantCulture),
                OpticalSystemDesign.Application.Runtime.AnalysisParameterKind.Boolean when bool.TryParse(merged[key], out var flag) =>
                    flag ? "true" : "false",
                OpticalSystemDesign.Application.Runtime.AnalysisParameterKind.Choice => NormalizeChoice(descriptor, merged[key]),
                OpticalSystemDesign.Application.Runtime.AnalysisParameterKind.File => merged[key].Trim(),
                _ => throw InvalidParameter(descriptor, merged[key])
            };
            merged[key] = normalized;
        }

        return merged;

        static string NormalizeChoice(
            OpticalSystemDesign.Application.Runtime.AnalysisParameterDescriptor descriptor,
            string value)
        {
            var normalized = value.Trim();
            return descriptor.Choices?.Contains(normalized, StringComparer.Ordinal) == true
                ? normalized
                : throw InvalidParameter(descriptor, value);
        }

        static ArgumentException InvalidParameter(
            OpticalSystemDesign.Application.Runtime.AnalysisParameterDescriptor descriptor,
            string value) => new(
                $"Analysis parameter '{descriptor.Key}' has invalid value '{value}'.",
                descriptor.Key);
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

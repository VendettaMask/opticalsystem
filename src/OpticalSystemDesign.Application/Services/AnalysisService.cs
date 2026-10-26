using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.Core.Visualization;
using static OpticalSystemDesign.Application.Services.WorkbenchMapper;
using ContractAnalysisColorMap = OpticalSystemDesign.Application.Contracts.AnalysisColorMap;
using ContractAnalysisLineStyle = OpticalSystemDesign.Application.Contracts.AnalysisLineStyle;
using ContractAnalysisMarkerStyle = OpticalSystemDesign.Application.Contracts.AnalysisMarkerStyle;
using ContractAnalysisParameterDescriptor = OpticalSystemDesign.Application.Contracts.AnalysisParameterDescriptor;
using ContractAnalysisParameterKind = OpticalSystemDesign.Application.Contracts.AnalysisParameterKind;
using ContractAnalysisSeriesKind = OpticalSystemDesign.Application.Contracts.AnalysisSeriesKind;

namespace OpticalSystemDesign.Application.Services;

internal sealed partial class AnalysisService : WorkbenchServiceBase, IAnalysisService
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

    public IReadOnlyList<ContractAnalysisParameterDescriptor> GetParameters(
        string analysisName, IReadOnlyDictionary<string, string>? settings = null)
    {
        EnsureAvailable(analysisName);
        lock (Gate)
        {
            var navigation = Runtime.GetMultiConfigurationNavigation();
            var worker = Runtime;
            var selection = AnalysisConfigurationSelection.Read(analysisName, settings);
            if (_modes.CurrentMode == OpticalWorkbenchMode.Sequential && navigation.Count > 1
                && selection is not (AnalysisConfigurationSelection.Current or AnalysisConfigurationSelection.All))
            {
                var index = AnalysisConfigurationSelection.Resolve(selection, navigation.ActiveIndex, navigation.Count);
                var document = Runtime.CaptureDocument();
                worker = CanonicalKey(analysisName) == "Interferogram"
                    ? WorkbenchRuntime.CreateComputationRuntime(document with { ActiveOptic = document.Configurations[index], ActiveConfigurationIndex = index })
                    : new WorkbenchRuntime(document.Configurations[index]);
            }
            var parameters = worker.GetAnalysisParameters(analysisName).Select(parameter => new ContractAnalysisParameterDescriptor(
                parameter.Key,
                parameter.DisplayName,
                (ContractAnalysisParameterKind)(int)parameter.Kind,
                parameter.DefaultValue,
                parameter.Minimum,
                parameter.Maximum,
                parameter.Increment,
                parameter.Choices)).ToList();
            if (_modes.CurrentMode == OpticalWorkbenchMode.Sequential && navigation.Count > 1)
                parameters.Insert(0, AnalysisConfigurationSelection.Descriptor(analysisName, navigation.Count));
            return parameters;
        }
    }

    public Dictionary<string, string> MergeSettings(
        string analysisName,
        IReadOnlyDictionary<string, string>? saved)
    {
        EnsureAvailable(analysisName);
        var available = GetParameters(analysisName).FirstOrDefault(parameter => parameter.Key == AnalysisConfigurationSelection.Key);
        var applicable = saved?.ToDictionary(item => item.Key, item => item.Value);
        if (applicable is not null && applicable.TryGetValue(AnalysisConfigurationSelection.Key, out var selection)
            && available?.Choices?.Contains(selection, StringComparer.Ordinal) != true)
            applicable[AnalysisConfigurationSelection.Key] = AnalysisConfigurationSelection.Current;
        var merged = GetParameters(analysisName, applicable).ToDictionary(parameter => parameter.Key, parameter => parameter.DefaultValue);
        if (applicable is not null)
            foreach (var item in applicable)
                if (merged.ContainsKey(item.Key)) merged[item.Key] = item.Value;
        return merged;
    }

    public Task<AnalysisResultDto> RunAsync(
        AnalysisRequestDto request,
        CancellationToken cancellationToken = default)
    {
        EnsureAvailable(request.AnalysisKey);
        Optic snapshot;
        NonSequentialDocument nonSequentialSnapshot;
        LoadedOpticalDocument? configurationSnapshot;
        OpticalDocumentSnapshot sourceDocument;
        long sourceRevision;
        CancellationTokenSource linked;
        lock (Gate)
        {
            sourceRevision = Workspace.Revision;
            sourceDocument = Workspace.GetDocumentSnapshot();
            snapshot = Optic.FromSnapshot(Runtime.CurrentOptic.ToSnapshot());
            nonSequentialSnapshot = Runtime.CurrentNonSequentialDocument.Clone();
            snapshot.ConfigureRayTraceCache(_rayTraceCache, sourceRevision);
            // Freeze inactive optics too. Distinct configurations must never
            // share the current optic's ray-cache identity.
            configurationSnapshot = _modes.CurrentMode == OpticalWorkbenchMode.Sequential
                && (Runtime.GetMultiConfigurationNavigation().Count > 1
                    || CanonicalKey(request.AnalysisKey) == "Configuration Matrix Spot Diagram")
                ? Runtime.CaptureDocument() : null;
            linked = Workspace.LinkDocumentToken(cancellationToken);
        }

        return RunAnalysisWorkerAsync(
            snapshot,
            nonSequentialSnapshot,
            configurationSnapshot,
            sourceDocument,
            sourceRevision,
            request,
            linked,
            _nonSequentialAnalysisSession);
    }

    private void EnsureAvailable(string analysisName)
    {
        if (WorkbenchAnalysisCatalog.IsRetiredAnalysis(analysisName))
            throw new InvalidOperationException("通用 Zernike 系数入口已移除，请选择 Zernike Fringe、Standard 或 Annular 系数分析。");
        if (!WorkbenchAnalysisCatalog.IsAvailableInMode(analysisName, _modes.CurrentMode))
        {
            throw new InvalidOperationException(
                $"分析“{WorkbenchAnalysisCatalog.DisplayName(WorkbenchAnalysisCatalog.CanonicalKey(analysisName))}”不属于当前{(_modes.CurrentMode == OpticalWorkbenchMode.NonSequential ? "非序列" : "顺序")}模式。");
        }
    }

    private static async Task<AnalysisResultDto> RunAnalysisWorkerAsync(
        Optic snapshot,
        NonSequentialDocument nonSequentialSnapshot,
        LoadedOpticalDocument? configurationSnapshot,
        OpticalDocumentSnapshot sourceDocument,
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
                var selection = AnalysisConfigurationSelection.Read(canonicalAnalysisKey, request.Settings);
                var count = configurationSnapshot?.Configurations.Count ?? 1;
                var activeIndex = configurationSnapshot?.ActiveConfigurationIndex ?? 0;
                AnalysisConfigurationSelection.Validate(canonicalAnalysisKey, selection, activeIndex, count);
                var selectedIndex = AnalysisConfigurationSelection.Resolve(selection, activeIndex, count);
                var selectedOptic = configurationSnapshot?.Configurations[selectedIndex] ?? snapshot;
                var parameterWorker = canonicalAnalysisKey == "Interferogram" && configurationSnapshot is not null
                    ? WorkbenchRuntime.CreateComputationRuntime(configurationSnapshot with { ActiveOptic = selectedOptic, ActiveConfigurationIndex = selectedIndex })
                    : selection == AnalysisConfigurationSelection.All
                    && canonicalAnalysisKey == "Configuration Matrix Spot Diagram" && configurationSnapshot is not null
                    ? WorkbenchRuntime.CreateComputationRuntime(configurationSnapshot)
                    : new WorkbenchRuntime(selectedOptic, nonSequentialSnapshot);
                var normalizedSettings = NormalizeAnalysisSettings(
                    parameterWorker,
                    canonicalAnalysisKey,
                    request.Settings);
                if (count > 1)
                {
                    if (selection == AnalysisConfigurationSelection.All)
                        foreach (var (optic, index) in configurationSnapshot!.Configurations.Select((optic, index) => (optic, index)))
                            ValidateConfigurationInputs(optic, normalizedSettings, index + 1);
                    else ValidateConfigurationInputs(selectedOptic, normalizedSettings, selectedIndex + 1);
                }
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
                    ? analysisSession?.ReconstructDetectors(nonSequentialSnapshot, selectedSourceId, selectedOptic.Materials)
                    : null;
                var worker = databaseDetectors is null ? parameterWorker
                    : new WorkbenchRuntime(selectedOptic, nonSequentialSnapshot, databaseDetectors);
                var view = selection == AnalysisConfigurationSelection.All && count > 1
                    && AnalysisConfigurationSelection.AllDisplay(canonicalAnalysisKey) == AnalysisConfigurationDisplay.Overlay
                    ? BuildConfigurationOverlay(configurationSnapshot!, canonicalAnalysisKey, request.Settings, linked.Token)
                    : ToAnalysisViewDto(worker.BuildAnalysisView(canonicalAnalysisKey, normalizedSettings, linked.Token)) with
                    {
                        PresentationKind = WorkbenchAnalysisCatalog.PresentationKind(canonicalAnalysisKey)
                    };
                if (canonicalAnalysisKey == "Configuration Matrix Spot Diagram" && selection != AnalysisConfigurationSelection.All)
                    view = RemapSingleConfigurationMatrix(view, selectedIndex + 1);
                var numbers = selection == AnalysisConfigurationSelection.All
                    ? Enumerable.Range(1, count).ToArray() : [selectedIndex + 1];
                if (count > 1)
                    view = DescribeConfiguration(view, new(selection, count, numbers,
                        selection == AnalysisConfigurationSelection.All
                            ? AnalysisConfigurationSelection.AllDisplay(canonicalAnalysisKey) : AnalysisConfigurationDisplay.Single));
                var summary = SourceSummary(sourceDocument, selectedOptic);
                var scene = view.PresentationKind == AnalysisPresentationKind.CardinalPoints
                    && view.Outcome == Contracts.AnalysisOutcome.Success
                    ? new SceneDto(sourceRevision, SceneDimension.TwoDimensional,
                        ToScene2Dto(new Layout2DBuilder(selectedOptic).Build(options: new LayoutBuildOptions(
                            LowerPupil: -1, UpperPupil: 1, MarginalAndChiefOnly: true))), null, summary)
                    : null;
                var fingerprintSettings = normalizedSettings.ToDictionary(item => item.Key, item => item.Value);
                if (count > 1) fingerprintSettings[AnalysisConfigurationSelection.Key] = selection;
                linked.Token.ThrowIfCancellationRequested();
                return new AnalysisResultDto(
                    request.InstanceId,
                    request.Generation,
                    sourceRevision,
                    view,
                    new AnalysisExecutionProvenanceDto(
                        canonicalAnalysisKey,
                        CreateRequestFingerprint(canonicalAnalysisKey, fingerprintSettings),
                        "Application.WorkbenchRuntime.BuildAnalysisView/v1"), summary, scene);
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
                OpticalSystemDesign.Application.Runtime.AnalysisParameterKind.Text => merged[key].Trim(),
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

namespace OptilandWorkbench.Application.Contracts;

public interface IWorkspaceEventStream
{
    event EventHandler<WorkspaceChangedEventArgs>? Changed;

    event EventHandler? StatusChanged;

    long Revision { get; }
}

public interface IWorkbenchModeService
{
    event EventHandler<WorkbenchModeChangedEventArgs>? ModeChanged;

    OpticalWorkbenchMode CurrentMode { get; }

    void SwitchTo(OpticalWorkbenchMode mode);
}

public interface INonSequentialDocumentService
{
    NonSequentialDocumentDto GetDocument();

    IReadOnlyList<NonSequentialObjectKind> GetObjectKinds();

    NonSequentialObjectParameters GetDefaultParameters(NonSequentialObjectKind kind);

    IReadOnlyList<string> GetMaterialNames();

    Guid AddObject(NonSequentialObjectKind kind, int? insertionIndex = null);

    Guid DuplicateObject(Guid id);

    Guid PasteObject(NonSequentialObjectUpdateDto template, int insertionIndex);

    void DeleteObject(Guid id);

    void MoveObject(Guid id, int destinationIndex);

    void UpdateObject(NonSequentialObjectUpdateDto update);

    void UpdateWavelengths(IReadOnlyList<NonSequentialWavelengthDto> wavelengths);

    NonSequentialConversionResultDto ConvertFromSequential();

    Task<NonSequentialMeshImportResultDto> ImportStlAsync(
        string path,
        NonSequentialMeshImportOptionsDto options,
        CancellationToken cancellationToken = default);

    Task<NonSequentialTraceRunResultDto> TraceAsync(
        NonSequentialTraceRunRequestDto request,
        CancellationToken cancellationToken = default);

    NonSequentialRayDatabaseDto OpenRayDatabase(string path, string? pathFilterExpression = null);
}

public interface INonSequentialAnalysisService
{
    event EventHandler<NonSequentialTraceSessionDto?>? SessionChanged;

    event EventHandler<NonSequentialTraceSessionDto?>? LayoutSessionChanged;

    NonSequentialTraceSessionDto? GetCurrentSession();

    NonSequentialTraceSessionDto? GetCurrentLayoutSession();

    Task<NonSequentialTraceSessionDto> PrepareLayoutSessionAsync(CancellationToken cancellationToken = default);

    Task<NonSequentialTraceSessionDto> RefreshLayoutSessionAsync(CancellationToken cancellationToken = default);

    [Obsolete("Compatibility alias. Layout tracing must be an explicit user action; use PrepareLayoutSessionAsync.")]
    Task<NonSequentialTraceSessionDto> EnsureLayoutSessionAsync(CancellationToken cancellationToken = default);

    Task<NonSequentialTraceRunResultDto> TraceAsync(
        NonSequentialTraceRunRequestDto request,
        CancellationToken cancellationToken = default);

    Task ClearDetectorsAsync(CancellationToken cancellationToken = default);

    NonSequentialRayDatabaseDto OpenRayDatabase(string path, string? pathFilterExpression = null);

    NonSequentialRayDatabaseDto InspectRayDatabase(
        string path,
        string? pathFilterExpression = null,
        CancellationToken cancellationToken = default);

    void SelectRayDatabase(string path, string? pathFilterExpression = null);

    NonSequentialRayDatabasePageDto GetRayDatabasePage(
        string? path = null,
        int pageIndex = 0,
        int pageSize = 100,
        string? pathFilterExpression = null,
        CancellationToken cancellationToken = default);

    NonSequentialDetectorViewDto GetDetectorView(
        NonSequentialDetectorViewRequestDto request,
        CancellationToken cancellationToken = default);
}

public interface IOpticalDocumentService
{
    OpticalDocumentSnapshot GetSnapshot();

    string? CurrentPath { get; }

    void NewBlank();

    void NewCooke();

    void NewTessar();

    Task OpenAsync(string path, CancellationToken cancellationToken = default);

    Task SaveAsync(string path, CancellationToken cancellationToken = default);

    bool Undo();

    bool Redo();
}

public interface IPrescriptionService
{
    PrescriptionOptionsDto GetOptions();

    IReadOnlyList<SurfaceRowDto> GetSurfaces();

    SystemSettingsDto GetSystemSettings();

    EnvironmentSettingsDto GetEnvironmentSettings();

    IReadOnlyList<string> GetGlassCatalogs();

    IReadOnlyList<FieldRowDto> GetFields();

    IReadOnlyList<WavelengthRowDto> GetWavelengths();

    void AddSurface();

    void RemoveSurface(int surfaceNumber);

    void UpdateSurface(SurfaceRowDto surface);

    void UpdateSurfaceComponents(int surfaceNumber, SurfaceComponentUpdateDto update);

    void AddField();

    void RemoveField(int index);

    void UpdateField(FieldRowDto field);

    void AddWavelength();

    void RemoveWavelength(int index);

    void UpdateWavelength(WavelengthRowDto wavelength);

    void UpdateSystemSettings(SystemSettingsDto settings);

    void UpdateEnvironmentSettings(EnvironmentSettingsDto settings);

    void UpdateGlassCatalogs(IReadOnlyList<string> catalogs);
}

public interface IAnalysisService
{
    IReadOnlyList<string> AnalysisNames { get; }

    string CanonicalKey(string analysisName);

    IReadOnlyList<AnalysisParameterDescriptor> GetParameters(string analysisName);

    Dictionary<string, string> MergeSettings(string analysisName, IReadOnlyDictionary<string, string>? saved);

    Task<AnalysisResultDto> RunAsync(AnalysisRequestDto request, CancellationToken cancellationToken = default);
}

public interface IVisualizationService
{
    VisualizationOptionsDto GetVisualizationOptions();

    Task<SceneDto> BuildSceneAsync(SceneDimension dimension, CancellationToken cancellationToken = default);

    Task<SceneDto> BuildSceneAsync(
        VisualizationRequestDto request,
        CancellationToken cancellationToken = default);
}

public interface ICadExportService
{
    Task<CadExportResultDto> ExportAsync(
        string path,
        CadExportOptionsDto? options = null,
        CancellationToken cancellationToken = default);
}

public interface IOptimizationService
{
    IReadOnlyList<string> OptimizerNames { get; }

    IReadOnlyList<MeritOperandTypeDto> GetMeritOperandTypes();

    IReadOnlyList<MeritOperandRowDto> GetMeritFunction();

    void SetMeritFunction(IReadOnlyList<MeritOperandRowDto> operands);

    void GenerateDefaultMeritFunction(MeritFunctionPreset preset);

    void GenerateMeritFunction(OptimizationWizardSettingsDto settings);

    OptimizationVariableUpdateResultDto UpdateAllSurfaceVariables(
        OptimizationVariableUpdateMode mode);

    Task<QuickFocusResultDto> QuickFocusAsync(
        CancellationToken cancellationToken = default);

    Task<OptimizationResultDto> OptimizeSurfaceRadiusAsync(
        int surfaceNumber,
        string optimizerName,
        int maxIterations,
        CancellationToken cancellationToken = default);

    Task<OptimizationRunResultDto> OptimizeVariablesAsync(
        string optimizerName,
        int maxIterations,
        CancellationToken cancellationToken = default);
}

public interface ITolerancingService
{
    IReadOnlyList<ToleranceOperandDto> GenerateWizard(ToleranceWizardSettingsDto settings);

    ToleranceValidationResultDto ValidateOperands(IReadOnlyList<ToleranceOperandDto> operands);

    Task<TolerancingResultDto> RunAsync(TolerancingRequestDto request, CancellationToken cancellationToken = default);
}

public interface IMultiConfigurationService
{
    IReadOnlyList<MultiConfigurationRowDto> GetRows();

    int Add();

    void Activate(int configurationIndex);

    void SetThickness(int configurationIndex, int surfaceNumber, double thickness);
}

public interface IMaterialCatalogService
{
    IReadOnlyList<MaterialCatalogDto> GetCatalogs();

    IReadOnlyList<string> GetCatalogNames();

    IReadOnlyList<GlassMaterialDto> GetGlasses();

    AnalysisViewDto Analyze(MaterialAnalysisRequestDto request);

    Task<MaterialCatalogImportResultDto> ImportZemaxCatalogAsync(
        string path,
        CancellationToken cancellationToken = default);
}

public interface ILensLibraryService
{
    string LibraryDirectory { get; }

    IReadOnlyList<LensLibraryEntryDto> GetLenses();

    IReadOnlyList<CommercialLensEntryDto> GetCommercialLenses();

    string? GetNativeProjectPath(string lensId);

    string? GetCommercialNativeProjectPath(string lensId);

    Task<SceneDto?> BuildPreviewAsync(
        string lensId,
        CancellationToken cancellationToken = default);

    Task<SceneDto?> BuildCommercialPreviewAsync(
        string lensId,
        CancellationToken cancellationToken = default);
}

public interface IWorkbenchApplication : IDisposable
{
    IWorkbenchModeService Modes { get; }

    INonSequentialDocumentService NonSequential { get; }

    INonSequentialAnalysisService NonSequentialAnalysis { get; }

    IOpticalDocumentService Documents { get; }

    IPrescriptionService Prescription { get; }

    IAnalysisService Analyses { get; }

    IVisualizationService Visualization { get; }

    ICadExportService CadExport { get; }

    IOptimizationService Optimization { get; }

    ITolerancingService Tolerancing { get; }

    IMultiConfigurationService MultiConfiguration { get; }

    IMaterialCatalogService Materials { get; }

    ILensLibraryService Lenses { get; }

    IWorkspaceEventStream Events { get; }
}

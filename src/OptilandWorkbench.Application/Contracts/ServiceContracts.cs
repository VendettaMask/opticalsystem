namespace OptilandWorkbench.Application.Contracts;

public interface IWorkspaceEventStream
{
    event EventHandler<WorkspaceChangedEventArgs>? Changed;

    long Revision { get; }
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

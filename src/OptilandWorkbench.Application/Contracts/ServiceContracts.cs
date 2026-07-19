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

public interface IOptimizationService
{
    IReadOnlyList<string> OptimizerNames { get; }

    Task<OptimizationResultDto> OptimizeSurfaceRadiusAsync(
        int surfaceNumber,
        string optimizerName,
        int maxIterations,
        CancellationToken cancellationToken = default);
}

public interface ITolerancingService
{
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

    IReadOnlyList<GlassMaterialDto> GetGlasses();

    Task<MaterialCatalogImportResultDto> ImportZemaxCatalogAsync(
        string path,
        CancellationToken cancellationToken = default);
}

public interface IWorkbenchApplication : IDisposable
{
    IOpticalDocumentService Documents { get; }

    IPrescriptionService Prescription { get; }

    IAnalysisService Analyses { get; }

    IVisualizationService Visualization { get; }

    IOptimizationService Optimization { get; }

    ITolerancingService Tolerancing { get; }

    IMultiConfigurationService MultiConfiguration { get; }

    IMaterialCatalogService Materials { get; }

    IWorkspaceEventStream Events { get; }
}

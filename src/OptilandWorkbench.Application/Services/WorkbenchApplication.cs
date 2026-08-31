using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Core;

namespace OptilandWorkbench.Application.Services;

public sealed class WorkbenchApplication : IWorkbenchApplication
{
    private readonly WorkspaceCoordinator _workspace;
    private readonly NonSequentialAnalysisSession _nonSequentialAnalysisSession;
    private readonly NonSequentialLayoutSession _nonSequentialLayoutSession;
    private readonly NonSequentialAnalysisService _nonSequentialAnalysisService;
    private bool _disposed;

    private WorkbenchApplication(
        Optic optic,
        string? userCatalogDirectory,
        string lensLibraryDirectory,
        string? zemaxStockCatalogDirectory)
    {
        var context = new OpticContext(optic);
        _workspace = new WorkspaceCoordinator(context);
        _nonSequentialAnalysisSession = new NonSequentialAnalysisSession(_workspace);
        _nonSequentialLayoutSession = new NonSequentialLayoutSession(_workspace);

        Modes = new WorkbenchModeService(_workspace);
        _nonSequentialAnalysisService = new NonSequentialAnalysisService(
            _workspace,
            _nonSequentialAnalysisSession,
            _nonSequentialLayoutSession);
        NonSequentialAnalysis = _nonSequentialAnalysisService;
        NonSequential = new NonSequentialDocumentService(_workspace, NonSequentialAnalysis);

        Documents = new OpticalDocumentService(_workspace);
        Prescription = new PrescriptionService(_workspace);
        Analyses = new AnalysisService(_workspace, Modes, _nonSequentialAnalysisSession);
        Visualization = new VisualizationService(_workspace, Modes, _nonSequentialLayoutSession);
        CadExport = new CadExportService(_workspace);
        Optimization = new OptimizationService(_workspace);
        Tolerancing = new TolerancingService(_workspace);
        MultiConfiguration = new MultiConfigurationService(_workspace);
        Materials = new MaterialCatalogService(_workspace, userCatalogDirectory);
        Lenses = new LensLibraryService(lensLibraryDirectory, zemaxStockCatalogDirectory);
        Events = _workspace;
    }

    public IOpticalDocumentService Documents { get; }

    public IWorkbenchModeService Modes { get; }

    public INonSequentialDocumentService NonSequential { get; }

    public INonSequentialAnalysisService NonSequentialAnalysis { get; }

    public IPrescriptionService Prescription { get; }

    public IAnalysisService Analyses { get; }

    public IVisualizationService Visualization { get; }

    public ICadExportService CadExport { get; }

    public IOptimizationService Optimization { get; }

    public ITolerancingService Tolerancing { get; }

    public IMultiConfigurationService MultiConfiguration { get; }

    public IMaterialCatalogService Materials { get; }

    public ILensLibraryService Lenses { get; }

    public IWorkspaceEventStream Events { get; }

    public static WorkbenchApplication Create(
        string? sample = null,
        string? userCatalogDirectory = null,
        string? lensLibraryDirectory = null,
        string? zemaxStockCatalogDirectory = null)
    {
        MaterialCatalogService.LoadUserCatalogs(userCatalogDirectory);
        var optic = sample?.ToLowerInvariant() switch
        {
            "cooke" => Optic.CreateCookeTriplet(),
            "tessar" => Optic.CreateTessarLens(),
            _ => Optic.CreateBlank()
        };
        var usesPackagedLensLibrary = lensLibraryDirectory is null;
        lensLibraryDirectory ??= Path.Combine(AppContext.BaseDirectory, "LensLibrary");
        if (usesPackagedLensLibrary && string.IsNullOrWhiteSpace(zemaxStockCatalogDirectory))
        {
            var candidate = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Zemax",
                "Stockcat");
            zemaxStockCatalogDirectory = Directory.Exists(candidate) ? candidate : null;
        }

        return new WorkbenchApplication(
            optic,
            userCatalogDirectory,
            lensLibraryDirectory,
            zemaxStockCatalogDirectory);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _nonSequentialAnalysisService.Dispose();
        _nonSequentialLayoutSession.Dispose();
        _nonSequentialAnalysisSession.Dispose();
        _workspace.Dispose();
    }
}

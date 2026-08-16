using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Core;

namespace OptilandWorkbench.Application.Services;

public sealed class WorkbenchApplication : IWorkbenchApplication
{
    private readonly WorkspaceCoordinator _workspace;
    private bool _disposed;

    private WorkbenchApplication(
        Optic optic,
        string? userCatalogDirectory,
        string lensLibraryDirectory,
        string? zemaxStockCatalogDirectory)
    {
        var context = new OpticContext(optic);
        _workspace = new WorkspaceCoordinator(context);

        Documents = new OpticalDocumentService(_workspace);
        Prescription = new PrescriptionService(_workspace);
        Analyses = new AnalysisService(_workspace);
        Visualization = new VisualizationService(_workspace);
        CadExport = new CadExportService(_workspace);
        Optimization = new OptimizationService(_workspace);
        Tolerancing = new TolerancingService(_workspace);
        MultiConfiguration = new MultiConfigurationService(_workspace);
        Materials = new MaterialCatalogService(_workspace, userCatalogDirectory);
        Lenses = new LensLibraryService(lensLibraryDirectory, zemaxStockCatalogDirectory);
        Events = _workspace;
    }

    public IOpticalDocumentService Documents { get; }

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
        _workspace.Dispose();
    }
}

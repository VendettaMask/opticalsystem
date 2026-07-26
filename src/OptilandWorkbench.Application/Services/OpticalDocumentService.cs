using System.Text.Json;
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
using ContractAnalysisColorMap = OptilandWorkbench.Application.Contracts.AnalysisColorMap;
using ContractAnalysisLineStyle = OptilandWorkbench.Application.Contracts.AnalysisLineStyle;
using ContractAnalysisMarkerStyle = OptilandWorkbench.Application.Contracts.AnalysisMarkerStyle;
using ContractAnalysisParameterDescriptor = OptilandWorkbench.Application.Contracts.AnalysisParameterDescriptor;
using ContractAnalysisParameterKind = OptilandWorkbench.Application.Contracts.AnalysisParameterKind;
using ContractAnalysisSeriesKind = OptilandWorkbench.Application.Contracts.AnalysisSeriesKind;

namespace OptilandWorkbench.Application.Services;

internal sealed class OpticalDocumentService : WorkbenchServiceBase, IOpticalDocumentService
{
    public OpticalDocumentService(WorkspaceCoordinator workspace)
        : base(workspace)
    {
    }

    public string? CurrentPath => Workspace.CurrentPath;

    public OpticalDocumentSnapshot GetSnapshot()
    {
        return Workspace.GetDocumentSnapshot();
    }

    public void NewBlank() => Workspace.ReplaceDocument(WorkspaceChangeCategory.Document, Connector.NewBlank);

    public void NewCooke() => Workspace.ReplaceDocument(WorkspaceChangeCategory.Document, Connector.NewDemo);

    public void NewTessar() => Workspace.ReplaceDocument(WorkspaceChangeCategory.Document, Connector.NewTessar);

    public async Task OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Workspace.CancelDocumentTasks();
        using var linked = Workspace.LinkDocumentToken(cancellationToken);
        var fullPath = Path.GetFullPath(path);
        var document = await OpticalWorkspaceModel.ReadDocumentAsync(fullPath, linked.Token).ConfigureAwait(false);
        linked.Token.ThrowIfCancellationRequested();
        lock (Gate)
        {
            linked.Token.ThrowIfCancellationRequested();
            Workspace.CurrentPath = fullPath;
            Workspace.SetPendingCategory(WorkspaceChangeCategory.Document);
            Connector.ApplyLoadedDocument(document, fullPath);
        }
    }

    public async Task SaveAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LoadedOpticalDocument document;
        long documentGeneration;
        lock (Gate)
        {
            document = Connector.CaptureDocument();
            documentGeneration = Workspace.DocumentGeneration;
        }

        var fullPath = Path.GetFullPath(path);
        await OpticalWorkspaceModel.SaveDocumentAsync(document, fullPath, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        lock (Gate)
        {
            if (documentGeneration != Workspace.DocumentGeneration)
            {
                return;
            }

            Workspace.SetPendingCategory(WorkspaceChangeCategory.Document);
            Workspace.CurrentPath = fullPath;
            Connector.NotifySaved(fullPath);
        }
    }

    public bool Undo() => Mutate(WorkspaceChangeCategory.Prescription, Connector.Undo);

    public bool Redo() => Mutate(WorkspaceChangeCategory.Prescription, Connector.Redo);
}

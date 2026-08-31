using System.Text.Json;
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
using ContractAnalysisColorMap = OptilandWorkbench.Application.Contracts.AnalysisColorMap;
using ContractAnalysisLineStyle = OptilandWorkbench.Application.Contracts.AnalysisLineStyle;
using ContractAnalysisMarkerStyle = OptilandWorkbench.Application.Contracts.AnalysisMarkerStyle;
using ContractAnalysisParameterDescriptor = OptilandWorkbench.Application.Contracts.AnalysisParameterDescriptor;
using ContractAnalysisParameterKind = OptilandWorkbench.Application.Contracts.AnalysisParameterKind;
using ContractAnalysisSeriesKind = OptilandWorkbench.Application.Contracts.AnalysisSeriesKind;

namespace OptilandWorkbench.Application.Services;

internal sealed class OpticalDocumentService : WorkbenchServiceBase, IOpticalDocumentService
{
    private readonly Func<string, CancellationToken, Task<LoadedOpticalDocument>> _readDocumentAsync;
    private readonly Func<LoadedOpticalDocument, string, CancellationToken, Task> _saveDocumentAsync;
    private Task _saveTail = Task.CompletedTask;

    public OpticalDocumentService(WorkspaceCoordinator workspace)
        : this(workspace, WorkbenchRuntime.ReadDocumentAsync, WorkbenchRuntime.SaveDocumentAsync)
    {
    }

    internal OpticalDocumentService(
        WorkspaceCoordinator workspace,
        Func<LoadedOpticalDocument, string, CancellationToken, Task> saveDocumentAsync)
        : this(workspace, WorkbenchRuntime.ReadDocumentAsync, saveDocumentAsync)
    {
    }

    internal OpticalDocumentService(
        WorkspaceCoordinator workspace,
        Func<string, CancellationToken, Task<LoadedOpticalDocument>> readDocumentAsync,
        Func<LoadedOpticalDocument, string, CancellationToken, Task> saveDocumentAsync)
        : base(workspace)
    {
        _readDocumentAsync = readDocumentAsync ?? throw new ArgumentNullException(nameof(readDocumentAsync));
        _saveDocumentAsync = saveDocumentAsync ?? throw new ArgumentNullException(nameof(saveDocumentAsync));
    }

    public string? CurrentPath => Workspace.CurrentPath;

    public OpticalDocumentSnapshot GetSnapshot()
    {
        return Workspace.GetDocumentSnapshot();
    }

    public void NewBlank() => Workspace.ReplaceDocument(WorkspaceChangeCategory.Document, Runtime.NewBlank);

    public void NewCooke() => Workspace.ReplaceDocument(WorkspaceChangeCategory.Document, Runtime.NewDemo);

    public void NewTessar() => Workspace.ReplaceDocument(WorkspaceChangeCategory.Document, Runtime.NewTessar);

    public async Task OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Workspace.CancelDocumentTasks();
        using var linked = Workspace.LinkDocumentToken(cancellationToken);
        var fullPath = Path.GetFullPath(path);
        var document = await _readDocumentAsync(fullPath, linked.Token).ConfigureAwait(false);
        linked.Token.ThrowIfCancellationRequested();
        Workspace.ReplaceDocument(
            WorkspaceChangeCategory.Document,
            () => Runtime.ApplyLoadedDocument(document, fullPath),
            fullPath,
            cancellationToken);
    }

    public Task SaveAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = Path.GetFullPath(path);
        LoadedOpticalDocument document;
        long documentGeneration;
        long sourceRevision;
        Task predecessor;
        TaskCompletionSource completion;
        lock (Gate)
        {
            document = Runtime.CaptureDocument();
            documentGeneration = Workspace.DocumentGeneration;
            sourceRevision = Workspace.Revision;
            predecessor = _saveTail;
            completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _saveTail = completion.Task;
        }

        return SaveQueuedAsync(
            predecessor,
            completion,
            document,
            documentGeneration,
            sourceRevision,
            fullPath,
            cancellationToken);
    }

    private async Task SaveQueuedAsync(
        Task predecessor,
        TaskCompletionSource completion,
        LoadedOpticalDocument document,
        long documentGeneration,
        long sourceRevision,
        string fullPath,
        CancellationToken cancellationToken)
    {
        try
        {
            await predecessor.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            await _saveDocumentAsync(document, fullPath, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            lock (Gate)
            {
                if (documentGeneration != Workspace.DocumentGeneration)
                {
                    return;
                }

                Workspace.CurrentPath = fullPath;
                Workspace.MarkSavedRevision(sourceRevision);
                Runtime.NotifySaved(
                    fullPath,
                    includesCurrentRevision: sourceRevision == Workspace.Revision);
            }
        }
        finally
        {
            completion.TrySetResult();
        }
    }

    public bool Undo() => MutateTransactional(WorkspaceChangeCategory.Prescription, Runtime.Undo);

    public bool Redo() => MutateTransactional(WorkspaceChangeCategory.Prescription, Runtime.Redo);
}

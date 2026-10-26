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
using OpticalSystemDesign.Core.Optimization;
using OpticalSystemDesign.Core.Phase;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.Core.Visualization;
using ContractAnalysisColorMap = OpticalSystemDesign.Application.Contracts.AnalysisColorMap;
using ContractAnalysisLineStyle = OpticalSystemDesign.Application.Contracts.AnalysisLineStyle;
using ContractAnalysisMarkerStyle = OpticalSystemDesign.Application.Contracts.AnalysisMarkerStyle;
using ContractAnalysisParameterDescriptor = OpticalSystemDesign.Application.Contracts.AnalysisParameterDescriptor;
using ContractAnalysisParameterKind = OpticalSystemDesign.Application.Contracts.AnalysisParameterKind;
using ContractAnalysisSeriesKind = OpticalSystemDesign.Application.Contracts.AnalysisSeriesKind;

namespace OpticalSystemDesign.Application.Services;

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

    public Task OpenAsync(string path, CancellationToken cancellationToken = default)
        => OpenDocumentAsync(path, false, cancellationToken);

    public Task RestoreRecoveryCopyAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!path.EndsWith(".staropt", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("恢复副本必须是 STAROPT 工程。", nameof(path));
        return OpenDocumentAsync(path, true, cancellationToken);
    }

    public DocumentRecoveryState GetRecoveryState() => Workspace.GetRecoveryState();

    public async Task<DocumentRecoveryState?> WriteRecoveryCopyAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!path.EndsWith(".staropt", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("恢复副本必须使用 STAROPT 格式。", nameof(path));
        var fullPath = Path.GetFullPath(path);
        LoadedOpticalDocument document;
        DocumentRecoveryState source;
        lock (Gate)
        {
            source = Workspace.GetRecoveryState();
            if (!source.IsDirty) return null;
            if (source.OriginalPath is not null && string.Equals(fullPath, source.OriginalPath,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new ArgumentException("恢复副本不能覆盖正式工程。", nameof(path));
            document = Runtime.CaptureDocument();
        }
        await _saveDocumentAsync(document, fullPath, cancellationToken).ConfigureAwait(false);
        return source;
    }

    private async Task OpenDocumentAsync(string path, bool recovery, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CancellationTokenSource linked;
        long sourceRevision;
        long documentGeneration;
        lock (Gate)
        {
            Workspace.CancelDocumentTasks();
            linked = Workspace.LinkDocumentToken(cancellationToken);
            sourceRevision = Workspace.Revision;
            documentGeneration = Workspace.DocumentGeneration;
        }
        using var lifetime = linked;
        var fullPath = Path.GetFullPath(path);
        var document = await _readDocumentAsync(fullPath, linked.Token).ConfigureAwait(false);
        linked.Token.ThrowIfCancellationRequested();
        Workspace.ReplaceDocument(
            WorkspaceChangeCategory.Document,
            () => Runtime.ApplyLoadedDocument(document, fullPath),
            recovery ? null : fullPath,
            cancellationToken,
            validateBeforeReplace: () =>
            {
                linked.Token.ThrowIfCancellationRequested();
                if (sourceRevision != Workspace.Revision || documentGeneration != Workspace.DocumentGeneration)
                    throw new OperationCanceledException("打开期间文档已变化，旧打开请求已取消。", linked.Token);
            });
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

namespace OptilandWorkbench.Application.Runtime;

internal sealed record DocumentUndoRedoCheckpoint(
    IReadOnlyList<LoadedOpticalDocument> Undo,
    IReadOnlyList<LoadedOpticalDocument> Redo);

internal sealed class DocumentUndoRedoManager
{
    private readonly LinkedList<LoadedOpticalDocument> _undo = new();
    private readonly LinkedList<LoadedOpticalDocument> _redo = new();

    public DocumentUndoRedoManager(int capacity = 100)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "撤销历史容量必须大于零。");
        }

        Capacity = capacity;
    }

    public int Capacity { get; }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public void Capture(LoadedOpticalDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        AddBounded(_undo, document);
        _redo.Clear();
    }

    public bool TryUndo(
        LoadedOpticalDocument current,
        out LoadedOpticalDocument? previous)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (!CanUndo)
        {
            previous = null;
            return false;
        }

        previous = _undo.Last!.Value;
        _undo.RemoveLast();
        AddBounded(_redo, current);
        return true;
    }

    public bool TryRedo(
        LoadedOpticalDocument current,
        out LoadedOpticalDocument? next)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (!CanRedo)
        {
            next = null;
            return false;
        }

        next = _redo.Last!.Value;
        _redo.RemoveLast();
        AddBounded(_undo, current);
        return true;
    }

    public DocumentUndoRedoCheckpoint CreateCheckpoint() => new(
        _undo.ToArray(),
        _redo.ToArray());

    public void RestoreCheckpoint(DocumentUndoRedoCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        _undo.Clear();
        _redo.Clear();
        foreach (var document in checkpoint.Undo)
        {
            _undo.AddLast(document);
        }

        foreach (var document in checkpoint.Redo)
        {
            _redo.AddLast(document);
        }
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }

    private void AddBounded(
        LinkedList<LoadedOpticalDocument> history,
        LoadedOpticalDocument document)
    {
        history.AddLast(document);
        if (history.Count > Capacity)
        {
            history.RemoveFirst();
        }
    }
}

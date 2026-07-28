using OptilandWorkbench.Core.Serialization;

namespace OptilandWorkbench.Core.Services;

public sealed class UndoRedoManager
{
    private readonly LinkedList<OpticSnapshot> _undo = new();
    private readonly LinkedList<OpticSnapshot> _redo = new();

    public UndoRedoManager(int capacity = 100)
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

    public void Capture(Optic optic)
    {
        AddBounded(_undo, optic.ToSnapshot());
        _redo.Clear();
    }

    public bool TryUndo(Optic optic)
    {
        if (!CanUndo)
        {
            return false;
        }

        var current = optic.ToSnapshot();
        var previous = _undo.Last!.Value;
        optic.ApplySnapshot(previous);
        _undo.RemoveLast();
        AddBounded(_redo, current);
        return true;
    }

    public bool TryRedo(Optic optic)
    {
        if (!CanRedo)
        {
            return false;
        }

        var current = optic.ToSnapshot();
        var next = _redo.Last!.Value;
        optic.ApplySnapshot(next);
        _redo.RemoveLast();
        AddBounded(_undo, current);
        return true;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }

    private void AddBounded(LinkedList<OpticSnapshot> history, OpticSnapshot snapshot)
    {
        history.AddLast(snapshot);
        if (history.Count > Capacity)
        {
            history.RemoveFirst();
        }
    }
}

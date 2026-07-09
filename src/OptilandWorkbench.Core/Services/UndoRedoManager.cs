using OptilandWorkbench.Core.Serialization;

namespace OptilandWorkbench.Core.Services;

public sealed class UndoRedoManager
{
    private readonly Stack<OpticSnapshot> _undo = new();
    private readonly Stack<OpticSnapshot> _redo = new();

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public void Capture(Optic optic)
    {
        _undo.Push(optic.ToSnapshot());
        _redo.Clear();
    }

    public bool TryUndo(Optic optic)
    {
        if (!CanUndo)
        {
            return false;
        }

        var current = optic.ToSnapshot();
        var previous = _undo.Pop();
        _redo.Push(current);
        optic.ApplySnapshot(previous);
        return true;
    }

    public bool TryRedo(Optic optic)
    {
        if (!CanRedo)
        {
            return false;
        }

        var current = optic.ToSnapshot();
        var next = _redo.Pop();
        _undo.Push(current);
        optic.ApplySnapshot(next);
        return true;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}

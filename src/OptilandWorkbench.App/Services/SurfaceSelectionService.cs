namespace OptilandWorkbench.App.Services;

public sealed class SurfaceSelectionChangedEventArgs(int? surfaceNumber) : EventArgs
{
    public int? SurfaceNumber { get; } = surfaceNumber;
}

public sealed class SurfaceSelectionService
{
    public event EventHandler<SurfaceSelectionChangedEventArgs>? Changed;

    public int? SelectedSurfaceNumber { get; private set; }

    public void Select(int? surfaceNumber)
    {
        if (SelectedSurfaceNumber == surfaceNumber)
        {
            return;
        }

        SelectedSurfaceNumber = surfaceNumber;
        Changed?.Invoke(this, new SurfaceSelectionChangedEventArgs(surfaceNumber));
    }
}

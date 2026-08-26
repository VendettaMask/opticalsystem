using OptilandWorkbench.Application.Contracts;

namespace OptilandWorkbench.Application.Services;

internal sealed class WorkbenchModeService : IWorkbenchModeService
{
    private readonly WorkspaceCoordinator _workspace;

    public WorkbenchModeService(WorkspaceCoordinator workspace)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
    }

    public event EventHandler<WorkbenchModeChangedEventArgs>? ModeChanged;

    public OpticalWorkbenchMode CurrentMode { get; private set; }

    public void SwitchTo(OpticalWorkbenchMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        if (mode == CurrentMode)
        {
            return;
        }

        var previous = CurrentMode;
        CurrentMode = mode;
        ModeChanged?.Invoke(this, new WorkbenchModeChangedEventArgs(previous, mode));
    }
}

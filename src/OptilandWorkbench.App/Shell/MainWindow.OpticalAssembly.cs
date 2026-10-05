using OptilandWorkbench.App.Laboratories;

namespace OptilandWorkbench.App;

public sealed partial class MainWindow
{
    private OpticalAssemblyWindow? _opticalAssemblyWindow;

    private void ShowOpticalAssembly()
    {
        if (_opticalAssemblyWindow is { } existing) { existing.Activate(); return; }
        var window = new OpticalAssemblyWindow(_application);
        _opticalAssemblyWindow = window;
        window.Closed += (_, _) => _opticalAssemblyWindow = null;
        window.Show(this);
    }
}

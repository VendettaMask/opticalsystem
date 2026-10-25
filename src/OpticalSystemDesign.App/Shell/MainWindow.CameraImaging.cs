using OpticalSystemDesign.App.Laboratories;

namespace OpticalSystemDesign.App;

public sealed partial class MainWindow
{
    private CameraImagingWindow? _cameraImagingWindow;
    private void ShowCameraImaging()
    {
        if (_cameraImagingWindow is { } existing) { existing.Activate(); return; }
        _cameraImagingWindow = new CameraImagingWindow(_application);
        _cameraImagingWindow.Closed += (_, _) => _cameraImagingWindow = null;
        _cameraImagingWindow.Show(this);
    }
}

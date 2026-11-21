namespace OpticalSystemDesign.App;

public sealed partial class MainWindow
{
    private async Task ShowLensScaleAsync() => await new LensScaleWindow(_application.Prescription).ShowDialog<bool>(this);
}

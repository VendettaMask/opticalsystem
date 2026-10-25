using OpticalSystemDesign.App.Panels;

namespace OpticalSystemDesign.App;

public sealed partial class MainWindow
{
    private MarkdownReportWindow? _reportExportWindow;

    private void ShowMarkdownReportExport(bool exportImmediately = false)
    {
        if (_reportExportWindow is { } existing)
        {
            existing.Activate();
            if (exportImmediately) _ = existing.StartExportAsync();
            return;
        }
        _reportExportWindow = new MarkdownReportWindow(_application, _settings, exportImmediately);
        _reportExportWindow.Closed += (_, _) => _reportExportWindow = null;
        _reportExportWindow.Show(this);
    }
}

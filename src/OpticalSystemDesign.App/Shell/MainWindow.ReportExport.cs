using OpticalSystemDesign.App.Panels;

namespace OpticalSystemDesign.App;

public sealed partial class MainWindow
{
    private MarkdownReportWindow? _reportExportWindow;

    private void ShowMarkdownReportExport()
    {
        if (_reportExportWindow is { } existing)
        {
            existing.Activate();
            return;
        }
        _reportExportWindow = new MarkdownReportWindow(_application, _settings);
        _reportExportWindow.Closed += (_, _) => _reportExportWindow = null;
        _reportExportWindow.Show(this);
    }
}

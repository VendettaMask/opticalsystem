using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Services.Updates;

namespace OpticalSystemDesign.App;

public sealed partial class MainWindow
{
    private bool _updateDialogOpen;

    private async Task ShowUpdatesAsync()
    {
        if (_updateDialogOpen) return;
        _updateDialogOpen = true;
        try
        {
            using var client = GitHubReleaseUpdateService.CreateClient();
            var service = new GitHubReleaseUpdateService(client, ProductVersionInfo.Current, ProductReleaseSource.CurrentRuntime);
            await new SoftwareUpdateWindow(service).ShowDialog(this);
        }
        finally { _updateDialogOpen = false; }
    }
}

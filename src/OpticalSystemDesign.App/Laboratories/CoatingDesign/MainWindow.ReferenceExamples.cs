using OpticalSystemDesign.CoatingDesign.Engine;

namespace OpticalSystemDesign.App.Laboratories.CoatingDesign;

public sealed partial class MainWindow
{
    private async Task LoadReferenceExampleAsync(string id)
    {
        if (!await ConfirmUnsavedChangesAsync("切换镀膜示例")) return;
        Load(WorkspaceExample(ReferenceExamples.Create(id)), markDirty: false);
        var revision = _uiRevision;
        SetBusy(true);
        try
        {
            var applied = await _session.RunAsync((snapshot, token) => Task.Run(() => _service.Evaluate(snapshot, token: token), token));
            if (!applied || revision != _uiRevision) return;
            Load(_session.Current, markDirty: false);
            await _plot.PendingEvaluation;
        }
        finally { SetBusy(false); }
    }
}

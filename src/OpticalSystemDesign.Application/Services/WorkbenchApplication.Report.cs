using OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.Application.Services;

public sealed partial class WorkbenchApplication
{
    public DesignReportSnapshot CaptureReportSnapshot()
    {
        lock (_workspace.Gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var document = _workspace.Runtime.CaptureDocument();
            var source = _workspace.GetDocumentSnapshot();
            var capturedAt = DateTimeOffset.Now;
            var copy = new WorkbenchApplication(document.ActiveOptic, null,
                Path.Combine(AppContext.BaseDirectory, "LensLibrary"));
            try
            {
                copy._workspace.Runtime.ApplyLoadedDocument(document, source.Path ?? "report-snapshot.staropt");
                copy._workspace.CurrentPath = source.Path;
                copy.Modes.SwitchTo(Modes.CurrentMode);
                return new DesignReportSnapshot(copy, source, capturedAt);
            }
            catch { copy.Dispose(); throw; }
        }
    }
}

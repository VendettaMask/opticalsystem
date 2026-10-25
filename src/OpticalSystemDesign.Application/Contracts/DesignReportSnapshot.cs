namespace OpticalSystemDesign.Application.Contracts;

/// <summary>A private workspace owned by one report, never the editable source workspace.</summary>
public sealed record DesignReportSnapshot(
    IWorkbenchApplication Workspace,
    OpticalDocumentSnapshot SourceDocument,
    DateTimeOffset CapturedAt) : IDisposable
{
    public void Dispose() => Workspace.Dispose();
}

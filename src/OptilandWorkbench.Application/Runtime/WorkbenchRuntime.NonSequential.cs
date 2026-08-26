using OptilandWorkbench.Core.NonSequential;

namespace OptilandWorkbench.Application.Runtime;

public partial class WorkbenchRuntime
{
    public void ReplaceNonSequentialDocument(NonSequentialDocument document, string status)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.Validate();
        CaptureCurrentState();
        _nonSequentialDocument = document.Clone();
        SetStatus(status);
        OpticChanged?.Invoke(this, EventArgs.Empty);
    }
}

using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Core;

namespace OptilandWorkbench.Application.Legacy;

[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public sealed class OptilandConnector : WorkbenchRuntime
{
    public OptilandConnector(Optic optic)
        : base(optic)
    {
    }
}

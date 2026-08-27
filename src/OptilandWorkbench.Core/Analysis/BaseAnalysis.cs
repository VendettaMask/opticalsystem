using OptilandWorkbench.Core.Services;
using OptilandWorkbench.Core.Capabilities;

namespace OptilandWorkbench.Core.Analysis;

public abstract class BaseAnalysis
{
    protected BaseAnalysis(Optic optic)
    {
        OpticCapabilityPreflight.EnsureSupported(optic, OpticCapabilityOperation.Analysis);
        Optic = optic;
    }

    protected Optic Optic { get; }

    public abstract string Name { get; }

    public abstract AnalysisData GenerateData();

    public AnalysisData GenerateData(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var scope = ComputationCancellation.Push(cancellationToken);
        var result = GenerateData();
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }
}

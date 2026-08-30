using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Tolerancing;

namespace OptilandWorkbench.Tests;

public sealed class ParallelMonteCarloConfigurationTests
{
    [Fact]
    public void ParallelMonteCarloValidatesDegreeAndHonorsCancellation()
    {
        var optic = Optic.CreateBlank();
        var monteCarlo = new MonteCarlo(optic, optic.CreateTolerancing());

        Assert.Throws<ArgumentOutOfRangeException>(() => monteCarlo.RunDetailed(
            1,
            1,
            0,
            CancellationToken.None,
            worker => worker.CreateTolerancing(),
            maxDegreeOfParallelism: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => monteCarlo.RunDetailed(
            MonteCarlo.MaximumTrialCount + 1,
            1,
            0,
            CancellationToken.None,
            worker => worker.CreateTolerancing()));
        Assert.Throws<ArgumentOutOfRangeException>(() => monteCarlo.RunDetailed(
            1,
            1,
            MonteCarlo.MaximumCompensationIterations + 1,
            CancellationToken.None,
            worker => worker.CreateTolerancing()));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => monteCarlo.RunDetailed(
            10,
            1,
            0,
            cancellation.Token,
            worker => worker.CreateTolerancing(),
            maxDegreeOfParallelism: 2));
    }
}

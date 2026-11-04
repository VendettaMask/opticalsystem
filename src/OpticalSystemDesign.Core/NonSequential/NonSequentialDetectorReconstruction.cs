using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.NonSequential;

public static class NonSequentialDetectorReconstruction
{
    public static IReadOnlyList<NonSequentialDetectorFrame> Reconstruct(
        NonSequentialDocument document, IEnumerable<NonSequentialRayBranch> branches,
        Guid? sourceObjectId = null, CancellationToken cancellationToken = default,
        Guid? detectorId = null, bool collectMoments = false)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(branches);
        using var scope = ComputationCancellation.Push(cancellationToken);
        var collector = new NonSequentialDetectorCollector(document, sourceObjectId, detectorId, collectMoments: collectMoments);
        foreach (var branch in branches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            collector.OnBranch(branch);
        }
        return collector.Snapshot();
    }
}

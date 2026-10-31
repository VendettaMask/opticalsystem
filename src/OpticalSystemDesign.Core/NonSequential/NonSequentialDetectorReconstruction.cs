namespace OpticalSystemDesign.Core.NonSequential;

public static class NonSequentialDetectorReconstruction
{
    public static IReadOnlyList<NonSequentialDetectorFrame> Reconstruct(
        NonSequentialDocument document,
        IEnumerable<NonSequentialRayBranch> branches,
        Guid? sourceObjectId = null,
        CancellationToken cancellationToken = default,
        Guid? detectorId = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(branches);
        var detectors = document.Objects
            .Where(item => item.Enabled && item.Kind == NonSequentialObjectKind.DetectorRectangle
                && (detectorId is null || item.Id == detectorId))
            .ToDictionary(item => item.Id, item => new NonSequentialDetectorAccumulator(item));
        var wavelengths = new NonSequentialWavelengthIndex(document.Wavelengths);
        foreach (var branch in branches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sourceObjectId is Guid selectedSource && branch.SourceObjectId != selectedSource)
            {
                continue;
            }

            HashSet<DetectorHitWithinBranch>? recordedHits = null;
            foreach (var segment in branch.Segments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (segment.BranchId != branch.Id
                    || segment.ObjectId is not Guid hitDetectorId
                    || !detectors.TryGetValue(hitDetectorId, out var detector))
                {
                    continue;
                }

                var local = document.ToLocalPoint(hitDetectorId, segment.End);
                var localDirection = document.ToLocalDirection(hitDetectorId, segment.OutgoingDirection);
                if (!detector.Accepts(localDirection))
                {
                    continue;
                }
                if (!(recordedHits ??= new HashSet<DetectorHitWithinBranch>()).Add(
                        new DetectorHitWithinBranch(
                            hitDetectorId,
                            segment.FaceNumber,
                            BitConverter.DoubleToInt64Bits(segment.CumulativePathLength))))
                {
                    continue;
                }

                var wavelength = branch.WavelengthNanometers > 0
                    ? branch.WavelengthNanometers
                    : segment.WavelengthNanometers;
                var directionLength = localDirection.Length;
                detector.Add(
                    local,
                    directionLength > 1e-15 && double.IsFinite(directionLength) ? localDirection / directionLength : null,
                    wavelengths.FindNearestNumber(wavelength),
                    segment.Intensity);
            }
        }
        return detectors.Values.Select(item => item.ToFrame()).ToArray();
    }

    private readonly record struct DetectorHitWithinBranch(
        Guid DetectorId,
        int FaceNumber,
        long CumulativePathBits);

}

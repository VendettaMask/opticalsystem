using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.NonSequential;

/// <summary>Bounded streaming accumulation shared by database reconstruction and design objectives.</summary>
public sealed class NonSequentialDetectorCollector : INonSequentialTraceSink
{
    private readonly NonSequentialDocument _document;
    private readonly Guid? _source;
    private readonly Func<NonSequentialRayBranch, bool> _matches;
    private readonly Dictionary<Guid, NonSequentialDetectorAccumulator> _detectors;
    private readonly NonSequentialWavelengthIndex _wavelengths;
    public NonSequentialDetectorCollector(NonSequentialDocument document, Guid? sourceObjectId = null, Guid? detectorId = null,
        string? pathFilter = null, bool collectMoments = true)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document)); _source = sourceObjectId;
        _matches = NonSequentialPathFilter.Parse(pathFilter).Bind(document);
        _detectors = document.Objects.Where(o => o.Enabled && o.Kind == NonSequentialObjectKind.DetectorRectangle
            && (detectorId is null || detectorId == o.Id)).ToDictionary(o => o.Id, o => new NonSequentialDetectorAccumulator(o, collectMoments));
        _wavelengths = new(document.Wavelengths);
    }
    public void OnBranch(NonSequentialRayBranch branch)
    {
        ComputationCancellation.ThrowIfCancellationRequested();
        if (_source is { } source && branch.SourceObjectId != source || !_matches(branch)) return;
        HashSet<(Guid Object, int Face, long Path)>? recorded = null;
        foreach (var segment in branch.Segments)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            if (segment.BranchId != branch.Id || segment.ObjectId is not { } id || !_detectors.TryGetValue(id, out var detector)) continue;
            var point = _document.ToLocalPoint(id, segment.End); var direction = _document.ToLocalDirection(id, segment.OutgoingDirection);
            if (!detector.Accepts(direction)) continue;
            if (_document.TryFindObject(id, out var item) && item.Parameters is DetectorRectangleParameters { IncidentSurface: { } port }
                && !port.PassesAperture(point)) continue;
            if (!(recorded ??= []).Add((id, segment.FaceNumber, BitConverter.DoubleToInt64Bits(segment.CumulativePathLength)))) continue;
            var wave = branch.WavelengthNanometers > 0 ? branch.WavelengthNanometers : segment.WavelengthNanometers;
            var length = direction.Length;
            detector.Add(point, length > 1e-15 && double.IsFinite(length) ? direction / length : null,
                _wavelengths.FindNearestNumber(wave), segment.Intensity, segment.IncidentField, _document, segment.OutgoingDirection,
                wave, _document.TraceSettings.CoherentDetection);
        }
    }
    public IReadOnlyList<NonSequentialDetectorFrame> Snapshot() => Array.AsReadOnly(_detectors.Values.Select(d => d.ToFrame()).ToArray());
}

namespace OptilandWorkbench.Core.NonSequential;

public static class NonSequentialDetectorReconstruction
{
    public static IReadOnlyList<NonSequentialDetectorFrame> Reconstruct(
        NonSequentialDocument document,
        IEnumerable<NonSequentialRayBranch> branches,
        Guid? sourceObjectId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(branches);
        var detectors = document.Objects
            .Where(item => item.Enabled && item.Kind == NonSequentialObjectKind.DetectorRectangle)
            .ToDictionary(item => item.Id, item => new Accumulator(item));
        var recordedHits = new HashSet<DetectorHitKey>();
        foreach (var branch in branches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sourceObjectId is Guid selectedSource && branch.SourceObjectId != selectedSource)
            {
                continue;
            }

            foreach (var segment in branch.Segments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (segment.ObjectId is not Guid detectorId
                    || !detectors.TryGetValue(detectorId, out var detector)
                    || !recordedHits.Add(new DetectorHitKey(
                        segment.BranchId,
                        detectorId,
                        segment.FaceNumber,
                        BitConverter.DoubleToInt64Bits(segment.CumulativePathLength))))
                {
                    continue;
                }

                var local = document.ToLocalPoint(detectorId, segment.End);
                var localDirection = document.ToLocalDirection(detectorId, segment.OutgoingDirection);
                var wavelength = branch.WavelengthNanometers > 0
                    ? branch.WavelengthNanometers
                    : segment.WavelengthNanometers;
                var wavelengthNumber = document.Wavelengths.ToList().FindIndex(item =>
                    Math.Abs(item.Nanometers - wavelength) <= 1e-9) + 1;
                detector.Add(local, localDirection, Math.Max(1, wavelengthNumber), segment.Intensity);
            }
        }
        return detectors.Values.Select(item => item.ToFrame()).ToArray();
    }

    private readonly record struct DetectorHitKey(
        long OriginalBranchId,
        Guid DetectorId,
        int FaceNumber,
        long CumulativePathBits);

    private sealed class Accumulator
    {
        private readonly NonSequentialObjectDefinition _item;
        private readonly DetectorRectangleParameters _parameters;
        private readonly Dictionary<int, double[]> _pixels = new();
        private readonly Dictionary<int, long[]> _hits = new();
        private readonly Dictionary<int, double[]> _angularPixels = new();
        private readonly Dictionary<int, long[]> _angularHits = new();

        public Accumulator(NonSequentialObjectDefinition item)
        {
            _item = item;
            _parameters = (DetectorRectangleParameters)item.Parameters;
        }

        public void Add(Backend.Vector3D point, Backend.Vector3D direction, int wavelength, double power)
        {
            var x = (int)Math.Floor((point.X / _parameters.WidthMillimeters + 0.5) * _parameters.PixelsX);
            var y = (int)Math.Floor((point.Y / _parameters.HeightMillimeters + 0.5) * _parameters.PixelsY);
            if (x < 0 || x >= _parameters.PixelsX || y < 0 || y >= _parameters.PixelsY) return;
            if (!_pixels.TryGetValue(wavelength, out var values))
            {
                values = new double[_parameters.PixelsX * _parameters.PixelsY];
                _pixels[wavelength] = values;
            }
            values[y * _parameters.PixelsX + x] += power;
            if (!_hits.TryGetValue(wavelength, out var hitValues))
            {
                hitValues = new long[_parameters.PixelsX * _parameters.PixelsY];
                _hits[wavelength] = hitValues;
            }
            hitValues[y * _parameters.PixelsX + x]++;

            var length = direction.Length;
            if (length <= 1e-15 || !double.IsFinite(length)) return;
            var normalized = direction / length;
            var angleX = Math.Atan2(normalized.X, Math.Abs(normalized.Z)) * 180 / Math.PI;
            var angleY = Math.Atan2(normalized.Y, Math.Abs(normalized.Z)) * 180 / Math.PI;
            var angularX = Math.Clamp((int)Math.Floor((angleX / 180 + 0.5) * _parameters.PixelsX), 0, _parameters.PixelsX - 1);
            var angularY = Math.Clamp((int)Math.Floor((angleY / 180 + 0.5) * _parameters.PixelsY), 0, _parameters.PixelsY - 1);
            if (!_angularPixels.TryGetValue(wavelength, out var angularValues))
            {
                angularValues = new double[_parameters.PixelsX * _parameters.PixelsY];
                _angularPixels[wavelength] = angularValues;
            }
            angularValues[angularY * _parameters.PixelsX + angularX] += power;
            if (!_angularHits.TryGetValue(wavelength, out var angularHitValues))
            {
                angularHitValues = new long[_parameters.PixelsX * _parameters.PixelsY];
                _angularHits[wavelength] = angularHitValues;
            }
            angularHitValues[angularY * _parameters.PixelsX + angularX]++;
        }

        public NonSequentialDetectorFrame ToFrame()
        {
            var values = _pixels.ToDictionary(
                item => item.Key,
                item => (IReadOnlyList<double>)item.Value);
            return new NonSequentialDetectorFrame(
                _item.Id,
                _item.Name,
                _parameters.PixelsX,
                _parameters.PixelsY,
                values,
                values.Values.Sum(pixels => pixels.Sum()),
                _hits.ToDictionary(item => item.Key, item => (IReadOnlyList<long>)item.Value),
                _angularPixels.ToDictionary(item => item.Key, item => (IReadOnlyList<double>)item.Value),
                _angularHits.ToDictionary(item => item.Key, item => (IReadOnlyList<long>)item.Value));
        }
    }
}

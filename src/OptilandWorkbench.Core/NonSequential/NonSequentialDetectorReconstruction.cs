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
        var wavelengths = document.Wavelengths
            .Select((item, index) => (item.Nanometers, Number: index + 1))
            .OrderBy(item => item.Nanometers)
            .ToArray();
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
                    || segment.ObjectId is not Guid detectorId
                    || !detectors.TryGetValue(detectorId, out var detector))
                {
                    continue;
                }

                var local = document.ToLocalPoint(detectorId, segment.End);
                var localDirection = document.ToLocalDirection(detectorId, segment.OutgoingDirection);
                if (!detector.Accepts(localDirection))
                {
                    continue;
                }
                if (!(recordedHits ??= new HashSet<DetectorHitWithinBranch>()).Add(
                        new DetectorHitWithinBranch(
                            detectorId,
                            segment.FaceNumber,
                            BitConverter.DoubleToInt64Bits(segment.CumulativePathLength))))
                {
                    continue;
                }

                var wavelength = branch.WavelengthNanometers > 0
                    ? branch.WavelengthNanometers
                    : segment.WavelengthNanometers;
                detector.Add(
                    local,
                    localDirection,
                    ResolveWavelengthNumber(wavelengths, wavelength),
                    segment.Intensity);
            }
        }
        return detectors.Values.Select(item => item.ToFrame()).ToArray();
    }

    private static int ResolveWavelengthNumber(
        (double Nanometers, int Number)[] wavelengths,
        double value)
    {
        if (wavelengths.Length == 0 || !double.IsFinite(value))
        {
            return 1;
        }

        var low = 0;
        var high = wavelengths.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var comparison = wavelengths[middle].Nanometers.CompareTo(value);
            if (comparison == 0)
            {
                return wavelengths[middle].Number;
            }

            if (comparison < 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        var insertion = low;
        var nearest = insertion switch
        {
            0 => wavelengths[0],
            _ when insertion == wavelengths.Length => wavelengths[^1],
            _ => Math.Abs(wavelengths[insertion - 1].Nanometers - value)
                <= Math.Abs(wavelengths[insertion].Nanometers - value)
                ? wavelengths[insertion - 1]
                : wavelengths[insertion]
        };
        return Math.Abs(nearest.Nanometers - value) <= 1e-9 ? nearest.Number : 1;
    }

    private readonly record struct DetectorHitWithinBranch(
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

        public bool Accepts(Backend.Vector3D direction) =>
            !_parameters.FrontOnly || direction.Z > 0;

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
            var values = Snapshot(_pixels);
            return new NonSequentialDetectorFrame(
                _item.Id,
                _item.Name,
                _parameters.PixelsX,
                _parameters.PixelsY,
                values,
                values.Values.Sum(pixels => pixels.Sum()),
                Snapshot(_hits),
                Snapshot(_angularPixels),
                Snapshot(_angularHits));
        }

        private static IReadOnlyDictionary<int, IReadOnlyList<double>> Snapshot(Dictionary<int, double[]> source) =>
            new System.Collections.ObjectModel.ReadOnlyDictionary<int, IReadOnlyList<double>>(
                source.ToDictionary(item => item.Key, item => Snapshot(item.Value)));

        private static IReadOnlyDictionary<int, IReadOnlyList<long>> Snapshot(Dictionary<int, long[]> source) =>
            new System.Collections.ObjectModel.ReadOnlyDictionary<int, IReadOnlyList<long>>(
                source.ToDictionary(item => item.Key, item => Snapshot(item.Value)));

        private static IReadOnlyList<double> Snapshot(double[] values) => Array.AsReadOnly(values.ToArray());

        private static IReadOnlyList<long> Snapshot(long[] values) => Array.AsReadOnly(values.ToArray());
    }
}

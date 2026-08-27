namespace OptilandWorkbench.Core.NonSequential;

public static class NonSequentialDetectorReconstruction
{
    public static IReadOnlyList<NonSequentialDetectorFrame> Reconstruct(
        NonSequentialDocument document,
        IEnumerable<NonSequentialRayBranch> branches)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(branches);
        var detectors = document.Objects
            .Where(item => item.Enabled && item.Kind == NonSequentialObjectKind.DetectorRectangle)
            .ToDictionary(item => item.Id, item => new Accumulator(item));
        foreach (var branch in branches)
        {
            if (branch.TerminationReason != Raytrace.NonSequentialTerminationReason.DetectorHit
                || branch.Segments.LastOrDefault() is not { ObjectId: Guid detectorId } segment
                || !detectors.TryGetValue(detectorId, out var detector))
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
        return detectors.Values.Select(item => item.ToFrame(document)).ToArray();
    }

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

        public NonSequentialDetectorFrame ToFrame(NonSequentialDocument document)
        {
            var values = new Dictionary<int, IReadOnlyList<double>>();
            for (var wavelength = 1; wavelength <= document.Wavelengths.Count; wavelength++)
            {
                values[wavelength] = _pixels.TryGetValue(wavelength, out var pixels)
                    ? pixels.ToArray()
                    : new double[_parameters.PixelsX * _parameters.PixelsY];
            }
            return new NonSequentialDetectorFrame(
                _item.Id,
                _item.Name,
                _parameters.PixelsX,
                _parameters.PixelsY,
                values,
                values.Values.Sum(pixels => pixels.Sum()),
                document.Wavelengths.Select((_, index) => index + 1).ToDictionary(
                    wavelength => wavelength,
                    wavelength => (IReadOnlyList<long>)(_hits.TryGetValue(wavelength, out var pixels)
                        ? pixels.ToArray()
                        : new long[_parameters.PixelsX * _parameters.PixelsY])),
                document.Wavelengths.Select((_, index) => index + 1).ToDictionary(
                    wavelength => wavelength,
                    wavelength => (IReadOnlyList<double>)(_angularPixels.TryGetValue(wavelength, out var pixels)
                        ? pixels.ToArray()
                        : new double[_parameters.PixelsX * _parameters.PixelsY])),
                document.Wavelengths.Select((_, index) => index + 1).ToDictionary(
                    wavelength => wavelength,
                    wavelength => (IReadOnlyList<long>)(_angularHits.TryGetValue(wavelength, out var pixels)
                        ? pixels.ToArray()
                        : new long[_parameters.PixelsX * _parameters.PixelsY])));
        }
    }
}

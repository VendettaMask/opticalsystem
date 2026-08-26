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
            var wavelength = branch.WavelengthNanometers > 0
                ? branch.WavelengthNanometers
                : segment.WavelengthNanometers;
            var wavelengthNumber = document.Wavelengths.ToList().FindIndex(item =>
                Math.Abs(item.Nanometers - wavelength) <= 1e-9) + 1;
            detector.Add(local, Math.Max(1, wavelengthNumber), segment.Intensity);
        }
        return detectors.Values.Select(item => item.ToFrame(document)).ToArray();
    }

    private sealed class Accumulator
    {
        private readonly NonSequentialObjectDefinition _item;
        private readonly DetectorRectangleParameters _parameters;
        private readonly Dictionary<int, double[]> _pixels = new();

        public Accumulator(NonSequentialObjectDefinition item)
        {
            _item = item;
            _parameters = (DetectorRectangleParameters)item.Parameters;
        }

        public void Add(Backend.Vector3D point, int wavelength, double power)
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
                values.Values.Sum(pixels => pixels.Sum()));
        }
    }
}

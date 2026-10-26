using OpticalSystemDesign.Core.Backend;

namespace OpticalSystemDesign.Core.NonSequential;

// Shared spatial/angular accumulation. Callers supply their established normalized
// direction, or null to retain a spatial hit without an angular sample.
internal sealed class NonSequentialDetectorAccumulator
{
    private readonly NonSequentialObjectDefinition _item;
    private readonly DetectorRectangleParameters _parameters;
    private readonly Dictionary<int, double[]> _pixels = new();
    private readonly Dictionary<int, long[]> _hits = new();
    private readonly Dictionary<int, double[]> _angularPixels = new();
    private readonly Dictionary<int, long[]> _angularHits = new();

    public NonSequentialDetectorAccumulator(NonSequentialObjectDefinition item)
    {
        _item = item;
        _parameters = (DetectorRectangleParameters)item.Parameters;
    }

    public bool Accepts(Vector3D direction) =>
        !_parameters.FrontOnly || direction.Z > 0;

    public void Add(Vector3D point, Vector3D? normalizedDirection, int wavelength, double power)
    {
        if (!NonSequentialDetectorPixels.TryIndex(point.X, _parameters.WidthMillimeters, _parameters.PixelsX, out var x)
            || !NonSequentialDetectorPixels.TryIndex(point.Y, _parameters.HeightMillimeters, _parameters.PixelsY, out var y)) return;
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

        if (normalizedDirection is not Vector3D normalized) return;
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

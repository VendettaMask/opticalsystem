using OpticalSystemDesign.Core.Backend;

namespace OpticalSystemDesign.Core.NonSequential;

// Shared spatial/angular accumulation. Callers supply their established normalized
// direction, or null to retain a spatial hit without an angular sample.
internal sealed partial class NonSequentialDetectorAccumulator
{
    private readonly NonSequentialObjectDefinition _item;
    private readonly DetectorRectangleParameters _parameters;
    private readonly Dictionary<int, double[]> _pixels = new();
    private readonly Dictionary<int, long[]> _hits = new();
    private readonly Dictionary<int, double[]> _angularPixels = new();
    private readonly Dictionary<int, long[]> _angularHits = new();
    private readonly bool _collectMoments;
    private readonly Dictionary<int, NonSequentialMomentAccumulator> _moments = [];

    public NonSequentialDetectorAccumulator(NonSequentialObjectDefinition item, bool collectMoments = false)
    {
        _item = item;
        _parameters = (DetectorRectangleParameters)item.Parameters;
        _collectMoments = collectMoments;
    }

    public bool Accepts(Vector3D direction) =>
        !_parameters.FrontOnly || direction.Z > 0;

    public void Add(Vector3D point, Vector3D? normalizedDirection, int wavelength, double power,
        NonSequentialIncidentField? field = null, NonSequentialDocument? document = null, Vector3D? worldDirection = null,
        double wavelengthNanometers = 0, bool coherent = false)
    {
        if (_parameters.PolarizationComponent != 0)
        {
            if (coherent && _parameters.PolarizationComponent >= 4)
                throw new NotSupportedException("忽略相位的探测器分量筛选尚不支持相干复场累积。");
            if (field is null || document is null) throw new InvalidOperationException("探测器的偏振分量筛选需要启用偏振追迹。");
            var component = _parameters.PolarizationComponent;
            var selected = component switch { 1 or 7 => 1, 2 or 8 => 2, 3 or 9 => 4, 4 => 3, 5 => 5, 6 => 6, _ => throw new InvalidDataException("Invalid detector polarization component.") };
            NonSequentialComplexVector Project(NonSequentialComplexVector e)
            {
                Vector3D Mask(Vector3D v)
                {
                    v = document.ToLocalDirection(_item.Id, v);
                    return document.ToWorldDirection(_item.Id, new((selected & 1) != 0 ? v.X : 0, (selected & 2) != 0 ? v.Y : 0, (selected & 4) != 0 ? v.Z : 0));
                }
                return new(Mask(e.Real), Mask(e.Imaginary));
            }
            var state = field.State with { First = Project(field.State.First), Second = Project(field.State.Second) };
            var fraction = Math.Clamp(state.Power / field.State.Power, 0, 1);
            power *= fraction;
            if (fraction > 0)
            {
                var norm = 1 / Math.Sqrt(state.Power);
                state = state with { First = NonSequentialComplexVector.From(state.First.Field * norm), Second = NonSequentialComplexVector.From(state.Second.Field * norm) };
                field = new(state, field.PowerWatts * fraction, field.RefractiveIndex);
            }
            else field = null;
        }
        if (!NonSequentialDetectorPixels.TryIndex(point.X, _parameters.WidthMillimeters, _parameters.PixelsX, out var x)
            || !NonSequentialDetectorPixels.TryIndex(point.Y, _parameters.HeightMillimeters, _parameters.PixelsY, out var y)) return;
        if (!_pixels.TryGetValue(wavelength, out var values))
        {
            values = new double[_parameters.PixelsX * _parameters.PixelsY];
            _pixels[wavelength] = values;
        }
        values[y * _parameters.PixelsX + x] += power;
        if (_collectMoments)
        {
            if (!_moments.TryGetValue(wavelength, out var moments)) _moments.Add(wavelength, moments = new());
            moments.Add(point.X, point.Y, power);
        }
        if (field is not null)
            AddField(x, y, point, normalizedDirection, wavelength, power, field,
                document ?? throw new ArgumentNullException(nameof(document)), wavelengthNanometers, coherent);
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
            Snapshot(_angularHits))
        {
            StokesByWavelength = StokesSnapshot(),
            CoherentByWavelength = CoherentSnapshot(),
            MomentsByWavelength = _collectMoments ? new System.Collections.ObjectModel.ReadOnlyDictionary<int, NonSequentialDetectorMoments>(
                _moments.ToDictionary(p => p.Key, p => p.Value.Snapshot())) : null
        };
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

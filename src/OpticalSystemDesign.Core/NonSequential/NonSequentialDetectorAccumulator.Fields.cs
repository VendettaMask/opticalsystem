using System.Collections.ObjectModel;
using System.Numerics;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Rays;

namespace OpticalSystemDesign.Core.NonSequential;

internal sealed partial class NonSequentialDetectorAccumulator
{
    private readonly Dictionary<int, double[][]> _stokes = [];
    private readonly Dictionary<(int Wave, string Group), FieldBins> _fields = [];
    private sealed class FieldBins(int count)
    {
        internal readonly ComplexElectricField[] First = new ComplexElectricField[count], Second = new ComplexElectricField[count];
        internal readonly double[] Amplitudes = new double[count], Power = new double[count];
        internal readonly long[] Hits = new long[count];
    }

    private void AddField(int x, int y, Vector3D point, Vector3D? direction, int wave, double power,
        NonSequentialIncidentField incident, NonSequentialDocument document, double wavelength, bool coherent)
    {
        var count = _parameters.PixelsX * _parameters.PixelsY; var index = y * _parameters.PixelsX + x;
        var a = incident.State.First.Field; var b = incident.State.Second.Field;
        var ux = document.ToWorldDirection(_item.Id, new(1, 0, 0));
        var uy = document.ToWorldDirection(_item.Id, new(0, 1, 0));
        var uz = document.ToWorldDirection(_item.Id, new(0, 0, 1));
        a = new(a.Dot(ux), a.Dot(uy), a.Dot(uz)); b = new(b.Dot(ux), b.Dot(uy), b.Dot(uz));
        if (!_stokes.TryGetValue(wave, out var s)) _stokes.Add(wave, s = Enumerable.Range(0, 5).Select(_ => new double[count]).ToArray());
        foreach (var mode in new[] { a, b })
        {
            var xx = mode.X.Magnitude * mode.X.Magnitude; var yy = mode.Y.Magnitude * mode.Y.Magnitude;
            var cross = mode.X * Complex.Conjugate(mode.Y);
            s[0][index] += power * (xx + yy); s[1][index] += power * (xx - yy);
            s[2][index] += power * 2 * cross.Real; s[3][index] += power * 2 * cross.Imaginary;
            s[4][index] += power * mode.Z.Magnitude * mode.Z.Magnitude;
        }
        if (!coherent) return;
        if (direction is not { } k || !double.IsFinite(wavelength) || wavelength <= 0) throw new InvalidDataException("Coherent hit needs direction and wavelength.");
        var centre = new Vector3D(((x + .5) / _parameters.PixelsX - .5) * _parameters.WidthMillimeters,
            ((y + .5) / _parameters.PixelsY - .5) * _parameters.HeightMillimeters, 0);
        var delta = centre - point;
        var opticalOffset = incident.RefractiveIndex * (k.X * delta.X + k.Y * delta.Y + k.Z * delta.Z);
        var phase = Complex.FromPolarCoordinates(Math.Sqrt(power), 2 * Math.PI * Math.IEEERemainder(opticalOffset, wavelength * 1e-6) / (wavelength * 1e-6));
        if (!_fields.TryGetValue((wave, incident.State.CoherenceGroup), out var bins))
            _fields.Add((wave, incident.State.CoherenceGroup), bins = new(count));
        bins.First[index] += a * phase; bins.Second[index] += b * phase;
        bins.Amplitudes[index] += Math.Sqrt(power); bins.Power[index] += power;
        bins.Hits[index]++;
    }

    private IReadOnlyDictionary<int, NonSequentialStokesFrame>? StokesSnapshot() => _stokes.Count == 0 ? null
        : new ReadOnlyDictionary<int, NonSequentialStokesFrame>(_stokes.ToDictionary(p => p.Key,
            p => new NonSequentialStokesFrame(Snapshot(p.Value[0]), Snapshot(p.Value[1]), Snapshot(p.Value[2]),
                Snapshot(p.Value[3]), Snapshot(p.Value[4]))));

    private IReadOnlyDictionary<int, NonSequentialCoherentFrame>? CoherentSnapshot()
    {
        if (_fields.Count == 0) return null;
        var result = new Dictionary<int, NonSequentialCoherentFrame>();
        foreach (var wave in _fields.GroupBy(p => p.Key.Wave))
        {
            var count = _parameters.PixelsX * _parameters.PixelsY; var raw = new double[count]; var power = new double[count];
            var modes = new List<NonSequentialCoherentModeFrame>();
            foreach (var pair in wave)
            {
                var bins = pair.Value; var values = Enumerable.Range(0, count).Select(i => bins.First[i].SquaredNorm + bins.Second[i].SquaredNorm).ToArray();
                modes.Add(new(pair.Key.Group, Array.AsReadOnly(bins.First.Select(NonSequentialComplexVector.From).ToArray()),
                    Array.AsReadOnly(bins.Second.Select(NonSequentialComplexVector.From).ToArray()))
                    { SummedRayAmplitudes = Snapshot(bins.Amplitudes), HitCounts = Snapshot(bins.Hits) });
                var total = values.Sum(); var incident = bins.Power.Sum();
                for (var i = 0; i < count; i++)
                {
                    raw[i] += values[i];
                    // Explicit ray-detector conventions. Group/wavelength energy is never mixed during normalization.
                    power[i] += _parameters.NormalizeCoherentPower && count > 1
                        ? total > 0 ? values[i] * incident / total : 0
                        : bins.Amplitudes[i] > 0 ? values[i] / (bins.Amplitudes[i] * bins.Amplitudes[i]) * bins.Power[i] : 0;
                }
            }
            result.Add(wave.Key, new(Array.AsReadOnly(raw), Array.AsReadOnly(power), _parameters.NormalizeCoherentPower && count > 1)
                { Modes = Array.AsReadOnly(modes.ToArray()) });
        }
        return new ReadOnlyDictionary<int, NonSequentialCoherentFrame>(result);
    }
}

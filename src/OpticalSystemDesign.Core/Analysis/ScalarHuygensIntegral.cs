using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis;

/// <summary>A scalar field cell on a physical homogeneous output plane. Amplitude is sqrt(W/mm²),
/// optical path and edge vectors are mm. Cells belong to ONE mutually coherent source point/band.</summary>
public readonly record struct ScalarHuygensCell(Vector3D Position, Vector3D Direction,
    Vector3D EdgeU, Vector3D EdgeV, double AreaSquareMillimeters, double Amplitude, double OpticalPathMillimeters);

/// <summary>Absolute finite-cell scalar Huygens propagation. No peak/window normalization.
/// Small-angle scalar irradiance convention, homogeneous n=1 output; not vector Maxwell transport.</summary>
public static class ScalarHuygensIntegral
{
    public static double IrradianceWattsPerSquareMillimeter(IReadOnlyList<ScalarHuygensCell> cells,
        Vector3D point, double wavelengthNanometers)
    {
        ArgumentNullException.ThrowIfNull(cells); ComputationCancellation.ThrowIfCancellationRequested();
        if (!double.IsFinite(wavelengthNanometers) || wavelengthNanometers is < 200 or > 20000)
            throw new ArgumentOutOfRangeException(nameof(wavelengthNanometers));
        if (cells.Count == 0) return 0;
        return Evaluate(new RawCells(cells), point, wavelengthNanometers);
    }

    public static PreparedField Prepare(IReadOnlyList<ScalarHuygensCell> cells, double wavelengthNanometers)
        => new(cells, wavelengthNanometers);

    private readonly record struct Cell(ScalarHuygensCell Source, double AmplitudeArea, double PathDifference, double AbsoluteDirectionZ);
    private interface ICells
    {
        int Count { get; }
        Vector3D ReferencePosition { get; }
        Cell Get(int index);
    }
    private readonly struct RawCells(IReadOnlyList<ScalarHuygensCell> cells) : ICells
    {
        public int Count => cells.Count;
        public Vector3D ReferencePosition => cells[0].Position;
        public Cell Get(int index) => MakeCell(cells[index], cells[0].OpticalPathMillimeters);
    }
    private static Cell MakeCell(ScalarHuygensCell cell, double referencePath)
    {
        if (cell.Amplitude < 0 || !double.IsFinite(cell.Amplitude) || !double.IsFinite(cell.OpticalPathMillimeters)
            || !double.IsFinite(cell.AreaSquareMillimeters) || cell.AreaSquareMillimeters <= 0)
            throw new ArgumentException("Invalid scalar pupil cell or output point.");
        return new(cell, cell.Amplitude * cell.AreaSquareMillimeters,
            cell.OpticalPathMillimeters - referencePath, Math.Abs(cell.Direction.Z));
    }
    public sealed class PreparedField
    {
        private readonly Cell[] _cells;
        private readonly double _wavelength;
        internal PreparedField(IReadOnlyList<ScalarHuygensCell> cells, double wavelength)
        {
            ArgumentNullException.ThrowIfNull(cells); ComputationCancellation.ThrowIfCancellationRequested();
            if (!double.IsFinite(wavelength) || wavelength is < 200 or > 20000) throw new ArgumentOutOfRangeException(nameof(wavelength));
            _wavelength = wavelength;
            _cells = cells.Select(c => MakeCell(c, cells[0].OpticalPathMillimeters)).ToArray();
        }
        public double IrradianceWattsPerSquareMillimeter(Vector3D point)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            return _cells.Length == 0 ? 0 : Evaluate(new PreparedCells(_cells), point, _wavelength);
        }
    }
    private readonly struct PreparedCells(Cell[] cells) : ICells
    {
        public int Count => cells.Length;
        public Vector3D ReferencePosition => cells[0].Source.Position;
        public Cell Get(int index) => cells[index];
    }

    private static double Evaluate<TCells>(TCells cells, Vector3D point, double wavelengthNanometers) where TCells : struct, ICells
    {
        var referenceDistance = (point - cells.ReferencePosition).Length;
        var k = 2 * Math.PI / (wavelengthNanometers * 1e-6);
        double real = 0, imaginary = 0;
        for (var index = 0; index < cells.Count; index++)
        {
            var prepared = cells.Get(index); var cell = prepared.Source;
            var delta = point - cell.Position; var distance = delta.Length;
            if (!double.IsFinite(distance) || distance <= 0 || delta.Z <= 0)
                throw new ArgumentException("Invalid scalar pupil cell or output point.");
            var direction = delta / distance;
            var gradient = cell.Direction - direction;
            var footprint = Sinc(k * Dot(gradient, cell.EdgeU) / 2) * Sinc(k * Dot(gradient, cell.EdgeV) / 2);
            var obliquity = (prepared.AbsoluteDirectionZ + direction.Z) / 2;
            var amplitude = prepared.AmplitudeArea * obliquity * footprint
                / (wavelengthNanometers * 1e-6 * distance);
            var phase = k * (prepared.PathDifference + (distance - referenceDistance));
            var (sin, cos) = Math.SinCos(phase); real += amplitude * cos; imaginary += amplitude * sin;
        }
        var value = real * real + imaginary * imaginary;
        if (!double.IsFinite(value)) throw new InvalidOperationException("Nonfinite scalar diffraction irradiance.");
        return value;
    }

    private static double Sinc(double x) => Math.Abs(x) < 1e-8 ? 1 - x * x / 6 : Math.Sin(x) / x;
    private static double Dot(Vector3D a, Vector3D b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
}

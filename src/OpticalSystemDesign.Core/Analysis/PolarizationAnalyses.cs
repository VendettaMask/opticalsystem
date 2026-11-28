using System.Globalization;
using System.Numerics;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis;

public enum PolarizationAnalysisKind { RayTrace, PupilMap, Transmission, PhaseAberration, TransmissionFan }

public sealed record PolarizationAnalysisSettings(int FieldNumber = 1, int WavelengthNumber = 1,
    int SurfaceIndex = -1, int GridSize = 17, int FanPoints = 65, double Px = 0, double Py = 0,
    bool UseRayAiming = true, SystemPolarization? Input = null);

/// <summary>Sequential polarization analyses using the formal flux-normalized complex-field tracer.</summary>
public sealed class PolarizationAnalysis(Optic optic, PolarizationAnalysisKind kind,
    PolarizationAnalysisSettings? settings = null) : BaseAnalysis(optic)
{
    private readonly PolarizationAnalysisSettings _settings = settings ?? new();
    public override string Name => Key(kind);
    public static string Key(PolarizationAnalysisKind value) => value switch
    {
        PolarizationAnalysisKind.RayTrace => "Polarization Ray Trace",
        PolarizationAnalysisKind.PupilMap => "Polarization Pupil Map",
        PolarizationAnalysisKind.Transmission => "Polarization Transmission",
        PolarizationAnalysisKind.PhaseAberration => "Polarization Phase Aberration",
        PolarizationAnalysisKind.TransmissionFan => "Transmission Fan",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
    private SystemPolarization Input => _settings.Input ?? Optic.Polarization;
    private bool Unpolarized => kind is PolarizationAnalysisKind.Transmission or PolarizationAnalysisKind.TransmissionFan && Input.Unpolarized;
    private int Target => _settings.SurfaceIndex < 0 ? Optic.SurfaceGroup.Items.Count - 1 : _settings.SurfaceIndex;
    private sealed record PupilSample(double Px, double Py, double LaunchPower, ComplexElectricField Field, double Power, bool Valid)
    {
        public double Transmission => LaunchPower > 0 ? Power / LaunchPower : 0;
    }

    public override AnalysisData GenerateData()
    {
        if (Optic.Wavelengths.Count == 0 || Optic.SurfaceGroup.Items.Count < 2)
            return AnalysisData.Unavailable(Name, "No optical data");
        if (!Unpolarized) Input.Validate();
        if (_settings.GridSize is < 3 or > 129 || _settings.FanPoints is < 3 or > 513
            || Target < 1 || Target >= Optic.SurfaceGroup.Items.Count
            || !double.IsFinite(_settings.Px) || !double.IsFinite(_settings.Py)
            || _settings.Px * _settings.Px + _settings.Py * _settings.Py > 1 + 1e-12)
            throw new ArgumentOutOfRangeException(nameof(settings));
        var fields = SpotAnalysisEngine.DefinedFields(Optic);
        var fieldNumbers = Numbers(_settings.FieldNumber, fields.Count);
        var waveNumbers = Numbers(_settings.WavelengthNumber, Optic.Wavelengths.Count);
        if ((long)fieldNumbers.Length * waveNumbers.Length * _settings.GridSize * _settings.GridSize > 1_000_000)
            throw new ArgumentException("偏振分析采样总数不能超过 1000000。");
        if (kind == PolarizationAnalysisKind.Transmission) return Transmission(fieldNumbers, waveNumbers, fields);
        if (fieldNumbers.Length != 1 || waveNumbers.Length != 1) throw new ArgumentException("请选择单一视场和波长。");
        var f = fieldNumbers[0]; var w = waveNumbers[0];
        return kind switch
        {
            PolarizationAnalysisKind.RayTrace => RayTrace(f, w, fields[f - 1]),
            PolarizationAnalysisKind.PupilMap => PupilMap(f, w, fields[f - 1]),
            _ => Fans(f, w, fields[f - 1])
        };
    }
    private static int[] Numbers(int requested, int count)
    {
        if (requested < 0 || requested > count) throw new ArgumentOutOfRangeException(nameof(requested));
        return requested == 0 ? Enumerable.Range(1, count).ToArray() : [requested];
    }
    private PupilSample Trace((double Hx, double Hy) field, Wavelength wavelength, double px, double py, int target, bool propagation = false)
    {
        ComputationCancellation.ThrowIfCancellationRequested();
        var bundle = Optic.SequentialRayTracer.RayGenerator.GenerateGeneric(field.Hx, field.Hy, px, py,
            wavelength.Micrometers, _settings.UseRayAiming);
        var source = bundle.Rays.Single();
        // The shared geometrical trace distinguishes clipping from unsupported field transport.
        // Clipped launched rays remain in the pupil integral's denominator with zero output.
        var history = Optic.SequentialRayTracer.Trace(bundle).RayHistories.Single();
        if (history.Count <= target || history.Take(target + 1).Any(s => s.Vignetted) || history[target].Intensity <= 0)
            return new(px, py, source.Intensity, default, 0, false);
        var input = Unpolarized ? new JonesInputState(1, 0, ReferenceAxis: Input.ReferenceAxis) : Input.Input;
        var actual = Optic.SequentialRayTracer.TracePolarized(source, input, target,
            inputCoordinates: Optic.SurfaceGroup.Items[0].CoordinateSystem,
            timeConvention: HarmonicTimeConvention.Positive, includePropagationPhase: propagation,
            useRealInterfaceIndicesForBulkAbsorption: true);
        return new(px, py, source.Intensity, actual.LocalField,
            Unpolarized ? actual.UnpolarizedIntensity : actual.PolarizedIntensity, true);
    }
    private List<PupilSample> Grid((double Hx, double Hy) field, Wavelength wavelength)
    {
        var result = new List<PupilSample>();
        for (var y = 0; y < _settings.GridSize; y++)
            for (var x = 0; x < _settings.GridSize; x++)
            {
                var px = -1 + 2.0 * x / (_settings.GridSize - 1); var py = -1 + 2.0 * y / (_settings.GridSize - 1);
                if (px * px + py * py <= 1 + 1e-12) result.Add(Trace(field, wavelength, px, py, Target));
            }
        return result;
    }
    private double PupilAreaFactor((double Hx, double Hy) field)
    {
        var transform = Optic.SequentialRayTracer.RayGenerator.GetPupilVignetting(field.Hx, field.Hy);
        return Math.Abs((1 - transform.CompressionX) * (1 - transform.CompressionY));
    }
    private Dictionary<string, object> Metadata(int field, int wave) => new()
    {
        ["FieldNumber"] = field,
        ["WavelengthNumber"] = wave,
        ["WavelengthMicrometers"] = wave > 0 ? Optic.Wavelengths[wave - 1].Micrometers : 0,
        ["SurfaceIndex"] = Target,
        ["GridSize"] = _settings.GridSize,
        ["Unpolarized"] = Unpolarized,
        ["Jx"] = Input.Jx,
        ["Jy"] = Input.Jy,
        ["XPhaseDegrees"] = Input.XPhaseDegrees,
        ["YPhaseDegrees"] = Input.YPhaseDegrees,
        ["UseRayAiming"] = _settings.UseRayAiming,
        ["FieldCoordinates"] = "Surface local XYZ",
        ["TimeConvention"] = "exp(+i omega t)",
        ["IncludesBulkAbsorption"] = true,
        ["IncludesPropagationPhase"] = kind == PolarizationAnalysisKind.RayTrace,
        ["InterfaceIndexModel"] = "Real-index homogeneous interfaces with Beer-Lambert bulk absorption; coherent films use their complex amplitudes"
    };
    private AnalysisData RayTrace(int f, int w, (double Hx, double Hy) field)
    {
        var wavelength = Optic.Wavelengths[w - 1]; var rows = new List<IReadOnlyList<string>>();
        for (var surface = 1; surface <= Target; surface++)
        {
            var sample = Trace(field, wavelength, _settings.Px, _settings.Py, surface, propagation: true);
            rows.Add([Optic.SurfaceGroup.Items[surface].Number.ToString(CultureInfo.InvariantCulture),
                .. new[] { sample.Field.X, sample.Field.Y, sample.Field.Z }.SelectMany(c => new[] { N(c.Real, sample.Valid), N(c.Imaginary, sample.Valid) }),
                N(sample.Transmission), N(PhaseDifference(sample.Field), sample.Valid), N(Orientation(sample.Field), sample.Valid)]);
            if (!sample.Valid) break;
        }
        return Data(Metadata(f, w), new(["表面", "Re(Ex)", "Im(Ex)", "Re(Ey)", "Im(Ey)", "Re(Ez)", "Im(Ez)", "透过率", "Δφ (°)", "椭圆方位 (°)"], rows));
    }
    private AnalysisData Transmission(int[] fieldNumbers, int[] waveNumbers, IReadOnlyList<(double Hx, double Hy)> fields)
    {
        var rows = new List<IReadOnlyList<string>>();
        foreach (var f in fieldNumbers)
            foreach (var w in waveNumbers)
            {
                var field = fields[f - 1]; var wave = Optic.Wavelengths[w - 1]; var grid = Grid(field, wave);
                var launch = grid.Sum(s => s.LaunchPower);
                var integrated = launch > 0 ? PupilAreaFactor(field) * grid.Sum(s => s.Power) / launch : 0;
                double previous = 1;
                for (var surface = 1; surface <= Target; surface++)
                {
                    var chief = Trace(field, wave, 0, 0, surface);
                    rows.Add([f.ToString(CultureInfo.InvariantCulture), N(wave.Nanometers),
                        Optic.SurfaceGroup.Items[surface].Number.ToString(CultureInfo.InvariantCulture),
                        surface == Target ? N(100 * integrated) : "—", N(previous > 0 ? 100 * chief.Transmission / previous : double.NaN),
                        N(100 * chief.Transmission), grid.Count(s => s.Valid).ToString(CultureInfo.InvariantCulture)]);
                    previous = chief.Transmission;
                }
            }
        var values = Metadata(_settings.FieldNumber, _settings.WavelengthNumber);
        values["FieldCount"] = fieldNumbers.Length; values["WavelengthCount"] = waveNumbers.Length;
        values["Integration"] = "Uniform circular entrance-pupil grid; all launched power retained in denominator";
        return Data(values, new(["视场", "波长 (nm)", "表面", "光瞳积分 (%)", "主光线相对 (%)", "主光线累计 (%)", "有效光线数"], rows));
    }
    private AnalysisData PupilMap(int f, int w, (double Hx, double Hy) field)
    {
        var grid = Grid(field, Optic.Wavelengths[w - 1]); var series = new List<AnalysisSeries>
        {
            new("Px", "Py", Enumerable.Range(0, 65).Select(i => new AnalysisPoint(Math.Cos(2 * Math.PI * i / 64),
                Math.Sin(2 * Math.PI * i / 64))).ToArray(), LineStyle: AnalysisLineStyle.Dashed, ColorIndex: 1, Opacity: .5,
                XQuantity: AnalysisAxisQuantity.PupilCoordinate, XUnit: AnalysisAxisUnit.Dimensionless,
                YQuantity: AnalysisAxisQuantity.PupilCoordinate, YUnit: AnalysisAxisUnit.Dimensionless)
        };
        var rows = new List<IReadOnlyList<string>>(); var size = .65 / (_settings.GridSize - 1);
        foreach (var s in grid)
        {
            rows.Add([N(s.Px), N(s.Py), N(s.Field.X.Magnitude, s.Valid), N(s.Field.Y.Magnitude, s.Valid),
                N(s.Field.Z.Magnitude, s.Valid), N(s.Transmission), N(PhaseDifference(s.Field), s.Valid), N(Orientation(s.Field), s.Valid)]);
            if (!s.Valid || s.LaunchPower <= 0) continue;
            var points = Enumerable.Range(0, 33).Select(i =>
            {
                var phase = Complex.FromPolarCoordinates(1, 2 * Math.PI * i / 32);
                return new AnalysisPoint(s.Px + size * (s.Field.X * phase).Real / Math.Sqrt(s.LaunchPower),
                    s.Py + size * (s.Field.Y * phase).Real / Math.Sqrt(s.LaunchPower));
            }).ToArray();
            series.Add(new("Px", "Py", points, ColorIndex: 0, XQuantity: AnalysisAxisQuantity.PupilCoordinate,
                XUnit: AnalysisAxisUnit.Dimensionless, YQuantity: AnalysisAxisQuantity.PupilCoordinate, YUnit: AnalysisAxisUnit.Dimensionless));
        }
        if (!grid.Any(s => s.Valid)) return AnalysisData.Unavailable(Name, "没有到达所选表面的有效光线。");
        var values = Metadata(f, w); values["ValidRayCount"] = grid.Count(s => s.Valid);
        values["PupilTransmission"] = grid.Sum(s => s.LaunchPower) > 0 ? PupilAreaFactor(field) * grid.Sum(s => s.Power) / grid.Sum(s => s.LaunchPower) : 0;
        values["EllipseProjection"] = "Local Ex/Ey; power includes Ex/Ey/Ez";
        return Data(values, new(["Px", "Py", "|Ex|", "|Ey|", "|Ez|", "透过率", "Δφ (°)", "椭圆方位 (°)"], rows)) with
        { SeriesList = series, PlotOptions = new(EqualAspect: true, XMinimum: -1.1, XMaximum: 1.1, YMinimum: -1.1, YMaximum: 1.1, GridOpacity: 0) };
    }
    private AnalysisData Fans(int f, int w, (double Hx, double Hy) field)
    {
        var phase = kind == PolarizationAnalysisKind.PhaseAberration; var wavelength = Optic.Wavelengths[w - 1];
        var chief = Trace(field, wavelength, 0, 0, Target); var panes = new List<AnalysisPlotPane>();
        var rows = new List<IReadOnlyList<string>>();
        for (var axis = 0; axis < 2; axis++)
        {
            var samples = Enumerable.Range(0, _settings.FanPoints).Select(i =>
            {
                var p = -1 + 2.0 * i / (_settings.FanPoints - 1);
                return Trace(field, wavelength, axis == 0 ? p : 0, axis == 1 ? p : 0, Target);
            }).ToArray();
            var series = new List<AnalysisSeries>();
            var refs = new[] { chief.Field.X, chief.Field.Y };
            for (var component = 0; component < (phase ? 2 : 1); component++)
            {
                var c = component;
                var points = samples.Select(s => new AnalysisPoint(axis == 0 ? s.Px : s.Py,
                    phase ? RelativePhase(c == 0 ? s.Field.X : s.Field.Y, refs[c], s.Valid) : 100 * s.Transmission)).ToArray();
                series.Add(new(axis == 0 ? "Px" : "Py", phase ? "相位像差 (waves)" : "透过率 (%)", points,
                    Name: phase ? c == 0 ? "Ex" : "Ey" : "透过率", ColorIndex: c,
                    XQuantity: AnalysisAxisQuantity.PupilCoordinate, XUnit: AnalysisAxisUnit.Dimensionless,
                    YQuantity: phase ? AnalysisAxisQuantity.WavefrontError : AnalysisAxisQuantity.Transmission,
                    YUnit: phase ? AnalysisAxisUnit.Wave : AnalysisAxisUnit.Percent));
            }
            foreach (var s in samples) rows.Add([axis == 0 ? "X" : "Y", N(axis == 0 ? s.Px : s.Py), N(100 * s.Transmission),
                N(RelativePhase(s.Field.X, chief.Field.X, s.Valid)), N(RelativePhase(s.Field.Y, chief.Field.Y, s.Valid))]);
            panes.Add(new(axis == 0 ? "X 瞳截面" : "Y 瞳截面", series,
                new(Title: axis == 0 ? "X 瞳截面" : "Y 瞳截面", XMinimum: -1, XMaximum: 1, YMinimum: phase ? null : 0, ShowLegend: true)));
        }
        var values = Metadata(f, w); values["FanPoints"] = _settings.FanPoints;
        values["IncludesPropagationPhase"] = false; values["PhaseReference"] = "Chief ray, principal branch (-0.5, 0.5] waves; zero fields undefined";
        return Data(values, new(["瞳截面", "瞳坐标", "透过率 (%)", "Ex (waves)", "Ey (waves)"], rows)) with { PlotPanes = panes, PlotPaneColumns = 2 };
    }
    private static double RelativePhase(Complex value, Complex reference, bool valid) => valid && value.Magnitude > 1e-14 && reference.Magnitude > 1e-14
        ? (value * Complex.Conjugate(reference)).Phase / (2 * Math.PI) : double.NaN;
    private static double PhaseDifference(ComplexElectricField e) => e.X.Magnitude > 1e-14 && e.Y.Magnitude > 1e-14
        ? PolarizationMetrics.PhaseDifference(e) * 180 / Math.PI : double.NaN;
    private static double Orientation(ComplexElectricField e) => .5 * Math.Atan2(2 * (e.X * Complex.Conjugate(e.Y)).Real,
        e.X.Magnitude * e.X.Magnitude - e.Y.Magnitude * e.Y.Magnitude) * 180 / Math.PI;
    private AnalysisData Data(Dictionary<string, object> values, AnalysisTable table) => new(Name, values, Table: table,
        ReportText: string.Join(Environment.NewLine, new[] { Name, string.Join("\t", table.Columns) }.Concat(table.Rows.Select(r => string.Join("\t", r)))));
    private static string N(double value, bool valid = true) => valid && double.IsFinite(value) ? value.ToString("G12", CultureInfo.InvariantCulture) : "—";
}

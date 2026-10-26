using System.Globalization;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis;

public sealed record InterferogramSettings(
    int Sampling = 64,
    int SurfaceNumber = -1,
    double ScaleFactor = 1,
    string ShowAs = "灰度",
    int WavelengthNumber = 0,
    int FieldNumber = 1,
    double XTilt = 0,
    double YTilt = 0,
    int Beam1 = -1,
    int Beam2 = 0,
    bool RefBeamToVertex = false,
    bool UseExitPupilShape = true,
    bool ConsiderPathLengthDifference = false,
    string ContourFormat = "",
    double Sx = 0,
    double Sy = 0,
    double Sr = 1);

/// <summary>Zemax-style equal-intensity, perfectly overlapping pupil interferogram.</summary>
public sealed class InterferogramAnalysis : BaseAnalysis
{
    private readonly InterferogramSettings _settings;
    private readonly IReadOnlyList<Optic> _configurations;
    private readonly Dictionary<Optic, Optic> _prepared = new();
    public InterferogramAnalysis(Optic optic, InterferogramSettings? settings = null,
        IReadOnlyList<Optic>? configurations = null) : base(optic)
    {
        _settings = settings ?? new();
        _configurations = configurations ?? [optic];
        if (_settings.Sampling is < 8 or > 512) throw new ArgumentOutOfRangeException(nameof(settings), "Sampling must be 8–512.");
        if (!double.IsFinite(_settings.ScaleFactor) || _settings.ScaleFactor <= 0
            || !double.IsFinite(_settings.XTilt) || !double.IsFinite(_settings.YTilt)
            || !double.IsFinite(_settings.Sx) || !double.IsFinite(_settings.Sy)
            || !double.IsFinite(_settings.Sr) || _settings.Sr <= 0 || _settings.Sr > 1)
            throw new ArgumentException("Interferogram scale, tilts and subaperture must be finite; scale and radius must be positive.", nameof(settings));
        if (_settings.RefBeamToVertex && _settings.ScaleFactor != 1)
            throw new ArgumentException("Ref Beam To Vertex requires Scale Factor = 1.", nameof(settings));
        if (_settings.ShowAs is not ("灰度" or "伪彩色" or "等高线"))
            throw new ArgumentException("Unknown interferogram display mode.", nameof(settings));
    }
    public override string Name => "Interferogram";

    public override AnalysisData GenerateData()
    {
        var s = _settings;
        _prepared.Clear();
        var beam1 = Resolve(s.Beam1); var beam2 = Resolve(s.Beam2);
        var shapeSource = beam1 ?? beam2 ?? Optic;
        var wave = SelectWavelength(shapeSource);
        if (wave is null || shapeSource.Fields.Count == 0)
            return AnalysisData.Unavailable(Name, "No wavelength or field is defined.");
        if (s.FieldNumber < 1 || s.FieldNumber > shapeSource.Fields.Count)
            throw new ArgumentOutOfRangeException(nameof(s.FieldNumber));
        var field = SpotAnalysisEngine.DefinedFields(shapeSource)[s.FieldNumber - 1];
        var grid = new PupilGridSpecification(s.Sampling, false, true, 1);
        var coordinates = new List<(double X, double Y)> { (0, 0) };
        for (var row = 0; row < s.Sampling; row++)
            for (var column = 0; column < s.Sampling; column++)
            {
                var x = grid.Coordinate(column); var y = grid.Coordinate(row);
                if (x * x + y * y > 1) continue;
                x = s.Sx + s.Sr * x; y = s.Sy + s.Sr * y;
                if (x * x + y * y <= 1 + 1e-12) coordinates.Add((x, y));
            }
        if (coordinates.Count <= 1) return AnalysisData.Unavailable(Name, "Subaperture does not overlap the entrance pupil.");
        var a = Trace(beam1, coordinates); var b = ReferenceEquals(beam1, beam2) ? a : Trace(beam2, coordinates);
        var path1 = ChiefPathToImage(beam1); var path2 = ReferenceEquals(beam1, beam2) ? path1 : ChiefPathToImage(beam2);
        // The shared Core OPD is reference-path minus traced-path; chief OPL
        // offsets therefore enter with the opposite sign to the pupil OPD.
        var pathPhase = s.ConsiderPathLengthDifference ? (path2 - path1) / (wave.Micrometers * 1e-3) : 0;
        var points = new List<AnalysisPoint>(); var phasePoints = new List<AnalysisPoint>();
        var opdValues = new List<double>();
        for (var i = 1; i < coordinates.Count; i++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            if (a is not null && (a.Samples[i].Intensity <= 0 || !double.IsFinite(a.Samples[i].OpdWaves))
                || b is not null && (b.Samples[i].Intensity <= 0 || !double.IsFinite(b.Samples[i].OpdWaves))) continue;
            var (x, y) = coordinates[i];
            var opd = BeamOpd(a, beam1, i) - BeamOpd(b, beam2, i);
            var phase = s.ScaleFactor * (opd + pathPhase) + (s.XTilt * x + s.YTilt * y) / 2;
            if (!double.IsFinite(phase)) throw new InvalidOperationException("Interferogram phase is not finite.");
            points.Add(new(x, y, Value: NormalizedIntensity(phase)));
            phasePoints.Add(new(x, y, Value: phase)); opdValues.Add(opd);
        }
        if (points.Count == 0) return AnalysisData.Unavailable(Name, "The two beams have no valid common pupil samples.");
        var shapeOptic = PrepareSurface(shapeSource, wave);
        var shapeField = SpotAnalysisEngine.DefinedFields(shapeOptic)[s.FieldNumber - 1];
        var axes = s.UseExitPupilShape && !shapeOptic.ImageSpaceAfocal
            ? DiffractionEngine.WorkingFNumbers(shapeOptic, shapeField, wave, shapeOptic.RayAimingEnabled) : (Tangential: 1d, Sagittal: 1d);
        var scaleX = Math.Min(axes.Tangential, axes.Sagittal) / axes.Sagittal;
        var scaleY = Math.Min(axes.Tangential, axes.Sagittal) / axes.Tangential;
        var series = new AnalysisSeries("Pupil X", "Pupil Y", points, AnalysisSeriesKind.Heatmap,
            ValueLabel: "Normalized fringe intensity", ColorMap: s.ShowAs == "伪彩色" ? AnalysisColorMap.Jet : AnalysisColorMap.InverseGray,
            ValueMinimum: 0, ValueMaximum: 1,
            XQuantity: AnalysisAxisQuantity.PupilCoordinate, XUnit: AnalysisAxisUnit.Dimensionless,
            YQuantity: AnalysisAxisQuantity.PupilCoordinate, YUnit: AnalysisAxisUnit.Dimensionless,
            ValueQuantity: AnalysisAxisQuantity.Intensity, ValueUnit: AnalysisAxisUnit.Dimensionless);
        var values = new Dictionary<string, object>
        {
            ["Sampling"] = $"{s.Sampling} x {s.Sampling}",
            ["ScaleFactor"] = s.ScaleFactor,
            ["XTilt"] = s.XTilt,
            ["YTilt"] = s.YTilt,
            ["Beam1"] = s.Beam1,
            ["Beam2"] = s.Beam2,
            ["RefBeamToVertex"] = s.RefBeamToVertex,
            ["UseExitPupilShape"] = s.UseExitPupilShape,
            ["ConsiderPathLengthDifference"] = s.ConsiderPathLengthDifference,
            ["PathLengthDifferenceMillimeters"] = s.ConsiderPathLengthDifference ? path1 - path2 : "Not evaluated",
            ["DisplayAs"] = s.ShowAs,
            ["ContourFormat"] = s.ContourFormat,
            ["ContourLevels"] = ContourLevels(s.ContourFormat),
            ["PupilSx"] = s.Sx,
            ["PupilSy"] = s.Sy,
            ["PupilSr"] = s.Sr,
            ["WavelengthMicrometers"] = wave.Micrometers,
            ["FieldHx"] = field.Hx,
            ["FieldHy"] = field.Hy,
            ["FieldX"] = shapeSource.Fields[s.FieldNumber - 1].X,
            ["FieldY"] = shapeSource.Fields[s.FieldNumber - 1].Y,
            ["FieldIsAngular"] = shapeSource.FieldDefinition == FieldDefinitionKind.Angle,
            ["SurfaceNumber"] = s.SurfaceNumber < 0 ? shapeSource.SurfaceGroup.Items[^1].Number : s.SurfaceNumber,
            ["PeakToValleyWaves"] = opdValues.Max() - opdValues.Min(),
            ["RmsWaves"] = Math.Sqrt(opdValues.Average(v => v * v)),
            ["PupilDiameterMillimeters"] = Math.Abs(shapeOptic.Paraxial.EstimateExitPupilDiameter(wave.Micrometers)),
            ["UseRayAiming"] = shapeSource.RayAimingEnabled,
            ["ValidSampleCount"] = points.Count,
            ["InterferogramModel"] = "Equal-intensity beams; coincident normalized pupils; center phase zero unless chief path difference included"
        };
        var tableRows = points.Select((p, i) => (IReadOnlyList<string>)new[]
        {
            p.X.ToString("R", CultureInfo.InvariantCulture), p.Y.ToString("R", CultureInfo.InvariantCulture),
            opdValues[i].ToString("R", CultureInfo.InvariantCulture), phasePoints[i].Value!.Value.ToString("R", CultureInfo.InvariantCulture),
            p.Value!.Value.ToString("R", CultureInfo.InvariantCulture)
        }).ToArray();
        return new AnalysisData(Name, values, series, [series], new AnalysisPlotOptions(
            Title: "Interferogram", EqualAspect: true, DefaultSquareViewport: true,
            XMinimum: s.Sx - s.Sr, XMaximum: s.Sx + s.Sr, YMinimum: s.Sy - s.Sr, YMaximum: s.Sy + s.Sr,
            SpatialDisplayScaleX: scaleX, SpatialDisplayScaleY: scaleY),
            Table: new AnalysisTable(["Pupil X", "Pupil Y", "OPD difference (waves)", "Phase (fringes)", "Normalized intensity"], tableRows),
            ReportText: $"Interferogram; Scale={s.ScaleFactor:R}; X-Tilt={s.XTilt:R}; Y-Tilt={s.YTilt:R}; Beam1={s.Beam1}; Beam2={s.Beam2}; equal-intensity, overlapping pupils.");
    }

    public static double NormalizedIntensity(double phaseFringes)
    {
        if (!double.IsFinite(phaseFringes)) throw new ArgumentOutOfRangeException(nameof(phaseFringes));
        // Captured Zemax data has unity at zero phase; its grey plot maps unity
        // to black (a dark central fringe). Keep physical values and palette separate.
        return Math.Clamp((1 + Math.Cos(2 * Math.PI * Math.IEEERemainder(phaseFringes, 1))) / 2, 0, 1);
    }
    public static double[] ContourLevels(string format)
    {
        var defaults = Enumerable.Range(1, 9).Select(i => i / 10d).ToArray();
        if (string.IsNullOrWhiteSpace(format)) return defaults;
        var levels = format.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(v => double.Parse(v, NumberStyles.Float, CultureInfo.InvariantCulture)).ToArray();
        if (levels.Any(v => !double.IsFinite(v))) throw new ArgumentException("Contour levels must be finite.");
        if (levels.Length == 1)
        {
            var step = levels[0];
            if (step <= 0) throw new ArgumentException("Contour spacing must be positive.");
            if (1 / step > 100) return defaults;
            return Enumerable.Range(1, Math.Max(0, (int)Math.Ceiling(1 / step) - 1)).Select(i => i * step).ToArray();
        }
        return levels.Length > 100 ? defaults : levels.Distinct().Order().Where(v => v >= 0 && v <= 1).ToArray();
    }
    private Optic? Resolve(int number) => number switch
    {
        0 => null,
        -1 => Optic,
        > 0 when number <= _configurations.Count => _configurations[number - 1],
        _ => throw new ArgumentOutOfRangeException(nameof(number), "Beam configuration is unavailable.")
    };
    private Wavelength? SelectWavelength(Optic optic) => _settings.WavelengthNumber == 0
        ? optic.Wavelengths.FirstOrDefault(w => w.IsPrimary) ?? optic.Wavelengths.FirstOrDefault()
        : optic.Wavelengths.ElementAtOrDefault(_settings.WavelengthNumber - 1)
            ?? throw new ArgumentOutOfRangeException(nameof(_settings.WavelengthNumber));
    private WavefrontResult? Trace(Optic? optic, IReadOnlyList<(double X, double Y)> coordinates)
    {
        if (optic is null) return null;
        var wavelength = SelectWavelength(optic) ?? throw new InvalidOperationException("Beam has no wavelength.");
        var actual = SelectWavelength(Resolve(_settings.Beam1) ?? Resolve(_settings.Beam2) ?? Optic)!;
        if (wavelength.Micrometers != actual.Micrometers) throw new InvalidOperationException("The two beam wavelengths must agree.");
        if (_settings.FieldNumber > optic.Fields.Count) throw new ArgumentOutOfRangeException(nameof(_settings.FieldNumber));
        var prepared = PrepareSurface(optic, wavelength);
        var field = SpotAnalysisEngine.DefinedFields(prepared)[_settings.FieldNumber - 1];
        return WavefrontEngine.GenerateChiefRaySamples(prepared, field, wavelength, coordinates, aimAtStop: prepared.RayAimingEnabled);
    }
    private double ChiefPathToImage(Optic? optic)
    {
        if (optic is null || !_settings.ConsiderPathLengthDifference) return 0;
        var wavelength = SelectWavelength(optic)!;
        var field = SpotAnalysisEngine.DefinedFields(optic)[_settings.FieldNumber - 1];
        return WavefrontEngine.GenerateChiefRaySamples(optic, field, wavelength, [(0d, 0d)], aimAtStop: optic.RayAimingEnabled)
            .ChiefOpticalPathLength ?? throw new InvalidOperationException("Physical chief optical path is unavailable.");
    }
    private double BeamOpd(WavefrontResult? wavefront, Optic? optic, int index)
    {
        if (wavefront is null) return 0;
        var value = wavefront.Samples[index].OpdWaves;
        if (!_settings.RefBeamToVertex) return value;
        var sphere = wavefront.ReferenceSphere ?? throw new NotSupportedException("Vertex reference requires a focal reference sphere.");
        var reference = wavefront.Samples[0]; var sample = wavefront.Samples[index];
        var vertex = PrepareSurface(optic!, SelectWavelength(optic!)!).SurfaceGroup.Items[^1].CoordinateSystem.Origin;
        var delta = new Vector3D(sphere.CenterX - vertex.X, sphere.CenterY - vertex.Y, sphere.CenterZ - vertex.Z);
        var pupilDelta = new Vector3D(sample.PupilX - reference.PupilX, sample.PupilY - reference.PupilY, sample.PupilZ - reference.PupilZ);
        return value + wavefront.ImageRefractiveIndex * (delta.X * pupilDelta.X + delta.Y * pupilDelta.Y + delta.Z * pupilDelta.Z)
            / (sphere.Radius * SelectWavelength(optic!)!.Micrometers * 1e-3);
    }
    private Optic PrepareSurface(Optic source, Wavelength wavelength)
    {
        var number = _settings.SurfaceNumber;
        if (number < 0 || number == source.SurfaceGroup.Items[^1].Number) return source;
        if (!_prepared.TryGetValue(source, out var prepared))
            _prepared[source] = prepared = InterferogramSurfacePreparation.Create(source, number, wavelength);
        return prepared;
    }
}
